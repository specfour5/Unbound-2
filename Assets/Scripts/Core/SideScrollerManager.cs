using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Real-time campaign mode: the player drives a long procedurally generated
/// map left to right, fighting enemy tanks spawned along the way. There are
/// no turns — weapons fire on cooldown, and reaching the far end wins.
/// </summary>
public class SideScrollerManager : MonoBehaviour, IGameMode
{
    [Header("Wiring (set by the setup script)")]
    public TerrainGrid terrain;
    public Tank player;
    public List<Tank> enemies = new List<Tank>();
    public SideScrollerUI ui;

    [Header("Rules")]
    [Tooltip("Player x at the start (for the progress bar).")]
    public float startX = -220f;
    [Tooltip("Reaching this x wins the run.")]
    public float endX = 225f;
    public float windMax = 4f;
    [Tooltip("Scales the wind roll; set from the setup menu.")]
    public float windMultiplier = 1f;
    [Tooltip("Seconds between wind changes.")]
    public float windChangeTime = 18f;

    public bool IsTurnBased => false;
    public float Wind { get; private set; }

    public int Kills { get; private set; }
    public int TotalEnemies => enemies != null ? enemies.Count : 0;
    public bool GameOver { get; private set; }
    public float Progress01
    {
        get
        {
            if (player == null) return 0f;
            return Mathf.Clamp01((player.transform.position.x - startX) / (endX - startX));
        }
    }

    float windTarget;
    float windTimer;

    void Start()
    {
        // Carry the duel setup menu's choices across the scene load.
        if (GameConfig.weapon != null && player != null)
            player.weapon = GameConfig.weapon;
        windMultiplier = GameConfig.windMultiplier;

        player.Setup(this, terrain);
        player.unlimitedFuel = true; // driving IS the game; no stranding
        foreach (var e in enemies)
            e.Setup(this, terrain);

        windTarget = Random.Range(-windMax, windMax) * windMultiplier;
        if (CameraFollow.Instance != null)
            CameraFollow.Instance.Follow(player.transform);
        if (ui != null) ui.Init(this);
    }

    void Update()
    {
        if (GameOver)
        {
            if (Input.GetKeyDown(KeyCode.R))
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            return;
        }

        // Wind drifts: re-roll a target every so often, ease toward it.
        windTimer -= Time.deltaTime;
        if (windTimer <= 0f)
        {
            windTimer = windChangeTime;
            windTarget = Random.Range(-windMax, windMax) * windMultiplier;
        }
        Wind = Mathf.MoveTowards(Wind, windTarget, Time.deltaTime * 1.5f);

        if (player != null && player.IsAlive && player.transform.position.x >= endX)
        {
            GameOver = true;
            if (ui != null) ui.ShowEnd(true, Kills, TotalEnemies);
        }
    }

    // --- IGameMode ---

    public bool ControlsActive(Tank t) => t != null && t.IsAlive && !GameOver;

    public bool CanFire(Tank t) => t != null && t.IsAlive && !GameOver && t.cooldownLeft <= 0f;

    public Tank GetTargetFor(Tank me)
    {
        if (me == null || !me.IsAlive) return null;
        if (me.isPlayer)
        {
            // Nearest living enemy.
            Tank best = null;
            float bd = float.MaxValue;
            foreach (var e in enemies)
            {
                if (e == null || !e.IsAlive) continue;
                float d = Vector2.Distance(me.transform.position, e.transform.position);
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }
        return player != null && player.IsAlive ? player : null;
    }

    public void OnFired(Tank t) { /* camera already follows the shell */ }

    public void OnProjectileResolved()
    {
        // Shell's done: point the camera back at the player.
        if (!GameOver && player != null && player.IsAlive && CameraFollow.Instance != null)
            CameraFollow.Instance.Follow(player.transform);
    }

    public void OnTankKilled(Tank t)
    {
        if (GameOver || t == null) return;
        if (t.isPlayer)
        {
            GameOver = true;
            if (ui != null) ui.ShowEnd(false, Kills, TotalEnemies);
        }
        else
        {
            Kills++;
        }
    }
}
