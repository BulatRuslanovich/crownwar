using System;
using Godot;
using Warcrown.World;

namespace Warcrown.Dev;

/// <summary>
/// The unit sheets: every kind in every frame of every animation, with their faction-colour masks. Drawn in the
/// game's moody, high-contrast style: a warm sun from the top-left, a cold sky rim light on the far edges, deep indigo
/// shadows, a near-black outline, and blows that leave a smear of light.
/// </summary>
public static partial class ArtGenerator
{
    /// <summary>How much moodier than the ground figures and buildings are drawn (see <see cref="Ramp.Of"/>).</summary>
    private const float FigureMood = 0.22f;

    private static readonly Ramp SkinR = Ramp.Of(new Color("dba27a"), mood: FigureMood);
    private static readonly Ramp SteelR = Ramp.Of(new Color("a3adbd"), mood: FigureMood);
    private static readonly Ramp IronR = Ramp.Of(new Color("5e6474"), mood: FigureMood);
    private static readonly Ramp LeatherR = Ramp.Of(new Color("8a5631"), mood: FigureMood);
    private static readonly Ramp TrousersR = Ramp.Of(new Color("4f4048"), mood: FigureMood);
    private static readonly Ramp BootR = Ramp.Of(new Color("3a2a26"), mood: FigureMood);
    private static readonly Ramp WoodR = Ramp.Of(new Color("946338"), mood: FigureMood);
    private static readonly Ramp DarkWoodR = Ramp.Of(new Color("5e3d24"), mood: FigureMood);
    private static readonly Ramp HoodR = Ramp.Of(new Color("4a6a33"), mood: FigureMood);
    private static readonly Ramp RobeR = Ramp.Of(new Color("6b452b"), mood: FigureMood);
    private static readonly Ramp RopeR = Ramp.Of(new Color("d8c99e"), mood: FigureMood);
    private static readonly Ramp GoldR = Ramp.Of(new Color("eab43a"), mood: FigureMood);
    private static readonly Ramp HorseR = Ramp.Of(new Color("8a5530"), mood: FigureMood);
    private static readonly Ramp ManeR = Ramp.Of(new Color("2c1e1a"), mood: FigureMood);
    private static readonly Ramp StoneR = Ramp.Of(new Color("8f8a84"), mood: FigureMood);
    private static readonly Ramp FeatherR = Ramp.Of(new Color("ece2cc"), mood: FigureMood);
    private static readonly Ramp WingR = Ramp.Of(new Color("9c7346"), mood: FigureMood);
    private static readonly Ramp LionR = Ramp.Of(new Color("c88c44"), mood: FigureMood);
    private static readonly Ramp BeakR = Ramp.Of(new Color("f2b634"), mood: FigureMood);
    private static readonly Ramp ScaleR = Ramp.Of(new Color("3f4a45"), mood: FigureMood);
    private static readonly Ramp BellyR = Ramp.Of(new Color("c9a062"), mood: FigureMood);
    private static readonly Ramp HornR = Ramp.Of(new Color("e6d9ba"), mood: FigureMood);
    private static readonly Ramp MailR = Ramp.Of(new Color("4a4652"), mood: FigureMood);
    private static readonly Ramp FurR = Ramp.Of(new Color("6b4628"), mood: FigureMood);
    private static readonly Ramp FlameR = Ramp.Of(new Color("ff8a2a"), mood: FigureMood);
    private static readonly Ramp MembraneR = Ramp.Of(new Color("4e2f33"), mood: FigureMood);
    private static readonly Color Eye = new("1e1418");
    private static readonly Ramp FaceShadowR = Ramp.Of(new Color("2a2030"));
    private static readonly Color SilhouetteRim = new("0c0916");
    private static readonly Color SkyRim = new("8fe1ff");
    private static readonly Color Holy = new("fff2a8");
    private static readonly Color Ember = new("ffcf5a");
    private static readonly Color SteelSmear = new("9fe4ff");

    /// <summary>
    /// The soldiers' sheet (units.png): cells of 32×40, feet at (16, 39), facing right, one row a kind
    /// (<see cref="ArtLayout.UnitKinds"/>); and the big beasts' sheet (units_big.png): 64×64, feet at (32, 62)
    /// (<see cref="ArtLayout.BigKinds"/>). Columns: the animations of <see cref="ArtLayout"/> (idle, walk, attack,
    /// hurt, death). The *_team.png sheets hold the faction-colour masks.
    /// </summary>
    private static (PixelCanvas Soldiers, PixelCanvas Big) Units()
    {
        var soldiers = new PixelCanvas(ArtLayout.UnitCellWidth * ArtLayout.UnitFrames, ArtLayout.UnitCellHeight * ArtLayout.UnitKinds.Length);
        var big = new PixelCanvas(ArtLayout.BigCellSize * ArtLayout.UnitFrames, ArtLayout.BigCellSize * ArtLayout.BigKinds.Length);
        for (int row = 0; row < ArtLayout.UnitKinds.Length; row++)
            DrawRow(soldiers, row, ArtLayout.UnitKinds[row], new Vector2I(ArtLayout.UnitCellWidth, ArtLayout.UnitCellHeight), ArtLayout.UnitFeet);
        for (int row = 0; row < ArtLayout.BigKinds.Length; row++)
            DrawRow(big, row, ArtLayout.BigKinds[row], new Vector2I(ArtLayout.BigCellSize, ArtLayout.BigCellSize), ArtLayout.BigFeet);
        return (soldiers, big);
    }

    /// <summary>Every frame of one kind: each drawn, rim-lit and outlined in its cell (a pool under the body that lies still at the last).</summary>
    private static void DrawRow(PixelCanvas sheet, int row, string kind, Vector2I cellSize, Vector2I feet)
    {
        for (int frame = 0; frame < ArtLayout.UnitFrames; frame++)
        {
            var cell = new Vector2I(frame * cellSize.X, row * cellSize.Y);
            var pose = Pose.Of(frame);
            sheet.Origin = cell;
            sheet.Clip = new Rect2I(cell, cellSize);
            if (pose.Lying && pose.Index == ArtLayout.DeathFrames - 1 && !IsMachine(kind))
                sheet.GroundShadow(feet.X - 2, feet.Y - 1, cellSize.X * 0.34f, 2.4f, 0.55f); // a dark pool
            DrawKind(sheet, kind, pose, feet);
            sheet.Origin = Vector2I.Zero;
            sheet.RimLight(new Rect2I(cell, cellSize), SkyRim, pose.Lying ? 0.3f : 0.45f);
            sheet.SelOut(SilhouetteRim, new Rect2I(cell, cellSize));
            if (pose.Lying && pose.Index == ArtLayout.DeathFrames - 1)
                Darken(sheet, new Rect2I(cell, cellSize), 0.25f); // the last frame: still, sinking into the dusk
        }
        sheet.Clip = null;
    }

    private static bool IsMachine(string kind) => kind is "ram" or "trebuchet";

    /// <summary>
    /// How big each kind is drawn, the man-at-arms a bit over three quarters of a tile tall: crossbowmen short, archers
    /// and priests a little taller, knights bigger, riders up on their horses, the warlord a head and more over everyone.
    /// </summary>
    private static float SizeOf(string kind) => kind switch
    {
        "infantry" => 0.78f,
        "archer" => 0.8f,
        "crossbowman" => 0.76f,
        "monk" => 0.8f,
        "heavy" => 0.9f,
        "cavalry" => 0.9f,
        "griffin" => 0.95f,
        "ram" => 0.92f,
        "warlord" => 1.3f,
        _ => 1f,
    };

    private static void DrawKind(PixelCanvas c, string kind, Pose pose, Vector2I feet)
    {
        var f = new Figure(c, feet, SizeOf(kind));
        switch (kind)
        {
            case "infantry": Infantry(f, pose); break;
            case "archer": Archer(f, pose); break;
            case "heavy": Heavy(f, pose); break;
            case "ram": Ram(f, pose); break;
            case "cavalry": Cavalry(f, pose); break;
            case "crossbowman": Crossbowman(f, pose); break;
            case "monk": Monk(f, pose); break;
            case "trebuchet": Trebuchet(f, pose); break;
            case "griffin": Griffin(f, pose); break;
            case "warlord": Warlord(f, pose); break;
            case "dragon": Dragon(c, pose); break;
        }
    }

    /// <summary>Darkens the drawn pixels of an area towards the dusk by <paramref name="amount"/> (a body lying still).</summary>
    private static void Darken(PixelCanvas c, Rect2I area, float amount)
    {
        for (int y = area.Position.Y; y < area.End.Y; y++)
            for (int x = area.Position.X; x < area.End.X; x++)
            {
                var b = c.Base.GetPixel(x, y);
                if (b.A > 0f)
                    c.Base.SetPixel(x, y, b.Lerp(new Color("1a1430"), amount) with { A = b.A });
                var m = c.Mask.GetPixel(x, y);
                if (m.A > 0f)
                    c.Mask.SetPixel(x, y, new Color(m.R * (1f - amount), m.G * (1f - amount), m.B * (1f - amount)));
            }
    }

    private enum Act { Idle, Walk, Attack, Hurt, Death }

    /// <summary>
    /// How a body stands in a frame (in the 32×40 cell): the centre of the near and the far foot and how high each is
    /// lifted, how far the upper body bobs (down breathing in, up mid-stride), sinks (buckling knees) and leans (back
    /// winding up, forward striking); which part of the attack it is in; the cycle's phase (0..1) for wings and capes.
    /// </summary>
    private readonly record struct Pose(Act Act, int Index, float Near, float Far, float NearLift, float FarLift, int Bob, int Lean,
        int Sink, bool Windup, bool Strike, bool Follow, int Step, float Phase)
    {
        public static Pose Of(int frame)
        {
            if (frame < ArtLayout.FrameWalk)
            {
                int i = frame - ArtLayout.FrameIdle;
                return new Pose(Act.Idle, i, 18f, 13f, 0f, 0f, i is 1 or 2 ? 1 : 0, 0, 0, false, false, false, 0, i / (float)ArtLayout.IdleFrames);
            }
            if (frame < ArtLayout.FrameAttack)
            {
                int i = frame - ArtLayout.FrameWalk;
                float phase = i / (float)ArtLayout.WalkFrames;
                float a = phase * Mathf.Tau;
                // The near foot pushes back along the ground for half the cycle and swings forward through the air for
                // the other; the far foot is half a cycle behind. The body is highest as the feet pass.
                float near = 16f + 5.5f * Mathf.Cos(a), far = 16f + 5.5f * Mathf.Cos(a + Mathf.Pi);
                float nearLift = Mathf.Max(0f, -Mathf.Sin(a)) * 3.5f, farLift = Mathf.Max(0f, Mathf.Sin(a)) * 3.5f;
                int bob = -(int)Mathf.Round(Mathf.Abs(Mathf.Sin(a)) * 1.4f);
                return new Pose(Act.Walk, i, near, far, nearLift, farLift, bob, 0, 0, false, false, false, 1 + i / 2, phase);
            }
            if (frame < ArtLayout.FrameHurt)
            {
                int i = frame - ArtLayout.FrameAttack;
                return i switch
                {
                    0 => new Pose(Act.Attack, i, 20f, 11f, 0f, 0f, 0, -1, 0, true, false, false, 0, 0.1f),
                    1 => new Pose(Act.Attack, i, 21f, 10f, 0f, 0f, 1, -2, 0, true, false, false, 0, 0.2f),
                    2 => new Pose(Act.Attack, i, 23f, 10f, 0f, 0f, 0, 2, 0, false, true, false, 0, 0.5f),
                    3 => new Pose(Act.Attack, i, 23f, 11f, 0f, 0f, 0, 3, 0, false, true, true, 0, 0.6f),
                    4 => new Pose(Act.Attack, i, 21f, 12f, 0f, 0f, 0, 1, 0, false, false, false, 0, 0.75f),
                    _ => new Pose(Act.Attack, i, 19f, 13f, 0f, 0f, 0, 0, 0, false, false, false, 0, 0.9f),
                };
            }
            if (frame == ArtLayout.FrameHurt)
                return Flinch;
            int d = frame - ArtLayout.FrameDeath;
            return d switch
            {
                0 => Flinch with { Act = Act.Death, Index = 0, Lean = -3 },
                1 => new Pose(Act.Death, 1, 19f, 12f, 0f, 0f, 0, -1, 4, false, false, false, 0, 0.5f),
                2 => new Pose(Act.Death, 2, 20f, 14f, 0f, 0f, 0, -4, 8, false, false, false, 0, 0.75f),
                _ => new Pose(Act.Death, d, 18f, 13f, 0f, 0f, 0, 0, 0, false, false, false, 0, 0f),
            };
        }

        private static readonly Pose Flinch = new(Act.Hurt, 0, 18f, 11f, 0f, 0f, 1, -2, 0, false, false, false, 0, 0.3f);

        /// <summary>The last frames of a death: lying on the ground.</summary>
        public bool Lying => Act == Act.Death && Index >= 3;

        public bool Hurt => Act == Act.Hurt || Act == Act.Death;

        /// <summary>Half way through the stride (the second and fourth quarter of the cycle), for things that bounce with it.</summary>
        public bool Passing => Step is 2 or 4;
    }

    /// <summary>
    /// A figure drawn in the coordinates of the soldiers' 32×40 cell (feet at 16, 39), placed at <paramref name="Feet"/>
    /// of the canvas cell and scaled by <paramref name="Scale"/> (the warlord is the same rig, half as big again).
    /// <see cref="Shift"/> moves what follows (the upper body bobbing and leaning).
    /// </summary>
    private sealed class Figure
    {
        public Figure(PixelCanvas canvas, Vector2I feet, float scale)
        {
            C = canvas;
            Feet = feet;
            Scale = scale;
        }

        public PixelCanvas C { get; }
        public Vector2I Feet { get; }
        public float Scale { get; }
        public Vector2 Shift { get; set; }

        public Vector2 P(float x, float y) => new(Feet.X + (x + Shift.X - 16f) * Scale, Feet.Y + (y + Shift.Y - 39f) * Scale);

        /// <summary>The rig's coordinates of a canvas pixel (the inverse of <see cref="P"/>).</summary>
        private Vector2 Unproject(float px, float py) => new((px - Feet.X) / Scale + 16f - Shift.X, (py - Feet.Y) / Scale + 39f - Shift.Y);

        private Vector2 Radii(float rx, float ry) => new Vector2(rx, ry) * Scale;

        /// <summary>A line's width scaled with the figure, but never thinner than a pixel (strings, shafts).</summary>
        private float Width(float w) => Mathf.Max(w * Scale, Mathf.Min(w, 1f));

        public void Stroke(float x0, float y0, float x1, float y1, float w, Ramp r, float bias = 0f)
        {
            var a = P(x0, y0);
            var b = P(x1, y1);
            C.Stroke(a.X, a.Y, b.X, b.Y, Width(w), r, bias);
        }

        public void Taper(float x0, float y0, float x1, float y1, float w0, float w1, Ramp r, float bias = 0f)
        {
            var a = P(x0, y0);
            var b = P(x1, y1);
            C.Taper(a.X, a.Y, b.X, b.Y, Width(w0), Width(w1), r, bias);
        }

        public void TeamStroke(float x0, float y0, float x1, float y1, float w, float bias = 0f)
        {
            var a = P(x0, y0);
            var b = P(x1, y1);
            C.TeamStroke(a.X, a.Y, b.X, b.Y, Width(w), bias);
        }

        public void Ball(float cx, float cy, float rx, float ry, Ramp r, float bias = 0f, float flat = 0f)
        {
            var p = P(cx, cy);
            var radii = Radii(rx, ry);
            C.Ball(p.X, p.Y, radii.X, radii.Y, r, bias, flat);
        }

        public void TeamBall(float cx, float cy, float rx, float ry, float bias = 0f, float flat = 0f)
        {
            var p = P(cx, cy);
            var radii = Radii(rx, ry);
            C.TeamBall(p.X, p.Y, radii.X, radii.Y, bias, flat);
        }

