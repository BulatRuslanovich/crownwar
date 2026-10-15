using Godot;
using Lordsio.Core;

namespace Lordsio.UI;

/// <summary>The top bar: gold (and the treasury limit), net income, army size, the day/night clock.</summary>
public partial class Hud
{
    /// <summary>The numbers the bar last showed (see <see cref="Changed{T}"/>).</summary>
    private (int Gold, int Capacity, int Net, int Income, int Upkeep, int ArmyUpkeep, int Population, int Cap, bool Night, int Left, int Text) _barShown;

    private void UpdateResourceBar()
    {
        if (_match.Sim.GetFaction(_match.LocalFactionId) is { } faction)
        {
            var treasury = faction.Treasury;
            int secondsLeft = Mathf.CeilToInt(_match.Sim.SecondsToDayNightChange);
            if (!Changed(ref _barShown, (treasury.Gold, treasury.Capacity, faction.NetPerMinute, faction.IncomePerSecond,
                    faction.UpkeepPerMinute, faction.ArmyUpkeepPerMinute, faction.Population, faction.ArmyCap, _match.Sim.IsNight,
                    secondsLeft, _textVersion)))
                return;
            _gold.Text = treasury.Capacity > 0
                ? string.Format(Tr("HUD_GOLD_CAPPED"), treasury.Gold, treasury.Capacity)
                : string.Format(Tr("HUD_GOLD"), treasury.Gold);
            _gold.Modulate = treasury.IsFull ? ProblemColor : Colors.White;
            _gold.TooltipText = treasury.Capacity > 0 ? string.Format(Tr("HUD_GOLD_CAP_TOOLTIP"), treasury.Capacity) : "";
            float net = faction.NetPerMinute / 60f;
            _income.Text = string.Format(Tr("HUD_INCOME"), net);
            _income.Modulate = net < 0f ? ProblemColor : Colors.White;
            _income.TooltipText = string.Format(Tr("HUD_INCOME_TOOLTIP"), faction.IncomePerSecond,
                faction.UpkeepPerMinute / 60f, faction.ArmyUpkeepPerMinute, _match.Sim.Rules.UpkeepAllowance);
            _army.Text = string.Format(Tr("HUD_ARMY"), faction.Population, faction.ArmyCap);
            _army.Modulate = faction.Population >= faction.ArmyCap ? ProblemColor : Colors.White;

            var sim = _match.Sim;
            _dayNight.Visible = sim.Rules.NightSeconds > 0f;
            int left = secondsLeft;
            _dayNight.Text = string.Format(Tr(sim.IsNight ? "HUD_NIGHT" : "HUD_DAY"), left / 60, left % 60);
            _dayNight.Modulate = sim.IsNight ? NightColor : Colors.White;
            _dayNight.TooltipText = Tr("HUD_NIGHT_TOOLTIP");
        }
    }

    /// <summary>A coin before the gold and a soldier before the army size.</summary>
    private void AddResourceIcons()
    {
        var row = _gold.GetParent();
        var coin = new TextureRect { Texture = Icons.Coin(), StretchMode = TextureRect.StretchModeEnum.KeepCentered };
        row.AddChild(coin);
        row.MoveChild(coin, _gold.GetIndex());
        var soldier = new TextureRect
        {
            Texture = Icons.Unit("infantry", FactionColors.Get(_match.LocalFactionId), scale: 1),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
        };
        row.AddChild(soldier);
        row.MoveChild(soldier, _army.GetIndex());
        ((HBoxContainer)row).AddThemeConstantOverride("separation", 10);
    }
}
