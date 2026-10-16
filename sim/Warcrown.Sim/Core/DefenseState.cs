using System.Numerics;

namespace Warcrown.Sim.Core;

/// <summary>
/// The Defense mode as it stands: who holds the citadel, who the horde is, and where the waves are. Read by the
/// interface (wave counter, warnings, the end screen); only the simulation changes it.
/// </summary>
public sealed class DefenseState
{
    internal DefenseState(int players, int garrisonFactionId, int hordeFactionId, int citadelId, int firstWaveTick, int seed)
    {
        Players = players;
        GarrisonFactionId = garrisonFactionId;
        HordeFactionId = hordeFactionId;
        CitadelId = citadelId;
        NextWaveTick = firstWaveTick;
        Rng = new Random(seed ^ 0x5EED);
    }

    /// <summary>Player factions are 0 .. Players − 1.</summary>
    public int Players { get; }
    public int GarrisonFactionId { get; }
    public int HordeFactionId { get; }
    public int CitadelId { get; }

    /// <summary>The number of the latest wave that has come (0 before the first).</summary>
    public int WavesLaunched { get; internal set; }

    /// <summary>Waves wiped out to the last man: the team's score.</summary>
    public int WavesCleared { get; internal set; }

    /// <summary>The tick the next wave comes.</summary>
    public int NextWaveTick { get; internal set; }

    /// <summary>Where the next wave will come from, once it has been announced; empty before that.</summary>
    public IReadOnlyList<Vector2> IncomingFrom => Incoming;

    /// <summary>Horde units of each wave still alive, by wave number.</summary>
    public int AliveIn(int wave) => AliveInWave.GetValueOrDefault(wave);

    public bool IsPlayer(int factionId) => factionId >= 0 && factionId < Players;

    internal readonly List<Vector2> Incoming = new();
    internal readonly Random Rng;
    internal readonly SortedDictionary<int, int> AliveInWave = new();
    internal readonly Dictionary<int, int> WaveOfUnit = new();
    internal int CitadelComponent;
}
