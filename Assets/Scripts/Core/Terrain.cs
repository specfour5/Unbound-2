using UnityEngine;

/// <summary>
/// True 2D destructible terrain: every grid cell is independently solid or
/// empty, so explosions carve ragged bowls, shells can burrow sideways into
/// tunnels, and overhangs / floating chunks are all representable. There is
/// deliberately NO column-top invariant anywhere in this class.
///
/// Simulation stays on the coarse grid (pixelSize); rendering draws only
/// boundary cells (solid cells touching empty) as pixel quads, so overhangs
/// and tunnels render correctly. Physics never touches a terrain collider:
/// suspension modules probe downward through the grid and projectiles sweep
/// the grid along their flight path.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
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
    [Tooltip("When true, hills grow rougher from left to right (campaign difficulty).")]
    public bool rampDifficulty;
    [Tooltip("When true, the far half of the map hides stone blobs underground (tougher digging).")]
    public bool deepStone;

    [Header("Simulation grid")]
    [Tooltip("World units per sim cell. Smaller = finer crater edges and tunnels.")]
    public float pixelSize = 0.15f;

    [Header("Explosions")]
    [Tooltip("Blast force absorbed per solid cell the shockwave crosses. Makes surface blasts dig wide shallow bowls (energy vents into the air) and buried blasts blow spherical cavities (confined in all directions).")]
    public float blastAbsorption = 8f;
    [Tooltip("Random per-pixel variation in how easily blasts break terrain (force units). Higher = more ragged, less uniform craters.")]
    public float breakNoise = 6f;

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
    bool[,] scorched; // blast-charred pixels: never grass
    bool[,] stone;    // blast-fractured rock: grey instead of dirt
    bool[,] muddy;    // track-churned pixels: dark muddy dirt instead of grass
    bool muddyDirty;  // set when new track mud is marked; rebuilt throttled
    float lastMudRebuild = -10f;
    float[] compacted; // per-column wheel compaction this battle (world units)
    int cols, rows;
    float gridY0;
    Mesh mesh;
    DebrisSystem debris;

    public float LeftX => -width / 2f;
    public float RightX => width / 2f;

    void Awake()
    {
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
        debris = GetComponent<DebrisSystem>();
        if (debris == null) debris = gameObject.AddComponent<DebrisSystem>();
        Generate();
    }

    /// <summary>Builds the hills from layered sine waves, then applies flatten spots.</summary>
    public void Generate()
    {
        // Projectiles sweep the grid manually now, so any stale edge collider
        // baked by older scene builds must go (it would collide with ghosts).
        var stale = GetComponent<EdgeCollider2D>();
        if (stale != null)
        {
            if (Application.isPlaying) Destroy(stale);
            else DestroyImmediate(stale);
        }

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

        float p1 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float p2 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float p3 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float f1 = 1f + (float)rng.NextDouble() * 1.5f;
        float f2 = 3f + (float)rng.NextDouble() * 3f;

        float[] heights = new float[cols];
        for (int c = 0; c < cols; c++)
        {
            float t = (float)c / Mathf.Max(1, cols - 1);
            heights[c] = baseHeight
                + Mathf.Sin(t * Mathf.PI * f1 + p1) * amplitude * 0.6f
                + Mathf.Sin(t * Mathf.PI * f2 + p2) * amplitude * 0.3f
                + Mathf.Sin(t * Mathf.PI * 9f + p3) * amplitude * 0.05f;
            // Campaign difficulty: hills start gentle and grow rougher.
            if (rampDifficulty)
            {
                float ramp = Mathf.Lerp(0.65f, 1.6f, t);
                heights[c] = baseHeight + (heights[c] - baseHeight) * ramp;
            }
        }

        // Gentle smoothing pass: takes the edge off single-column spikes
        // without changing the landscape's character.
        for (int c = 1; c < cols - 1; c++)
            heights[c] = (heights[c - 1] + heights[c] * 2f + heights[c + 1]) * 0.25f;

        // Fill a depth-thick band under the surface contour.
        for (int c = 0; c < cols; c++)
        {
            float h = Mathf.Max(1.5f, heights[c]);
            int rTop = RowAt(h);
            int rBot = RowAt(h - depth);
            for (int r = Mathf.Max(0, rBot); r <= Mathf.Min(rTop, rows - 1); r++)
                solid[c, r] = true;
        }

        foreach (var spot in flattenSpots)
            ApplyFlatten(spot.x, spot.radius);

        // Campaign flavor: the far half of the map hides stone blobs a few
        // pixels under the surface, so late-game shells dig less easily.
        if (deepStone)
        {
            for (int c = cols / 2; c < cols; c++)
            {
                if (rng.NextDouble() > 0.35) continue;
                int r = TopSolidRow(c) - (2 + rng.Next(5)); // 2..6 px under the surface
                if (r < 2) continue;
                int blob = 1 + rng.Next(3); // 1..3 px tall
                for (int rr = r; rr > r - blob && rr >= 0; rr--)
                    stone[c, rr] = true;
            }
        }

        float minSurface = float.MaxValue;
        for (int c = 0; c < cols; c++)
            minSurface = Mathf.Min(minSurface, GetHeightAt(LeftX + (c + 0.5f) * pixelSize));
        Debug.Log($"[Terrain] Generate done: cols={cols} rows={rows} pixelSize={pixelSize} " +
                  $"minSurface={minSurface:F2} flattenSpots={flattenSpots.Count}");

        Rebuild();
    }

    int ColumnAt(float x)
    {
        return Mathf.Clamp(Mathf.FloorToInt((x - LeftX) / pixelSize), 0, cols - 1);
    }

    int RowAt(float y)
    {
        return Mathf.FloorToInt((y - gridY0) / pixelSize);
    }

    /// <summary>Topmost solid row in a column, or -1 when the column is empty.</summary>
    int TopSolidRow(int c)
    {
        for (int r = rows - 1; r >= 0; r--)
            if (solid[c, r]) return r;
        return -1;
    }

    /// <summary>Height of the highest solid cell top at x (spawn placement).</summary>
    public float GetHeightAt(float x)
    {
        if (solid == null) return baseHeight;
        int t = TopSolidRow(ColumnAt(x));
        return t < 0 ? gridY0 : gridY0 + (t + 1) * pixelSize;
    }

    /// <summary>
    /// Downward probe for suspension modules: the top surface of the first
    /// solid cell at column x at or below startY, or startY - maxDist when
    /// there is nothing to stand on. Overhangs and tunnel floors just work:
    /// the probe finds whatever is actually below the wheel.
    /// </summary>
    public float SampleGroundBelow(float x, float startY, float maxDist)
    {
        if (solid == null) return startY - maxDist;
        int c = ColumnAt(x);
        int r0 = Mathf.Min(RowAt(startY), rows - 1);
        int r1 = Mathf.Max(RowAt(startY - maxDist), 0);
        for (int r = r0; r >= r1; r--)
            if (solid[c, r]) return gridY0 + (r + 1) * pixelSize;
        return startY - maxDist;
    }

    /// <summary>
    /// True when the given world point is inside solid terrain. Used by the
    /// vehicle hull bumper probes and the projectile flight sweep.
    /// </summary>
    public bool IsSolidAt(float x, float y)
    {
        if (solid == null) return false;
        int r = RowAt(y);
        if (r < 0 || r >= rows) return false;
        return solid[ColumnAt(x), r];
    }

    /// <summary>
    /// Registers a flattened spawn pad (survives regeneration) and applies it
    /// now. Per-cell version: clears everything above the pad height and fills
    /// the depth band below it, feathering into the natural surface at the rim.
    /// </summary>
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
        int c0 = Mathf.Max(0, cc - cr), c1 = Mathf.Min(cols - 1, cc + cr);
        for (int c = c0; c <= c1; c++)
        {
            float colX = LeftX + (c + 0.5f) * pixelSize;
            float blend = 1f - Mathf.Abs(c - cc) / (float)(cr + 1);
            float target = Mathf.Lerp(GetHeightAt(colX), h, blend * blend);
            int rTop = RowAt(target);
            int rBot = RowAt(target - depth);
            for (int r = 0; r < rows; r++)
            {
                bool want = r <= rTop && r >= rBot;
                if (want && !solid[c, r])
                {
                    solid[c, r] = true;
                    scorched[c, r] = stone[c, r] = muddy[c, r] = false;
                }
                else if (!want && solid[c, r])
                {
                    solid[c, r] = false;
                    scorched[c, r] = stone[c, r] = muddy[c, r] = false;
                }
            }
        }
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
            int t = TopSolidRow(c);
            if (t >= 0 && !muddy[c, t] && !scorched[c, t]) { muddy[c, t] = true; any = true; }
        }
        if (any) muddyDirty = true;
    }

    void LateUpdate()
    {
        // Fold newly churned track mud into the mesh a few times a second.
        // The interval scales with map length so long campaign maps don't
        // hitch while driving (a full rebuild touches every cell).
        float interval = 0.2f * Mathf.Max(1f, cols / 800f);
        if (muddyDirty && Time.time - lastMudRebuild > interval)
        {
            muddyDirty = false;
            lastMudRebuild = Time.time;
            Rebuild();
        }
    }

    /// <summary>
    /// Wheels: smoothly depresses the terrain as a vehicle rolls over it.
    /// Each column's surface is compacted toward a cosine-falloff rut (deepest
    /// at the wheel center, feathering out to the edges), at most one pixel per
    /// call and never deeper than depth total — so rolling back and forth can't
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
            int t = TopSolidRow(c);
            if (t < 0) continue;
            solid[c, t] = false;
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
            int t = TopSolidRow(c);
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
            if (t - removed >= 0) muddy[c, t - removed] = true;
            any = true;
        }
        if (any) muddyDirty = true;
    }

    /// <summary>True when the cell has open sky within a few pixels above it.</summary>
    bool UpExposed(int c, int r, int range)
    {
        for (int k = 1; k <= range; k++)
        {
            int rr = r + k;
            if (rr >= rows || !solid[c, rr]) return true;
        }
        return false;
    }

    /// <summary>
    /// How resistant the terrain pixel at a world point is to penetration and
    /// blasts. Stone 4, scorched 2.5, dirt/mud 2, grass 1, empty 0.
    /// </summary>
    public float GetHardnessAt(float x, float y)
    {
        if (solid == null) return 0f;
        int c = ColumnAt(x);
        int r = RowAt(y);
        if (r < 0 || r >= rows || !solid[c, r]) return 0f;
        if (stone[c, r]) return 4f;
        if (scorched[c, r]) return 2.5f;
        if (muddy[c, r]) return 2f;
        if (UpExposed(c, r, 4)) return 1f; // grass skin
        return 2f; // dirt
    }

    /// <summary>
    /// Knocks out pixels in a circle centered on world position. Harder pixels
    /// resist: a pixel breaks when explosiveForce * falloff exceeds its
    /// hardness (x HardnessTune), so force breaks harder terrain and the
    /// crater shrinks in stone instead of ignoring it.
    /// The shockwave loses blastAbsorption force per solid cell it crosses
    /// between the blast and the pixel, so surface blasts dig wide shallow
    /// bowls (energy vents upward through air) while buried blasts blow
    /// roughly spherical cavities (confined in every direction).
    /// There is no column-top cleanup: ragged walls, overhangs and tunnels are
    /// all legal results. A smoothing pass knocks single-pixel spikes off the
    /// fresh crater walls (break noise still keeps craters varied).
    /// Pixels near the blast vaporize; destroyed pixels in
    /// the outer band scatter as physical debris with a force gradient.
    /// Freshly exposed faces are charred where they face the sky and fractured
    /// to stone where they face sideways/down.
    /// </summary>
    public void CarveCrater(Vector2 center, float radius, float explosiveForce)
    {
        if (solid == null || radius <= 0f) return;
        const float HardnessTune = 8f;
        const float VaporizeFrac = 0.55f; // inside this fraction: vaporized, no debris
        const int MaxDebrisPerBlast = 260;
        int c0 = Mathf.Max(0, ColumnAt(center.x - radius));
        int c1 = Mathf.Min(cols - 1, ColumnAt(center.x + radius));
        int r0 = Mathf.Max(0, RowAt(center.y - radius));
        int r1 = Mathf.Min(rows - 1, RowAt(center.y + radius) + 1);
        float r2 = radius * radius;
        var carved = new System.Collections.Generic.List<int>(256);
        var seeds = new System.Collections.Generic.List<DebrisSeed>(256);
        for (int c = c0; c <= c1; c++)
            for (int r = r0; r <= r1; r++)
            {
                if (!solid[c, r]) continue;
                float px = LeftX + (c + 0.5f) * pixelSize;
                float py = gridY0 + (r + 0.5f) * pixelSize;
                float dx = px - center.x, dy = py - center.y;
                float d2 = dx * dx + dy * dy;
                if (d2 > r2) continue;
                float dist = Mathf.Sqrt(d2);
                float falloff = 1f - dist / radius;
                float force = explosiveForce * falloff
                    - ShockAbsorption(center, px, py) * blastAbsorption;
                // Positional noise (stable per location): no two craters break the same way.
                float noise = (Hash01(c * 7 + 1, r * 13 + 5) - 0.5f) * breakNoise;
                if (force + noise > GetHardnessAt(px, py) * HardnessTune)
                {
                    if (dist > radius * VaporizeFrac)
                        seeds.Add(new DebrisSeed
                        {
                            pos = new Vector2(px, py),
                            color = CellColor(c, r),
                            dist = dist,
                        });
                    solid[c, r] = false;
                    carved.Add(c * rows + r);
                }
            }
        SpawnDebris(center, radius, explosiveForce, seeds, MaxDebrisPerBlast);
        SmoothCrater(center, radius, carved);
        // Char / fracture the freshly exposed faces around the blast.
        foreach (int packed in carved)
        {
            int c = packed / rows, r = packed % rows;
            TryWeatherFace(c + 1, r);
            TryWeatherFace(c - 1, r);
            TryWeatherFace(c, r + 1);
            TryWeatherFace(c, r - 1);
        }
        Rebuild();
    }

    struct DebrisSeed
    {
        public Vector2 pos;
        public Color color;
        public float dist;
    }

    /// <summary>
    /// Counts the solid cells on the segment from the blast center to a
    /// target point: the shockwave's path through the ground. Rays that vent
    /// through air lose nothing; rays buried in dirt lose the most.
    /// </summary>

    float ShockAbsorption(Vector2 from, float tx, float ty)
    {
        float dx = tx - from.x, dy = ty - from.y;
        float dist = Mathf.Sqrt(dx * dx + dy * dy);
        if (dist < 1e-6f) return 0f;
        float step = pixelSize * 0.5f;
        int n = Mathf.Max(1, Mathf.FloorToInt(dist / step));
        int absorbed = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (i + 0.5f) / n;
            if (IsSolidAt(from.x + dx * t, from.y + dy * t)) absorbed++;
        }
        return absorbed;
    }

    /// <summary>
    /// Smoothing pass over the fresh crater: removes solid cells left jutting
    /// into the blast (3+ empty 4-neighbors) that the break noise would
    /// otherwise leave as single-pixel spikes. Limited to the blast radius so
    /// tunnels and overhangs elsewhere are untouched. Smoothed cells join the
    /// carved set so their fresh faces get weathered too.
    /// </summary>
    void SmoothCrater(Vector2 center, float radius, System.Collections.Generic.List<int> carved)
    {
        float r2 = radius * radius * 1.1f; // slight margin past the blast edge
        for (int pass = 0; pass < 2; pass++)
        {
            var spikes = new System.Collections.Generic.List<int>(64);
            int c0 = Mathf.Max(0, ColumnAt(center.x - radius));
            int c1 = Mathf.Min(cols - 1, ColumnAt(center.x + radius));
            int r0 = Mathf.Max(0, RowAt(center.y - radius));
            int r1 = Mathf.Min(rows - 1, RowAt(center.y + radius) + 1);
            for (int c = c0; c <= c1; c++)
                for (int r = r0; r <= r1; r++)
                {
                    if (!solid[c, r]) continue;
                    float px = LeftX + (c + 0.5f) * pixelSize;
                    float py = gridY0 + (r + 0.5f) * pixelSize;
                    float dx = px - center.x, dy = py - center.y;
                    if (dx * dx + dy * dy > r2) continue;
                    int empty = 0;
                    if (c + 1 >= cols || !solid[c + 1, r]) empty++;
                    if (c - 1 < 0 || !solid[c - 1, r]) empty++;
                    if (r + 1 >= rows || !solid[c, r + 1]) empty++;
                    if (r - 1 < 0 || !solid[c, r - 1]) empty++;
                    if (empty >= 3) spikes.Add(c * rows + r);
                }
            if (spikes.Count == 0) break;
            foreach (int packed in spikes)
            {
                solid[packed / rows, packed % rows] = false;
                carved.Add(packed);
            }
        }
    }

    /// <summary>
    /// Scatters the outer-band debris: speed falls off with distance from the
    /// blast (inner band flies fastest) and scales with the explosion force,
    /// with an upward bias and random jitter so it reads as an explosion.
    /// </summary>
    void SpawnDebris(Vector2 center, float radius, float explosiveForce,
        System.Collections.Generic.List<DebrisSeed> seeds, int maxDebris)
    {
        if (debris == null || seeds.Count == 0) return;
        if (seeds.Count > maxDebris)
        {
            // Thin randomly, single pass (approximate cap is fine).
            float keep = (float)maxDebris / seeds.Count;
            seeds.RemoveAll(_ => Random.value > keep);
        }
        float vaporR = radius * 0.55f;
        float band = Mathf.Max(0.01f, radius - vaporR);
        foreach (var s in seeds)
        {
            float t = Mathf.Clamp01((s.dist - vaporR) / band); // 0 inner -> 1 edge
            float speed = Mathf.Min(14f, Mathf.Lerp(1f, 0.35f, t) * explosiveForce * 0.12f);
            Vector2 dir = s.pos - center;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
            dir.Normalize();
            dir += Vector2.up * 0.55f; // explosions throw upward
            dir.Normalize();
            float ang = Random.Range(-0.4f, 0.4f);
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            dir = new Vector2(dir.x * ca - dir.y * sa, dir.x * sa + dir.y * ca);
            dir *= speed * Random.Range(0.7f, 1.3f);
            debris.SpawnChunk(s.pos, dir, s.color, pixelSize * Random.Range(0.8f, 1.4f));
        }
    }

    void TryWeatherFace(int c, int r)
    {
        if (c < 0 || c >= cols || r < 0 || r >= rows) return;
        if (!solid[c, r] || scorched[c, r] || stone[c, r]) return;
        // Sky-facing blast faces char; sideways/down faces fracture to stone.
        if (UpExposed(c, r, 3)) scorched[c, r] = true;
        else stone[c, r] = true;
    }

    static float Hash01(int a, int b)
    {
        int h = (a * 73856093) ^ (b * 19349663);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }

    /// <summary>
    /// The classic pixel-terrain palette, evaluated per boundary cell:
    /// grass where the cell sees sky, dirt below, charred / stone / mud
    /// overrides from the blast and track systems.
    /// </summary>
    Color CellColor(int c, int r)
    {
        float px = LeftX + (c + 0.5f) * pixelSize;
        float py = gridY0 + (r + 0.5f) * pixelSize;
        int hx = Mathf.FloorToInt(px / 0.15f);
        int hy = Mathf.FloorToInt(py / 0.15f);
        float h = Hash01(hx * 3 + 1, hy * 7 + 2);

        if (scorched[c, r]) return ScorchedColor(hx, hy);
        if (stone[c, r]) return StoneColor(hx, hy);

        // Grass band: cells with open sky not far above.
        int upDist = 99;
        for (int k = 1; k <= 5; k++)
            if (r + k >= rows || !solid[c, r + k]) { upDist = k; break; }

        if (muddy[c, r] && upDist <= 2)
            return new Color(0.34f, 0.25f, 0.15f) * (0.9f + 0.2f * h); // churned track mud

        if (upDist <= 4)
        {
            int gd = 2 + (int)(Hash01(hx, 777) * 2.999f); // jagged grass edge
            if (upDist <= gd)
            {
                if (upDist == 1) return GrassDark * 0.8f; // dark crust on the surface line
                if (h < 0.15f) return GrassLight;
                if (h > 0.85f) return GrassDark;
                return GrassBase * (0.95f + 0.10f * h);
            }
        }

        // Dirt: speckled, darkening with absolute depth for a rich underground feel.
        Color d;
        if (h < 0.13f) d = DirtDark;
        else if (h < 0.26f) d = DirtLight;
        else d = DirtBase * (0.94f + 0.12f * h);
        return d * (0.55f + 0.45f * ((float)r / Mathf.Max(1, rows - 1)));
    }

    /// <summary>Charred blast-crater pixels: dark, mottled, never grass.</summary>
    static Color ScorchedColor(int c, int r)
    {
        float h1 = Hash01(c * 5 + 3, r * 11 + 7);
        if (h1 < 0.25f) return new Color(0.13f, 0.10f, 0.08f);
        if (h1 < 0.45f) return new Color(0.30f, 0.21f, 0.14f);
        return new Color(0.22f, 0.16f, 0.12f) * (0.92f + 0.16f * h1);
    }

    /// <summary>Blast-fractured rock: neutral grey stone instead of dirt.</summary>
    static Color StoneColor(int c, int r)
    {
        float h1 = Hash01(c * 9 + 5, r * 13 + 11);
        if (h1 < 0.20f) return new Color(0.40f, 0.40f, 0.43f);
        if (h1 < 0.40f) return new Color(0.62f, 0.62f, 0.65f);
        return new Color(0.51f, 0.51f, 0.54f) * (0.92f + 0.16f * h1);
    }

    /// <summary>
    /// Renders every solid cell as a pixel quad with a slight vertical
    /// gradient for form. Side view shows the whole terrain face, so the
    /// interior must be filled — boundary-only rendering leaves the ground
    /// hollow/see-through.
    /// </summary>
    readonly System.Collections.Generic.List<Vector3> rebuildVerts =
        new System.Collections.Generic.List<Vector3>(65536);
    readonly System.Collections.Generic.List<Color> rebuildColors =
        new System.Collections.Generic.List<Color>(65536);
    readonly System.Collections.Generic.List<int> rebuildTris =
        new System.Collections.Generic.List<int>(98304);

    void Rebuild()
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "TerrainMesh" };
            // Keep 32-bit indices so big maps never hit the 65k vertex cap.
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            GetComponent<MeshFilter>().mesh = mesh;
        }

        var verts = rebuildVerts;
        var colors = rebuildColors;
        var tris = rebuildTris;
        verts.Clear();
        colors.Clear();
        tris.Clear();

        for (int c = 0; c < cols; c++)
        {
            float x0 = LeftX + c * pixelSize;
            float x1 = x0 + pixelSize;
            for (int r = 0; r < rows; r++)
            {
                if (!solid[c, r]) continue;

                float y0 = gridY0 + r * pixelSize;
                float y1 = y0 + pixelSize;
                Color baseCol = CellColor(c, r);
                Color topCol = baseCol * 1.06f;
                Color botCol = baseCol * 0.82f;

                int i = verts.Count;
                verts.Add(new Vector3(x0, y0, 0));
                verts.Add(new Vector3(x1, y0, 0));
                verts.Add(new Vector3(x1, y1, 0));
                verts.Add(new Vector3(x0, y1, 0));
                colors.Add(botCol);
                colors.Add(botCol);
                colors.Add(topCol);
                colors.Add(topCol);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
        }

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }
}
