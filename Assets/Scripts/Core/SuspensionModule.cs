using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// One ground-contact module (wheel, leg, ...). Two-pass simulation:
/// pass 1 probes the terrain and computes a progressive spring force;
/// pass 2 applies it averaged with neighboring modules (anti-roll coupling)
/// plus per-module drive friction and lateral grip.
/// The spring is progressive: push force ramps up toward full bump
/// (bump-stop feel), and a gentle top-out spring resists full droop, so a
/// bobbing wheel can't hand its oscillation to the next wheel along.
/// </summary>
public abstract class SuspensionModule : MonoBehaviour
{
    [Header("Suspension")]
    [Tooltip("Axle rest distance below the anchor.")]
    public float restLength = 0.32f;
    public float minLength = 0.08f;
    public float maxLength = 0.55f;
    [Tooltip("Spring stiffness per module.")]
    public float stiffness = 220f;
    [Tooltip("Spring damping per module.")]
    public float damping = 28f;

    [Header("Progressive spring")]
    [Tooltip("Extra firming near full bump: push force ramps up to (1 + this) x linear at max compression.")]
    public float bumpProgressive = 1.5f;
    [Tooltip("Top-out spring stiffness: gentle downward pull as the module nears full droop.")]
    public float topOutStiffness = 60f;

    [Header("Neighbor coupling")]
    [Tooltip("Anti-roll: blend this module's spring force with its neighbors'. 0 = independent, 1 = full average.")]
    [Range(0f, 1f)] public float neighborCoupling = 0.5f;

    [Header("Drive")]
    [Tooltip("Friction force this module contributes at full throttle and full load.")]
    public float driveForce = 15f;
    [Tooltip("Friction multiplier (worn wheels grip less, claws grip more).")]
    public float grip = 1f;
    [Tooltip("Sideways slide resistance at the contact patch.")]
    public float lateralGrip = 50f;
    [Tooltip("Rolling drag at the contact patch (wheels only).")]
    public float rollingResistance = 2f;

    [HideInInspector] public Vehicle vehicle;
    [HideInInspector] public Terrain terrain;
    [HideInInspector] public List<SuspensionModule> neighbors = new List<SuspensionModule>();
    [HideInInspector] public float springScalar; // pass-1 result: signed force along hull-up (+ = push)

    public bool IsGrounded { get; protected set; }
    /// <summary>Current suspension length (anchor to axle/contact-center).</summary>
    public float CurrentLength { get; protected set; }
    /// <summary>Axle position in hull-local space (module origin sits at the anchor).</summary>
    public Vector2 AxleLocal => new Vector2(0f, -CurrentLength);
    /// <summary>Last computed ground contact point (world).</summary>
    public Vector2 LastContact { get; protected set; }

    protected Rigidbody2D rb;
    protected float prevCompression;
    protected Vector2 anchorPoint;
    protected Vector2 contactPoint;
    protected float loadFactor;

    /// <summary>Contact patch radius (wheel radius, foot radius, ...).</summary>
    protected virtual float ContactRadius => 0.2f;
    /// <summary>How far past the contact the ground probe reaches (droop detection).</summary>
    protected virtual float ProbeSlack => 0.4f;
    /// <summary>True for rolling contacts (wheels), false for planting ones (feet).</summary>
    protected virtual bool Rolling => false;

    public virtual void Init(Vehicle v, Terrain t)
    {
        vehicle = v;
        terrain = t;
        rb = v != null ? v.GetComponent<Rigidbody2D>() : null;
        CurrentLength = restLength;
        prevCompression = 0f;
    }

    /// <summary>
    /// Smooth terrain height under a point: Catmull-Rom through the column
    /// surfaces (the same smooth silhouette the renderer draws), so the
    /// suspension never sees the sim grid's pixel steps.
    /// </summary>
    protected float SampleGround(float x)
    {
        return terrain.SampleSmoothHeight(x);
    }

