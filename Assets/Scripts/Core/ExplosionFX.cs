using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Cheap procedural explosion: an expanding flash plus flying sparks.
/// No particles, no art assets. Call ExplosionFX.Spawn(position, radius).
/// </summary>
public class ExplosionFX : MonoBehaviour
{
    class Spark
    {
        public Transform t;
        public Vector2 v;
        public SpriteRenderer r;
    }

    public static void Spawn(Vector2 pos, float radius)
    {
        var go = new GameObject("ExplosionFX");
        go.transform.position = pos;
        go.AddComponent<ExplosionFX>().Init(radius);
    }

    readonly List<Spark> sparks = new List<Spark>();
    SpriteRenderer flash;
    float life = 0.55f;
    float t;
    float radius;

    void Init(float r)
    {
        radius = r;
        flash = MakeSprite(Art.CenteredCircle, new Color(1f, 0.6f, 0.15f, 0.9f), 9);
        flash.transform.localScale = Vector3.one * 0.5f;

        int n = 14;
        for (int i = 0; i < n; i++)
        {
            var sr = MakeSprite(Art.CenteredCircle, new Color(1f, Random.Range(0.3f, 0.7f), 0.1f, 1f), 10);
            sr.transform.localScale = Vector3.one * Random.Range(0.15f, 0.35f);
            float a = Random.Range(0f, Mathf.PI * 2f);
            float sp = Random.Range(3f, 10f) * (0.5f + radius * 0.15f);
            sparks.Add(new Spark
            {
                t = sr.transform,
                v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp,
                r = sr
            });
        }
    }

    SpriteRenderer MakeSprite(Sprite sprite, Color c, int order)
    {
        var go = new GameObject("fx");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = c;
        sr.sortingOrder = order;
        return sr;
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = t / life;
        if (k >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        flash.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, radius * 2.2f, k);
        var fc = flash.color;
        fc.a = 0.9f * (1f - k);
        flash.color = fc;

        foreach (var s in sparks)
        {
            s.t.position += (Vector3)(s.v * Time.deltaTime);
            s.v += Vector2.down * 12f * Time.deltaTime;
            var c = s.r.color;
            c.a = 1f - k;
            s.r.color = c;
        }
    }
}
