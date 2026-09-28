using UnityEngine;

/// <summary>
/// Destructible terrain with a smooth silhouette and pixel-style coloring.
/// The ground is simulated on a coarse grid (explosions knock out circles,
/// collision follows column tops), but rendered as smooth strips following a
/// Catmull-Rom surface. Colors are evaluated on virtual 0.15u pixel cells, so
/// it keeps the classic pixel-terrain look with none of the stair-stepped edges.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(EdgeCollider2D))]
public class Terrain : MonoBehaviour
{
    [System.Serializable]
    public struct FlattenSpot
    {
        public float x;
        public float radius;
    }

    [Header("Size")]
    public float width = 120f;
    public float depth = 12f;

    [Header("Shape")]
    public float baseHeight = 7f;
    public float amplitude = 4f;
    [Tooltip("0 = random every play. Any other value = same hills every time.")]
    public int seed = 42;

    [Header("Simulation grid")]
    [Tooltip("World units per sim cell. Smaller = finer crater edges. Rendering is smooth regardless.")]
    public float pixelSize = 0.15f;

    [Header("Spawn flattening")]
    public System.Collections.Generic.List<FlattenSpot> flattenSpots =
        new System.Collections.Generic.List<FlattenSpot>();

    // Pixel palette (sampled from the reference look).
    static readonly Color GrassBase = new Color(0.32f, 0.72f, 0.26f);
    static readonly Color GrassLight = new Color(0.46f, 0.83f, 0.34f);
    static readonly Color GrassDark = new Color(0.22f, 0.55f, 0.20f);
    static readonly Color DirtBase = new Color(0.58f, 0.40f, 0.24f);
    static readonly Color DirtLight = new Color(0.69f, 0.51f, 0.32f);
    static readonly Color DirtDark = new Color(0.43f, 0.28f, 0.16f);

    bool[,] solid;
    bool[,] scorched; // blast-charred pixels: never regrow grass
    bool[,] stone;    // blast-exposed rock: grey instead of grass
    bool[,] muddy;    // track-churned pixels: dark muddy dirt instead of grass
    bool muddyDirty;  // set when new track mud is marked; rebuilt throttled
    float lastMudRebuild = -10f;
    float[] compacted; // per-column wheel compaction this battle (world units)
    int cols, rows;
    float gridY0;
    int[] topRow; // topmost solid row per column (-1 = empty)
    Mesh mesh;
    EdgeCollider2D edge;

    public float LeftX => -width / 2f;
    public float RightX => width / 2f;

    void Awake()
    {
        edge = GetComponent<EdgeCollider2D>();
        var renderer = GetComponent<MeshRenderer>();
        if (renderer.sharedMaterial == null)
            renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        // The vehicle hull collider ignores terrain: suspension modules probe
        // the ground and hold the hull up with springs instead. Projectiles
        // (Default layer) still hit hulls, and hulls still hit each other.
        int vl = LayerMask.NameToLayer("Vehicle");
        int tl = LayerMask.NameToLayer("Terrain");
        if (vl >= 0 && tl >= 0)
            Physics2D.IgnoreLayerCollision(vl, tl, true);
        Generate();
    }

    /// <summary>Builds the hills from layered sine waves, then applies flatten spots.</summary>
    public void Generate()
    {
        int useSeed = seed == 0 ? Random.Range(1, 100000) : seed;
        var rng = new System.Random(useSeed);

        cols = Mathf.CeilToInt(width / pixelSize);
        gridY0 = -12f;
        rows = Mathf.CeilToInt((16f - gridY0) / pixelSize);
        solid = new bool[cols, rows];
        scorched = new bool[cols, rows];
        stone = new bool[cols, rows];
        muddy = new bool[cols, rows];
        muddyDirty = false;
        compacted = new float[cols];
        topRow = new int[cols];

        float p1 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float p2 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float p3 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float f1 = 1f + (float)rng.NextDouble() * 1.5f;
        float f2 = 3f + (float)rng.NextDouble() * 3f;

        float[] heights = new float[cols];
        for (int c = 0; c < cols; c++)
        {
            float x = LeftX + (c + 0.5f) / cols * width;
            float t = (float)c / Mathf.Max(1, cols - 1);
            heights[c] = baseHeight
                + Mathf.Sin(t * Mathf.PI * f1 + p1) * amplitude * 0.6f
                + Mathf.Sin(t * Mathf.PI * f2 + p2) * amplitude * 0.3f
                + Mathf.Sin(t * Mathf.PI * 9f + p3) * amplitude * 0.05f;
        }

        // Gentle smoothing pass: takes the edge off single-column spikes
        // without changing the landscape's character.
        for (int c = 1; c < cols - 1; c++)
            heights[c] = (heights[c - 1] + heights[c] * 2f + heights[c + 1]) * 0.25f;

        for (int c = 0; c < cols; c++)
        {
            float h = Mathf.Max(1.5f, heights[c]);
            SetColumnSurface(c, h);
        }

        RefreshTops(); // flattening reads surface heights, so tops must be current
        foreach (var spot in flattenSpots)
            ApplyFlatten(spot.x, spot.radius);

        float minSurface = float.MaxValue;
        for (int c = 0; c < cols; c++)
            minSurface = Mathf.Min(minSurface, SurfaceY(c));
        Debug.Log($"[Terrain] Generate done: cols={cols} pixelSize={pixelSize} " +
                  $"minSurface={minSurface:F2} flattenSpots={flattenSpots.Count}");

        Rebuild();
    }

