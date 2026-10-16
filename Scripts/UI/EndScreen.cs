using System.Collections.Generic;
using System.Linq;
using Godot;
using Warcrown.Core;
using Warcrown.Sim.Core;

namespace Warcrown.UI;

/// <summary>
/// Victory / defeat panel with the match in review: how long it lasted and who won, awards for the best general,
/// builder, treasurer and destroyer, a graph of every side's army, income or buildings over time, and a table of
/// what each side trained, killed, lost, built, razed and earned. On defeat the player may keep watching the bots
/// fight on; the panel then hides until the match ends.
/// </summary>
public partial class EndScreen : CanvasLayer
{
    private Button _menuButton = null!;
    private static readonly Color VictoryColor = UiPalette.Highlight;
    private static readonly Color DefeatColor = UiPalette.Problem;
    private static readonly Color LocalColor = UiPalette.Highlight;
    private static readonly Color Muted = UiPalette.TextMuted;

    private MatchController _match = null!;
    private Label _title = null!;
    private Label _stats = null!;
    private Button _watch = null!;
    private Button _restart = null!;
    private VBoxContainer _details = null!;
    private HBoxContainer _awards = null!;
    private StatsChart _chart = null!;
    private GridContainer _table = null!;
    private readonly List<Button> _measureButtons = new();
    private bool _shownForDefeat;
    private int _winningTeam = -1;

    public void Initialize(MatchController match)
    {
        _match = match;
        _title = GetNode<Label>("Dim/Center/Panel/Rows/Title");
        _stats = GetNode<Label>("Dim/Center/Panel/Rows/Stats");
        _stats.HorizontalAlignment = HorizontalAlignment.Center;
        _watch = GetNode<Button>("Dim/Center/Panel/Rows/Buttons/Watch");
        _restart = GetNode<Button>("Dim/Center/Panel/Rows/Buttons/Restart");
        _restart.Pressed += () => _match.RequestRestart();
        _restart.Visible = _match.Net == null; // a network match cannot be replayed alone
        (_menuButton = GetNode<Button>("Dim/Center/Panel/Rows/Buttons/Menu")).Pressed += () => _match.RequestMenu();
        _watch.Pressed += () => Visible = false;
        BuildDetails(GetNode<VBoxContainer>("Dim/Center/Panel/Rows"));
        Visible = false;
    }

    /// <summary>Awards, the graph with its measure buttons and the table, under the title.</summary>
    private void BuildDetails(VBoxContainer rows)
    {
        rows.CustomMinimumSize = new Vector2(900, 0);
        _details = new VBoxContainer { Name = "Details" };
        _details.AddThemeConstantOverride("separation", 10);
        rows.AddChild(_details);
        rows.MoveChild(_details, _stats.GetIndex() + 1);

        _awards = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _awards.AddThemeConstantOverride("separation", 28);
        _details.AddChild(_awards);

        var measures = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        measures.AddThemeConstantOverride("separation", 8);
        _details.AddChild(measures);
        foreach (var (measure, key) in new[]
                 {
                     (StatsChart.Measure.Army, "STATS_ARMY"), (StatsChart.Measure.Income, "STATS_INCOME"),
                     (StatsChart.Measure.Buildings, "STATS_BUILDINGS"),
                 })
        {
            var button = new Button { Text = key, ToggleMode = true, FocusMode = Control.FocusModeEnum.None };
            button.AddThemeFontSizeOverride("font_size", 14);
            button.Pressed += () => ShowMeasure(measure);
            measures.AddChild(button);
            _measureButtons.Add(button);
        }

        _chart = new StatsChart { CustomMinimumSize = new Vector2(880, 200) };
        _details.AddChild(_chart);

        _table = new GridContainer { Columns = 9 };
        _table.AddThemeConstantOverride("h_separation", 18);
        _table.AddThemeConstantOverride("v_separation", 6);
        _details.AddChild(_table);
    }

    private void ShowMeasure(StatsChart.Measure measure)
    {
        _chart.Showing = measure;
        for (int i = 0; i < _measureButtons.Count; i++)
            _measureButtons[i].ButtonPressed = i == (int)measure;
    }

    public void OnSimEvent(SimEvent e)
    {
        int player = _match.LocalFactionId;
        switch (e)
        {
            case FactionEliminated eliminated when eliminated.FactionId == player && !_shownForDefeat:
                _shownForDefeat = true;
                Present(victory: false, matchOver: false);
                break;
            case MatchEnded ended:
                _winningTeam = ended.WinningTeam;
                Present(ended.WinningTeam == _match.LocalTeam, matchOver: true);
                break;
        }
    }

    private void Present(bool victory, bool matchOver)
    {
        var defense = _match.Sim.Defense;
        _title.Text = Tr(defense != null && matchOver ? "END_CITADEL_FELL" : victory ? "END_VICTORY" : "END_DEFEAT");
        _title.Modulate = victory ? VictoryColor : DefeatColor;
        _watch.Visible = !matchOver;
        _details.Visible = true;

        var sim = _match.Sim;
        _match.Stats.Finish(sim);
        int seconds = sim.Tick / Simulation.TicksPerSecond;
        var sides = sim.Factions.Where(f => f.CapitalId != 0).Select(f => f.Id).ToList();
        var winners = sides.Where(f => _winningTeam >= 0 && sim.GetFaction(f)!.Team == _winningTeam).ToList();
        string text = string.Format(Tr("END_DURATION"), seconds / 60, seconds % 60);
        if (winners.Count > 0)
            text += "   ·   " + string.Format(Tr("END_WINNER"), string.Join(", ", winners.Select(f => Names.Player(_match, f))));
        if (defense != null)
            text += "   ·   " + string.Format(Tr("END_WAVES_HELD"), defense.WavesCleared, defense.WavesLaunched);
        _stats.Text = text;

        FillAwards(sides);
        _chart.Show(_match.Stats.Samples, sides, _match.LocalFactionId);
        ShowMeasure(_chart.Showing);
        FillTable(sides, winners);
        Visible = true;
        UiMotion.FadeIn(GetNode<CanvasItem>("Dim"), 0.4);
        FocusFirstButton();
    }

