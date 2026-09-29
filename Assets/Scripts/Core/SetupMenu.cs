using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Pre-game setup screen: mode, wind, spawn distance, fuel, and a weapon
/// loadout picker (one row per tank hardpoint), then start.
/// Built by the setup script; hidden once the battle begins.
/// </summary>
public class SetupMenu : MonoBehaviour
{
    [Header("Wiring (set by the setup script)")]
    public TurnManager turnManager;
    public TerrainGrid terrain;
    public GameObject panel;
    public Button[] windButtons;
    public Button[] spawnButtons;
    public Button[] fuelButtons;
    public Button[] modeButtons;
    public Button startButton;
    public RectTransform windFrame;
    public RectTransform spawnFrame;
    public RectTransform fuelFrame;
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
    int modeIndex = 0;

    // Loadout state: one selected option per hardpoint. The buttons and
    // selection frames are found by name at runtime (Unity can't serialize
    // jagged arrays, so the builder names them deterministically instead of
    // wiring them): Box/Hardpoint_<id>_<weaponId|none>, Box/HardpointFrame_<id>.
    string[][] hardpointOptionIds;
    int[] hardpointSelection;
    Button[][] hardpointButtons;
    RectTransform[] hardpointFrames;

    /// <summary>
    /// Weapon ids available per hardpoint (null = the "None" option on
    /// hardpoints that allow it). The setup script builds one button row per
    /// hardpoint from these.
    /// </summary>
    public static string[][] HardpointOptions()
    {
        var layout = Tank.HardpointLayout;
        var all = new string[layout.Length][];
        for (int h = 0; h < layout.Length; h++)
        {
            var opts = new List<string>();
            if (layout[h].allowEmpty) opts.Add(null); // "None"
            foreach (var w in WeaponCatalog.All)
                if (w.available && w.mount == layout[h].mount)
                    opts.Add(w.id);
            all[h] = opts.ToArray();
        }
        return all;
    }

    public static string OptionLabel(string weaponId)
        => weaponId == null ? "None"
            : (WeaponCatalog.Find(weaponId) != null ? WeaponCatalog.Find(weaponId).displayName : weaponId);

    public static int DefaultOptionIndex(int hardpoint)
    {
        var layout = Tank.HardpointLayout[hardpoint];
        var opts = HardpointOptions()[hardpoint];
        for (int i = 0; i < opts.Length; i++)
            if (opts[i] == layout.defaultWeaponId) return i;
        return 0;
    }

    void Start()
    {
        var layout = Tank.HardpointLayout;
        hardpointOptionIds = HardpointOptions();
        hardpointSelection = new int[layout.Length];
        hardpointButtons = new Button[layout.Length][];
        hardpointFrames = new RectTransform[layout.Length];
        for (int h = 0; h < layout.Length; h++)
        {
            hardpointSelection[h] = DefaultOptionIndex(h);
            var opts = hardpointOptionIds[h];
            hardpointButtons[h] = new Button[opts.Length];
            for (int i = 0; i < opts.Length; i++)
            {
                var bt = transform.Find("Box/Hardpoint_" + layout[h].id + "_" + (opts[i] ?? "none"));
                hardpointButtons[h][i] = bt != null ? bt.GetComponent<Button>() : null;
            }
            var fr = transform.Find("Box/HardpointFrame_" + layout[h].id);
            hardpointFrames[h] = fr != null ? fr.GetComponent<RectTransform>() : null;
        }

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
        if (hardpointButtons != null)
            for (int h = 0; h < hardpointButtons.Length; h++)
                for (int i = 0; i < hardpointButtons[h].Length; i++)
                {
                    int hh = h, k = i;
                    var b = hardpointButtons[h][i];
                    if (b != null && b.interactable)
                        b.onClick.AddListener(() => SelectHardpointWeapon(hh, k));
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

    public void SelectHardpointWeapon(int hardpoint, int option)
    {
        if (hardpointSelection == null) return;
        hardpoint = Mathf.Clamp(hardpoint, 0, hardpointSelection.Length - 1);
        option = Mathf.Clamp(option, 0, hardpointOptionIds[hardpoint].Length - 1);
        hardpointSelection[hardpoint] = option;
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
        if (hardpointButtons != null)
            for (int h = 0; h < hardpointButtons.Length; h++)
            {
                for (int i = 0; i < hardpointButtons[h].Length; i++)
                {
                    var b = hardpointButtons[h][i];
                    if (b == null) continue;
                    bool sel = i == hardpointSelection[h];
                    Tint(b, sel);
                    StyleLabel(b, sel);
                }
                var frame = h < hardpointFrames.Length ? hardpointFrames[h] : null;
                var selBtn = hardpointSelection[h] < hardpointButtons[h].Length
                    ? hardpointButtons[h][hardpointSelection[h]] : null;
                if (frame != null && selBtn != null)
                    frame.anchoredPosition = selBtn.GetComponent<RectTransform>().anchoredPosition;
            }
        if (modeFrame != null && modeButtons.Length > 0)
            modeFrame.anchoredPosition = modeButtons[modeIndex].GetComponent<RectTransform>().anchoredPosition;
        if (windFrame != null && windButtons.Length > 0)
            windFrame.anchoredPosition = windButtons[windIndex].GetComponent<RectTransform>().anchoredPosition;
        if (spawnFrame != null && spawnButtons.Length > 0)
            spawnFrame.anchoredPosition = spawnButtons[spawnIndex].GetComponent<RectTransform>().anchoredPosition;
        if (fuelFrame != null && fuelButtons.Length > 0)
            fuelFrame.anchoredPosition = fuelButtons[fuelIndex].GetComponent<RectTransform>().anchoredPosition;
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
        // One weapon id per hardpoint (null = empty slot).
        var loadout = new List<string>();
        for (int h = 0; h < hardpointOptionIds.Length; h++)
            loadout.Add(hardpointOptionIds[h][hardpointSelection[h]]);

        // Side-scroller: carry the loadout + wind choices into the campaign scene.
        if (modeIndex == 1)
        {
            GameConfig.loadoutWeaponIds = loadout;
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
            t.SetLoadout(loadout);
        }
        turnManager.windMultiplier = windMults[windIndex];
        if (panel != null) panel.SetActive(false);
        turnManager.BeginGame();
    }
}