    /// <summary>Recomputes the topmost solid row per column.</summary>
    void RefreshTops()
    {
        for (int c = 0; c < cols; c++)
        {
            topRow[c] = -1;
            for (int r = rows - 1; r >= 0; r--)
                if (solid[c, r]) { topRow[c] = r; break; }
        }
    }

    /// <summary>Fills/clears one column so its surface sits at height h.</summary>
    void SetColumnSurface(int c, float h)
    {
        for (int r = 0; r < rows; r++)
        {
            float cb = gridY0 + r * pixelSize;
            float ct = cb + pixelSize;
            solid[c, r] = cb < h && ct > h - depth;
        }
    }

    /// <summary>Registers a flattened spawn area (survives regeneration) and applies it now.</summary>
    public void FlattenArea(float x, float radius)
    {
        flattenSpots.Add(new FlattenSpot { x = x, radius = radius });
        if (solid != null)
        {
            ApplyFlatten(x, radius);
            Rebuild();
        }
    }

    void ApplyFlatten(float x, float radius)
    {
        float h = GetHeightAt(x);
        int cc = ColumnAt(x);
        int cr = Mathf.Max(1, Mathf.CeilToInt(radius / pixelSize));
        for (int c = Mathf.Max(0, cc - cr); c <= Mathf.Min(cols - 1, cc + cr); c++)
        {
            float blend = 1f - Mathf.Abs(c - cc) / (float)(cr + 1);
            float colX = LeftX + (c + 0.5f) / cols * width;
            float target = Mathf.Lerp(SurfaceY(c), h, blend * blend);
            SetColumnSurface(c, target);
        }
    }

    int ColumnAt(float x)
    {
        return Mathf.Clamp(Mathf.FloorToInt((x - LeftX) / pixelSize), 0, cols - 1);
    }

    float SurfaceY(int c)
    {
        return topRow[c] < 0 ? gridY0 : gridY0 + (topRow[c] + 1) * pixelSize;
    }

    public float GetHeightAt(float x)
    {
        if (solid == null) return baseHeight;
        return SurfaceY(ColumnAt(x));
    }

    /// <summary>
    /// Smooth (C1) terrain height: Catmull-Rom interpolation through the column
    /// surfaces, clamped against overshoot like the renderer. The suspension
    /// probes sample this instead of the quantized GetHeightAt: a stepped
    /// signal kicked the springs every column and made the damper see huge
    /// phantom compression velocities (the persistent hull bounce).
    /// </summary>
    public float SampleSmoothHeight(float x)
    {
        if (solid == null) return baseHeight;
        float fx = (x - LeftX) / pixelSize - 0.5f; // column centers are the knots
        int c = Mathf.FloorToInt(fx);
        float t = Mathf.Clamp01(fx - c);
        float h0 = SurfaceY(Mathf.Clamp(c - 1, 0, cols - 1));
        float h1 = SurfaceY(Mathf.Clamp(c, 0, cols - 1));
        float h2 = SurfaceY(Mathf.Clamp(c + 1, 0, cols - 1));
        float h3 = SurfaceY(Mathf.Clamp(c + 2, 0, cols - 1));
        float t2 = t * t, t3 = t2 * t;
        float h = 0.5f * ((2f * h1) + (-h0 + h2) * t
            + (2f * h0 - 5f * h1 + 4f * h2 - h3) * t2
            + (-h0 + 3f * h1 - 3f * h2 + h3) * t3);
        return Mathf.Clamp(h, Mathf.Min(h1, h2), Mathf.Max(h1, h2));
    }

