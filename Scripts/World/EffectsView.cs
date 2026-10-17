using System;
using System.Collections.Generic;
using Godot;
using Warcrown.Core;
using Warcrown.Sim.Core;
using Warcrown.UI;

namespace Warcrown.World;

/// <summary>
/// Short-lived pixel effects driven by simulation events: hit sparks and what flies off a struck target (blood,
/// sparks off plate, wood chips, stone splinters), dust, death puffs, collapsing buildings, a catapult stone's flash,
/// flying rocks and crater, construction sparkles and alert pings. Two instances run in a match: a ground layer
/// below the actors (rubble, craters, corpses) and an air layer above them (everything else).
/// </summary>
public partial class EffectsView : Node2D
{
    private enum Kind
    {
        Spark,
        Dust,
        Puff,
        Collapse,
        Rubble,
        Sparkle,
        Ping,
        Impact,
        /// <summary>A fallen soldier lying on the ground for a while, then fading (ground layer).</summary>
        Corpse,
        /// <summary>Bits knocked off a target, thrown up, falling and lying a moment (Size: how hard they fly).</summary>
        Debris,
        /// <summary>Rocks of a shattered catapult stone flying out of the impact.</summary>
        Chunks,
        /// <summary>The bright burst of a stone striking home (bright even at night).</summary>
        Flash,
        /// <summary>A scorched pit left by a stone, fading after a while (ground layer).</summary>
        Crater,
        /// <summary>A dragon's fire licking over the ground it struck (Size: the reach of the blast).</summary>
        Flames,
        /// <summary>Little green crosses rising from a soldier a monk has mended.</summary>
        Heal,
        /// <summary>A number floating up and fading (gold taken or given back), in <see cref="Effect.Text"/>.</summary>
        Text,
    }

    private record struct Effect(Kind Kind, Vector2 Position, float Age, float Duration, float Size, int Seed, Color Color, string? Text = null);

    private static readonly Color SparkColor = new("fff2b0");
    private static readonly Color DustColor = new("b8a789");
    private static readonly Color RubbleColor = new("5f5a54");
    private static readonly Color RubbleLight = new("858079");
    private static readonly Color SparkleColor = UiPalette.Highlight;
    private static readonly Color PingColor = UiPalette.Hostile;
    private static readonly Color Blood = new("9c1f1f");
    private static readonly Color HealColor = new("8dff7a");
    private static readonly Color PlateSpark = new("fff6c8");
    private static readonly Color WoodChip = new("a87a4a");
    private static readonly Color StoneChip = new("9d968e");
    private static readonly Color RockChunk = new("6e6862");
    private static readonly Color FlashColor = new("fff1b0");
    private static readonly Color CraterColor = new("3a3128");
    private static readonly Color CraterRim = new("8a7a5e");
    private static readonly Color FireColor = new("ff9a2e");
    private static readonly Color ScorchColor = new("231a14");
    private const float CraterSeconds = 14f;
    /// <summary>The last seconds in which rubble and craters fade; the share of its time a body lies before it does.</summary>
    private const float RubbleFadeSeconds = 5f, CraterFadeSeconds = 3f, CorpseFadeFrom = 0.75f;
    /// <summary>A fallen soldier's death plays out over this long before he lies still.</summary>
    private const float FallSeconds = 0.55f;
    private const int PopupFontSize = 16;
    private Font? _font;

    /// <summary>True for the layer drawn under units and buildings.</summary>
    [Export] public bool Ground { get; set; }

    private readonly List<Effect> _effects = new();
    private MatchController _match = null!;
    private int _seed;
    /// <summary>An effect began since the last frame: the layer must be drawn again.</summary>
    private bool _added;

    public void Initialize(MatchController match)
    {
        _match = match;
    }

