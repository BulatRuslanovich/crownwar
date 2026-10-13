namespace Lordsio.Sim.Core;

/// <summary>State fingerprint for lockstep play.</summary>
public sealed partial class Simulation
{
    /// <summary>
    /// 64-bit FNV-1a hash of everything that matters to the rules: tick, units (positions by their exact
    /// float bits, health, orders, targets), buildings, projectiles and treasuries. Peers running the same
    /// commands on the same build must get the same value every tick; a mismatch means a desync.
    /// </summary>
    public ulong ComputeChecksum()
    {
        var hash = new Fnv();
        hash.Add(Tick);
        foreach (var unit in _units)
        {
            hash.Add(unit.Id);
            hash.Add(unit.FactionId);
            hash.Add(BitConverter.SingleToInt32Bits(unit.Position.X));
            hash.Add(BitConverter.SingleToInt32Bits(unit.Position.Y));
            hash.Add(unit.Health);
            hash.Add((int)unit.Order);
            hash.Add(unit.Target?.Id ?? 0);
            hash.Add(unit.PathIndex);
            hash.Add(unit.Path.Count);
        }
        foreach (var building in _buildings)
        {
            hash.Add(building.Id);
            hash.Add(building.Health);
            hash.Add(building.ConstructionTicksLeft);
            hash.Add(building.Queue.Count);
            hash.Add(building.Target?.Id ?? 0);
        }
        foreach (var projectile in _projectiles)
        {
            hash.Add(BitConverter.SingleToInt32Bits(projectile.Position.X));
            hash.Add(BitConverter.SingleToInt32Bits(projectile.Position.Y));
            hash.Add(projectile.Target.Id);
        }
        foreach (var faction in _factions)
        {
            hash.Add(faction.Treasury.Gold);
            hash.Add(faction.IsEliminated ? 1 : 0);
        }
        return hash.Value;
    }

    private struct Fnv
    {
        private const ulong Prime = 1099511628211UL;
        private ulong _value;
        private bool _started;

        public ulong Value => _started ? _value : 14695981039346656037UL;

        public void Add(int data)
        {
            if (!_started)
            {
                _value = 14695981039346656037UL;
                _started = true;
            }
            for (int i = 0; i < 4; i++)
            {
                _value ^= (byte)(data >> (8 * i));
                _value *= Prime;
            }
        }
    }
}