    /// <summary>
    /// True when the given world point is inside solid terrain. Used by the
    /// vehicle hull bumper probes so the body never interpenetrates crater
    /// walls (the hull collider no longer touches terrain; wheels probe it).
    /// </summary>
    public bool IsSolidAt(float x, float y)
    {
        if (solid == null) return false;
        int r = Mathf.FloorToInt((y - gridY0) / pixelSize);
        if (r < 0 || r >= rows) return false;
        return solid[ColumnAt(x), r];
    }

    /// <summary>
    /// Churns the top grass pixels under rolling tracks into dark muddy dirt.
    /// Called by tanks as they drive; the visual refresh is throttled so it
    /// never hitches movement.
    /// </summary>
    public void MarkTrackMud(float x0, float x1)
    {
        if (solid == null) return;
        int c0 = Mathf.Clamp(ColumnAt(x0), 0, cols - 1);
        int c1 = Mathf.Clamp(ColumnAt(x1), 0, cols - 1);
        bool any = false;
        for (int c = c0; c <= c1; c++)
        {
            int t = topRow[c];
            if (t >= 0 && !muddy[c, t] && !scorched[c, t]) { muddy[c, t] = true; any = true; }
        }
        if (any) muddyDirty = true;
    }

    void LateUpdate()
    {
        // Fold newly churned track mud into the mesh a few times a second.
        if (muddyDirty && Time.time - lastMudRebuild > 0.2f)
        {
            muddyDirty = false;
            lastMudRebuild = Time.time;
            Rebuild();
        }
    }

    /// <summary>
    /// Wheels: smoothly depresses the terrain as a vehicle rolls over it.
    /// Each column is compacted toward a cosine-falloff rut (deepest at the
    /// wheel center, feathering out to the edges), at most one pixel per call
    /// and never deeper than depth total — so rolling back and forth can't
    /// drill to bedrock. Call every physics frame while a wheel is grounded.
    /// Newly exposed pixels are churned to mud. The mesh refresh is throttled.
    /// </summary>
    public void DepressSmooth(float x, float halfWidth, float depth)
    {
        if (solid == null || halfWidth <= 0f || depth <= 0f) return;
        bool any = false;
        int c0 = Mathf.Max(0, ColumnAt(x - halfWidth));
        int c1 = Mathf.Min(cols - 1, ColumnAt(x + halfWidth));
        for (int c = c0; c <= c1; c++)
        {
            float cx = LeftX + (c + 0.5f) * pixelSize;
            float d = Mathf.Abs(cx - x) / halfWidth;
            if (d > 1f) continue;
            float want = depth * (0.5f + 0.5f * Mathf.Cos(d * Mathf.PI));
            if (compacted[c] + pixelSize > want + 1e-4f) continue; // already at rut depth
            int t = topRow[c];
            if (t < 0) continue;
            solid[c, t] = false;
            topRow[c] = t - 1;
            compacted[c] += pixelSize;
            if (t - 1 >= 0) muddy[c, t - 1] = true;
            any = true;
        }
        if (any) muddyDirty = true;
    }

    /// <summary>
    /// Legs/feet: stamps a chunky footprint where a foot lands. Knocks out the
    /// top 1-2 pixels in a small radius (deeper at the center), pixel-aligned
    /// like a mini crater but without scorch — the disturbed earth reads as mud.
    /// Call once per footfall.
    /// </summary>
    public void StampFootprint(float x, float radius)
    {
        if (solid == null || radius <= 0f) return;
        bool any = false;
        int c0 = Mathf.Max(0, ColumnAt(x - radius));
        int c1 = Mathf.Min(cols - 1, ColumnAt(x + radius));
        for (int c = c0; c <= c1; c++)
        {
            int t = topRow[c];
            if (t < 0) continue;
            float cx = LeftX + (c + 0.5f) * pixelSize;
            float d = Mathf.Abs(cx - x) / radius;
            if (d > 1f) continue;
            int dig = d < 0.5f ? 2 : 1;
            int removed = 0;
            for (int k = 0; k < dig && t - k >= 0; k++)
            {
                solid[c, t - k] = false;
                removed++;
            }
            topRow[c] = t - removed;
            if (t - removed >= 0) muddy[c, t - removed] = true;
            any = true;
        }
        if (any) muddyDirty = true;
    }

