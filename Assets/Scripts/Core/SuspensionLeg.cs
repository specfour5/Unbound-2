using UnityEngine;

/// <summary>
/// A spring-strut leg (v1: pogo-style). Same suspension/drive interface as a
/// wheel, so walkers plug into any Vehicle, but the foot doesn't roll: it
/// plants, grips, and shoves. Each touchdown stamps a chunky footprint into
/// the terrain. (A real stepping gait is future work; the physics hook is
/// already here.)
/// </summary>
public class SuspensionLeg : SuspensionModule
{
    [Header("Leg")]
    public float footRadius = 0.16f;
    [Tooltip("How far past the foot the ground probe reaches.")]
    public float probeSlack = 0.5f;
    [Tooltip("Child sprite for the foot; bobs with the strut.")]
    public Transform footVisual;
    [Tooltip("Child sprite stretched between anchor and foot (the strut).")]
    public Transform strutVisual;

    bool wasGrounded;

    public override void Simulate(float dt)
    {
        bool grounded = SimulateCore(dt, footRadius, probeSlack, rolling: false);

        if (grounded && !wasGrounded && terrain != null)
            terrain.StampFootprint(LastContact.x, 0.35f); // touchdown: chunky print
        wasGrounded = grounded;

        if (footVisual != null)
            footVisual.localPosition = new Vector3(0f, -CurrentLength, 0f);

        if (strutVisual != null)
        {
            // Stretch the strut from the anchor (local origin) to the foot.
            strutVisual.localPosition = new Vector3(0f, -CurrentLength * 0.5f, 0f);
            strutVisual.localScale = new Vector3(0.14f, Mathf.Max(CurrentLength, 0.05f), 1f);
        }
    }
}
