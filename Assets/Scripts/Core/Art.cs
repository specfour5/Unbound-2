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
}
