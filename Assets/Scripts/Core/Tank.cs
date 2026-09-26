using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Base tank: health, per-turn fuel movement, turret aiming, firing, slope tilting.
/// PlayerTank adds keyboard input, EnemyTank adds AI. Neither should fight this class.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public class Tank : MonoBehaviour
{
    public const float HealthBarW = 3.0f;
    public const float HealthBarH = 0.5f;

    /// <summary>One damageable part of the tank (hull, turret, weapon...).</summary>
    [System.Serializable]
    public class ComponentSlot
    {
        public string name = "Hull";
        public float maxHP = 100f;
        [HideInInspector] public float hp;
    }

    [Header("Identity")]
    public bool isPlayer;
    [Tooltip("+1 faces right, -1 faces left.")]
    public int facing = 1;

    [Header("Components (hull, turret, weapons each have their own pool)")]
    public List<ComponentSlot> components = new List<ComponentSlot>();

    [Header("Stats")]
    public float moveSpeed = 5f;
    [Tooltip("How many world units the tank may drive per turn.")]
    public float fuelPerTurn = 8f;
    [HideInInspector] public float baseFuelPerTurn = 8f;
    [Tooltip("When true, driving never drains fuel.")]
    public bool unlimitedFuel = false;

    [Header("Ground handling (scale these with hull size)")]
    [Tooltip("Terrain sample spread, front to back. Match to the track contact patch.")]
    public float probeHalfWidth = 0.83f;
    [Tooltip("Rest height of the tank origin above the ground surface.")]
    public float rideHeight = 0.45f;
    [Tooltip("How much the leading track may climb over small pixels instead of digging in.")]
    public float climbForgiveness = 0.35f;
    [Tooltip("How stiffly the tank's height tracks the terrain.")]
    public float groundFollowSharpness = 12f;
    public float slopeAlignSpeed = 6f;

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
    [Tooltip("Hull sprite object; flipped on the X axis to face the drive direction.")]
    public Transform hull;
    [Tooltip("Uniform base scale of the hull sprite (set by the setup script).")]
    public float hullScale = 1f;
    public SpriteRenderer healthFill;
    public SpriteRenderer healthBG;
    public Transform healthBarRoot;
    public TextMesh healthText;
    [Tooltip("Renderer for the Hull component (flashes when hull is critical).")]
    public SpriteRenderer hullRenderer;
    [Tooltip("Renderer for the Turret component (flashes when turret is critical).")]
    public SpriteRenderer turretRenderer;
    [Tooltip("Renderer for the Cannon component (flashes when cannon is critical).")]
    public SpriteRenderer weaponRenderer;

    protected Rigidbody2D rb;
    protected Collider2D col;
    protected TurnManager turnManager;
    protected Terrain terrain;
    protected float moveInput;

    Color hullBaseColor = Color.white;
    Color turretBaseColor = Color.white;
    Color weaponBaseColor = Color.white;

    Transform[] previewDots;
    const int previewCount = 18;

    /// <summary>Average of all component health pools (what the health bar shows).</summary>
    public float AverageHP
    {
        get
        {
            if (components == null || components.Count == 0) return 0f;
            float sum = 0f;
            foreach (var c in components) sum += c.hp;
            return sum / components.Count;
        }
    }

    public float AverageMaxHP
    {
        get
        {
            if (components == null || components.Count == 0) return 1f;
            float sum = 0f;
            foreach (var c in components) sum += c.maxHP;
            return sum / components.Count;
        }
    }

    public bool IsAlive => AverageHP > 0f;
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
        baseFuelPerTurn = fuelPerTurn;
        if (components != null)
            foreach (var c in components) c.hp = c.maxHP;
    }

    public virtual void Setup(TurnManager tm, Terrain tr)
    {
        turnManager = tm;
        terrain = tr;
        SetFacing(facing);
        if (hullRenderer != null) hullBaseColor = hullRenderer.color;
        if (turretRenderer != null) turretBaseColor = turretRenderer.color;
        if (weaponRenderer != null) weaponBaseColor = weaponRenderer.color;
        // Reassign bar sprites at runtime so they always use fresh,
        // correctly-sized Art sprites even if the scene baked stale ones.
        if (healthBG != null) healthBG.sprite = Art.CenteredWhite;
        if (healthFill != null) healthFill.sprite = Art.LeftPivotWhite;
        // Enforce readable HP text size even if the scene baked the old one,
        // and (re)assign a font at runtime: builtin font references baked by
        // the editor script don't always survive serialization, which leaves
        // the TextMesh with degenerate single-pixel geometry.
        if (healthText != null)
        {
            if (healthText.font == null)
            {
                healthText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (healthText.font == null)
                {
                    // Last resort: grab any loaded font (builtin names vary by Unity version).
                    var allFonts = Resources.FindObjectsOfTypeAll<Font>();
                    if (allFonts.Length > 0) healthText.font = allFonts[0];
                }
            }
            healthText.characterSize = 0.05f;
            healthText.fontStyle = FontStyle.Bold;
            Debug.Log($"[Tank] Setup HP text: font={(healthText.font != null ? healthText.font.name : "NULL")} " +
                      $"charSize={healthText.characterSize} text='{healthText.text}'");
        }
        else
        {
            Debug.LogWarning("[Tank] Setup: healthText is NULL (scene predates the HP number).");
        }
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
        if (IsMyTurn && !HasFired && IsAlive && (unlimitedFuel || FuelLeft > 0f) && Mathf.Abs(moveInput) > 0.01f)
        {
            int wantFace = moveInput > 0f ? 1 : -1;
            if (wantFace != facing) SetFacing(wantFace);

            float step = Mathf.Clamp(moveInput, -1f, 1f) * moveSpeed * Time.fixedDeltaTime;
            float nx = Mathf.Clamp(rb.position.x + step, terrain.LeftX + 2f, terrain.RightX - 2f);
            float actual = nx - rb.position.x;
            rb.linearVelocity = new Vector2(actual / Time.fixedDeltaTime, rb.linearVelocity.y);
            if (!unlimitedFuel)
                FuelLeft = Mathf.Max(0f, FuelLeft - Mathf.Abs(actual));
        }
        else if (IsMyTurn && Mathf.Abs(moveInput) <= 0.01f)
        {
            // Stop promptly when the driver lets go (does not fight knockback:
            // knockback only happens while HasFired or on someone else's turn).
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }

        // Hug the terrain on our own turn (before firing): height from the
        // track contacts, so no bouncing on pixels and no floating.
        // Knockback stays fully physical: it only happens after firing or on
        // someone else's turn, when this constraint is off.
        if (IsMyTurn && !HasFired && IsAlive)
            ConstrainToGround();
    }

    protected virtual void Update()
    {
        AlignToSlope();
        UpdateComponentFlash();
        // Fell through the terrain or off the side of the world: destroyed.
        if (IsAlive && (transform.position.y < -11f ||
            Mathf.Abs(transform.position.x) > terrain.width * 0.5f + 10f))
            Die(silent: true);
    }

    /// <summary>
    /// A component at or below 20% health flashes white; the blink rate
    /// speeds up as it approaches 0%.
    /// </summary>
    void UpdateComponentFlash()
    {
        if (!IsAlive || components == null) return;
        FlashComponent("Hull", hullRenderer, hullBaseColor);
        FlashComponent("Turret", turretRenderer, turretBaseColor);
        FlashComponent("Cannon", weaponRenderer, weaponBaseColor);
    }

    void FlashComponent(string componentName, SpriteRenderer sr, Color baseColor)
    {
        if (sr == null) return;
        var c = components.Find(x => x.name == componentName);
        if (c == null || c.maxHP <= 0f) { sr.color = baseColor; return; }
        float frac = c.hp / c.maxHP;
        if (frac > 0.2f || !IsAlive) { sr.color = baseColor; return; }
        float urgency = 1f - Mathf.Clamp01(frac / 0.2f); // 0 at 20%, 1 at 0%
        float blinksPerSecond = Mathf.Lerp(1f, 6f, urgency);
        bool on = (Time.time * blinksPerSecond) % 1f < 0.5f;
        sr.color = on ? Color.white : baseColor;
    }

    /// <summary>
    /// Kinematically hugs the tank to the terrain: height from the lower of the
    /// front/rear track contacts (plus a small forgiveness so the leading edge
    /// climbs small pixels instead of digging in). No bounce, no float.
    /// </summary>
    void ConstrainToGround()
    {
        if (terrain == null) return;
        float x = rb.position.x;
        float hLead = terrain.GetHeightAt(x + facing * probeHalfWidth);
        float hTrail = terrain.GetHeightAt(x - facing * probeHalfWidth);

        float climb = Mathf.Clamp(hLead - hTrail, 0f, climbForgiveness);
        float targetY = (hLead + hTrail) * 0.5f + rideHeight + climb;

        float vy = Mathf.Clamp((targetY - rb.position.y) * groundFollowSharpness, -10f, 10f);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, vy);
    }

    /// <summary>
    /// Tilts the visual body to the average inclination between the front and
    /// rear track contacts. Stable on pixel steps and scales with hull size
    /// via probeHalfWidth (no raycasts).
    /// </summary>
    void AlignToSlope()
    {
        if (visual == null || terrain == null) return;
        float x = transform.position.x;
        float hLead = terrain.GetHeightAt(x + facing * probeHalfWidth);
        float hTrail = terrain.GetHeightAt(x - facing * probeHalfWidth);
        float z = Mathf.Atan2(hLead - hTrail, 2f * probeHalfWidth) * Mathf.Rad2Deg * facing;
        z = Mathf.Clamp(z, -30f, 30f);
        Quaternion want = Quaternion.Euler(0f, 0f, z);
        visual.rotation = Quaternion.Lerp(visual.rotation, want,
            1f - Mathf.Exp(-slopeAlignSpeed * Time.deltaTime));
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

    /// <summary>Turns the hull's front toward a direction (+1 right, -1 left).</summary>
    public void SetFacing(int dir)
    {
        facing = dir >= 0 ? 1 : -1;
        if (hull != null)
            hull.localScale = new Vector3(hullScale * facing, hullScale, 1f);
        UpdateBarrel();
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
        if (!IsAlive || components == null || components.Count == 0) return;
        // Each hit damages one random component; the bar shows the average.
        var c = components[Random.Range(0, components.Count)];
        c.hp = Mathf.Max(0f, c.hp - dmg);
        UpdateHealthBar();
        if (AverageHP <= 0f) Die(silent: false);
    }

    public void Knockback(Vector2 force)
    {
        if (IsAlive) rb.AddForce(force, ForceMode2D.Impulse);
    }

    protected void Die(bool silent)
    {
        if (components != null)
            foreach (var c in components) c.hp = 0f;
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
        float f = Mathf.Clamp01(AverageHP / AverageMaxHP);
        healthFill.transform.localScale = new Vector3(HealthBarW * f, HealthBarH, 1f);
        healthFill.color = Color.red;
        if (healthText != null)
            healthText.text = Mathf.CeilToInt(AverageHP).ToString();
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
