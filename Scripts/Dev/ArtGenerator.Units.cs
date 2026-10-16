using System;
using Godot;
using Warcrown.World;

namespace Warcrown.Dev;

/// <summary>The unit sheet: every kind in every pose, with its faction-colour mask.</summary>
public static partial class ArtGenerator
{
    /// <summary>
    /// Cells of 16×20, feet at (8, 19), facing right. Rows: infantry, archer, heavy, ram, cavalry, crossbowman, monk,
    /// trebuchet, warlord. Columns (see
    /// <see cref="ArtLayout"/>): idle, breathing, four walking steps, wind-up and strike. units_team.png holds the
    /// faction-colour mask.
    /// </summary>
    private static PixelCanvas Units()
    {
        var c = new PixelCanvas(ArtLayout.UnitCellWidth * ArtLayout.UnitFrames, ArtLayout.UnitCellHeight * ArtLayout.UnitRows);
        for (int frame = 0; frame < ArtLayout.UnitFrames; frame++)
        {
            for (int row = 0; row < ArtLayout.UnitRows; row++)
            {
                var cell = new Vector2I(frame * ArtLayout.UnitCellWidth, row * ArtLayout.UnitCellHeight);
                c.Origin = cell;
                var pose = Pose.Of(frame);
                switch (row)
                {
                    case 0: Infantry(c, pose, cell); break;
                    case 1: Archer(c, pose, cell); break;
                    case 2: Heavy(c, pose, cell); break;
                    case 3: Ram(c, pose, frame); break;
                    case 4: Cavalry(c, pose, cell); break;
                    case 5: Crossbowman(c, pose, cell); break;
                    case 6: Monk(c, pose, cell); break;
                    case 7: Trebuchet(c, pose, frame); break;
                    default: Warlord(c, pose, cell); break;
                }
                c.Origin = cell;
                c.Outline(Outline, new Rect2I(cell, new Vector2I(ArtLayout.UnitCellWidth, ArtLayout.UnitCellHeight)));
            }
        }
        return c;
    }

    /// <summary>
    /// How a soldier stands in a frame: where the near and the far leg are (x of each, and which one is lifted), how
    /// far the upper body bobs (down while breathing, up mid-stride) and leans (back winding up, forward striking).
    /// </summary>
    private readonly record struct Pose(int Near, int Far, int NearLift, int FarLift, int Bob, int Lean, bool Windup, bool Strike, int Step)
    {
        public static Pose Of(int frame) => frame switch
        {
            ArtLayout.FrameBreath => new(9, 5, 0, 0, 1, 0, false, false, 0),
            ArtLayout.FrameWalk1 => new(10, 4, 0, 0, 0, 0, false, false, 1),
            ArtLayout.FrameWalk2 => new(8, 6, 1, 0, -1, 0, false, false, 2),
            ArtLayout.FrameWalk3 => new(4, 10, 0, 0, 0, 0, false, false, 3),
            ArtLayout.FrameWalk4 => new(6, 8, 0, 1, -1, 0, false, false, 4),
            ArtLayout.FrameWindup => new(10, 4, 0, 0, 0, -1, true, false, 0),
            ArtLayout.FrameStrike => new(11, 4, 0, 0, 0, 1, false, true, 0),
            _ => new(9, 5, 0, 0, 0, 0, false, false, 0),
        };
    }

    /// <summary>
    /// Both legs (the far one darker) and, by moving the canvas origin, the upper body's bob and lean: the caller draws
    /// the body after this with local coordinates as if standing straight.
    /// </summary>
    private static void Legs(PixelCanvas c, Pose pose, Vector2I cell, int width, Color near, Color far)
    {
        int top = 16 + Math.Min(0, pose.Bob);
        c.Rect(pose.Far, top - pose.FarLift, width, 19 - top, far);
        c.Rect(pose.Far, 18 - pose.FarLift, width, 1, Boots);
        c.Rect(pose.Near, top - pose.NearLift, width, 19 - top, near);
        c.Rect(pose.Near, 18 - pose.NearLift, width, 1, Boots);
        c.Origin = cell + new Vector2I(pose.Lean, pose.Bob);
    }

