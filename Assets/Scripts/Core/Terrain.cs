using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Destructible 2D terrain built from a heightmap.
/// Rendered as a vertex-colored mesh, collided with an EdgeCollider2D.
/// Explosions carve circular craters out of it.
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

    [System.Serializable]
    public struct StrataLayer
    {
        public string name;
        [Tooltip("Thickness in world units. Ignored for the last layer (fills to Depth).")]
        public float thickness;
        public Color color;

        public StrataLayer(string name, float thickness, Color color)
        {
            this.name = name;
            this.thickness = thickness;
            this.color = color;
        }
    }

    [Header("Size")]
    public float width = 120f;
    public float depth = 12f;
    public int samples = 240;

    [Header("Shape")]
    public float baseHeight = 7f;
    public float amplitude = 4f;
    [Tooltip("0 = random every play. Any other value = same hills every time.")]
    public int seed = 42;

    [Header("Spawn flattening")]
    public List<FlattenSpot> flattenSpots = new List<FlattenSpot>();

    [Header("Appearance (Terraria-style strata)")]
    [Tooltip("Bands from the surface down. The last one fills to Depth.")]
    public StrataLayer[] layers = new StrataLayer[]
    {
        new StrataLayer("Grass",      0.6f, new Color(0.40f, 0.74f, 0.27f)),
        new StrataLayer("GrassRoots", 0.7f, new Color(0.36f, 0.58f, 0.25f)),
        new StrataLayer("LightDirt",  2.0f, new Color(0.62f, 0.45f, 0.27f)),
        new StrataLayer("Dirt",       3.0f, new Color(0.50f, 0.34f, 0.20f)),
        new StrataLayer("DeepDirt",   0f,   new Color(0.33f, 0.22f, 0.13f)),
    };

    float[] heights;
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
        heights = new float[samples + 1];

        float p1 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float p2 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float p3 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float f1 = 1f + (float)rng.NextDouble() * 1.5f;
        float f2 = 3f + (float)rng.NextDouble() * 3f;

        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            float h = baseHeight
                + Mathf.Sin(t * Mathf.PI * f1 + p1) * amplitude * 0.6f
                + Mathf.Sin(t * Mathf.PI * f2 + p2) * amplitude * 0.3f
                + Mathf.Sin(t * Mathf.PI * 9f + p3) * amplitude * 0.1f;
            heights[i] = Mathf.Max(1.5f, h);
        }

        foreach (var spot in flattenSpots)
            ApplyFlatten(spot.x, spot.radius);

        Rebuild();
    }

    /// <summary>Registers a flattened spawn area (survives regeneration) and applies it now.</summary>
    public void FlattenArea(float x, float radius)
    {
        flattenSpots.Add(new FlattenSpot { x = x, radius = radius });
        if (heights != null)
        {
            ApplyFlatten(x, radius);
            Rebuild();
        }
    }

    void ApplyFlatten(float x, float radius)
    {
        float h = GetHeightAt(x);
        int c = Mathf.RoundToInt((x - LeftX) / width * samples);
        int r = Mathf.RoundToInt(radius / width * samples);
        for (int i = Mathf.Max(0, c - r); i <= Mathf.Min(samples, c + r); i++)
        {
            float blend = 1f - Mathf.Abs(i - c) / (float)(r + 1);
            heights[i] = Mathf.Lerp(heights[i], h, blend * blend);
        }
    }

    public float GetHeightAt(float x)
    {
        if (heights == null) return baseHeight;
        float t = Mathf.Clamp01((x - LeftX) / width) * samples;
        int i = Mathf.FloorToInt(t);
        int j = Mathf.Min(samples, i + 1);
        return Mathf.Lerp(heights[i], heights[j], t - i);
    }

    /// <summary>Carves a circular crater centered on world position.</summary>
    public void CarveCrater(Vector2 center, float radius)
    {
        if (heights == null) return;
        int c = Mathf.RoundToInt((center.x - LeftX) / width * samples);
        int r = Mathf.CeilToInt(radius / width * samples) + 1;
        for (int i = Mathf.Max(0, c - r); i <= Mathf.Min(samples, c + r); i++)
        {
            float x = LeftX + (float)i / samples * width;
            float dx = x - center.x;
            if (Mathf.Abs(dx) > radius) continue;
            float circleY = center.y - Mathf.Sqrt(radius * radius - dx * dx);
            if (heights[i] > circleY)
                heights[i] = Mathf.Max(0.5f, circleY);
        }
        Rebuild();
    }

    /// <summary>
    /// Deterministic wobble for a strata boundary so bands follow the hills
    /// with a hand-made feel instead of perfectly flat lines.
    /// </summary>
    static float BoundaryWobble(float x, int boundary)
    {
        float p = boundary * 1.71f;
        return (Mathf.Sin(x * 0.33f + p) * 0.6f
              + Mathf.Sin(x * 0.83f + p * 2.33f) * 0.3f) * 0.45f;
    }

    /// <summary>Deterministic 0..1 pseudo-random for subtle per-column tint variation.</summary>
    static float Hash01(int a, int b)
    {
        int h = (a * 73856093) ^ (b * 19349663);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }

    void Rebuild()
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "TerrainMesh" };
            GetComponent<MeshFilter>().mesh = mesh;
        }
        if (edge == null) edge = GetComponent<EdgeCollider2D>();

        int L = (layers != null && layers.Length > 0) ? layers.Length : 1;
        mesh.Clear();
        int n = samples + 1;
        int rows = L * 2; // top + bottom vertex row per layer (crisp bands)

        // Depth of each layer boundary below the surface, before wobble.
        float[] bound = new float[L + 1];
        for (int k = 1; k < L; k++)
            bound[k] = bound[k - 1] + Mathf.Max(0.2f, layers[k - 1].thickness);
        bound[L] = depth;

        Vector3[] verts = new Vector3[n * rows];
        Color[] colors = new Color[n * rows];
        Vector2[] uvs = new Vector2[n * rows];
        var tris = new List<int>(samples * L * 6);

        for (int i = 0; i < n; i++)
        {
            float x = LeftX + (float)i / samples * width;
            float h = heights[i];
            for (int k = 0; k < L; k++)
            {
                Color c = (layers != null && layers.Length > 0) ? layers[k].color
                                                               : new Color(0.5f, 0.34f, 0.2f);
                // Subtle per-column brightness variation so bands feel textured.
                c *= 0.94f + 0.12f * Hash01(i, k);

                float topY = h - bound[k] - (k == 0 ? 0f : BoundaryWobble(x, k));
                float botY = (k == L - 1) ? h - depth
                                          : h - bound[k + 1] - BoundaryWobble(x, k + 1);
                botY = Mathf.Min(botY, topY - 0.15f);

                int vt = (i * L + k) * 2;
                verts[vt] = new Vector3(x, topY, 0);
                verts[vt + 1] = new Vector3(x, botY, 0);
                colors[vt] = c;
                colors[vt + 1] = c;
                uvs[vt] = new Vector2((float)i / samples, 1f - (float)k / L);
                uvs[vt + 1] = new Vector2((float)i / samples, 1f - (float)(k + 1) / L);

                if (i < samples)
                {
                    int a0 = vt, a1 = vt + 1;
                    int b0 = vt + L * 2, b1 = vt + L * 2 + 1;
                    tris.Add(a0); tris.Add(a1); tris.Add(b0);
                    tris.Add(a1); tris.Add(b1); tris.Add(b0);
                }
            }
        }

        mesh.vertices = verts;
        mesh.colors = colors;
        mesh.uv = uvs;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Vector2[] pts = new Vector2[n];
        for (int i = 0; i < n; i++)
            pts[i] = new Vector2(LeftX + (float)i / samples * width, heights[i]);
        edge.points = pts;
    }
}
