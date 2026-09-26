using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// One-click scene builder. In Unity: menu bar -> Tanks -> Build Game Scene.
/// Creates the terrain, both tanks, camera, HUD, and turn manager, then saves.
/// </summary>
public static class TankGameSetup
{
    const float PlayerX = -38f;
    const float EnemyX = 38f;

    [MenuItem("Tanks/Build Game Scene")]
    public static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        EnsureInputBoth();

        // --- Terrain ---
        var terrainGO = new GameObject("Terrain");
        var terrain = terrainGO.AddComponent<Terrain>();
        terrain.width = 120f;
        terrain.samples = 240;
        terrain.baseHeight = 7f;
        terrain.amplitude = 4f;
        terrain.seed = 42;
        terrainGO.GetComponent<MeshRenderer>().material =
            new Material(Shader.Find("Sprites/Default"));
        terrain.Generate();
        terrain.FlattenArea(PlayerX, 8f);
        terrain.FlattenArea(EnemyX, 8f);

        // --- Projectile template (kept inactive under Templates) ---
        var templates = new GameObject("Templates");
        var projTemplate = CreateProjectileTemplate();
        projTemplate.transform.SetParent(templates.transform, false);
        projTemplate.SetActive(false);

        // --- Tanks ---
        Tank player = CreateTank("PlayerTank", new Color(0.30f, 0.75f, 0.35f), 1, true, projTemplate, terrain, PlayerX);
        Tank enemy = CreateTank("EnemyTank", new Color(0.85f, 0.32f, 0.30f), -1, false, projTemplate, terrain, EnemyX);

        // --- Camera ---
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 13f;
        camGO.transform.position = new Vector3(0f, 9f, -10f);
        camGO.AddComponent<AudioListener>();
        var follow = camGO.AddComponent<CameraFollow>();
        follow.minX = -45f;
        follow.maxX = 45f;

        // --- UI ---
        var ui = BuildUI();
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();

