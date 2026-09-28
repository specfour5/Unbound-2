using UnityEngine;

/// <summary>
/// One ground-contact module (wheel, leg, ...). The module is a virtual
/// sprung mass: it probes the terrain below its hull anchor, pushes the hull
/// through a spring-damper, and contributes drive friction plus lateral grip
/// at its contact patch. No rigidbody of its own, so any number of modules
/// stays solver-stable.
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

    public bool IsGrounded { get; protected set; }
    /// <summary>Current suspension length (anchor to axle/contact-center).</summary>
    public float CurrentLength { get; protected set; }
    /// <summary>Axle position in hull-local space (module origin sits at the anchor).</summary>
    public Vector2 AxleLocal => new Vector2(0f, -CurrentLength);
    /// <summary>Last computed ground contact point (world).</summary>
    public Vector2 LastContact { get; protected set; }

    protected Rigidbody2D rb;
    protected float prevCompression;

    public virtual void Init(Vehicle v, Terrain t)
    {
        vehicle = v;
        terrain = t;
        rb = v != null ? v.GetComponent<Rigidbody2D>() : null;
        CurrentLength = restLength;
        prevCompression = 0f;
    }

    /// <summary>Smoothed terrain height (3-tap) so single-pixel steps don't jerk modules.</summary>
    protected float SampleGround(float x)
    {
        const float r = 0.15f;
        return (terrain.GetHeightAt(x - r)
              + terrain.GetHeightAt(x) * 2f
              + terrain.GetHeightAt(x + r)) * 0.25f;
    }

    public abstract void Simulate(float dt);

    /// <summary>
    /// Shared spring/drive core. Probes the terrain along the hull's down
    /// axis, applies the suspension spring at the anchor (so load differences
    /// pitch and roll the hull), then drive friction, lateral grip and
    /// rolling drag at the contact patch. Returns true when touching ground.
    /// </summary>
    protected bool SimulateCore(float dt, float contactRadius, float probeSlack, bool rolling)
    {
        if (vehicle == null || terrain == null || rb == null)
        {
            IsGrounded = false;
            return false;
        }

        Transform hull = vehicle.transform;
        Vector2 anchor = hull.TransformPoint(transform.localPosition);
        Vector2 down = (Vector2)(hull.rotation * Vector2.down);
        Vector2 up = -down;

        float naturalLen = restLength + contactRadius;
        float probeLen = naturalLen + probeSlack;
        float groundY = SampleGround(anchor.x + down.x * probeLen);
        float distAlong = (anchor.y - groundY) / Mathf.Max(0.35f, -down.y);

        IsGrounded = distAlong < naturalLen;
        // Track the ground continuously (clamped): the wheel extends smoothly
        // toward full droop as the hull rises instead of snapping between
        // "tucked" and "dangling" at the contact threshold. That snap was
        // making the wheels (and the track band between them) pop up/down.
        // Spring force stays continuous too: compression hits 0 exactly at
        // the threshold, so lift-off and touchdown are seamless.
        CurrentLength = Mathf.Clamp(distAlong - contactRadius, minLength, maxLength);

        // Spring only pushes (a dangling module doesn't yank the hull down).
        float compression = Mathf.Max(0f, restLength - CurrentLength);
        float compVel = (compression - prevCompression) / dt;
        prevCompression = compression;

        Vector2 fwd = vehicle.Forward;
        if (IsGrounded)
        {
            Vector2 springF = up * (stiffness * compression - damping * compVel);
            if (Vector2.Dot(springF, up) < 0f) springF = Vector2.zero;
            rb.AddForceAtPosition(springF, anchor);

            Vector2 contact = anchor + down * (CurrentLength + contactRadius);
            LastContact = contact;
            // Heavily loaded modules contribute more friction (weight transfer).
            float loadF = Mathf.Clamp01(compression / (restLength * 0.6f) + 0.25f);

            // Drive friction and rolling drag act through the center of mass.
            // Pushing at the ground contact would lever the hull into a
            // wheelie (the contact sits ~1 unit below the CoM), which is
            // what made the suspension porpoise under throttle.
            float speedF = Vector2.Dot(rb.linearVelocity, fwd);
            float driveF = 0f;
            if (vehicle.DriveActive)
            {
                float input = Mathf.Clamp(vehicle.moveInput, -1f, 1f);
                float target = input * vehicle.maxSpeed;
                float gripF = target != 0f ? Mathf.Clamp01(1f - speedF / target) : 0f;
                driveF = input * driveForce * grip * loadF * gripF;
            }
            float rollF = rolling ? -speedF * rollingResistance * loadF : 0f;
            rb.AddForce(fwd * (driveF + rollF));

            // Lateral grip: kill sideways sliding at the contact patch.
            Vector2 lat = new Vector2(-fwd.y, fwd.x);
            float speedL = Vector2.Dot(rb.linearVelocity, lat);
            rb.AddForceAtPosition(-lat * (speedL * lateralGrip * grip * loadF), contact);
        }
        else
        {
            LastContact = anchor + down * (CurrentLength + contactRadius);
        }
        return IsGrounded;
    }
}