    /// <summary>
    /// How resistant the terrain pixel at a world point is to penetration and
    /// blasts. Stone 4, scorched 2.5, dirt/mud 2, grass 1, empty 0.
    /// </summary>
    public float GetHardnessAt(float x, float y)
    {
        if (solid == null) return 0f;
        int c = ColumnAt(x);
        int r = Mathf.FloorToInt((y - gridY0) / pixelSize);
        if (r < 0 || r >= rows || !solid[c, r]) return 0f;
        if (stone[c, r]) return 4f;
        if (scorched[c, r]) return 2.5f;
        if (muddy[c, r]) return 2f;
        int top = topRow[c];
        if (top >= 0 && r > top - GrassDepth(c)) return 1f; // grass skin
        return 2f; // dirt
    }

    /// <summary>
    /// Knocks out pixels in a circle centered on world position. Harder pixels
    /// resist: a pixel breaks when explosiveForce * falloff exceeds its
    /// hardness (x HardnessTune), so force breaks harder terrain and the
    /// crater shrinks in stone instead of ignoring it.
    /// </summary>
    public void CarveCrater(Vector2 center, float radius, float explosiveForce)
    {
        if (solid == null || radius <= 0f) return;
        const float HardnessTune = 8f;
        int[] oldTop = (int[])topRow.Clone();
        bool[] colHit = new bool[cols];
        int c0 = Mathf.Max(0, ColumnAt(center.x - radius));
        int c1 = Mathf.Min(cols - 1, ColumnAt(center.x + radius));
        int r0 = Mathf.Max(0, Mathf.FloorToInt((center.y - radius - gridY0) / pixelSize));
        int r1 = Mathf.Min(rows - 1, Mathf.CeilToInt((center.y + radius - gridY0) / pixelSize));
        float r2 = radius * radius;
        for (int c = c0; c <= c1; c++)
            for (int r = r0; r <= r1; r++)
            {
                float px = LeftX + (c + 0.5f) * pixelSize;
                float py = gridY0 + (r + 0.5f) * pixelSize;
                float dx = px - center.x, dy = py - center.y;
                float d2 = dx * dx + dy * dy;
                if (d2 <= r2 && solid[c, r])
                {
                    float falloff = 1f - Mathf.Sqrt(d2) / radius;
                    if (explosiveForce * falloff > GetHardnessAt(px, py) * HardnessTune)
                    {
                        solid[c, r] = false;
                        colHit[c] = true;
                    }
                }
            }
        // Scorch the freshly exposed surface instead of regrowing grass.
        for (int c = c0; c <= c1; c++)
        {
            int nt = -1;
            for (int r = rows - 1; r >= 0; r--)
                if (solid[c, r]) { nt = r; break; }
            if (nt >= 0 && nt < oldTop[c])
            {
                scorched[c, nt] = true;
                if (nt - 1 >= 0) scorched[c, nt - 1] = true;
            }
        }
        // Any grass left clinging to the blast zone becomes exposed stone.
        for (int c = c0; c <= c1; c++)
        {
            if (!colHit[c]) continue;
            int top = -1;
            for (int r = rows - 1; r >= 0; r--)
                if (solid[c, r]) { top = r; break; }
            if (top < 0) continue;
            int gd = GrassDepth(c);
            for (int k = 0; k < 3 && top - k >= 0; k++)
            {
                int r = top - k;
                if (!solid[c, r] || scorched[c, r] || stone[c, r]) continue;
                if (k < gd) stone[c, r] = true;
            }
        }
        // Shave 1-wide needles: the hardness test can leave a single hard column
        // (usually a stone-capped rim) towering over a fresh crater while the
        // dirt around it is blasted away. Anything towering 2+ px over both
        // neighbors is an artifact: cut it level and scorch the fresh top.
        {
            int cc0 = Mathf.Max(0, c0 - 1), cc1 = Mathf.Min(cols - 1, c1 + 1);
            int span = cc1 - cc0 + 1;
            int[] tops = new int[span];
            for (int c = cc0; c <= cc1; c++)
            {
                int t = -1;
                for (int r = rows - 1; r >= 0; r--)
                    if (solid[c, r]) { t = r; break; }
                tops[c - cc0] = t;
            }
            for (int c = c0; c <= c1; c++)
            {
                int i = c - cc0;
                int cap = Mathf.Max(tops[Mathf.Max(0, i - 1)], tops[Mathf.Min(span - 1, i + 1)]);
                if (tops[i] - cap >= 2)
                {
                    for (int r = tops[i]; r > cap; r--) solid[c, r] = false;
                    if (cap >= 0) scorched[c, cap] = true;
                }
            }
        }
        Rebuild();
    }
    static float Hash01(int a, int b)
    {
        int h = (a * 73856093) ^ (b * 19349663);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }

