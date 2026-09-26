using UnityEngine;

/// <summary>
/// Keyboard control for the human tank.
/// A/D or arrows: drive (limited fuel per turn) | W/S or up/down: aim |
/// Hold SPACE to charge the shot (maxChargeTime seconds to full power),
/// release to fire.
/// </summary>
public class PlayerTank : Tank
{
    [Tooltip("Seconds of holding SPACE to reach full shot power.")]
    public float maxChargeTime = 1.5f;

    bool charging;
    float chargeTime;

    /// <summary>0..1 charge level, for the HUD.</summary>
    public float Charge01 => Mathf.Clamp01(chargeTime / maxChargeTime);
    public bool IsCharging => charging;

    protected override void Update()
    {
        base.Update();

        if (!IsMyTurn || HasFired || !IsAlive)
        {
            moveInput = 0f;
            charging = false;
            HidePreview();
            return;
        }

        moveInput = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) moveInput -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) moveInput += 1f;

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            AdjustAngle(angleAdjustSpeed * Time.deltaTime);
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            AdjustAngle(-angleAdjustSpeed * Time.deltaTime);

        // Hold SPACE to charge the shot, release to fire. Power ramps from
        // min to max over maxChargeTime seconds; holding longer stays at max.
        // The aim preview arc grows live as the charge builds.
        if (Input.GetKeyDown(KeyCode.Space) && !charging)
        {
            charging = true;
            chargeTime = 0f;
            power = minPower;
        }
        if (charging)
        {
            if (Input.GetKey(KeyCode.Space))
            {
                chargeTime = Mathf.Min(chargeTime + Time.deltaTime, maxChargeTime);
                power = Mathf.Lerp(minPower, maxPower, Charge01);
            }
            else
            {
                charging = false;
                Fire();
            }
        }

        UpdatePreview(turnManager.Wind);
    }
}
