using UnityEngine;

/// <summary>
/// Destructible pixel-art terrain (Terraria-style).
/// The ground is a grid of chunky square pixels: dark-outlined grass on top
/// with a jagged edge, speckled dirt below. Explosions knock out pixels in a
/// circle, leaving blocky craters. Collision follows the pixel tops.
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

    [Header("Pixel look")]
    [Tooltip("World units per pixel. Smaller = finer grain.")]
    public float pixelSize = 0.35f;

    [Header("Spawn flattening")]
    public System.Collections.Generic.List<FlattenSpot> flattenSpots =
        new System.Collections.Generic.List<FlattenSpot>();

    // Pixel palette (sampled from the reference look).
    static readonly Color Outline = new Color(0.10f, 0.13f, 0.10f);
    static readonly Color GrassBase = new Color(0.32f, 0.72f, 0.26f);
    static readonly Color GrassLight = new Color(0.46f, 0.83f, 0.34f);
    static readonly Color GrassDark = new Color(0.22f, 0.55f, 0.20f);
    static readonly Color DirtBase = new Color(0.58f, 0.40f, 0.24f);
    static readonly Color DirtLight = new Color(0.69f, 0.51f, 0.32f);
    static readonly Color DirtDark = new Color(0.43f, 0.28f, 0.16f);

    bool[,] solid;
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
        topRow = new int[cols];

        float p1 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float p2 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float p3 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float f1 = 1f + (float)rng.NextDouble() * 1.5f;
        float f2 = 3f + (float)rng.NextDouble() * 3f;

        for (int c = 0; c < cols; c++)
        {
            float x = LeftX + (c + 0.5f) / cols * width;
            float t = (float)c / Mathf.Max(1, cols - 1);
            float h = baseHeight
                + Mathf.Sin(t * Mathf.PI * f1 + p1) * amplitude * 0.6f
                + Mathf.Sin(t * Mathf.PI * f2 + p2) * amplitude * 0.3f
                + Mathf.Sin(t * Mathf.PI * 9f + p3) * amplitude * 0.1f;
            h = Mathf.Max(1.5f, h);
            SetColumnSurface(c, h);
        }

        foreach (var spot in flattenSpots)
            ApplyFlatten(spot.x, spot.radius);

        Rebuild();
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
                if (dx * dx + dy * dy <= r2)
                    solid[c, r] = false;
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
    static int GrassDepth(int c) => 1 + (int)(Hash01(c, 777) * 2.999f);

    Color PixelColor(int c, int r, int depthPx)
    {
        float h1 = Hash01(c * 3 + 1, r * 7 + 2);
        if (depthPx < GrassDepth(c))
        {
            if (h1 < 0.15f) return GrassLight;
            if (h1 > 0.85f) return GrassDark;
            return GrassBase * (0.95f + 0.10f * h1);
        }
        Color d;
        if (h1 < 0.13f) d = DirtDark;
        else if (h1 < 0.26f) d = DirtLight;
        else d = DirtBase * (0.94f + 0.12f * h1);
        // Slight darkening with depth for richness.
        return d * (1f - 0.12f * Mathf.Min(1f, depthPx / 18f));
    }

    void Rebuild()
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "TerrainMesh" };
            GetComponent<MeshFilter>().mesh = mesh;
        }
        if (edge == null) edge = GetComponent<EdgeCollider2D>();

        // Refresh topmost-solid row per column.
        for (int c = 0; c < cols; c++)
        {
            topRow[c] = -1;
            for (int r = rows - 1; r >= 0; r--)
                if (solid[c, r]) { topRow[c] = r; break; }
        }

        int count = 0;
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows; r++)
                if (solid[c, r]) count++;

        var verts = new Vector3[count * 4];
        var colors = new Color[count * 4];
        var uvs = new Vector2[count * 4];
        var tris = new int[count * 6];

        int q = 0;
        for (int c = 0; c < cols; c++)
        {
            float x0 = LeftX + c * pixelSize;
            float x1 = x0 + pixelSize;
            for (int r = 0; r < rows; r++)
            {
                if (!solid[c, r]) continue;
                float y0 = gridY0 + r * pixelSize;
                float y1 = y0 + pixelSize;
                int depthPx = topRow[c] - r;
                bool isSurface = depthPx == 0;
                Color body = PixelColor(c, r, depthPx);

                int v = q * 4;
                verts[v] = new Vector3(x0, y1, 0);
                verts[v + 1] = new Vector3(x1, y1, 0);
                verts[v + 2] = new Vector3(x0, y0, 0);
                verts[v + 3] = new Vector3(x1, y0, 0);
                // Dark outline along the very top, like the reference.
                colors[v] = isSurface ? Outline : body;
                colors[v + 1] = isSurface ? Outline : body;
                colors[v + 2] = body;
                colors[v + 3] = body;
                uvs[v] = new Vector2(0, 1);
                uvs[v + 1] = new Vector2(1, 1);
                uvs[v + 2] = new Vector2(0, 0);
                uvs[v + 3] = new Vector2(1, 0);

                int t = q * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                q++;
            }
        }

        mesh.Clear();
        mesh.vertices = verts;
        mesh.colors = colors;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var pts = new Vector2[cols];
        for (int c = 0; c < cols; c++)
            pts[c] = new Vector2(LeftX + c * pixelSize, SurfaceY(c));
        edge.points = pts;
    }
}
