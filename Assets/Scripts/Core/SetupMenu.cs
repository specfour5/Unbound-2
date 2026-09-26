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
    public Button startButton;

    // Wind options: index 2 ("Default") matches the game's long-standing behavior.
    public static readonly string[] WindNames = { "None", "Light", "Default", "Strong", "Extreme" };
    static readonly float[] windMults = { 0f, 0.5f, 1f, 1.75f, 2.75f };

    // Spawn options: full distance between the two tanks (they spawn symmetric).
    public static readonly string[] SpawnNames = { "Close", "Default", "Far" };
    static readonly float[] spawnDists = { 40f, 76f, 110f };

    int windIndex = 2;
    int spawnIndex = 1;

    void Start()
    {
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
        startButton.onClick.AddListener(StartBattle);
        Refresh();
        if (panel != null) panel.SetActive(true);
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

    void Refresh()
    {
        for (int i = 0; i < windButtons.Length; i++) Tint(windButtons[i], i == windIndex);
        for (int i = 0; i < spawnButtons.Length; i++) Tint(spawnButtons[i], i == spawnIndex);
    }

    static void Tint(Button b, bool selected)
    {
        var img = b.GetComponent<Image>();
        if (img != null)
            img.color = selected ? new Color(0.25f, 0.62f, 0.32f) : new Color(0.22f, 0.24f, 0.28f);
    }

    void StartBattle()
    {
        float dist = spawnDists[spawnIndex];
        foreach (var t in turnManager.tanks)
        {
            float x = t.isPlayer ? -dist / 2f : dist / 2f;
            terrain.FlattenArea(x, 8f);
            t.transform.position = new Vector3(x, terrain.GetHeightAt(x) + 1.5f, 0f);
            t.SetFacing(t.isPlayer ? 1 : -1);
        }
        turnManager.windMultiplier = windMults[windIndex];
        if (panel != null) panel.SetActive(false);
        turnManager.BeginGame();
    }
}
