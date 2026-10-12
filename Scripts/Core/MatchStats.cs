using Lordsio.Sim.Core;

namespace Lordsio.Core;

/// <summary>The local player's match statistics, gathered from simulation events for the end screen.</summary>
public sealed class MatchStats
{
    private readonly int _player;

    public MatchStats(int playerFactionId)
    {
        _player = playerFactionId;
    }

    public int UnitsTrained { get; private set; }
    public int UnitsLost { get; private set; }
    public int EnemiesKilled { get; private set; }
    public int BuildingsBuilt { get; private set; }
    public int BuildingsLost { get; private set; }
    public int BuildingsDestroyed { get; private set; }

    public void OnSimEvent(SimEvent e)
    {
        switch (e)
        {
            case UnitRecruited r when r.FactionId == _player:
                UnitsTrained++;
                break;
            case UnitDied d when d.FactionId == _player:
                UnitsLost++;
                break;
            case UnitDied d when d.KillerFactionId == _player:
                EnemiesKilled++;
                break;
            case BuildingCompleted c when c.FactionId == _player:
                BuildingsBuilt++;
                break;
            case BuildingDestroyed b when b.FactionId == _player:
                BuildingsLost++;
                break;
            case BuildingDestroyed b when b.KillerFactionId == _player:
                BuildingsDestroyed++;
                break;
        }
    }
}
