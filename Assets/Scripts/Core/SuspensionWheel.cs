using UnityEngine;

/// <summary>
/// A sprung wheel: bobs on its suspension independently of the hull, spins
/// with ground speed, leaves a rut in soft terrain, and churns mud under
/// load. Each grounded wheel adds its own share of drive friction.
/// </summary>
public class SuspensionWheel : SuspensionModule
{
    [Header("Wheel")]
    public float wheelRadius = 0.26f;
    [Tooltip("How far past the wheel the ground probe reaches (droop detection).")]
    public float probeSlack = 0.45f;
    [Tooltip("Rut depth carved per pass (0 = leave no deformation).")]
    public float rutDepth = 0.2f; // one terrain pixel deep, then compacted[] caps it

    [Tooltip("Child sprite; bobs with the suspension and spins with speed.")]
    public Transform wheelVisual;

    float spin;

    public override void Simulate(float dt)
    {
        bool grounded = SimulateCore(dt, wheelRadius, probeSlack, rolling: true);
        if (vehicle == null) return;

        if (wheelVisual != null)
        {
            wheelVisual.localPosition = new Vector3(0f, -CurrentLength, 0f);
            if (grounded && rb != null)
                // Spin with ground speed; facing flips the screen direction.
                spin -= vehicle.facing * Vector2.Dot(rb.linearVelocity, vehicle.Forward) / wheelRadius * dt;
            wheelVisual.localRotation = Quaternion.Euler(0f, 0f, spin * Mathf.Rad2Deg);
        }

        if (grounded && terrain != null)
        {
            float speedF = Mathf.Abs(vehicle.ForwardSpeed);
            if (speedF > 0.4f)
            {
                // Rolling wheels cut a shallow rut and churn the grass to mud.
                if (rutDepth > 0f)
                    terrain.DepressSmooth(LastContact.x, 0.3f, rutDepth);
                terrain.MarkTrackMud(LastContact.x - 0.3f, LastContact.x + 0.3f);
            }
        }
    }
}