    /// <summary>The best general, builder, treasurer and destroyer of the match (nobody gets an award for nothing).</summary>
    private void FillAwards(List<int> sides)
    {
        foreach (var child in _awards.GetChildren())
            child.QueueFree();
        var stats = _match.Stats;
        foreach (var (title, hint, value) in new (string, string, System.Func<int, int>)[]
                 {
                     ("AWARD_WARLORD", "AWARD_WARLORD_HINT", f => stats.Of(f).Killed),
                     ("AWARD_BUILDER", "AWARD_BUILDER_HINT", f => stats.Of(f).Built),
                     ("AWARD_TREASURER", "AWARD_TREASURER_HINT", f => stats.Of(f).GoldEarned + stats.Of(f).GoldLooted),
                     ("AWARD_DESTROYER", "AWARD_DESTROYER_HINT", f => stats.Of(f).Razed),
                 })
        {
            int best = sides.OrderByDescending(value).ThenBy(f => f).FirstOrDefault(-1);
            if (best < 0 || value(best) <= 0)
                continue;
            var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            box.AddThemeConstantOverride("separation", 2);
            box.AddChild(new Label { Text = title, ThemeTypeVariation = "HeaderSmall", HorizontalAlignment = HorizontalAlignment.Center });
            var name = new Label { Text = Names.Player(_match, best), HorizontalAlignment = HorizontalAlignment.Center };
            name.AddThemeColorOverride("font_color", FactionColors.Get(best).Lightened(0.25f));
            box.AddChild(name);
            var detail = new Label { Text = string.Format(Tr(hint), value(best)), HorizontalAlignment = HorizontalAlignment.Center, Modulate = Muted };
            detail.AddThemeFontSizeOverride("font_size", 13);
            box.AddChild(detail);
            _awards.AddChild(box);
        }
    }

    /// <summary>One row a side, the winners first, then by enemies killed; the player's row in gold.</summary>
    private void FillTable(List<int> sides, List<int> winners)
    {
        foreach (var child in _table.GetChildren())
            child.QueueFree();
        var stats = _match.Stats;
        void Header(string key) => _table.AddChild(new Label { Text = key, Modulate = Muted, HorizontalAlignment = HorizontalAlignment.Right });
        _table.AddChild(new Control());
        _table.AddChild(new Label { Text = "SCORE_PLAYER", Modulate = Muted });
        foreach (string key in new[] { "STATS_TRAINED", "STATS_KILLED", "STATS_LOST", "STATS_BUILT", "STATS_RAZED", "STATS_GOLD", "SCORE_STATUS" })
            Header(key);

        var crown = GD.Load<Texture2D>("res://Assets/UI/logo.png");
        foreach (int f in sides.OrderByDescending(f => winners.Contains(f)).ThenByDescending(f => stats.Of(f).Killed))
        {
            var side = stats.Of(f);
            var mark = new HBoxContainer();
            mark.AddChild(new ColorRect { Color = FactionColors.Get(f), CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            mark.AddChild(new TextureRect
            {
                Texture = winners.Contains(f) ? crown : null, CustomMinimumSize = new Vector2(24, 16),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
            _table.AddChild(mark);
            var name = new Label { Text = Names.Player(_match, f) };
            if (f == _match.LocalFactionId)
                name.AddThemeColorOverride("font_color", LocalColor);
            _table.AddChild(name);
            foreach (int number in new[] { side.Trained, side.Killed, side.Lost, side.Built, side.Razed, side.GoldEarned + side.GoldLooted })
                _table.AddChild(new Label { Text = number.ToString(), HorizontalAlignment = HorizontalAlignment.Right });
            bool fallen = _match.Sim.GetFaction(f)!.IsEliminated;
            _table.AddChild(new Label
            {
                Text = Tr(fallen ? "SCORE_FALLEN" : "SCORE_ALIVE"), HorizontalAlignment = HorizontalAlignment.Right,
                Modulate = fallen ? DefeatColor : Colors.White,
            });
        }
    }

    /// <summary>The network game broke off (desync, host left): say why; the only way on is the menu.</summary>
    public void ShowProblem(string reasonKey)
    {
        _title.Text = Tr("NET_GAME_INTERRUPTED");
        _title.Modulate = DefeatColor;
        _stats.Text = Tr(reasonKey);
        _details.Visible = false;
        _watch.Visible = false;
        _restart.Visible = false;
        Visible = true;
        UiMotion.FadeIn(GetNode<CanvasItem>("Dim"), 0.4);
        _menuButton.GrabFocus();
    }

    private void FocusFirstButton() =>
        (_restart.Visible ? _restart : _menuButton).GrabFocus();
}
