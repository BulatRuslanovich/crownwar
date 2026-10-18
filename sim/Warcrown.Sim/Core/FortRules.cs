using Warcrown.Sim.Units;

namespace Warcrown.Sim.Core;

/// <summary>
/// Forts (the "forts" section of the rules file): neutral strongholds between the castles, each held from the start by
/// a guard of <paramref name="Guard"/>. Whoever brings one down takes it over with <paramref name="CaptureHealthPercent"/>
/// percent of its health, and it pays them its income from then on; taken from a player, it changes hands again. The
/// fort building itself is the building type marked <c>"capturable"</c>; how many stand is the map's business.
/// </summary>
public sealed record FortRules(int CaptureHealthPercent, IReadOnlyList<StartingUnits> Guard)
{
    public static readonly FortRules Default = new(50, Array.Empty<StartingUnits>());

    internal void Validate(UnitCatalog units)
    {
        if (CaptureHealthPercent is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(CaptureHealthPercent), "A captured fort keeps a share (1–100%) of its health.");
        foreach (var guard in Guard)
        {
            if (!units.Contains(guard.UnitTypeId) || guard.Count <= 0)
                throw new ArgumentException($"A fort's guard names unknown unit type '{guard.UnitTypeId}' or no men.");
        }
    }
}
