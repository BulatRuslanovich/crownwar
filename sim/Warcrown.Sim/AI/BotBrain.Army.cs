using System.Numerics;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;

namespace Warcrown.Sim.AI;

/// <summary>The bot's army: gather, expand, defend, attack and retreat, and which target to march on.</summary>
public sealed partial class BotBrain
{
    private void CommandArmy(Building capital, List<Unit> army, int armyValue, List<Unit> threats)
    {
        if (army.Count == 0)
        {
            Mode = BotMode.Gather;
            return;
        }

        // A raid on an outlying building by a stronger force is not worth the army: let it fall and hold the castle,
        // whose arrows join the fight, instead of feeding soldiers in one by one.
        if (threats.Count > 0 && !threats.Any(t => capital.SurfaceDistance(t.Position) <= ThreatRadius)
            && threats.Sum(t => t.Type.Cost) > armyValue)
        {
            Mode = BotMode.Gather;
            Send(army, RallyPoint(capital), MoveMode.AttackMove);
            return;
        }

        if (threats.Count > 0)
        {
            Mode = BotMode.Defend;
            // Meet the intruders closest to home first.
            var nearest = threats.OrderBy(t => Vector2.Distance(t.Position, capital.Position)).First();
            if (!nearest.Type.IsFlying)
            {
                Send(army, nearest.Position, MoveMode.AttackMove);
                return;
            }
            // Only arrows reach a flyer: the bowmen go for it, the rest meet what walks, or wait at home.
            var shooters = army
                .Where(u => u.Type.HitsAir && u.Target == null
                            && (u.Destination is not { } d || Vector2.Distance(d, nearest.Position) > 3f))
                .Select(u => u.Id).ToList();
            if (shooters.Count > 0)
                Issue(new MoveCommand(FactionId, shooters, nearest.Position, MoveMode.AttackMove));
            var walkers = army.Where(u => !u.Type.HitsAir).ToList();
            var walking = threats.Where(t => !t.Type.IsFlying).OrderBy(t => Vector2.Distance(t.Position, capital.Position)).FirstOrDefault();
            if (walkers.Count > 0)
                Send(walkers, walking?.Position ?? RallyPoint(capital), MoveMode.AttackMove);
            return;
        }

        if (Mode == BotMode.Attack)
        {
            var target = _sim.GetBuilding(_attackTargetId);
            if (target == null || !_sim.AreEnemies(FactionId, target.FactionId))
            {
                Mode = BotMode.Gather; // destroyed (or a fort taken): pick the next target below
            }
            else if (armyValue < _launchValue * _profile.RetreatFraction)
            {
                Mode = BotMode.Retreat;
                Send(army, RallyPoint(capital), MoveMode.Move, force: true);
                return;
            }
            else
            {
                Send(army, target.Position, MoveMode.AttackMove);
                return;
            }
        }

        if (Mode == BotMode.Retreat)
        {
            var rally = RallyPoint(capital);
            if (army.Average(u => Vector2.Distance(u.Position, rally)) > RegroupDistance * 2)
                return; // still walking home
            Mode = BotMode.Gather;
        }

        if (armyValue >= _profile.AttackArmyValue && ChooseAttackTarget(capital, armyValue) is { } attackTarget)
        {
            Mode = BotMode.Attack;
            _attackTargetId = attackTarget.Id;
            _launchValue = armyValue;
            Send(army, attackTarget.Position, MoveMode.AttackMove, force: true);
            return;
        }

        if (_expansion is { } deposit)
        {
            Mode = BotMode.Expand;
            // Stand next to the ore (towards home), not on it, so the mine can be placed.
            var toHome = Vector2.Normalize(capital.Position - deposit.Center);
            Send(army, deposit.Center + toHome * 2.5f, MoveMode.AttackMove);
            return;
        }

        Mode = BotMode.Gather;
        Send(army, RallyPoint(capital), MoveMode.AttackMove);
    }

    /// <summary>
    /// Cheapest prize for the effort: distance plus how well it is defended. Castles only once
    /// the army is big enough for a siege; their fall wins the war. Forts (neutral or an enemy's) draw the army a little
    /// more than other buildings: brought down, they become the bot's own.
    /// </summary>
    private Building? ChooseAttackTarget(Building capital, int armyValue)
    {
        Building? best = null;
        float bestScore = float.MaxValue;
        foreach (var building in _sim.Buildings)
        {
            if (!_sim.AreEnemies(FactionId, building.FactionId))
                continue;
            if (building.Type.IsCapital && armyValue < _profile.CapitalAssaultValue)
                continue;
            int defence = DefenceValue(building.Position, building.FactionId);
            if (defence * _profile.AttackAdvantage > armyValue)
                continue;
            float score = Vector2.Distance(building.Position, capital.Position) + defence / 25f;
            if (building.Type.IsMine && building.Deposit is { IsExhausted: false } deposit)
                score -= deposit.Reserve * _sim.Rules.MineRaidPercent / 100 / 20f; // a full deposit is worth the walk
            if (building.Type.IsCapital)
                score *= 0.6f; // the decisive target
            else if (building.Type.IsCapturable)
                score *= 0.7f; // taken, not razed: it pays for the rest of the match
            if (score < bestScore)
            {
                bestScore = score;
                best = building;
            }
        }
        return best;
    }

    private int DefenceValue(Vector2 point, int owner)
    {
        int value = 0;
        foreach (var u in _sim.Units)
        {
            if (u.FactionId == owner && Vector2.Distance(u.Position, point) <= DefenceRadius)
                value += u.Type.Cost;
        }
        // Shooting buildings (towers, the castle) count in proportion to their damage.
        foreach (var b in _sim.Buildings)
        {
            if (b.FactionId == owner && b.Type.Attack is { } attack && b.IsComplete && Vector2.Distance(b.Position, point) <= DefenceRadius)
                value += TowerDefenceValue * attack.Damage / 10;
        }
        return value;
    }

    /// <summary>
    /// Orders units towards <paramref name="goal"/>, skipping those already fighting or already on their
    /// way, so repeated decisions do not reset the march every second.
    /// </summary>
    private void Send(List<Unit> army, Vector2 goal, MoveMode mode, bool force = false)
    {
        bool newGoal = force || _armyGoal is not { } previous || Vector2.Distance(previous, goal) > 3f;
        _armyGoal = goal;
        var ids = army
            .Where(u => newGoal || (u.Target == null && u.Order == UnitOrder.None && Vector2.Distance(u.Position, goal) > RegroupDistance))
            .Where(u => mode == MoveMode.Move || u.Target == null || newGoal)
            .Select(u => u.Id).ToList();
        if (ids.Count > 0)
            Issue(new MoveCommand(FactionId, ids, goal, mode));
    }
}
