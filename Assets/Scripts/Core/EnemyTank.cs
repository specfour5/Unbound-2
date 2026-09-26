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
        AimAtOpponent();
        yield return new WaitForSeconds(0.7f);
        Fire();
    }

    void AimAtOpponent()
    {
        Tank target = turnManager.GetFirstAliveOpponent(this);
        if (target == null) return;

        Vector2 to = (Vector2)target.transform.position + Vector2.up * 0.5f;
        SetFacing(to.x >= transform.position.x ? 1 : -1); // turn the front toward the target

        Vector2 from = muzzle.position;
        float dx = (to.x - from.x) * facing; // forward distance (positive = ahead)
        float dy = to.y - from.y;
        float G = -Physics2D.gravity.y * Projectile.GravityScale;
        float v = Mathf.Lerp(minPower, maxPower, 0.6f);

        float solution = 45f;
        bool inRange = false;
        if (dx > 1f)
        {
            float v2 = v * v;
            float disc = v2 * v2 - G * (G * dx * dx + 2f * dy * v2);
            if (disc > 0f)
            {
                // Low-arc solution of the projectile range equation.
                float tan = (v2 - Mathf.Sqrt(disc)) / (G * dx);
                solution = Mathf.Atan(tan) * Mathf.Rad2Deg;
                inRange = true;
            }
        }

        float err = 1f - skill;
        angle = Mathf.Clamp(solution + Random.Range(-1f, 1f) * err * 22f, minAngle, maxAngle);
        power = Mathf.Clamp(v * (inRange ? Random.Range(1f - err * 0.2f, 1f + err * 0.2f) : 1f),
            minPower, maxPower);
        UpdateBarrel();
    }
}
