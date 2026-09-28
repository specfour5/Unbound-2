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

        // Physics layers: the hull collider ignores terrain (the suspension
        // probes it instead); projectiles and other vehicles still collide.
        // Terrain.Awake applies the ignore at runtime via these names.
        VehicleLayer = EnsureLayer("Vehicle");
        TerrainLayer = EnsureLayer("Terrain");

        // --- Terrain ---
        var terrainGO = new GameObject("Terrain");
        if (TerrainLayer >= 0) terrainGO.layer = TerrainLayer;
        var terrain = terrainGO.AddComponent<Terrain>();
        terrain.width = 120f;
        terrain.baseHeight = 7f;
        terrain.amplitude = 4f;
        terrain.seed = 42;
        terrain.blastAbsorption = 20f;
        terrain.breakNoise = 10f;
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
        BuildGameOverCanvas(ui);
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

        // --- Setup menu (pre-game options; starts the game on Start click) ---
        var menu = BuildSetupMenu();
        menu.turnManager = tm;
        menu.terrain = terrain;
        tm.startOnAwake = false;

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/TankGame.unity");
        EditorUtility.DisplayDialog("Tanks", "Scene built and saved as TankGame.unity.\nPress Play!", "Let's go");
    }

    [MenuItem("Tanks/Build Side-Scroller Scene")]
    public static void BuildSideScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        EnsureInputBoth();

        VehicleLayer = EnsureLayer("Vehicle");
        TerrainLayer = EnsureLayer("Terrain");

        const float mapWidth = 480f;
        const float playerStartX = -220f;
        const float endX = 225f;
        const int enemyCount = 8;

        // --- Terrain: long, procedural, rougher and stonier to the right ---
        var terrainGO = new GameObject("Terrain");
        if (TerrainLayer >= 0) terrainGO.layer = TerrainLayer;
        var terrain = terrainGO.AddComponent<Terrain>();
        terrain.width = mapWidth;
        terrain.baseHeight = 7f;
        terrain.amplitude = 4f;
        terrain.seed = 0; // random hills every run
        terrain.rampDifficulty = true;
        terrain.deepStone = true;
        terrain.blastAbsorption = 20f;
        terrain.breakNoise = 10f;
        terrainGO.GetComponent<MeshRenderer>().material =
            new Material(Shader.Find("Sprites/Default"));
        // Flattened pads: player start + each enemy spawn (applied in Generate).
        terrain.FlattenArea(playerStartX, 10f);
        var enemyXs = new List<float>();
        for (int i = 0; i < enemyCount; i++)
        {
            float ex = 40f + i * 55f;
            enemyXs.Add(ex);
            terrain.FlattenArea(ex, 8f);
        }
        terrain.Generate();

        // --- Projectile template (kept inactive under Templates) ---
        var templates = new GameObject("Templates");
        var projTemplate = CreateProjectileTemplate();
        projTemplate.transform.SetParent(templates.transform, false);
        projTemplate.SetActive(false);

        // --- Player ---
        Tank player = CreateTank("PlayerTank", new Color(0.30f, 0.75f, 0.35f), 1, true,
            projTemplate, terrain, playerStartX);
        player.unlimitedFuel = true; // driving IS the game; no stranding mid-run
        player.weapon = WeaponCatalog.BasicCannon;

        // --- Enemies: parked tanks with real-time AI; skill ramps with distance ---
        var enemies = new List<Tank>();
        for (int i = 0; i < enemyCount; i++)
        {
            Tank e = CreateTank("EnemyTank_" + i, new Color(0.85f, 0.32f, 0.30f), -1, false,
                projTemplate, terrain, enemyXs[i]);
            var ai = (EnemyTank)e;
            ai.realTimeAI = true;
            ai.skill = Mathf.Lerp(0.45f, 0.85f, (float)i / Mathf.Max(1, enemyCount - 1));
            ai.aggroRange = 48f;
            e.weapon = WeaponCatalog.BasicCannon;
            enemies.Add(e);
        }

        // --- Camera ---
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 13f;
        camGO.transform.position = new Vector3(playerStartX, 9f, -10f);
        camGO.AddComponent<AudioListener>();
        var follow = camGO.AddComponent<CameraFollow>();
        follow.minX = -mapWidth / 2f + 5f;
        follow.maxX = mapWidth / 2f - 5f;

        // --- HUD ---
        var ui = BuildSideUI();

        // --- Campaign manager ---
        var mgrGO = new GameObject("SideScrollerManager");
        var mgr = mgrGO.AddComponent<SideScrollerManager>();
        mgr.terrain = terrain;
        mgr.player = player;
        mgr.enemies = enemies;
        mgr.ui = ui;
        mgr.startX = playerStartX;
        mgr.endX = endX;

        // The duel menu's MODE row loads the campaign scene by name, so both
        // scenes must be registered for builds.
        EnsureSceneInBuild("Assets/Scenes/TankGame.unity");
        EnsureSceneInBuild("Assets/Scenes/SideScroller.unity");

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/SideScroller.unity");
        EditorUtility.DisplayDialog("Tanks", "Side-scroller scene built and saved.\nPress Play!", "Let's go");
    }

    static void EnsureSceneInBuild(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var s in scenes)
            if (s.path == path) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    /// <summary>Campaign HUD: kills, wind, progress, cooldown bar, end panel.</summary>
    static SideScrollerUI BuildSideUI()
    {
        var canvasGO = new GameObject("SideScrollerUI");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        canvasGO.AddComponent<GraphicRaycaster>();
        var ui = canvasGO.AddComponent<SideScrollerUI>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var kills = MakeLabel("KillsText", canvasGO.transform, 0f, font, 24);
        var kRT = kills.GetComponent<RectTransform>();
        kRT.anchorMin = new Vector2(0f, 1f); kRT.anchorMax = new Vector2(0f, 1f);
        kRT.pivot = new Vector2(0f, 1f);
        kRT.anchoredPosition = new Vector2(16f, -16f);
        kRT.sizeDelta = new Vector2(320f, 36f);
        kills.alignment = TextAnchor.UpperLeft;
        kills.text = "KILLS  0/8";

        var wind = MakeLabel("WindText", canvasGO.transform, 0f, font, 24);
        var wRT = wind.GetComponent<RectTransform>();
        wRT.anchorMin = new Vector2(1f, 1f); wRT.anchorMax = new Vector2(1f, 1f);
        wRT.pivot = new Vector2(1f, 1f);
        wRT.anchoredPosition = new Vector2(-16f, -16f);
        wRT.sizeDelta = new Vector2(320f, 36f);
        wind.alignment = TextAnchor.UpperRight;

        // Progress bar (top-center): how far across the map the player is.
        var prog = new GameObject("ProgressBar");
        prog.transform.SetParent(canvasGO.transform, false);
        var progRT = prog.AddComponent<RectTransform>();
        progRT.anchorMin = new Vector2(0.5f, 1f); progRT.anchorMax = new Vector2(0.5f, 1f);
        progRT.pivot = new Vector2(0.5f, 1f);
        progRT.anchoredPosition = new Vector2(0f, -18f);
        progRT.sizeDelta = new Vector2(440f, 16f);
        var progBg = prog.AddComponent<Image>();
        progBg.sprite = Art.CenteredWhite;
        progBg.color = new Color(0f, 0f, 0f, 0.5f);
        var progFill = BarFill(prog.transform, new Color(0.35f, 0.75f, 0.95f));

        // Cooldown bar (bottom-center).
        var cd = new GameObject("CooldownBar");
        cd.transform.SetParent(canvasGO.transform, false);
        var cdRT = cd.AddComponent<RectTransform>();
        cdRT.anchorMin = new Vector2(0.5f, 0f); cdRT.anchorMax = new Vector2(0.5f, 0f);
        cdRT.pivot = new Vector2(0.5f, 0f);
        cdRT.anchoredPosition = new Vector2(0f, 48f);
        cdRT.sizeDelta = new Vector2(300f, 18f);
        var cdBg = cd.AddComponent<Image>();
        cdBg.sprite = Art.CenteredWhite;
        cdBg.color = new Color(0f, 0f, 0f, 0.5f);
        var cdFill = BarFill(cd.transform, new Color(0.35f, 0.85f, 0.40f));

        var cdLabel = MakeLabel("CooldownLabel", canvasGO.transform, 0f, font, 15);
        var clRT = cdLabel.GetComponent<RectTransform>();
        clRT.anchorMin = new Vector2(0.5f, 0f); clRT.anchorMax = new Vector2(0.5f, 0f);
        clRT.pivot = new Vector2(0.5f, 0f);
        clRT.anchoredPosition = new Vector2(0f, 72f);
        clRT.sizeDelta = new Vector2(300f, 22f);
        cdLabel.alignment = TextAnchor.LowerCenter;

        // End panel (hidden until the run ends).
        var end = new GameObject("EndPanel");
        end.transform.SetParent(canvasGO.transform, false);
        var endRT = end.AddComponent<RectTransform>();
        endRT.anchorMin = Vector2.zero; endRT.anchorMax = Vector2.one;
        endRT.offsetMin = Vector2.zero; endRT.offsetMax = Vector2.zero;
        var endImg = end.AddComponent<Image>();
        endImg.sprite = Art.CenteredWhite;
        endImg.color = new Color(0f, 0f, 0f, 0.72f);
        var endTitle = MakeLabel("EndTitle", end.transform, 0f, font, 64);
        var etRT = endTitle.GetComponent<RectTransform>();
        etRT.anchorMin = new Vector2(0.5f, 0.5f); etRT.anchorMax = new Vector2(0.5f, 0.5f);
        etRT.anchoredPosition = new Vector2(0f, 40f);
        etRT.sizeDelta = new Vector2(900f, 100f);
        endTitle.alignment = TextAnchor.MiddleCenter;
        var endSub = MakeLabel("EndSubtitle", end.transform, 0f, font, 26);
        var esRT = endSub.GetComponent<RectTransform>();
        esRT.anchorMin = new Vector2(0.5f, 0.5f); esRT.anchorMax = new Vector2(0.5f, 0.5f);
        esRT.anchoredPosition = new Vector2(0f, -40f);
        esRT.sizeDelta = new Vector2(900f, 60f);
        endSub.alignment = TextAnchor.MiddleCenter;
        end.SetActive(false);

        ui.killsText = kills;
        ui.windText = wind;
        ui.progressFill = progFill;
        ui.cooldownFill = cdFill;
        ui.cooldownLabel = cdLabel;
        ui.endPanel = end;
        ui.endTitle = endTitle;
        ui.endSubtitle = endSub;
        return ui;
    }

    /// <summary>Full-rect filled Image child, for HUD bars.</summary>
    static Image BarFill(Transform parent, Color color)
    {
        var fill = new GameObject("Fill");
        fill.transform.SetParent(parent, false);
        var rt = fill.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = fill.AddComponent<Image>();
        img.sprite = Art.CenteredWhite;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.color = color;
        return img;
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
    const string HullBodySpritePath = "Assets/Sprites/Generated/HullBody.png";
    const string WheelSpritePath = "Assets/Sprites/Generated/Wheel.png";

    // Draw order for tank parts (higher = nearer the camera). The track band
    // sits behind the wheels; the wheels tuck behind the hull skirts; the
    // turret base sits down inside the hull and is hidden behind it; the
    // barrel emerges from behind the turret. Future weapons can slot in
    // above (drawn ahead of the tank) or below (drawn behind it) these values.
    const int SortTrack = 1;
    const int SortWheel = 2;
    const int SortBarrel = 3;
    const int SortDome = 4;
    const int SortHull = 5;

    static int VehicleLayer = -1;
    static int TerrainLayer = -1;

    /// <summary>
    /// Creates a project layer if missing (user layers 8..31). Returns the index.
    /// </summary>
    static int EnsureLayer(string name)
    {
        int idx = LayerMask.NameToLayer(name);
        if (idx >= 0) return idx;
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return -1;
        var so = new SerializedObject(assets[0]);
        var layers = so.FindProperty("layers");
        for (int i = 8; i < 32; i++)
        {
            var sp = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(sp.stringValue))
            {
                sp.stringValue = name;
                so.ApplyModifiedProperties();
                return i;
            }
        }
        Debug.LogError("Tanks: no free user layer for '" + name + "'.");
        return -1;
    }

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
        if (VehicleLayer >= 0) go.layer = VehicleLayer;

        // Free-spinning hull on suspension: springs hold it up and pitch it
        // with the terrain instead of a frozen, script-tilted body.
        var rb = go.AddComponent<Rigidbody2D>();
        rb.mass = 4.5f;
        rb.angularDamping = 4f;
        rb.centerOfMass = new Vector2(0f, -0.35f); // low CoM: self-righting
        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(2.8f, 1.05f);
        col.offset = new Vector2(0f, 0.1f);

        Tank tank = isPlayer ? (Tank)go.AddComponent<PlayerTank>() : go.AddComponent<EnemyTank>();
        tank.isPlayer = isPlayer;

        // Component health pools: hull, turret, and weapon each track damage
        // separately; the health bar shows the average.
        tank.components = new List<Tank.ComponentSlot>
        {
            new Tank.ComponentSlot { name = "Hull", maxHP = 100f },
            new Tank.ComponentSlot { name = "Turret", maxHP = 100f },
            new Tank.ComponentSlot { name = "Cannon", maxHP = 100f },
        };
        tank.facing = facing;
        tank.hullMass = 4.5f;
        tank.maxSpeed = 5f;
        tank.projectileTemplate = projTemplate;

        // Body visuals root: everything that pitches with the hull.
        var bodyVisuals = new GameObject("BodyVisuals");
        bodyVisuals.transform.SetParent(go.transform, false);
        tank.bodyVisuals = bodyVisuals;

        // ----- Sprite visuals -----
        // The hull body art is cropped WITHOUT tracks: the tracks are a
        // separate band stretched between the suspension wheels below.
        EnsureSprite(HullBodySpritePath);
        EnsureSprite(WheelSpritePath);
        EnsureSprite(DomeSpritePath);
        EnsureSprite(BarrelSpritePath);
        Sprite hullSpr = AssetDatabase.LoadAssetAtPath<Sprite>(HullBodySpritePath);
        Sprite wheelSpr = AssetDatabase.LoadAssetAtPath<Sprite>(WheelSpritePath);
        Sprite domeSpr = AssetDatabase.LoadAssetAtPath<Sprite>(DomeSpritePath);
        Sprite barrelSpr = AssetDatabase.LoadAssetAtPath<Sprite>(BarrelSpritePath);
        if (hullSpr == null || wheelSpr == null || domeSpr == null || barrelSpr == null)
            Debug.LogError("Tanks: missing tank sprites (HullBody/Wheel/Dome/Barrel.png).");

        var hull = new GameObject("HullBody");
        hull.transform.SetParent(bodyVisuals.transform, false);
        const float hullK = 0.16374f; // hull content 1710px @100ppu -> 2.8 world units wide
        // The crop removed 435px from the bottom of the 1280px image, so its
        // center sits 217.5px higher in the art: shift up 0.356 to keep the
        // body where it was, then 0.12 down so the skirts meet the track band.
        hull.transform.localPosition = new Vector3(-facing * hullK * 0.035f, 0.2482f + 0.356f - 0.12f, 0f);
        hull.transform.localScale = new Vector3(hullK * facing, hullK, 1f);
        var hullSR = hull.AddComponent<SpriteRenderer>();
        hullSR.sprite = hullSpr;
        hullSR.color = color;
        hullSR.sortingOrder = SortHull;
        tank.hull = hull.transform;
        tank.hullScale = hullK;
        tank.hullRenderer = hullSR;

        // ----- Suspension: four sprung wheels, independent of the hull -----
        float[] wheelX = { -1.02f, -0.36f, 0.36f, 1.02f };
        SuspensionModule frontWheel = null, rearWheel = null;
        foreach (float wx in wheelX)
        {
            var wgo = new GameObject("Wheel");
            wgo.transform.SetParent(bodyVisuals.transform, false);
            wgo.transform.localPosition = new Vector3(wx, -0.12f, 0f);
            var wheel = wgo.AddComponent<SuspensionWheel>();
            wheel.restLength = 0.32f;
            wheel.minLength = 0.08f;
            wheel.maxLength = 0.55f;
            wheel.stiffness = 220f;
            wheel.damping = 28f;
            wheel.driveForce = 15f;
            wheel.lateralGrip = 50f;
            wheel.rollingResistance = 2f;
            wheel.wheelRadius = 0.26f;
            wheel.probeSlack = 0.45f;
            wheel.rutDepth = 0.2f; // one terrain pixel deep, then compacted[] caps it

            var wvis = new GameObject("Visual");
            wvis.transform.SetParent(wgo.transform, false);
            wvis.transform.localScale = Vector3.one * 0.2167f; // 240px @100ppu -> 0.52 diameter
            var wsr = wvis.AddComponent<SpriteRenderer>();
            wsr.sprite = wheelSpr;
            wsr.sortingOrder = SortWheel;
            wheel.wheelVisual = wvis.transform;

            if (frontWheel == null || wx > frontWheel.transform.localPosition.x) frontWheel = wheel;
            if (rearWheel == null || wx < rearWheel.transform.localPosition.x) rearWheel = wheel;
        }

        // ----- Track band stretched between the end wheels -----
        var trackGO = new GameObject("Track");
        trackGO.transform.SetParent(bodyVisuals.transform, false);
        var track = trackGO.AddComponent<TrackVisual>();
        track.frontModule = frontWheel;
        track.rearModule = rearWheel;
        track.bandHeight = 0.52f; // bottom edge lands on the wheel bottoms at ride height
        track.sortingOrder = SortTrack;
        track.Init(tank);

        var dome = new GameObject("Dome");
        dome.transform.SetParent(bodyVisuals.transform, false);
        const float domeS = 0.08060f; // dome content 1985px wide -> 1.6 world units
        // Dome base sunk into the hull so the turret's bottom edge hides
        // behind the hull body (both shifted down 0.12 with the new hull art).
        dome.transform.localPosition = new Vector3(-0.0089f, 0.5177f, 0f);
        dome.transform.localScale = new Vector3(domeS, domeS, 1f);
        var domeSR = dome.AddComponent<SpriteRenderer>();
        domeSR.sprite = domeSpr;
        domeSR.color = color;
        domeSR.sortingOrder = SortDome;
        tank.turretRenderer = domeSR;

        var pivot = new GameObject("TurretPivot");
        pivot.transform.SetParent(bodyVisuals.transform, false);
        pivot.transform.localPosition = new Vector3(0f, 0.63f, 0f);

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
        bsr.sortingOrder = SortBarrel;
        tank.weaponRenderer = bsr;

        var muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(pivot.transform, false);
        muzzle.transform.localPosition = new Vector3(1.95f, 0f, 0f);

        tank.turretPivot = pivot.transform;
        tank.muzzle = muzzle.transform;

        // Health bar (kept upright and at a fixed height in Tank.Update,
        // even though the hull pitches on its suspension).
        // Red meter showing the average of the component pools, with the
        // average hitpoints as a number inside.
        var hb = new GameObject("HealthBar");
        hb.transform.SetParent(go.transform, false);
        hb.transform.localPosition = new Vector3(0f, 2.2f, 0f);

        var bg = new GameObject("BG");
        bg.transform.SetParent(hb.transform, false);
        bg.transform.localScale = new Vector3(Tank.HealthBarW + 0.1f, Tank.HealthBarH + 0.1f, 1f);
        var bgsr = bg.AddComponent<SpriteRenderer>();
        bgsr.sprite = Art.CenteredWhite;
        bgsr.color = new Color(0f, 0f, 0f, 0.6f);
        bgsr.sortingOrder = 6;

        var fill = new GameObject("Fill");
        fill.transform.SetParent(hb.transform, false);
        fill.transform.localPosition = new Vector3(-Tank.HealthBarW / 2f, 0f, 0f);
        var fsr = fill.AddComponent<SpriteRenderer>();
        fsr.sprite = Art.LeftPivotWhite;
        fsr.color = Color.red;
        fsr.sortingOrder = 7;

        var hpt = new GameObject("HPText");
        hpt.transform.SetParent(hb.transform, false);
        hpt.transform.localPosition = new Vector3(0f, 0f, -0.1f);
        var tm = hpt.AddComponent<TextMesh>();
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.fontSize = 64;
        tm.characterSize = 0.05f;
        tm.fontStyle = FontStyle.Bold;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        tm.text = "100";
        tm.GetComponent<Renderer>().sortingOrder = 8;

        tank.healthFill = fsr;
        tank.healthBG = bgsr;
        tank.healthBarRoot = hb.transform;
        tank.healthText = tm;

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

    static Button MakeButton(string name, Transform parent, string text, Font font,
        int fontSize, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.sprite = Art.CenteredWhite;
        img.color = new Color(0.22f, 0.24f, 0.28f);
        var btn = go.AddComponent<Button>();

        var t = MakeLabel(name + "_Label", go.transform, 0f, font, fontSize);
        var trt = t.GetComponent<RectTransform>();
        trt.sizeDelta = new Vector2(size.x - 8f, size.y - 6f);
        t.text = text;
        return btn;
    }

    // Yellow outline drawn behind the selected option button. It's a solid
    // yellow rect slightly larger than the button; the opaque button covers
    // the middle, leaving a yellow border visible around it.
    static RectTransform MakeSelectionFrame(Transform parent, Vector2 buttonSize)
    {
        var go = new GameObject("SelectionFrame");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(buttonSize.x + 10f, buttonSize.y + 10f);
        var img = go.AddComponent<Image>();
        img.sprite = Art.CenteredWhite;
        img.color = new Color(1f, 0.85f, 0f, 1f);
        go.SetActive(true);
        return rt;
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

        // Power bar (top-right; hidden at runtime now that power is fixed per weapon)
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
        help.text = "A/D or \u2190/\u2192 move     W/S or \u2191/\u2193 aim     SPACE fire";

        return ui;
    }

    // Game-over panel on its own canvas, separate from the HUD canvas.
    // (Large text on the shared canvas caused font-atlas junk glyphs;
    // the setup menu needed the same isolation.)
    static void BuildGameOverCanvas(GameUI ui)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGO = new GameObject("GameOverCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20; // above the setup menu canvas
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);

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

        ui.gameOverPanel = panel;
        ui.gameOverText = goText;
    }

    static SetupMenu BuildSetupMenu()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Menu gets its own canvas, separate from the HUD canvas. This isolates
        // its text batching from the HUD's.
        var canvasGO = new GameObject("SetupMenuCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10; // draw above the HUD
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        canvasGO.AddComponent<GraphicRaycaster>();

        var dim = new GameObject("SetupMenu");
        dim.transform.SetParent(canvasGO.transform, false);
        var dimRT = dim.AddComponent<RectTransform>();
        dimRT.anchorMin = Vector2.zero;
        dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero;
        dimRT.offsetMax = Vector2.zero;
        var dimImg = dim.AddComponent<Image>();
        dimImg.sprite = Art.CenteredWhite;
        dimImg.color = new Color(0f, 0f, 0f, 0.72f);

        var box = new GameObject("Box");
        box.transform.SetParent(dim.transform, false);
        var boxRT = box.AddComponent<RectTransform>();
        boxRT.anchorMin = new Vector2(0.5f, 0.5f);
        boxRT.anchorMax = new Vector2(0.5f, 0.5f);
        boxRT.pivot = new Vector2(0.5f, 0.5f);
        boxRT.sizeDelta = new Vector2(720f, 780f);
        var boxImg = box.AddComponent<Image>();
        boxImg.sprite = Art.CenteredWhite;
        boxImg.color = new Color(0.10f, 0.11f, 0.14f, 0.97f);

        var title = MakeLabel("Title", box.transform, 0f, font, 22);
        var titleRT = title.GetComponent<RectTransform>();
        titleRT.anchoredPosition = new Vector2(0f, 340f);
        titleRT.sizeDelta = new Vector2(600f, 60f);
        title.text = "BATTLE SETUP";

        var modeLabel = MakeLabel("ModeLabel", box.transform, 0f, font, 22);
        modeLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 280f);
        modeLabel.text = "MODE";

        var modeButtons = new Button[SetupMenu.ModeNames.Length];
        for (int i = 0; i < modeButtons.Length; i++)
        {
            float x = (i - (modeButtons.Length - 1) / 2f) * 208f;
            modeButtons[i] = MakeButton("Mode_" + SetupMenu.ModeNames[i], box.transform,
                SetupMenu.ModeNames[i], font, 22, new Vector2(x, 235f), new Vector2(200f, 46f));
        }
        // Yellow selection frame behind the selected mode button ("Duel" = index 0).
        var modeFrame = MakeSelectionFrame(box.transform, new Vector2(200f, 46f));
        modeFrame.SetSiblingIndex(0);
        modeFrame.anchoredPosition = new Vector2((0 - (modeButtons.Length - 1) / 2f) * 208f, 235f);

        var windLabel = MakeLabel("WindLabel", box.transform, 0f, font, 22);
        windLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 175f);
        windLabel.text = "WIND";

        var windButtons = new Button[SetupMenu.WindNames.Length];
        for (int i = 0; i < windButtons.Length; i++)
        {
            float x = (i - (windButtons.Length - 1) / 2f) * 136f;
            windButtons[i] = MakeButton("Wind_" + SetupMenu.WindNames[i], box.transform,
                SetupMenu.WindNames[i], font, 22, new Vector2(x, 130f), new Vector2(128f, 46f));
        }
        // Yellow selection frame behind the selected wind button ("Default" = index 2).
        var windFrame = MakeSelectionFrame(box.transform, new Vector2(128f, 46f));
        windFrame.SetSiblingIndex(0);
        windFrame.anchoredPosition = new Vector2((2 - (windButtons.Length - 1) / 2f) * 136f, 130f);

        var spawnLabel = MakeLabel("SpawnLabel", box.transform, 0f, font, 22);
        spawnLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 70f);
        spawnLabel.text = "SPAWN DISTANCE";

        var spawnButtons = new Button[SetupMenu.SpawnNames.Length];
        for (int i = 0; i < spawnButtons.Length; i++)
        {
            float x = (i - (spawnButtons.Length - 1) / 2f) * 158f;
            spawnButtons[i] = MakeButton("Spawn_" + SetupMenu.SpawnNames[i], box.transform,
                SetupMenu.SpawnNames[i], font, 22, new Vector2(x, 25f), new Vector2(150f, 46f));
        }
        // Yellow selection frame behind the selected spawn button ("Default" = index 1).
        var spawnFrame = MakeSelectionFrame(box.transform, new Vector2(150f, 46f));
        spawnFrame.SetSiblingIndex(0);
        spawnFrame.anchoredPosition = new Vector2((1 - (spawnButtons.Length - 1) / 2f) * 158f, 25f);

        var fuelLabel = MakeLabel("FuelLabel", box.transform, 0f, font, 22);
        fuelLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -35f);
        fuelLabel.text = "FUEL";

        var fuelButtons = new Button[SetupMenu.FuelNames.Length];
        for (int i = 0; i < fuelButtons.Length; i++)
        {
            float x = (i - (fuelButtons.Length - 1) / 2f) * 158f;
            fuelButtons[i] = MakeButton("Fuel_" + SetupMenu.FuelNames[i], box.transform,
                SetupMenu.FuelNames[i], font, 22, new Vector2(x, -80f), new Vector2(150f, 46f));
        }
        // Yellow selection frame behind the selected fuel button ("Low" = index 0).
        var fuelFrame = MakeSelectionFrame(box.transform, new Vector2(150f, 46f));
        fuelFrame.SetSiblingIndex(0);
        fuelFrame.anchoredPosition = new Vector2((0 - (fuelButtons.Length - 1) / 2f) * 158f, -80f);

        var weaponLabel = MakeLabel("WeaponLabel", box.transform, 0f, font, 22);
        weaponLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -140f);
        weaponLabel.text = "WEAPON";

        var defs = WeaponCatalog.All;
        var weaponButtons = new Button[defs.Count];
        for (int i = 0; i < weaponButtons.Length; i++)
        {
            float x = (i - (weaponButtons.Length - 1) / 2f) * 178f;
            weaponButtons[i] = MakeButton("Weapon_" + defs[i].id, box.transform,
                defs[i].displayName, font, 22, new Vector2(x, -185f), new Vector2(170f, 46f));
            weaponButtons[i].interactable = defs[i].available;
        }
        // Yellow selection frame behind the selected weapon button (index 0).
        var weaponFrame = MakeSelectionFrame(box.transform, new Vector2(170f, 46f));
        weaponFrame.SetSiblingIndex(0);
        weaponFrame.anchoredPosition = new Vector2((0 - (weaponButtons.Length - 1) / 2f) * 178f, -185f);

        var start = MakeButton("StartButton", box.transform, "START BATTLE", font, 22,
            new Vector2(0f, -270f), new Vector2(300f, 64f));
        start.GetComponent<Image>().color = new Color(0.25f, 0.62f, 0.32f);

        var menu = dim.AddComponent<SetupMenu>();
        menu.panel = canvasGO;
        menu.modeButtons = modeButtons;
        menu.windButtons = windButtons;
        menu.spawnButtons = spawnButtons;
        menu.fuelButtons = fuelButtons;
        menu.weaponButtons = weaponButtons;
        menu.startButton = start;
        menu.modeFrame = modeFrame;
        menu.windFrame = windFrame;
        menu.spawnFrame = spawnFrame;
        menu.fuelFrame = fuelFrame;
        menu.weaponFrame = weaponFrame;
        return menu;
    }
}
