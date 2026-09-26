using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD: power / angle / wind / timer readouts, fuel bar, turn banners,
/// controls help, and the win/lose panel. The setup script builds the
/// whole Canvas; this class just finds the pieces by name and drives them.
/// </summary>
public class GameUI : MonoBehaviour
{
    public TurnManager turnManager;

    Text powerText;
    Text angleText;
    Text windText;
    Text timerText;
    Text bannerText;
    Text helpText;
    Image fuelFill;
    GameObject gameOverPanel;
    Text gameOverText;

    float bannerTimer;

    void Awake()
    {
        powerText = FindText("TopBar/PowerText");
        angleText = FindText("TopBar/AngleText");
        windText = FindText("TopBar/WindText");
        timerText = FindText("TopBar/TimerText");
        bannerText = FindText("BannerText");
        helpText = FindText("HelpText");
        fuelFill = transform.Find("FuelBar/Fill").GetComponent<Image>();
        gameOverPanel = transform.Find("GameOverPanel").gameObject;
        gameOverText = gameOverPanel.transform.Find("GameOverText").GetComponent<Text>();
        SetBannerAlpha(0f);
    }

    Text FindText(string path)
    {
        var t = transform.Find(path);
        return t != null ? t.GetComponent<Text>() : null;
    }

    void Update()
    {
        if (turnManager == null) return;

        var tank = turnManager.CurrentTank;
        if (tank != null)
        {
            powerText.text = $"Power  {tank.power:F0}";
            angleText.text = $"Angle  {tank.angle:F0}°";
            fuelFill.fillAmount = Mathf.Clamp01(tank.FuelLeft / tank.fuelPerTurn);
            timerText.text = $"{Mathf.CeilToInt(Mathf.Max(0f, turnManager.TimeLeft))}s";
            helpText.gameObject.SetActive(tank.isPlayer);
        }

        float w = Mathf.Abs(turnManager.Wind);
        windText.text = turnManager.Wind >= 0 ? $"Wind  → {w:F1}" : $"Wind  ← {w:F1}";

        if (bannerTimer > 0f)
        {
            bannerTimer -= Time.deltaTime;
            SetBannerAlpha(Mathf.Clamp01(bannerTimer / 0.5f));
        }
    }

    public void ShowTurnBanner(string s)
    {
        bannerText.text = s;
        bannerTimer = 1.6f;
        SetBannerAlpha(1f);
    }

    public void ShowGameOver(bool won)
    {
        gameOverPanel.SetActive(true);
        gameOverText.text = won ? "You Win!" : "You Lose\n<size=36>Press R to restart</size>";
    }

    void SetBannerAlpha(float a)
    {
        if (bannerText == null) return;
        var c = bannerText.color;
        c.a = a;
        bannerText.color = c;
    }
}
