using System.Collections.Generic;
using System.Linq;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Core;

namespace Lordsio.UI;

/// <summary>
/// Diplomacy (P, or the button on the top bar in games with other people): every other side, how the player stands
/// with it, and what can be done — offer peace, accept or refuse an offer, declare war. Bots take no part: they are
/// always at war. All of it goes through <see cref="MatchController.Issue"/>, like any order.
/// Also owns the offer pop-up: someone offering the player peace is shown at the top of the screen with its time
/// running out and buttons to answer straight away.
/// </summary>
public partial class DiplomacyPanel : PanelContainer
{
    private static readonly Color WarColor = UiPalette.Problem;
    private static readonly Color PeaceColor = new("9be38a");
    private static readonly Color AllyColor = new("8fc8ff");
    private static readonly Color PendingColor = UiPalette.Highlight;

    private MatchController _match = null!;
    private readonly List<Row> _rows = new();
    private Label _hint = null!;
    private PanelContainer _popup = null!;
    private Label _popupText = null!;
    private int _popupFaction = -1;

    private sealed record Row(int Faction, Label Name, Label Status, Button First, Button Second);

    /// <param name="popupParent">Where the offer pop-up goes (the HUD layer): it shows while this panel is closed.</param>
    public void Initialize(MatchController match, Node popupParent)
    {
        _match = match;
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        GrowHorizontal = GrowDirection.Both;
        Position = new Vector2(Position.X, 90);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 10);
        AddChild(rows);
        rows.AddChild(new Label { Text = "DIPLO_TITLE", ThemeTypeVariation = "HeaderMedium", HorizontalAlignment = HorizontalAlignment.Center });
        _hint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(560, 0) };
        _hint.AddThemeFontSizeOverride("font_size", 14);
        rows.AddChild(_hint);

        // One line a side (not a grid: buttons come and go, and a grid would shift its columns when they hide).
        foreach (var faction in match.Sim.Factions.Where(f => f.CapitalId != 0 && f.Id != match.LocalFactionId))
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 14);
            rows.AddChild(line);
            line.AddChild(new ColorRect
            {
                Color = FactionColors.Get(faction.Id), CustomMinimumSize = new Vector2(14, 14),
                SizeFlagsVertical = SizeFlags.ShrinkCenter, SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            });
            var row = new Row(faction.Id, new Label { CustomMinimumSize = new Vector2(170, 0) }, new Label { CustomMinimumSize = new Vector2(250, 0) },
                MakeButton(), MakeButton());
            line.AddChild(row.Name);
            line.AddChild(row.Status);
            line.AddChild(row.First);
            line.AddChild(row.Second);
            row.First.Pressed += () => Act(row, first: true);
            row.Second.Pressed += () => Act(row, first: false);
            _rows.Add(row);
        }

        var close = MakeButton();
        close.Text = "DIPLO_CLOSE";
        close.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        close.Pressed += () => Visible = false;
        rows.AddChild(close);
        Visible = false;

        CreatePopup(popupParent);
    }

    /// <summary>Whether anyone is there to deal with: another side of another team played by a person.</summary>
    public bool HasCounterparts => _rows.Any(r => _match.Sim.StanceBetween(_match.LocalFactionId, r.Faction) != Stance.Ally
                                                  && !_match.IsBotControlled(r.Faction));

    public void Toggle() => Visible = !Visible;

    /// <summary>How the player stands with a side, as a short colored label (also used by the scoreboard).</summary>
    public static (string Text, Color Color) Describe(MatchController match, int faction)
    {
        var sim = match.Sim;
        int me = match.LocalFactionId;
        if (sim.GetFaction(faction) is { IsEliminated: true })
            return (TranslationServer.Translate("SCORE_FALLEN"), Colors.Gray);
        var stance = sim.StanceBetween(me, faction);
        if (stance == Stance.Ally)
            return (TranslationServer.Translate("DIPLO_ALLY"), AllyColor);
        if (sim.SecondsToWar(me, faction) is { } war)
            return (string.Format(TranslationServer.Translate("DIPLO_WAR_IN"), TimeFormat.MinSecLeft(war)), PendingColor);
        if (stance == Stance.Peace)
            return (TranslationServer.Translate("DIPLO_PEACE"), PeaceColor);
        if (sim.HasPeaceOffer(faction, me))
            return (string.Format(TranslationServer.Translate("DIPLO_THEY_OFFER"), TimeFormat.MinSecLeft(sim.PeaceOfferSecondsLeft(faction, me))), PendingColor);
        if (sim.HasPeaceOffer(me, faction))
            return (string.Format(TranslationServer.Translate("DIPLO_YOU_OFFERED"), TimeFormat.MinSecLeft(sim.PeaceOfferSecondsLeft(me, faction))), PendingColor);
        return (TranslationServer.Translate("DIPLO_WAR"), WarColor);
    }

    public override void _Process(double delta)
    {
        UpdatePopup();
        if (!Visible)
            return;
        var sim = _match.Sim;
        int me = _match.LocalFactionId;
        _hint.Text = string.Format(Tr("DIPLO_HINT"), Mathf.RoundToInt(sim.Rules.WarWarningSeconds));
        foreach (var row in _rows)
        {
            row.Name.Text = Names.Player(_match, row.Faction);
            (row.Status.Text, row.Status.Modulate) = Describe(_match, row.Faction);
            var (first, second) = Actions(row.Faction);
            Show(row.First, first);
            Show(row.Second, second);
            if (_match.IsBotControlled(row.Faction) && sim.StanceBetween(me, row.Faction) == Stance.War
                && sim.GetFaction(row.Faction) is { IsEliminated: false })
                row.Status.Text = Tr("DIPLO_BOT");
        }

        static void Show(Button button, string? text)
        {
            button.Visible = text != null;
            if (text != null)
                button.Text = text;
        }
    }

    /// <summary>What the player can do about a side now: up to two buttons (translation keys), or none.</summary>
    private (string? First, string? Second) Actions(int faction)
    {
        var sim = _match.Sim;
        int me = _match.LocalFactionId;
        if (sim.GetFaction(faction) is not { IsEliminated: false } || sim.GetFaction(me) is not { IsEliminated: false })
            return (null, null);
        var stance = sim.StanceBetween(me, faction);
        if (stance == Stance.Ally || _match.IsBotControlled(faction) && stance == Stance.War)
            return (null, null);
        if (stance == Stance.Peace)
            return (sim.SecondsToWar(me, faction) == null ? "DIPLO_DECLARE_WAR" : null, null);
        if (sim.HasPeaceOffer(faction, me))
            return ("DIPLO_ACCEPT", "DIPLO_REFUSE");
        return (sim.HasPeaceOffer(me, faction) ? null : "DIPLO_OFFER_PEACE", null);
    }

    private void Act(Row row, bool first)
    {
        int me = _match.LocalFactionId;
        var (firstAction, secondAction) = Actions(row.Faction);
        Command? command = (first ? firstAction : secondAction) switch
        {
            "DIPLO_OFFER_PEACE" => new ProposePeaceCommand(me, row.Faction),
            "DIPLO_ACCEPT" => new AnswerPeaceCommand(me, row.Faction, Accept: true),
            "DIPLO_REFUSE" => new AnswerPeaceCommand(me, row.Faction, Accept: false),
            "DIPLO_DECLARE_WAR" => new DeclareWarCommand(me, row.Faction),
            _ => null,
        };
        if (command != null)
            _match.Issue(command);
    }

    /// <summary>The pop-up for an offer of peace to the player: who offers, how long it stands, and the answers.</summary>
    private void CreatePopup(Node parent)
    {
        _popup = new PanelContainer { Name = "PeaceOffer", Visible = false };
        parent.AddChild(_popup);
        _popup.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        _popup.GrowHorizontal = GrowDirection.Both;
        _popup.Position = new Vector2(_popup.Position.X, 90);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        _popup.AddChild(row);
        _popupText = new Label { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        row.AddChild(_popupText);
        var accept = MakeButton();
        accept.Text = "DIPLO_ACCEPT";
        accept.Pressed += () => Answer(true);
        row.AddChild(accept);
        var refuse = MakeButton();
        refuse.Text = "DIPLO_REFUSE";
        refuse.Pressed += () => Answer(false);
        row.AddChild(refuse);
    }

    private void UpdatePopup()
    {
        var sim = _match.Sim;
        int me = _match.LocalFactionId;
        _popupFaction = Visible ? -1 : _rows.Select(r => r.Faction).FirstOrDefault(f => sim.HasPeaceOffer(f, me), -1);
        _popup.Visible = _popupFaction >= 0 && !_match.IsShowcase;
        if (_popup.Visible)
            _popupText.Text = string.Format(Tr("DIPLO_POPUP"), Names.Player(_match, _popupFaction), TimeFormat.MinSecLeft(sim.PeaceOfferSecondsLeft(_popupFaction, me)));
    }

    private void Answer(bool accept)
    {
        if (_popupFaction >= 0)
            _match.Issue(new AnswerPeaceCommand(_match.LocalFactionId, _popupFaction, accept));
    }

    private static Button MakeButton()
    {
        var button = new Button { FocusMode = FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 14);
        return button;
    }

}
