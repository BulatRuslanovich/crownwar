using Godot;

namespace Lordsio.UI;

/// <summary>Small eased motions that make the interface feel alive: panels fading in, buttons giving under the finger.</summary>
public static class UiMotion
{
    /// <summary>The panel appears out of nothing instead of popping into view.</summary>
    public static void FadeIn(CanvasItem item, double seconds = 0.18)
    {
        item.Modulate = item.Modulate with { A = 0f };
        item.CreateTween().TweenProperty(item, "modulate:a", 1f, seconds).SetEase(Tween.EaseType.Out);
    }

    /// <summary>A pressed button sinks a little (about its middle) and springs back when let go.</summary>
    public static void Squeeze(Control control, bool down)
    {
        control.PivotOffset = control.Size / 2f;
        var tween = control.CreateTween();
        if (down)
            tween.TweenProperty(control, "scale", new Vector2(0.94f, 0.94f), 0.05).SetEase(Tween.EaseType.Out);
        else
            tween.TweenProperty(control, "scale", Vector2.One, 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>A quick swell and settle, to draw the eye to a number that just jumped.</summary>
    public static void Pulse(Control control, float size = 1.18f)
    {
        control.PivotOffset = control.Size / 2f;
        control.Scale = new Vector2(size, size);
        control.CreateTween().TweenProperty(control, "scale", Vector2.One, 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }
}