    /// <summary>Spearman: a crested helmet and a round shield on his back in his colours.</summary>
    private static void Infantry(PixelCanvas c, Pose pose, Vector2I cell)
    {
        Legs(c, pose, cell, 2, new Color("4a3a30"), new Color("33281f"));
        // Shield slung on the back, behind the body.
        for (int y = 9; y < 15; y++)
            for (int x = 2; x < 6; x++)
                if (!((x == 2 || x == 5) && (y == 9 || y == 14)))
                    c.Team(x, y, x == 2 ? 0.6f : 0.78f);
        c.Px(3, 11, SteelLight); // boss
        c.TeamRect(5, 10, 6, 6);
        c.Rect(5, 14, 6, 1, Bark);
        c.Shaded(6, 6, 4, 4, Skin, Skin, SkinDark);
        c.Rect(5, 5, 6, 2, Steel);
        c.Rect(6, 4, 4, 1, SteelLight);
        // Crest running back over the helmet.
        c.Team(6, 3, 1f); c.Team(7, 3, 0.9f); c.Team(8, 3, 0.8f); c.Team(5, 4, 0.75f); c.Team(4, 5, 0.6f);
        c.Px(9, 7, Outline); // eye
        if (pose.Strike)
        {
            // Spear thrust forward.
            c.Line(5, 13, 13, 9, Leather);
            c.Px(14, 8, Steel);
            c.Px(15, 8, SteelLight);
            c.Rect(11, 11, 1, 2, Skin);
        }
        else if (pose.Windup)
        {
            // Spear drawn back level at the hip, ready to thrust.
            c.Rect(1, 11, 11, 1, Leather);
            c.Px(12, 11, Steel);
            c.Px(13, 11, SteelLight);
            c.Rect(8, 11, 1, 2, Skin);
        }
        else
        {
            // Upright spear; on the march it rocks with the stride.
            int tilt = pose.Step is 1 or 3 ? 1 : 0;
            c.Rect(12, 4, 1, 14, Leather);
            c.Rect(12 + tilt, 2, 1, 2, SteelLight);
            c.Px(11 + tilt, 1, Steel);
            c.Rect(11, 11 + (pose.Step is 2 or 4 ? 1 : 0), 1, 2, Skin);
        }
    }

    /// <summary>Bowman in a hood and cape of his colours, with a quiver.</summary>
    private static void Archer(PixelCanvas c, Pose pose, Vector2I cell)
    {
        Legs(c, pose, cell, 2, new Color("4a3a30"), new Color("33281f"));
        // Cape behind, swinging back a little on the march.
        int flutter = pose.Step is 2 or 4 ? 1 : 0;
        for (int y = 9; y < 16; y++)
            c.Team(4 - (y > 12 ? flutter : 0), y, 0.62f);
        c.Rect(3, 9, 2, 5, Leather); // quiver
        c.Px(3, 8, String);
        c.Px(4, 7, String);
        c.Rect(5, 10, 6, 6, Hood);
        c.Rect(5, 10, 1, 6, HoodDark);
        c.Rect(5, 14, 6, 1, Bark);
        c.Shaded(6, 6, 4, 4, Skin, Skin, SkinDark);
        // Hood in the faction's colours.
        c.Team(5, 5, 0.95f); c.Team(6, 5, 1f); c.Team(7, 5, 1f); c.Team(8, 5, 0.9f); c.Team(9, 5, 0.8f);
        c.Team(6, 4, 0.9f); c.Team(7, 4, 0.85f); c.Team(8, 4, 0.75f);
        c.Team(5, 6, 0.7f); c.Team(5, 7, 0.65f); c.Team(5, 8, 0.6f); c.Team(6, 9, 0.7f); c.Team(7, 9, 0.7f);
        c.Px(9, 7, Outline); // eye
        if (pose.Windup)
        {
            // Bow drawn: string pulled back to the cheek, arrow on the string.
            c.Line(12, 6, 13, 9, Wood);
            c.Line(13, 10, 12, 14, Wood);
            c.Line(12, 6, 9, 10, String);
            c.Line(9, 10, 12, 14, String);
            c.Rect(9, 10, 6, 1, WoodLight);
            c.Px(15, 10, SteelLight);
            c.Px(10, 10, Skin);
        }
        else if (pose.Strike)
        {
            // Loosed: the arm out, the string still quivering, the arrow gone.
            c.Line(13, 6, 14, 9, Wood);
            c.Line(14, 10, 13, 14, Wood);
            c.Px(12, 7, String); c.Px(13, 9, String); c.Px(12, 11, String); c.Px(13, 13, String);
            c.Rect(10, 10, 3, 1, Skin);
        }
        else
        {
            c.Line(12, 6, 13, 8, Wood);
            c.Rect(13, 9, 1, 3, Wood);
            c.Line(13, 12, 12, 14, Wood);
            c.Rect(11, 7, 1, 7, String);
            c.Rect(11, 11, 1, 1, Skin);
        }
    }

