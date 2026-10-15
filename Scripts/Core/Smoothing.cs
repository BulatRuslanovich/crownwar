namespace Lordsio.Core;

/// <summary>Frame-rate independent easing towards a target.</summary>
public static class Smoothing
{
    /// <summary>
    /// The share of the remaining way to cover this frame when approaching a target at <paramref name="rate"/> per second
    /// (for <c>Lerp(current, target, Factor(rate, delta))</c>): the same motion at any frame rate.
    /// </summary>
    public static float Factor(float rate, float delta) => 1f - System.MathF.Exp(-rate * delta);
}