    /// <summary>How many pixels deep the grass runs in a column (jagged edge).</summary>
    static int GrassDepth(int c) => 2 + (int)(Hash01(c, 777) * 2.999f);

    /// <summary>
    /// The classic pixel-terrain palette, evaluated on virtual 0.15u cells over
    /// the smooth surface: crisp pixel blocks, smooth silhouette.
    /// </summary>
    Color PixelStyleColor(float x, float y, float surfY, bool scorched, bool stone, float shade)
    {
        const float VP = 0.15f;
        int px = Mathf.FloorToInt(x / VP);
        int py = Mathf.FloorToInt(y / VP);
        float depthPx = (surfY - y) / VP;
        float h = Hash01(px * 3 + 1, py * 7 + 2);
        int mc = ColumnAt(x);
        int mt = topRow[mc];
        bool mud = mt >= 0 && muddy[mc, mt] && depthPx < 1.5f;
        Color c;
        if (scorched && depthPx < 3f)
            c = ScorchedColor(px, py);
        else if (stone && depthPx < 3f)
            c = StoneColor(px, py);
        else if (mud)
            c = new Color(0.34f, 0.25f, 0.15f) * (0.9f + 0.2f * h); // churned track mud
        else if (depthPx < 0.5f && !scorched && !stone)
            c = new Color(0.10f, 0.13f, 0.10f); // dark surface line, like the old outline
        else if (depthPx < 2f + Hash01(px, 777) * 2.999f)
        {
            if (h < 0.15f) c = GrassLight;
            else if (h > 0.85f) c = GrassDark;
            else c = GrassBase * (0.95f + 0.10f * h);
        }
        else
        {
            Color d;
            if (h < 0.13f) d = DirtDark;
            else if (h < 0.26f) d = DirtLight;
            else d = DirtBase * (0.94f + 0.12f * h);
            // Strong darkening with depth for a rich underground feel.
            c = d * (1f - 0.5f * Mathf.Min(1f, depthPx / 22f));
        }
        return c * shade;
    }

    /// <summary>Charred blast-crater pixels: dark, mottled, never grass.</summary>
    static Color ScorchedColor(int c, int r)
    {
        float h1 = Hash01(c * 5 + 3, r * 11 + 7);
        if (h1 < 0.25f) return new Color(0.13f, 0.10f, 0.08f);
        if (h1 < 0.45f) return new Color(0.30f, 0.21f, 0.14f);
        return new Color(0.22f, 0.16f, 0.12f) * (0.92f + 0.16f * h1);
    }

    /// <summary>Blast-exposed rock: neutral grey stone instead of grass.</summary>
    static Color StoneColor(int c, int r)
    {
        float h1 = Hash01(c * 9 + 5, r * 13 + 11);
        if (h1 < 0.20f) return new Color(0.40f, 0.40f, 0.43f);
        if (h1 < 0.40f) return new Color(0.62f, 0.62f, 0.65f);
        return new Color(0.51f, 0.51f, 0.54f) * (0.92f + 0.16f * h1);
    }

