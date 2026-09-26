# Unbound 2 — Turn-Based Artillery Tanks (Unity 2D)

A Worms / Scorched-Earth style game: side-view 2D, destructible terrain, and
turn-based combat. Each turn you drive your tank (limited fuel), adjust turret
angle and shot power, then fire. Then the AI does the same to you.

## Install

1. Copy the `Assets/Scripts` and `Assets/Sprites` folders from this pack into
   your Unity project's `Assets` folder (so you have `Assets/Scripts/Core/...`,
   `Assets/Scripts/Editor/...`, and `Assets/Sprites/...`).
2. Back in Unity, wait for it to compile.
3. Menu bar → **Tanks → Build Game Scene**. This generates the whole scene:
   terrain, player tank, enemy tank, camera, HUD, and turn manager, then saves
   it as `Assets/Scenes/TankGame.unity`.
4. Press **Play**.

The setup script also sets **Active Input Handling to "Both"** automatically
(Edit → Project Settings → Player), since the tank controls use classic
keyboard input.

## Battle setup
Before the first turn, a setup box appears:
- **Wind**: None / Light / **Default** / Strong / Extreme — scales how hard the wind
  blows each turn (Default is the original behavior).
- **Spawn distance**: Close / **Default** / Far — how far apart the two tanks spawn.
- **Start Battle** applies the picks, drops the tanks onto flattened ground, and
  begins the turn loop.

## Controls (your turn)

| Keys | Action |
|---|---|
| A/D or ←/→ | Drive (uses fuel) |
| W/S or ↑/↓ | Turret angle |
| Space (hold) | Charge shot: 1.5 s to full power |
| Space (release) | Fire (ends your turn) |
| R | Restart after game over |

Turns are 30 seconds. The dotted arc shows where your shell will go,
including wind.

## How it's built

```
Assets/Scripts/
  Core/
    Art.cs            Procedural sprites (HUD, dots, effects)
    Terrain.cs        Heightmap terrain: mesh + EdgeCollider2D, crater carving
    Tank.cs           Base tank: health, fuel movement, aiming, firing, slope tilt,
                      sprite visuals; the hull's front flips toward the drive direction
    PlayerTank.cs     Keyboard input + trajectory preview
    EnemyTank.cs      AI: repositions, solves ballistics, fires with skill-based error
                      (turns its front toward you before aiming)
    Projectile.cs     Shell physics, wind, explosion (terrain + radial damage)
    ExplosionFX.cs    Procedural flash + sparks
    TurnManager.cs    Turn order, wind rolls, timer, win/lose
    CameraFollow.cs   Follows tank → shell → next tank, with screen shake
    GameUI.cs         HUD: power/angle/wind/timer, fuel bar, banners
  Editor/
    TankGameSetup.cs  Tanks → Build Game Scene (builds everything above)
Assets/Sprites/
  TankHull.png      Tank hull with treads and a visible front (nose + headlight)
  TurretDome.png    Turret dome (stays level while the barrel pitches)
  Barrel.png        Cannon barrel (pitches with the turret)
```

Tank sprites are painted near-white so the setup script can tint them team
colors (green player, red enemy) via `SpriteRenderer.color`.

Key design points:

- **Turn flow** lives in `TurnManager`: `BeginTurn → (player input or AI coroutine)
  → Fire → wait for `Projectile` to report its explosion → next tank`.
- **Wind** is re-rolled every turn and pushes shells via `AddForce`.
  The aim preview and the AI both account for it.
- **AI aiming** solves the projectile range equation for a fixed power and
  adds random error scaled by `skill` (1 = perfect).
- **Terrain** is deterministic by default (`seed = 42`) so the editor-built
  tank spawn positions always line up. Set `seed = 0` for random hills
  every run (tanks drop onto the terrain at spawn).

## Tuning knobs

- `TurnManager`: `turnTime`, `maxWind`
- `Tank`: `moveSpeed`, `fuelPerTurn`, `minPower`/`maxPower`, `maxHealth`
- `Projectile`: `blastRadius`, `maxDamage`
- `EnemyTank`: `skill` (0–1)
- `Terrain`: `width`, `amplitude`, `baseHeight`, `seed`

## Ideas for next steps

- More AI tanks (the TurnManager already supports N tanks — just add them to
  the list and re-run the builder, or duplicate in the scene)
- Weapon pickups / multiple shell types (cluster, napalm)
- Parabolic "drop" crates, shields, health packs
- Sound effects on fire/explosion/turn change
- Mobile touch controls
