using UnityEngine;

/// <summary>
/// Keyboard control for the human tank.
/// A/D or arrows: drive (limited fuel per turn) | W/S or up/down: aim |
/// SPACE: fire at the weapon's fixed power.
/// </summary>
public class PlayerTank : Tank
{
    protected override void Update()
    {
        base.Update();

        if (!IsMyTurn || HasFired || !IsAlive)
        {
            moveInput = 0f;
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

        // Shot power is a function of the weapon: SPACE fires immediately.
        if (Input.GetKeyDown(KeyCode.Space))
            Fire();

        UpdatePreview(turnManager.Wind);
    }
}
