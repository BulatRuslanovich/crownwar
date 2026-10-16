using Godot;
using Warcrown.Core;

namespace Warcrown.UI;

/// <summary>
/// The top bar: gold (and the treasury limit), net income, army size, the day/night clock; in Defense also the next
/// wave and how many of the horde are still on the field.
/// </summary>
public partial class Hud
{
    private Label? _wave;
    private (int Next, int Left, int Alive, int Text) _waveShown = (-1, -1, -1, -1);
    private int _hordeCountedTick = -1;
    private int _hordeAlive;
    /// <summary>The numbers the bar last showed (see <see cref="Changed{T}"/>).</summary>
    private (int Gold, int Capacity, int Net, int Income, int Upkeep, int ArmyUpkeep, int Population, int Cap, bool Night, int Left, int Text) _barShown;

    /// <summary>A change of the treasury at least this big (loot, a purchase) rolls the counter and flashes it.</summary>
    private const int GoldJump = 25;
    private const float GoldFlashSeconds = 0.6f;
    private static readonly Color GoldGained = new(0.55f, 1f, 0.5f);
    private static readonly Color GoldSpent = new(1f, 0.7f, 0.45f);
    /// <summary>The sum the counter shows: it rolls towards the treasury rather than jumping.</summary>
    private float _goldShown = -1f;
    private float _goldFlash;
    private Color _goldFlashColor;

    private void UpdateResourceBar(float delta)
    {
        if (_match.Sim.GetFaction(_match.LocalFactionId) is { } faction)
        {
            var treasury = faction.Treasury;
            int shown = RollGold(treasury.Gold, treasury.IsFull, delta);
            int secondsLeft = Mathf.CeilToInt(_match.Sim.SecondsToDayNightChange);
            if (!Changed(ref _barShown, (shown, treasury.Capacity, faction.NetPerMinute, faction.IncomePerSecond,
                    faction.UpkeepPerMinute, faction.ArmyUpkeepPerMinute, faction.Population, faction.ArmyCap, _match.Sim.IsNight,
                    secondsLeft, _textVersion)))
                return;
            _gold.Text = treasury.Capacity > 0
                ? string.Format(Tr("HUD_GOLD_CAPPED"), shown, treasury.Capacity)
                : string.Format(Tr("HUD_GOLD"), shown);
            if (_goldFlash <= 0f)
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

    /// <summary>Defense: the wave counter on the top bar.</summary>
    private void AddWaveCounter()
    {
        if (_match.Sim.Defense == null)
            return;
        _wave = new Label { MouseFilter = Control.MouseFilterEnum.Pass, TooltipText = "HUD_WAVE_TOOLTIP" };
        GetNode("ResourceBar/Row").AddChild(_wave);
    }

    /// <summary>"Wave 4 in 1:20", and while some of the horde still stand, how many.</summary>
    private void UpdateWaveCounter()
    {
        if (_wave == null || _match.Sim.Defense is not { } defense)
            return;
        var sim = _match.Sim;
        if (sim.Tick != _hordeCountedTick)
        {
            _hordeCountedTick = sim.Tick;
            _hordeAlive = 0;
            foreach (var unit in sim.Units)
                if (unit.FactionId == defense.HordeFactionId)
                    _hordeAlive++;
        }
        int left = Mathf.CeilToInt(sim.SecondsToNextWave);
        if (!Changed(ref _waveShown, (defense.WavesLaunched + 1, left, _hordeAlive, _textVersion)))
            return;
        string text = string.Format(Tr("HUD_WAVE_IN"), defense.WavesLaunched + 1, TimeFormat.MinSec(left));
        if (_hordeAlive > 0)
            text += "   " + string.Format(Tr("HUD_HORDE_ALIVE"), _hordeAlive);
        _wave.Text = text;
        _wave.Modulate = left <= sim.Rules.Defense.WarningSeconds || _hordeAlive > 0 ? ProblemColor : Colors.White;
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

    /// <summary>
    /// The gold to show this frame: small changes (the income of a tick) at once, a jump rolled over a few tenths of a
    /// second, with a green (gain) or orange (spending) flash and a swell of the counter when it starts.
    /// </summary>
    private int RollGold(int gold, bool full, float delta)
    {
        if (_goldShown < 0f)
            _goldShown = gold;
        float jump = gold - _goldShown;
        if (Mathf.Abs(jump) >= GoldJump)
        {
            if (_goldFlash <= 0f)
                UiMotion.Pulse(_gold);
            _goldFlash = GoldFlashSeconds;
            _goldFlashColor = jump > 0f ? GoldGained : GoldSpent;
        }
        _goldShown = Mathf.Abs(jump) < 1f ? gold : Mathf.MoveToward(_goldShown, gold, Mathf.Max(60f, Mathf.Abs(jump) * 7f) * delta);
        if (_goldFlash > 0f)
        {
            _goldFlash = Mathf.Max(0f, _goldFlash - delta);
            var rest = full ? ProblemColor : Colors.White;
            _gold.Modulate = rest.Lerp(_goldFlashColor, _goldFlash / GoldFlashSeconds);
        }
        return Mathf.RoundToInt(_goldShown);
    }
}
