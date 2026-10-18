using Warcrown.Sim.Buildings;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Core;

/// <summary>
/// Forts: neutral strongholds between the castles (see <see cref="FortRules"/>). Each stands from the start, neutral
/// (faction -1, at war with everyone) and held by a guard that defends its post. Whoever brings a fort down takes it
/// over instead of razing it, and it pays its income to its new owner; taken from a player, it changes hands again, and
/// when its owner is eliminated it goes back to being neutral.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>The map's forts, each with its guard round its gate; none if the rules have no fort building.</summary>
    private void PlaceForts(IReadOnlyList<Cell> anchors)
    {
        if (Rules.Fort is not { } type)
            return;
        foreach (var anchor in anchors)
        {
            if (Building.FootprintOf(anchor, type.Size).Any(cell => !Map.IsWalkable(cell) || DepositAt(cell) != null))
                throw new ArgumentException($"A fort does not fit at {anchor}.");
            var fort = AddBuilding(type, NeutralFactionId, anchor, constructionTicks: 0, invested: 0);
            foreach (var group in Rules.Forts.Guard)
            {
                var unitType = Rules.Units.Get(group.UnitTypeId);
                foreach (var slot in Formation.ComputeSlots(Map, SpawnPointOf(fort), group.Count, unitType.Radius))
                    SpawnUnit(unitType.Id, NeutralFactionId, slot);
            }
        }
    }

    /// <summary>The owner of what belongs to nobody: forts not yet taken and their guards.</summary>
    public const int NeutralFactionId = -1;

    /// <summary>
    /// A fort brought down by a faction still in the match becomes theirs, with a share of its health, rather than
    /// falling; true if it did. Whoever was attacking it on its new owner's side lets go (an ally's building is no target).
    /// </summary>
    private bool TryCapture(Building fort)
    {
        if (!fort.Type.IsCapturable || GetFaction(fort.KilledByFactionId) is not { IsEliminated: false } captor)
            return false;
        int from = fort.FactionId;
        fort.FactionId = captor.Id;
        fort.Health = Math.Max(1, fort.MaxHealth * Rules.Forts.CaptureHealthPercent / 100);
        fort.KilledByFactionId = -1;
        fort.Target = null;
        fort.RallyPoint = null;
        _events.Add(new FortCaptured(captor.Id, from, fort.Id, fort.Position));
        return true;
    }

    /// <summary>The forts of an eliminated faction go back to nobody (its other buildings collapse).</summary>
    private void ReleaseFort(Building fort)
    {
        int from = fort.FactionId;
        fort.FactionId = NeutralFactionId;
        fort.Target = null;
        fort.RallyPoint = null;
        _events.Add(new FortCaptured(NeutralFactionId, from, fort.Id, fort.Position));
    }
}
