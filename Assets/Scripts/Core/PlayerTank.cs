using UnityEngine;

/// <summary>
/// Keyboard control for the human tank.
/// A/D or arrows: drive (limited fuel per turn) | W/S or up/down: aim |
/// SPACE: fire at the weapon's fixed power | 1/2/3: switch active weapon.
/// </summary>
public class PlayerTank : Tank
{
    static readonly KeyCode[] slotKeys =
        { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3 };

    protected override void Update()
    {
        base.Update();

        if (!CanControl)
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

        // Number keys switch the active hardpoint's weapon (empty slots are
        // skipped); the firing arc and HUD follow the active weapon.
        for (int i = 0; i < slotKeys.Length; i++)
            if (Input.GetKeyDown(slotKeys[i]))
                SelectMount(i);

        // Shot power is a function of the weapon: SPACE fires immediately.
        // Fire() itself enforces the mode's gate (turn or cooldown).
        if (Input.GetKeyDown(KeyCode.Space))
            Fire();

        UpdatePreview(mode != null ? mode.Wind : 0f);
    }
}
