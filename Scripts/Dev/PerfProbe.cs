using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace Warcrown.Dev;

/// <summary>
/// Dev-only (--perf-log=SECONDS): measures frames with vsync off, after a short warm-up, then prints the numbers and
/// quits — the frame time, memory allocated per frame on the main
/// thread, how many garbage collections ran, and the renderer's draw calls per frame. A repeatable way to compare before and after an optimisation.
/// </summary>
public partial class PerfProbe : Node
{
    private const double WarmUpSeconds = 3.0;

    private readonly List<double> _frames = new();
    private double _seconds;
    private double _drawCalls, _objects;
    private double _elapsed;
    private long _startBytes;
    private int _startCollections;
    private long _last;
    private bool _measuring;

    public static PerfProbe Start(Node parent, double seconds)
    {
        var probe = new PerfProbe { Name = "PerfProbe", _seconds = seconds, ProcessMode = ProcessModeEnum.Always };
        parent.AddChild(probe);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        return probe;
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        long now = Stopwatch.GetTimestamp();
        if (!_measuring)
        {
            if (_elapsed < WarmUpSeconds)
                return;
            _measuring = true;
            _elapsed = 0;
            _startBytes = GC.GetAllocatedBytesForCurrentThread();
            _startCollections = GC.CollectionCount(0);
            _last = now;
            return;
        }
        _frames.Add(Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds);
        _drawCalls += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        _objects += Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        _last = now;
        if (_elapsed < _seconds)
            return;

        long bytes = GC.GetAllocatedBytesForCurrentThread() - _startBytes;
        int collections = GC.CollectionCount(0) - _startCollections;
        var frames = new List<double>(_frames);
        frames.Sort();
        double avg = 0;
        foreach (double f in _frames) avg += f;
        avg /= _frames.Count;
        GD.Print($"PERF frames {_frames.Count}: frame avg {avg:0.00} ms ({1000 / avg:0} fps), p99 {frames[(int)(frames.Count * 0.99)]:0.00} ms, " +
                 $"max {frames[^1]:0.00} ms; alloc {bytes / 1024.0 / _frames.Count:0.0} KB/frame; gen0 GCs {collections}; draw calls {_drawCalls / _frames.Count:0}, objects {_objects / _frames.Count:0} per frame");
        SetProcess(false);
        GetTree().Quit();
    }
}
