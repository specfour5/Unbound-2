using UnityEngine;

/// <summary>
/// Destructible smooth terrain.
/// The ground is simulated on a coarse grid (explosions knock out circles,
/// collision follows column tops), but rendered as smooth layered strips that
/// follow the surface: turf lip, grass, topsoil, dirt, deep earth.
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

    /// <summary>Knocks out pixels in a circle centered on world position.</summary>
    public void CarveCrater(Vector2 center, float radius)
    {
        if (solid == null) return;
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
                if (dx * dx + dy * dy <= r2 && solid[c, r])
                {
                    solid[c, r] = false;
                    colHit[c] = true;
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
        Rebuild();
    }

    /// <summary>Deterministic 0..1 pseudo-random for per-pixel variation.</summary>
    static float Hash01(int a, int b)
    {
        int h = (a * 73856093) ^ (b * 19349663);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }

    /// <summary>How many pixels deep the grass runs in a column (jagged edge).</summary>
    static int GrassDepth(int c) => 2 + (int)(Hash01(c, 777) * 2.999f);

    /// <summary>
    /// Vertex color for a smooth-terrain band strip (0 = turf lip … 4 = deep
    /// earth). v = 0 at the band's top edge, 1 at its bottom.
    /// </summary>
    Color BandColor(int band, int i, float v, bool scorched, bool stone)
    {
        if (scorched && band <= 2)
        {
            // Blast-charred surface: dark mottled char, never grass.
            return ScorchedColor(i, band) * (v < 0.5f ? 1f : 0.85f);
        }
        if (stone && band <= 1)
        {
            // Blast-exposed rock instead of grass.
            return StoneColor(i, band) * (v < 0.5f ? 1f : 0.85f);
        }
        float h = Hash01(i * 3 + 1, band * 7 + 2);
        float variation = 0.92f + 0.16f * h;
        Color top, bot;
        switch (band)
        {
            case 0: // turf lip: dark crisp surface line
                top = new Color(0.13f, 0.30f, 0.12f);
                bot = new Color(0.20f, 0.44f, 0.17f);
                break;
            case 1: // grass
                top = GrassBase;
                bot = GrassDark;
                break;
            case 2: // topsoil
                top = new Color(0.55f, 0.38f, 0.23f);
                bot = new Color(0.46f, 0.30f, 0.18f);
                break;
            case 3: // dirt
                top = DirtBase;
                bot = DirtDark;
                break;
            default: // deep earth
                top = new Color(0.33f, 0.21f, 0.13f);
                bot = new Color(0.15f, 0.10f, 0.06f);
                break;
        }
        Color c = Color.Lerp(top, bot, v) * variation;
        // Fine speckle keeps the soil organic at high resolution.
        if (band >= 2)
        {
            if (h < 0.12f) c *= 0.78f;
            else if (h > 0.88f) c = Color.Lerp(c, DirtLight, 0.5f);
        }
        else if (band == 1)
        {
            if (h < 0.15f) c = Color.Lerp(c, GrassLight, 0.6f);
            else if (h > 0.85f) c = Color.Lerp(c, GrassDark, 0.6f);
        }
        return c;
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

        // ---- layered strips following the surface (offsets below it) ----
        float[] bandTop = { 0f, -0.10f, -0.50f, -1.60f, -3.40f };
        float[] bandBot = { -0.10f, -0.50f, -1.60f, -3.40f, -100f };
        int nb = bandTop.Length;

        var verts = new Vector3[nb * n * 2];
        var colors = new Color[nb * n * 2];
        var tris = new int[nb * (n - 1) * 6];

        for (int b = 0; b < nb; b++)
        {
            int vb = b * n * 2;
            for (int i = 0; i < n; i++)
            {
                float x = LeftX + i * dx;
                float yt = Mathf.Max(surfY[i] + bandTop[b], gridY0);
                float yb = Mathf.Max(surfY[i] + bandBot[b], gridY0);
                verts[vb + i * 2] = new Vector3(x, yt, 0);
                verts[vb + i * 2 + 1] = new Vector3(x, yb, 0);
                colors[vb + i * 2] = BandColor(b, i, 0f, surfScorched[i], surfStone[i]) * shade[i];
                colors[vb + i * 2 + 1] = BandColor(b, i, 1f, surfScorched[i], surfStone[i]) * shade[i];
            }
            int tb = b * (n - 1) * 6;
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
