using System.Linq;
using Godot;
using Lordsio.Net;
using Lordsio.Sim.AI;
using Lordsio.Sim.Net;
using Lordsio.UI;

namespace Lordsio.Core;

/// <summary>Network games: the session from the network menu through the lobby to the match, and what happens when it fails.</summary>
public partial class Main
{
    private NetSession Session()
    {
        if (_session != null)
            return _session;
        _session = new NetSession { Name = "NetSession" };
        AddChild(_session);
        // Session events can fire inside a button handler of the very screen they replace,
        // so screen switches are deferred to the end of the frame.
        _session.LobbyUpdated += () => Callable.From(OnLobbyUpdated).CallDeferred();
        _session.MatchStarting += start => Callable.From(() => StartNetworkMatch(start)).CallDeferred();
        _session.Failed += reason => Callable.From(() => OnSessionFailed(reason)).CallDeferred();
        return _session;
    }

    private void EndSession()
    {
        if (_session == null)
            return;
        _session.Close();
        _session.QueueFree();
        _session = null;
    }

    private void ShowNetMenu(string? problemKey)
    {
        GetTree().Paused = false;
        EnsureBackdrop();
        var menu = NetMenuScene.Instantiate<NetMenu>();
        menu.Initialize(Session(), problemKey);
        menu.BackRequested += ShowMenu;
        Replace(menu);
    }

    private void OnLobbyUpdated()
    {
        if (_session == null || _session.InMatch)
            return;
        if (_screen is NetMenu)
        {
            var lobby = LobbyScene.Instantiate<Lobby>();
            lobby.Initialize(_session);
            lobby.LeaveRequested += () =>
            {
                _session?.Close();
                ShowNetMenu(null);
            };
            Replace(lobby);
        }
        if (_devAutoStartPlayers > 0 && _session.IsHost)
            DevAutoStart(_session);
    }

    private void StartNetworkMatch(StartMessage start)
    {
        if (_session == null)
            return;
        var slots = start.Slots.Select(s => new SlotConfig(
            s.Kind == NetSlotKind.Bot ? SlotKind.Bot : SlotKind.Human, s.Name, (BotDifficulty)s.Difficulty, s.Team)).ToList();
        var driver = new NetMatchDriver(_session, start) { LogChecksums = _devNetLog };
        var match = CreateMatch(new MatchConfig(slots, start.Seed, start.YourFaction), driver);
        match.MenuRequested += ShowMenu;
        if (_devAutoplay)
            match.AddBot(match.LocalFactionId, BotDifficulty.Hard).CommandSink = match.Issue;
        if (_devPauseTest)
        {
            // Dev: this player pauses the match for everyone at 20 s and resumes it at 26 s.
            GetTree().CreateTimer(20.0).Timeout += () => { if (IsInstanceValid(match)) match.SetPaused(true); };
            GetTree().CreateTimer(26.0, processAlways: true).Timeout += () => { if (IsInstanceValid(match)) match.SetPaused(false); };
        }
        if (_devDiplomacyTest)
            DevDiplomacyTest(match);
    }

    /// <summary>Lost connection, rejected, host gone: in a match the end screen explains; elsewhere back to the network menu.</summary>
    private void OnSessionFailed(string reasonKey)
    {
        if (_screen is MatchController match)
            match.ShowNetworkProblem(reasonKey);
        else
            ShowNetMenu(reasonKey);
    }
}
