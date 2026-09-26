using UnityEngine;
using System;

/// <summary>
/// A fired shell. Flies with real physics, feels wind, and explodes on
/// impact: carves the terrain, damages nearby tanks with falloff, knocks
/// them around, then tells the TurnManager the turn can end.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class Projectile : MonoBehaviour
{
    // Must match the physics used by Tank.UpdatePreview and EnemyTank.AimAtOpponent.
    public const float GravityScale = 1f;
    public const float WindEffect = 0.55f;

    [Header("Tuning")]
    public float blastRadius = 4.5f;
    public float maxDamage = 55f;
    public float lifeTime = 12f;

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
            Explode(); // flew off somewhere: end the turn anyway
    }

    void OnCollisionEnter2D(Collision2D collision) => Explode();

    void Explode()
    {
        if (exploded) return;
        exploded = true;
        Vector2 p = transform.position;

        var terrain = FindAnyObjectByType<Terrain>();
        if (terrain != null)
            terrain.CarveCrater(p, blastRadius * 0.85f);

        foreach (var tank in FindObjectsByType<Tank>(FindObjectsSortMode.None))
        {
            if (!tank.IsAlive) continue;
            float d = Vector2.Distance(p, tank.transform.position);
            if (d < blastRadius)
            {
                float f = 1f - d / blastRadius;
                tank.TakeDamage(Mathf.Max(6f, maxDamage * f));
                Vector2 dir = (Vector2)tank.transform.position - p;
                if (dir.sqrMagnitude < 0.01f) dir = Vector2.up;
                tank.Knockback(dir.normalized * f * 9f + Vector2.up * f * 4f);
            }
        }

        ExplosionFX.Spawn(p, blastRadius);
        CameraFollow.Shake(1.2f);
        onExploded?.Invoke();
        Destroy(gameObject);
    }
}
