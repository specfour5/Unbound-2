using UnityEngine;
using System;

/// <summary>
/// A fired shell. Flies with real physics, feels wind, and resolves on
/// impact according to its WeaponDef:
///   - terrain: penetration vs pixel hardness decides whether the shell
///     burrows before detonating; explosiveForce vs hardness shapes the crater.
///   - tanks: penetration vs armorHardness decides between a penetrating
///     direct hit and a surface blast.
/// ExplosiveSize sets how far the blast travels, explosiveForce how much
/// damage it deals (and how hard a material it breaks).
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class Projectile : MonoBehaviour
{
    // Must match the physics used by Tank.UpdatePreview and EnemyTank.AimAtOpponent.
    public const float GravityScale = 1f;
    public const float WindEffect = 0.55f;

    [Header("Tuning")]
    public float lifeTime = 12f;

    WeaponDef weapon; // set by Configure; Basic Cannon if never set
    Rigidbody2D rb;
    Terrain terrain; // cached; shells sweep the true-2D grid themselves
    Vector2 lastSweepPos;
    float wind;
    Action onExploded;
    bool exploded;
    float age;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = GravityScale;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    public void Configure(WeaponDef def)
    {
        weapon = def;
    }

    public void Launch(Vector2 velocity, float windValue, Action explodedCallback)
    {
        wind = windValue;
        onExploded = explodedCallback;
        rb.linearVelocity = velocity;
        lastSweepPos = rb.position;
    }

    void FixedUpdate()
    {
        if (exploded) return;
        // Terrain hit test: sweep the segment traveled since the last physics
        // step against the grid, in pixelSize/2 increments. Terrain has no
        // collider anymore (true-2D occupancy can't be an edge loop), and the
        // sweep means fast shells can't skip through thin crater walls.
        Vector2 cur = rb.position;
        if (terrain == null) terrain = FindAnyObjectByType<Terrain>();
        if (terrain != null && SweepHitsTerrain(lastSweepPos, cur, out Vector2 hitP))
        {
            OnTerrainHit(hitP);
            return;
        }
        lastSweepPos = cur;
        if (Mathf.Abs(wind) > 0.01f)
            rb.AddForce(Vector2.right * wind * WindEffect, ForceMode2D.Force);
    }

    bool SweepHitsTerrain(Vector2 a, Vector2 b, out Vector2 hit)
    {
        hit = b;
        float dist = Vector2.Distance(a, b);
        float step = terrain.pixelSize * 0.5f;
        int n = Mathf.Max(1, Mathf.CeilToInt(dist / step));
        for (int i = 1; i <= n; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, (float)i / n);
            if (terrain.IsSolidAt(p.x, p.y)) { hit = p; return true; }
        }
        return false;
    }

    void OnTerrainHit(Vector2 p)
    {
        var w = weapon ?? WeaponCatalog.BasicCannon;
        float hardness = terrain.GetHardnessAt(p.x, p.y);
        if (w.penetration > hardness)
        {
            // Burrows into the ground before detonating — with no column-top
            // invariant this naturally digs angled tunnels, not just bowls.
            Vector2 dir = rb.linearVelocity.sqrMagnitude > 0.01f
                ? rb.linearVelocity.normalized : Vector2.down;
            p += dir * (w.penetration - hardness) * 0.75f;
        }
        ExplodeAt(p);
    }

    void Update()
    {
        age += Time.deltaTime;
        if (rb.linearVelocity.sqrMagnitude > 0.1f)
        {
            float z = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, z);
        }
        if (age > lifeTime && !exploded)
            ExplodeAt(transform.position); // flew off somewhere: end the turn anyway
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (exploded) return;
        // Terrain hits are found by the grid sweep in FixedUpdate (terrain has
        // no collider); physics collisions here are tanks only.
        var w = weapon ?? WeaponCatalog.BasicCannon;
        Vector2 p = collision.contacts.Length > 0
            ? collision.contacts[0].point : (Vector2)transform.position;

        var tank = collision.gameObject.GetComponent<Tank>();
        if (tank != null && tank.IsAlive)
        {
            if (w.penetration > tank.armorHardness)
            {
                // Punches through the armor: full force straight into a component.
                exploded = true;
                tank.TakeDamage(w.explosiveForce);
                Vector2 kb = rb.linearVelocity.sqrMagnitude > 0.01f
                    ? rb.linearVelocity.normalized : Vector2.up;
                tank.Knockback(kb * w.explosiveForce * 0.12f);
                ExplosionFX.Spawn(p, 1.6f);
                CameraFollow.Shake(0.7f);
                onExploded?.Invoke();
                Destroy(gameObject);
                return;
            }
            // Otherwise the shell bursts on the armor: normal blast below.
        }
        ExplodeAt(p);
    }

    void ExplodeAt(Vector2 p)
    {
        if (exploded) return;
        exploded = true;
        var w = weapon ?? WeaponCatalog.BasicCannon;
        float radius = w.explosiveSize;
        float force = w.explosiveForce;

        var terrain = FindAnyObjectByType<Terrain>();
        if (terrain != null)
            terrain.CarveCrater(p, radius, force);

        foreach (var tank in FindObjectsByType<Tank>(FindObjectsInactive.Exclude))
        {
            if (!tank.IsAlive) continue;
            float d = Vector2.Distance(p, tank.transform.position);
            if (d < radius)
            {
                float f = 1f - d / radius;
                tank.TakeDamage(Mathf.Max(6f, force * f));
                Vector2 dir = (Vector2)tank.transform.position - p;
                if (dir.sqrMagnitude < 0.01f) dir = Vector2.up;
                tank.Knockback(dir.normalized * f * 9f + Vector2.up * f * 4f);
            }
        }

        ExplosionFX.Spawn(p, radius);
        CameraFollow.Shake(1.2f);
        onExploded?.Invoke();
        Destroy(gameObject);
    }
}