        // --- Turn manager ---
        var tmGO = new GameObject("TurnManager");
        var tm = tmGO.AddComponent<TurnManager>();
        tm.terrain = terrain;
        tm.ui = ui;
        tm.tanks = new List<Tank> { player, enemy };
        ui.turnManager = tm;

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/TankGame.unity");
        EditorUtility.DisplayDialog("Tanks", "Scene built and saved as TankGame.unity.\nPress Play!", "Let's go");
    }

    // The tank scripts read keyboard via Input.GetKey, which needs the old
    // Input Manager enabled. "Both" keeps the new Input System working too.
    static void EnsureInputBoth()
    {
        try
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop != null && prop.intValue != 2)
            {
                prop.intValue = 2;
                so.ApplyModifiedProperties();
                Debug.Log("Tanks: Active Input Handler set to Both.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Tanks: could not set Active Input Handler automatically: " + e.Message
                + "\nIf keyboard input doesn't work, set Edit > Project Settings > Player > Active Input Handling to Both.");
        }
    }

    const string HullSpritePath = "Assets/Sprites/TankHull.png";
    const string DomeSpritePath = "Assets/Sprites/TurretDome.png";
    const string BarrelSpritePath = "Assets/Sprites/Barrel.png";

    /// <summary>
    /// Makes sure a tank PNG imports as one single Sprite so LoadAssetAtPath finds it.
    /// If the importer ever ends up in Multiple (sprite sheet) mode, Unity looks for a
    /// "Name_0" sub-rect that lies outside the texture and the sprite silently fails
    /// to import, so we force Single mode back here on every build.
    /// </summary>
    static void EnsureSprite(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        bool changed = false;
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }
        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            changed = true;
        }
        if (changed) importer.SaveAndReimport();
    }

    static Tank CreateTank(string name, Color color, int facing, bool isPlayer,
        GameObject projTemplate, Terrain terrain, float x)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(x, terrain.GetHeightAt(x) + 1.5f, 0f);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(2.8f, 1.05f);
        col.offset = new Vector2(0f, 0.1f);

        Tank tank = isPlayer ? (Tank)go.AddComponent<PlayerTank>() : go.AddComponent<EnemyTank>();
        tank.isPlayer = isPlayer;
        tank.facing = facing;
        tank.projectileTemplate = projTemplate;

        // Visual root (tilts to the slope; colliders stay put)
        var visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform, false);
        tank.visual = visual.transform;

        // ----- Sprite visuals (hull has a visible front; dome stays level; barrel pitches) -----
        EnsureSprite(HullSpritePath);
        EnsureSprite(DomeSpritePath);
        EnsureSprite(BarrelSpritePath);
        Sprite hullSpr = AssetDatabase.LoadAssetAtPath<Sprite>(HullSpritePath);
        Sprite domeSpr = AssetDatabase.LoadAssetAtPath<Sprite>(DomeSpritePath);
        Sprite barrelSpr = AssetDatabase.LoadAssetAtPath<Sprite>(BarrelSpritePath);
        if (hullSpr == null || domeSpr == null || barrelSpr == null)
            Debug.LogError("Tanks: missing tank sprites in Assets/Sprites (TankHull/Dome/Barrel.png).");

        var hull = new GameObject("Hull");
        hull.transform.SetParent(visual.transform, false);
        const float hullK = 0.16374f; // hull content 1710px @100ppu -> 2.8 world units wide
        hull.transform.localPosition = new Vector3(-facing * hullK * 0.035f, 0.2482f, 0f);
        hull.transform.localScale = new Vector3(hullK * facing, hullK, 1f);
        var hullSR = hull.AddComponent<SpriteRenderer>();
        hullSR.sprite = hullSpr;
        hullSR.color = color;
        hullSR.sortingOrder = 2;
        tank.hull = hull.transform;
        tank.hullScale = hullK;

        var dome = new GameObject("Dome");
        dome.transform.SetParent(visual.transform, false);
        const float domeS = 0.08060f; // dome content 1985px wide -> 1.6 world units
        dome.transform.localPosition = new Vector3(-0.0089f, 0.8378f, 0f);
        dome.transform.localScale = new Vector3(domeS, domeS, 1f);
        var domeSR = dome.AddComponent<SpriteRenderer>();
        domeSR.sprite = domeSpr;
        domeSR.color = color;
        domeSR.sortingOrder = 4;

        var pivot = new GameObject("TurretPivot");
        pivot.transform.SetParent(visual.transform, false);
        pivot.transform.localPosition = new Vector3(0f, 0.75f, 0f);

        var barrel = new GameObject("Barrel");
        barrel.transform.SetParent(pivot.transform, false);
        const float barrelS = 0.07454f; // barrel content 2616px long -> tip lands at 1.95
        // NOTE: localPosition is in pivot units (not scaled by barrelS): it places
        // the barrel tip exactly on the muzzle at pivot-local (1.95, 0).
        barrel.transform.localPosition = new Vector3(0.9758f, 0.024f, 0f);
        barrel.transform.localScale = new Vector3(barrelS, barrelS, 1f);
        var bsr = barrel.AddComponent<SpriteRenderer>();
        bsr.sprite = barrelSpr;
        bsr.color = new Color(0.2f, 0.2f, 0.22f);
        bsr.sortingOrder = 3;

        var muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(pivot.transform, false);
        muzzle.transform.localPosition = new Vector3(1.95f, 0f, 0f);

        tank.turretPivot = pivot.transform;
        tank.muzzle = muzzle.transform;

        // Health bar (stays upright, not tilted with the body)
        var hb = new GameObject("HealthBar");
        hb.transform.SetParent(go.transform, false);
        hb.transform.localPosition = new Vector3(0f, 2.1f, 0f);

        var bg = new GameObject("BG");
        bg.transform.SetParent(hb.transform, false);
        bg.transform.localScale = new Vector3(Tank.HealthBarW + 0.1f, Tank.HealthBarH + 0.1f, 1f);
        var bgsr = bg.AddComponent<SpriteRenderer>();
        bgsr.sprite = Art.CenteredWhite;
        bgsr.color = new Color(0f, 0f, 0f, 0.6f);
        bgsr.sortingOrder = 5;

        var fill = new GameObject("Fill");
        fill.transform.SetParent(hb.transform, false);
        fill.transform.localPosition = new Vector3(-Tank.HealthBarW / 2f, 0f, 0f);
        var fsr = fill.AddComponent<SpriteRenderer>();
        fsr.sprite = Art.LeftPivotWhite;
        fsr.color = Color.green;
        fsr.sortingOrder = 6;

        tank.healthFill = fsr;
        tank.healthBarRoot = hb.transform;

        return tank;
    }

    static GameObject CreateProjectileTemplate()
    {
        var go = new GameObject("ProjectileTemplate");
        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = Projectile.GravityScale;
        var col = go.AddComponent<CircleCollider2D>();
        col.radius = 0.22f;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Art.CenteredCircle;
        sr.color = new Color(1f, 0.85f, 0.3f);
        sr.sortingOrder = 4;
        go.transform.localScale = Vector3.one * 0.55f;

        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.45f;
        trail.startWidth = 0.3f;
        trail.endWidth = 0.05f;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.startColor = new Color(1f, 0.7f, 0.2f, 0.8f);
        trail.endColor = new Color(1f, 0.3f, 0.1f, 0f);
        trail.sortingOrder = 3;

        go.AddComponent<Projectile>();
        return go;
    }

    static Text MakeLabel(string name, Transform parent, float x, Font font, int size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(220f, 40f);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        return t;
    }

    static GameUI BuildUI()
    {
        var canvasGO = new GameObject("GameUI");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        canvasGO.AddComponent<GraphicRaycaster>();
        var ui = canvasGO.AddComponent<GameUI>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Top bar
        var top = new GameObject("TopBar");
        top.transform.SetParent(canvasGO.transform, false);
        var topRT = top.AddComponent<RectTransform>();
        topRT.anchorMin = new Vector2(0f, 1f);
        topRT.anchorMax = new Vector2(1f, 1f);
        topRT.pivot = new Vector2(0.5f, 1f);
        topRT.anchoredPosition = new Vector2(0f, -8f);
        topRT.sizeDelta = new Vector2(0f, 48f);
        var topImg = top.AddComponent<Image>();
        topImg.sprite = Art.CenteredWhite;
        topImg.color = new Color(0f, 0f, 0f, 0.45f);

        MakeLabel("PowerText", top.transform, -420f, font, 22);
        MakeLabel("AngleText", top.transform, -210f, font, 22);
        MakeLabel("WindText", top.transform, 0f, font, 22);
        MakeLabel("TimerText", top.transform, 420f, font, 22);

        // Fuel bar
        var fuel = new GameObject("FuelBar");
        fuel.transform.SetParent(canvasGO.transform, false);
        var fuelRT = fuel.AddComponent<RectTransform>();
        fuelRT.anchorMin = new Vector2(0f, 1f);
        fuelRT.anchorMax = new Vector2(0f, 1f);
        fuelRT.pivot = new Vector2(0f, 1f);
        fuelRT.anchoredPosition = new Vector2(16f, -66f);
        fuelRT.sizeDelta = new Vector2(260f, 20f);
        var fuelBg = fuel.AddComponent<Image>();
        fuelBg.sprite = Art.CenteredWhite;
        fuelBg.color = new Color(0f, 0f, 0f, 0.5f);

        var fill = new GameObject("Fill");
        fill.transform.SetParent(fuel.transform, false);
        var fillRT = fill.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.sprite = Art.CenteredWhite;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.color = new Color(0.95f, 0.75f, 0.2f);

        var fuelLabel = MakeLabel("FuelLabel", canvasGO.transform, 0f, font, 15);
        var flRT = fuelLabel.GetComponent<RectTransform>();
        flRT.anchorMin = new Vector2(0f, 1f);
        flRT.anchorMax = new Vector2(0f, 1f);
        flRT.pivot = new Vector2(0f, 1f);
        flRT.anchoredPosition = new Vector2(16f, -90f);
        flRT.sizeDelta = new Vector2(260f, 22f);
        fuelLabel.alignment = TextAnchor.UpperLeft;
        fuelLabel.text = "FUEL";

        // Power bar (top-right, fills with a green->red gradient as you charge)
        var pbar = new GameObject("PowerBar");
        pbar.transform.SetParent(canvasGO.transform, false);
        var pbarRT = pbar.AddComponent<RectTransform>();
        pbarRT.anchorMin = new Vector2(1f, 1f);
        pbarRT.anchorMax = new Vector2(1f, 1f);
        pbarRT.pivot = new Vector2(1f, 1f);
        pbarRT.anchoredPosition = new Vector2(-16f, -66f);
        pbarRT.sizeDelta = new Vector2(260f, 20f);
        var pbarBg = pbar.AddComponent<Image>();
        pbarBg.sprite = Art.CenteredWhite;
        pbarBg.color = new Color(0f, 0f, 0f, 0.5f);

        var pfill = new GameObject("Fill");
        pfill.transform.SetParent(pbar.transform, false);
        var pfillRT = pfill.AddComponent<RectTransform>();
        pfillRT.anchorMin = Vector2.zero;
        pfillRT.anchorMax = Vector2.one;
        pfillRT.offsetMin = Vector2.zero;
        pfillRT.offsetMax = Vector2.zero;
        var pfillImg = pfill.AddComponent<Image>();
        pfillImg.sprite = Art.CenteredWhite;
        pfillImg.type = Image.Type.Filled;
        pfillImg.fillMethod = Image.FillMethod.Horizontal;
        pfillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        pfillImg.color = Color.green;

        var powerLabel = MakeLabel("PowerLabel", canvasGO.transform, 0f, font, 15);
        var plRT = powerLabel.GetComponent<RectTransform>();
        plRT.anchorMin = new Vector2(1f, 1f);
        plRT.anchorMax = new Vector2(1f, 1f);
        plRT.pivot = new Vector2(1f, 1f);
        plRT.anchoredPosition = new Vector2(-16f, -90f);
        plRT.sizeDelta = new Vector2(260f, 22f);
        powerLabel.alignment = TextAnchor.UpperRight;
        powerLabel.text = "POWER";

        // Turn banner
        var banner = MakeLabel("BannerText", canvasGO.transform, 0f, font, 64);
        var bRT = banner.GetComponent<RectTransform>();
        bRT.anchorMin = new Vector2(0.5f, 0.62f);
        bRT.anchorMax = new Vector2(0.5f, 0.62f);
        bRT.sizeDelta = new Vector2(800f, 120f);
        banner.alignment = TextAnchor.MiddleCenter;

        // Controls help
        var help = MakeLabel("HelpText", canvasGO.transform, 0f, font, 18);
        var hRT = help.GetComponent<RectTransform>();
        hRT.anchorMin = new Vector2(0.5f, 0f);
        hRT.anchorMax = new Vector2(0.5f, 0f);
        hRT.pivot = new Vector2(0.5f, 0f);
        hRT.anchoredPosition = new Vector2(0f, 14f);
        hRT.sizeDelta = new Vector2(1100f, 30f);
        help.alignment = TextAnchor.MiddleCenter;
        help.text = "A/D or \u2190/\u2192 move     W/S or \u2191/\u2193 aim     HOLD SPACE charge, RELEASE fire";

        // Game over panel
        var panel = new GameObject("GameOverPanel");
        panel.transform.SetParent(canvasGO.transform, false);
        var pRT = panel.AddComponent<RectTransform>();
        pRT.anchorMin = Vector2.zero;
        pRT.anchorMax = Vector2.one;
        pRT.offsetMin = Vector2.zero;
        pRT.offsetMax = Vector2.zero;
        var panelImg = panel.AddComponent<Image>();
        panelImg.sprite = Art.CenteredWhite;
        panelImg.color = new Color(0f, 0f, 0f, 0.75f);

        var goText = MakeLabel("GameOverText", panel.transform, 0f, font, 72);
        var gRT = goText.GetComponent<RectTransform>();
        gRT.anchorMin = new Vector2(0.5f, 0.5f);
        gRT.anchorMax = new Vector2(0.5f, 0.5f);
        gRT.sizeDelta = new Vector2(900f, 300f);
        goText.alignment = TextAnchor.MiddleCenter;
        panel.SetActive(false);

        return ui;
    }
}
