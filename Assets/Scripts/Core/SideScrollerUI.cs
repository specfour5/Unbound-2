using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Campaign HUD: kills, map progress, weapon cooldown, wind, and the end panel.
/// All elements are built by the setup script and wired here.
/// </summary>
public class SideScrollerUI : MonoBehaviour
{
    [Header("Wiring (set by the setup script)")]
    public Text killsText;
    public Text windText;
    public Image progressFill;
    public Image cooldownFill;
    public Text cooldownLabel;
    public GameObject endPanel;
    public Text endTitle;
    public Text endSubtitle;

    SideScrollerManager manager;

    public void Init(SideScrollerManager m) => manager = m;

    void Update()
    {
        if (manager == null || manager.GameOver) return;
        if (killsText != null)
            killsText.text = $"KILLS  {manager.Kills}/{manager.TotalEnemies}";
        if (progressFill != null)
            progressFill.fillAmount = manager.Progress01;
        if (manager.player != null)
        {
            // Cooldown bar + label follow the ACTIVE weapon; each mounted
            // weapon cools down on its own timer.
            if (cooldownFill != null)
            {
                float cd = manager.player.ShotCooldown;
                float ready = cd > 0f ? 1f - Mathf.Clamp01(manager.player.CooldownLeft / cd) : 1f;
                cooldownFill.fillAmount = ready;
                cooldownFill.color = ready >= 1f
                    ? new Color(0.35f, 0.85f, 0.40f)
                    : new Color(0.85f, 0.70f, 0.25f);
            }
            if (cooldownLabel != null)
                cooldownLabel.text = (manager.player.ActiveWeapon != null
                    ? manager.player.ActiveWeapon.displayName : "Cannon").ToUpper();
        }
        if (windText != null)
        {
            float w = Mathf.Abs(manager.Wind);
            windText.text = manager.Wind >= 0 ? $"WIND  → {w:F1}" : $"WIND  ← {w:F1}";
        }
    }

    public void ShowEnd(bool won, int kills, int total)
    {
        if (endPanel != null) endPanel.SetActive(true);
        if (endTitle != null)
        {
            endTitle.text = won ? "RUN COMPLETE" : "TANK DESTROYED";
            endTitle.color = won ? new Color(0.35f, 0.85f, 0.40f) : new Color(0.9f, 0.3f, 0.3f);
        }
        if (endSubtitle != null)
            endSubtitle.text = $"Kills: {kills}/{total}     —     press R to retry";
    }
}
