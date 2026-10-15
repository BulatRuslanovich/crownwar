using System.Numerics;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;

namespace Lordsio.Sim.AI;

/// <summary>
/// The Defense mode: the horde near the shared citadel is as much a threat as near the bot's own outpost, and a
/// fallen outpost is built again near where it stood (the army meanwhile guards the citadel).
/// </summary>
public sealed partial class BotBrain
{
    /// <summary>Horde units this close (tiles) to the citadel's walls are met by every player's army.</summary>
    private const float CitadelGuardRadius = 14f;

    /// <summary>Where the bot's outpost stood last, to build the next one there.</summary>
    private Vector2? _home;

    /// <summary>Defense: adds the enemies at the citadel to <paramref name="threats"/>.</summary>
    private void AddCitadelThreats(List<Unit> threats)
    {
        if (_sim.Defense is not { } defense || _sim.GetBuilding(defense.CitadelId) is not { } citadel)
            return;
        foreach (var u in _sim.Units)
        {
            if (u.IsAlive && _sim.AreEnemies(FactionId, u.FactionId) && citadel.SurfaceDistance(u.Position) <= CitadelGuardRadius
                && !threats.Contains(u))
                threats.Add(u);
        }
    }

    /// <summary>
    /// Defense, without an outpost: the army meets whatever comes at the citadel (or at what is left of the base), and
    /// a new outpost is ordered near the old one as soon as it can be paid for.
    /// </summary>
    private void PlayWithoutOutpost(Faction me)
    {
        if (_sim.Defense is not { } defense || _home is not { } home)
            return;
        _own.Clear();
        foreach (var building in _sim.Buildings)
            if (building.FactionId == FactionId)
                _own.Add(building);
        var army = _sim.Units.Where(u => u.FactionId == FactionId).ToList();
        var threats = FindThreats();
        if (army.Count > 0)
        {
            var citadel = _sim.GetBuilding(defense.CitadelId);
            var guard = citadel?.Position ?? home;
            if (threats.Count > 0)
                Send(army, threats.OrderBy(t => Vector2.Distance(t.Position, guard)).First().Position, MoveMode.AttackMove);
            else
                Send(army, home, MoveMode.AttackMove);
        }

        var castle = _sim.Rules.BuildingTypes.First(t => t.IsCapital);
        if (_sim.MayBuildOutpost(FactionId, castle) && me.Treasury.CanAfford(_sim.BuildCost(castle)))
            TryPlaceNear(castle, home, 0, 8, sendBuilder: true);
    }
}
