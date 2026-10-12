using System.Numerics;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;

namespace Lordsio.Sim.Territory;

/// <summary>
/// King-of-the-hill capture: a settlement is taken by standing in its zone with no defenders around.
/// <list type="bullet">
/// <item>Any unit of the owner (or a neutral settlement's garrison) in the zone blocks capture.</item>
/// <item>Two attacking factions in the zone contest it: progress freezes.</item>
/// <item>A lone attacker gains progress, faster with more units (up to 2× with 5); another faction's
/// earlier progress must be worn down first. Without attackers progress decays.</item>
/// </list>
/// Neutral troops never capture anything.
/// </summary>
internal sealed class CaptureSystem
{
    private const int MaxSpeedupUnits = 5;
    private const float SpeedupPerExtraUnit = 0.25f;

    private readonly Dictionary<int, int> _presence = new();

    /// <summary>Updates every settlement and returns those that changed owner this tick.</summary>
    public List<(Settlement Settlement, int PreviousOwner)> Update(
        IReadOnlyList<Settlement> settlements, IReadOnlyList<Unit> units, float captureRadius)
    {
        var captured = new List<(Settlement, int)>();
        float radiusSq = captureRadius * captureRadius;
        foreach (var settlement in settlements)
        {
            CountPresence(settlement, units, radiusSq);
            int attacker = Settlement.NeutralOwner;
            int attackerFactions = 0;
            foreach (var (factionId, _) in _presence)
            {
                if (factionId == Settlement.NeutralOwner || factionId == settlement.OwnerId)
                    continue;
                attacker = factionId;
                attackerFactions++;
            }
            bool defended = _presence.ContainsKey(settlement.OwnerId);
            float perTick = 1f / (settlement.Type.CaptureTime * Simulation.TicksPerSecond);

            settlement.IsContested = attackerFactions > 0 && (defended || attackerFactions > 1);
            if (attackerFactions == 0)
            {
                Decay(settlement, perTick);
                continue;
            }
            if (settlement.IsContested)
                continue;

            int count = Math.Min(_presence[attacker], MaxSpeedupUnits);
            float gain = perTick * (1f + SpeedupPerExtraUnit * (count - 1));
            if (settlement.CapturingFactionId != attacker && settlement.CaptureProgress > 0f)
            {
                // Wear down the previous capturer's progress before building our own.
                settlement.CaptureProgress = MathF.Max(0f, settlement.CaptureProgress - gain);
                if (settlement.CaptureProgress == 0f)
                    settlement.CapturingFactionId = attacker;
                continue;
            }

            settlement.CapturingFactionId = attacker;
            settlement.CaptureProgress += gain;
            if (settlement.CaptureProgress >= 1f)
            {
                int previous = settlement.OwnerId;
                settlement.OwnerId = attacker;
                settlement.ClearQueue();
                settlement.CaptureProgress = 0f;
                settlement.CapturingFactionId = Settlement.NeutralOwner;
                captured.Add((settlement, previous));
            }
        }
        return captured;
    }

    private void CountPresence(Settlement settlement, IReadOnlyList<Unit> units, float radiusSq)
    {
        _presence.Clear();
        Vector2 center = settlement.Cell.Center;
        foreach (var unit in units)
        {
            if (!unit.IsAlive || Vector2.DistanceSquared(unit.Position, center) > radiusSq)
                continue;
            _presence[unit.FactionId] = _presence.GetValueOrDefault(unit.FactionId) + 1;
        }
    }

    private static void Decay(Settlement settlement, float perTick)
    {
        if (settlement.CaptureProgress <= 0f)
            return;
        settlement.CaptureProgress = MathF.Max(0f, settlement.CaptureProgress - perTick);
        if (settlement.CaptureProgress == 0f)
            settlement.CapturingFactionId = Settlement.NeutralOwner;
    }
}