        /// <summary>A polygon lit by a function of its own (unscaled) coordinates; faction colour when <paramref name="r"/> is null.</summary>
        public void Poly(Ramp? r, Func<float, float, float> light, params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++)
                points[i] = P(xy[2 * i], xy[2 * i + 1]);
            C.Polygon(points, r, (px, py) =>
            {
                var q = Unproject(px + 0.5f, py + 0.5f);
                return light(q.X, q.Y);
            });
        }

        /// <summary>A block of <paramref name="w"/>×<paramref name="h"/> (unscaled) pixels of one colour.</summary>
        public void Dot(float x, float y, Color color, float w = 1f, float h = 1f)
        {
            var p = P(x, y);
            int pw = Math.Max(1, (int)Mathf.Round(w * Scale)), ph = Math.Max(1, (int)Mathf.Round(h * Scale));
            C.Rect((int)Mathf.Floor(p.X), (int)Mathf.Floor(p.Y), pw, ph, color);
        }

        public void TeamDot(float x, float y, float shade, float w = 1f, float h = 1f)
        {
            var p = P(x, y);
            int pw = Math.Max(1, (int)Mathf.Round(w * Scale)), ph = Math.Max(1, (int)Mathf.Round(h * Scale));
            for (int j = 0; j < ph; j++)
                for (int i = 0; i < pw; i++)
                    C.Team((int)Mathf.Floor(p.X) + i, (int)Mathf.Floor(p.Y) + j, shade);
        }

        /// <summary>The smear of a blow: a crescent of light round (cx, cy) from one angle (degrees) to another.</summary>
        public void Smear(float cx, float cy, float radius, float fromDeg, float toDeg, float thickness, Color tint, float alpha = 0.85f)
        {
            var p = P(cx, cy);
            C.Smear(p.X, p.Y, radius * Scale, Mathf.DegToRad(fromDeg), Mathf.DegToRad(toDeg), thickness * Scale, tint, alpha);
        }

