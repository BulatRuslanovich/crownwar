using Godot;
using Lordsio.Core;
using Lordsio.Sim.Core;

namespace Lordsio.UI;

/// <summary>
/// Victory / defeat panel with the player's statistics. On defeat the player may keep watching
/// the bots fight on; the panel then hides until the match ends.
/// </summary>
public partial class EndScreen : CanvasLayer
{
    private static readonly Color VictoryColor = new("ffe066");
    private static readonly Color DefeatColor = new("ff8a80");

    private MatchController _match = null!;
    private Label _title = null!;
    private Label _stats = null!;
    private Button _watch = null!;
    private Button _restart = null!;
    private bool _shownForDefeat;

    public void Initialize(MatchController match)
    {
        _match = match;
        _title = GetNode<Label>("Dim/Center/Panel/Rows/Title");
        _stats = GetNode<Label>("Dim/Center/Panel/Rows/Stats");
        _watch = GetNode<Button>("Dim/Center/Panel/Rows/Buttons/Watch");
        _restart = GetNode<Button>("Dim/Center/Panel/Rows/Buttons/Restart");
        _restart.Pressed += () => _match.RequestRestart();
        _restart.Visible = _match.Net == null; // a network match cannot be replayed alone
        GetNode<Button>("Dim/Center/Panel/Rows/Buttons/Menu").Pressed += () => _match.RequestMenu();
        _watch.Pressed += () => Visible = false;
        Visible = false;
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
                Present(ended.WinningTeam == _match.LocalTeam, matchOver: true);
                break;
        }
    }

    private void Present(bool victory, bool matchOver)
    {
        _title.Text = Tr(victory ? "END_VICTORY" : "END_DEFEAT");
        _title.Modulate = victory ? VictoryColor : DefeatColor;
        _watch.Visible = !matchOver;

        var s = _match.Stats;
        int seconds = _match.Sim.Tick / Simulation.TicksPerSecond;
        _stats.Text = string.Format(Tr("END_STATS"),
            seconds / 60, seconds % 60, s.UnitsTrained, s.UnitsLost, s.EnemiesKilled,
            s.BuildingsBuilt, s.BuildingsLost, s.BuildingsDestroyed);
        Visible = true;
        FocusFirstButton();
    }

    /// <summary>The network game broke off (desync, host left): say why; the only way on is the menu.</summary>
    public void ShowProblem(string reasonKey)
    {
        _title.Text = Tr("NET_GAME_INTERRUPTED");
        _title.Modulate = DefeatColor;
        _stats.Text = Tr(reasonKey);
        _watch.Visible = false;
        _restart.Visible = false;
        Visible = true;
        GetNode<Button>("Dim/Center/Panel/Rows/Buttons/Menu").GrabFocus();
    }

    private void FocusFirstButton() =>
        (_restart.Visible ? _restart : GetNode<Button>("Dim/Center/Panel/Rows/Buttons/Menu")).GrabFocus();
}