    /// <summary>
    /// A covered battering ram on two wheels: the roof is in faction colours, the wheels turn while it rolls, the
    /// iron-headed log is drawn back to wind up and thrust out of the front to strike.
    /// </summary>
    private static void Ram(PixelCanvas c, Pose pose, int frame)
    {
        int bob = pose.Step is 2 or 4 ? -1 : 0;
        // Roof of hides over a timber frame.
        for (int row = 0; row < 5; row++)
        {
            int inset = 4 - row;
            for (int x = 1 + inset; x < 13 - inset; x++)
                c.Team(x, 6 + row + bob, row == 4 ? 0.6f : x < 7 ? 0.95f : 0.75f);
        }
        c.Rect(1, 11 + bob, 12, 4, Wood);
        for (int x = 2; x < 13; x += 3)
            c.Rect(x, 11 + bob, 1, 4, WoodDark);
        int thrust = pose.Strike ? 2 : pose.Windup ? -3 : 0;
        c.Rect(6 + thrust, 12 + bob, 7, 2, Bark);
        c.Rect(13 + thrust, 11 + bob, 1, 4, SteelDark);
        c.Px(13 + thrust, 11 + bob, Steel);
        // Wheels whose spoke turns a quarter each step.
        var spokes = new[] { new Vector2I(0, -2), new Vector2I(2, 0), new Vector2I(0, 2), new Vector2I(-2, 0) };
        foreach (int wx in new[] { 4, 11 })
        {
            c.Disc(wx, 16, 2.4f, WoodDark);
            c.Px(wx, 16, Steel);
            var spoke = spokes[pose.Step == 0 ? (frame == ArtLayout.FrameStrike ? 1 : 0) : pose.Step - 1];
            c.Px(wx + spoke.X, 16 + spoke.Y, WoodLight);
            c.Px(wx + spoke.X / 2, 16 + spoke.Y / 2, WoodLight);
        }
    }

    private static readonly Color Horse = new("7a5030");
    private static readonly Color HorseLight = new("93643f");
    private static readonly Color HorseDark = new("553620");
    private static readonly Color Mane = new("2e2018");
    private static readonly Color Robe = new("6b4a2e");
    private static readonly Color RobeDark = new("4e3420");
    private static readonly Color Holy = new("fff2a8");

