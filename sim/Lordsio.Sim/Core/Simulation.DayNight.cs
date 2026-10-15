using Lordsio.Sim.Units;

namespace Lordsio.Sim.Core;

/// <summary>The day and night cycle, counted from the tick so every peer sees the same.</summary>
public sealed partial class Simulation
{
    private int DayTicks => Ticks.FromSeconds(Rules.DaySeconds);
    private int NightTicks => Ticks.FromSeconds(Rules.NightSeconds);

    /// <summary>Night falls after every day; at night units see less far (the fog of war closes in).</summary>
    public bool IsNight => NightTicks > 0 && Tick % (DayTicks + NightTicks) >= DayTicks;

    /// <summary>Seconds until night falls or the day breaks; 0 if there is no night.</summary>
    public float SecondsToDayNightChange
    {
        get
        {
            if (NightTicks <= 0)
                return 0f;
            int phase = Tick % (DayTicks + NightTicks);
            int left = phase < DayTicks ? DayTicks - phase : DayTicks + NightTicks - phase;
            return left / (float)TicksPerSecond;
        }
    }

    /// <summary>
    /// How far the day has come, from 0 at dawn to 1 at dusk (1 all night; 0.5 when there is no night): where the sun
    /// stands, for the views' shadows.
    /// </summary>
    public float DayProgress => NightTicks <= 0 ? 0.5f : Math.Min(1f, Tick % (DayTicks + NightTicks) / (float)DayTicks);

    /// <summary>How far a unit sees now: by night, a share of its sight range.</summary>
    public float SightOf(Unit unit) => IsNight ? unit.Type.SightRange * Rules.NightSightFactor : unit.Type.SightRange;
}
