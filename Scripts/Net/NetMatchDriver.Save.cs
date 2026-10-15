using System;
using System.Collections.Generic;
using System.Linq;
using Lordsio.Sim.Core;
using Lordsio.Sim.Net;

namespace Lordsio.Net;

/// <summary>The host's save of the match, frame by frame, so a crashed game can be brought back.</summary>
public sealed partial class NetMatchDriver
{
    private System.IO.FileStream? _save;
    private readonly List<FrameMessage> _unsaved = new();
    private double _sinceSave;
    private const double SaveIntervalSeconds = 1.0;

    /// <summary>Writes the frames made since the last save (about once a second).</summary>
    private void SaveNow()
    {
        if (_save == null)
        {
            _unsaved.Clear(); // nothing to save to (finished, or the disk refused)
            return;
        }
        if (_unsaved.Count == 0)
            return;
        try
        {
            MatchSave.AppendFrames(_save, _unsaved);
            _save.Flush(flushToDisk: true);
        }
        catch (System.IO.IOException e)
        {
            Godot.GD.PushWarning($"Saving the network match failed: {e.Message}");
        }
        _unsaved.Clear();
    }

    /// <summary>The match is over: its save is of no use any more.</summary>
    public void DiscardSave()
    {
        _save?.Dispose();
        _save = null;
        _unsaved.Clear();
        if (_host != null)
            MatchSaveStore.Delete();
    }

    /// <summary>Writes what is left and closes the save file (see <see cref="Dispose"/>).</summary>
    private void CloseSave()
    {
        SaveNow();
        _save?.Dispose();
        _save = null;
    }
}