    public void OnSimEvent(SimEvent e)
    {
        switch (e)
        {
            case GoldLooted loot when !Ground && loot.FactionId == _match.LocalFactionId:
                AddText(SimConvert.ToWorld(loot.Position) + new Vector2(0, -32), $"+{loot.Amount}", UiPalette.Gold);
                break;
            case BuildingDemolished pulled when !Ground && pulled.FactionId == _match.LocalFactionId && pulled.Refund > 0:
                AddText(SimConvert.ToWorld(pulled.Position) + new Vector2(0, -32), $"+{pulled.Refund}", UiPalette.Gold);
                break;
            case BuildingPlaced placed when !Ground && _match.Sim.GetBuilding(placed.BuildingId) is { } site:
                // Dust thrown out where the site thumps down.
                var footing = SimConvert.ToWorld(site.Position + new System.Numerics.Vector2(0f, site.Type.Size / 2f));
                Add(Kind.Impact, footing, 0.45f, site.Type.Size * SimConvert.TileSize * 0.55f, DustColor);
                break;
            case SplashLanded { Fire: true } burst when !Ground:
                var burnt = SimConvert.ToWorld(burst.Position);
                Add(Kind.Flash, burnt + new Vector2(0, -6), 0.22f, 22f, FireColor);
                Add(Kind.Flames, burnt, 0.9f, burst.Radius * SimConvert.TileSize, FireColor);
                break;
            case SplashLanded splash when !Ground:
                var landed = SimConvert.ToWorld(splash.Position);
                Add(Kind.Impact, landed, 0.5f, splash.Radius * SimConvert.TileSize, DustColor);
                Add(Kind.Flash, landed + new Vector2(0, -6), 0.16f, 18f, FlashColor);
                Add(Kind.Chunks, landed, 1.1f, 1f, RockChunk);
                break;
            case UnitPromoted promoted when !Ground:
                var hero = SimConvert.ToWorld(promoted.Position) - new Vector2(0, Lift(promoted.UnitId));
                Add(Kind.Sparkle, hero + new Vector2(0, -16), 0.8f, 16f, SparkleColor);
                if (promoted.FactionId == _match.LocalFactionId)
                    AddText(hero + new Vector2(0, -44), Tr(HudText.RankKey(promoted.Rank)), UiPalette.Gold);
                break;
            case UnitHealed healed when !Ground && healed.Amount > 0:
                Add(Kind.Heal, SimConvert.ToWorld(healed.Position) + new Vector2(0, -16 - Lift(healed.TargetId)), 0.9f, 1f, HealColor);
                break;
            case SplashLanded splash when Ground:
                Add(Kind.Crater, SimConvert.ToWorld(splash.Position), CraterSeconds, splash.Radius * SimConvert.TileSize * 0.55f,
                    splash.Fire ? ScorchColor : CraterColor);
                break;
            case AttackLanded hit when !Ground && !hit.Splash: // a stone's victims share its one impact cloud
                bool building = _match.Sim.GetBuilding(hit.TargetId) != null;
                var at = SimConvert.ToWorld(hit.Position) + new Vector2(0, (building ? -16 : -12) - Lift(hit.TargetId));
                Add(building || hit.Ranged ? Kind.Dust : Kind.Spark, at, building ? 0.35f : 0.18f, building ? 12f : 6f, DustColor);
                Add(Kind.Debris, at, 0.75f, building ? 1.2f : 1f, DebrisColor(hit.TargetId, building));
                break;
            case UnitDied died when !Ground:
                Add(Kind.Puff, SimConvert.ToWorld(died.Position) + new Vector2(0, -10 - ArtLayout.FlightAltitude(died.UnitTypeId)), 0.6f, 12f,
                    FactionColors.Get(died.FactionId));
                break;
            case UnitDied died when Ground && died.UnitTypeId.Length > 0:
                _corpseKinds[++_seed] = died.UnitTypeId;
                _effects.Add(new Effect(Kind.Corpse, SimConvert.ToWorld(died.Position), 0f, CorpseSeconds, 0f, _seed,
                    FactionColors.Get(died.FactionId)));
                _added = true;
                break;
            case BuildingDestroyed or BuildingDemolished:
                // A demolished building comes down the same way, only on its owner's orders.
                var (simPosition, footprint) = e switch
                {
                    BuildingDestroyed d => (d.Position, d.Size),
                    BuildingDemolished m => (m.Position, m.Size),
                    _ => default,
                };
                float size = footprint * SimConvert.TileSize / 2f;
                var center = SimConvert.ToWorld(simPosition);
                if (Ground)
                    Add(Kind.Rubble, center, 30f, size, RubbleColor);
                else
                    Add(Kind.Collapse, center, 1.4f, size, DustColor);
                break;
            case BuildingCompleted completed when !Ground && _match.Sim.GetBuilding(completed.BuildingId) is { } b:
                Add(Kind.Sparkle, SimConvert.ToWorld(b.Position), 0.8f, b.Type.Size * SimConvert.TileSize / 2f, SparkleColor);
                break;
        }
    }

