/// <summary>
/// What a Tank needs from whoever runs the game. TurnManager (duel) and
/// SideScrollerManager (real-time campaign) both implement this, so Tank,
/// PlayerTank and EnemyTank stay mode-agnostic.
/// </summary>
public interface IGameMode
{
    /// <summary>True for turn-based play (firing consumes the turn).</summary>
    bool IsTurnBased { get; }

    /// <summary>Current wind (positive blows +x). Used for ballistics and the aim preview.</summary>
    float Wind { get; }

    /// <summary>May this tank drive and aim right now?</summary>
    bool ControlsActive(Tank t);

    /// <summary>May this tank fire right now? (turn gate vs cooldown).</summary>
    bool CanFire(Tank t);

    /// <summary>Best target for this tank's weapons (AI aiming).</summary>
    Tank GetTargetFor(Tank t);

    /// <summary>Called right after a tank fires.</summary>
    void OnFired(Tank t);

    /// <summary>Called after a shell's explosion has resolved.</summary>
    void OnProjectileResolved();

    /// <summary>Called when a tank dies (scoring, game over).</summary>
    void OnTankKilled(Tank t);
}
