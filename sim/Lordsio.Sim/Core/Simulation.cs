using System.Numerics;
using Lordsio.Sim.Combat;
using Lordsio.Sim.Territory;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Core;

/// <summary>
/// Authoritative game state advanced in fixed ticks. Knows nothing about rendering or input.
/// Players and bots change it only through <see cref="Issue"/>.
/// </summary>
public sealed class Simulation
{
    public const int TicksPerSecond = 20;
    public const float TickDuration = 1f / TicksPerSecond;

    /// <summary>How far from a blocked click we look for a reachable replacement point.</summary>
    private const int MaxGoalSearchRadius = 64;

    // Stuck handling: every second a marching unit must cover a fraction of its speed.
    private const int ProgressCheckTicks = TicksPerSecond;
    private const float MinProgressFraction = 0.25f;
    private const int RepathAfterChecks = 2;
    private const int GiveUpAfterChecks = 5;
    /// <summary>A unit stuck this close to its slot (crowded by neighbours) simply counts as arrived.</summary>
    private const float ArrivalSlack = 1.5f;

    /// <summary>Recruits appear this far below (in front of) their settlement.</summary>
    private static readonly Vector2 RecruitSpawnOffset = new(0f, 1.5f);

    private readonly List<Unit> _units = new();
    private readonly Dictionary<int, Unit> _unitsById = new();
    private readonly List<Faction> _factions = new();
    private readonly List<Settlement> _settlements = new();
    private readonly List<Projectile> _projectiles = new();
    private readonly Queue<Command> _pending = new();
    private readonly Pathfinder _pathfinder;
    private readonly CombatSystem _combat;
    private readonly CaptureSystem _capture = new();
    private List<SimEvent> _events = new();
    private int _nextUnitId = 1;

    public Simulation(MapDefinition map, GameRules rules)
    {
        Map = map.Grid;
        Rules = rules;
        _pathfinder = new Pathfinder(Map);
        _combat = new CombatSystem(Map, _pathfinder, _projectiles);

        var factionIds = map.SettlementSpawns.Select(s => s.OwnerId)
            .Concat(map.UnitSpawns.Select(u => u.FactionId))
            .Where(id => id >= 0).Distinct().Order();
        foreach (int id in factionIds)
            _factions.Add(new Faction(id, rules.StartingGold));

        foreach (var spawn in map.SettlementSpawns)
        {
            var settlement = new Settlement(_settlements.Count + 1, rules.GetSettlementType(spawn.TypeId), spawn.Cell, spawn.OwnerId);
            _settlements.Add(settlement);
            if (settlement.OwnerId == Settlement.NeutralOwner)
                SpawnGarrison(settlement);
        }
        Regions = new Regions(Map, _settlements);

        foreach (var spawn in map.UnitSpawns)
        {
            var type = Rules.Units.Get(spawn.TypeId);
            foreach (var slot in Formation.ComputeSlots(Map, spawn.Cell.Center, spawn.Count, type.Radius))
                SpawnUnit(spawn.TypeId, spawn.FactionId, slot);
        }

        foreach (var faction in _factions)
            faction.IsContender = OwnsCapital(faction.Id);
        UpdateEconomy(payIncome: false);
    }

    public TileGrid Map { get; }
    public GameRules Rules { get; }
    public Regions Regions { get; }
    public int Tick { get; private set; }
    public IReadOnlyList<Unit> Units => _units;
    public IReadOnlyList<Faction> Factions => _factions;
    public IReadOnlyList<Settlement> Settlements => _settlements;
    public IReadOnlyList<Projectile> Projectiles => _projectiles;

    /// <summary>Set once a single contender (or nobody) is left; the world no longer changes.</summary>
    public bool IsOver { get; private set; }

    /// <summary>The last faction standing, or -1 when the match ended without survivors or is still running.</summary>
    public int WinnerId { get; private set; } = -1;

    public Unit? GetUnit(int id) => _unitsById.GetValueOrDefault(id);
    public Faction? GetFaction(int id) => _factions.Find(f => f.Id == id);
    public Settlement? GetSettlement(int id) => _settlements.Find(s => s.Id == id);

    public void Issue(Command command) => _pending.Enqueue(command);

    /// <summary>Events produced since the last call, oldest first.</summary>
    public IReadOnlyList<SimEvent> TakeEvents()
    {
        if (_events.Count == 0)
            return Array.Empty<SimEvent>();
        var taken = _events;
        _events = new List<SimEvent>();
        return taken;
    }