    /// <summary>
    /// A horseman: a bay horse at a gallop (four strides of its legs, bobbing mid-stride) with a saddle cloth in the
    /// rider's colours, the rider in a team tunic with a plumed helmet and a lance — couched back to wind up, thrust
    /// level to strike.
    /// </summary>
    private static void Cavalry(PixelCanvas c, Pose pose, Vector2I cell)
    {
        int bob = pose.Step is 2 or 4 ? -1 : 0;
        // Legs: back pair and front pair, reaching and gathering in turn.
        (int backA, int backB, int frontA, int frontB, int lift) = pose.Step switch
        {
            1 => (2, 5, 10, 13, 0),
            2 => (3, 4, 11, 12, 1),
            3 => (4, 3, 12, 11, 0),
            4 => (5, 2, 13, 10, 1),
            _ => (3, 5, 10, 12, 0),
        };
        foreach (var (x, near) in new[] { (backB, false), (frontB, false), (backA, true), (frontA, true) })
        {
            int up = !near ? lift : 0;
            c.Rect(x, 15 + bob - up, 1, 4 - (bob < 0 ? 0 : 0), near ? Horse : HorseDark);
            c.Px(x, 18 - up, Boots);
        }
        c.Origin = cell + new Vector2I(0, bob);
        // Body, neck and head; mane and tail.
        c.Rect(2, 11, 11, 4, Horse);
        c.Rect(2, 11, 11, 1, HorseLight);
        c.Rect(3, 14, 9, 1, HorseDark);
        c.Rect(1, 11, 1, 3, Mane);
        c.Px(0, 13, Mane);
        c.Rect(11, 8, 2, 4, Horse);
        c.Rect(12, 7, 3, 2, Horse);
        c.Px(15, 8, HorseDark);
        c.Px(13, 7, Outline); // eye
        c.Rect(11, 7, 1, 3, Mane);
        // Saddle cloth in the rider's colours.
        for (int x = 5; x < 10; x++)
        {
            c.Team(x, 11, 0.9f);
            c.Team(x, 12, 0.75f);
        }
        c.Team(5, 13, 0.6f);
        c.Team(9, 13, 0.6f);
        // Rider.
        c.TeamRect(6, 6, 3, 5);
        c.Shaded(6, 3, 3, 3, Skin, Skin, SkinDark);
        c.Rect(6, 2, 3, 1, Steel);
        c.Team(6, 1, 1f); c.Team(5, 2, 0.75f); // plume
        c.Px(8, 4, Outline);
        if (pose.Windup)
        {
            c.Line(2, 5, 9, 8, Leather); // lance couched back
            c.Px(10, 8, SteelLight);
        }
        else if (pose.Strike)
        {
            c.Rect(8, 8, 8, 1, Leather); // lance thrust level
            c.Px(15, 7, SteelLight);
            c.Px(15, 8, SteelLight);
        }
        else
        {
            c.Line(9, 9, 14, 1, Leather);
            c.Px(14, 0, SteelLight);
            c.Px(15, 0, Steel);
        }
        c.Px(9, 8, Skin); // hand
    }

    /// <summary>
    /// A crossbowman in a kettle helmet and a tabard of his colours, the crossbow across his chest; winding up he
    /// shoulders it and aims, loosing he rocks back with the string snapped forward.
    /// </summary>
    private static void Crossbowman(PixelCanvas c, Pose pose, Vector2I cell)
    {
        Legs(c, pose, cell, 2, new Color("4a3a30"), new Color("33281f"));
        c.Rect(5, 10, 6, 6, Leather);
        c.TeamRect(6, 10, 4, 5); // tabard
        c.Rect(5, 14, 6, 1, Bark);
        c.Shaded(6, 6, 4, 4, Skin, Skin, SkinDark);
        c.Rect(5, 5, 6, 2, Steel); // kettle helmet with a broad brim
        c.Rect(4, 6, 8, 1, SteelDark);
        c.Px(9, 7, Outline);
        int y = pose.Windup ? 8 : pose.Strike ? 10 : 11;
        int x = pose.Strike ? 9 : 10;
        c.Rect(x - 3, y, 6, 1, Wood); // stock
        c.Rect(x + 3, y - 2, 1, 5, SteelDark); // the bow, across
        c.Px(x + 3, y - 2, Steel);
        if (pose.Windup)
        {
            c.Line(x + 3, y - 2, x, y, String);
            c.Line(x, y, x + 3, y + 2, String);
            c.Rect(x, y, 4, 1, WoodLight); // the bolt
            c.Px(x + 4, y, SteelLight);
        }
        else
        {
            c.Rect(x + 2, y - 2, 1, 5, String);
        }
        c.Px(x - 1, y + 1, Skin);
    }

