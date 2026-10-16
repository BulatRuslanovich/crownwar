using System.Collections.Generic;
using Godot;

namespace Warcrown.UI;

/// <summary>
/// The HUD's short messages, a few stacked with the newest on top: each fades in, stays a while and fades out, so a
/// burst of news in a busy fight is not lost the way a single line would overwrite it. A message repeated while still
/// shown just stays up longer.
/// </summary>
public partial class MessageFeed : VBoxContainer
{
    private const int MaxLines = 4;
    private const float FadeInSeconds = 0.15f;
    private const float FadeOutSeconds = 0.5f;

    private readonly List<(Label Label, float Age)> _lines = new();
    private Label _template = null!;
    private float _duration;

    /// <summary>Takes the place and the look of <paramref name="template"/> (a label of the HUD scene), which is hidden.</summary>
    public void Initialize(Label template, float duration)
    {
        _template = template;
        _duration = duration;
        MouseFilter = MouseFilterEnum.Ignore;
        AnchorLeft = template.AnchorLeft;
        AnchorRight = template.AnchorRight;
        AnchorTop = template.AnchorTop;
        AnchorBottom = template.AnchorTop;
        OffsetLeft = template.OffsetLeft;
        OffsetRight = template.OffsetRight;
        OffsetTop = template.OffsetTop;
        GrowHorizontal = template.GrowHorizontal;
        AddThemeConstantOverride("separation", 2);
        template.Visible = false;
    }

    public void Show(string text)
    {
        if (_lines.Count > 0 && _lines[0].Label.Text == text)
        {
            _lines[0] = (_lines[0].Label, Mathf.Min(_lines[0].Age, FadeInSeconds)); // said again: it stays up
            return;
        }
        var label = (Label)_template.Duplicate();
        label.Visible = true;
        label.Text = text;
        label.Modulate = Colors.Transparent;
        AddChild(label);
        MoveChild(label, 0);
        _lines.Insert(0, (label, 0f));
        while (_lines.Count > MaxLines)
            Drop(_lines.Count - 1);
    }

    public override void _Process(double delta)
    {
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            var (label, age) = _lines[i];
            age += (float)delta;
            if (age >= _duration)
            {
                Drop(i);
                continue;
            }
            _lines[i] = (label, age);
            float alpha = Mathf.Min(1f, age / FadeInSeconds) * Mathf.Clamp((_duration - age) / FadeOutSeconds, 0f, 1f);
            label.Modulate = Colors.White with { A = alpha };
        }
    }

    private void Drop(int index)
    {
        _lines[index].Label.QueueFree();
        _lines.RemoveAt(index);
    }
}
