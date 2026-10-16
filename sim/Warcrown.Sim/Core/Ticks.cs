namespace Warcrown.Sim.Core;

/// <summary>Durations from the rules (seconds) turned into simulation ticks, the same way everywhere.</summary>
internal static class Ticks
{
    /// <summary>The whole number of ticks nearest to <paramref name="seconds"/>.</summary>
    public static int FromSeconds(float seconds) => (int)MathF.Round(seconds * Simulation.TicksPerSecond);

    /// <summary>As <see cref="FromSeconds"/>, but never less than one tick: for timers that must run out.</summary>
    public static int AtLeastOne(float seconds) => Math.Max(1, FromSeconds(seconds));
}