    /// <summary>
    /// A monk in a brown habit with a cowl of his colours and a staff; his hem sways as he walks. To mend he raises the
    /// staff, its head shining.
    /// </summary>
    private static void Monk(PixelCanvas c, Pose pose, Vector2I cell)
    {
        // Feet peeking out under the habit as he walks.
        int step = pose.Step;
        c.Rect(step is 1 or 2 ? 9 : 6, 18, 2, 1, Boots);
        c.Rect(step is 3 or 4 ? 9 : 6, 18, 2, 1, Boots);
        c.Origin = cell + new Vector2I(pose.Lean, pose.Bob);
        // The habit widens to the hem, which swings with the stride.
        for (int row = 0; row < 9; row++)
        {
            int half = 3 + row / 3;
            int sway = row >= 6 && step is 2 or 4 ? (step == 2 ? 1 : -1) : 0;
            for (int x = 8 - half + sway; x < 8 + half + sway; x++)
                c.Px(x, 9 + row, x < 8 + sway ? Robe : RobeDark);
        }
        c.Rect(5, 13, 6, 1, String); // rope belt
        c.Px(6, 14, String);
        c.Shaded(6, 5, 4, 4, Skin, Skin, SkinDark);
        c.Px(9, 6, Outline);
        // Cowl in the faction's colours, falling over the shoulders.
        c.Team(6, 4, 1f); c.Team(7, 4, 1f); c.Team(8, 4, 0.9f); c.Team(5, 5, 0.85f); c.Team(5, 6, 0.75f); c.Team(5, 7, 0.7f);
        for (int x = 5; x < 11; x++)
            c.Team(x, 9, x < 8 ? 0.85f : 0.65f);
        if (pose.Windup || pose.Strike)
        {
            // Staff raised high, its head aglow; the other hand lifted in blessing.
            c.Rect(11, 0, 1, 12, Wood);
            c.Px(11, 0, Holy);
            c.Px(10, 0, pose.Strike ? Holy : Gold);
            c.Px(12, 0, pose.Strike ? Holy : Gold);
            c.Px(11, 1, Gold);
            c.Px(4, 7, Skin);
            c.Px(4, 6, Skin);
        }
        else
        {
            c.Rect(12, 3, 1, 16, Wood);
            c.Px(12, 2, WoodLight);
            c.Px(11, 11, Skin);
        }
    }

    /// <summary>
    /// A trebuchet on a wheeled bed: an A-frame with the throwing arm and a counterweight box in the owner's colours.
    /// Rolling, its wheels turn; winding up, the arm is hauled down with the weight raised; loosing, the arm flies over
    /// and the weight drops.
    /// </summary>
    private static void Trebuchet(PixelCanvas c, Pose pose, int frame)
    {
        // Bed and wheels.
        c.Rect(1, 13, 14, 2, Wood);
        c.Rect(1, 13, 14, 1, WoodLight);
        var spokes = new[] { new Vector2I(0, -2), new Vector2I(2, 0), new Vector2I(0, 2), new Vector2I(-2, 0) };
        foreach (int wx in new[] { 3, 12 })
        {
            c.Disc(wx, 16, 2.4f, WoodDark);
            c.Px(wx, 16, Steel);
            var spoke = spokes[pose.Step == 0 ? (frame == ArtLayout.FrameStrike ? 1 : 0) : pose.Step - 1];
            c.Px(wx + spoke.X, 16 + spoke.Y, WoodLight);
        }
        // A-frame up to the pivot.
        c.Line(4, 12, 7, 4, WoodDark);
        c.Line(10, 12, 8, 4, WoodDark);
        c.Rect(6, 4, 3, 1, Wood);
        // Arm: the long end with its sling, and the short end with the counterweight.
        (Vector2I longEnd, Vector2I weight) = pose.Windup ? (new Vector2I(1, 11), new Vector2I(11, 1))
            : pose.Strike ? (new Vector2I(14, 0), new Vector2I(3, 8))
            : (new Vector2I(2, 1), new Vector2I(11, 7));
        c.Line(7, 4, longEnd.X, longEnd.Y, Wood);
        c.Line(7, 4, (7 + weight.X) / 2, (4 + weight.Y) / 2, Wood);
        c.Px(longEnd.X, longEnd.Y, Leather);
        if (pose.Windup)
            c.Disc(longEnd.X + 1, longEnd.Y, 1.2f, RockLight); // the stone in the sling
        // Counterweight box in the faction's colours, iron-bound.
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                c.Team(weight.X - 1 + x, weight.Y + y, x == 0 ? 1f : 0.72f);
        c.Rect(weight.X - 1, weight.Y + 3, 3, 1, SteelDark);
    }

