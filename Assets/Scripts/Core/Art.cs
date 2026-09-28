using UnityEngine;

/// <summary>
/// Procedural sprite helpers so the game needs zero art assets.
/// Everything is drawn from a 1x1 white pixel or a generated circle.
/// </summary>
public static class Art
{
    static Sprite centeredWhite;
    static Sprite leftPivotWhite;
    static Sprite centeredCircle;
    static Texture2D circleTex;
    static Sprite rocket;
    static Sprite launcher;

    /// <summary>1x1 white sprite, pivot in the center. 1 pixel = 1 world unit,
    /// so scale the transform directly in world units to size it.</summary>
    public static Sprite CenteredWhite
    {
        get
        {
            if (centeredWhite == null)
                centeredWhite = Sprite.Create(Texture2D.whiteTexture,
                    new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return centeredWhite;
        }
    }

    /// <summary>1x1 white sprite, pivot on the left edge. 1 pixel = 1 world unit.
    /// Useful for bars that shrink left-to-right.</summary>
    public static Sprite LeftPivotWhite
    {
        get
        {
            if (leftPivotWhite == null)
                leftPivotWhite = Sprite.Create(Texture2D.whiteTexture,
                    new Rect(0, 0, 1, 1), new Vector2(0f, 0.5f), 1f);
            return leftPivotWhite;
        }
    }

    /// <summary>Soft white circle, 1 unit in diameter. Tint via SpriteRenderer.color.</summary>
    public static Sprite CenteredCircle
    {
        get
        {
            if (centeredCircle == null)
            {
                if (circleTex == null)
                {
                    const int size = 64;
                    circleTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float dx = (x - (size - 1) * 0.5f) / (size * 0.5f);
                            float dy = (y - (size - 1) * 0.5f) / (size * 0.5f);
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            float a = Mathf.Clamp01(1f - d);
                            circleTex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                        }
                    }
                    circleTex.Apply();
                }
                centeredCircle = Sprite.Create(circleTex,
                    new Rect(0, 0, circleTex.width, circleTex.height),
                    new Vector2(0.5f, 0.5f), circleTex.width);
            }
            return centeredCircle;
        }
    }

    /// <summary>Small rocket pointing +x: body, nose cone, fins. ~0.5 x 0.2 units.</summary>
    public static Sprite Rocket
    {
        get
        {
            if (rocket == null)
            {
                const int w = 48, h = 20;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                // Body: light gray tube.
                for (int y = 7; y < 13; y++)
                    for (int x = 8; x < 36; x++)
                        tex.SetPixel(x, y, new Color(0.85f, 0.85f, 0.88f, 1f));
                // Nose cone: triangle to the right.
                for (int x = 36; x < 46; x++)
                {
                    int half = (46 - x) * 3 / 10;
                    for (int y = 10 - half; y <= 10 + half; y++)
                        tex.SetPixel(x, y, new Color(0.95f, 0.3f, 0.2f, 1f));
                }
                // Fins: top and bottom at the rear.
                for (int x = 4; x < 12; x++)
                {
                    for (int y = 2; y < 7; y++) tex.SetPixel(x, y, new Color(0.7f, 0.7f, 0.75f, 1f));
                    for (int y = 13; y < 18; y++) tex.SetPixel(x, y, new Color(0.7f, 0.7f, 0.75f, 1f));
                }
                // Exhaust glow at the very back.
                for (int y = 8; y < 12; y++)
                    for (int x = 0; x < 4; x++)
                        tex.SetPixel(x, y, new Color(1f, 0.6f, 0.15f, 1f));
                tex.Apply();
                rocket = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 96f);
            }
            return rocket;
        }
    }

    /// <summary>Box launcher with 6 tubes (2x3), pointing +x. ~1.2 x 0.6 units.</summary>
    public static Sprite Launcher
    {
        get
        {
            if (launcher == null)
            {
                const int w = 96, h = 48;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                // Housing: dark olive box.
                for (int y = 6; y < 42; y++)
                    for (int x = 4; x < 92; x++)
                        tex.SetPixel(x, y, new Color(0.32f, 0.34f, 0.28f, 1f));
                // Frame edges darker.
                for (int x = 4; x < 92; x++)
                {
                    for (int y = 6; y < 10; y++) tex.SetPixel(x, y, new Color(0.22f, 0.24f, 0.2f, 1f));
                    for (int y = 38; y < 42; y++) tex.SetPixel(x, y, new Color(0.22f, 0.24f, 0.2f, 1f));
                }
                // 6 tube openings (2 rows x 3 cols) on the right face.
                for (int row = 0; row < 2; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        int cx = 66 + col * 8, cy = 16 + row * 16;
                        for (int y = -4; y <= 4; y++)
                            for (int x = -4; x <= 4; x++)
                            {
                                float d = Mathf.Sqrt(x * x + y * y);
                                if (d <= 4f)
                                    tex.SetPixel(cx + x, cy + y,
                                        d <= 2.5f ? new Color(0.08f, 0.08f, 0.08f, 1f)
                                                  : new Color(0.45f, 0.45f, 0.42f, 1f));
                            }
                    }
                tex.Apply();
                launcher = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 80f);
            }
            return launcher;
        }
    }
}