    /// <summary>
    /// Whether a <see cref="RecruitCommand"/> would currently succeed. The same check the command runs,
    /// exposed so the UI can disable buttons and bots can plan without guessing the rules.
    /// </summary>
    public RecruitError CanRecruit(int factionId, int settlementId, string unitTypeId)
    {
        if (GetSettlement(settlementId) is not { } settlement)
            return RecruitError.UnknownSettlement;
        if (settlement.OwnerId != factionId || GetFaction(factionId) is not { IsEliminated: false } faction)
            return RecruitError.NotOwner;
        if (!settlement.Type.Recruits.Contains(unitTypeId))
            return RecruitError.CannotRecruitType;
        if (settlement.Queue.Count >= Settlement.MaxQueueLength)
            return RecruitError.QueueFull;
        if (PopulationOf(factionId) >= faction.ArmyCap)
            return RecruitError.ArmyCapReached;
        if (!faction.Treasury.CanAfford(Rules.Units.Get(unitTypeId).Cost))
            return RecruitError.NotEnoughGold;
        return RecruitError.None;
    }

    public void Step()
    {
        if (IsOver)
            return;
        Tick++;
        foreach (var unit in _units)
            unit.PreviousPosition = unit.Position;

        while (_pending.TryDequeue(out var command))
            Apply(command);

        UpdateEconomy(payIncome: true);
        AdvanceProduction();

        foreach (var unit in _units)
            _combat.UpdateBehaviour(unit, _units, Tick);
        foreach (var unit in _units)
            unit.Move(TickDuration);
        _combat.AdvanceProjectiles(TickDuration);

        UnitCollisions.Resolve(_units);
        foreach (var unit in _units)
        {
            KeepOnWalkableGround(unit);
            CheckProgress(unit);
            CompleteFinishedOrder(unit);
        }

        RemoveDeadUnits();
        foreach (var (settlement, previousOwner) in _capture.Update(_settlements, _units, Rules.CaptureRadius))
            _events.Add(new SettlementCaptured(settlement.OwnerId, settlement.Id, previousOwner));
        CheckEliminations();
    }

    public Unit SpawnUnit(string typeId, int factionId, Vector2 position)
    {
        if (!Map.IsWalkable(Cell.FromPosition(position)))
            throw new ArgumentException($"Cannot spawn a unit on a blocked tile at {position}.");
        var unit = new Unit(_nextUnitId++, Rules.Units.Get(typeId), factionId, position);
        _units.Add(unit);
        _unitsById.Add(unit.Id, unit);
        return unit;
    }

    private void SpawnGarrison(Settlement settlement)
    {
        foreach (var entry in settlement.Type.Garrison)
        {
            var type = Rules.Units.Get(entry.UnitTypeId);
            foreach (var slot in Formation.ComputeSlots(Map, settlement.Cell.Center, entry.Count, type.Radius))
                SpawnUnit(type.Id, Settlement.NeutralOwner, slot);
        }
    }

    private void Apply(Command command)
    {
        if (command.FactionId >= 0 && GetFaction(command.FactionId) is { IsEliminated: true })
            return; // the eliminated no longer give orders
        switch (command)
        {
            case MoveCommand move:
                ApplyMove(move);
                break;
            case AttackUnitCommand attack:
                ApplyAttack(attack);
                break;
            case RecruitCommand recruit:
                ApplyRecruit(recruit);
                break;
            default:
                throw new NotSupportedException($"Unhandled command type {command.GetType().Name}.");
        }
    }

    /// <summary>
    /// Units that died or belong to someone else are silently skipped: the issuer's view of the world
    /// may be a tick out of date, and foreign units are not theirs to command.
    /// </summary>
    private IEnumerable<Unit> OwnLivingUnits(int factionId, IReadOnlyList<int> unitIds)
    {
        foreach (int id in unitIds.Distinct())
        {
            if (GetUnit(id) is { IsAlive: true } unit && unit.FactionId == factionId)
                yield return unit;
        }
    }

    private void ApplyMove(MoveCommand move)
    {
        // Units are grouped by walkable region, since each region resolves a blocked target differently.
        var groups = new SortedDictionary<int, List<Unit>>();
        foreach (var unit in OwnLivingUnits(move.FactionId, move.UnitIds))
        {
            int component = Map.ComponentOf(Cell.FromPosition(unit.Position));
            if (!groups.TryGetValue(component, out var list))
                groups[component] = list = new List<Unit>();
            list.Add(unit);
        }

        var order = move.Mode == MoveMode.AttackMove ? UnitOrder.AttackMove : UnitOrder.Move;
        foreach (var (component, group) in groups)
            OrderGroupMove(group, component, move.Target, order);
    }

