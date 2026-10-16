using Godot;
using Warcrown.Sim.Core;

namespace Warcrown.UI;

/// <summary>Short messages in the middle of the screen: what happened to the player, diplomacy news, alerts.</summary>
public partial class Hud
{
    /// <summary>Reacts to simulation events that concern the local player.</summary>
    public void OnSimEvent(SimEvent e)
    {
        int player = _match.LocalFactionId;
        switch (e)
        {
            case RecruitRejected rejected when rejected.FactionId == player:
                ShowMessage(HudText.Describe(rejected.Reason));
                break;
            case BuildRejected rejected when rejected.FactionId == player:
                ShowMessage(HudText.DescribePlacement(rejected.Reason));
                break;
            case UnitRecruited recruited when recruited.FactionId == player && _match.Sim.GetUnit(recruited.UnitId) is { } unit:
                ShowMessage(string.Format(Tr("MSG_UNIT_READY"), Names.Of(unit.Type)));
                break;
            case BuildingCompleted completed when completed.FactionId == player && _match.Sim.GetBuilding(completed.BuildingId) is { } building:
                ShowMessage(string.Format(Tr("MSG_BUILDING_COMPLETED"), Names.Of(building.Type)));
                break;
            case BuildingDestroyed destroyed:
                string name = Names.Of(_match.Sim.Rules.GetBuildingType(destroyed.BuildingTypeId));
                if (destroyed.FactionId == player && _match.Sim.Defense != null && _match.Sim.Rules.GetBuildingType(destroyed.BuildingTypeId).IsCapital)
                    ShowMessage(string.Format(Tr("MSG_OUTPOST_LOST"), _match.Sim.Rules.Defense.OutpostCost));
                else if (destroyed.FactionId == player)
                    ShowMessage(string.Format(Tr("MSG_BUILDING_LOST"), name));
                else if (destroyed.KillerFactionId == player)
                    ShowMessage(string.Format(Tr("MSG_ENEMY_BUILDING_DESTROYED"), name));
                break;
            case BuildingUpgradeStarted started when started.FactionId == player && _match.Sim.GetBuilding(started.BuildingId) is { } building:
                ShowMessage(string.Format(Tr("MSG_UPGRADE_STARTED"), Names.Of(building.Type), building.Type.Tier + 1));
                break;
            case BuildingUpgraded upgraded when upgraded.FactionId == player && _match.Sim.GetBuilding(upgraded.BuildingId) is { } building:
                ShowMessage(string.Format(Tr("MSG_UPGRADE_DONE"), Names.Of(building.Type), upgraded.Tier));
                break;
            case UpgradeRejected rejected when rejected.FactionId == player:
                ShowMessage(HudText.Describe(rejected.Reason));
                break;
            case BuildingDemolished demolished when demolished.FactionId == player:
                ShowMessage(string.Format(Tr("MSG_DEMOLISHED"),
                    Names.Of(_match.Sim.Rules.GetBuildingType(demolished.BuildingTypeId)), demolished.Refund));
                break;
            case PeaceOffered or PeaceRefused or PeaceMade or WarDeclared or WarBegan or DiplomacyRejected:
                if (DescribeDiplomacy(e) is { } news)
                    ShowMessage(news);
                break;
            case BuildPlanned planned when planned.FactionId == player:
                ShowMessage(string.Format(Tr("MSG_BUILDER_SENT"), Names.Of(_match.Sim.Rules.GetBuildingType(planned.BuildingTypeId))));
                break;
            case BuildPlanCancelled cancelled when cancelled.FactionId == player:
                ShowMessage(string.Format(Tr("MSG_BUILD_PLAN_CANCELLED"),
                    Names.Of(_match.Sim.Rules.GetBuildingType(cancelled.BuildingTypeId)), cancelled.Refund));
                break;
            case GoldLooted loot when loot.FactionId == player:
                ShowMessage(string.Format(Tr("MSG_LOOTED"), loot.Amount));
                break;
            case DepositExhausted exhausted when exhausted.FactionId == player:
                ShowMessage(string.Format(Tr("MSG_DEPOSIT_EXHAUSTED"), _match.Sim.Rules.DepositRegenSeconds));
                break;
            case DepositRestored restored when restored.FactionId == player:
                ShowMessage(Tr("MSG_DEPOSIT_RESTORED"));
                break;
            case WaveIncoming incoming:
                ShowMessage(string.Format(Tr("MSG_WAVE_INCOMING"), incoming.Wave, Mathf.RoundToInt(incoming.Seconds)));
                _alert.PointAt(incoming.From, WaveArrowSeconds);
                break;
            case WaveStarted started:
                ShowMessage(string.Format(Tr(started.Wave % 10 == 0 ? "MSG_WAVE_BOSS" : "MSG_WAVE_STARTED"), started.Wave, started.Units));
                _alert.PointAt(started.From, WaveArrowSeconds);
                break;
            case WaveCleared cleared:
                ShowMessage(string.Format(Tr("MSG_WAVE_CLEARED"), cleared.Wave, cleared.Reward));
                break;
            case FactionEliminated eliminated when eliminated.FactionId != player:
                ShowMessage(string.Format(Tr("MSG_FACTION_FELL"), Names.Faction(eliminated.FactionId)));
                break;
        }
    }