    void Rebuild()
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "TerrainMesh" };
            // Keep 32-bit indices so the strip meshes never hit the 65k vertex cap.
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            GetComponent<MeshFilter>().mesh = mesh;
        }
        if (edge == null) edge = GetComponent<EdgeCollider2D>();

        RefreshTops();

        // ---- smooth surface line: Catmull-Rom through the column tops ----
        const int SUB = 4; // surface subdivisions per sim column
        int n = cols * SUB + 1;
        float dx = width / (n - 1);
        float[] colTop = new float[cols];
        for (int c = 0; c < cols; c++) colTop[c] = SurfaceY(c);

        float[] surfY = new float[n];
        bool[] surfScorched = new bool[n];
        bool[] surfStone = new bool[n];
        for (int i = 0; i < n; i++)
        {
            float x = LeftX + i * dx;
            float fc = Mathf.Clamp((x - LeftX) / pixelSize - 0.5f, 0f, cols - 1.001f);
            int c0 = Mathf.Min(Mathf.FloorToInt(fc), cols - 2);
            float fr = fc - Mathf.Floor(fc);
            float p0 = colTop[Mathf.Max(0, c0 - 1)];
            float p1 = colTop[c0];
            float p2 = colTop[c0 + 1];
            float p3 = colTop[Mathf.Min(cols - 1, c0 + 2)];
            float fr2 = fr * fr, fr3 = fr2 * fr;
            float y = 0.5f * (2f * p1 + (-p0 + p2) * fr
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * fr2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * fr3);
            // Never overshoot the neighboring column tops: no ringing on crater walls.
            surfY[i] = Mathf.Clamp(y, Mathf.Min(p1, p2), Mathf.Max(p1, p2));

            int c = Mathf.Clamp(Mathf.RoundToInt((x - LeftX) / pixelSize - 0.5f), 0, cols - 1);
            int top = topRow[c];
            bool sc = top >= 0 && scorched[c, top];
            surfScorched[i] = sc;
            surfStone[i] = !sc && top >= 0 &&
                (stone[c, top] || (top - 1 >= 0 && stone[c, top - 1]));
        }

        // ---- slope shading: steep faces catch less light, gives the hills form ----
        float[] shade = new float[n];
        for (int i = 0; i < n; i++)
        {
            int a = Mathf.Max(0, i - 1), b2 = Mathf.Min(n - 1, i + 1);
            float slope = Mathf.Abs(surfY[b2] - surfY[a]) / Mathf.Max(1e-4f, (b2 - a) * dx);
            shade[i] = 1f - 0.18f * Mathf.Min(1f, slope * 0.8f);
        }

        // ---- smooth strips with pixel-style coloring ----
        // Row offsets below the surface line. Near-surface rows sit one virtual
        // pixel (0.15u) apart so the coloring renders as crisp pixel blocks;
        // deeper rows are sparse (dark dirt needs little detail).
        float[] rowOff = { 0f, -0.15f, -0.30f, -0.45f, -0.60f, -0.75f, -0.90f,
                           -1.20f, -1.50f, -1.80f, -2.40f, -3.00f,
                           -4.00f, -5.50f, -7.50f, -10.00f, -12.50f };
        int nr = rowOff.Length;

        var verts = new Vector3[(nr - 1) * n * 2];
        var colors = new Color[(nr - 1) * n * 2];
        var tris = new int[(nr - 1) * (n - 1) * 6];

        for (int s = 0; s < nr - 1; s++)
        {
            int vb = s * n * 2;
            for (int i = 0; i < n; i++)
            {
                float x = LeftX + i * dx;
                float sy = surfY[i];
                float yt = Mathf.Max(sy + rowOff[s], gridY0);
                float yb = Mathf.Max(sy + rowOff[s + 1], gridY0);
                verts[vb + i * 2] = new Vector3(x, yt, 0);
                verts[vb + i * 2 + 1] = new Vector3(x, yb, 0);
                bool sc = surfScorched[i], st = surfStone[i];
                colors[vb + i * 2] = PixelStyleColor(x, yt, sy, sc, st, shade[i]);
                colors[vb + i * 2 + 1] = PixelStyleColor(x, yb, sy, sc, st, shade[i]);
            }
            int tb = s * (n - 1) * 6;
            for (int i = 0; i < n - 1; i++)
            {
                int v0 = vb + i * 2;     // top_i
                int v1 = v0 + 2;         // top_{i+1}
                int v2 = v0 + 1;         // bottom_i
                int v3 = v0 + 3;         // bottom_{i+1}
                int t = tb + i * 6;
                tris[t] = v0; tris[t + 1] = v2; tris[t + 2] = v1;
                tris[t + 3] = v1; tris[t + 4] = v2; tris[t + 5] = v3;
            }
        }

        mesh.Clear();
        mesh.vertices = verts;
        mesh.colors = colors;
        mesh.triangles = tris;
        mesh.RecalculateBounds();

        var pts = new Vector2[cols];
        for (int c = 0; c < cols; c++)
            pts[c] = new Vector2(LeftX + c * pixelSize, SurfaceY(c));
        edge.points = pts;
    }
}
