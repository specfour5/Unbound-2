using UnityEngine;

/// <summary>
/// Track band stretched between the outermost suspension modules every
/// frame, so the tracks hug the terrain while the hull floats on its
/// suspension above them. Treads scroll with the odometer for a rolling look.
/// Built on a plain quad (full UV control); the end wheels drawn on top cover
/// the square corners.
/// </summary>
public class TrackVisual : MonoBehaviour
{
    [Tooltip("Module at the track's front end (max local X).")]
    public SuspensionModule frontModule;
    [Tooltip("Module at the track's rear end (min local X).")]
    public SuspensionModule rearModule;
    [Tooltip("Vertical thickness of the track loop.")]
    public float bandHeight = 0.72f;
    [Tooltip("World length of one tread repeat.")]
    public float treadWorldSize = 0.45f;
    public int sortingOrder = 1;

    Vehicle vehicle;
    Material mat;
    float lastLen = -1f;
    bool built;

    public void Init(Vehicle v)
    {
        vehicle = v;
    }

    void Awake()
    {
        BuildVisuals();
    }

    void BuildVisuals()
    {
        if (built) return;
        built = true;

        mat = new Material(Shader.Find("Sprites/Default"));
        Texture2D tex = MakeTrackTexture();
        tex.wrapMode = TextureWrapMode.Repeat;
        mat.mainTexture = tex;

        var mf = gameObject.AddComponent<MeshFilter>();
        var mesh = new Mesh();
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; // CCW, faces +Z
        mesh.RecalculateNormals();
        mf.mesh = mesh;

        var mr = gameObject.AddComponent<MeshRenderer>();
        mr.material = mat;
        mr.sortingOrder = sortingOrder;
    }

    void LateUpdate()
    {
        if (!built || vehicle == null || frontModule == null || rearModule == null) return;

        Vector2 pf = (Vector2)frontModule.transform.localPosition + frontModule.AxleLocal;
        Vector2 pr = (Vector2)rearModule.transform.localPosition + rearModule.AxleLocal;
        Vector2 mid = (pf + pr) * 0.5f;
        float len = Vector2.Distance(pf, pr) + bandHeight;

        transform.localPosition = new Vector3(mid.x, mid.y, 0f);
        transform.localRotation = Quaternion.Euler(0f, 0f,
            Mathf.Atan2(pf.y - pr.y, pf.x - pr.x) * Mathf.Rad2Deg);
        transform.localScale = new Vector3(len, bandHeight, 1f);

        if (Mathf.Abs(len - lastLen) > 0.001f)
        {
            mat.mainTextureScale = new Vector2(len / treadWorldSize, 1f);
            lastLen = len;
        }
        // Treads at the contact patch stream backward relative to motion.
        mat.mainTextureOffset = new Vector2(vehicle.Odometer / treadWorldSize, 0f);
    }

    /// <summary>Procedural track-link texture: dark band, tread bars, edge wear.</summary>
    static Texture2D MakeTrackTexture()
    {
        const int W = 128, H = 32;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var px = new Color32[W * H];
        var baseC = new Color32(0x34, 0x38, 0x42, 255);
        var linkC = new Color32(0x4a, 0x4f, 0x5c, 255);
        var edgeC = new Color32(0x23, 0x26, 0x2c, 255);
        var hiC = new Color32(0x5c, 0x62, 0x74, 255);
        // Deterministic speckle so the band doesn't look flat.
        var rng = new System.Random(1234);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            Color32 c = baseC;
            bool edge = y < 3 || y >= H - 3;
            bool bar = (x % 16) < 9; // tread bars
            if (edge) c = edgeC;
            else if (bar) c = (x % 16) == 0 || (x % 16) == 8 ? edgeC : linkC;
            if (!edge && bar && y < 8) c = hiC; // top wear highlight
            int n = rng.Next(9) - 4;
            c.r = (byte)Mathf.Clamp(c.r + n, 0, 255);
            c.g = (byte)Mathf.Clamp(c.g + n, 0, 255);
            c.b = (byte)Mathf.Clamp(c.b + n, 0, 255);
            px[y * W + x] = c;
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }
}
