using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lordsio.Core;

namespace Lordsio.UI;

/// <summary>
/// The end screen's graph: one line per side in its colour over the match's time, for the chosen measure (army value,
/// income or buildings). Minutes along the bottom, round values up the side, a faint grid; the local player's line is
/// drawn last and thicker, and a fallen side's line ends where it fell.
/// </summary>
public partial class StatsChart : Control
{
    public enum Measure { Army, Income, Buildings }

    private static readonly Color Back = new(0.05f, 0.04f, 0.03f, 0.85f);
    private static readonly Color Grid = new(1f, 0.9f, 0.7f, 0.08f);
    private static readonly Color Axis = UiPalette.TextMuted;
    private const float Left = 48f, Bottom = 22f, Top = 8f, Right = 12f;

    private IReadOnlyList<MatchStats.Sample> _samples = Array.Empty<MatchStats.Sample>();
    private IReadOnlyList<int> _factions = Array.Empty<int>();
    private int _local = -1;
    private Measure _measure = Measure.Army;

    public void Show(IReadOnlyList<MatchStats.Sample> samples, IReadOnlyList<int> factions, int localFaction)
    {
        _samples = samples;
        _factions = factions;
        _local = localFaction;
        QueueRedraw();
    }

    public Measure Showing
    {
        get => _measure;
        set
        {
            _measure = value;
            QueueRedraw();
        }
    }

    private int Value(MatchStats.Sample sample, int faction) =>
        sample.Sides.TryGetValue(faction, out var s)
            ? _measure switch { Measure.Income => s.Income, Measure.Buildings => s.Buildings, _ => s.ArmyValue }
            : 0;

    public override void _Draw()
    {
        var area = new Rect2(Left, Top, Size.X - Left - Right, Size.Y - Top - Bottom);
        DrawRect(new Rect2(Vector2.Zero, Size), Back);
        if (_samples.Count < 2 || area.Size.X <= 0 || area.Size.Y <= 0)
            return;
        var font = GetThemeDefaultFont();
        const int fontSize = 12;

        int maxTime = Math.Max(1, _samples[^1].Seconds);
        int maxValue = Math.Max(1, _samples.Max(s => _factions.Max(f => Value(s, f))));
        int step = NiceStep(maxValue / 4f);
        int top = (maxValue + step - 1) / step * step;
        Vector2 At(int seconds, int value) =>
            new(area.Position.X + area.Size.X * seconds / maxTime, area.End.Y - area.Size.Y * value / top);

        // Grid and values up the side.
        for (int v = 0; v <= top; v += step)
        {
            float y = At(0, v).Y;
            DrawLine(new Vector2(area.Position.X, y), new Vector2(area.End.X, y), Grid, 1f);
            DrawString(font, new Vector2(2f, y + fontSize / 2f - 2f), Short(v), HorizontalAlignment.Right, Left - 8f, fontSize, Axis);
        }
        // Minutes along the bottom.
        int minutes = maxTime / 60;
        int every = minutes <= 6 ? 1 : minutes <= 15 ? 2 : minutes <= 40 ? 5 : 10;
        for (int m = 0; m * 60 <= maxTime; m += every)
        {
            float x = At(m * 60, 0).X;
            DrawLine(new Vector2(x, area.Position.Y), new Vector2(x, area.End.Y), Grid, 1f);
            DrawString(font, new Vector2(x - 20f, area.End.Y + fontSize + 4f), $"{m}:00", HorizontalAlignment.Center, 40f, fontSize, Axis);
        }
        DrawLine(new Vector2(area.Position.X, area.End.Y), new Vector2(area.End.X, area.End.Y), Axis, 1f);

        // Lines, the player's on top.
        foreach (int faction in _factions.OrderBy(f => f == _local ? 1 : 0))
        {
            var points = new List<Vector2>();
            foreach (var sample in _samples)
            {
                if (!sample.Sides.ContainsKey(faction))
                    continue;
                points.Add(At(sample.Seconds, Value(sample, faction)));
            }
            if (points.Count < 2)
                continue;
            var color = FactionColors.Get(faction);
            float width = faction == _local ? 3f : 2f;
            DrawPolyline(points.ToArray(), color.Darkened(0.5f), width + 2f);
            DrawPolyline(points.ToArray(), color, width);
            DrawRect(new Rect2(points[^1] - Vector2.One * 2.5f, Vector2.One * 5f), color);
        }
    }

    /// <summary>A round grid step near <paramref name="raw"/>: 1, 2 or 5 times a power of ten.</summary>
    private static int NiceStep(float raw)
    {
        if (raw <= 1f)
            return 1;
        double power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double fraction = raw / power;
        double nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return (int)(nice * power);
    }

    private static string Short(int value) => value >= 10000 ? $"{value / 1000}k" : value.ToString();
}
