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

    /// <summary>Every frame of one kind: each drawn, rim-lit and outlined in its cell; the lying frames of its death turned from a fallen pose.</summary>
    private static void DrawRow(PixelCanvas sheet, int row, string kind, Vector2I cellSize, Vector2I feet)
    {
        for (int frame = 0; frame < ArtLayout.UnitFrames; frame++)
        {
            var cell = new Vector2I(frame * cellSize.X, row * cellSize.Y);
            var pose = Pose.Of(frame);
            sheet.Origin = cell;
            sheet.Clip = new Rect2I(cell, cellSize);
            if (pose.Lying)
            {
                // Drawn standing in a scratch cell as it was struck down, then laid on its back here.
                var scratch = new PixelCanvas(cellSize.X, cellSize.Y);
                DrawKind(scratch, kind, Pose.Fallen, feet);
                scratch.RimLight(new Rect2I(Vector2I.Zero, cellSize), SkyRim, 0.35f);
                if (pose.Index == ArtLayout.DeathFrames - 1 && !IsMachine(kind))
                    sheet.GroundShadow(feet.X - 2, feet.Y - 1, cellSize.X * 0.38f, 2.6f, 0.55f); // a dark pool
                sheet.CopyLying(scratch, new Rect2I(Vector2I.Zero, cellSize), feet);
            }
            else
            {
                DrawKind(sheet, kind, pose, feet);
                sheet.Origin = Vector2I.Zero;
                sheet.RimLight(new Rect2I(cell, cellSize), SkyRim, 0.45f);
            }
            sheet.Origin = Vector2I.Zero;
            sheet.SelOut(SilhouetteRim, new Rect2I(cell, cellSize));
            if (pose.Lying && pose.Index == ArtLayout.DeathFrames - 1)
                Darken(sheet, new Rect2I(cell, cellSize), 0.25f); // the last frame: still, sinking into the dusk
        }
        sheet.Clip = null;
    }

    private static bool IsMachine(string kind) => kind is "ram" or "trebuchet";

    private static void DrawKind(PixelCanvas c, string kind, Pose pose, Vector2I feet)
    {
        var f = new Figure(c, feet, kind == "warlord" ? 1.45f : 1f);
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
                1 => new Pose(Act.Death, 1, 19f, 12f, 0f, 0f, 0, 2, 4, false, false, false, 0, 0.5f),
                2 => new Pose(Act.Death, 2, 19f, 12f, 0f, 0f, 0, 5, 8, false, false, false, 0, 0.75f),
                _ => new Pose(Act.Death, d, 18f, 13f, 0f, 0f, 0, 0, 0, false, false, false, 0, 0f),
            };
        }

        private static readonly Pose Flinch = new(Act.Hurt, 0, 18f, 11f, 0f, 0f, 1, -2, 0, false, false, false, 0, 0.3f);

        /// <summary>The pose a body is laid down in for the lying frames of its death.</summary>
        public static readonly Pose Fallen = new(Act.Death, 2, 18f, 14f, 0f, 0f, 0, 0, 0, false, false, false, 0, 0.6f);

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

        public void Stroke(float x0, float y0, float x1, float y1, float w, Ramp r, float bias = 0f)
        {
            var a = P(x0, y0);
            var b = P(x1, y1);
            C.Stroke(a.X, a.Y, b.X, b.Y, w * Scale, r, bias);
        }

        public void Taper(float x0, float y0, float x1, float y1, float w0, float w1, Ramp r, float bias = 0f)
        {
            var a = P(x0, y0);
            var b = P(x1, y1);
            C.Taper(a.X, a.Y, b.X, b.Y, w0 * Scale, w1 * Scale, r, bias);
        }

        public void TeamStroke(float x0, float y0, float x1, float y1, float w, float bias = 0f)
        {
            var a = P(x0, y0);
            var b = P(x1, y1);
            C.TeamStroke(a.X, a.Y, b.X, b.Y, w * Scale, bias);
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
    /// How a kind of soldier is built (in the 32×40 cell's pixels): half the width of the shoulders and of the waist,
    /// the length of the legs and of the torso, the head's radius, how far the shoulders hunch forward, how heavily the
    /// body sinks into each step, how long the stride is, and how thick the limbs are.
    /// </summary>
    private readonly record struct Build(float Shoulder, float Waist, float Leg, float Torso, float HeadR, float Hunch,
        float Weight, float Stride, float Limb);

    private static readonly Build Trooper = new(5.3f, 3.8f, 12.8f, 10.5f, 3.6f, 0.2f, 1f, 1f, 1.1f);
    private static readonly Build Hunter = new(4.7f, 3.3f, 13.2f, 10f, 3.5f, 1.1f, 0.8f, 1.15f, 1f);
    private static readonly Build Knight = new(6.4f, 4.8f, 12f, 11f, 3.9f, 0f, 1.6f, 0.8f, 1.35f);
    private static readonly Build Stocky = new(5.7f, 4.7f, 11.2f, 10f, 3.7f, 0.5f, 1.25f, 0.9f, 1.25f);
    private static readonly Build Priest = new(4.9f, 4.3f, 13f, 10.5f, 3.6f, 1.3f, 0.9f, 0.9f, 1.1f);
    private static readonly Build Brute = new(6.4f, 5.2f, 12f, 11.5f, 3.9f, 1.4f, 1.8f, 0.85f, 1.45f);

    /// <summary>
    /// A soldier's skeleton in one frame: hips, neck, head and shoulders placed by the build and the pose, the feet by the
    /// stride, and the knees and elbows found between them (they bend the way joints bend), so arms and legs keep their
    /// length however hands and feet are placed. Draws the body's masses on its <see cref="Figure"/>.
    /// </summary>
    private sealed class Body
    {
        public Body(Figure f, Pose pose, Build build)
        {
            F = f;
            Pose = pose;
            B = build;
            var p = pose;
            float a = p.Phase * Mathf.Tau;
            bool walk = p.Act == Act.Walk;
            float stride = 5.5f * B.Stride;
            float near = walk ? 16f + stride * Mathf.Cos(a) : p.Near;
            float far = walk ? 16f - stride * Mathf.Cos(a) : p.Far;
            float lift = 3.6f / Mathf.Sqrt(B.Weight);
            float nearLift = walk ? Mathf.Max(0f, -Mathf.Sin(a)) * lift : p.NearLift;
            float farLift = walk ? Mathf.Max(0f, Mathf.Sin(a)) * lift : p.FarLift;
            // The body is lowest as a foot strikes the ground (the feet apart) and rises as they pass; the heavy sink deeper.
            float bob = walk ? (1f - Mathf.Abs(Mathf.Sin(a))) * 1.3f * B.Weight - 0.4f : p.Bob;
            float breathe = p.Act == Act.Idle ? Mathf.Sin(a) * 0.6f : 0f;
            float lean = p.Lean + (walk ? 0.9f : 0f) + B.Hunch;
            float hipY = 37.2f - B.Leg + 0.3f + bob + p.Sink;
            Hip = new Vector2(16f - p.Lean * 0.25f, hipY);
            Neck = new Vector2(Hip.X + lean, hipY - B.Torso - breathe);
            Head = Neck + new Vector2(p.Hurt ? -0.8f : 0.6f, -B.HeadR * 0.95f + (walk ? 0.3f * (1f - Mathf.Abs(Mathf.Sin(a - 0.5f))) : 0f));
            // The arms swing against the legs: the near arm forward as the near leg goes back.
            Swing = walk ? -Mathf.Cos(a) : 0f;
            NearShoulder = Neck + new Vector2(0.6f + Swing * 0.4f, 1.8f);
            FarShoulder = Neck + new Vector2(-1.8f - Swing * 0.3f, 1.8f);
            NearAnkle = new Vector2(near, 37.2f - nearLift);
            FarAnkle = new Vector2(far, 37.2f - farLift);
        }

        public Figure F { get; }
        public Pose Pose { get; }
        public Build B { get; }
        public Vector2 Hip { get; }
        public Vector2 Neck { get; }
        public Vector2 Head { get; }
        public Vector2 NearShoulder { get; }
        public Vector2 FarShoulder { get; }
        public Vector2 NearAnkle { get; }
        public Vector2 FarAnkle { get; }

        /// <summary>The near arm's swing on the march: 1 fully forward, -1 fully back.</summary>
        public float Swing { get; }

        private float ArmSegment => B.Torso * 0.5f;
        private float LegSegment => B.Leg / 2f;

        /// <summary>The point <paramref name="target"/>, or as near it as a limb of <paramref name="length"/> from <paramref name="from"/> reaches.</summary>
        private static Vector2 Reach(Vector2 from, Vector2 target, float length)
        {
            var d = target - from;
            float l = d.Length();
            return l <= length || l < 1e-4f ? target : from + d / l * length;
        }

        /// <summary>The joint between two segments from <paramref name="a"/> to <paramref name="b"/>, bending forward (a knee) or back (an elbow).</summary>
        private static Vector2 Joint(Vector2 a, Vector2 b, float l1, float l2, bool forward)
        {
            var d = b - a;
            float dist = Mathf.Clamp(d.Length(), 0.01f, l1 + l2 - 0.01f);
            var dir = d.Length() > 1e-4f ? d.Normalized() : Vector2.Down;
            float along = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float across = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
            var perp = new Vector2(-dir.Y, dir.X);
            var j1 = a + dir * along + perp * across;
            var j2 = a + dir * along - perp * across;
            return forward == (j1.X > j2.X) ? j1 : j2;
        }

        public void Shadow() => F.Shadow(16f, 5f + B.Shoulder * 0.8f);

        /// <summary>A leg: a thigh tapering into the knee, a shin into the ankle, and a boot, its toe forward.</summary>
        public void Leg(bool near, Ramp trousers, Ramp boots)
        {
            float bias = near ? 0.1f : -1f;
            var hip = Hip + new Vector2(near ? 0.9f : -0.9f, 0f);
            var ankle = Reach(hip, near ? NearAnkle : FarAnkle, LegSegment * 2f);
            var knee = Joint(hip, ankle, LegSegment, LegSegment, forward: true);
            float w = B.Limb;
            F.Taper(hip.X, hip.Y, knee.X, knee.Y, 4f * w, 3.1f * w, trousers, bias);
            F.Taper(knee.X, knee.Y, ankle.X, ankle.Y, 3.1f * w, 2.4f * w, trousers, bias - 0.2f);
            F.Poly(boots, (x, y) => (y < ankle.Y ? 0.6f : -0.2f) + bias,
                ankle.X - 1.4f, ankle.Y - 1.6f, ankle.X + 1.2f, ankle.Y - 1.6f, ankle.X + 1.6f, ankle.Y + 0.2f, ankle.X + 3.4f, ankle.Y + 0.9f,
                ankle.X + 3.4f, ankle.Y + 1.8f, ankle.X - 1.6f, ankle.Y + 1.8f);
        }

        /// <summary>
        /// The torso's mass: square shoulders, the chest swelling forward, the back drawing in to the waist and widening
        /// again at the hips; in <paramref name="r"/> or faction colour.
        /// </summary>
        public void Torso(Ramp? r, float bias = 0f)
        {
            float w = B.Shoulder, wa = B.Waist, t = B.Torso;
            var n = Neck;
            var h = Hip;
            F.Poly(r, Round(n.X - w, n.X + w, bias),
                n.X - w * 0.8f, n.Y + 0.4f, n.X + w * 0.5f, n.Y + 0.3f, n.X + w * 0.85f, n.Y + t * 0.3f, h.X + wa * 0.75f, h.Y - t * 0.25f,
                h.X + wa * 0.9f, h.Y + 1.4f, h.X - wa * 0.95f, h.Y + 1.4f, h.X - wa * 0.85f, h.Y - t * 0.35f, n.X - w * 0.95f, n.Y + t * 0.22f);
        }

        /// <summary>A breastplate over the torso: a ridge catching the light, a brass neckline, a two-lame skirt.</summary>
        public void Cuirass(Ramp metal)
        {
            float w = B.Shoulder * 0.85f, wa = B.Waist * 0.9f, t = B.Torso;
            var n = Neck;
            var h = Hip;
            F.Poly(metal, Round(n.X - w, n.X + w, 0.7f),
                n.X - w * 0.7f, n.Y + 1f, n.X + w * 0.55f, n.Y + 0.9f, n.X + w * 0.95f, n.Y + t * 0.32f, h.X + wa * 0.8f, h.Y - t * 0.2f,
                h.X + wa * 0.75f, h.Y - 0.6f, h.X - wa * 0.8f, h.Y - 0.6f, h.X - wa * 0.85f, h.Y - t * 0.35f);
            var ridgeTop = n + new Vector2(w * 0.35f, 1.6f);
            var ridgeBottom = h + new Vector2(wa * 0.25f, -1.5f);
            F.Stroke(ridgeTop.X, ridgeTop.Y, ridgeBottom.X, ridgeBottom.Y, 0.8f, metal, 1.6f);
            F.Stroke(n.X - w * 0.6f, n.Y + 1f, n.X + w * 0.5f, n.Y + 1f, 1f, BrassR, 0.3f);
            F.Stroke(h.X - wa * 0.9f, h.Y + 0.4f, h.X + wa * 0.9f, h.Y + 0.4f, 1.4f, metal, 0.5f);
            F.Stroke(h.X - wa * 0.85f, h.Y + 1.9f, h.X + wa * 0.85f, h.Y + 1.9f, 1.4f, metal, -0.3f);
        }

        public void Belt()
        {
            float wa = B.Waist;
            F.Stroke(Hip.X - wa * 0.95f, Hip.Y - 0.6f, Hip.X + wa * 0.9f, Hip.Y - 0.6f, 1.8f, LeatherR, -0.5f);
            F.Ball(Hip.X + wa * 0.4f, Hip.Y - 0.6f, 1.2f, 1.1f, BrassR, 0.6f);
        }

        /// <summary>A sash in the faction's colours across the chest, from the near shoulder to the far hip, knotted there.</summary>
        public void Sash()
        {
            var from = NearShoulder + new Vector2(0.5f, 0.3f);
            var to = Hip + new Vector2(-B.Waist * 0.8f, -1f);
            F.TeamStroke(from.X, from.Y, to.X, to.Y, 2.2f, 0.2f);
            F.TeamStroke(to.X, to.Y, to.X - 1f, to.Y + 3f + (Pose.Act == Act.Walk ? 0.5f : 0f), 1.6f, -0.4f);
        }

        /// <summary>A shoulder guard of lames, brass-rimmed, over the near shoulder; with a purity seal hanging from it if <paramref name="seal"/>.</summary>
        public void Pauldron(Ramp metal, bool seal = false)
        {
            float size = B.Shoulder * 0.75f;
            var c = NearShoulder + new Vector2(-0.6f, 0.4f);
            F.Ball(c.X, c.Y, size, size * 0.8f, metal, 0.5f);
            F.Stroke(c.X - size + 0.5f, c.Y + size * 0.65f, c.X + size - 0.5f, c.Y + size * 0.65f, 1.1f, BrassR, 0f);
            if (!seal)
                return;
            // Wax as big as a coin, a strip of parchment hanging from it.
            F.Stroke(c.X + 0.6f, c.Y + size, c.X + 1.2f, c.Y + size + 4f, 1.8f, ParchmentR, 0.2f);
            F.Ball(c.X + 0.4f, c.Y + size * 0.7f, 1.6f, 1.5f, WaxR, 0.4f);
        }

        /// <summary>The head: a face lost in the shadow of what is drawn over it, the jaw catching a little light.</summary>
        public void Face()
        {
            float r = B.HeadR;
            F.Ball(Head.X, Head.Y, r, r * 1.05f, FaceShadowR, -0.6f);
            F.Dot(Head.X + r * 0.35f, Head.Y + r * 0.7f, SkinR.Shade, 2f, 1f);
        }

        /// <summary>
        /// An arm from a shoulder towards <paramref name="target"/>: an upper arm tapering into the elbow, a forearm into a
        /// gauntlet (the forearm in <paramref name="forearm"/>, a vambrace, if given); the elbow bends back and down. Returns
        /// where the hand ends up (as near the target as the arm reaches).
        /// </summary>
        public Vector2 Arm(bool near, Vector2 target, Ramp sleeve, Ramp? forearm = null)
        {
            var shoulder = near ? NearShoulder : FarShoulder;
            float bias = near ? 0f : -1.1f;
            var hand = Reach(shoulder, target, ArmSegment * 2f);
            var elbow = Joint(shoulder, hand, ArmSegment, ArmSegment, forward: false);
            float w = B.Limb;
            F.Taper(shoulder.X, shoulder.Y, elbow.X, elbow.Y, 3.4f * w, 2.7f * w, sleeve, bias);
            F.Taper(elbow.X, elbow.Y, hand.X, hand.Y, 2.7f * w, 2.2f * w, forearm ?? sleeve, bias - 0.1f + (forearm != null ? 0.4f : 0f));
            F.Ball(hand.X, hand.Y, 1.5f * w, 1.4f * w, LeatherR, bias + 0.3f);
            return hand;
        }

        /// <summary>Where an empty hand hangs: by the side, swinging against the stride.</summary>
        public Vector2 HangingHand(bool near) => (near ? NearShoulder : FarShoulder)
                                               + new Vector2((near ? 1f : -1f) * Swing * 3.2f + 0.5f, ArmSegment * 1.85f);

        /// <summary>A dark cloak from the back of the shoulders.</summary>
        public void Cloak(float length, float width = 5f) => Cape(F, Pose, Neck.X - 1f, Neck.Y + 0.6f, length, width);

        /// <summary>A plume in the faction's colours from (x, y), curling back and stirring with the cycle.</summary>
        public void Plume(float x, float y, float length = 6f)
        {
            float stir = Mathf.Sin(Pose.Phase * Mathf.Tau) * 0.8f + (Pose.Act == Act.Walk ? 0.8f : 0f);
            F.TeamStroke(x, y, x - length * 0.45f, y - 1.6f, 2.4f, 0.6f);
            F.TeamStroke(x - length * 0.45f, y - 1.6f, x - length, y + 0.8f + stir, 1.8f, 0f);
            F.TeamStroke(x - length * 0.85f, y + 0.4f + stir, x - length * 1.1f, y + 2.5f + stir, 1.2f, -0.5f);
        }

        /// <summary>A morion: a round crown with a tall comb, a brim sweeping up to points front and back.</summary>
        public void Morion(Ramp metal)
        {
            var c = Head;
            float r = B.HeadR;
            F.Ball(c.X, c.Y - r * 0.55f, r * 1.05f, r * 0.75f, metal, 0.8f);
            F.Poly(metal, (x, y) => 1.4f - (x - c.X + r) * 0.22f,
                c.X - r * 0.8f, c.Y - r * 1.05f, c.X - r * 0.15f, c.Y - r * 2.05f, c.X + r * 0.6f, c.Y - r * 1.75f, c.X + r * 0.95f, c.Y - r * 1.05f);
            F.Poly(metal, (x, y) => 0.7f - (y - c.Y) * 0.4f,
                c.X - r * 1.7f, c.Y - r * 0.85f, c.X - r * 1.15f, c.Y - r * 0.1f, c.X + r * 1.25f, c.Y - r * 0.1f, c.X + r * 1.8f, c.Y - r * 0.85f,
                c.X + r * 1.35f, c.Y - r * 0.4f, c.X - r * 1.3f, c.Y - r * 0.4f);
        }

        /// <summary>A sallet: a rounded bowl drawn out into a long tail behind, a visor slit in front.</summary>
        public void Sallet(Ramp metal)
        {
            var c = Head;
            float r = B.HeadR;
            F.Poly(metal, (x, y) => 1.9f - (x - c.X + r) * 0.18f - (y - c.Y + r) * 0.12f,
                c.X - r * 1.1f, c.Y + r * 0.1f, c.X - r * 1f, c.Y - r * 0.9f, c.X - r * 0.2f, c.Y - r * 1.3f, c.X + r * 0.8f, c.Y - r * 1.15f,
                c.X + r * 1.25f, c.Y - r * 0.4f, c.X + r * 1.3f, c.Y + r * 0.45f, c.X + r * 0.55f, c.Y + r * 0.45f, c.X + r * 0.5f, c.Y + r * 0.05f,
                c.X - r * 0.5f, c.Y + r * 0.1f, c.X - r * 2.2f, c.Y + r * 0.8f);
            F.Stroke(c.X - r * 0.1f, c.Y - r * 0.3f, c.X + r * 1.25f, c.Y - r * 0.3f, 1.2f, BlackSteelR, -2f);
        }

        /// <summary>A great helm: flat-topped, a brass cross over the face round the eye slit, breaths below.</summary>
        public void GreatHelm(Ramp metal)
        {
            var c = Head;
            float r = B.HeadR;
            F.Poly(metal, (x, y) => 1.4f - (x - c.X + r) * 0.2f,
                c.X - r * 1.1f, c.Y + r * 1.15f, c.X - r * 1.15f, c.Y - r * 0.85f, c.X - r * 0.7f, c.Y - r * 1.3f, c.X + r * 1f, c.Y - r * 1.3f,
                c.X + r * 1.35f, c.Y - r * 0.85f, c.X + r * 1.4f, c.Y + r * 1.15f);
            F.Stroke(c.X - r * 0.5f, c.Y - r * 0.25f, c.X + r * 1.35f, c.Y - r * 0.25f, 1.4f, BlackSteelR, -2f);
            F.Stroke(c.X + r * 0.65f, c.Y - r * 1.2f, c.X + r * 0.65f, c.Y + r * 1.05f, 1.3f, BrassR, 0.3f);
            F.Stroke(c.X - r * 0.2f, c.Y + r * 0.3f, c.X + r * 1.35f, c.Y + r * 0.3f, 1.2f, BrassR, 0f);
        }

        /// <summary>A deep hood (or cowl) with its point falling behind, framing the shadowed face.</summary>
        public void Hood(Ramp cloth, float drape = 0f)
        {
            var c = Head;
            float r = B.HeadR;
            F.Poly(cloth, (x, y) => 1.1f - (x - c.X + r) * 0.16f - (y - c.Y + r) * 0.08f,
                c.X - r * 1.25f, c.Y + r * 1.2f + drape, c.X - r * 1.35f, c.Y - r * 0.2f, c.X - r * 0.8f, c.Y - r * 1.15f,
                c.X + r * 0.2f, c.Y - r * 1.45f, c.X + r * 1.2f, c.Y - r * 1.05f, c.X + r * 1.45f, c.Y - r * 0.2f,
                c.X + r * 0.55f, c.Y - r * 0.55f, c.X - r * 0.1f, c.Y + r * 0.3f, c.X + r * 0.2f, c.Y + r * 1.25f);
            F.Poly(cloth, (x, y) => -0.3f, c.X - r * 0.9f, c.Y - r * 0.9f, c.X - r * 2.6f, c.Y + r * 0.6f, c.X - r * 1.2f, c.Y + r * 0.2f);
        }
    }

    // ---- The soldiers ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Halberdier of the state troops: an upright, square-shouldered trooper in a morion with a plume in his colours, a
    /// breastplate over a dark doublet, a sash, a cloak; the halberd stands tall beside him and thrusts with a streak.
    /// </summary>
    private static void Infantry(Figure f, Pose pose)
    {
        var b = new Body(f, pose, Trooper);
        b.Shadow();
        b.Cloak(11f, 4.5f);
        var ns = b.NearShoulder;
        // Where the halberd is and where the hands hold it.
        Vector2 hand, butt, tip;
        if (pose.Strike)
        {
            float reach = pose.Follow ? 2f : 0f;
            hand = ns + new Vector2(6f + reach, 2.5f);
            butt = hand + new Vector2(-12f, 2f);
            tip = hand + new Vector2(9f, -1.5f);
        }
        else if (pose.Windup)
        {
            float back = pose.Index == 1 ? 2.5f : 0.5f;
            hand = ns + new Vector2(1.5f - back, 4f);
            butt = hand + new Vector2(-13f, 0.8f);
            tip = hand + new Vector2(7f, -0.6f);
        }
        else if (pose.Hurt)
        {
            hand = ns + new Vector2(3f, 5f);
            butt = new Vector2(hand.X + 4f, 37.5f);
            tip = hand + new Vector2(-3f, -14f);
        }
        else
        {
            float rock = pose.Act == Act.Walk ? b.Swing * 0.8f : 0f;
            hand = ns + new Vector2(5.8f, 5f);
            butt = new Vector2(hand.X + 0.4f - rock, 38f);
            tip = new Vector2(hand.X + 0.4f + rock, hand.Y - 14.5f);
        }
        var shaftBehind = butt + (tip - butt) * 0.3f;
        b.Arm(false, pose.Strike || pose.Windup ? shaftBehind : b.HangingHand(false), DoubletR, PlateR);
        b.Leg(false, TrousersR, BootR);
        b.Torso(DoubletR);
        b.Cuirass(PlateR);
        b.Sash();
        b.Leg(true, TrousersR, BootR);
        b.Face();
        b.Morion(PlateR);
        b.Plume(b.Head.X - b.B.HeadR * 0.4f, b.Head.Y - b.B.HeadR * 1.5f, 5f);
        Halberd(f, butt.X, butt.Y, tip.X, tip.Y);
        if (pose.Strike && !pose.Follow)
            f.Streak(butt.X, butt.Y - 2f, tip.X + 4f, tip.Y - 1f, 3f, SteelSmear);
        b.Arm(true, hand, DoubletR, PlateR);
    }

    /// <summary>
    /// A huntsman-archer, lean and stooping, in a deep dark hood and a leather jack, a scarf in his colours at the
    /// throat, a quiver on his back and a longbow taller than his chest; the loosed arrow leaves a streak.
    /// </summary>
    private static void Archer(Figure f, Pose pose)
    {
        var b = new Body(f, pose, Hunter);
        b.Shadow();
        b.Cloak(12f, 4f);
        // Quiver across the back, fletchings over the shoulder.
        var q0 = b.Neck + new Vector2(-3.2f, 1.5f);
        var q1 = b.Hip + new Vector2(-2.8f, -2f);
        f.Taper(q0.X, q0.Y, q1.X, q1.Y, 3.6f, 3f, LeatherR, -0.4f);
        f.Stroke(q0.X - 1f, q0.Y - 1.5f, q0.X + 0.5f, q0.Y - 2.5f, 1.4f, FeatherR, 0.2f);
        var ns = b.NearShoulder;
        Vector2 bowHand, drawHand;
        float pull = pose.Windup && pose.Index == 1 ? 2f : 0f;
        if (pose.Windup || pose.Strike)
        {
            bowHand = ns + new Vector2(8.5f, 0.8f);
            drawHand = pose.Windup ? b.Head + new Vector2(1.6f - pull, 2.6f) : b.Head + new Vector2(-3.8f, 2f);
        }
        else
        {
            bowHand = ns + new Vector2(5.5f, 5.5f);
            drawHand = b.HangingHand(false);
        }
        b.Arm(false, drawHand, OliveR);
        b.Leg(false, TrousersR, BootR);
        b.Torso(LeatherR, 0.3f);
        b.Belt();
        b.Leg(true, TrousersR, BootR);
        b.Face();
        b.Hood(OliveR, 0.5f);
        // The scarf at the throat, its end blowing back.
        var throat = b.Neck + new Vector2(0.6f, 0.6f);
        f.TeamStroke(throat.X - 2.2f, throat.Y, throat.X + 2f, throat.Y + 0.2f, 1.8f, 0.3f);
        f.TeamStroke(throat.X - 2f, throat.Y + 0.4f, throat.X - 4f - (pose.Act == Act.Walk ? 1f : 0f), throat.Y + 3f, 1.3f, -0.3f);
        // The bow (in the near, left hand), the string, an arrow while it is drawn.
        float half = 10f;
        float bend = pose.Windup ? 4.5f + pull * 0.5f : pose.Strike ? 3.5f : 3f;
        Bow(f, bowHand.X - 0.5f, bowHand.Y - half, bowHand.X - 0.5f, bowHand.Y + half, bend);
        if (pose.Windup)
        {
            f.Stroke(bowHand.X - 0.5f, bowHand.Y - half, drawHand.X, drawHand.Y, 0.8f, RopeR);
            f.Stroke(drawHand.X, drawHand.Y, bowHand.X - 0.5f, bowHand.Y + half, 0.8f, RopeR);
            f.Stroke(drawHand.X, drawHand.Y, bowHand.X + 5f, bowHand.Y, 1.1f, WoodR, 0.6f);
            f.Dot(bowHand.X + 4.5f, bowHand.Y - 0.5f, PlateR.Glint, 2f, 1f);
        }
        else
        {
            float quiver = pose.Strike ? (pose.Follow ? 0.6f : -0.6f) : 0f;
            f.Stroke(bowHand.X - 1f, bowHand.Y - half, bowHand.X - 1f + quiver, bowHand.Y, 0.8f, RopeR);
            f.Stroke(bowHand.X - 1f + quiver, bowHand.Y, bowHand.X - 1f, bowHand.Y + half, 0.8f, RopeR);
            if (pose.Strike && !pose.Follow)
                f.Streak(bowHand.X - 2f, bowHand.Y, 31f, bowHand.Y - 0.5f, 2f, SteelSmear);
        }
        b.Arm(true, bowHand, OliveR);
    }

    /// <summary>
    /// A knight of a templar order, broad and heavy, in blackened plate: a great helm with a brass cross and a short
    /// plume in his colours, a great pauldron with a purity seal, a dark cloak, a heater shield bearing his colours'
    /// cross, and a sword that cleaves an arc of light.
    /// </summary>
    private static void Heavy(Figure f, Pose pose)
    {
        var b = new Body(f, pose, Knight);
        b.Shadow();
        b.Cloak(14f, 5.5f);
        var ns = b.NearShoulder;
        // The sword arm is the far (right) one; the shield rides on the near (left) forearm.
        Vector2 swordHand, swordTip;
        if (pose.Windup)
        {
            swordHand = b.Neck + new Vector2(-3f - (pose.Index == 1 ? 1.5f : 0f), -4f);
            swordTip = swordHand + new Vector2(-9f, 6f);
        }
        else if (pose.Strike)
        {
            swordHand = ns + new Vector2(6.5f, pose.Follow ? 6f : 2f);
            swordTip = swordHand + (pose.Follow ? new Vector2(8f, 9f) : new Vector2(11f, 3f));
        }
        else
        {
            swordHand = b.HangingHand(false) + new Vector2(0f, -1.5f);
            swordTip = swordHand + new Vector2(-3f, -12f);
        }
        if (!pose.Strike)
        {
            b.Arm(false, swordHand, BlackSteelR);
            Sword(f, swordHand.X, swordHand.Y, swordTip.X, swordTip.Y);
        }
        b.Leg(false, BlackSteelR, BlackSteelR);
        b.Torso(BlackSteelR);
        b.Cuirass(PlateR);
        b.Belt();
        b.Leg(true, BlackSteelR, BlackSteelR);
        b.Face();
        b.GreatHelm(BlackSteelR);
        b.Plume(b.Head.X - b.B.HeadR * 0.5f, b.Head.Y - b.B.HeadR * 1.3f, 4.5f);
        var shieldHand = b.Arm(true, ns + new Vector2(3.5f, 6.5f), BlackSteelR);
        b.Pauldron(BlackSteelR, seal: true);
        // The heater shield: blackened steel rimmed in brass, the faction's cross on it.
        var s = shieldHand + new Vector2(1.8f, -0.5f);
        f.Poly(BrassR, (x, y) => 0.6f, s.X - 4.5f, s.Y - 6f, s.X + 4.5f, s.Y - 6f, s.X + 4.5f, s.Y + 1.5f, s.X, s.Y + 8f, s.X - 4.5f, s.Y + 1.5f);
        f.Poly(BlackSteelR, (x, y) => 1f - (x - s.X + 3.5f) * 0.28f - (y - s.Y + 5f) * 0.05f,
            s.X - 3.5f, s.Y - 5f, s.X + 3.5f, s.Y - 5f, s.X + 3.5f, s.Y + 1.3f, s.X, s.Y + 6.5f, s.X - 3.5f, s.Y + 1.3f);
        f.TeamStroke(s.X, s.Y - 4f, s.X, s.Y + 4.5f, 1.8f, 0.4f);
        f.TeamStroke(s.X - 2.6f, s.Y - 1f, s.X + 2.6f, s.Y - 1f, 1.8f, 0.4f);
        if (pose.Strike)
        {
            if (!pose.Follow)
                f.Smear(b.NearShoulder.X, b.NearShoulder.Y, 13f, -120f, 25f, 3.5f, SteelSmear);
            b.Arm(false, swordHand, BlackSteelR);
            Sword(f, swordHand.X, swordHand.Y, swordTip.X, swordTip.Y);
        }
    }

    /// <summary>
    /// A crossbowman, short and stocky, in a sallet and a quilted gambeson, a sash in his colours, a great pavise slung
    /// on his back bearing a band of his colours; winding up he shoulders the crossbow and aims, loosing he rocks back
    /// with the string snapped forward and the bolt streaking away.
    /// </summary>
    private static void Crossbowman(Figure f, Pose pose)
    {
        var b = new Body(f, pose, Stocky);
        b.Shadow();
        // The pavise slung on the back: a tall wooden shield, ridged down the middle, a band of his colours across it.
        var top = b.Neck + new Vector2(-5.5f, -2.5f);
        var bottom = b.Hip + new Vector2(-4.5f, 4f);
        f.Poly(PaviseR, (x, y) => 0.4f - (x - top.X) * 0.12f,
            top.X - 2.5f, top.Y, top.X + 2.5f, top.Y - 0.3f, bottom.X + 2.5f, bottom.Y, bottom.X - 2.5f, bottom.Y + 0.3f);
        f.Stroke(top.X, top.Y + 0.5f, bottom.X, bottom.Y - 0.5f, 0.9f, PaviseR, 1.2f);
        var band = top + (bottom - top) * 0.35f;
        f.TeamStroke(band.X - 2.4f, band.Y, band.X + 2.4f, band.Y - 0.2f, 2.4f, 0f);
        f.Stroke(top.X - 2.5f, top.Y, top.X + 2.5f, top.Y - 0.3f, 1f, IronR, 0.4f);
        var ns = b.NearShoulder;
        // The crossbow: carried at the hip, shouldered to aim, kicking up as it looses.
        Vector2 grip = pose.Windup ? ns + new Vector2(2f, 1f) : pose.Strike ? ns + new Vector2(1f, 0f) : ns + new Vector2(2.5f, 6.5f);
        float aim = pose.Strike && !pose.Follow ? -0.8f : 0f;
        var stockBack = grip + new Vector2(-3f, 0.5f);
        var nose = grip + new Vector2(9f, -0.5f + aim * 3f);
        b.Arm(false, grip + new Vector2(-1.5f, 0.8f), GambesonR);
        b.Leg(false, TrousersR, BootR);
        b.Torso(GambesonR, -0.2f);
        // The quilting: rows of stitches across the gambeson.
        for (float y = b.Neck.Y + 2.5f; y < b.Hip.Y - 1f; y += 2.4f)
        {
            float lean = (y - b.Neck.Y) / b.B.Torso * (b.Hip.X - b.Neck.X);
            f.Stroke(b.Neck.X + lean - b.B.Shoulder * 0.7f, y, b.Neck.X + lean + b.B.Shoulder * 0.6f, y, 0.5f, GambesonR, -1.4f);
        }
        b.Sash();
        b.Belt();
        b.Leg(true, TrousersR, BootR);
        b.Face();
        b.Sallet(PlateR);
        f.Stroke(stockBack.X, stockBack.Y, nose.X, nose.Y, 2.4f, DarkWoodR, 0.4f);
        f.Stroke(nose.X - 1f, nose.Y - 5f, nose.X - 1f, nose.Y + 4f, 2f, IronR, 0.5f);
        if (pose.Windup)
        {
            f.Stroke(nose.X - 1f, nose.Y - 5f, grip.X + 1f, grip.Y - 0.5f, 0.8f, RopeR);
            f.Stroke(grip.X + 1f, grip.Y - 0.5f, nose.X - 1f, nose.Y + 4f, 0.8f, RopeR);
            f.Stroke(grip.X + 1f, grip.Y - 1f, nose.X + 2f, nose.Y - 1f, 1.2f, WoodR, 0.7f);
        }
        else
        {
            f.Stroke(nose.X - 2f, nose.Y - 5f, nose.X - 2f, nose.Y + 4f, 0.8f, RopeR);
            if (pose.Strike && !pose.Follow)
                f.Streak(nose.X, nose.Y - 1f, 31f, nose.Y - 2f, 2f, SteelSmear);
        }
        b.Arm(true, grip, GambesonR);
    }

    /// <summary>
    /// A warrior priest, stooped under a dark habit and cowl, a stole in his colours, a skull and a chained book at his
    /// belt, a staff crowned with a gilded twin-tailed comet; his hem sways as he walks. To mend he raises the staff
    /// high, the comet blazing with holy light, the other hand lifted.
    /// </summary>
    private static void Monk(Figure f, Pose pose)
    {
        var b = new Body(f, pose, Priest);
        b.Shadow();
        // Sandalled feet peeking out under the habit.
        foreach (var (ankle, bias) in new[] { (b.FarAnkle, -0.8f), (b.NearAnkle, 0f) })
            f.Stroke(ankle.X - 1f, Mathf.Min(38.3f, ankle.Y + 1.2f), ankle.X + 1.5f, Mathf.Min(38.3f, ankle.Y + 1.2f), 1.8f, BootR, bias);
        bool blessing = pose.Windup || pose.Strike;
        var ns = b.NearShoulder;
        var staffHand = blessing ? b.Head + new Vector2(5f, pose.Windup && pose.Index == 0 ? 1f : -2f) : ns + new Vector2(4.5f, 6.5f);
        b.Arm(false, blessing ? b.Head + new Vector2(-3f, -3.5f) : b.Hip + new Vector2(1.5f, -2.5f), PriestRobeR);
        // The habit, widening to a hem that swings with the stride.
        float sway = Mathf.Sin(pose.Phase * Mathf.Tau) * 1.5f;
        var n = b.Neck;
        float hem = 38.5f;
        f.Poly(PriestRobeR, Round(n.X - 6f, n.X + 6f),
            n.X - b.B.Shoulder * 0.85f, n.Y + 0.4f, n.X + b.B.Shoulder * 0.6f, n.Y + 0.3f, n.X + b.B.Shoulder * 0.9f, n.Y + 4f,
            b.Hip.X + 5f + sway, hem, b.Hip.X - 5.5f + sway, hem, b.Hip.X - 4f, b.Hip.Y - 3f);
        f.Stroke(b.Hip.X - 5f + sway, hem - 0.5f, b.Hip.X + 4.5f + sway, hem - 0.5f, 1.2f, PriestRobeR, -1.2f);
        // The stole down the front, the rope belt, a skull and a chained book.
        f.TeamStroke(n.X + 1.6f, n.Y + 1f, b.Hip.X + 2.6f + sway * 0.4f, hem - 4f, 1.8f, 0.3f);
        b.Belt();
        var belt = b.Hip + new Vector2(-b.B.Waist * 0.6f, 1.2f);
        f.Ball(belt.X, belt.Y + 1f, 1.8f, 1.7f, SkullR, 0.3f);
        f.Dot(belt.X - 0.6f, belt.Y + 0.8f, Eye, 1f, 1f);
        f.Poly(LeatherR, (x, y) => 0.2f, belt.X + 1.5f, belt.Y + 0.5f, belt.X + 4f, belt.Y + 0.5f, belt.X + 4f, belt.Y + 4f, belt.X + 1.5f, belt.Y + 4f);
        f.Stroke(belt.X + 1.7f, belt.Y + 0.7f, belt.X + 1.7f, belt.Y + 3.8f, 0.7f, ParchmentR, 0.5f);
        b.Face();
        b.Hood(CloakR, 1f);
        var staffTop = staffHand + new Vector2(0f, blessing ? -10f : -14f);
        var staffFoot = blessing ? staffHand + new Vector2(0f, 8f) : new Vector2(staffHand.X, 38.5f);
        f.Stroke(staffFoot.X, staffFoot.Y, staffTop.X, staffTop.Y, 1.6f, DarkWoodR);
        Comet(f, staffTop.X, staffTop.Y, pose.Strike ? 1.8f : blessing ? 0.8f : 0.3f);
        if (pose.Strike && !pose.Follow)
            f.Smear(staffTop.X, staffTop.Y, 4.5f, 0f, 360f, 1.5f, Holy, 0.7f);
        b.Arm(true, staffHand, PriestRobeR);
    }

    /// <summary>
    /// The horde's warlord (half as big again, on the big sheet): a hulking, hunched brute in blackened mail, a horned
    /// helm with glowing eyes, a fur mantle, a dark cloak, a rag of his colours at his belt, and a great axe that
    /// cleaves a wide arc.
    /// </summary>
    private static void Warlord(Figure f, Pose pose)
    {
        var b = new Body(f, pose, Brute);
        b.Shadow();
        b.Cloak(15f, 6f);
        var ns = b.NearShoulder;
        Vector2 hand, head;
        if (pose.Windup)
        {
            hand = b.Neck + new Vector2(-2f - (pose.Index == 1 ? 1.5f : 0f), -3.5f);
            head = hand + new Vector2(-8f, -2f);
        }
        else if (pose.Strike)
        {
            hand = ns + new Vector2(6f, pose.Follow ? 7f : 3f);
            head = hand + (pose.Follow ? new Vector2(5f, 10f) : new Vector2(9f, 6f));
        }
        else
        {
            hand = ns + new Vector2(5f, 6f);
            head = new Vector2(hand.X + (pose.Act == Act.Walk ? b.Swing * 0.6f : 0f), hand.Y - 15f);
        }
        b.Arm(false, pose.Strike || pose.Windup ? hand + new Vector2(-2f, 1f) : b.HangingHand(false), MailR);
        b.Leg(false, MailR, BootR);
        b.Torso(MailR);
        for (float y = b.Neck.Y + 2f; y < b.Hip.Y; y += 2f)
        {
            float lean = (y - b.Neck.Y) / b.B.Torso * (b.Hip.X - b.Neck.X);
            f.Stroke(b.Neck.X + lean - b.B.Shoulder * 0.7f, y, b.Neck.X + lean + b.B.Shoulder * 0.65f, y, 0.6f, MailR, 0.6f);
        }
        b.Belt();
        var rag = b.Hip + new Vector2(-1f, 0.5f);
        f.TeamStroke(rag.X, rag.Y, rag.X - 0.5f, rag.Y + 5f + (pose.Passing ? 0.5f : 0f), 2.2f, -0.2f);
        b.Leg(true, MailR, BootR);
        f.Ball(b.Neck.X - 0.5f, b.Neck.Y + 1.5f, b.B.Shoulder * 1.3f, 2.6f, FurR, 0.3f);
        // Head in a horned helm, the eyes glowing out of its slit.
        var c = b.Head;
        float r = b.B.HeadR;
        f.Ball(c.X, c.Y, r * 1.2f, r * 1.25f, MailR, 0.4f);
        f.Dot(c.X - r * 0.1f, c.Y - r * 0.1f, Eye, 5f, 1.5f);
        f.Dot(c.X + r * 0.6f, c.Y - r * 0.1f, new Color("ff4a1a"), 1.4f, 1.2f);
        f.Stroke(c.X - r * 0.9f, c.Y - r * 0.6f, c.X - r * 1.7f, c.Y - r * 1.6f, 2f, HornR, 0.3f);
        f.Stroke(c.X - r * 1.7f, c.Y - r * 1.6f, c.X - r * 1.5f, c.Y - r * 2.6f, 1.4f, HornR, 0.5f);
        f.Stroke(c.X + r * 0.9f, c.Y - r * 0.6f, c.X + r * 1.7f, c.Y - r * 1.6f, 2f, HornR, 0.1f);
        f.Stroke(c.X + r * 1.7f, c.Y - r * 1.6f, c.X + r * 1.5f, c.Y - r * 2.6f, 1.4f, HornR, 0.3f);
        if (pose.Strike && !pose.Follow)
            f.Smear(ns.X, ns.Y, 14f, -110f, 45f, 4f, new Color("ff9a6a"));
        var foot = hand - (head - hand).Normalized() * 6f;
        Axe(f, foot.X, foot.Y, head.X, head.Y);
        b.Arm(true, hand, MailR);
    }

    /// <summary>
    /// A pistolier-knight: a bay at a gallop (its legs reaching and gathering through the cycle, bobbing mid-stride) in a
    /// dark caparison trimmed in the rider's colours; the rider in a breastplate and burgonet with a plume, a lance with
    /// a pennon in his colours — couched back to wind up, thrust level to strike with a streak of speed.
    /// </summary>
    private static void Cavalry(Figure f, Pose pose)
    {
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
        float beat = pose.Act switch
        {
            Act.Walk or Act.Idle => pose.Phase * 4f,
            Act.Attack => pose.Windup ? 0f : pose.Strike ? 1.5f : 1f,
            _ => pose.Index >= 1 ? 2f : 1.5f,
        };
        float rise = Mathf.Sin(beat / 4f * Mathf.Tau) * 1.5f;
        float rear = pose.Windup ? -3f : pose.Strike ? 1f : pose.Hurt ? 1f : 0f;
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
        float headX = X(pose.Strike ? 54f : pose.Hurt ? 49f : 51f), headY = Y(pose.Windup ? 22f : pose.Hurt ? 27f : 25f);
        f.Stroke(X(37f), Y(40f), (X(37f) + headX) / 2f + 1f, (Y(40f) + headY) / 2f - 1f, 6f, ScaleR, 0.1f);
        f.Stroke((X(37f) + headX) / 2f + 1f, (Y(40f) + headY) / 2f - 1f, headX - 2f, headY + 1f, 5f, ScaleR, 0.2f);
        f.Stroke(X(38f), Y(43f), (X(38f) + headX) / 2f + 2f, (Y(43f) + headY) / 2f + 1f, 2.2f, BellyR, -0.2f);
        // The harness: a collar strap in the master's colours and a banner on a pole at the withers.
        f.TeamStroke((X(37f) + headX) / 2f - 1f, (Y(40f) + headY) / 2f - 3f, (X(37f) + headX) / 2f + 3f, (Y(40f) + headY) / 2f + 2f, 1.6f, 0.2f);
        f.Stroke(X(33f), Y(36f), X(33f), Y(26f), 1f, DarkWoodR, 0.4f);
        f.Poly(null, (x, y) => 0.7f - (y - Y(26f)) * 0.15f, X(33f), Y(26f), X(26f), Y(26.5f) + swish * 0.5f, X(27.5f), Y(29f) + swish * 0.5f,
            X(26f), Y(31.5f) + swish * 0.5f, X(33f), Y(31f));
        f.Ball(headX, headY, 5f, 3.6f, ScaleR, 0.3f);
        bool open = pose.Windup || pose.Strike || pose.Hurt;
        f.Poly(ScaleR, (x, y) => 1f - (y - headY) * 0.4f, headX + 2f, headY - 2.5f, headX + 9f, headY - 1f, headX + 9f, headY + 1f, headX + 3f, headY + 1.5f);
        f.Poly(ScaleR, (x, y) => -0.2f, headX + 2f, headY + 2f, headX + 8f, headY + (open ? 5f : 2f), headX + 7f, headY + (open ? 6f : 3f), headX + 1f, headY + 3.5f);
        if (open)
            f.Poly(FlameR, (x, y) => pose.Windup || pose.Strike ? 0.5f : -2f, headX + 3f, headY + 1.6f, headX + 8.5f, headY + 1.2f, headX + 7.5f, headY + 4.5f);
        f.Dot(headX + 1f, headY - 1.5f, Ember, 1.5f, 1f);
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