    /// <summary>
    /// The horde's warlord (drawn half as big again in the game): a hulking brute in blackened mail, a horned helm, a
    /// fur mantle, a cape in his colours and a great two-handed axe.
    /// </summary>
    private static void Warlord(PixelCanvas c, Pose pose, Vector2I cell)
    {
        var mail = new Color("3d3b40");
        var mailLight = new Color("5a5860");
        var fur = new Color("6b4a2e");
        var horn = new Color("e6dcc0");
        Legs(c, pose, cell, 3, new Color("2e2a26"), new Color("201d1a"));
        // Cape behind, in his colours, swinging on the march.
        int flutter = pose.Step is 2 or 4 ? 1 : 0;
        for (int y = 8; y < 17; y++)
        {
            c.Team(2 - (y > 12 ? flutter : 0), y, 0.55f);
            c.Team(3, y, 0.7f);
        }
        if (pose.Windup)
        {
            // The axe heaved up over the shoulder.
            c.Line(8, 9, 3, 2, Leather);
            c.Rect(1, 0, 3, 4, SteelLight);
            c.Px(0, 1, Steel);
        }
        else if (!pose.Strike)
        {
            int bob = pose.Step is 2 or 4 ? 1 : 0;
            c.Rect(13, 3 + bob, 1, 14, Leather);
            c.Rect(14, 3 + bob, 2, 5, SteelLight);
            c.Px(15, 2 + bob, Steel);
            c.Px(15, 8 + bob, Steel);
        }
        // A broad body of mail with a fur mantle over the shoulders.
        c.Shaded(4, 9, 9, 8, mail, mailLight, new Color("2a282c"));
        c.Rect(4, 15, 9, 1, Leather);
        c.Px(8, 15, Gold);
        c.Rect(3, 8, 11, 2, fur);
        c.Rect(4, 8, 9, 1, new Color("86613c"));
        // Head in a horned helm, eyes glowing.
        c.Shaded(5, 3, 6, 5, mail, mailLight, new Color("2a282c"));
        c.Rect(7, 5, 4, 1, Outline);
        c.Px(9, 5, new Color("ff5a2a"));
        c.Px(4, 3, horn); c.Px(3, 2, horn); c.Px(3, 1, horn);
        c.Px(11, 3, horn); c.Px(12, 2, horn); c.Px(12, 1, horn);
        if (pose.Strike)
        {
            // The axe brought down in a great arc in front.
            c.Line(9, 9, 14, 13, Leather);
            c.Rect(13, 13, 3, 4, SteelLight);
            c.Px(15, 17, Steel);
        }
    }

    /// <summary>Knight in plate with a plumed helm, a kite shield in his colours and a sword.</summary>
    private static void Heavy(PixelCanvas c, Pose pose, Vector2I cell)
    {
        Legs(c, pose, cell, 3, SteelDark, new Color("5d616a"));
        if (pose.Windup)
        {
            // Sword raised high behind the head.
            c.Line(7, 8, 3, 1, SteelLight);
            c.Px(2, 0, Steel);
            c.Rect(6, 8, 2, 1, Leather);
        }
        else if (!pose.Strike)
        {
            c.Rect(3, 3 + (pose.Step is 2 or 4 ? 1 : 0), 1, 8, SteelLight);
            c.Rect(2, 11 + (pose.Step is 2 or 4 ? 1 : 0), 3, 1, Leather);
        }
        c.Shaded(4, 9, 8, 7, Steel, SteelLight, SteelDark);
        c.Team(7, 9); c.Team(8, 9);
        for (int y = 10; y < 16; y++) { c.Team(7, y, 0.85f); c.Team(8, y, 0.7f); }
        c.Shaded(5, 3, 6, 6, Steel, SteelLight, SteelDark);
        c.Rect(7, 5, 4, 1, Outline); // visor
        // Plume on the helm, streaming back.
        c.Team(7, 2, 1f); c.Team(8, 2, 0.9f); c.Team(6, 2, 0.85f); c.Team(5, 2, 0.7f); c.Team(4, 3, 0.6f);
        c.Team(7, 1, 0.9f);
        // Kite shield in faction colours with a steel rim.
        c.TeamRect(11, 9, 4, 7);
        c.Rect(11, 9, 4, 1, SteelLight);
        c.Rect(11, 15, 4, 1, SteelDark);
        c.Px(12, 12, SteelLight);
        if (pose.Strike)
        {
            // Sword swung over and forward above the shield.
            c.Line(10, 8, 15, 3, SteelLight);
            c.Px(15, 2, Steel);
            c.Rect(9, 8, 2, 1, Leather);
        }
    }
}