    /// <summary>How high above its feet a unit is drawn (a flyer is struck, mended or promoted up there).</summary>
    private float Lift(int unitId) => ArtLayout.FlightAltitude(_match.Sim.GetUnit(unitId)?.Type.Id);

    /// <summary>What flies off a struck target: splinters of stone, chips of a ram's timber, sparks off plate, blood.</summary>
    private Color DebrisColor(int targetId, bool building)
    {
        if (building)
            return StoneChip;
        return _match.Sim.GetUnit(targetId)?.Type switch
        {
            { IsSiege: true } => WoodChip,
            { RangedArmor: >= 0.3f } => PlateSpark,
            _ => Blood,
        };
    }

    /// <summary>Expanding ring marking where the player is being attacked.</summary>
    public void Ping(System.Numerics.Vector2 simPosition) =>
        Add(Kind.Ping, SimConvert.ToWorld(simPosition), 2f, 48f, PingColor);

    public override void _Process(double delta)
    {
        if (_effects.Count == 0)
            return;
        bool changed = _added;
        _added = false;
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            var effect = _effects[i];
            effect.Age += (float)delta;
            if (effect.Age >= effect.Duration)
            {
                if (effect.Kind == Kind.Corpse)
                    _corpseKinds.Remove(effect.Seed);
                _effects.RemoveAt(i);
                changed = true;
                continue;
            }
            _effects[i] = effect;
            changed |= !IsStill(effect);
        }
        // Lying rubble, craters and the fallen look the same frame after frame: the layer is drawn again only
        // when something moves, fades, comes or goes.
        if (changed)
            QueueRedraw();
    }

    /// <summary>Drawn the same as last frame: rubble, a crater or a body that has not started to fade yet.</summary>
    private static bool IsStill(Effect e) => e.Kind switch
    {
        Kind.Rubble => e.Duration - e.Age > RubbleFadeSeconds,
        Kind.Crater => e.Duration - e.Age > CraterFadeSeconds,
        Kind.Corpse => e.Age >= FallSeconds && e.Age < e.Duration * CorpseFadeFrom,
        _ => false,
    };

    public override void _Draw()
    {
        foreach (var e in _effects)
        {
            float t = e.Age / e.Duration;
            var rng = new EffectRandom(e.Seed);
            switch (e.Kind)
            {
                case Kind.Spark: DrawSpark(e, t, rng); break;
                case Kind.Dust: DrawSpecks(e, t, rng, count: 5); break;
                case Kind.Puff: DrawSpecks(e, t, rng, count: 9); break;
                case Kind.Corpse: DrawCorpse(e, t); break;
                case Kind.Debris:
                case Kind.Chunks: DrawDebris(e, t, rng); break;
                case Kind.Flash: DrawFlash(e, t); break;
                case Kind.Flames: DrawFlames(e, t, rng); break;
                case Kind.Crater: DrawCrater(e, rng); break;
                case Kind.Heal: DrawHeal(e, t, rng); break;
                case Kind.Impact: DrawImpact(e, t, rng); break;
                case Kind.Collapse: DrawCollapse(e, t, rng); break;
                case Kind.Rubble: DrawRubble(e, rng); break;
                case Kind.Sparkle: DrawSparkle(e, t); break;
                case Kind.Ping: DrawPing(e, t); break;
                case Kind.Text: DrawText(e, t); break;
            }
        }
    }

    /// <summary>A star of short rays that shrinks.</summary>
    private void DrawSpark(Effect e, float t, EffectRandom rng)
    {
        float ray = e.Size * (1f - t) + 2f;
        for (int r = 0; r < 4; r++)
        {
            var dir = Vector2.Right.Rotated(Mathf.Pi / 4f * r + rng.NextSingle());
            DrawLine(e.Position - dir * ray, e.Position + dir * ray, SparkColor with { A = 1f - t }, 2f);
        }
        DrawRect(new Rect2(e.Position.Round() - Vector2.One, new Vector2(2, 2)), Colors.White with { A = 1f - t });
    }

    /// <summary>Dust kicked up by a blow (or a puff where a unit fell, some specks in the colour of its blood).</summary>
    private void DrawSpecks(Effect e, float t, EffectRandom rng, int count)
    {
        bool puff = e.Kind == Kind.Puff;
        for (int s = 0; s < count; s++)
        {
            var dir = Vector2.Right.Rotated(rng.NextSingle() * Mathf.Tau);
            var p = e.Position + dir * e.Size * t * (0.5f + rng.NextSingle()) + new Vector2(0, -8f * t);
            var color = (s % 3 == 0 && puff ? e.Color : DustColor) with { A = 0.8f * (1f - t) };
            DrawRect(new Rect2(p.Round(), Vector2.One * (s % 2 == 0 ? 4f : 3f)), color);
        }
    }

    /// <summary>A stone's flash, bright against the night too: the world's darkening is undone for it.</summary>
    private void DrawFlash(Effect e, float t)
    {
        DrawCircle(e.Position, e.Size * (0.5f + 0.5f * t), _match.Lit(e.Color) with { A = 1f - t });
        DrawCircle(e.Position, e.Size * 0.4f * (1f - t), _match.Lit(Colors.White));
    }

    /// <summary>Three small crosses drifting up and fading, bright even at night.</summary>
    private void DrawHeal(Effect e, float t, EffectRandom rng)
    {
        var cross = _match.Lit(e.Color) with { A = 1f - t };
        for (int s = 0; s < 3; s++)
        {
            var p = (e.Position + new Vector2((rng.NextSingle() - 0.5f) * 20f, -20f * t - s * 6f)).Round();
            DrawRect(new Rect2(p + new Vector2(-3, -1), new Vector2(6, 2)), cross);
            DrawRect(new Rect2(p + new Vector2(-1, -3), new Vector2(2, 6)), cross);
        }
    }

    /// <summary>A flattened ring of dust spreading over the splash area, with specks thrown out.</summary>
    private void DrawImpact(Effect e, float t, EffectRandom rng)
    {
        DrawSetTransform(e.Position, 0f, new Vector2(1f, 0.5f));
        DrawArc(Vector2.Zero, e.Size * (0.3f + 0.7f * t), 0f, Mathf.Tau, 32, DustColor with { A = 0.7f * (1f - t) }, 4f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        for (int s = 0; s < 14; s++)
        {
            var dir = Vector2.Right.Rotated(rng.NextSingle() * Mathf.Tau);
            var p = e.Position + new Vector2(dir.X, dir.Y * 0.5f) * e.Size * t * (0.4f + rng.NextSingle()) + new Vector2(0, -16f * t);
            DrawRect(new Rect2(p.Round(), Vector2.One * (3 + s % 2)), DustColor with { A = 0.8f * (1f - t) });
        }
    }

    /// <summary>A dust cloud billowing out of the footprint, with debris thrown up and falling.</summary>
    private void DrawCollapse(Effect e, float t, EffectRandom rng)
    {
        for (int s = 0; s < 60; s++)
        {
            var dir = Vector2.Right.Rotated(rng.NextSingle() * Mathf.Tau);
            float dist = e.Size * (0.3f + 1.2f * t) * rng.NextSingle();
            var p = e.Position + dir * dist + new Vector2(0, -12f * t);
            DrawRect(new Rect2(p.Round(), Vector2.One * (4 + rng.Next(5))), DustColor with { A = 0.7f * (1f - t) });
        }
        for (int s = 0; s < 14; s++)
        {
            float vx = (rng.NextSingle() - 0.5f) * 120f, vy = -80f - rng.NextSingle() * 80f;
            float time = e.Age;
            var p = e.Position + new Vector2(vx * time, vy * time + 180f * time * time);
            DrawRect(new Rect2(p.Round(), Vector2.One * (3 + s % 3)), (s % 3 == 0 ? RubbleLight : RubbleColor) with { A = 1f - t });
        }
    }

    /// <summary>Ruins on the ground, fading away over the last few seconds.</summary>
    private void DrawRubble(Effect e, EffectRandom rng)
    {
        float alpha = Mathf.Clamp((e.Duration - e.Age) / RubbleFadeSeconds, 0f, 1f);
        for (int s = 0; s < (int)(e.Size * 1.5f); s++)
        {
            var p = e.Position + new Vector2((rng.NextSingle() - 0.5f) * e.Size * 2f, (rng.NextSingle() - 0.5f) * e.Size * 1.6f);
            var color = (s % 4 == 0 ? RubbleLight : RubbleColor) with { A = alpha };
            DrawRect(new Rect2(p.Round(), new Vector2(4 + rng.Next(6), 3 + rng.Next(3))), color);
        }
    }

    /// <summary>A ring of sparkles rising over a finished building.</summary>
    private void DrawSparkle(Effect e, float t)
    {
        for (int s = 0; s < 12; s++)
        {
            float angle = s / 12f * Mathf.Tau;
            // A little four-pointed twinkle.
            var p = (e.Position + Vector2.Right.Rotated(angle) * e.Size * (0.6f + 0.8f * t) + new Vector2(0, -e.Size * 0.6f)).Round();
            DrawRect(new Rect2(p - new Vector2(2, 1), new Vector2(4, 2)), e.Color with { A = 1f - t });
            DrawRect(new Rect2(p - new Vector2(1, 2), new Vector2(2, 4)), e.Color with { A = 1f - t });
        }
    }

    /// <summary>The number rises quickly then drifts (ease-out), and fades over the last part of its life.</summary>
    private void DrawText(Effect e, float t)
    {
        _font ??= GD.Load<Font>("res://Assets/Fonts/PressStart2P-Regular.ttf");
        float rise = 1f - (1f - t) * (1f - t);
        float alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
        var size = _font.GetStringSize(e.Text!, HorizontalAlignment.Left, -1, PopupFontSize);
        var at = (e.Position + new Vector2(-size.X / 2f, -36f * rise)).Round();
        DrawStringOutline(_font, at, e.Text!, HorizontalAlignment.Left, -1, PopupFontSize, 6, Colors.Black with { A = alpha });
        DrawString(_font, at, e.Text!, HorizontalAlignment.Left, -1, PopupFontSize, e.Color with { A = alpha });
    }

    /// <summary>Two rings pulsing out where the player is attacked, as thick on screen at every zoom.</summary>
    private void DrawPing(Effect e, float t)
    {
        for (int ring = 0; ring < 2; ring++)
        {
            float k = (t * 2f + ring * 0.5f) % 1f;
            DrawArc(e.Position, 8f + e.Size * k, 0f, Mathf.Tau, 48, e.Color with { A = 1f - k }, 2f / _match.Camera.ZoomLevel);
        }
    }

    /// <summary>
    /// Bits thrown up from the hit, falling under gravity onto the ground a few pixels below (the target's feet) and
    /// lying there a moment before fading. Chunks are a stone's rocks: more, bigger, thrown farther.
    /// </summary>
    private void DrawDebris(Effect e, float t, EffectRandom rng)
    {
        bool chunks = e.Kind == Kind.Chunks;
        int count = chunks ? 12 : 5;
        const float gravity = 520f;
        float ground = e.Position.Y + (chunks ? 4f : 12f);
        float alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
        for (int s = 0; s < count; s++)
        {
            float vx = (rng.NextSingle() - 0.5f) * (chunks ? 220f : 100f) * e.Size;
            float vy = -(chunks ? 100f : 50f) - rng.NextSingle() * (chunks ? 120f : 70f) * e.Size;
            float time = e.Age;
            var p = e.Position + new Vector2(vx * time, vy * time + 0.5f * gravity * time * time);
            if (p.Y > ground)
            {
                // Landed: it stops where it hit the ground.
                float landing = (-vy + Mathf.Sqrt(vy * vy + 2f * gravity * (ground - e.Position.Y))) / gravity;
                p = new Vector2(e.Position.X + vx * landing, ground + 2 * (s % 3) - 2);
            }
            float size = chunks ? 4 + s % 3 : 2 + (s % 3 == 0 ? 1 : 0);
            var color = s % 3 == 0 ? e.Color.Lightened(0.25f) : e.Color;
            DrawRect(new Rect2(p.Round(), new Vector2(size, size)), color with { A = alpha });
        }
    }

    /// <summary>A flattened scorched pit with a rim of thrown-up earth and clods around it.</summary>
    private void DrawCrater(Effect e, EffectRandom rng)
    {
        float alpha = Mathf.Clamp((e.Duration - e.Age) / CraterFadeSeconds, 0f, 1f) * 0.75f;
        DrawSetTransform(e.Position, 0f, new Vector2(1f, 0.5f));
        DrawCircle(Vector2.Zero, e.Size + 3f, CraterRim with { A = alpha * 0.8f });
        DrawCircle(Vector2.Zero, e.Size, CraterColor with { A = alpha });
        DrawCircle(new Vector2(-e.Size * 0.25f, -e.Size * 0.25f), e.Size * 0.45f, CraterColor.Darkened(0.3f) with { A = alpha });
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        for (int s = 0; s < 10; s++)
        {
            var dir = Vector2.Right.Rotated(rng.NextSingle() * Mathf.Tau);
            var p = e.Position + new Vector2(dir.X, dir.Y * 0.5f) * e.Size * (1.2f + rng.NextSingle() * 0.8f);
            DrawRect(new Rect2(p.Round(), new Vector2(4, 2)), CraterRim with { A = alpha });
        }
    }

    private static readonly Color[] FlameColors = { new("fff3a0"), new("ff9a2e"), new("e2541f"), new("8a2a14") };
    private static readonly Color EmberColor = new("ffb347");

    /// <summary>
    /// A dragon's fire bursting on the ground: tongues of flame licking up all over the splash area and dying down,
    /// yellow at the heart, red at the tips, embers drifting off; bright even at night.
    /// </summary>
    private void DrawFlames(Effect e, float t, EffectRandom rng)
    {
        for (int s = 0; s < 22; s++)
        {
            var dir = Vector2.Right.Rotated(rng.NextSingle() * Mathf.Tau);
            float reach = e.Size * Mathf.Sqrt(rng.NextSingle());
            var foot = e.Position + new Vector2(dir.X, dir.Y * 0.5f) * reach * (0.5f + 0.5f * t);
            float start = rng.NextSingle() * 0.35f;
            float tall = 10f + rng.NextSingle() * 14f;
            float life = Mathf.Clamp((t - start) / (1f - start), 0f, 1f);
            if (life <= 0f || life >= 1f)
                continue;
            tall *= Mathf.Sin(life * Mathf.Pi);
            for (int k = 0; k < FlameColors.Length; k++)
            {
                float f = k / (float)FlameColors.Length;
                float width = Mathf.Round(6f * (1f - f * 0.7f));
                var p = (foot + new Vector2(Mathf.Sin(e.Age * 14f + s) * 2f * f, -tall * f)).Round();
                DrawRect(new Rect2(p - new Vector2(width / 2f, 0), new Vector2(width, Mathf.Max(2f, Mathf.Round(tall / 4f)))),
                    _match.Lit(FlameColors[k]) with { A = 1f - life * 0.6f });
            }
        }
        for (int s = 0; s < 8; s++)
        {
            var p = e.Position + new Vector2((rng.NextSingle() - 0.5f) * e.Size * 1.6f, -40f * t * (0.5f + rng.NextSingle()));
            DrawRect(new Rect2(p.Round(), new Vector2(2, 2)), _match.Lit(EmberColor) with { A = 1f - t });
        }
    }

    private const float CorpseSeconds = 8f;

    /// <summary>
    /// The unit's standing picture tipped onto its side (left or right at random), darkened; it lies still, then
    /// fades over its last quarter. Size carries the unit's row in the atlas.
    /// </summary>
    private void DrawCorpse(Effect e, float t)
    {
        if (!_corpseKinds.TryGetValue(e.Seed, out var kind))
            return;
        var art = ArtLayout.UnitArtOf(kind);
        int w = art.CellWidth, h = art.CellHeight;
        // The death plays out (a stagger, buckling knees, the fall) and the last frame, lying, stays; a flyer drops out
        // of the sky meanwhile, gathering speed.
        float fall = Mathf.Min(1f, e.Age / FallSeconds);
        int frame = ArtLayout.FrameDeath + Mathf.Min(ArtLayout.DeathFrames - 1, (int)(fall * ArtLayout.DeathFrames));
        var region = new Rect2(frame * w, art.Row * h, w, h);
        float alpha = t < CorpseFadeFrom ? 1f : 1f - (t - CorpseFadeFrom) / (1f - CorpseFadeFrom);
        float side = e.Seed % 2 == 0 ? 1f : -1f; // which way it faced
        float altitude = ArtLayout.FlightAltitude(kind) * (1f - fall * fall);
        DrawSetTransform(e.Position - new Vector2(0, altitude), 0f, new Vector2(side, 1f));
        var rect = new Rect2(-art.Feet.X, -art.Feet.Y, w, h);
        DrawTextureRectRegion(_match.UnitSheets.For(e.Color, art.Big), rect, region, Colors.White with { A = alpha });
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>The kind of each lying body, by its effect's seed.</summary>
    private readonly Dictionary<int, string> _corpseKinds = new();

    private void AddText(Vector2 position, string text, Color color)
    {
        _effects.Add(new Effect(Kind.Text, position, 0f, 1.3f, 0f, ++_seed, color, text));
        _added = true;
    }

    private void Add(Kind kind, Vector2 position, float duration, float size, Color color)
    {
        _effects.Add(new Effect(kind, position, 0f, duration, size, ++_seed, color));
        _added = true;
    }

    /// <summary>
    /// The same scatter for an effect every frame, without allocating: a small generator (SplitMix32) restarted from
    /// the effect's seed each time it is drawn.
    /// </summary>
    private struct EffectRandom
    {
        private uint _state;

        public EffectRandom(int seed) => _state = (uint)seed * 0x9E3779B9u + 0x7F4A7C15u;

        private uint NextUInt()
        {
            uint z = _state += 0x9E3779B9u;
            z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
            z = (z ^ (z >> 13)) * 0xC2B2AE35u;
            return z ^ (z >> 16);
        }

        /// <summary>A number in [0, 1).</summary>
        public float NextSingle() => (NextUInt() >> 8) * (1f / (1 << 24));

        /// <summary>An integer in [0, <paramref name="max"/>).</summary>
        public int Next(int max) => (int)(NextUInt() % (uint)max);
    }
}