    private void ApplyAttack(AttackUnitCommand attack)
    {
        if (GetUnit(attack.TargetUnitId) is not { IsAlive: true } target || target.FactionId == attack.FactionId)
            return;
        foreach (var unit in OwnLivingUnits(attack.FactionId, attack.UnitIds))
        {
            unit.StartAttack(target);
            unit.LastOrder = MoveOrderResult.Accepted;
        }
    }

    private void OrderGroupMove(List<Unit> group, int component, Vector2 target, UnitOrder order)
    {
        Cell requested = Cell.FromPosition(target);
        Cell? goal = Map.FindNearestInComponent(requested, component, MaxGoalSearchRadius);
        if (goal == null)
        {
            foreach (var unit in group)
            {
                unit.CompleteOrder();
                unit.LastOrder = MoveOrderResult.Unreachable;
            }
            return;
        }

        bool adjusted = goal.Value != requested;
        Vector2 center = adjusted ? goal.Value.Center : target;
        float clearance = group.Max(u => u.Type.Radius);
        var slots = Formation.ComputeSlots(Map, center, group.Count, clearance);

        // Closest units take the central slots; the rest fill the outer rings.
        group.Sort((a, b) =>
        {
            int byDistance = Vector2.DistanceSquared(a.Position, center).CompareTo(Vector2.DistanceSquared(b.Position, center));
            return byDistance != 0 ? byDistance : a.Id.CompareTo(b.Id);
        });

        for (int i = 0; i < group.Count; i++)
        {
            var unit = group[i];
            var path = _pathfinder.FindPath(unit.Position, slots[i], unit.Type.Radius);
            if (path == null)
            {
                unit.CompleteOrder();
                unit.LastOrder = MoveOrderResult.Unreachable;
                continue;
            }
            unit.StartMove(order, slots[i], path);
            unit.LastOrder = adjusted ? MoveOrderResult.Adjusted : MoveOrderResult.Accepted;
        }
    }

    private void ApplyRecruit(RecruitCommand recruit)
    {
        var error = Rules.Units.Contains(recruit.UnitTypeId)
            ? CanRecruit(recruit.FactionId, recruit.SettlementId, recruit.UnitTypeId)
            : RecruitError.CannotRecruitType;
        if (error != RecruitError.None)
        {
            _events.Add(new RecruitRejected(recruit.FactionId, recruit.SettlementId, recruit.UnitTypeId, error));
            return;
        }

        var type = Rules.Units.Get(recruit.UnitTypeId);
        var faction = GetFaction(recruit.FactionId)!;
        faction.Treasury.TrySpend(type.Cost);
        faction.Population++;
        int ticks = Math.Max(1, (int)MathF.Round(type.BuildTime * TicksPerSecond));
        GetSettlement(recruit.SettlementId)!.Enqueue(new ProductionItem(type, ticks));
    }

    /// <summary>Recomputes income, army cap and population of every faction, optionally paying one tick of income.</summary>
    private void UpdateEconomy(bool payIncome)
    {
        foreach (var faction in _factions)
        {
            int income = 0, cap = 0;
            foreach (var settlement in _settlements)
            {
                if (settlement.OwnerId != faction.Id)
                    continue;
                income += settlement.Type.Income;
                cap += settlement.Type.ArmyCap;
            }
            faction.IncomePerSecond = income;
            faction.ArmyCap = cap;
            faction.Population = PopulationOf(faction.Id);
            if (payIncome)
                faction.Treasury.AccrueTick(income, TicksPerSecond);
        }
    }

    private int PopulationOf(int factionId)
    {
        int count = 0;
        foreach (var unit in _units)
            if (unit.FactionId == factionId)
                count++;
        foreach (var settlement in _settlements)
            if (settlement.OwnerId == factionId)
                count += settlement.Queue.Count;
        return count;
    }

    private void AdvanceProduction()
    {
        foreach (var settlement in _settlements)
        {
            if (settlement.AdvanceProduction() is not { } type)
                continue;
            var unit = SpawnUnit(type.Id, settlement.OwnerId, FreeSpotNear(settlement, type.Radius));
            _events.Add(new UnitRecruited(settlement.OwnerId, settlement.Id, unit.Id));
        }
    }

    /// <summary>First formation slot in front of the settlement that no unit is standing on.</summary>
    private Vector2 FreeSpotNear(Settlement settlement, float radius)
    {
        Vector2 front = settlement.Cell.Center + RecruitSpawnOffset;
        if (!Map.AreConnected(Cell.FromPosition(front), settlement.Cell))
            front = settlement.Cell.Center;

        var slots = Formation.ComputeSlots(Map, front, 32, radius);
        foreach (var slot in slots)
        {
            bool occupied = _units.Any(u => Vector2.DistanceSquared(u.Position, slot) < MathF.Pow(u.Type.Radius + radius, 2));
            if (!occupied)
                return slot;
        }
        return slots[0]; // crowded: separation will make room
    }