    /// <summary>How long the screen-edge arrows point at where a wave comes in.</summary>
    private const float WaveArrowSeconds = 8f;

    /// <summary>Warns that the player is under attack and points at the place.</summary>
    public void ShowAlert(System.Numerics.Vector2 simPosition)
    {
        ShowMessage(Tr("MSG_UNDER_ATTACK"));
        _alert.PointAt(simPosition);
    }

    /// <summary>A short message in the middle of the screen, e.g. about other players in a network game.</summary>
    public void ShowNotice(string text) => ShowMessage(text);

    /// <summary>
    /// News of diplomacy: what concerns the player in full; treaties and wars between others too, since everyone
    /// should know who is fighting whom. Null for what is not worth a message.
    /// </summary>
    private string? DescribeDiplomacy(SimEvent e)
    {
        int me = _match.LocalFactionId;
        string Name(int faction) => Names.Player(_match, faction);
        return e switch
        {
            PeaceOffered o when o.OtherFactionId == me => string.Format(Tr("MSG_PEACE_OFFERED_TO_YOU"), Name(o.FactionId)),
            PeaceOffered o when o.FactionId == me => string.Format(Tr("MSG_PEACE_OFFER_SENT"), Name(o.OtherFactionId)),
            PeaceRefused r when r.OtherFactionId == me =>
                string.Format(Tr(r.Expired ? "MSG_PEACE_EXPIRED" : "MSG_PEACE_REFUSED"), Name(r.FactionId)),
            PeaceMade m when m.FactionId == me || m.OtherFactionId == me =>
                string.Format(Tr("MSG_PEACE_MADE"), Name(m.FactionId == me ? m.OtherFactionId : m.FactionId)),
            PeaceMade m => string.Format(Tr("MSG_PEACE_MADE_OTHERS"), Name(m.FactionId), Name(m.OtherFactionId)),
            WarDeclared w when w.OtherFactionId == me =>
                string.Format(Tr("MSG_WAR_DECLARED_ON_YOU"), Name(w.FactionId), Mathf.RoundToInt(w.Seconds)),
            WarDeclared w when w.FactionId == me =>
                string.Format(Tr("MSG_WAR_DECLARED_BY_YOU"), Name(w.OtherFactionId), Mathf.RoundToInt(w.Seconds)),
            WarDeclared w => string.Format(Tr("MSG_WAR_DECLARED_OTHERS"), Name(w.FactionId), Name(w.OtherFactionId)),
            WarBegan b when b.FactionId == me || b.OtherFactionId == me =>
                string.Format(Tr("MSG_WAR_BEGAN"), Name(b.FactionId == me ? b.OtherFactionId : b.FactionId)),
            DiplomacyRejected r when r.FactionId == me => Tr("MSG_DIPLOMACY_REJECTED"),
            _ => null,
        };
    }

    private void ShowMessage(string text)
    {
        _messages.Show(text);
    }
}
