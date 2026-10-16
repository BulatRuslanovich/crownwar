using System;
using Godot;
using Lordsio.Sim.Core;

namespace Lordsio.Core;

/// <summary>What the simulation reports each frame, and who gets to know: the fog decides what is seen and heard.</summary>
public partial class MatchController
{
    /// <summary>
    /// Hands this frame's simulation events to whoever shows them: statistics, the HUD, alerts and the end screen
    /// always; effects, sounds and screen shake only for what the player can see.
    /// </summary>
    private void DispatchEvents(double delta)
    {
        if (_replayedThisFrame)
        {
            // Replaying the match after a rejoin: count it, but no effects, sounds or messages for the past.
            foreach (var e in Sim.TakeEvents())
                Stats.OnSimEvent(e);
            return;
        }

        if (IsShowcase)
        {
            // Only the world reacts; no interface, statistics or sounds behind the menu.
            foreach (var e in Sim.TakeEvents())
            {
                if (e is AttackLanded hit)
                    _lastFight = hit.Position;
                _unitsView.OnSimEvent(e);
                _buildingsView.OnSimEvent(e);
                _groundEffects.OnSimEvent(e);
                _airEffects.OnSimEvent(e);
            }
            UpdateShowcase(delta);
            return;
        }

        foreach (var e in Sim.TakeEvents())
        {
            if (e is AttackLanded hit && (IsFriendly(hit.FactionId) || IsFriendly(hit.TargetFactionId)))
                _battleHeat = MathF.Min(BattleHeatMax, _battleHeat + 1f);
            Stats.OnSimEvent(e);
            if (Net is { LogChecksums: true } && e is PeaceOffered or PeaceRefused or PeaceMade or WarDeclared or WarBegan or DiplomacyRejected)
                GD.Print($"[net] tick {Sim.Tick}: {e}"); // both copies' logs must agree on these
            _hud.OnSimEvent(e);
            _alerts.OnSimEvent(e, Sim.Tick);
            _endScreen.OnSimEvent(e);
            if (e is FactionEliminated { FactionId: var fallen } && fallen == LocalFactionId || e is MatchEnded)
                Fog.RevealAll(); // spectating: the rest of the match in full view
            if (e is MatchEnded)
                Net?.DiscardSave(); // a finished match is not one to bring back
            if (!CanSee(e))
                continue; // a fight in the fog makes no sparks or sounds
            ShakeFor(e);
            _unitsView.OnSimEvent(e);
            _buildingsView.OnSimEvent(e);
            _groundEffects.OnSimEvent(e);
            _airEffects.OnSimEvent(e);
            _sounds.OnSimEvent(e);
        }
        UpdateBattleMood();
    }

    /// <summary>Whether the player may see (and hear) where an event happened.</summary>
    private bool CanSee(SimEvent e) => e switch
    {
        AttackLanded hit => Fog.IsVisible(hit.Position),
        SplashLanded splash => Fog.IsVisible(splash.Position),
        ProjectileFired shot => Fog.IsVisible(shot.Position),
        UnitDied died => IsFriendly(died.FactionId) || Fog.IsVisible(died.Position),
        UnitPromoted promoted => IsFriendly(promoted.FactionId) || Fog.IsVisible(promoted.Position),
        BuildingDestroyed destroyed => IsFriendly(destroyed.FactionId) || Fog.IsVisible(destroyed.Position),
        BuildingDemolished demolished => IsFriendly(demolished.FactionId) || Fog.IsVisible(demolished.Position),
        BuildingCompleted completed => IsFriendly(completed.FactionId),
        _ => true,
    };

    /// <summary>Heavy blows on screen shake it: a stone landing a little, a building collapsing more, a castle most.</summary>
    private void ShakeFor(SimEvent e)
    {
        var (at, amount) = e switch
        {
            SplashLanded s => (s.Position, 0.18f),
            BuildingDestroyed d => (d.Position, Sim.Rules.GetBuildingType(d.BuildingTypeId).IsCapital ? 0.9f : 0.4f),
            _ => (default(System.Numerics.Vector2), 0f),
        };
        if (amount <= 0f)
            return;
        // Fades with the distance from the middle of the screen: a far-off collapse is only felt a little.
        var screen = Camera.VisibleRect;
        float distance = SimConvert.ToWorld(at).DistanceTo(screen.GetCenter());
        float nearness = Mathf.Clamp(1f - distance / screen.Size.Length(), 0f, 1f);
        Camera.Shake(amount * nearness);
        // A castle falling in view freezes the moment: the world slows to a crawl for a breath (single player only:
        // in a network game every machine must keep stepping in time).
        if (e is BuildingDestroyed { Size: >= 3 } fallen && Sim.Rules.GetBuildingType(fallen.BuildingTypeId).IsCapital
            && Net == null && !IsShowcase && nearness > 0f)
            HitStop(CastleHitStopSeconds, CastleHitStopScale);
    }

    private const double CastleHitStopSeconds = 0.35;
    private const double CastleHitStopScale = 0.15;
    private double? _timeScaleBeforeHitStop;

    /// <summary>Slows time to <paramref name="scale"/> for <paramref name="seconds"/> of real time, then restores it.</summary>
    private async void HitStop(double seconds, double scale)
    {
        if (_timeScaleBeforeHitStop != null)
            return; // already frozen
        _timeScaleBeforeHitStop = Engine.TimeScale;
        Engine.TimeScale *= scale;
        await ToSignal(GetTree().CreateTimer(seconds, processAlways: true, processInPhysics: false, ignoreTimeScale: true),
            SceneTreeTimer.SignalName.Timeout);
        EndHitStop();
    }

    private void EndHitStop()
    {
        if (_timeScaleBeforeHitStop is not { } before)
            return;
        Engine.TimeScale = before;
        _timeScaleBeforeHitStop = null;
    }
}