    /// <summary>Undoes pushes into walls, the map edge or another walkable region.</summary>
    private void KeepOnWalkableGround(Unit unit)
    {
        Vector2 pos = Map.PushOutOfWalls(unit.Position, unit.Type.Radius);
        var cell = Cell.FromPosition(pos);
        var previousCell = Cell.FromPosition(unit.PreviousPosition);
        if (!Map.IsWalkable(cell) || Map.ComponentOf(cell) != Map.ComponentOf(previousCell))
            pos = unit.PreviousPosition;
        unit.Position = pos;
    }

    /// <summary>A march whose path has run out (and that is not busy fighting) is done.</summary>
    private static void CompleteFinishedOrder(Unit unit)
    {
        if (unit.State != UnitState.Idle || unit.Target != null)
            return;
        if (unit.Order == UnitOrder.Move)
            unit.CompleteOrder();
        else if (unit.Order == UnitOrder.AttackMove && unit.Destination is { } d
                 && Vector2.Distance(unit.Position, d) <= ArrivalSlack)
            unit.CompleteOrder();
        // An attack-move that ended a chase away from its slot resumes next tick.
    }

    private void CheckProgress(Unit unit)
    {
        if (unit.State != UnitState.Moving || unit.Target != null || unit.Destination is not { } destination)
            return;
        if (++unit.TicksSinceProgressCheck < ProgressCheckTicks)
            return;

        float moved = Vector2.Distance(unit.Position, unit.ProgressAnchor);
        float expected = unit.Type.Speed * ProgressCheckTicks * TickDuration;
        unit.StuckChecks = moved < expected * MinProgressFraction ? unit.StuckChecks + 1 : 0;
        unit.TicksSinceProgressCheck = 0;
        unit.ProgressAnchor = unit.Position;
        if (unit.StuckChecks == 0)
            return;

        bool nearSlot = Vector2.Distance(unit.Position, destination) <= ArrivalSlack;
        if (unit.StuckChecks >= RepathAfterChecks && nearSlot)
        {
            unit.CompleteOrder(); // close enough: the slot is crowded, stand where we are
            return;
        }
        if (unit.StuckChecks >= GiveUpAfterChecks)
        {
            unit.CompleteOrder();
            unit.LastOrder = MoveOrderResult.Blocked;
            return;
        }
        if (unit.StuckChecks == RepathAfterChecks)
        {
            var path = _pathfinder.FindPath(unit.Position, destination, unit.Type.Radius);
            if (path == null)
            {
                unit.CompleteOrder();
                unit.LastOrder = MoveOrderResult.Blocked;
                return;
            }
            unit.ReplacePath(path);
        }
    }

    private void RemoveDeadUnits()
    {
        for (int i = _units.Count - 1; i >= 0; i--)
        {
            var unit = _units[i];
            if (unit.IsAlive)
                continue;
            _units.RemoveAt(i);
            _unitsById.Remove(unit.Id);
            _events.Add(new UnitDied(unit.FactionId, unit.Id, unit.KilledByFactionId));
        }
    }

    private bool OwnsCapital(int factionId) =>
        _settlements.Any(s => s.OwnerId == factionId && s.Type.IsCapital);

    /// <summary>Contenders without a capital are out; when one contender is left, it wins.</summary>
    private void CheckEliminations()
    {
        int contenders = 0;
        foreach (var faction in _factions)
        {
            if (!faction.IsContender || faction.IsEliminated)
                continue;
            if (OwnsCapital(faction.Id))
            {
                contenders++;
                continue;
            }
            Eliminate(faction);
        }

        bool hadContest = _factions.Count(f => f.IsContender) >= 2;
        if (hadContest && contenders <= 1)
        {
            IsOver = true;
            WinnerId = _factions.FirstOrDefault(f => f.IsContender && !f.IsEliminated)?.Id ?? -1;
            _events.Add(new MatchEnded(WinnerId));
        }
    }

    private void Eliminate(Faction faction)
    {
        faction.IsEliminated = true;
        foreach (var settlement in _settlements)
        {
            if (settlement.OwnerId != faction.Id)
                continue;
            settlement.OwnerId = Settlement.NeutralOwner;
            settlement.ClearQueue();
        }
        // The army disbands. Zero health so units still targeting them (and arrows in flight) let go.
        foreach (var unit in _units.Where(u => u.FactionId == faction.Id).ToList())
        {
            unit.Health = 0;
            _units.Remove(unit);
            _unitsById.Remove(unit.Id);
        }
        _events.Add(new FactionEliminated(faction.Id));
    }
}
