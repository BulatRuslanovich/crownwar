using System.Collections.Generic;
using System.Linq;
using Warcrown.Sim.Core;

namespace Warcrown.Core;

/// <summary>
/// Statistics of every side of the match for the end screen: counters gathered from simulation events (troops trained,
/// killed and lost, buildings put up, lost and razed, gold looted), gold earned (income added up second by second), and
/// a sample every <see cref="SampleSeconds"/> of each side's army value, income and buildings, for the graph.
/// Purely the view's bookkeeping: it reads the simulation and never changes it.
/// </summary>
public sealed class MatchStats
{
    /// <summary>Seconds of match time between two points of the graph.</summary>
    public const int SampleSeconds = 10;

    public sealed class Side
    {
        public int Trained { get; internal set; }
        public int Lost { get; internal set; }
        public int Killed { get; internal set; }
        public int Built { get; internal set; }
        public int BuildingsLost { get; internal set; }
        public int Razed { get; internal set; }
        public int GoldEarned { get; internal set; }
        public int GoldLooted { get; internal set; }
    }

    /// <summary>One point of the graph: match time and, per faction id, what the side had.</summary>
    public sealed record Sample(int Seconds, IReadOnlyDictionary<int, (int ArmyValue, int Income, int Buildings)> Sides);

    private readonly Dictionary<int, Side> _sides = new();
    private readonly List<Sample> _samples = new();
    private int _countedSecond;

    public IReadOnlyList<Sample> Samples => _samples;

    public Side Of(int factionId)
    {
        if (!_sides.TryGetValue(factionId, out var side))
            _sides[factionId] = side = new Side();
        return side;
    }

    public void OnSimEvent(SimEvent e)
    {
        switch (e)
        {
            case UnitRecruited r:
                Of(r.FactionId).Trained++;
                break;
            case UnitDied d:
                Of(d.FactionId).Lost++;
                if (d.KillerFactionId >= 0 && d.KillerFactionId != d.FactionId)
                    Of(d.KillerFactionId).Killed++;
                break;
            case BuildingCompleted c:
                Of(c.FactionId).Built++;
                break;
            case BuildingDestroyed b:
                Of(b.FactionId).BuildingsLost++;
                if (b.KillerFactionId >= 0 && b.KillerFactionId != b.FactionId)
                    Of(b.KillerFactionId).Razed++;
                break;
            case GoldLooted loot:
                Of(loot.FactionId).GoldLooted += loot.Amount;
                break;
        }
    }

    /// <summary>
    /// Called every frame: adds up each second's income and takes a sample every <see cref="SampleSeconds"/> (and once at
    /// the start, so the graph begins at zero time).
    /// </summary>
    public void Update(Simulation sim)
    {
        int second = sim.Tick / Simulation.TicksPerSecond;
        if (_samples.Count == 0)
            Take(sim, 0);
        while (_countedSecond < second)
        {
            _countedSecond++;
            foreach (var faction in sim.Factions.Where(f => !f.IsEliminated))
                Of(faction.Id).GoldEarned += faction.IncomePerSecond;
            if (_countedSecond % SampleSeconds == 0)
                Take(sim, _countedSecond);
        }
    }

    /// <summary>A last point at the very end, so the graph reaches the final moment.</summary>
    public void Finish(Simulation sim)
    {
        int second = sim.Tick / Simulation.TicksPerSecond;
        if (_samples.Count == 0 || _samples[^1].Seconds < second)
            Take(sim, second);
    }

    private void Take(Simulation sim, int seconds)
    {
        var sides = new Dictionary<int, (int, int, int)>();
        foreach (var faction in sim.Factions.Where(f => f.CapitalId != 0))
        {
            int army = 0;
            foreach (var unit in sim.Units)
                if (unit.FactionId == faction.Id && unit.IsAlive)
                    army += unit.Type.Cost;
            int buildings = sim.Buildings.Count(b => b.FactionId == faction.Id && b.IsAlive);
            sides[faction.Id] = (army, faction.IsEliminated ? 0 : faction.IncomePerSecond, buildings);
        }
        _samples.Add(new Sample(seconds, sides));
    }
}
