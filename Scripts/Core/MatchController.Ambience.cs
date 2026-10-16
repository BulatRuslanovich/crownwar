using System;
using Godot;
using Warcrown.Sim.Core;

namespace Warcrown.Core;

/// <summary>The mood of the world: night falling, the wind, and how hard the local team is fighting (for the music).</summary>
public partial class MatchController
{
    /// <summary>
    /// How tense the music should be: 0 calm, 1 tension (a skirmish), 2 battle (a real fight, the buildings under
    /// attack, at once when the castle is). From the battle heat, with hysteresis, so the music neither rises on a
    /// stray arrow nor flickers between levels mid-fight.
    /// </summary>
    public int MusicLevel { get; private set; }

    /// <summary>Blows involving the local team (more for one on its buildings), decaying over a few seconds.</summary>
    private float _battleHeat;
    public float BattleHeat => _battleHeat;
    private const float BattleHeatHalfLife = 4f;
    /// <summary>The heat at which the music rises to tension and battle, and at which it falls back from them.</summary>
    private const float TensionStarts = 3f, BattleStarts = 10f, BattleEnds = 5f, TensionEnds = 1.5f;
    /// <summary>A blow on a building of the local team counts this much more than one between units.</summary>
    private const float BuildingHitHeat = 3f;
    /// <summary>Caps the heat, so calm returns within some 15 seconds even after a huge battle.</summary>
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

    /// <summary>Warms the battle heat by a blow that involves the local team.</summary>
    private void HeatUp(AttackLanded hit)
    {
        if (!IsFriendly(hit.FactionId) && !IsFriendly(hit.TargetFactionId))
            return;
        float heat = 1f;
        if (IsFriendly(hit.TargetFactionId) && Sim.GetBuilding(hit.TargetId) is { } building)
        {
            if (building.Type.IsCapital || building.Type.IsCitadel)
                heat = BattleHeatMax; // the castle under attack: full battle at once
            else
                heat = BuildingHitHeat;
        }
        _battleHeat = MathF.Min(BattleHeatMax, _battleHeat + heat);
    }

    private void UpdateBattleMood()
    {
        if (Sim.IsOver)
            MusicLevel = 0;
        else if (_battleHeat >= BattleStarts)
            MusicLevel = 2;
        else if (MusicLevel == 2 && _battleHeat <= BattleEnds)
            MusicLevel = 1;
        else if (MusicLevel == 0 && _battleHeat >= TensionStarts)
            MusicLevel = 1;
        else if (MusicLevel == 1 && _battleHeat <= TensionEnds)
            MusicLevel = 0;
    }
}
