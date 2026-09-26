using UnityEngine;

/// <summary>
/// Base tank: health, per-turn fuel movement, turret aiming, firing, slope tilting.
/// PlayerTank adds keyboard input, EnemyTank adds AI. Neither should fight this class.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public class Tank : MonoBehaviour
{
    public const float HealthBarW = 2.5f;
    public const float HealthBarH = 0.22f;

    [Header("Identity")]
    public bool isPlayer;
    [Tooltip("+1 faces right, -1 faces left.")]
    public int facing = 1;

    [Header("Stats")]
    public float maxHealth = 100f;
    public float moveSpeed = 5f;
    [Tooltip("How many world units the tank may drive per turn.")]
    public float fuelPerTurn = 8f;

    [Header("Weapon")]
    public float minAngle = 5f;
    public float maxAngle = 175f;
    public float minPower = 8f;
    public float maxPower = 32f;
    public float angle = 45f;
    public float power = 18f;
    public float angleAdjustSpeed = 45f;
    public GameObject projectileTemplate;

    [Header("Scene refs (wired by the setup script)")]
    public Transform visual;
    public Transform turretPivot;
    public Transform muzzle;
    public SpriteRenderer healthFill;
    public Transform healthBarRoot;

    protected Rigidbody2D rb;
    protected Collider2D col;
    protected TurnManager turnManager;
    protected Terrain terrain;
    protected float moveInput;

    Transform[] previewDots;
    const int previewCount = 18;

    public float Health { get; protected set; }
    public bool IsAlive => Health > 0f;
    public bool IsMyTurn { get; protected set; }
    public bool HasFired { get; protected set; }
    public float FuelLeft { get; protected set; }

    /// <summary>World-space direction the barrel is pointing.</summary>
    public Vector2 AimDir
    {
        get
        {
            float r = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r) * facing, Mathf.Sin(r));
        }
    }

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        Health = maxHealth;
    }

    public virtual void Setup(TurnManager tm, Terrain tr)
    {
        turnManager = tm;
        terrain = tr;
        UpdateBarrel();
        UpdateHealthBar();
        if (isPlayer) BuildPreviewDots();
    }

    public virtual void BeginTurn()
    {
        IsMyTurn = true;
        HasFired = false;
        FuelLeft = fuelPerTurn;
    }

    public virtual void EndTurnCleanup()
    {
        IsMyTurn = false;
        moveInput = 0f;
        HidePreview();
    }

    void FixedUpdate()
    {
        if (IsMyTurn && !HasFired && IsAlive && FuelLeft > 0f && Mathf.Abs(moveInput) > 0.01f)
        {
            float step = Mathf.Clamp(moveInput, -1f, 1f) * moveSpeed * Time.fixedDeltaTime;
            float nx = Mathf.Clamp(rb.position.x + step, terrain.LeftX + 2f, terrain.RightX - 2f);
            float actual = nx - rb.position.x;
            rb.linearVelocity = new Vector2(actual / Time.fixedDeltaTime, rb.linearVelocity.y);
            FuelLeft = Mathf.Max(0f, FuelLeft - Mathf.Abs(actual));
        }
        else if (IsMyTurn && Mathf.Abs(moveInput) <= 0.01f)
        {
            // Stop promptly when the driver lets go (does not fight knockback:
            // knockback only happens while HasFired or on someone else's turn).
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
    }

    protected virtual void Update()
    {
        AlignToSlope();
        if (transform.position.y < -30f && IsAlive)
            Die(silent: true); // fell through the world somehow
    }

    /// <summary>Tilts the visual body to match the ground slope.</summary>
    void AlignToSlope()
    {
        if (visual == null) return;
        var hits = Physics2D.RaycastAll(transform.position + Vector3.up * 0.5f, Vector2.down, 5f);
        foreach (var h in hits)
        {
            if (h.collider == null || h.collider == col) continue;
            float z = Vector2.SignedAngle(Vector2.up, h.normal);
            z = Mathf.Clamp(z, -30f, 30f);
            Quaternion want = Quaternion.Euler(0f, 0f, z);
            visual.rotation = Quaternion.Lerp(visual.rotation, want, 1f - Mathf.Exp(-8f * Time.deltaTime));
            return;
        }
    }

    public void AdjustAngle(float delta)
    {
        if (!CanAim()) return;
        angle = Mathf.Clamp(angle + delta, minAngle, maxAngle);
        UpdateBarrel();
    }

    bool CanAim() => IsMyTurn && !HasFired && IsAlive;

    protected void UpdateBarrel()
    {
        if (turretPivot == null) return;
        float z = Mathf.Atan2(AimDir.y, AimDir.x) * Mathf.Rad2Deg;
        turretPivot.rotation = Quaternion.Euler(0f, 0f, z);
    }

    public virtual void Fire()
    {
        if (!IsMyTurn || HasFired || !IsAlive) return;
        HasFired = true;
        HidePreview();

        GameObject go = Instantiate(projectileTemplate);
        go.transform.position = muzzle.position;
        go.SetActive(true);

        var proj = go.GetComponent<Projectile>();
        var pcol = go.GetComponent<Collider2D>();
        if (pcol != null && col != null)
            Physics2D.IgnoreCollision(pcol, col);

        proj.Launch(AimDir * power, turnManager.Wind, OnProjectileExploded);
        ExplosionFX.Spawn(muzzle.position, 0.9f);

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.Follow(go.transform);
        turnManager.OnTankFired(this);
    }

    void OnProjectileExploded() => turnManager.OnProjectileResolved();

    public void TakeDamage(float dmg)
    {
        if (!IsAlive) return;
        Health = Mathf.Max(0f, Health - dmg);
        UpdateHealthBar();
        if (Health <= 0f) Die(silent: false);
    }

    public void Knockback(Vector2 force)
    {
        if (IsAlive) rb.AddForce(force, ForceMode2D.Impulse);
    }

    protected void Die(bool silent)
    {
        Health = 0f;
        if (!silent) ExplosionFX.Spawn(transform.position, 3.5f);
        if (visual != null) visual.gameObject.SetActive(false);
        if (healthBarRoot != null) healthBarRoot.gameObject.SetActive(false);
        HidePreview();
        col.enabled = false;
        rb.simulated = false;
        turnManager.OnTankKilled(this);
    }

    void UpdateHealthBar()
    {
        if (healthFill == null) return;
        float f = Mathf.Clamp01(Health / maxHealth);
        healthFill.transform.localScale = new Vector3(HealthBarW * f, HealthBarH, 1f);
        healthFill.color = Color.Lerp(Color.red, Color.green, f);
    }

    #region Aim preview (player only)

    void BuildPreviewDots()
    {
        var root = new GameObject(name + "_AimPreview");
        previewDots = new Transform[previewCount];
        for (int i = 0; i < previewCount; i++)
        {
            var d = new GameObject("dot");
            d.transform.SetParent(root.transform, false);
            d.transform.localScale = Vector3.one * 0.28f;
            var sr = d.AddComponent<SpriteRenderer>();
            sr.sprite = Art.CenteredCircle;
            sr.color = new Color(1f, 1f, 1f, 0.45f);
            sr.sortingOrder = 4;
            d.SetActive(false);
            previewDots[i] = d.transform;
        }
    }

    protected void UpdatePreview(float wind)
    {
        if (previewDots == null) return;
        bool show = isPlayer && IsMyTurn && !HasFired && IsAlive;
        Vector2 p = muzzle.position;
        Vector2 v = AimDir * power;
        Vector2 accel = (Vector2)Physics2D.gravity * Projectile.GravityScale
                      + Vector2.right * wind * Projectile.WindEffect;
        const float dt = 0.12f;
        for (int i = 0; i < previewDots.Length; i++)
        {
            v += accel * dt;
            p += v * dt;
            previewDots[i].position = p;
            previewDots[i].gameObject.SetActive(show);
        }
    }

    protected void HidePreview()
    {
        if (previewDots == null) return;
        foreach (var d in previewDots) d.gameObject.SetActive(false);
    }

    #endregion
}
