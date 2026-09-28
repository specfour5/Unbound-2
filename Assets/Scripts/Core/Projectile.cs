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
    }

    void FixedUpdate()
    {
        if (!exploded && Mathf.Abs(wind) > 0.01f)
            rb.AddForce(Vector2.right * wind * WindEffect, ForceMode2D.Force);
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
        else
        {
            var terrain = collision.gameObject.GetComponent<Terrain>();
            if (terrain != null)
            {
                float hardness = terrain.GetHardnessAt(p.x, p.y);
                if (w.penetration > hardness)
                {
                    // Burrows into the ground before detonating.
                    Vector2 dir = rb.linearVelocity.sqrMagnitude > 0.01f
                        ? rb.linearVelocity.normalized : Vector2.down;
                    p += dir * (w.penetration - hardness) * 0.75f;
                }
            }
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
