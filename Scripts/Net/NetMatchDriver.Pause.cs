using System;
using System.Collections.Generic;
using System.Linq;
using Lordsio.Sim.Core;
using Lordsio.Sim.Net;

namespace Lordsio.Net;

/// <summary>Pausing a network match: everyone stops (and goes on) together, the host decides.</summary>
public sealed partial class NetMatchDriver
{
    /// <summary>Someone paused the match: no frames are made, every machine stands still with the pause menu open.</summary>
    public bool IsPausedByPlayer { get; private set; }

    /// <summary>Who paused the match.</summary>
    public string PausedBy { get; private set; } = "";

    public event Action? PauseChanged;

    /// <summary>Asks to pause or resume for everyone (the host decides and tells all).</summary>
    public void RequestPause(bool paused)
    {
        if (_host != null)
            SetPause(paused, _session.NameOfPeer(1));
        else
            _session.Send(1, new PauseMessage(paused));
    }

    private void SetPause(bool paused, string by)
    {
        if (paused == IsPausedByPlayer)
            return;
        IsPausedByPlayer = paused;
        PausedBy = by;
        if (_host != null)
            _session.Broadcast(new PauseMessage(paused, by));
        PauseChanged?.Invoke();
    }
}