        /// <summary>A straight streak of light behind a thrust, fading towards its tail.</summary>
        public void Streak(float x0, float y0, float x1, float y1, float thickness, Color tint)
        {
            var a = P(x0, y0);
            var b = P(x1, y1);
            int steps = (int)Mathf.Ceil(a.DistanceTo(b)) + 1;
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                var q = a.Lerp(b, t);
                float w = thickness * Scale * t;
                for (float k = -w / 2f; k <= w / 2f; k += 0.5f)
                {
                    int x = (int)Mathf.Round(q.X), y = (int)Mathf.Round(q.Y + k);
                    if (!C.IsSolid(x, y) && C.Get(x, y).A <= 0f)
                        C.Px(x, y, Colors.White.Lerp(tint, Mathf.Abs(k) / Mathf.Max(0.5f, w)) with { A = 0.25f + 0.6f * t }, solid: false);
                }
            }
        }

        public void Shadow(float cx, float rx)
        {
            var p = P(cx, 39f);
            C.GroundShadow(p.X, Feet.Y - 0.5f, rx * Scale, 2.2f * Scale, 0.32f);
        }
    }

    // ---- Materials and gear shared by everyone ----------------------------------------------------------------------

    // Materials of the Empire's soldiery, grim and worn: blackened and dull steel, brass trim, dark wool and leather,
    // quilted linen, parchment and wax. The faction's colour is kept for the accents only.
    private static readonly Ramp PlateR = Ramp.Of(new Color("7f8794"), mood: FigureMood);
    private static readonly Ramp BlackSteelR = Ramp.Of(new Color("4c515c"), mood: FigureMood);
    private static readonly Ramp BrassR = Ramp.Of(new Color("b88f3e"), mood: FigureMood);
    private static readonly Ramp DoubletR = Ramp.Of(new Color("3e2e33"), mood: FigureMood);
    private static readonly Ramp GambesonR = Ramp.Of(new Color("8c7b5c"), mood: FigureMood);
    private static readonly Ramp CloakR = Ramp.Of(new Color("3a3437"), mood: FigureMood);
    private static readonly Ramp OliveR = Ramp.Of(new Color("38402c"), mood: FigureMood);
    private static readonly Ramp ParchmentR = Ramp.Of(new Color("d4c296"), mood: FigureMood);
    private static readonly Ramp PriestRobeR = Ramp.Of(new Color("4a3427"), mood: FigureMood);
    private static readonly Ramp SkullR = Ramp.Of(new Color("d8ceb4"), mood: FigureMood);
    private static readonly Ramp WaxR = Ramp.Of(new Color("9a1e1e"), mood: FigureMood);
    private static readonly Ramp PaviseR = Ramp.Of(new Color("5a4632"), mood: FigureMood);
    private static readonly Ramp HabitR = Ramp.Of(new Color("d6ccb4"), 0.8f, mood: FigureMood);

    /// <summary>Lit like a cylinder across x from <paramref name="left"/> to <paramref name="right"/> (bright left of centre).</summary>
    private static Func<float, float, float> Round(float left, float right, float bias = 0f) => (x, _) =>
    {
        float across = (x - left) / Mathf.Max(1f, right - left);
        return 1.3f - 3.4f * Mathf.Abs(across - 0.28f) + bias;
    };

    /// <summary>A rider's arm from the shoulder to the gauntleted hand (the far one darker).</summary>
    private static void Arm(Figure f, float sx, float sy, float hx, float hy, Ramp? sleeve, float bias = 0f)
    {
        if (sleeve is { } r)
            f.Stroke(sx, sy, hx, hy, 3f, r, bias);
        else
            f.TeamStroke(sx, sy, hx, hy, 3f, bias);
        f.Ball(hx, hy, 1.6f, 1.6f, LeatherR, bias + 0.2f);
    }

    /// <summary>A dark, weather-worn cloak from (x, y) at the shoulders, its ragged hem streaming back and waving through the cycle.</summary>
    private static void Cape(Figure f, Pose pose, float x, float y, float length, float width = 5f)
    {
        float wave = Mathf.Sin(pose.Phase * Mathf.Tau) * 1.5f;
        float stream = pose.Act == Act.Walk ? 3f : pose.Strike ? 2f : pose.Hurt ? -1f : 0f;
        float hemX = x - width * 0.7f - stream * 1.4f, hemY = y + length + wave;
        f.Poly(CloakR, (px, py) => -0.5f + (px - (x - width)) * 0.18f - (py - y) * 0.03f,
            x, y, x - width * 0.4f, y, x - width - stream, y + length * 0.6f + wave,
            hemX - 0.5f, hemY - 0.5f, hemX + 1.2f, hemY - 2f, hemX + 2f, hemY + 0.5f, hemX + 3.5f, hemY - 1.5f, // a torn hem
            x + 1f, y + length * 0.85f);
    }

    /// <summary>
    /// A halberd: an ash shaft with an iron-shod foot, and at its head an axe blade on the forward side, a spike beyond
    /// and a hook behind.
    /// </summary>
    private static void Halberd(Figure f, float x0, float y0, float x1, float y1)
    {
        f.Stroke(x0, y0, x1, y1, 1.6f, WoodR);
        var dir = new Vector2(x1 - x0, y1 - y0).Normalized();
        var front = new Vector2(dir.Y, -dir.X); // the side the blade faces: forward (right) for an upright shaft
        if (front.X < 0f)
            front = -front;
        var head = new Vector2(x1, y1) - dir * 3f;
        f.Stroke(head.X - dir.X * 2f, head.Y - dir.Y * 2f, head.X + dir.X * 2f, head.Y + dir.Y * 2f, 2f, PlateR, 0.3f);
        f.Poly(PlateR, (x, y) => 1.2f - ((x - head.X) * front.X + (y - head.Y) * front.Y) * 0.35f,
            head.X - dir.X * 2.5f, head.Y - dir.Y * 2.5f,
            head.X + front.X * 4.5f - dir.X * 3.5f, head.Y + front.Y * 4.5f - dir.Y * 3.5f,
            head.X + front.X * 5f + dir.X * 1.5f, head.Y + front.Y * 5f + dir.Y * 1.5f,
            head.X + dir.X * 1.5f, head.Y + dir.Y * 1.5f);
        f.Poly(PlateR, (x, y) => 0.9f, x1 - front.X * 0.9f, y1 - front.Y * 0.9f, x1 + dir.X * 5f, y1 + dir.Y * 5f, x1 + front.X * 0.9f, y1 + front.Y * 0.9f);
        f.Stroke(head.X - front.X * 1f, head.Y - front.Y * 1f, head.X - front.X * 3f + dir.X * 1f, head.Y - front.Y * 3f + dir.Y * 1f, 1f, PlateR, -0.2f);
        f.Stroke(x0, y0, x0 - dir.X * 1.5f, y0 - dir.Y * 1.5f, 1.8f, IronR);
    }

    /// <summary>A spear (a lance): a wooden shaft with a leaf-shaped steel head pointing the way it goes.</summary>
    private static void Spear(Figure f, float x0, float y0, float x1, float y1)
    {
        f.Stroke(x0, y0, x1, y1, 1.6f, WoodR);
        var dir = new Vector2(x1 - x0, y1 - y0).Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        f.Poly(PlateR, (x, y) => 0.8f - ((x - x1) * side.X + (y - y1) * side.Y) * 0.8f,
            x1 + side.X * 1.7f, y1 + side.Y * 1.7f, x1 + dir.X * 5.5f, y1 + dir.Y * 5.5f, x1 - side.X * 1.7f, y1 - side.Y * 1.7f);
    }

    /// <summary>A small swallow-tailed pennon in the faction's colours, a little way below the tip of a lance or pole.</summary>
    private static void Pennon(Figure f, Pose pose, float x, float y, float length = 5f)
    {
        float flutter = Mathf.Sin(pose.Phase * Mathf.Tau * 2f) * 0.8f;
        f.Poly(null, (px, py) => 0.6f - (py - y) * 0.3f, x, y, x - length, y + 0.5f + flutter, x - length + 1.5f, y + 1.5f + flutter,
            x - length, y + 2.5f + flutter, x, y + 2.5f);
    }

    /// <summary>A sword from the hilt (x0, y0) to the point.</summary>
    private static void Sword(Figure f, float x0, float y0, float x1, float y1)
    {
        var dir = new Vector2(x1 - x0, y1 - y0).Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        f.Stroke(x0 + dir.X * 2f, y0 + dir.Y * 2f, x1, y1, 2f, PlateR, 0.6f);
        f.Stroke(x0 + side.X * 2.5f, y0 + side.Y * 2.5f, x0 - side.X * 2.5f, y0 - side.Y * 2.5f, 1.4f, BrassR);
        f.Stroke(x0, y0, x0 - dir.X * 2f, y0 - dir.Y * 2f, 1.6f, LeatherR);
        f.Ball(x0 - dir.X * 2.6f, y0 - dir.Y * 2.6f, 1f, 1f, BrassR, 0.4f);
    }

    /// <summary>A bow from (x0, y0) to (x1, y1), its belly bulging <paramref name="bulge"/> pixels forward.</summary>
    private static void Bow(Figure f, float x0, float y0, float x1, float y1, float bulge)
    {
        float mx = (x0 + x1) / 2f + bulge, my = (y0 + y1) / 2f;
        f.Stroke(x0, y0, mx, my - 4f, 1.6f, DarkWoodR, 0.5f);
        f.Stroke(mx, my - 4f, mx, my + 4f, 1.8f, LeatherR);
        f.Stroke(mx, my + 4f, x1, y1, 1.6f, DarkWoodR, 0.5f);
    }

    /// <summary>A gilded twin-tailed comet crowning a staff: a bright head, two tails streaming down behind.</summary>
    private static void Comet(Figure f, float x, float y, float glow)
    {
        f.Stroke(x - 0.5f, y + 1f, x - 3f, y + 5f, 1.2f, BrassR, glow - 0.4f);
        f.Stroke(x + 0.5f, y + 1f, x + 2.5f, y + 5.5f, 1.2f, BrassR, glow - 0.6f);
        f.Ball(x, y, 2.1f, 2.1f, GoldR, glow);
    }

    /// <summary>A great axe from the haft's foot (x0, y0) to its head.</summary>
    private static void Axe(Figure f, float x0, float y0, float x1, float y1)
    {
        f.Stroke(x0, y0, x1, y1, 1.8f, LeatherR);
        var dir = new Vector2(x1 - x0, y1 - y0).Normalized();
        var side = new Vector2(dir.Y, -dir.X);
        if (side.X < 0f)
            side = -side;
        var a = new Vector2(x1, y1) - dir * 1f;
        f.Poly(PlateR, (x, y) => 1.2f - ((x - a.X) * side.X + (y - a.Y) * side.Y) * 0.25f,
            a.X - dir.X * 3f, a.Y - dir.Y * 3f, a.X + side.X * 5f - dir.X * 5f, a.Y + side.Y * 5f - dir.Y * 5f,
            a.X + side.X * 6f + dir.X * 1f, a.Y + side.Y * 6f + dir.Y * 1f, a.X + side.X * 5f + dir.X * 4f, a.Y + side.Y * 5f + dir.Y * 4f,
            a.X + dir.X * 1f, a.Y + dir.Y * 1f);
    }

    // ---- The soldiers' bodies --------------------------------------------------------------------------------------

    /// <summary>
    /// How a kind of soldier is built (in the 32×40 cell's pixels, before the kind's scale): half the width of the
    /// shoulders and of the waist, the length of the legs and of the torso, the head's radius, how far the shoulders hunch
    /// forward, how heavily the body sinks into each step, how long the stride is, and how thick the limbs are. Soldiers
    /// are stocky — about three and a half heads tall, helmets and shields big — so they read when small.
    /// </summary>
    private readonly record struct Build(float Shoulder, float Waist, float Leg, float Torso, float HeadR, float Hunch,
        float Weight, float Stride, float Limb);

    private static readonly Build Trooper = new(6.2f, 4.4f, 10f, 9.5f, 4.2f, 0.3f, 1.1f, 1f, 1.25f);
    private static readonly Build Hunter = new(5.4f, 4f, 10.6f, 9f, 4f, 0.8f, 0.85f, 1.1f, 1.1f);
    private static readonly Build Knight = new(7f, 5.1f, 10.4f, 10.2f, 4.4f, 0f, 1.6f, 0.85f, 1.45f);
    private static readonly Build Stocky = new(6.4f, 5.2f, 9.4f, 9f, 4.2f, 0.4f, 1.3f, 0.9f, 1.35f);
    private static readonly Build Priest = new(5.6f, 5f, 10.4f, 9.4f, 4.1f, 0.9f, 0.9f, 0.9f, 1.15f);
    private static readonly Build Brute = new(7.2f, 5.8f, 10f, 10.5f, 4.2f, 1.2f, 1.8f, 0.85f, 1.6f);

    /// <summary>
    /// A soldier's skeleton in one frame, seen three quarters from the front, turned to the right: the near shoulder (his
    /// right, the weapon arm) on the left of the picture, the far one (his left, the shield arm) on the right; hips, neck,
    /// head and shoulders placed by the build and the pose, the feet by the stride, and the knees and elbows found between
    /// them (they bend the way joints bend), so arms and legs keep their length however hands and feet are placed. In the
    /// last frames of a death it lies on its back along the ground instead, head to the left. The torso and what is worn
    /// on it are laid out along the spine (<see cref="Local"/>), so they are drawn the same either way.
    /// </summary>
    private sealed class Body
    {
        /// <summary>Where the light comes from (up and to the left).</summary>
        private static readonly Vector2 Sun = new(-0.6f, -0.8f);

        public Body(Figure f, Pose pose, Build build)
        {
            F = f;
            Pose = pose;
            B = build;
            if (pose.Lying)
                LieDown();
            else
                Stand();
            var spine = Neck - Hip;
            Length = spine.Length();
            Axis = spine / Length;
            // Across the chest: to the right standing (the chest faces the viewer, the sun lights its left); lying on the
            // back, up (the chest towards the sky), the body seen from the side, so only half as deep as it is broad.
            Across = Lying ? new Vector2(0f, -0.55f) : new Vector2(-Axis.Y, Axis.X);
        }

        private void Stand()
        {
            var p = Pose;
            float a = p.Phase * Mathf.Tau;
            bool walk = p.Act == Act.Walk;
            float stride = 4.2f * B.Stride;
            float lift = 3.2f / Mathf.Sqrt(B.Weight);
            // The feet: the near (right) one on the left of the picture, the far (left) one further right and a little
            // further away. Marching, each swings forward (to the right) through the air and pushes back along the ground.
            float nearStep = walk ? stride * Mathf.Cos(a) : p.Far - 13f;
            float farStep = walk ? -stride * Mathf.Cos(a) : p.Near - 18f;
            float nearLift = walk ? Mathf.Max(0f, -Mathf.Sin(a)) * lift : p.FarLift;
            float farLift = walk ? Mathf.Max(0f, Mathf.Sin(a)) * lift : p.NearLift;
            // The body is lowest as a foot strikes the ground and rises as they pass; the heavy sink deeper.
            float bob = walk ? (1f - Mathf.Abs(Mathf.Sin(a))) * 1.1f * B.Weight - 0.3f : p.Bob;
            float breathe = p.Act == Act.Idle ? Mathf.Sin(a) * 0.5f : 0f;
            float lean = p.Lean * 0.8f + (walk ? 0.7f : 0f) + B.Hunch;
            float hipY = 37.4f - B.Leg + 0.3f + bob + p.Sink;
            Hip = new Vector2(15.5f - p.Lean * 0.25f, hipY);
            Neck = new Vector2(Hip.X + lean, hipY - B.Torso - breathe);
            Head = Neck + new Vector2(p.Hurt ? -0.8f : 0.5f, -B.HeadR * 0.92f + (walk ? 0.3f * (1f - Mathf.Abs(Mathf.Sin(a - 0.5f))) : 0f));
            // The arms swing against the legs.
            Swing = walk ? -Mathf.Cos(a) : 0f;
            NearShoulder = Neck + new Vector2(-B.Shoulder * 0.78f, 1.7f + breathe * 0.4f);
            FarShoulder = Neck + new Vector2(B.Shoulder * 0.72f, 1.3f + breathe * 0.4f);
            NearAnkle = new Vector2(Hip.X - B.Waist * 0.55f + nearStep, 37.8f - nearLift);
            FarAnkle = new Vector2(Hip.X + B.Waist * 0.55f + farStep, 37f - farLift);
        }

        /// <summary>
        /// Laid out on its back along the ground, centred in the cell: the head resting on the earth at the left, the far
        /// knee drawn up, the near leg stretched out. In the first lying frame the chest has not quite settled.
        /// </summary>
        private void LieDown()
        {
            Lying = true;
            float span = 2f * B.HeadR + B.Torso + B.Leg * 0.95f + 3f;
            float left = 16f - span / 2f;
            float axisY = 38.3f - B.Shoulder * 0.5f;
            float settle = Pose.Index == 3 ? -0.8f : 0f;
            Neck = new Vector2(left + 2f * B.HeadR, axisY + settle);
            Hip = new Vector2(Neck.X + B.Torso, axisY + 0.5f);
            Head = new Vector2(Neck.X - B.HeadR * 0.85f, 38.5f - B.HeadR);
            NearShoulder = Neck + new Vector2(1.6f, 1.4f);
            FarShoulder = Neck + new Vector2(1.2f, -1.6f);
            NearAnkle = new Vector2(Hip.X + B.Leg * 0.95f, 37.8f);
            FarAnkle = new Vector2(Hip.X + B.Leg * 0.7f, 36.4f);
        }

        public Figure F { get; }
        public Pose Pose { get; }
        public Build B { get; }
        public bool Lying { get; private set; }
        public Vector2 Hip { get; private set; }
        public Vector2 Neck { get; private set; }
        public Vector2 Head { get; private set; }
        public Vector2 NearShoulder { get; private set; }
        public Vector2 FarShoulder { get; private set; }
        public Vector2 NearAnkle { get; private set; }
        public Vector2 FarAnkle { get; private set; }

        /// <summary>Along the spine from the hip to the neck, its length, and across the chest (scaled for depth when lying).</summary>
        public Vector2 Axis { get; }
        public float Length { get; }
        public Vector2 Across { get; }

        /// <summary>The arms' swing on the march: 1 the near arm fully forward, -1 fully back.</summary>
        public float Swing { get; private set; }

        private float ArmSegment => B.Torso * 0.52f;
        private float LegSegment => B.Leg / 2f;

        /// <summary>A point <paramref name="u"/> up the spine from the hip and <paramref name="v"/> across the chest.</summary>
        public Vector2 Local(float u, float v) => Hip + Axis * u + Across * v;

        /// <summary>The point <paramref name="target"/>, or as near it as a limb of <paramref name="length"/> from <paramref name="from"/> reaches.</summary>
        private static Vector2 Reach(Vector2 from, Vector2 target, float length)
        {
            var d = target - from;
            float l = d.Length();
            return l <= length || l < 1e-4f ? target : from + d / l * length;
        }

        /// <summary>The joint between two segments from <paramref name="a"/> to <paramref name="b"/>, bending towards <paramref name="prefer"/>.</summary>
        private static Vector2 Joint(Vector2 a, Vector2 b, float l1, float l2, Vector2 prefer)
        {
            var d = b - a;
            float dist = Mathf.Clamp(d.Length(), 0.01f, l1 + l2 - 0.01f);
            var dir = d.Length() > 1e-4f ? d.Normalized() : Vector2.Down;
            float along = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float across = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
            var perp = new Vector2(-dir.Y, dir.X);
            return perp.Dot(prefer) >= 0f ? a + dir * along + perp * across : a + dir * along - perp * across;
        }

        /// <summary>Lit like a cylinder up the spine: bright on the side the sun is on, falling into shadow across.</summary>
        public Func<float, float, float> AcrossLight(float halfWidth, float bias)
        {
            var across = Across.Normalized();
            float depth = Across.Length();
            bool flip = across.Dot(Sun) > 0f;
            return (x, y) =>
            {
                float s = ((new Vector2(x, y) - Hip).Dot(across) / (halfWidth * depth) + 1f) / 2f;
                if (flip)
                    s = 1f - s;
                return 1.3f - 3.2f * Mathf.Abs(s - 0.3f) + bias;
            };
        }

        public void Polygon(Ramp? r, Func<float, float, float> light, params Vector2[] points)
        {
            var xy = new float[points.Length * 2];
            for (int i = 0; i < points.Length; i++)
            {
                xy[2 * i] = points[i].X;
                xy[2 * i + 1] = points[i].Y;
            }
            F.Poly(r, light, xy);
        }

        public void Line(Vector2 a, Vector2 b, float width, Ramp r, float bias = 0f) => F.Stroke(a.X, a.Y, b.X, b.Y, width, r, bias);

        public void TeamLine(Vector2 a, Vector2 b, float width, float bias = 0f) => F.TeamStroke(a.X, a.Y, b.X, b.Y, width, bias);

        public void Shadow() => F.Shadow(16f, Lying ? 12f : 4f + B.Shoulder);

        /// <summary>A leg: a thigh tapering into the knee, a shin into the ankle, and a boot, its toe forward (up, lying).</summary>
        public void Leg(bool near, Ramp trousers, Ramp boots)
        {
            float bias = near ? 0.1f : -0.7f;
            var hip = Lying ? Hip + new Vector2(0f, near ? 0.9f : -0.9f) : Hip + new Vector2((near ? -0.55f : 0.55f) * B.Waist, near ? 0.6f : 0f);
            var ankle = Reach(hip, near ? NearAnkle : FarAnkle, LegSegment * 2f);
            var knee = Joint(hip, ankle, LegSegment, LegSegment, Lying ? new Vector2(0f, -1f) : new Vector2(1f, 0.2f));
            float w = B.Limb;
            F.Taper(hip.X, hip.Y, knee.X, knee.Y, 4f * w, 3.2f * w, trousers, bias);
            F.Taper(knee.X, knee.Y, ankle.X, ankle.Y, 3.2f * w, 2.6f * w, trousers, bias - 0.2f);
            var toe = Lying ? new Vector2(0f, -1f) : new Vector2(1f, 0.15f).Normalized();
            var sole = Lying ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
            Vector2 Q(float along, float down) => ankle + toe * along + sole * down;
            Polygon(boots, (x, y) => ((new Vector2(x, y) - ankle).Dot(sole) < 0f ? 0.6f : -0.3f) + bias,
                Q(-1.6f, -1.8f), Q(1.4f, -1.8f), Q(1.8f, 0f), Q(3f, 0.7f), Q(3f, 1.7f), Q(-1.8f, 1.7f));
        }

        /// <summary>
        /// The torso's mass seen from the front: square shoulders, the chest, the waist drawing in and the hips widening
        /// again; in <paramref name="r"/> or faction colour.
        /// </summary>
        public void Torso(Ramp? r, float bias = 0f)
        {
            float w = B.Shoulder, wa = B.Waist, t = Length;
            Polygon(r, AcrossLight(w, bias),
                Local(t - 0.2f, -w * 0.95f), Local(t - 0.2f, w * 0.85f), Local(t * 0.6f, w * 0.85f), Local(t * 0.25f, wa * 0.95f),
                Local(-1.2f, wa), Local(-1.2f, -wa * 1.05f), Local(t * 0.3f, -wa * 1.0f), Local(t * 0.65f, -w * 0.95f));
        }

        /// <summary>A breastplate: a ridge down the middle catching the light, a brass neckline, rivets at the flanks, two lames below.</summary>
        public void Cuirass(Ramp metal)
        {
            float w = B.Shoulder * 0.8f, wa = B.Waist * 0.88f, t = Length;
            Polygon(metal, AcrossLight(w, 0.6f),
                Local(t - 1f, -w * 0.85f), Local(t - 1f, w * 0.8f), Local(t * 0.55f, w * 0.9f), Local(0.6f, wa * 0.9f),
                Local(0.6f, -wa * 0.95f), Local(t * 0.55f, -w * 0.95f));
            Line(Local(t - 1.6f, -w * 0.15f), Local(1.2f, -wa * 0.1f), 0.9f, metal, 1.7f);
            Line(Local(t - 1f, -w * 0.6f), Local(t - 1f, w * 0.55f), 1f, BrassR, 0.3f);
            var rivet = Local(t * 0.55f, -w * 0.75f);
            F.Dot(rivet.X, rivet.Y, BrassR.Light);
            Line(Local(-0.3f, -wa * 0.95f), Local(-0.3f, wa * 0.9f), 1.4f, metal, 0.5f);
            Line(Local(-1.8f, -wa * 0.9f), Local(-1.8f, wa * 0.85f), 1.4f, metal, -0.3f);
        }

        public void Belt()
        {
            float wa = B.Waist;
            Line(Local(0.4f, -wa * 1.02f), Local(0.4f, wa * 0.98f), 1.9f, LeatherR, -0.5f);
            var buckle = Local(0.4f, -wa * 0.1f);
            F.Ball(buckle.X, buckle.Y, 1.2f, 1.1f, BrassR, 0.6f);
        }

        /// <summary>
        /// A tabard's skirt in the faction's colours hanging from the belt over the thighs, its hem torn, swinging with
        /// the stride; <paramref name="strips"/> hangs it as two separate strips (front and side) instead.
        /// </summary>
        public void Tabard(float length, bool strips = false)
        {
            float wa = B.Waist;
            float sway = Pose.Act == Act.Walk ? Swing * 0.8f : 0f;
            Vector2 Down(float v, float l) => Local(-0.2f - l, v) + new Vector2(sway * l / length, 0f);
            var shade = AcrossLight(wa, -0.5f);
            if (strips)
            {
                foreach (float v in new[] { -wa * 0.35f, wa * 0.55f })
                    Polygon(null, shade, Local(-0.2f, v - 1.4f), Local(-0.2f, v + 1.4f), Down(v + 1.5f, length), Down(v, length - 1f),
                        Down(v - 1.5f, length));
                return;
            }
            Polygon(null, shade, Local(-0.2f, -wa * 0.9f), Local(-0.2f, wa * 0.85f), Down(wa * 0.95f, length - 1f), Down(wa * 0.55f, length),
                Down(wa * 0.2f, length - 1.6f), Down(-wa * 0.2f, length + 0.4f), Down(-wa * 0.6f, length - 1.2f), Down(-wa * 1f, length));
        }

        /// <summary>A scarf in the faction's colours wound round the neck, an end hanging over the chest.</summary>
        public void Scarf()
        {
            var n = Neck + new Vector2(0f, 0.6f);
            F.TeamBall(n.X, n.Y, B.Shoulder * 0.55f, 1.6f, 0.4f);
            float sway = Pose.Act == Act.Walk ? Swing * 0.6f : 0f;
            F.TeamStroke(n.X - B.Shoulder * 0.3f, n.Y + 0.8f, n.X - B.Shoulder * 0.45f + sway, n.Y + 5.5f, 1.8f, -0.1f);
        }

        /// <summary>A shoulder guard of lames, brass-rimmed, over a shoulder; with a purity seal hanging from it if <paramref name="seal"/>.</summary>
        public void Pauldron(bool near, Ramp metal, bool seal = false)
        {
            float size = B.Shoulder * 0.5f;
            var c = (near ? NearShoulder : FarShoulder) + new Vector2(near ? -0.3f : 0.3f, -0.2f);
            F.Ball(c.X, c.Y, size, size * 0.8f, metal, near ? 0.6f : 0.1f);
            F.Stroke(c.X - size + 0.4f, c.Y + size * 0.6f, c.X + size - 0.4f, c.Y + size * 0.6f, 1.1f, BrassR, near ? 0.2f : -0.4f);
            if (!seal || Lying)
                return;
            F.Stroke(c.X + 0.4f, c.Y + size, c.X + 0.8f, c.Y + size + 3.5f, 1.8f, ParchmentR, 0.2f);
            F.Ball(c.X + 0.3f, c.Y + size * 0.7f, 1.5f, 1.4f, WaxR, 0.4f);
        }

        /// <summary>The head: a face lost in the shadow of what is drawn over it.</summary>
        public void Face()
        {
            float r = B.HeadR;
            F.Ball(Head.X, Head.Y, r, r * 1.05f, FaceShadowR, -0.6f);
        }

        /// <summary>
        /// An arm from a shoulder towards <paramref name="target"/>: an upper arm tapering into the elbow, a forearm into a
        /// gauntlet (the forearm in <paramref name="forearm"/>, a vambrace, if given; the sleeves in the faction's colour,
        /// darkened, when <paramref name="sleeve"/> is null); the elbow bends outwards and down. Returns where the hand ends
        /// up (as near the target as the arm reaches).
        /// </summary>
        public Vector2 Arm(bool near, Vector2 target, Ramp? sleeve, Ramp? forearm = null)
        {
            var shoulder = near ? NearShoulder : FarShoulder;
            float bias = near ? 0.1f : -0.6f;
            var hand = Reach(shoulder, target, ArmSegment * 2f);
            var prefer = Lying ? new Vector2(0f, -1f) : new Vector2(near ? -1f : 1f, 0.4f);
            var elbow = Joint(shoulder, hand, ArmSegment, ArmSegment, prefer);
            float w = B.Limb;
            if (sleeve is { } cloth)
            {
                F.Taper(shoulder.X, shoulder.Y, elbow.X, elbow.Y, 3.4f * w, 2.8f * w, cloth, bias);
                F.Taper(elbow.X, elbow.Y, hand.X, hand.Y, 2.8f * w, 2.3f * w, forearm ?? cloth, bias - 0.1f + (forearm != null ? 0.4f : 0f));
            }
            else
            {
                F.TeamStroke(shoulder.X, shoulder.Y, elbow.X, elbow.Y, 3.1f * w, bias - 0.7f);
                F.TeamStroke(elbow.X, elbow.Y, hand.X, hand.Y, 2.6f * w, bias - 0.8f);
            }
            F.Ball(hand.X, hand.Y, 1.6f * w, 1.5f * w, LeatherR, bias + 0.3f);
            return hand;
        }

        /// <summary>
        /// Where an empty hand is: standing, hanging by the side and swinging against the stride; lying, the near one fallen
        /// by the hip, the far one flung back past the head.
        /// </summary>
        public Vector2 HangingHand(bool near) => Lying
            ? (near ? Hip + new Vector2(1f, 2.6f) : FarShoulder + new Vector2(-5f, -2.5f))
            : (near ? NearShoulder + new Vector2(-1.2f - Swing * 1.4f, ArmSegment * 1.8f)
                    : FarShoulder + new Vector2(1.2f + Swing * 1.4f, ArmSegment * 1.8f));

        /// <summary>A dark cloak hanging behind the shoulders, its ragged hem showing round the legs (lying, spread on the ground under the body).</summary>
        public void Cloak(float length)
        {
            if (Lying)
            {
                F.Poly(CloakR, (x, y) => -0.6f,
                    Neck.X - 1.5f, 38.9f, Neck.X + 0.5f, 36.3f, Hip.X + 2f, 36.5f, Hip.X + 5.5f, 38.6f, Hip.X + 4f, 38.2f, Hip.X + 3f, 39f);
                return;
            }
            float wave = Mathf.Sin(Pose.Phase * Mathf.Tau) * 0.8f;
            float stream = Pose.Act == Act.Walk ? 1.5f : 0f;
            float w = B.Shoulder;
            var top = Neck + new Vector2(-0.5f, 0.8f);
            float hem = Mathf.Min(38.5f, top.Y + length);
            F.Poly(CloakR, (x, y) => -0.6f + (x - top.X + w) * 0.08f,
                top.X - w * 0.95f, top.Y + 0.5f, top.X + w * 0.8f, top.Y, top.X + w * 0.95f - stream, hem - 1f + wave,
                top.X + w * 0.5f - stream, hem, top.X - w * 0.1f - stream, hem - 1.5f, top.X - w * 0.6f - stream, hem + wave, top.X - w * 1.25f - stream, hem - 1f);
        }

        /// <summary>A plume in the faction's colours from (x, y), curling back and stirring with the cycle.</summary>
        public void Plume(float x, float y, float length = 6f)
        {
            float stir = Lying ? 0f : Mathf.Sin(Pose.Phase * Mathf.Tau) * 0.8f + (Pose.Act == Act.Walk ? 0.8f : 0f);
            F.TeamStroke(x, y, x - length * 0.45f, y - 1.6f, 2.4f, 0.6f);
            F.TeamStroke(x - length * 0.45f, y - 1.6f, x - length, y + 0.8f + stir, 1.8f, 0f);
        }

        /// <summary>
        /// Where a helmet that fell off lies: on the ground in front of the body, the given share of the head's radius
        /// (from its centre to its rim) above the ground.
        /// </summary>
        public Vector2 DroppedHelm(float rim) => new(Hip.X - B.Torso * 0.2f, 39f - rim * B.HeadR);

        /// <summary>An armet: a rounded helm of dark steel, its visor drawn to a point in front with an eye slit, a brass rivet at the hinge.</summary>
        public void Armet(Ramp metal, Vector2? at = null)
        {
            var c = at ?? Head;
            float r = B.HeadR;
            F.Ball(c.X, c.Y - r * 0.05f, r * 1.05f, r * 1.12f, metal, 0.4f);
            // The visor: a snout to the front-right, the slit across it.
            F.Poly(metal, (x, y) => 0.9f - (y - c.Y) * 0.3f,
                c.X - r * 0.1f, c.Y - r * 0.35f, c.X + r * 1.05f, c.Y - r * 0.25f, c.X + r * 1.3f, c.Y + r * 0.35f, c.X + r * 0.7f, c.Y + r * 0.95f,
                c.X - r * 0.15f, c.Y + r * 0.8f);
            F.Stroke(c.X + r * 0.05f, c.Y - r * 0.05f, c.X + r * 1.1f, c.Y, 1.1f, BlackSteelR, -2.2f);
            F.Stroke(c.X - r * 0.5f, c.Y - r * 1.05f, c.X + r * 0.55f, c.Y - r * 1.05f, 0.9f, metal, 1.6f); // the comb's gleam
            F.Dot(c.X - r * 0.3f, c.Y + r * 0.05f, BrassR.Light);
        }

        /// <summary>A kettle hat (chapel de fer): a round crown over a wide brim that throws the face into shadow.</summary>
        public void KettleHat(Ramp metal, Vector2? at = null)
        {
            var c = at ?? Head;
            float r = B.HeadR;
            F.Ball(c.X, c.Y - r * 0.6f, r * 1f, r * 0.85f, metal, 0.6f);
            F.Ball(c.X + r * 0.15f, c.Y - r * 0.05f, r * 1.75f, r * 0.42f, metal, 0.1f, 1f);
            F.Stroke(c.X - r * 1.4f, c.Y + r * 0.2f, c.X + r * 1.7f, c.Y + r * 0.2f, 0.9f, BlackSteelR, -1.5f);
        }

        /// <summary>A great helm: a flat-topped steel bucket, an eye slit and breaths in front, brass at the brow.</summary>
        public void GreatHelm(Ramp metal, Vector2? at = null)
        {
            var c = at ?? Head;
            float r = B.HeadR;
            F.Poly(metal, (x, y) => 1.4f - (x - c.X + r) * 0.22f,
                c.X - r * 1.05f, c.Y + r * 1.15f, c.X - r * 1.1f, c.Y - r * 1.05f, c.X - r * 0.7f, c.Y - r * 1.3f, c.X + r * 0.85f, c.Y - r * 1.3f,
                c.X + r * 1.2f, c.Y - r * 1.05f, c.X + r * 1.25f, c.Y + r * 1.15f);
            F.Stroke(c.X - r * 0.35f, c.Y - r * 0.15f, c.X + r * 1.2f, c.Y - r * 0.15f, 1.3f, BlackSteelR, -2.2f);
            F.Stroke(c.X - r * 1.05f, c.Y - r * 0.6f, c.X + r * 1.2f, c.Y - r * 0.6f, 0.9f, BrassR, 0.2f);
            for (int i = 0; i < 3; i++)
                F.Dot(c.X + r * (0.35f + i * 0.28f), c.Y + r * 0.5f, BlackSteelR.Deep);
        }

        /// <summary>
        /// A deep hood with its point falling behind, the face a hollow of shadow at the front; lying, bunched round the
        /// head with the face turned up. In <paramref name="cloth"/>, or the faction's colour (darkened by <paramref name="dim"/>).
        /// </summary>
        public void Hood(Ramp? cloth, float dim = 0f)
        {
            var c = Head;
            float r = B.HeadR;
            if (Lying)
            {
                if (cloth is { } lying)
                    F.Ball(c.X - 0.4f, c.Y, r * 1.3f, r * 1.1f, lying, 0.3f);
                else
                    F.TeamBall(c.X - 0.4f, c.Y, r * 1.3f, r * 1.1f, 0.3f - dim);
                F.Ball(c.X + r * 0.45f, c.Y - r * 0.45f, r * 0.6f, r * 0.55f, FaceShadowR, -0.8f);
                return;
            }
            Func<float, float, float> light = (x, y) => 1.2f - (x - c.X + r) * 0.18f - (y - c.Y + r) * 0.08f - dim;
            var hood = new[]
            {
                c.X - r * 1.25f, c.Y + r * 1.15f, c.X - r * 1.3f, c.Y - r * 0.2f, c.X - r * 0.85f, c.Y - r * 1.1f, c.X + r * 0.15f, c.Y - r * 1.4f,
                c.X + r * 1.15f, c.Y - r * 1f, c.X + r * 1.4f, c.Y - r * 0.1f, c.X + r * 1.25f, c.Y + r * 1.15f,
            };
            F.Poly(cloth, light, hood);
            F.Poly(cloth, (x, y) => -0.3f - dim, c.X - r * 0.9f, c.Y - r * 0.9f, c.X - r * 2.2f, c.Y + r * 0.7f, c.X - r * 1.1f, c.Y + r * 0.3f);
            // The hollow where the face is, turned to the front-right.
            F.Ball(c.X + r * 0.35f, c.Y + r * 0.2f, r * 0.68f, r * 0.82f, FaceShadowR, -1.2f);
        }
    }

    // ---- The soldiers ---------------------------------------------------------------------------------------------

    /// <summary>A kite shield on the far arm: an iron rim, the field in the faction's colours, a gilded lily on it.</summary>
    private static void KiteShield(Figure f, Vector2 s, float scale = 1f)
    {
        float w = 4.6f * scale, h = 12f * scale;
        f.Poly(IronR, (x, y) => 0.3f, s.X - w, s.Y - h * 0.4f, s.X - w * 0.5f, s.Y - h * 0.5f, s.X + w * 0.5f, s.Y - h * 0.5f,
            s.X + w, s.Y - h * 0.4f, s.X + w * 0.85f, s.Y + h * 0.1f, s.X, s.Y + h * 0.5f, s.X - w * 0.85f, s.Y + h * 0.1f);
        float iw = w - 1f, ih = h - 2f;
        f.Poly(null, (x, y) => 1f - (x - s.X + iw) * 0.18f - (y - s.Y + ih * 0.4f) * 0.06f,
            s.X - iw, s.Y - ih * 0.4f, s.X - iw * 0.5f, s.Y - ih * 0.5f, s.X + iw * 0.5f, s.Y - ih * 0.5f, s.X + iw, s.Y - ih * 0.4f,
            s.X + iw * 0.85f, s.Y + ih * 0.1f, s.X, s.Y + ih * 0.5f, s.X - iw * 0.85f, s.Y + ih * 0.1f);
        // The lily: a stem and two curling petals.
        f.Stroke(s.X, s.Y - ih * 0.3f, s.X, s.Y + ih * 0.15f, 1.2f, GoldR, 0.6f);
        f.Stroke(s.X, s.Y - ih * 0.05f, s.X - 1.6f * scale, s.Y - ih * 0.25f, 1f, GoldR, 0.4f);
        f.Stroke(s.X, s.Y - ih * 0.05f, s.X + 1.6f * scale, s.Y - ih * 0.25f, 1f, GoldR, 0f);
        f.Stroke(s.X - 1.2f * scale, s.Y + ih * 0.05f, s.X + 1.2f * scale, s.Y + ih * 0.05f, 0.9f, GoldR, 0f);
    }

    /// <summary>
    /// The man-at-arms: an armet with a pointed visor, a scarf in his colours, a breastplate over a dark doublet, a
    /// tabard in his colours with a torn hem, a dark cloak, a kite shield with a gilded lily on his left arm and a sword
    /// in his right that cleaves an arc of light. Struck down, he lies on his back on his cloak, sword and shield fallen,
    /// the armet rolled off.
    /// </summary>
    private static void Infantry(Figure f, Pose pose)
    {
        var b = new Body(f, pose, Trooper);
        b.Shadow();
        if (b.Lying)
        {
            Sword(f, 2f, 38.6f, 14f, 38.2f);
            b.Cloak(0f);
            b.Arm(false, b.HangingHand(false), DoubletR, PlateR);
            b.Leg(false, TrousersR, BootR);
            b.Torso(DoubletR);
            b.Cuirass(PlateR);
            b.Belt();
            b.Leg(true, TrousersR, BootR);
            b.Face();
            b.Armet(BlackSteelR, b.DroppedHelm(1.1f));
            b.Arm(true, b.HangingHand(true), DoubletR, PlateR);
            FlatShield(f, b.Hip.X + 3f);
            return;
        }
        b.Cloak(15f);
        // The sword in the near (right) hand: carried low and forward, raised over the shoulder to wind up, cutting down
        // and across to the front.
        Vector2 hand, tip;
        if (pose.Windup)
        {
            hand = b.Head + new Vector2(-3f - (pose.Index == 1 ? 1.5f : 0f), -1.5f);
            tip = hand + new Vector2(-6f, -7f + (pose.Index == 1 ? 2f : 0f));
        }
        else if (pose.Strike)
        {
            hand = b.Neck + new Vector2(5f, pose.Follow ? 7f : 4f);
            tip = hand + (pose.Follow ? new Vector2(6f, 7f) : new Vector2(10f, 3f));
        }
        else if (pose.Hurt)
        {
            hand = b.NearShoulder + new Vector2(-2.5f, 5f);
            tip = hand + new Vector2(-4f, 8f);
        }
        else
        {
            hand = b.HangingHand(true) + new Vector2(0.5f, -1f);
            tip = hand + new Vector2(3.5f, 8.5f);
        }
        b.Leg(false, TrousersR, BootR);
        b.Leg(true, TrousersR, BootR);
        b.Torso(DoubletR);
        b.Cuirass(PlateR);
        b.Belt();
        b.Tabard(7f);
        b.Pauldron(false, BlackSteelR);
        b.Face();
        b.Armet(BlackSteelR);
        b.Scarf();
        // The shield on the far (left) arm, held before him.
        var shieldHand = b.Arm(false, b.FarShoulder + new Vector2(2.5f, 5.5f - (pose.Hurt ? 1.5f : 0f)), DoubletR, PlateR);
        KiteShield(f, shieldHand + new Vector2(1.5f, 1f));
        if (pose.Strike && !pose.Follow)
            f.Smear(b.Neck.X, b.Neck.Y + 2f, 11f, -100f, 30f, 3.5f, SteelSmear);
        Sword(f, hand.X, hand.Y, tip.X, tip.Y);
        b.Arm(true, hand, DoubletR, PlateR);
        b.Pauldron(true, BlackSteelR);
    }

    /// <summary>A shield fallen face up on the ground, seen edge-on: a sliver of iron, the faction's colours, a glint of gold.</summary>
    private static void FlatShield(Figure f, float x)
    {
        f.Poly(IronR, (px, py) => 0.4f, x, 37.4f, x + 9f, 37.2f, x + 11f, 38.4f, x + 9f, 39.4f, x, 39.4f);
        f.Poly(null, (px, py) => 0.6f, x + 0.8f, 37.9f, x + 8.6f, 37.8f, x + 10f, 38.4f, x + 8.6f, 39f, x + 0.8f, 39f);
        f.Dot(x + 4.5f, 38.2f, GoldR.Light, 1.5f, 1f);
    }

    /// <summary>
    /// A huntsman-archer, lean and stooping, in a deep hood of his colours and a leather jack, a dark cloak, a quiver of
    /// arrows over his shoulder and a longbow in his left hand; drawing, he holds the bow out to the front and pulls the
    /// string to his cheek, and the loosed arrow leaves a streak. Struck down, he lies on his back, the bow dropped beside
    /// him, arrows spilled.
    /// </summary>
    private static void Archer(Figure f, Pose pose)
    {
        var b = new Body(f, pose, Hunter);
        b.Shadow();
        if (b.Lying)
        {
            f.Stroke(3f, 37.4f, 15f, 38.6f, 1.6f, DarkWoodR, 0.5f);
            f.Stroke(15f, 38.6f, 27f, 37.4f, 1.6f, DarkWoodR, 0.5f);
            f.Stroke(3f, 37.2f, 27f, 37.2f, 0.8f, RopeR);
            foreach (var (x0, y0, x1, y1) in new[] { (b.Hip.X - 2f, 38.6f, b.Hip.X + 5f, 37.6f), (b.Hip.X, 39f, b.Hip.X + 6f, 39f) })
            {
                f.Stroke(x0, y0, x1, y1, 0.9f, WoodR, 0.5f);
                f.Dot(x0 - 1f, y0 - 0.5f, FeatherR.Base, 1.5f, 1f);
            }
            b.Cloak(0f);
            b.Arm(false, b.HangingHand(false), LeatherR);
            b.Leg(false, TrousersR, BootR);
            b.Torso(LeatherR, 0.2f);
            b.Belt();
            b.Leg(true, TrousersR, BootR);
            b.Hood(null, 0.5f);
            b.Arm(true, b.HangingHand(true), LeatherR);
            return;
        }
        b.Cloak(14f);
        // The quiver over the back, arrows' fletchings showing above the near shoulder.
        var q0 = b.Neck + new Vector2(-4.5f, -2.5f);
        var q1 = b.Hip + new Vector2(-1.5f, -2f);
        f.Taper(q0.X, q0.Y, q1.X, q1.Y, 3.4f, 3f, LeatherR, -0.5f);
        for (int i = 0; i < 3; i++)
            f.Stroke(q0.X - 0.8f + i * 1f, q0.Y - 2.5f + (i % 2) * 0.6f, q0.X - 0.6f + i * 1f, q0.Y + 0.5f, 1f, FeatherR, 0.3f - i * 0.4f);
        float pull = pose.Windup && pose.Index == 1 ? 2f : 0f;
        Vector2 bowHand, drawHand;
        if (pose.Windup || pose.Strike)
        {
            bowHand = b.FarShoulder + new Vector2(8f, -0.5f);
            drawHand = pose.Windup ? b.Head + new Vector2(-1f - pull, 2.6f) : b.Head + new Vector2(-4.5f, 2f);
        }
        else
        {
            bowHand = b.HangingHand(false) + new Vector2(0.5f, -1f);
            drawHand = b.HangingHand(true);
        }
        b.Leg(false, TrousersR, BootR);
        b.Leg(true, TrousersR, BootR);
        b.Torso(LeatherR, 0.2f);
        // Studs and a buckled strap across the jack.
        b.Line(b.NearShoulder + new Vector2(1f, 0.5f), b.Local(1.2f, b.B.Waist * 0.8f), 1.2f, DarkWoodR, 0f);
        b.Belt();
        b.Face();
        b.Hood(null, 0.5f);
        // The longbow in the far (left) hand: vertical at rest, held out to the front when drawn.
        float half = pose.Windup || pose.Strike ? 10f : 9f;
        float bend = pose.Windup ? 4.5f + pull * 0.5f : pose.Strike ? 3.5f : 3f;
        var arm = b.Arm(false, bowHand, LeatherR);
        Bow(f, arm.X - 0.5f, arm.Y - half, arm.X - 0.5f, arm.Y + half, bend);
        if (pose.Windup)
        {
            f.Stroke(arm.X - 0.5f, arm.Y - half, drawHand.X, drawHand.Y, 0.8f, RopeR);
            f.Stroke(drawHand.X, drawHand.Y, arm.X - 0.5f, arm.Y + half, 0.8f, RopeR);
            f.Stroke(drawHand.X, drawHand.Y, arm.X + 5f, arm.Y, 1.1f, WoodR, 0.6f);
            f.Dot(arm.X + 4.5f, arm.Y - 0.5f, PlateR.Glint, 2f, 1f);
        }
        else
        {
            float quiver = pose.Strike ? (pose.Follow ? 0.6f : -0.6f) : 0f;
            f.Stroke(arm.X - 1f, arm.Y - half, arm.X - 1f + quiver, arm.Y, 0.8f, RopeR);
            f.Stroke(arm.X - 1f + quiver, arm.Y, arm.X - 1f, arm.Y + half, 0.8f, RopeR);
            if (pose.Strike && !pose.Follow)
                f.Streak(arm.X, arm.Y, 31f, arm.Y - 0.5f, 2f, SteelSmear);
        }
        b.Arm(true, drawHand, LeatherR);
    }

    /// <summary>
    /// A halberdier of an elite company, broad and heavy in full blackened plate: a great helm with a brass brow, strips
    /// of a tabard in his colours hanging from the belt, a pauldron with a purity seal, a dark cloak, and a halberd taller
    /// than himself — held upright, drawn back to wind up, thrust forward with a streak. Struck down, he lies on his back,
    /// the halberd fallen beside him, the helm rolled off.
    /// </summary>
    private static void Heavy(Figure f, Pose pose)
    {
        var b = new Body(f, pose, Knight);
        b.Shadow();
        if (b.Lying)
        {
            Halberd(f, 1.5f, 38.4f, 27f, 37.6f);
            b.Cloak(0f);
            b.Arm(false, b.HangingHand(false), BlackSteelR);
            b.Leg(false, BlackSteelR, BlackSteelR);
            b.Torso(BlackSteelR);
            b.Cuirass(PlateR);
            b.Belt();
            b.Leg(true, BlackSteelR, BlackSteelR);
            b.Face();
            b.GreatHelm(BlackSteelR, b.DroppedHelm(1.15f));
            b.Arm(true, b.HangingHand(true), BlackSteelR);
            return;
        }
        b.Cloak(16f);
        Vector2 hand, butt, tip;
        if (pose.Strike)
        {
            float reach = pose.Follow ? 2f : 0f;
            hand = b.Neck + new Vector2(4f + reach, 4f);
            butt = hand + new Vector2(-10f, 3f);
            tip = hand + new Vector2(10f, -2.5f);
        }
        else if (pose.Windup)
        {
            float back = pose.Index == 1 ? 2.5f : 0.5f;
            hand = b.NearShoulder + new Vector2(0.5f - back, 4.5f);
            butt = hand + new Vector2(-8f, 3f);
            tip = hand + new Vector2(8f, -4.5f);
        }
        else if (pose.Hurt)
        {
            hand = b.NearShoulder + new Vector2(-2f, 6f);
            butt = new Vector2(hand.X + 3f, 38f);
            tip = hand + new Vector2(-3f, -15f);
        }
        else
        {
            float rock = pose.Act == Act.Walk ? b.Swing * 0.8f : 0f;
            hand = b.NearShoulder + new Vector2(-1.5f, 6f);
            butt = new Vector2(hand.X - rock, 38.5f);
            tip = new Vector2(hand.X + rock, hand.Y - 16f);
        }
        b.Arm(false, pose.Strike || pose.Windup ? hand + new Vector2(5f, 1f) : b.HangingHand(false), BlackSteelR, PlateR);
        b.Leg(false, BlackSteelR, BlackSteelR);
        b.Leg(true, BlackSteelR, BlackSteelR);
        b.Torso(BlackSteelR);
        b.Cuirass(PlateR);
        b.Belt();
        b.Tabard(8f, strips: true);
        b.Pauldron(false, BlackSteelR);
        b.Face();
        b.GreatHelm(PlateR);
        Halberd(f, butt.X, butt.Y, tip.X, tip.Y);
        if (pose.Strike && !pose.Follow)
            f.Streak(butt.X, butt.Y - 2f, tip.X + 4f, tip.Y - 1f, 3f, SteelSmear);
        b.Arm(true, hand, BlackSteelR, PlateR);
        b.Pauldron(true, BlackSteelR, seal: true);
    }

    /// <summary>
    /// A crossbowman, short and stocky, in a kettle hat and a quilted gambeson of his colours (darkened), a great pavise
    /// slung on his back; he carries the crossbow across his body at the hip, shoulders it to aim, and rocks back as it
    /// looses, the bolt streaking away. Struck down, he lies on his back across his pavise, the crossbow dropped at his
    /// feet, the hat rolled off.
    /// </summary>
    private static void Crossbowman(Figure f, Pose pose)
    {
        var b = new Body(f, pose, Stocky);
        b.Shadow();
        if (b.Lying)
        {
            f.Poly(PaviseR, (x, y) => 0.3f, b.Neck.X - 3f, 39f, b.Neck.X - 2f, 37.4f, b.Hip.X + 3f, 37.4f, b.Hip.X + 4f, 39f);
            float cx = b.Hip.X + 4f;
            f.Stroke(cx, 38.7f, cx + 9f, 38.3f, 2f, DarkWoodR, 0.4f);
            f.Stroke(cx + 8f, 36.6f, cx + 8.5f, 39.4f, 1.6f, IronR, 0.5f);
            b.Arm(false, b.HangingHand(false), null);
            b.Leg(false, TrousersR, BootR);
            b.Torso(null, -0.7f);
            b.Belt();
            b.Leg(true, TrousersR, BootR);
            b.Face();
            b.KettleHat(PlateR, b.DroppedHelm(0.5f));
            b.Arm(true, b.HangingHand(true), null);
            return;
        }
        // The pavise on the back: a tall wooden board, ridged down the middle, iron-capped, standing up over his shoulder.
        var top = b.Neck + new Vector2(-6f, -7.5f);
        var bottom = b.Hip + new Vector2(-6.5f, 1f);
        f.Poly(PaviseR, (x, y) => 0.6f - (x - top.X + 4f) * 0.15f,
            top.X - 4f, top.Y + 0.3f, top.X + 4.5f, top.Y, bottom.X + 4.5f, bottom.Y, bottom.X - 4f, bottom.Y + 0.3f);
        f.Stroke(top.X + 0.2f, top.Y + 0.5f, bottom.X + 0.2f, bottom.Y - 0.5f, 1.2f, PaviseR, 1.2f);
        f.Stroke(top.X - 4f, top.Y + 0.3f, top.X + 4.5f, top.Y, 1.2f, IronR, 0.5f);
        // The crossbow: across the body at the hip; shouldered to aim; kicking up as it looses.
        bool aiming = pose.Windup || pose.Strike;
        var grip = aiming ? b.NearShoulder + new Vector2(2f, 2f) : b.Hip + new Vector2(-3f, -1.5f);
        var nose = aiming ? grip + new Vector2(13f, pose.Strike && !pose.Follow ? -2.5f : -0.5f) : grip + new Vector2(11f, -3f);
        var dir = (nose - grip).Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        b.Leg(false, TrousersR, BootR);
        b.Leg(true, TrousersR, BootR);
        b.Torso(null, -0.7f);
        // The quilting: rows of stitches across the gambeson.
        for (float u = 1.5f; u < b.Length - 1f; u += 2.2f)
            b.Line(b.Local(u, -b.B.Shoulder * 0.8f), b.Local(u, b.B.Shoulder * 0.75f), 0.5f, CloakR, -1.6f);
        b.Belt();
        b.Face();
        b.KettleHat(PlateR);
        b.Arm(false, grip + dir * 6f, null);
        f.Stroke(grip.X - dir.X * 2f, grip.Y - dir.Y * 2f, nose.X, nose.Y, 2.6f, DarkWoodR, 0.4f);
        var prodA = nose - dir * 1f + side * 5f;
        var prodB = nose - dir * 1f - side * 4f;
        f.Stroke(prodA.X, prodA.Y, prodB.X, prodB.Y, 2f, IronR, 0.5f);
        if (pose.Windup)
        {
            f.Stroke(prodA.X, prodA.Y, grip.X + dir.X * 2f, grip.Y + dir.Y * 2f, 0.8f, RopeR);
            f.Stroke(grip.X + dir.X * 2f, grip.Y + dir.Y * 2f, prodB.X, prodB.Y, 0.8f, RopeR);
            f.Stroke(grip.X + dir.X * 2f, grip.Y + dir.Y * 2f - 0.5f, nose.X + dir.X * 2f, nose.Y + dir.Y * 2f - 0.5f, 1.1f, WoodR, 0.7f);
        }
        else
        {
            var mid = nose - dir * 2.2f;
            f.Stroke(prodA.X - dir.X * 1.2f, prodA.Y - dir.Y * 1.2f, prodB.X - dir.X * 1.2f, prodB.Y - dir.Y * 1.2f, 0.8f, RopeR);
            if (pose.Strike && !pose.Follow)
                f.Streak(mid.X, mid.Y, 31f, mid.Y - 1.5f, 2f, SteelSmear);
        }
        b.Arm(true, grip, null);
    }

    /// <summary>A censer on its chain from <paramref name="hand"/>, swinging by <paramref name="swing"/>, smoke curling from it.</summary>
    private static void Censer(Figure f, Vector2 hand, float swing, float phase, bool lit)
    {
        var ball = hand + new Vector2(swing * 3f, 6.5f - Mathf.Abs(swing) * 0.8f);
        f.Stroke(hand.X, hand.Y, ball.X, ball.Y - 1.6f, 0.7f, IronR, 0.6f);
        f.Ball(ball.X, ball.Y, 1.9f, 1.8f, BrassR, 0.3f);
        f.Stroke(ball.X - 1.6f, ball.Y, ball.X + 1.6f, ball.Y, 0.6f, BrassR, -1.4f);
        if (lit)
            f.Dot(ball.X - 0.3f, ball.Y + 0.6f, Ember, 1f, 1f);
        // Wisps of smoke rising and drifting.
        for (int i = 0; i < 3; i++)
        {
            float t = (phase + i / 3f) % 1f;
            var puff = ball + new Vector2(-1.5f * t - 0.5f + Mathf.Sin((t + i) * 6f) * 0.8f, -2f - t * 6f);
            f.Dot(puff.X, puff.Y, new Color(0.75f, 0.74f, 0.8f, 0.55f * (1f - t)), 1.5f + t, 1f);
        }
    }

    /// <summary>
    /// A warrior priest in a white habit and hood, stooped, a stole in his colours down the front, a censer swinging on
    /// its chain from his right hand and a staff crowned with a gilded comet in his left; his hem sways as he walks. To
    /// mend he raises the staff high, the comet blazing with holy light, the censer swung out. Struck down, he lies on his
    /// back in his habit, the staff fallen beside him.
    /// </summary>
    private static void Monk(Figure f, Pose pose)
    {
        var b = new Body(f, pose, Priest);
        b.Shadow();
        if (b.Lying)
        {
            f.Stroke(3f, 38.6f, 28f, 38.2f, 1.6f, DarkWoodR);
            Comet(f, 3f, 37.4f, -0.4f);
            b.Arm(false, b.HangingHand(false), HabitR);
            float hemX = b.Hip.X + b.B.Leg * 0.9f;
            f.Poly(HabitR, (x, y) => 1f - (y - 32f) * 0.35f,
                b.Neck.X - 0.5f, 38.8f, b.Neck.X, 38.3f - b.B.Shoulder * 1f, b.Hip.X, 37.8f - b.B.Waist * 0.9f, hemX, 35f, hemX + 0.5f, 38.8f);
            f.Stroke(hemX + 1f, 37.8f, hemX + 1f, 35.6f, 1.8f, BootR, -0.4f);
            f.Stroke(hemX + 1.5f, 38.8f, hemX + 2f, 36.8f, 1.8f, BootR);
            f.TeamStroke(b.Neck.X + 1f, 36f, hemX - 3f, 36.2f, 1.8f, 0.3f);
            b.Belt();
            b.Hood(HabitR);
            b.Arm(true, b.HangingHand(true), HabitR);
            return;
        }
        bool blessing = pose.Windup || pose.Strike;
        var staffHand = blessing ? b.Head + new Vector2(6f, pose.Windup && pose.Index == 0 ? 1f : -2f) : b.FarShoulder + new Vector2(2.5f, 6.5f);
        var censerHand = blessing ? b.NearShoulder + new Vector2(-3.5f, 3f) : b.HangingHand(true) + new Vector2(0.5f, -1.5f);
        // Feet peeking out under the habit.
        foreach (var (ankle, bias) in new[] { (b.FarAnkle, -0.8f), (b.NearAnkle, 0f) })
            f.Stroke(ankle.X - 1f, Mathf.Min(38.4f, ankle.Y + 1.2f), ankle.X + 1.8f, Mathf.Min(38.4f, ankle.Y + 1.2f), 1.8f, BootR, bias);
        // The habit, widening to a hem that swings with the stride.
        float sway = Mathf.Sin(pose.Phase * Mathf.Tau) * 1.2f;
        var n = b.Neck;
        float hem = 38.6f;
        float w = b.B.Shoulder;
        f.Poly(HabitR, b.AcrossLight(w * 1.2f, 0f),
            n.X - w * 0.95f, n.Y + 0.4f, n.X + w * 0.85f, n.Y + 0.3f, b.Hip.X + w * 1.05f + sway, hem, b.Hip.X - w * 1.15f + sway, hem);
        f.Stroke(b.Hip.X - w * 1.1f + sway, hem - 0.5f, b.Hip.X + w + sway, hem - 0.5f, 1.2f, HabitR, -1.3f);
        // The stole: two strips in his colours down the front; the rope belt.
        f.TeamStroke(n.X - 1.6f, n.Y + 1f, b.Hip.X - 1.4f + sway * 0.4f, hem - 3f, 1.8f, 0.3f);
        f.TeamStroke(n.X + 1.4f, n.Y + 1f, b.Hip.X + 1.8f + sway * 0.4f, hem - 3f, 1.8f, -0.3f);
        b.Line(b.Local(0.5f, -b.B.Waist), b.Local(0.5f, b.B.Waist), 1.4f, RopeR, -0.2f);
        b.Hood(HabitR);
        var staffTop = staffHand + new Vector2(0f, blessing ? -10f : -14f);
        var staffFoot = blessing ? staffHand + new Vector2(0f, 8f) : new Vector2(staffHand.X, 38.6f);
        f.Stroke(staffFoot.X, staffFoot.Y, staffTop.X, staffTop.Y, 1.6f, DarkWoodR);
        Comet(f, staffTop.X, staffTop.Y, pose.Strike ? 1.8f : blessing ? 0.8f : 0.3f);
        if (pose.Strike && !pose.Follow)
            f.Smear(staffTop.X, staffTop.Y, 4.5f, 0f, 360f, 1.5f, Holy, 0.7f);
        b.Arm(false, staffHand, HabitR);
        float swing = blessing ? -1f : pose.Act == Act.Walk ? b.Swing * 0.8f : Mathf.Sin(pose.Phase * Mathf.Tau) * 0.3f;
        Censer(f, b.Arm(true, censerHand, HabitR), swing, pose.Phase, lit: true);
    }

    /// <summary>The warlord's horned helm centred at <paramref name="c"/>: blackened mail, curving horns; its slit glowing while he lives.</summary>
    private static void HornedHelm(Figure f, Vector2 c, float r, bool alive)
    {
        f.Ball(c.X, c.Y, r * 1.15f, r * 1.2f, MailR, 0.4f);
        f.Dot(c.X - r * 0.05f, c.Y - r * 0.05f, Eye, 6f, 1.5f);
        if (alive)
        {
            f.Dot(c.X + r * 0.25f, c.Y - r * 0.05f, new Color("ff4a1a"), 1.2f, 1.2f);
            f.Dot(c.X + r * 0.75f, c.Y - r * 0.05f, new Color("ff4a1a"), 1.2f, 1.2f);
        }
        f.Stroke(c.X - r * 0.8f, c.Y - r * 0.7f, c.X - r * 1.7f, c.Y - r * 1.5f, 2f, HornR, 0.3f);
        f.Stroke(c.X - r * 1.7f, c.Y - r * 1.5f, c.X - r * 1.5f, c.Y - r * 2.5f, 1.4f, HornR, 0.5f);
        f.Stroke(c.X + r * 0.8f, c.Y - r * 0.7f, c.X + r * 1.7f, c.Y - r * 1.5f, 2f, HornR, 0.1f);
        f.Stroke(c.X + r * 1.7f, c.Y - r * 1.5f, c.X + r * 1.5f, c.Y - r * 2.5f, 1.4f, HornR, 0.3f);
    }

    /// <summary>
    /// The horde's warlord (on the big sheet, a head and more taller than any man): a hulking, hunched brute in
    /// blackened mail, a horned helm with glowing eyes, a fur mantle, a dark cloak, a rag of his colours at his belt, and a
    /// great axe that cleaves a wide arc. Struck down, he sprawls on his back, the axe fallen, the helm rolled off.
    /// </summary>
    private static void Warlord(Figure f, Pose pose)
    {
        var b = new Body(f, pose, Brute);
        b.Shadow();
        if (b.Lying)
        {
            Axe(f, 2f, 38.4f, 13f, 38f);
            b.Cloak(0f);
            b.Arm(false, b.HangingHand(false), MailR);
            b.Leg(false, MailR, BootR);
            b.Torso(MailR);
            b.Belt();
            b.Leg(true, MailR, BootR);
            b.Face();
            HornedHelm(f, b.DroppedHelm(1.2f), b.B.HeadR, alive: false);
            b.Arm(true, b.HangingHand(true), MailR);
            return;
        }
        b.Cloak(16f);
        Vector2 hand, head;
        if (pose.Windup)
        {
            hand = b.Head + new Vector2(-3f - (pose.Index == 1 ? 1.5f : 0f), -2f);
            head = hand + new Vector2(-7f, -5f);
        }
        else if (pose.Strike)
        {
            hand = b.Neck + new Vector2(5f, pose.Follow ? 8f : 4f);
            head = hand + (pose.Follow ? new Vector2(5f, 10f) : new Vector2(9f, 6f));
        }
        else
        {
            hand = b.NearShoulder + new Vector2(-1.5f, 6.5f);
            head = new Vector2(hand.X + (pose.Act == Act.Walk ? b.Swing * 0.6f : 0f), hand.Y - 15f);
        }
        b.Arm(false, pose.Strike || pose.Windup ? hand + new Vector2(4f, 1.5f) : b.HangingHand(false), MailR);
        b.Leg(false, MailR, BootR);
        b.Leg(true, MailR, BootR);
        b.Torso(MailR);
        for (float u = 1.5f; u < b.Length; u += 2f)
            b.Line(b.Local(u, -b.B.Shoulder * 0.85f), b.Local(u, b.B.Shoulder * 0.8f), 0.6f, MailR, 0.6f);
        b.Belt();
        var rag = b.Local(-0.6f, b.B.Waist * 0.3f);
        f.TeamStroke(rag.X, rag.Y, rag.X + 0.5f, rag.Y + 6f + (pose.Passing ? 0.5f : 0f), 2.6f, -0.2f);
        f.Ball(b.Neck.X, b.Neck.Y + 1.2f, b.B.Shoulder * 1.15f, 2.8f, FurR, 0.3f);
        HornedHelm(f, b.Head, b.B.HeadR, alive: true);
        if (pose.Strike && !pose.Follow)
            f.Smear(b.Neck.X, b.Neck.Y + 2f, 13f, -110f, 45f, 4f, new Color("ff9a6a"));
        var foot = hand - (head - hand).Normalized() * 6f;
        Axe(f, foot.X, foot.Y, head.X, head.Y);
        b.Arm(true, hand, MailR);
    }

    /// <summary>
    /// A cavalryman's horse struck down: on its side, legs stretched out along the ground, its head down; the rider
    /// slumped across its back, one arm hanging; the lance fallen in front.
    /// </summary>
    private static void FallenHorse(Figure f)
    {
        f.Shift = Vector2.Zero;
        f.Shadow(16f, 14f);
        Spear(f, 1f, 39f, 22f, 38.8f);
        f.Stroke(3f, 34.5f, -0.5f, 38.6f, 2.4f, ManeR);
        // The legs, far ones first and darker, stretched out on the ground towards the front.
        foreach (var (hipX, footX, footY) in new[] { (10f, 16f, 37.6f), (21f, 27f, 37.4f) })
        {
            float kneeX = (hipX + footX) / 2f;
            f.Stroke(hipX, 34.5f, kneeX, 36.4f, 2.6f, HorseR, -1f);
            f.Stroke(kneeX, 36.4f, footX, footY, 2f, HorseR, -1f);
            f.Dot(footX, footY - 1f, ManeR.Base, 2f, 2f);
        }
        f.Ball(15f, 33.6f, 11f, 4.4f, HorseR, 0.1f);
        foreach (var (hipX, footX, footY) in new[] { (8f, 15f, 38.6f), (19f, 26f, 38.6f) })
        {
            float kneeX = (hipX + footX) / 2f;
            f.Stroke(hipX, 35.5f, kneeX, 37.4f, 2.6f, HorseR);
            f.Stroke(kneeX, 37.4f, footX, footY, 2f, HorseR);
            f.Dot(footX, footY - 1f, ManeR.Base, 2f, 2f);
        }
        // The caparison draped over it, its hem in the rider's colours.
        f.Poly(CloakR, (x, y) => 0.6f - (y - 29f) * 0.25f, 8f, 29.5f, 20f, 29.3f, 21.5f, 34f, 9f, 34.5f);
        f.TeamStroke(9f, 34.2f, 21.2f, 33.8f, 1.1f, 0f);
        // The neck stretched out, the head on the ground, the chanfron still on it.
        f.Stroke(23f, 32.5f, 27.5f, 35.2f, 4.6f, HorseR, 0.2f);
        f.Poly(HorseR, (x, y) => 1f - (y - 34f) * 0.3f, 26.5f, 33.6f, 31.5f, 34.6f, 31.5f, 37.6f, 27.5f, 38.2f);
        f.Poly(PlateR, (x, y) => 1f, 27f, 33.8f, 31f, 34.6f, 30.6f, 36f, 27.4f, 35.4f);
        f.Stroke(22.5f, 31.5f, 26.5f, 34f, 1.2f, ManeR);
        // The rider slumped across the back: breastplate up, head hanging past the withers, an arm trailing.
        f.Stroke(17f, 29f, 22f, 31.5f, 2.4f, TrousersR, -0.4f);
        f.Stroke(22f, 31.5f, 23.5f, 34f, 2f, BootR);
        f.Poly(PlateR, (x, y) => 1.2f - (y - 26f) * 0.3f, 9.5f, 27.5f, 17.5f, 26.5f, 18f, 30f, 10f, 31f);
        f.Stroke(10f, 27.8f, 17.4f, 26.8f, 0.8f, BrassR);
        f.Ball(7.6f, 30.5f, 3.2f, 3f, FaceShadowR, -0.6f);
        f.Stroke(9f, 30.5f, 6.5f, 35f, 2.4f, PlateR, -0.3f);
        f.Ball(6.4f, 35.5f, 1.4f, 1.3f, LeatherR, 0.2f);
        f.TeamStroke(17f, 27f, 19.5f, 25.6f, 1.6f, 0.2f); // the plume of the fallen burgonet
        f.Poly(PlateR, (x, y) => 1f, 18f, 27.6f, 19f, 25.4f, 21.8f, 25.4f, 22.6f, 27.6f);
    }

    /// <summary>
    /// A griffin struck down: its belly on the earth, wings sprawled, its head down in the dirt; the rider slumped
    /// over its neck.
    /// </summary>
    private static void FallenGriffin(Figure f)
    {
        f.Shift = Vector2.Zero;
        f.Shadow(16f, 14f);
        f.Shift = new Vector2(0f, 7f);
        Wing(f, 13f, 22f, 1.7f, far: true);
        f.Stroke(6f, 28f, 9.5f, 31f, 2.6f, LionR, -0.7f);
        f.Stroke(4f, 26f, -1f, 30.5f, 1.4f, LionR, -0.3f);
        f.Ball(13f, 27f, 9f, 4.4f, LionR, 0.1f);
        f.Poly(CloakR, (x, y) => 0.6f - (y - 22f) * 0.3f, 9f, 23f, 18f, 23f, 19f, 29f, 15f, 30f, 12f, 28f, 8f, 29f);
        f.TeamStroke(8.5f, 28.8f, 12f, 28f, 1.1f, 0f);
        f.TeamStroke(15f, 29.8f, 18.8f, 28.8f, 1.1f, 0f);
        // Neck and head stretched out along the ground, the beak in the dirt, the eye shut.
        f.Stroke(18f, 26f, 23.5f, 29.5f, 5f, FeatherR, 0.2f);
        f.Ball(26f, 29.6f, 3.6f, 2.8f, FeatherR, 0.4f);
        f.Poly(BeakR, (x, y) => 0.8f, 28.5f, 28.4f, 31.5f, 30.2f, 30.5f, 31.6f, 28.5f, 31f);
        f.Stroke(25f, 28.8f, 26.5f, 28.8f, 0.8f, ManeR, -1f);
        // The rider fallen forward over the neck.
        f.Poly(PlateR, (x, y) => 1.2f - (y - 19f) * 0.3f, 11f, 20.5f, 18.5f, 21.5f, 18.5f, 24.5f, 11f, 24f);
        f.Ball(20.5f, 24f, 3f, 2.8f, FaceShadowR, -0.6f);
        f.Poly(PlateR, (x, y) => 1.2f, 18.5f, 23.5f, 19f, 21f, 22f, 20.8f, 23f, 23f);
        f.TeamStroke(19.5f, 21f, 17.5f, 19.5f, 1.6f, 0.4f);
        f.Stroke(19f, 24f, 22f, 27.5f, 2.2f, PlateR, -0.3f);
        Wing(f, 14f, 22f, 1.5f, far: false);
    }

    /// <summary>A ram smashed: its log dropped, an upright snapped, the roof of hides caved in, a wheel come off and lying flat.</summary>
    private static void WreckedRam(Figure f)
    {
        f.Shift = Vector2.Zero;
        f.Shadow(16f, 14f);
        f.Shift = new Vector2(0f, 2f);
        f.Stroke(6f, 34.5f, 24f, 35f, 4f, DarkWoodR, 0.1f);
        f.Poly(IronR, (x, y) => 0.6f, 24f, 32.5f, 28f, 33.5f, 28f, 37f, 24f, 37.5f);
        f.Stroke(5f, 18f, 5f, 33f, 2f, DarkWoodR, 0.3f);
        f.Stroke(15f, 19f, 15f, 33f, 2f, DarkWoodR, 0.3f);
        f.Stroke(25f, 33f, 27f, 25f, 2f, DarkWoodR, -0.2f); // a snapped upright
        f.Stroke(27.5f, 24f, 30f, 21f, 1.6f, DarkWoodR, 0f);
        f.Stroke(3f, 32f, 28f, 33.5f, 2.4f, DarkWoodR, 0f);
        // The roof sagging in towards the broken side.
        f.Poly(LeatherR, (x, y) => 0.4f - (y - 14f) * 0.2f, 2.5f, 21f, 9f, 12f, 18f, 13.5f, 26f, 22f, 29.5f, 27f, 21f, 25f, 14f, 21f);
        for (int i = 0; i < 3; i++)
            f.Stroke(7f + i * 6f, 13f + i * 1f, 5f + i * 7f, 21.5f + i * 1f, 0.8f, LeatherR, -1.2f);
        f.Stroke(22f, 9f, 24.5f, 15f, 1f, DarkWoodR, 0.4f); // the pennon's pole, knocked askew
        Wheel(f, 8f, 34.5f, 4f, 3);
        f.Ball(27f, 37.5f, 4f, 1.4f, IronR, -0.3f);
        f.Ball(27f, 37.3f, 3f, 0.8f, DarkWoodR, 0.2f);
    }

    /// <summary>A trebuchet smashed: a leg of its frame snapped, the arm broken in two, the counterweight fallen, a wheel lying flat.</summary>
    private static void WreckedTrebuchet(Figure f)
    {
        f.Shift = Vector2.Zero;
        f.Shadow(16f, 14f);
        f.Shift = new Vector2(0f, 2f);
        f.Stroke(8f, 31f, 15f, 11f, 2.4f, WoodR);
        f.Stroke(19f, 31f, 17.5f, 21f, 2.4f, WoodR, -0.4f); // snapped
        f.Stroke(17f, 20.5f, 20f, 18.5f, 1.6f, WoodR, -0.6f);
        f.Stroke(10f, 23f, 17f, 23f, 1.6f, DarkWoodR);
        // The arm: a stump still on the pivot, the rest on the ground.
        f.Stroke(15f, 11f, 9f, 18f, 2f, WoodR, 0.3f);
        f.Ball(15f, 11f, 1.4f, 1.4f, IronR, 0.6f);
        f.Stroke(11f, 36.8f, 27f, 36f, 2f, WoodR, 0.2f);
        // The counterweight fallen on its side, still in its brass bands.
        f.Poly(IronR, (x, y) => 0.6f, 22f, 31f, 29f, 31f, 29f, 36f, 22f, 36f);
        f.Stroke(22f, 33f, 29f, 33f, 0.8f, BrassR);
        f.Stroke(2f, 31.5f, 30f, 32.5f, 3f, WoodR, 0.2f);
        Wheel(f, 6f, 34.5f, 4f, 2);
        f.Ball(26f, 37.6f, 4f, 1.4f, IronR, -0.3f);
        f.Ball(26f, 37.4f, 3f, 0.8f, DarkWoodR, 0.2f);
    }

    /// <summary>
    /// A pistolier-knight: a bay at a gallop (its legs reaching and gathering through the cycle, bobbing mid-stride) in a
    /// dark caparison trimmed in the rider's colours; the rider in a breastplate and burgonet with a plume, a lance with
    /// a pennon in his colours — couched back to wind up, thrust level to strike with a streak of speed.
    /// </summary>
    private static void Cavalry(Figure f, Pose pose)
    {
        if (pose.Lying)
        {
            FallenHorse(f);
            return;
        }
        f.Shift = Vector2.Zero;
        f.Shadow(16f, 12f);
        float a = pose.Phase * Mathf.Tau;
        bool galloping = pose.Act == Act.Walk;
        int bob = galloping ? -(int)Mathf.Round(Mathf.Abs(Mathf.Sin(a)) * 1.5f) : pose.Hurt ? 1 : 0;
        float Reach(float offset) => galloping ? Mathf.Cos(a + offset) * 3.5f : 0f;
        float Lift(float offset) => galloping ? Mathf.Max(0f, Mathf.Sin(a + offset)) * 2.5f : 0f;
        float rear = pose.Windup ? -1f : 0f;
        foreach (var (hipX, offset, near) in new[] { (8f, 0.6f, false), (22f, 2.2f, false), (8f, 0f, true), (22f, 1.6f, true) })
        {
            float x = (near ? hipX - 2f : hipX + 1f) + Reach(offset);
            float up = Lift(offset) + (hipX > 15f && pose.Windup ? 3f : 0f);
            float bias = near ? 0f : -1f;
            f.Stroke(hipX, 27f + bob, (hipX + x) / 2f + up * 0.5f, 32f + bob - up * 0.5f, 2.6f, HorseR, bias);
            f.Stroke((hipX + x) / 2f + up * 0.5f, 32f + bob - up * 0.5f, x, 37f - up, 2f, HorseR, bias);
            f.Dot(x - 1f, 37f - up, ManeR.Base, 2f, 2f);
        }
        f.Shift = new Vector2(0, bob + rear);
        f.Stroke(4f, 23f, 1f - (galloping ? 1f : 0f), 30f, 2.4f, ManeR);
        f.Ball(15f, 25f, 11f, 5.5f, HorseR, 0.1f);
        f.Stroke(22f, 24f, 26f, 15f, 5f, HorseR, 0.2f);
        // A chanfron of steel over the horse's face.
        f.Poly(HorseR, (x, y) => 1f - (y - 12f) * 0.2f, 24f, 12f, 28f, 12f, 31f, 16f, 30f, 18f, 26f, 17f);
        f.Poly(PlateR, (x, y) => 1.2f - (x - 24f) * 0.15f, 24.5f, 11.5f, 28f, 11.8f, 30.5f, 15.2f, 27f, 14.5f);
        f.Dot(30f, 16f, HorseR.Deep, 1f, 1f);
        f.Stroke(23f, 13f, 21f, 21f, 1.6f, ManeR);
        f.Dot(25f, 10.5f, ManeR.Base, 1f, 2f);
        // The caparison, dark wool with a hem in the rider's colours; the saddle.
        f.Poly(CloakR, (x, y) => 0.6f - (y - 21f) * 0.25f, 8f, 21f, 20f, 21f, 21f, 29f, 17f, 30f, 15f, 28f, 12f, 30f, 8f, 29f);
        f.TeamStroke(8.5f, 29f, 12f, 29.8f, 1.1f, 0f);
        f.TeamStroke(15f, 28f, 20.8f, 28.8f, 1.1f, 0f);
        f.Stroke(11f, 20.5f, 18f, 20.5f, 2f, LeatherR);
        // The rider: a dark cloak, legs astride, breastplate, burgonet with a plume.
        Cape(f, pose, 13f, 11f, 9f, 4f);
        f.Stroke(15f, 21f, 17f, 27f, 2.4f, TrousersR);
        f.Stroke(17f, 27f, 18f, 30f, 2f, BootR);
        f.Poly(DoubletR, Round(12f, 20f), 12.5f, 11f, 19.5f, 11f, 19f, 20f, 13f, 20f);
        f.Poly(PlateR, Round(12f, 20f, 0.3f), 13f, 11.5f, 19.2f, 11.5f, 18.8f, 18f, 13.4f, 18f);
        f.Stroke(13f, 11.7f, 19.2f, 11.7f, 0.8f, BrassR);
        f.Ball(16.5f, 7f, 3.6f, 3.8f, FaceShadowR, -0.6f);
        f.Poly(PlateR, (x, y) => 1.2f - (x - 13f) * 0.2f, 12.8f, 8f, 13.5f, 4f, 16f, 2.8f, 19f, 3.5f, 20.5f, 6.5f, 20f, 7.5f, 16.5f, 6f, 15f, 9.5f);
        f.TeamStroke(14.5f, 3f, 11.5f, 1.8f, 1.8f, 0.5f);
        f.TeamStroke(11.5f, 1.8f, 9.5f, 3.5f, 1.3f, 0f);
        if (pose.Windup)
        {
            Spear(f, 2f, 9f, 18f, 15f);
            Arm(f, 18f, 13f, 15f, 14.5f, PlateR);
        }
        else if (pose.Strike)
        {
            float reach = pose.Follow ? 1f : 0f;
            Spear(f, 14f + reach, 15f, 29f + reach, 15f);
            Pennon(f, pose, 24f + reach, 15.5f, 4f);
            Arm(f, 18f, 13f, 20f + reach, 15f, PlateR);
            if (!pose.Follow)
                f.Streak(8f, 15f, 31f, 14.5f, 3f, SteelSmear);
        }
        else
        {
            Spear(f, 18f, 19f, 28f, 1f);
            Pennon(f, pose, 26.5f, 4f, 4.5f);
            Arm(f, 18f, 13f, 20.5f, 16f, PlateR);
        }
    }

    /// <summary>
    /// A covered battering ram on four wheels: a roof of dark hides over an iron-bound timber frame, a pennon in its
    /// crew's colours on a pole, an iron-headed log hanging inside — drawn back to wind up, swung out of the front to
    /// strike with a burst of splinters; the wheels turn as it rolls.
    /// </summary>
    private static void Ram(Figure f, Pose pose)
    {
        if (pose.Lying)
        {
            WreckedRam(f);
            return;
        }
        f.Shadow(16f, 14f);
        int bob = pose.Act == Act.Walk && pose.Passing ? -1 : pose.Hurt ? 1 : 0;
        f.Shift = new Vector2(0, bob + pose.Sink / 2);
        float thrust = pose.Strike ? (pose.Follow ? 3f : 5f) : pose.Windup ? (pose.Index == 1 ? -6f : -3f) : pose.Act == Act.Attack && pose.Index == 4 ? 1f : 0f;
        f.Stroke(8f + thrust, 27f, 26f + thrust, 27f, 4f, DarkWoodR, 0.3f);
        f.Poly(IronR, (x, y) => 1f - (y - 24f) * 0.3f, 26f + thrust, 24f, 30f + thrust, 25f, 30f + thrust, 29f, 26f + thrust, 30f);
        if (pose.Strike && !pose.Follow)
            f.Smear(31f + thrust, 27f, 4f, -70f, 70f, 2f, new Color("ffd890"), 0.8f);
        foreach (float x in new[] { 5f, 15f, 25f })
        {
            f.Stroke(x, 16f, x, 33f, 2f, DarkWoodR, 0.3f);
            f.Stroke(x - 1f, 24f, x + 1f, 24f, 1f, IronR, 0.4f);
        }
        f.Stroke(3f, 32f, 28f, 32f, 2.4f, DarkWoodR, 0f);
        f.Poly(LeatherR, (x, y) => 0.6f - (y - 10f) * 0.22f - (x - 4f) * 0.03f + (((int)x + (int)y / 3) % 5 == 0 ? -0.6f : 0f),
            2.5f, 20f, 9.5f, 9f, 22.5f, 9f, 29.5f, 20f);
        for (int i = 0; i < 4; i++)
            f.Stroke(7f + i * 6f, 10.5f, 5.5f + i * 7f, 19.5f, 0.8f, LeatherR, -1.2f);
        f.Stroke(2.5f, 20f, 29.5f, 20f, 1.4f, IronR, -0.2f);
        // A pole on the roof with the crew's pennon.
        f.Stroke(22f, 9.5f, 22f, 2f, 1f, DarkWoodR, 0.4f);
        Pennon(f, pose, 21.5f, 2f, 5f);
        int turn = pose.Act == Act.Walk ? pose.Index : 0;
        foreach (float wx in new[] { 8f, 24f })
            Wheel(f, wx, 34.5f, 4f, turn);
    }

    /// <summary>A spoked wheel: an iron tyre, a wooden rim, spokes turned <paramref name="turn"/> sixteenths.</summary>
    private static void Wheel(Figure f, float cx, float cy, float r, int turn)
    {
        f.Ball(cx, cy, r, r, IronR, -0.3f);
        f.Ball(cx, cy, r - 1f, r - 1f, DarkWoodR, -0.4f);
        for (int s = 0; s < 2; s++)
        {
            float a = turn * Mathf.Pi / 8f + s * Mathf.Pi / 2f;
            f.Stroke(cx - Mathf.Cos(a) * (r - 1f), cy - Mathf.Sin(a) * (r - 1f), cx + Mathf.Cos(a) * (r - 1f), cy + Mathf.Sin(a) * (r - 1f), 1f, WoodR, 0.5f);
        }
        f.Ball(cx, cy, 1f, 1f, IronR, 0.8f);
    }

    /// <summary>
    /// A trebuchet on a wheeled bed: an A-frame with the throwing arm and an iron counterweight box bound in brass, a
    /// pennon in its crew's colours atop the frame. Rolling, its wheels turn; winding up, the arm is hauled down with the
    /// weight raised; loosing, the arm flies over with a blur and the weight drops, then swings back and settles.
    /// </summary>
    private static void Trebuchet(Figure f, Pose pose)
    {
        if (pose.Lying)
        {
            WreckedTrebuchet(f);
            return;
        }
        f.Shadow(16f, 14f);
        f.Shift = new Vector2(0f, pose.Sink / 2);
        f.Stroke(22f, 31f, 16f, 9f, 2.2f, DarkWoodR, -0.9f);
        f.Stroke(8f, 31f, 15f, 9f, 2.4f, WoodR);
        f.Stroke(19f, 31f, 15.5f, 9f, 2.4f, WoodR);
        f.Stroke(10f, 22f, 20f, 22f, 1.6f, DarkWoodR);
        f.Stroke(15.5f, 9f, 15.5f, 3f, 0.9f, DarkWoodR, 0.4f);
        Pennon(f, pose, 15f, 3f, 4.5f);
        // The arm's angle through the throw (degrees, counter-clockwise from the right): hauled down behind, flung up
        // and over to the front, swinging back.
        float angle = pose.Windup ? (pose.Index == 1 ? 215f : 205f)
            : pose.Strike ? (pose.Follow ? 20f : 55f)
            : pose.Act == Act.Attack && pose.Index == 4 ? 80f
            : 135f;
        var pivot = new Vector2(15.5f, 9f);
        float rad = Mathf.DegToRad(angle);
        var longEnd = pivot + new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad)) * 16f;
        var shortEnd = pivot - (longEnd - pivot) * 0.45f;
        if (pose.Strike && !pose.Follow)
            f.Smear(pivot.X, pivot.Y, 15f, -170f, -55f, 3f, new Color("e8d0a0"), 0.6f);
        f.Stroke(pivot.X, pivot.Y, longEnd.X, longEnd.Y, 2f, WoodR, 0.3f);
        f.Stroke(pivot.X, pivot.Y, shortEnd.X, shortEnd.Y, 2.6f, WoodR, 0.3f);
        f.Ball(pivot.X, pivot.Y, 1.4f, 1.4f, IronR, 0.6f);
        f.Stroke(longEnd.X, longEnd.Y, longEnd.X + (pose.Strike ? 2f : 0f), longEnd.Y + 3f, 0.8f, RopeR);
        if (pose.Windup)
            f.Ball(longEnd.X + 2f, longEnd.Y + 1f, 2f, 2f, StoneR, 0.4f);
        f.Stroke(shortEnd.X, shortEnd.Y, shortEnd.X, shortEnd.Y + 3f, 1f, IronR);
        f.Poly(IronR, (x, y) => 1.2f - (x - shortEnd.X + 3f) * 0.35f, shortEnd.X - 3f, shortEnd.Y + 3f, shortEnd.X + 3f, shortEnd.Y + 3f,
            shortEnd.X + 3f, shortEnd.Y + 8f, shortEnd.X - 3f, shortEnd.Y + 8f);
        f.Stroke(shortEnd.X - 3f, shortEnd.Y + 4.5f, shortEnd.X + 3f, shortEnd.Y + 4.5f, 0.8f, BrassR);
        f.Stroke(shortEnd.X - 3f, shortEnd.Y + 6.8f, shortEnd.X + 3f, shortEnd.Y + 6.8f, 0.8f, BrassR, -0.4f);
        f.Stroke(2f, 31.5f, 30f, 31.5f, 3f, WoodR, 0.2f);
        int turn = pose.Act == Act.Walk ? pose.Index : 0;
        foreach (float wx in new[] { 6f, 26f })
            Wheel(f, wx, 34.5f, 4f, turn);
    }

    /// <summary>
    /// Keyframes of a feathered wing's beat: (tip, elbow, the way the flight feathers hang) raised high, level behind,
    /// swept down, level again; a beat between them blends the neighbours.
    /// </summary>
    private static readonly (float TipX, float TipY, float ElbowX, float ElbowY, float FeatherX, float FeatherY)[] WingBeat =
    {
        (3f, 1f, 9f, 11f, -1.5f, 6f),
        (-1f, 15f, 6f, 17f, -1f, 5f),
        (2f, 36f, 7f, 30f, -2f, -1f),
        (-1f, 17f, 6f, 18f, -1f, 4f),
    };

    /// <summary>
    /// A griffin under an armoured rider: an eagle's white head and wings on a lion's tawny body, a dark caparison
    /// hemmed in the faction's colours, a plume on the rider's helmet. Its wings beat through the cycle; winding up it rears
    /// with its talons raised, striking it lunges, beak and claws raking an arc of light.
    /// </summary>
    private static void Griffin(Figure f, Pose pose)
    {
        if (pose.Lying)
        {
            FallenGriffin(f);
            return;
        }
        f.Shift = Vector2.Zero;
        float beat = pose.Act switch
        {
            Act.Walk => pose.Phase * 4f,
            Act.Idle => pose.Phase * 4f,
            Act.Attack => pose.Windup ? 0f : pose.Strike ? 2f : 1f,
            _ => pose.Index >= 1 ? 2f : 1.5f,
        };
        float rear = pose.Windup ? -2f : pose.Hurt ? 1f : 0f, lunge = pose.Strike ? 2f : 0f;
        float rise = Mathf.Sin(beat / 4f * Mathf.Tau) * 1f;
        f.Shift = new Vector2(lunge, rear - rise + pose.Sink);
        Wing(f, 13f, 22f, beat + 0.3f, far: true);
        f.Stroke(6f, 28f, 3f, 33f, 2.6f, LionR, -0.7f);
        f.Stroke(9f, 29f, 7f, 35f, 2.8f, LionR);
        f.Dot(6f, 35f, LionR.Deep, 2f, 1.5f);
        f.Stroke(4f, 25f, 0.5f, 20f + rise, 1.4f, LionR, -0.3f);
        f.Ball(1f, 19f + rise, 1.4f, 1.6f, ManeR, 0.2f);
        f.Ball(13f, 27f, 9f, 5f, LionR, 0.1f);
        float clawX = pose.Windup ? 24f : pose.Strike ? 27f : 20f, clawY = pose.Windup ? 24f : pose.Strike ? 28f : 34f;
        f.Stroke(18f, 29f, clawX, clawY, 2.4f, BeakR, -0.2f);
        f.Stroke(clawX, clawY, clawX + 1.5f, clawY + 1.5f, 1f, ManeR);
        if (pose.Strike && !pose.Follow)
            f.Smear(22f, 24f, 8f, -40f, 80f, 2.5f, SteelSmear);
        f.Poly(CloakR, (x, y) => 0.7f - (y - 22f) * 0.3f, 9f, 22.5f, 18f, 22.5f, 19f, 29f, 15f, 30f, 12f, 28f, 8f, 29f);
        f.TeamStroke(8.5f, 28.8f, 12f, 28f, 1.1f, 0f);
        f.TeamStroke(15f, 29.8f, 18.8f, 28.8f, 1.1f, 0f);
        f.Stroke(18f, 25f, 22f, 18f, 5.5f, FeatherR, 0.2f);
        float headX = 23f + (pose.Strike ? 1.5f : 0f) - (pose.Hurt ? 1f : 0f), headY = 16f;
        f.Ball(headX, headY, 3.8f, 3.4f, FeatherR, 0.4f);
        bool open = pose.Strike || pose.Hurt;
        f.Poly(BeakR, (x, y) => 1.2f - (y - headY) * 0.5f, headX + 2.5f, headY - 1.5f, headX + 6f, headY - 0.5f, headX + 6.5f, headY + 2f,
            headX + 5f, headY + (open ? 2f : 1f), headX + 2.5f, headY + 1.5f);
        f.Dot(headX + 1f, headY - 1f, Eye, 1f, 1f);
        f.Dot(headX - 2f, headY - 3.5f, FeatherR.Glint, 2f, 1f);
        f.Stroke(13.5f, 22f, 15f, 27f, 2.2f, TrousersR);
        f.Stroke(15f, 27f, 16f, 29f, 1.8f, BootR);
        Cape(f, pose, 11.5f, 13f, 8f, 3.5f);
        f.Poly(DoubletR, Round(10.5f, 17f), 11f, 13f, 17f, 13f, 16.5f, 22f, 11.5f, 22f);
        f.Poly(PlateR, Round(10.5f, 17f, 0.3f), 11.4f, 13.5f, 16.6f, 13.5f, 16.2f, 19.5f, 11.8f, 19.5f);
        f.Stroke(11.4f, 13.7f, 16.6f, 13.7f, 0.8f, BrassR);
        f.Ball(14f, 9.5f, 3.2f, 3.4f, FaceShadowR, -0.6f);
        f.Poly(PlateR, (x, y) => 1.2f - (x - 11f) * 0.2f, 10.8f, 9.5f, 11.2f, 6.5f, 13.5f, 5.5f, 16.5f, 6f, 17.5f, 8.5f);
        f.TeamStroke(12.5f, 5.5f, 10f, 4.5f, 1.6f, 0.5f);
        f.TeamStroke(10f, 4.5f, 8.5f, 6f, 1.2f, 0f);
        if (pose.Strike)
            Sword(f, 18f, 15f, 27f, pose.Follow ? 16f : 11f);
        else if (pose.Windup)
            Sword(f, 16f, 13f, 10f, 4f);
        else
            Sword(f, 17f, 16f, 20f, 7f);
        Arm(f, 15.5f, 15f, pose.Strike ? 18f : pose.Windup ? 16f : 17f, pose.Strike ? 15f : pose.Windup ? 13f : 16f, PlateR);
        Wing(f, 14f, 22f, beat, far: false);
    }

    /// <summary>
    /// A feathered wing from the shoulder at a point of its beat (0..4, see <see cref="WingBeat"/>): coverts lit and
    /// pale, long flight feathers darker; the far wing is darker and a little smaller.
    /// </summary>
    private static void Wing(Figure f, float sx, float sy, float beat, bool far)
    {
        beat = (beat % 4f + 4f) % 4f;
        int k0 = (int)beat, k1 = (k0 + 1) % 4;
        float t = beat - k0;
        var a = WingBeat[k0];
        var b = WingBeat[k1];
        float L(float p, float q) => Mathf.Lerp(p, q, t);
        float scale = far ? 0.85f : 1f;
        float bias = far ? -1f : 0f;
        float tipX = sx + (L(a.TipX, b.TipX) - sx) * scale, tipY = sy + (L(a.TipY, b.TipY) - sy) * scale;
        float elbowX = sx + (L(a.ElbowX, b.ElbowX) - sx) * scale, elbowY = sy + (L(a.ElbowY, b.ElbowY) - sy) * scale;
        float fx = L(a.FeatherX, b.FeatherX), fy = L(a.FeatherY, b.FeatherY);
        for (int i = 0; i < 6; i++)
        {
            float u = i / 5f;
            float ax = Mathf.Lerp(elbowX, tipX, u), ay = Mathf.Lerp(elbowY, tipY, u);
            f.Stroke(ax, ay, ax + fx - (1f - u) * 2f, ay + fy * (1f - u * 0.5f), 2.4f, WingR, bias - 0.3f - u * 0.2f);
        }
        f.Stroke(sx, sy, elbowX, elbowY, 3.6f, FeatherR, bias + 0.2f);
        f.Stroke(elbowX, elbowY, tipX, tipY, 2.6f, WingR, bias + 0.3f);
        f.Stroke((sx + elbowX) / 2f, (sy + elbowY) / 2f + 1.5f, (elbowX + tipX) / 2f, (elbowY + tipY) / 2f + 1.5f, 2f, FeatherR, bias - 0.2f);
    }

    /// <summary>
    /// Keyframes of a dragon's wing beat, relative to the shoulder: the wrist, and the three finger tips relative to it.
    /// </summary>
    private static readonly (Vector2 Wrist, Vector2 A, Vector2 B, Vector2 C)[] DragonBeat =
    {
        (new(-6f, -27f), new(-17f, 3f), new(-18f, 13f), new(-12f, 22f)),
        (new(-14f, -15f), new(-13f, -3f), new(-14f, 7f), new(-9f, 16f)),
        (new(-14f, 6f), new(-14f, 6f), new(-8f, 13f), new(-1f, 15f)),
        (new(-15f, -12f), new(-13f, -2f), new(-14f, 8f), new(-9f, 16f)),
    };

    /// <summary>
    /// The dragon (big sheet): a dark-scaled body with a pale plated belly, a long neck and a horned head with ember
    /// eyes, a whipping tail, great wings of dark leathery membrane and its master's colours on its harness. Its wings beat through the
    /// cycle; winding up it rears and draws breath, its jaws glowing; striking, it breathes a gout of fire.
    /// </summary>
    private static void Dragon(PixelCanvas c, Pose pose)
    {
        // Coordinates here run over the whole 64×64 cell (its feet, the ground under it, at 32, 62).
        var f = new Figure(c, ArtLayout.BigFeet, 1f);
        bool down = pose.Lying;
        float beat = down ? 1.8f : pose.Act switch
        {
            Act.Walk or Act.Idle => pose.Phase * 4f,
            Act.Attack => pose.Windup ? 0f : pose.Strike ? 1.5f : 1f,
            _ => pose.Index >= 1 ? 2f : 1.5f,
        };
        float rise = Mathf.Sin(beat / 4f * Mathf.Tau) * 1.5f;
        float rear = down ? 9f : pose.Windup ? -3f : pose.Strike ? 1f : pose.Hurt ? 1f : 0f;
        f.Shift = new Vector2(0f, rear - rise + pose.Sink);
        float X(float x) => x - 16f;
        float Y(float y) => y - 23f;
        DragonWing(f, X(28f), Y(36f), beat + 0.3f, far: true);
        float swish = Mathf.Sin(pose.Phase * Mathf.Tau) * 2f;
        f.Stroke(X(19f), Y(45f), X(10f), Y(51f - swish), 5f, ScaleR, -0.2f);
        f.Stroke(X(10f), Y(51f - swish), X(4f), Y(56f - swish), 3.4f, ScaleR, -0.1f);
        f.Stroke(X(4f), Y(56f - swish), X(2f), Y(52f - swish), 2.2f, ScaleR);
        f.Poly(BellyR, (x, y) => 0.5f, X(0f), Y(53f - swish), X(1.5f), Y(47.5f - swish), X(4.5f), Y(51f - swish));
        f.Stroke(X(22f), Y(48f), X(19f), Y(55f), 4f, ScaleR, -1f);
        f.Stroke(X(36f), Y(47f), X(38f), Y(53f), 3.4f, ScaleR, -1f);
        f.Ball(X(29f), Y(43f), 13f, 7.5f, ScaleR, 0.1f);
        f.Poly(BellyR, (x, y) => 0.6f - (y - Y(46f)) * 0.3f, X(19f), Y(47f), X(39f), Y(45f), X(37f), Y(50f), X(22f), Y(51f));
        for (int i = 0; i < 5; i++)
            f.Stroke(X(21f + i * 4f), Y(47f), X(21.5f + i * 4f), Y(50.5f), 0.7f, BellyR, -1.2f);
        for (int i = 0; i < 5; i++)
            f.Poly(HornR, (x, y) => 0.6f, X(18f + i * 4.5f), Y(37f - (i == 2 ? 1f : 0f)), X(20f + i * 4.5f), Y(33f - (i % 2)), X(21.5f + i * 4.5f), Y(37f));
        f.Stroke(X(24f), Y(48f), X(22f), Y(56f), 4.6f, ScaleR, 0.1f);
        f.Stroke(X(22f), Y(56f), X(25f), Y(58f), 2f, HornR);
        float foreX = pose.Windup ? X(44f) : X(39f), foreY = pose.Windup ? Y(46f) : Y(54f);
        f.Stroke(X(36f), Y(47f), foreX, foreY, 4f, ScaleR, 0.1f);
        f.Stroke(foreX, foreY, foreX + 2.5f, foreY + 2f, 1.8f, HornR);
        float headX = X(down ? 53f : pose.Strike ? 54f : pose.Hurt ? 49f : 51f), headY = Y(down ? 49f : pose.Windup ? 22f : pose.Hurt ? 27f : 25f);
        f.Stroke(X(37f), Y(40f), (X(37f) + headX) / 2f + 1f, (Y(40f) + headY) / 2f - 1f, 6f, ScaleR, 0.1f);
        f.Stroke((X(37f) + headX) / 2f + 1f, (Y(40f) + headY) / 2f - 1f, headX - 2f, headY + 1f, 5f, ScaleR, 0.2f);
        f.Stroke(X(38f), Y(43f), (X(38f) + headX) / 2f + 2f, (Y(43f) + headY) / 2f + 1f, 2.2f, BellyR, -0.2f);
        // The harness: a collar strap in the master's colours and a banner on a pole at the withers.
        f.TeamStroke((X(37f) + headX) / 2f - 1f, (Y(40f) + headY) / 2f - 3f, (X(37f) + headX) / 2f + 3f, (Y(40f) + headY) / 2f + 2f, 1.6f, 0.2f);
        f.Stroke(X(33f), Y(36f), X(33f), Y(26f), 1f, DarkWoodR, 0.4f);
        f.Poly(null, (x, y) => 0.7f - (y - Y(26f)) * 0.15f, X(33f), Y(26f), X(26f), Y(26.5f) + swish * 0.5f, X(27.5f), Y(29f) + swish * 0.5f,
            X(26f), Y(31.5f) + swish * 0.5f, X(33f), Y(31f));
        f.Ball(headX, headY, 5f, 3.6f, ScaleR, 0.3f);
        bool open = !down && (pose.Windup || pose.Strike || pose.Hurt);
        f.Poly(ScaleR, (x, y) => 1f - (y - headY) * 0.4f, headX + 2f, headY - 2.5f, headX + 9f, headY - 1f, headX + 9f, headY + 1f, headX + 3f, headY + 1.5f);
        f.Poly(ScaleR, (x, y) => -0.2f, headX + 2f, headY + 2f, headX + 8f, headY + (open ? 5f : 2f), headX + 7f, headY + (open ? 6f : 3f), headX + 1f, headY + 3.5f);
        if (open)
            f.Poly(FlameR, (x, y) => pose.Windup || pose.Strike ? 0.5f : -2f, headX + 3f, headY + 1.6f, headX + 8.5f, headY + 1.2f, headX + 7.5f, headY + 4.5f);
        f.Dot(headX + 1f, headY - 1.5f, down ? ScaleR.Deep : Ember, 1.5f, 1f);
        f.Stroke(headX - 2f, headY - 2.5f, headX - 7f, headY - 7f, 2f, HornR, 0.4f);
        f.Stroke(headX - 1f, headY - 3f, headX - 4f, headY - 9f, 1.6f, HornR, 0.6f);
        f.Dot(headX + 7.5f, headY - 1.5f, ScaleR.Deep, 1f, 1f);
        if (pose.Windup)
        {
            // Drawing breath: embers in the jaws, smoke curling from the nostrils.
            f.Dot(headX + 4f, headY + 2.5f, Ember, 2f, 1f);
            f.Dot(headX + 6f, headY + 3f, Holy, 1f, 1f);
            f.Dot(headX + 9f, headY - 3f - pose.Index, new Color("5a5060"), 2f, 2f);
        }
        if (pose.Strike)
        {
            // A gout of fire rolling out of the jaws (dying back on the follow-through).
            int puffs = pose.Follow ? 3 : 5;
            for (int i = 0; i < puffs; i++)
            {
                float t = i / 4f;
                float fx = headX + 10f + i * 2.4f, fy = headY + 3f + i * 1.8f, r = 2f + t * 2.8f;
                f.Ball(fx, fy, r, r * 0.8f, FlameR, (pose.Follow ? 0.4f : 1.4f) - t);
            }
            f.Dot(headX + 10f, headY + 2.5f, Holy, 2f, 1.5f);
        }
        DragonWing(f, X(30f), Y(37f), beat, far: false);
    }

    /// <summary>
    /// A dragon's bat-like wing from the shoulder at a point of its beat (0..4, see <see cref="DragonBeat"/>): a bony
    /// arm to the wrist and three long fingers, dark leathery membranes between them (darker on the far wing),
    /// the trailing edge scalloped.
    /// </summary>
    private static void DragonWing(Figure f, float sx, float sy, float beat, bool far)
    {
        beat = (beat % 4f + 4f) % 4f;
        int k0 = (int)beat, k1 = (k0 + 1) % 4;
        float t = beat - k0;
        var a = DragonBeat[k0];
        var b = DragonBeat[k1];
        float k = far ? 0.85f : 1f;
        float bias = far ? -1.1f : 0f;
        var wrist = new Vector2(sx, sy) + a.Wrist.Lerp(b.Wrist, t) * k;
        var tips = new[] { a.A.Lerp(b.A, t), a.B.Lerp(b.B, t), a.C.Lerp(b.C, t) };
        for (int i = 0; i < tips.Length; i++)
            tips[i] = wrist + tips[i] * k;
        float wx = wrist.X, wy = wrist.Y;
        var root = new Vector2(sx + 2f, sy + 6f);
        var corners = new[] { tips[0], tips[1], tips[2], root };
        for (int i = 0; i < 3; i++)
        {
            var p = corners[i];
            var q = corners[i + 1];
            var notch = (p + q) / 2f + (wrist - (p + q) / 2f) * 0.22f;
            int fold = i;
            f.Poly(MembraneR, (x, y) => 0.4f + bias - (y - wy) * 0.04f - fold * 0.3f, wx, wy, p.X, p.Y, notch.X, notch.Y, q.X, q.Y);
        }
        f.Stroke(sx, sy, wx, wy, 3.2f, ScaleR, bias + 0.3f);
        foreach (var tip in tips)
            f.Stroke(wx, wy, tip.X, tip.Y, 1.4f, ScaleR, bias + 0.2f);
        f.Poly(HornR, (x, y) => 0.6f, wx - 1f, wy - 1f, wx + 1f, wy - 4f, wx + 1.5f, wy);
    }
}
