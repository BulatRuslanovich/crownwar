using System;
using Lordsio.Sim.Core;
using SimVector2 = System.Numerics.Vector2;

namespace Lordsio.Core;

/// <summary>
/// Decides when to warn the player that they are under attack: an enemy hit on one of the player's
/// units or buildings, at most once per <see cref="CooldownTicks"/>, plus whenever a fight breaks out
/// somewhere new (far from the previous warning). In Defense a blow on the shared citadel warns too.
/// </summary>
public sealed class AttackAlerts
{
    private const int CooldownTicks = 10 * Simulation.TicksPerSecond;
    /// <summary>A hit this far (tiles) from the last warning counts as a new front and warns again sooner.</summary>
    private const float NewFrontDistance = 15f;
    private const int NewFrontCooldownTicks = 3 * Simulation.TicksPerSecond;

    private readonly int _player;
    /// <summary>An ally whose hits count as the player's own (the citadel's garrison), or -1.</summary>
    private readonly int _guarded;
    private int _lastTick = int.MinValue / 2;
    private SimVector2 _lastPosition;

    public AttackAlerts(int playerFactionId, int? guardedFactionId = null)
    {
        _player = playerFactionId;
        _guarded = guardedFactionId ?? -1;
    }

    /// <summary>Where the most recent warning came from, for "jump to alert".</summary>
    public SimVector2? LastPosition { get; private set; }

    /// <summary>Raised with the position of the attack when the player should be warned.</summary>
    public event Action<SimVector2>? Alerted;

    public void OnSimEvent(SimEvent e, int tick)
    {
        if (e is not AttackLanded hit || hit.TargetFactionId != _player && hit.TargetFactionId != _guarded || hit.FactionId == _player)
            return;
        int since = tick - _lastTick;
        bool newFront = SimVector2.Distance(hit.Position, _lastPosition) > NewFrontDistance;
        if (since < CooldownTicks && !(newFront && since >= NewFrontCooldownTicks))
            return;
        _lastTick = tick;
        _lastPosition = hit.Position;
        LastPosition = hit.Position;
        Alerted?.Invoke(hit.Position);
    }
}
