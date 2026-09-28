using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pre-game setup screen: wind variability and spawn distance, then start.
/// Built by the setup script; hidden once the battle begins.
/// </summary>
public class SetupMenu : MonoBehaviour
{
    [Header("Wiring (set by the setup script)")]
    public TurnManager turnManager;
    public Terrain terrain;
    public GameObject panel;
    public Button[] windButtons;
    public Button[] spawnButtons;
    public Button[] fuelButtons;
    public Button[] weaponButtons;
    public Button[] modeButtons;
    public Button startButton;
    public RectTransform windFrame;
    public RectTransform spawnFrame;
    public RectTransform fuelFrame;
    public RectTransform weaponFrame;
    public RectTransform modeFrame;

    // Game mode: duel is the classic turn-based battle; side-scroller is the
    // real-time campaign (loads the SideScroller scene).
    public static readonly string[] ModeNames = { "Duel", "Side-Scroller" };

    // Wind options: index 2 ("Default") matches the game's long-standing behavior.
    public static readonly string[] WindNames = { "None", "Light", "Default", "Strong", "Extreme" };
    static readonly float[] windMults = { 0f, 0.5f, 1f, 1.75f, 2.75f };

    // Spawn options: full distance between the two tanks (they spawn symmetric).
    public static readonly string[] SpawnNames = { "Close", "Default", "Far" };
    static readonly float[] spawnDists = { 40f, 76f, 110f };

    // Fuel options: Low is the tank's default, High doubles it, Unlimited never drains.
    public static readonly string[] FuelNames = { "Low", "High", "Unlimited" };

    int windIndex = 2;
    int spawnIndex = 1;
    int fuelIndex = 0;
    int weaponIndex = 0;
    int modeIndex = 0;

    void Start()
    {
        for (int i = 0; i < modeButtons.Length; i++)
        {
            int k = i;
            modeButtons[i].onClick.AddListener(() => SelectMode(k));
        }
        for (int i = 0; i < windButtons.Length; i++)
        {
            int k = i;
            windButtons[i].onClick.AddListener(() => SelectWind(k));
        }
        for (int i = 0; i < spawnButtons.Length; i++)
        {
            int k = i;
            spawnButtons[i].onClick.AddListener(() => SelectSpawn(k));
        }
        for (int i = 0; i < fuelButtons.Length; i++)
        {
            int k = i;
            fuelButtons[i].onClick.AddListener(() => SelectFuel(k));
        }
        for (int i = 0; i < weaponButtons.Length; i++)
        {
            int k = i;
            if (weaponButtons[i].interactable)
                weaponButtons[i].onClick.AddListener(() => SelectWeapon(k));
        }
        startButton.onClick.AddListener(StartBattle);
        Refresh();
        if (panel != null) panel.SetActive(true);
    }

    public void SelectMode(int i)
    {
        modeIndex = Mathf.Clamp(i, 0, modeButtons.Length - 1);
        Refresh();
    }

    public void SelectWind(int i)
    {
        windIndex = Mathf.Clamp(i, 0, windButtons.Length - 1);
        Refresh();
    }

    public void SelectSpawn(int i)
    {
        spawnIndex = Mathf.Clamp(i, 0, spawnButtons.Length - 1);
        Refresh();
    }

    public void SelectFuel(int i)
    {
        fuelIndex = Mathf.Clamp(i, 0, fuelButtons.Length - 1);
        Refresh();
    }

    public void SelectWeapon(int i)
    {
        weaponIndex = Mathf.Clamp(i, 0, weaponButtons.Length - 1);
        Refresh();
    }

    void Refresh()
    {
        for (int i = 0; i < modeButtons.Length; i++)
        {
            bool sel = i == modeIndex;
            Tint(modeButtons[i], sel);
            StyleLabel(modeButtons[i], sel);
        }
        for (int i = 0; i < windButtons.Length; i++)
        {
            bool sel = i == windIndex;
            Tint(windButtons[i], sel);
            StyleLabel(windButtons[i], sel);
        }
        for (int i = 0; i < spawnButtons.Length; i++)
        {
            bool sel = i == spawnIndex;
            Tint(spawnButtons[i], sel);
            StyleLabel(spawnButtons[i], sel);
        }
        for (int i = 0; i < fuelButtons.Length; i++)
        {
            bool sel = i == fuelIndex;
            Tint(fuelButtons[i], sel);
            StyleLabel(fuelButtons[i], sel);
        }
        for (int i = 0; i < weaponButtons.Length; i++)
        {
            bool sel = i == weaponIndex;
            Tint(weaponButtons[i], sel);
            StyleLabel(weaponButtons[i], sel);
        }
        if (modeFrame != null && modeButtons.Length > 0)
            modeFrame.anchoredPosition = modeButtons[modeIndex].GetComponent<RectTransform>().anchoredPosition;
        if (windFrame != null && windButtons.Length > 0)
            windFrame.anchoredPosition = windButtons[windIndex].GetComponent<RectTransform>().anchoredPosition;
        if (spawnFrame != null && spawnButtons.Length > 0)
            spawnFrame.anchoredPosition = spawnButtons[spawnIndex].GetComponent<RectTransform>().anchoredPosition;
        if (fuelFrame != null && fuelButtons.Length > 0)
            fuelFrame.anchoredPosition = fuelButtons[fuelIndex].GetComponent<RectTransform>().anchoredPosition;
        if (weaponFrame != null && weaponButtons.Length > 0)
            weaponFrame.anchoredPosition = weaponButtons[weaponIndex].GetComponent<RectTransform>().anchoredPosition;
    }

    static void Tint(Button b, bool selected)
    {
        var img = b.GetComponent<Image>();
        if (img != null)
            img.color = selected ? new Color(0.25f, 0.62f, 0.32f) : new Color(0.22f, 0.24f, 0.28f);
    }

    static void StyleLabel(Button b, bool selected)
    {
        var t = b.GetComponentInChildren<Text>();
        if (t != null)
        {
            t.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            t.color = selected ? Color.yellow : Color.white;
        }
    }

    void StartBattle()
    {
        WeaponDef chosen = weaponIndex >= 0 && weaponIndex < WeaponCatalog.All.Count
            ? WeaponCatalog.All[weaponIndex] : WeaponCatalog.BasicCannon;

        // Side-scroller: carry the weapon + wind choices into the campaign scene.
        if (modeIndex == 1)
        {
            GameConfig.weapon = chosen;
            GameConfig.windMultiplier = windMults[windIndex];
            if (panel != null) panel.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.LoadScene("SideScroller");
            return;
        }

        float dist = spawnDists[spawnIndex];
        foreach (var t in turnManager.tanks)
        {
            float x = t.isPlayer ? -dist / 2f : dist / 2f;
            terrain.FlattenArea(x, 8f);
            t.transform.position = new Vector3(x, terrain.GetHeightAt(x) + 1.5f, 0f);
            t.SetFacing(t.isPlayer ? 1 : -1);
            t.fuelPerTurn = fuelIndex == 1 ? t.baseFuelPerTurn * 2f : t.baseFuelPerTurn;
            t.unlimitedFuel = fuelIndex == 2;
            t.weapon = chosen;
        }
        turnManager.windMultiplier = windMults[windIndex];
        if (panel != null) panel.SetActive(false);
        turnManager.BeginGame();
    }
}
