using UnityEngine;

/// The trainer panel's frame shapes - a chamfered plate in the spirit of HUD look A - drawn into
/// small 9-sliced textures in code, so there is nothing to import and the corner cut stays a crisp
/// few pixels at any panel size. Each shape is made from one signed distance to the chamfered
/// rectangle: inside for the glass, a thin band for the frame, a soft falloff outside for the glow.
public static class SilhouettePanelSprites
{
    public static Sprite Fill(float corner) => Make(corner, 0f, d => Mathf.Clamp01(0.5f - d));

    public static Sprite Edge(float corner, float width) =>
        Make(corner, 2f, d => Mathf.Clamp01(width * 0.5f + 0.5f - Mathf.Abs(d + width * 0.5f)));

    public static Sprite Glow(float corner, float radius) =>
        Make(corner, radius, d => d <= 0f ? 0f : Mathf.Pow(1f - Mathf.Clamp01(d / radius), 2.2f));

    private static Sprite Make(float corner, float margin, System.Func<float, float> alpha)
    {
        int m = Mathf.CeilToInt(margin);
        int c = Mathf.CeilToInt(corner);
        int size = 2 * (m + c) + 8;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        float half = size * 0.5f - m;          // the plate's half-size inside the margin
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = Mathf.Abs(x + 0.5f - size * 0.5f), fy = Mathf.Abs(y + 0.5f - size * 0.5f);
                float box = Mathf.Max(fx - half, fy - half);
                float cut = (fx + fy - (2f * half - corner)) * 0.70710678f;
                float d = Mathf.Max(box, cut);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha(d) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        float border = m + c + 2;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                             new Vector4(border, border, border, border));
    }
}
