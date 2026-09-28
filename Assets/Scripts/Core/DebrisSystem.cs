using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Explosion debris: terrain pixels from the outer band of a blast become
/// short-lived physical chunks, and destroyed stone becomes persistent
/// rubble grouped by connectivity (pebbles to boulders). Chunks scatter
/// with a force gradient, bounce off the terrain grid, settle, then fade.
/// Later blasts shove rubble around — and shatter it, with bigger boulders
/// needing a much closer hit. Purely visual — no gameplay effect.
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
        public float mass;   // stone rubble: pixel count; dirt = 1
        public bool isStone;
    }

    const int MaxChunks = 600;
    const int MaxStoneChunks = 220;
    const float Gravity = 22f;
    const float Bounce = 0.38f;
    const float FadeTime = 1.0f;
    // Rubble shatter: a chunk breaks when blast force exceeds this * sqrt(mass).
    // A lone pixel (mass 1) shatters at force 14 — well within a blast — while
    // a 16-pixel boulder needs force 56, more than a basic cannon's 55.
    const float ShatterBase = 14f;

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
            mass = 1f,
            isStone = false,
        });
    }

    /// <summary>
    /// Launches one stone rubble chunk. Size grows with the square root of
    /// the pixel count; rubble lives much longer than dirt so it piles up.
    /// The stone cap is enforced separately so rubble never crowds out dirt.
    /// </summary>
    public void SpawnStoneChunk(Vector2 pos, Vector2 vel, Color color, float mass)
    {
        int stoneCount = 0;
        foreach (var ch in chunks) if (ch.isStone) stoneCount++;
        if (stoneCount >= MaxStoneChunks)
        {
            for (int i = 0; i < chunks.Count; i++)
                if (chunks[i].isStone) { chunks.RemoveAt(i); break; }
        }
        else if (chunks.Count >= MaxChunks) chunks.RemoveAt(0);
        color.a = 1f;
        float px = terrain != null ? terrain.pixelSize : 0.15f;
        chunks.Add(new Chunk
        {
            pos = pos,
            vel = vel,
            rot = Random.Range(0f, Mathf.PI * 2f),
            rotVel = Random.Range(-7f, 7f),
            size = px * (1.2f + 0.9f * Mathf.Sqrt(mass)),
            color = color,
            age = 0f,
            maxLife = Random.Range(20f, 30f),
            still = 0f,
            sleeping = false,
            mass = mass,
            isStone = true,
        });
    }

    /// <summary>
    /// A new blast shoves settled rubble (and loose dirt) and shatters stone:
    /// impulse scales with force / mass, so pebbles fly and boulders barely
    /// shudder. A stone chunk shatters when the arriving force exceeds
    /// ShatterBase * sqrt(mass) — big chunks split into two smaller ones,
    /// lone pixels just break.
    /// </summary>
    public void BlastPush(Vector2 center, float radius, float explosiveForce)
    {
        float pushR = radius * 1.6f;
        // Deferred splits: spawning mid-iteration can shift chunk indices.
        var splits = new System.Collections.Generic.List<Chunk>(8);
        for (int i = chunks.Count - 1; i >= 0; i--)
        {
            Chunk ch = chunks[i];
            Vector2 to = ch.pos - center;
            float dist = to.magnitude;
            if (dist > pushR) continue;
            float force = explosiveForce * (1f - dist / pushR);
            if (dist < 1e-4f) to = Vector2.up; else to /= dist;
            Vector2 dir = to + Vector2.up * 0.45f;
            dir.Normalize();
            if (ch.isStone && force > ShatterBase * Mathf.Sqrt(ch.mass))
            {
                chunks.RemoveAt(i);
                if (ch.mass >= 3f)
                {
                    // Split into two smaller rocks flung apart (spawned below).
                    float half = ch.mass * 0.5f;
                    Vector2 side = new Vector2(-dir.y, dir.x);
                    splits.Add(new Chunk
                    {
                        pos = ch.pos + side * 0.1f,
                        vel = dir * force * 0.12f + side * 2f,
                        color = ch.color, mass = half,
                    });
                    splits.Add(new Chunk
                    {
                        pos = ch.pos - side * 0.1f,
                        vel = dir * force * 0.12f - side * 2f,
                        color = ch.color, mass = half,
                    });
                }
                // else: pebble broken to dust.
                continue;
            }
            ch.sleeping = false;
            ch.still = 0f;
            float m = ch.isStone ? ch.mass : 1f;
            ch.vel += dir * (force * 0.35f / m) * Random.Range(0.7f, 1.3f);
            ch.rotVel += Random.Range(-4f, 4f);
            chunks[i] = ch;
        }
        foreach (var s in splits)
            SpawnStoneChunk(s.pos, s.vel, s.color, s.mass);
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
