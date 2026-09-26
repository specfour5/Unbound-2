using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Owns the game flow: cycles tanks through turns, rolls wind each turn,
/// enforces the turn timer, waits for shots to resolve, and calls the win/lose.
/// </summary>
public class TurnManager : MonoBehaviour
{
    [Header("Wiring (set by the setup script)")]
    public List<Tank> tanks = new List<Tank>();
    public Terrain terrain;
    public GameUI ui;

    [Header("Rules")]
    public float turnTime = 30f;
    public float maxWind = 4f;
    [Tooltip("Scales the wind roll each turn; 0 = no wind. Set by the setup menu.")]
    public float windMultiplier = 1f;
    [Tooltip("If false, the setup menu starts the game via BeginGame().")]
    public bool startOnAwake = true;
    [Tooltip("Extra settle time after the shell explodes before the next turn.")]
    public float resolveDelay = 2.2f;

    public Tank CurrentTank { get; private set; }
    public float Wind { get; private set; }
    public float TimeLeft { get; private set; }
    public bool IsResolving { get; private set; }

    bool gameOver;
    bool gameStarted;
    bool projectileResolved;
    int turnIndex = -1;

    void Start()
    {
        foreach (var t in tanks)
            t.Setup(this, terrain);
        if (startOnAwake) BeginGame();
    }

    /// <summary>Starts the turn loop (called by the setup menu when the player is ready).</summary>
    public void BeginGame()
    {
        if (gameStarted || gameOver) return;
        gameStarted = true;
        StartCoroutine(GameLoop());
    }

    void Update()
    {
        if (gameOver && Input.GetKeyDown(KeyCode.R))
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    IEnumerator GameLoop()
    {
        yield return new WaitForSeconds(0.5f);
        while (!gameOver)
        {
            turnIndex = (turnIndex + 1) % tanks.Count;
            Tank tank = tanks[turnIndex];
            if (!tank.IsAlive) continue;
            yield return StartCoroutine(RunTurn(tank));
            if (!gameOver) CheckGameOver();
        }
    }

    IEnumerator RunTurn(Tank tank)
    {
        CurrentTank = tank;
        Wind = Random.Range(-maxWind, maxWind) * windMultiplier;
        TimeLeft = turnTime;
        IsResolving = false;
        projectileResolved = false;

        tank.BeginTurn();
        if (CameraFollow.Instance != null)
            CameraFollow.Instance.Follow(tank.transform);
        ui.ShowTurnBanner(tank.isPlayer ? "Your Turn" : "Enemy Turn");

        Coroutine ai = null;
        if (tank is EnemyTank enemy)
            ai = StartCoroutine(enemy.RunTurn());

        while (!tank.HasFired && TimeLeft > 0f && tank.IsAlive && !gameOver)
        {
            TimeLeft -= Time.deltaTime;
            yield return null;
        }

        if (ai != null) StopCoroutine(ai);
        tank.EndTurnCleanup();

        if (tank.HasFired && !gameOver)
        {
            IsResolving = true;
            float wait = 0f;
            while (!projectileResolved && wait < resolveDelay + 4f && !gameOver)
            {
                wait += Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(0.6f);
            IsResolving = false;
        }

        CurrentTank = null;
    }

    public void OnTankFired(Tank tank) { /* hook for future sound/FX */ }
    public void OnProjectileResolved() => projectileResolved = true;

    public void OnTankKilled(Tank tank)
    {
        if (gameOver) return;
        CheckGameOver();
    }

    void CheckGameOver()
    {
        Tank player = tanks.Find(t => t.isPlayer);
        bool playerDead = player == null || !player.IsAlive;
        bool enemiesDead = true;
        foreach (var t in tanks)
            if (!t.isPlayer && t.IsAlive) enemiesDead = false;

        if (playerDead || enemiesDead)
        {
            gameOver = true;
            ui.ShowGameOver(!playerDead && enemiesDead);
        }
    }

    public Tank GetFirstAliveOpponent(Tank me)
    {
        foreach (var t in tanks)
            if (t != me && t.isPlayer != me.isPlayer && t.IsAlive)
                return t;
        return null;
    }
}