    /// <summary>
    /// Pass 1: probe the terrain, update lengths, and compute the progressive
    /// spring force (stored in springScalar, applied in pass 2).
    /// </summary>
    public void ComputeSuspension(float dt)
    {
        if (vehicle == null || terrain == null || rb == null)
        {
            IsGrounded = false;
            springScalar = 0f;
            loadFactor = 0.25f;
            return;
        }

        Transform hull = vehicle.transform;
        anchorPoint = hull.TransformPoint(transform.localPosition);
        Vector2 down = (Vector2)(hull.rotation * Vector2.down);

        float contactRadius = ContactRadius;
        float naturalLen = restLength + contactRadius;
        float probeLen = naturalLen + ProbeSlack;
        float groundY = SampleGround(anchorPoint.x + down.x * probeLen);
        float distAlong = (anchorPoint.y - groundY) / Mathf.Max(0.35f, -down.y);

        IsGrounded = distAlong < naturalLen;
        // Continuous ground tracking (no up/down snap at the threshold).
        CurrentLength = Mathf.Clamp(distAlong - contactRadius, minLength, maxLength);

        float cMax = Mathf.Max(restLength - minLength, 0.01f);
        float compression = Mathf.Max(0f, restLength - CurrentLength);
        float compVel = (compression - prevCompression) / dt;
        prevCompression = compression;

        // Progressive bump: push force ramps quadratically to
        // (1 + bumpProgressive) x linear at full compression (bump stop).
        float bumpT = Mathf.Clamp01(compression / cMax);
        float bumpForce = stiffness * compression * (1f + bumpProgressive * bumpT * bumpT);

        // Top-out spring: gentle downward pull over the droop range,
        // strongest at full extension (resists topping out).
        float droopMax = Mathf.Max(maxLength - restLength, 0.01f);
        float droop = Mathf.Max(0f, CurrentLength - restLength);
        float topT = Mathf.Clamp01(droop / droopMax);
        float topOutForce = topOutStiffness * droop * topT;

        springScalar = bumpForce + damping * compVel - topOutForce;
        // NOTE: the damper term is +damping*compVel because compVel is the
        // *compression* velocity: when the hull rises, compression decreases
        // (compVel < 0) and the damper must reduce the upward push. The old
        // sign (-damping*compVel) was anti-damping: it fed energy into the
        // bounce, which is why more damping made the bouncing worse.
        // The spring may pull (top-out) but never harder than the top-out
        // spring allows: a dangling module can't yank the hull down.
        springScalar = Mathf.Max(springScalar, -topOutStiffness * droopMax);

        // Heavily loaded modules contribute more friction (weight transfer).
        loadFactor = Mathf.Clamp01(compression / (restLength * 0.6f) + 0.25f);
        contactPoint = anchorPoint + down * (CurrentLength + contactRadius);
        LastContact = contactPoint;
    }

    /// <summary>Pass 2a: apply the neighbor-coupled spring force at the anchor.</summary>
    protected void ApplyCoupledSpring()
    {
        if (rb == null || vehicle == null) return;
        float f = vehicle.GetCoupledSpringForce(this);
        Vector2 up = -(Vector2)(vehicle.transform.rotation * Vector2.down);
        rb.AddForceAtPosition(up * f, anchorPoint);
    }

    /// <summary>
    /// Pass 2b: drive friction and rolling drag through the center of mass
    /// (no wheelie torque), lateral grip at the contact patch.
    /// </summary>
    protected void ApplyDriveAndGrip()
    {
        if (rb == null || vehicle == null || !IsGrounded) return;
        Vector2 fwd = vehicle.Forward;

        float speedF = Vector2.Dot(rb.linearVelocity, fwd);
        float driveF = 0f;
        if (vehicle.DriveActive)
        {
            float input = Mathf.Clamp(vehicle.moveInput, -1f, 1f);
            float target = input * vehicle.maxSpeed;
            float gripF = target != 0f ? Mathf.Clamp01(1f - speedF / target) : 0f;
            driveF = input * driveForce * grip * loadFactor * gripF;
        }
        float rollF = Rolling ? -speedF * rollingResistance * loadFactor : 0f;
        rb.AddForce(fwd * (driveF + rollF));

        // Lateral grip: kill sideways sliding at the contact patch.
        Vector2 lat = new Vector2(-fwd.y, fwd.x);
        float speedL = Vector2.Dot(rb.linearVelocity, lat);
        rb.AddForceAtPosition(-lat * (speedL * lateralGrip * grip * loadFactor), contactPoint);
    }

    /// <summary>Pass 2: apply forces (coupled spring + drive/grip) and update visuals.</summary>
    public abstract void Simulate(float dt);
}
