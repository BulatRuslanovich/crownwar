using System.Collections.Generic;
using System.Linq;
using Godot;
using Warcrown.Core;

namespace Warcrown.UI;

/// <summary>
/// Held Tab: every side of the match — colour, name, team, how it stands with the player (ally, peace, war, a war
/// coming), still standing or fallen, and army and buildings for the player and allies. Enemy numbers stay hidden
/// ("?") unless the fog has lifted.
/// </summary>
public partial class Scoreboard : PanelContainer
{
    private MatchController _match = null!;
    private GridContainer _grid = null!;
    private readonly List<(ColorRect Swatch, Label Name, Label Team, Label Stance, Label Status, Label Army, Label Buildings, int Faction)> _rows = new();

    public void Initialize(MatchController match)
    {
        _match = match;
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        GrowHorizontal = GrowDirection.Both;
        Position = new Vector2(Position.X, 80);
        MouseFilter = MouseFilterEnum.Ignore;
        _grid = new GridContainer { Columns = 7 };
        _grid.AddThemeConstantOverride("h_separation", 20);
        _grid.AddThemeConstantOverride("v_separation", 8);
        AddChild(_grid);

        foreach (string header in new[] { "", "SCORE_PLAYER", "SCORE_TEAM", "SCORE_STANCE", "SCORE_STATUS", "SCORE_ARMY", "SCORE_BUILDINGS" })
            _grid.AddChild(new Label { Text = header, ThemeTypeVariation = "HeaderSmall" });
        foreach (var faction in _match.Sim.Factions.Where(f => f.CapitalId != 0))
        {
            var swatch = new ColorRect { Color = FactionColors.Get(faction.Id), CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            var row = (swatch, new Label(), new Label(), new Label(), new Label(), new Label(), new Label(), faction.Id);
            _grid.AddChild(row.swatch);
            _grid.AddChild(row.Item2);
            _grid.AddChild(row.Item3);
            _grid.AddChild(row.Item4);
            _grid.AddChild(row.Item5);
            _grid.AddChild(row.Item6);
            _grid.AddChild(row.Item7);
            _rows.Add(row);
        }
        Visible = false;
    }

    /// <summary>Dev: keep the table open (for screenshots).</summary>
    public bool Pinned { get; set; }

    public override void _Process(double delta)
    {
        Visible = Pinned || Input.IsPhysicalKeyPressed(Key.Tab);
        if (!Visible)
            return;
        int localTeam = _match.LocalTeam;
        foreach (var (_, name, team, stance, status, army, buildings, id) in _rows)
        {
            var faction = _match.Sim.GetFaction(id)!;
            bool known = faction.Team == localTeam || _match.Fog.RevealedAll;
            name.Text = Names.Player(_match, id);
            (stance.Text, stance.Modulate) = id == _match.LocalFactionId ? ("", Colors.White) : DiplomacyPanel.Describe(_match, id);
            name.Modulate = id == _match.LocalFactionId ? UiPalette.Highlight : Colors.White;
            team.Text = string.Format(Tr("NET_TEAM_N"), faction.Team + 1);
            status.Text = Tr(faction.IsEliminated ? "SCORE_FALLEN" : "SCORE_ALIVE");
            status.Modulate = faction.IsEliminated ? UiPalette.Problem : Colors.White;
            army.Text = known ? faction.Population.ToString() : "?";
            buildings.Text = known ? _match.Sim.Buildings.Count(b => b.FactionId == id && b.IsAlive).ToString() : "?";
        }
    }
}
