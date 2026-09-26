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

    [Header("Appearance")]
    public Color topColor = new Color(0.32f, 0.68f, 0.30f);
    public Color bottomColor = new Color(0.42f, 0.30f, 0.17f);

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

    void Rebuild()
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "TerrainMesh" };
            GetComponent<MeshFilter>().mesh = mesh;
        }
        if (edge == null) edge = GetComponent<EdgeCollider2D>();

        mesh.Clear();
        int n = samples + 1;

        Vector3[] verts = new Vector3[n * 2];
        Color[] colors = new Color[n * 2];
        Vector2[] uvs = new Vector2[n * 2];
        int[] tris = new int[samples * 6];

        for (int i = 0; i < n; i++)
        {
            float x = LeftX + (float)i / samples * width;
            float h = heights[i];
            verts[i * 2] = new Vector3(x, h, 0);
            verts[i * 2 + 1] = new Vector3(x, h - depth, 0);
            colors[i * 2] = topColor;
            colors[i * 2 + 1] = bottomColor;
            uvs[i * 2] = new Vector2((float)i / samples, 1f);
            uvs[i * 2 + 1] = new Vector2((float)i / samples, 0f);

            if (i < samples)
            {
                int v = i * 2, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v + 1; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
            }
        }

        mesh.vertices = verts;
        mesh.colors = colors;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Vector2[] pts = new Vector2[n];
        for (int i = 0; i < n; i++)
            pts[i] = new Vector2(LeftX + (float)i / samples * width, heights[i]);
        edge.points = pts;
    }
}
