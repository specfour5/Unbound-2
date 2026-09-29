using UnityEngine;
using System.Collections;

/// <summary>
/// Simple artillery AI: shuffles to a nearby spot, solves the ballistic
/// trajectory to the player, adds aim error based on skill, and fires.
/// </summary>
public class EnemyTank : Tank
{
    [Range(0f, 1f)]
    [Tooltip("1 = perfect aim, 0 = stormtrooper.")]
    public float skill = 0.8f;

    [Header("Real-time AI (side-scroller mode)")]
    [Tooltip("When true, aims and fires on cooldown whenever the target is in range.")]
    public bool realTimeAI;
    [Tooltip("Won't engage targets beyond this distance.")]
    public float aggroRange = 45f;

    float aiAimClock;
    float aiSettleClock;

    protected override void Update()
    {
        base.Update();
        if (!realTimeAI || !IsAlive || mode == null) return;

        Tank target = mode.GetTargetFor(this);
        if (target == null || !target.IsAlive) return;
        if (Vector2.Distance(transform.position, target.transform.position) > aggroRange)
            return;

        // Re-aim about once a second; fire once the new aim has settled and
        // the weapon's cooldown has elapsed.
        aiAimClock += Time.deltaTime;
        aiSettleClock += Time.deltaTime;
        if (aiAimClock >= 1f)
        {
            aiAimClock = 0f;
            aiSettleClock = 0f;
            AimAtOpponent(target);
        }
        if (aiSettleClock > 0.4f && mode.CanFire(this))
            Fire();
    }

    public IEnumerator RunTurn()
    {
        yield return new WaitForSeconds(0.8f);

        // Reposition a little (sometimes stays put).
        float dir = 0f;
        float roll = Random.value;
        if (roll < 0.4f) dir = -1f;
        else if (roll < 0.8f) dir = 1f;

        float t = 0f, moveFor = Random.Range(0.4f, 1.4f);
        while (t < moveFor && IsMyTurn && !HasFired && IsAlive)
        {
            moveInput = dir;
            t += Time.deltaTime;
            yield return null;
        }
        moveInput = 0f;

        yield return new WaitForSeconds(0.5f);
        AimAtOpponent(mode != null ? mode.GetTargetFor(this) : null);
        yield return new WaitForSeconds(0.7f);
        Fire();
    }

    void AimAtOpponent(Tank target)
    {
        if (target == null) return;

        Vector2 to = (Vector2)target.transform.position + Vector2.up * 0.5f;
        SetFacing(to.x >= transform.position.x ? 1 : -1); // turn the front toward the target

        Vector2 from = muzzle.position;
        float dx = (to.x - from.x) * facing; // forward distance (positive = ahead)
        float dy = to.y - from.y;
        float G = -Physics2D.gravity.y * Projectile.GravityScale;
        float v = ShotPower; // fixed by the weapon; the AI solves the angle for it

        float solution = 45f;
        if (dx > 1f)
        {
            float v2 = v * v;
            float disc = v2 * v2 - G * (G * dx * dx + 2f * dy * v2);
            if (disc > 0f)
            {
                // Low-arc solution of the projectile range equation.
                float tan = (v2 - Mathf.Sqrt(disc)) / (G * dx);
                solution = Mathf.Atan(tan) * Mathf.Rad2Deg;
            }
        }

        float err = 1f - skill;
        float worldAngle = solution + Random.Range(-1f, 1f) * err * 22f;
        // Convert the world-space solution to vehicle-relative elevation:
        // the signed angle from the (pitched) front axis to the desired
        // world firing direction. The AI always uses its main-gun loadout.
        float sRad = worldAngle * Mathf.Deg2Rad;
        Vector2 desired = new Vector2(Mathf.Cos(sRad) * facing, Mathf.Sin(sRad));
        var w = ActiveWeapon ?? WeaponCatalog.BasicCannon;
        angle = Mathf.Clamp(Vector2.SignedAngle(FrontDirection, desired),
            w.minElevation, w.maxElevation);
        UpdateBarrel();
    }
}
