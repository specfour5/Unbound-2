using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Physics-based vehicle hull. The hull is a real rigid body riding on
/// SuspensionModules (wheels, legs, ...): each module probes the terrain,
/// holds the hull up on its own spring, and contributes drive friction at
/// its contact patch. Different module layouts and tunings give different
/// designs their own handling on rough terrain.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Vehicle : MonoBehaviour
{
    [Header("Hull")]
    [Tooltip("Hull mass. Heavier hulls need more drive force and respond more lazily.")]
    public float hullMass = 4.5f;
    [Tooltip("Top speed the drive force aims for (world units/second).")]
    public float maxSpeed = 5f;
    [Tooltip("+1 faces right, -1 faces left (logical; flips drive direction and hull art).")]
    public int facing = 1;

    [Header("Drive")]
    [Tooltip("-1..1 throttle, set by the controller (player input / AI).")]
    public float moveInput;
    [Tooltip("Gated by turn logic: true only while this vehicle may drive.")]
    public bool driveEnabled;

    [Header("Brakes")]
    [Tooltip("Viscous brake force per unit of forward speed when coasting.")]
    public float brakeStrength = 25f;
    [Tooltip("Below this forward speed the brakes hold the tank dead still (no downhill creep).")]
    public float brakeHoldSpeed = 0.35f;

    [Header("Wall bumpers")]
    [Tooltip("How far a penetrating hull is nudged out of terrain per physics step.")]
    public float bumperPush = 0.07f;

    [HideInInspector] public List<SuspensionModule> modules = new List<SuspensionModule>();

    protected Rigidbody2D rb;
    protected Terrain terrain;

    /// <summary>
    /// Hull's right axis in world space (NOT flipped by facing: drive input
    /// is world-space, A = left and D = right; facing only flips the art).
    /// </summary>
    public Vector2 Forward => (Vector2)(transform.rotation * Vector2.right);
    public float ForwardSpeed => rb != null ? Vector2.Dot(rb.linearVelocity, Forward) : 0f;
    public bool AnyGrounded { get; protected set; }
    /// <summary>Total forward distance driven (drives the track tread scroll).</summary>
    public float Odometer { get; protected set; }
    public bool DriveActive => driveEnabled && Mathf.Abs(moveInput) > 0.01f;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.mass = Mathf.Max(hullMass, 0.1f);
        // A low center of mass keeps the hull self-righting after knockback.
        rb.centerOfMass = new Vector2(0f, -0.35f);
        if (rb.angularDamping < 3f) rb.angularDamping = 3f;

        modules.Clear();
        modules.AddRange(GetComponentsInChildren<SuspensionModule>());
        // Neighbors = adjacent modules along the hull (anti-roll coupling).
        modules.Sort((a, b) => a.transform.localPosition.x.CompareTo(b.transform.localPosition.x));
        for (int i = 0; i < modules.Count; i++)
        {
            modules[i].vehicle = this;
            var ns = new List<SuspensionModule>();
            if (i > 0) ns.Add(modules[i - 1]);
            if (i < modules.Count - 1) ns.Add(modules[i + 1]);
            modules[i].neighbors = ns;
        }
    }

    /// <summary>Called once the battle wires up (TurnManager -> Tank.Setup).</summary>
    public virtual void Setup(Terrain tr)
    {
        terrain = tr;
        for (int i = 0; i < modules.Count; i++)
            modules[i].Init(this, tr);
    }

    protected virtual void FixedUpdate()
    {
        if (rb == null || !rb.simulated) return;
        float dt = Time.fixedDeltaTime;

        AnyGrounded = false;
        // Pass 1: every module probes the terrain and computes its spring force.
        for (int i = 0; i < modules.Count; i++)
        {
            var m = modules[i];
            if (m == null || !m.isActiveAndEnabled) continue;
            m.ComputeSuspension(dt);
        }
        // Pass 2: apply neighbor-coupled springs, drive friction, and grip.
        for (int i = 0; i < modules.Count; i++)
        {
            var m = modules[i];
            if (m == null || !m.isActiveAndEnabled) continue;
            m.Simulate(dt);
            if (m.IsGrounded) AnyGrounded = true;
        }

        // Brakes: no drive input and a wheel on the ground — stop the roll
        // promptly and hold still on slopes instead of creeping downhill.
        // Stays applied after firing: the recoil impulse can still shove the
        // tank, but the slide dies out instead of rolling away.
        if (Mathf.Abs(moveInput) <= 0.01f && AnyGrounded)
        {
            Vector2 fwd = Forward;
            float speedF = Vector2.Dot(rb.linearVelocity, fwd);
            rb.AddForce(-fwd * (speedF * brakeStrength));
            if (Mathf.Abs(speedF) < brakeHoldSpeed)
                rb.linearVelocity -= fwd * speedF; // static hold: cancel drift
        }

        RunBumpers();
        EnforceBounds();

        // Cap spin: knockback may tumble the hull, never turn it into a propeller.
        const float maxSpin = 240f;
        if (Mathf.Abs(rb.angularVelocity) > maxSpin)
            rb.angularVelocity = Mathf.Sign(rb.angularVelocity) * maxSpin;

        Odometer += ForwardSpeed * dt;
    }

    /// <summary>
    /// This module's spring force blended with its neighbors' (anti-roll):
    /// a wheel that's suddenly loaded shares the push instead of rocking
    /// the hull and handing the oscillation down the line.
    /// </summary>
    public float GetCoupledSpringForce(SuspensionModule m)
    {
        float own = m.springScalar;
        var ns = m.neighbors;
        float c = Mathf.Clamp01(m.neighborCoupling);
        if (ns == null || ns.Count == 0 || c <= 0f) return own;
        float sum = 0f;
        int n = 0;
        for (int i = 0; i < ns.Count; i++)
        {
            var o = ns[i];
            if (o != null && o.isActiveAndEnabled) { sum += o.springScalar; n++; }
        }
        if (n == 0) return own;
        return Mathf.Lerp(own, sum / n, c);
    }

    /// <summary>
    /// The hull collider ignores terrain (suspension handles the ground), so
    /// these probes depenetrate the body if knockback or a hard landing ever
    /// pushes it inside a crater wall: nudge toward the nearest free cell and
    /// kill the inward velocity.
    /// </summary>
    void RunBumpers()
    {
        if (terrain == null) return;
        BumperProbe(new Vector2(1.45f, 0.05f));   // nose
        BumperProbe(new Vector2(-1.45f, 0.05f));  // tail
        BumperProbe(new Vector2(0f, -0.30f));     // belly
    }

    void BumperProbe(Vector2 local)
    {
        Vector2 p = transform.TransformPoint(local);
        if (!terrain.IsSolidAt(p.x, p.y)) return;
        foreach (var d in bumperDirs)
        {
            Vector2 q = p + d * 0.35f;
            if (!terrain.IsSolidAt(q.x, q.y))
            {
                rb.position += d * bumperPush;
                Vector2 v = rb.linearVelocity;
                float inward = Vector2.Dot(v, -d);
                if (inward > 0f) v += d * inward;
                rb.linearVelocity = v;
                return;
            }
        }
        rb.position += Vector2.up * bumperPush; // fully buried: shove up
    }

    static readonly Vector2[] bumperDirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

    void EnforceBounds()
    {
        if (terrain == null) return;
        float px = rb.position.x;
        Vector2 v = rb.linearVelocity;
        if (px <= terrain.LeftX + 2f && v.x < 0f) v.x = 0f;
        if (px >= terrain.RightX - 2f && v.x > 0f) v.x = 0f;
        rb.linearVelocity = v;
    }
}
