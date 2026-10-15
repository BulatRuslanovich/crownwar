using System;
using Godot;

namespace Lordsio.Core;

/// <summary>The mood of the world: night falling, the wind, and whether the local team is in a battle (for the music).</summary>
public partial class MatchController
{
    /// <summary>
    /// The local team is in a real fight: enough blows landed on or by its units and buildings lately.
    /// With hysteresis, so the battle music neither starts on a stray arrow nor flickers off mid-fight.
    /// </summary>
    public bool InBattle { get; private set; }

    /// <summary>Blows involving the local team, decaying over a few seconds.</summary>
    private float _battleHeat;
    public float BattleHeat => _battleHeat;
    private const float BattleHeatHalfLife = 4f;
    private const float BattleStarts = 6f;
    private const float BattleEnds = 1f;
    /// <summary>Caps the heat, so peace returns within some 15 seconds even after a huge battle.</summary>
    private const float BattleHeatMax = 15f;

    /// <summary>The world dims to this at night (the interface, on its own layers, stays as it is).</summary>
    private static readonly Color NightTint = new(0.55f, 0.6f, 0.85f);
    /// <summary>How quickly dusk and dawn shade the world (per second).</summary>
    private const float DuskSpeed = 1.5f;
    private CanvasModulate _night = null!;

    /// <summary>How dark it is now, 0 by day to 1 at full night (dusk and dawn in between): how bright lights shine.</summary>
    public float NightAmount { get; private set; }

    /// <summary>
    /// Undoes the night's darkening of the world (1 / the tint, per channel): lights, fires and fireflies are multiplied
    /// by it, so they shine at their own colour through the dark.
    /// </summary>
    public Color LightBoost { get; private set; } = Colors.White;

    /// <summary>A light's colour as it shows through the night: multiplied by <see cref="LightBoost"/>, alpha kept.</summary>
    public Color Lit(Color color) => new(color.R * LightBoost.R, color.G * LightBoost.G, color.B * LightBoost.B, color.A);

    /// <summary>The wind (pixels a second) that drifts clouds, smoke, snow and sand; it veers and gusts slowly.</summary>
    public Vector2 Wind { get; private set; } = new(7f, 1.5f);
    private float _windTime;

    /// <summary>Dusk and dawn, the wind's slow veering, and the battle heat cooling down.</summary>
    private void UpdateAmbience(float delta)
    {
        _night.Color = _night.Color.Lerp(Sim.IsNight ? NightTint : Colors.White, Smoothing.Factor(DuskSpeed, delta));
        NightAmount = Mathf.Clamp((1f - _night.Color.R) / (1f - NightTint.R), 0f, 1f);
        LightBoost = new Color(1f / _night.Color.R, 1f / _night.Color.G, 1f / _night.Color.B);
        _windTime += delta;
        Wind = new Vector2(7f + 3f * MathF.Sin(_windTime / 41f), 1.5f + 2f * MathF.Sin(_windTime / 27f + 1f));
        _battleHeat *= MathF.Pow(0.5f, delta / BattleHeatHalfLife);
    }

    private void UpdateBattleMood()
    {
        if (Sim.IsOver)
            InBattle = false;
        else if (_battleHeat >= BattleStarts)
            InBattle = true;
        else if (_battleHeat <= BattleEnds)
            InBattle = false;
    }
}
