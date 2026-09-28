using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Explosion debris: terrain pixels from the outer band of a blast become
/// short-lived physical chunks. They scatter with a force gradient (faster
/// near the blast, slower at the edge, scaled by the explosion force),
/// bounce off the terrain grid a few times with axis-separated collision,
/// settle, then fade out. Purely visual — no gameplay effect.
/// All chunks render through one dynamic mesh (a rotating quad each).
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DebrisSystem : MonoBehaviour
{
    struct Chunk
    {
        public Vector2 pos;
        public Vector2 vel;
        public float rot;
        public float rotVel;
        public float size;
        public Color color;
        public float age;
        public float maxLife;
        public float still;
        public bool sleeping;
    }

    const int MaxChunks = 600;
    const float Gravity = 22f;
    const float Bounce = 0.38f;
    const float FadeTime = 1.0f;

    TerrainGrid terrain;
    Mesh mesh;
    readonly List<Chunk> chunks = new List<Chunk>(256);
    readonly List<Vector3> verts = new List<Vector3>(MaxChunks * 4);
    readonly List<Color> colors = new List<Color>(MaxChunks * 4);
    readonly List<int> tris = new List<int>(MaxChunks * 6);

    void Awake()
    {
        terrain = GetComponent<TerrainGrid>();
        mesh = new Mesh { name = "DebrisMesh" };
        GetComponent<MeshFilter>().mesh = mesh;
        var renderer = GetComponent<MeshRenderer>();
        renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        renderer.sortingOrder = 10; // debris flies in front of the world
    }

    /// <summary>Launches one terrain chunk. Oldest chunks are dropped past the cap.</summary>
    public void SpawnChunk(Vector2 pos, Vector2 vel, Color color, float size)
    {
        if (chunks.Count >= MaxChunks) chunks.RemoveAt(0);
        color.a = 1f;
        chunks.Add(new Chunk
        {
            pos = pos,
            vel = vel,
            rot = Random.Range(0f, Mathf.PI * 2f),
            rotVel = Random.Range(-9f, 9f),
            size = size,
            color = color,
            age = 0f,
            maxLife = Random.Range(4f, 5.5f),
            still = 0f,
            sleeping = false,
        });
    }

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        for (int i = chunks.Count - 1; i >= 0; i--)
        {
            Chunk ch = chunks[i];
            ch.age += dt;
            if (ch.age >= ch.maxLife) { chunks.RemoveAt(i); continue; }
            if (!ch.sleeping)
            {
                ch.vel.y -= Gravity * dt;
                // Axis-separated collision against the terrain grid: cheap,
                // stable, and good enough for tiny chunks.
                float nx = ch.pos.x + ch.vel.x * dt;
                if (terrain != null && terrain.IsSolidAt(nx, ch.pos.y))
                {
                    ch.vel.x = -ch.vel.x * Bounce;
                    ch.vel.y *= 0.75f;
                    ch.rotVel *= 0.6f;
                }
                else ch.pos.x = nx;

                float ny = ch.pos.y + ch.vel.y * dt;
                if (terrain != null && terrain.IsSolidAt(ch.pos.x, ny))
                {
                    if (ch.vel.y < 0f)
                    {
                        ch.vel.y = -ch.vel.y * Bounce;
                        if (Mathf.Abs(ch.vel.y) < 0.9f) ch.vel.y = 0f;
                        ch.vel.x *= 0.7f;
                        ch.rotVel *= 0.5f;
                    }
                    else ch.vel.y = -ch.vel.y * Bounce;
                }
                else ch.pos.y = ny;

                ch.rot += ch.rotVel * dt;
                if (ch.vel.sqrMagnitude < 0.25f)
                {
                    ch.still += dt;
                    // FUTURE: debris-becomes-terrain goes here — when the
                    // chunk settles, stamp its cells back into the grid via
                    // Terrain instead of fading out.
                    if (ch.still > 0.7f) { ch.sleeping = true; ch.vel = Vector2.zero; }
                }
                else ch.still = 0f;
            }
            chunks[i] = ch;
        }
        RebuildMesh();
    }

    void RebuildMesh()
    {
        verts.Clear();
        colors.Clear();
        tris.Clear();
        foreach (var ch in chunks)
        {
            float alpha = 1f - Mathf.Clamp01((ch.age - (ch.maxLife - FadeTime)) / FadeTime);
            float c = Mathf.Cos(ch.rot), s = Mathf.Sin(ch.rot);
            float h = ch.size * 0.5f;
            Vector2 hx = new Vector2(c, s) * h;
            Vector2 hy = new Vector2(-s, c) * h;
            Vector2 p0 = ch.pos - hx - hy;
            Vector2 p1 = ch.pos + hx - hy;
            Vector2 p2 = ch.pos + hx + hy;
            Vector2 p3 = ch.pos - hx + hy;
            int v = verts.Count;
            verts.Add(new Vector3(p0.x, p0.y, 0f));
            verts.Add(new Vector3(p1.x, p1.y, 0f));
            verts.Add(new Vector3(p2.x, p2.y, 0f));
            verts.Add(new Vector3(p3.x, p3.y, 0f));
            Color col = ch.color;
            col.a = alpha;
            colors.Add(col); colors.Add(col); colors.Add(col); colors.Add(col);
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
            tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);
        }
        mesh.Clear();
        if (verts.Count > 0)
        {
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
