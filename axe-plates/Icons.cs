using System;
using UnityEngine;

namespace AxePlates
{
    internal static class Icons
    {
        public static Material TintedMaterial(Material m, Color c)
        {
            if (m == null) return null;
            var n = new Material(m);
            n.name = m.name + "_tint";
            Color t = Color.Lerp(Color.white, c, 0.8f) * 1.25f;
            t.a = 1f;
            if (n.HasProperty("_Color")) n.SetColor("_Color", t);
            if (n.HasProperty("_BaseColor")) n.SetColor("_BaseColor", t);
            return n;
        }

        public static Sprite Tint(Sprite src, Color c)
        {
            try
            {
                if (src == null || src.texture == null) return PlateIcon(c);
                var tex = src.texture;
                Rect r = src.textureRect;
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var t2 = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
                t2.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                var px = t2.GetPixels();
                for (int i = 0; i < px.Length; i++)
                {
                    float lum = px[i].r * 0.3f + px[i].g * 0.59f + px[i].b * 0.11f;
                    Color tc = c * Mathf.Clamp(lum * 1.6f + 0.15f, 0f, 1.3f);
                    px[i] = new Color(Mathf.Lerp(px[i].r, tc.r, 0.85f), Mathf.Lerp(px[i].g, tc.g, 0.85f), Mathf.Lerp(px[i].b, tc.b, 0.85f), px[i].a);
                }
                t2.SetPixels(px);
                t2.Apply();
                return Sprite.Create(t2, new Rect(0, 0, t2.width, t2.height), new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("icon tint failed: " + e.Message);
                return PlateIcon(c);
            }
        }

        // Curved band with a row of nails, drawn procedurally.
        public static Sprite PlateIcon(Color c)
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color[N * N];
            Vector2 centre = new Vector2(34f, 64f);
            float rOut = 74f, rIn = 56f, halfAng = 52f * Mathf.Deg2Rad;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Vector2 p = new Vector2(x, y) - centre;
                    float rad = p.magnitude;
                    float ang = Mathf.Atan2(p.y, p.x);
                    Color o = new Color(0, 0, 0, 0);
                    if (rad > rIn - 2f && rad < rOut + 2f && Mathf.Abs(ang) < halfAng + 0.03f)
                    {
                        bool edge = rad < rIn || rad > rOut || Mathf.Abs(ang) > halfAng;
                        float shade = 0.75f + 0.35f * Mathf.Clamp01((rad - rIn) / (rOut - rIn)) + 0.15f * (ang / halfAng);
                        o = edge ? new Color(0.08f, 0.07f, 0.06f, 1f) : new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
                    }
                    px[y * N + x] = o;
                }
            // nails
            float rNail = (rIn + rOut) * 0.5f;
            for (int i = 0; i < 7; i++)
            {
                float a = Mathf.Lerp(-halfAng * 0.8f, halfAng * 0.8f, i / 6f);
                Vector2 np = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rNail;
                for (int dy = -3; dy <= 3; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        if (dx * dx + dy * dy > 9) continue;
                        int xx = Mathf.RoundToInt(np.x) + dx, yy = Mathf.RoundToInt(np.y) + dy;
                        if (xx < 0 || yy < 0 || xx >= N || yy >= N) continue;
                        float k = (dx + dy) < 0 ? 0.35f : 0.12f;
                        px[yy * N + xx] = new Color(k, k, k, 1f);
                    }
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        }

        private static Texture2D mouldTex;
        // Slate/iron slab with a crescent groove (the plate shape) and a row of nail holes.
        public static Texture2D MouldTexture()
        {
            if (mouldTex != null) return mouldTex;
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color[N * N];
            var rnd = new System.Random(7);
            Vector2 centre = new Vector2(30f, 64f);
            float rOut = 80f, rIn = 60f, halfAng = 55f * Mathf.Deg2Rad;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float noise = (float)rnd.NextDouble() * 0.10f;
                    float g = 0.30f + noise + 0.08f * Mathf.Sin(x * 0.21f) * Mathf.Sin(y * 0.17f);
                    bool border = x < 5 || y < 5 || x > N - 6 || y > N - 6;
                    Color c = border ? new Color(0.14f, 0.13f, 0.12f, 1f) : new Color(g * 0.95f, g, g * 1.08f, 1f);
                    Vector2 p = new Vector2(x, y) - centre;
                    float rad = p.magnitude, ang = Mathf.Atan2(p.y, p.x);
                    if (!border && rad > rIn && rad < rOut && Mathf.Abs(ang) < halfAng)
                    {
                        float edge = Mathf.Min(rad - rIn, rOut - rad);
                        c = edge < 3f ? new Color(0.50f, 0.48f, 0.45f, 1f) : new Color(0.07f, 0.06f, 0.06f, 1f);
                    }
                    px[y * N + x] = c;
                }
            float rn = (rIn + rOut) * 0.5f;
            for (int k = 0; k < 7; k++)
            {
                float a = Mathf.Lerp(-halfAng * 0.8f, halfAng * 0.8f, k / 6f);
                Vector2 np = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rn;
                for (int dy = -3; dy <= 3; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        if (dx * dx + dy * dy > 9) continue;
                        int xx = Mathf.RoundToInt(np.x) + dx, yy = Mathf.RoundToInt(np.y) + dy;
                        if (xx >= 0 && yy >= 0 && xx < N && yy < N) px[yy * N + xx] = new Color(0.62f, 0.60f, 0.55f, 1f);
                    }
            }
            tex.SetPixels(px); tex.Apply();
            mouldTex = tex;
            return tex;
        }

        public static Sprite MouldIcon()
        {
            var t = MouldTexture();
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f));
        }

        public static Sprite Badge(Color c)
        {
            const int N = 48;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color[N * N];
            Vector2 ce = new Vector2(N / 2f - 0.5f, N / 2f - 0.5f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float r = (new Vector2(x, y) - ce).magnitude;
                    Color o = new Color(0, 0, 0, 0);
                    if (r <= 23f) o = new Color(0.05f, 0.05f, 0.05f, 1f);
                    if (r <= 20f) o = new Color(0.85f, 0.85f, 0.8f, 1f);
                    if (r <= 18f) o = c * 0.85f;
                    if (r <= 18f) o.a = 1f;
                    // plate arc and nails inside the circle
                    Vector2 p = new Vector2(x, y) - new Vector2(10f, 24f);
                    float rr = p.magnitude, ang = Mathf.Atan2(p.y, p.x);
                    if (rr > 17f && rr < 25f && Mathf.Abs(ang) < 0.9f && r <= 18f) o = Color.Lerp(c, Color.white, 0.55f);
                    px[y * N + x] = o;
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        }
    }
}
