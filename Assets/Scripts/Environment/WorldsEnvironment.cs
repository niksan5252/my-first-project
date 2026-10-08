using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum WorldSkin
{
    Arcade,     // inside a retro arcade game: pixel-block equalizer walls, pixel invaders/ghosts, coins
    Candy,      // candyland: lollipops, candy canes, gumdrops, cotton-candy clouds, sprinkles
    NeoTokyo,   // neon Tokyo night: torii gates, sakura trees, falling petals, lanterns, kanji signs, Fuji
    Ocean,      // deep ocean rave: kelp, jellyfish, fish schools, bubbles, light rays, a whale far off
    Volcano,    // run through a volcano: lava rivers, obsidian spires, geysers on the kick, embers, eruption
}

/// Video 12's scenery: five worlds, one per level, one class. Everything near scrolls with the
/// tunnel and wraps; the sky pieces sit beyond the fog (Custom/Starfield ignores fog, see
/// CosmicEnvironment). A global Volume pushes the bloom up on a drop. Time and energy come from
/// ClubBeat (the song clock), so it is on the music in Play and in a recording.
public class WorldsEnvironment : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [SerializeField] private WorldSkin skin;
    [SerializeField, Range(0f, 1f)] private float dropBloom = 0.4f;
    [SerializeField] private float bloomPeak = 5f;
    [SerializeField] private int seed = 1212;

    private const float Behind = 12f;
    private const float MaxLevel = 2.4f;

    private Color primary = new Color(1f, 0.3f, 0.7f);
    private Color secondary = new Color(0f, 0.9f, 1f);
    private float emission = 1.3f;
    private float scrollSpeed, span;
    private bool built;
    private System.Random rng;
    private Mesh cube, sphere, cyl, rockMesh;
    private MaterialPropertyBlock mpb;
    private readonly List<Object> owned = new List<Object>();

    private struct Mover { public Transform t; public MeshRenderer r; public Vector3 basePos, axis; public float seed, speed, size; public int group; }
    private readonly List<Mover> scrollers = new List<Mover>();        // plain scrolling props
    private readonly List<Mover> pulsers = new List<Mover>();          // scroll + per-group beat light
    private readonly List<Mover> floaters = new List<Mover>();         // scroll + bob/spin
    private readonly List<Mover> particles = new List<Mover>();        // fall/rise within a box in front of the camera
    private readonly List<Mover> columns = new List<Mover>();          // equalizer columns / geysers
    private readonly List<Material> groupMats = new List<Material>();
    private readonly List<Color> groupCols = new List<Color>();
    private Material particleMat, accentMat, glowSkyMat;
    private Material[] particleMats;
    private Volume bloomVolume;
    private VolumeProfile bloomProfile;

    public void ApplyPalette(Color primaryColor, Color secondaryColor, float emissionIntensity)
    {
        primary = primaryColor;
        secondary = secondaryColor;
        emission = Mathf.Min(emissionIntensity, 1.5f);
    }

    public void Initialize(float tunnelLength, float scrollSpeed)
    {
        if (built) return;
        built = true;
        this.scrollSpeed = scrollSpeed;
        span = tunnelLength + Behind + 8f;
        rng = new System.Random(seed + (int)skin * 151);
        mpb = new MaterialPropertyBlock();
        cube = RemixGeo.Primitive(PrimitiveType.Cube);
        sphere = RemixGeo.Primitive(PrimitiveType.Sphere);
        cyl = RemixGeo.Primitive(PrimitiveType.Cylinder);
        switch (skin)
        {
            case WorldSkin.Arcade: BuildArcade(); break;
            case WorldSkin.Candy: BuildCandy(); break;
            case WorldSkin.NeoTokyo: BuildTokyo(); break;
            case WorldSkin.Ocean: BuildOcean(); break;
            case WorldSkin.Volcano: BuildVolcano(); break;
        }
        BuildBloomVolume();
    }

    private void OnDestroy()
    {
        foreach (var o in owned) if (o != null) Destroy(o);
        if (bloomProfile != null) Destroy(bloomProfile);
    }

    // ---------- helpers ----------

    private float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    private T Own<T>(T o) where T : Object { owned.Add(o); return o; }
    private Material Neon(Color c) => Own(RemixGeo.Neon(c, emission));
    private Material Lit(Color c, float smooth = 0.3f)
    {
        var m = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        return m;
    }
    private int Group(Color c) { groupMats.Add(Neon(c)); groupCols.Add(c); return groupMats.Count - 1; }

    private Transform Root(string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(transform, false);
        return t;
    }

    private MeshRenderer Place(string name, Mesh mesh, Material mat, Transform parent, Vector3 pos, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    private Transform Prop(Transform root, string name, Vector3 pos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        t.localPosition = pos;
        return t;
    }

    private float Far => span - Behind;

    private Texture2D Sprite(int size, float power)
    {
        var tex = Own(new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp });
        var px = new Color32[size * size];
        float h = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - h) / h, dy = (y + 0.5f - h) / h, r = Mathf.Sqrt(dx * dx + dy * dy);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Pow(Mathf.Clamp01(1f - r), power) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // A sky quad beyond the fog; tex may be any alpha mask. Returns its material (for _Intensity).
    private Material SkyQuad(string name, Vector3 pos, float w, float h, Color col, Texture2D tex, float intensity, float rotZ = 0f)
    {
        var m = Own(new Mesh { name = name });
        m.vertices = new[] { new Vector3(-w, -h, 0), new Vector3(w, -h, 0), new Vector3(w, h, 0), new Vector3(-w, h, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.uv2 = new Vector2[4];
        m.colors = new[] { col, col, col, col };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        var mat = Own(new Material(Shader.Find("Custom/Starfield")));
        mat.SetTexture("_BaseMap", tex);
        mat.SetFloat("_Intensity", intensity);
        mat.SetFloat("_TwinkleAmount", 0f);
        var mr = Place(name, m, mat, transform, pos, Vector3.one);
        mr.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        return mat;
    }

    // A texture from a function (x, y in 0..1) -> alpha.
    private Texture2D Mask(int w, int h, System.Func<float, float, float> a)
    {
        var tex = Own(new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp });
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a((x + 0.5f) / w, (y + 0.5f) / h)) * 255f));
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private Mesh Rock()
    {
        var v = sphere.vertices;
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 p = v[i];
            float h = Mathf.Sin(p.x * 12.9898f + p.y * 78.233f + p.z * 37.719f) * 43758.5453f;
            h -= Mathf.Floor(h);
            v[i] = p * (0.75f + 0.45f * h);
        }
        var m = Own(new Mesh { name = "Rock" });
        m.vertices = v; m.triangles = sphere.triangles; m.uv = sphere.uv;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // Particles that live in a box in front of the camera and wrap inside it.
    private void ParticleBox(string name, int count, Mesh mesh, Material[] mats, Vector3 scale, Vector2 speed, float x = 14f, float y0 = 0f, float y1 = 16f, float z1 = 70f)
    {
        var root = Root(name);
        for (int i = 0; i < count; i++)
        {
            var p = new Vector3(Rand(-x, x), Rand(y0, y1), Rand(1f, z1));
            var mr = Place("P", mesh, mats[i % mats.Length], root, p, scale);
            particles.Add(new Mover { t = mr.transform, r = mr, basePos = new Vector3(x, y1 - y0, z1), speed = Rand(speed.x, speed.y), seed = Rand(0f, 6.28f),
                                      axis = new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), Rand(-1f, 1f)).normalized, size = y0 });
        }
    }

    // ---------- Arcade ----------

    private static readonly string[] Invader =
    {
        "..X.....X..",
        "...X...X...",
        "..XXXXXXX..",
        ".XX.XXX.XX.",
        "XXXXXXXXXXX",
        "X.XXXXXXX.X",
        "X.X.....X.X",
        "...XX.XX...",
    };
    private static readonly string[] Ghost =
    {
        "...XXXX...",
        ".XXXXXXXX.",
        "XXXXXXXXXX",
        "XX..XX..XX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "X.XX..XX.X",
    };
    private static readonly string[] Pac =
    {
        "..XXXXX..",
        ".XXXXXXX.",
        "XXXXXX...",
        "XXXX.....",
        "XXXXXX...",
        ".XXXXXXX.",
        "..XXXXX..",
    };

    private Transform PixelSprite(Transform root, string[] art, Material mat, Vector3 pos, float px)
    {
        var t = Prop(root, "Sprite", pos);
        int h = art.Length, w = art[0].Length;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (art[y][x] == 'X')
                    Place("Px", cube, mat, t, new Vector3((x - w * 0.5f) * px, (h - y) * px, 0f), Vector3.one * px * 0.92f);
        return t;
    }

    private void BuildArcade()
    {
        Color[] cols = { primary, secondary, new Color(1f, 0.9f, 0.1f), new Color(0.3f, 1f, 0.3f) };
        foreach (var c in cols) Group(c);
        var dark = Lit(new Color(0.03f, 0.02f, 0.06f), 0.6f);

        // Equalizer walls: columns of pixel blocks on both sides, height jumping with the music.
        var eq = Root("Equalizer");
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 1.6f)
            {
                var col = Prop(eq, "Column", new Vector3(side * 9.5f, -0.5f, z));
                int g = (int)((z + Behind) / 1.6f) % cols.Length;
                for (int k = 0; k < 10; k++)
                    Place("Block", cube, groupMats[g], col, new Vector3(0f, 0.55f + k * 0.75f, 0f), new Vector3(0.6f, 0.6f, 1.3f));
                columns.Add(new Mover { t = col, seed = Rand(0f, 6.28f), group = g, size = Rand(0.6f, 1f) });
            }
        // A dark back wall behind the columns, with a pixel grid line.
        var wall = Root("BackWall");
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var w = Prop(wall, "Wall", new Vector3(side * 12f, 6f, z + 6f));
                Place("Body", cube, dark, w, Vector3.zero, new Vector3(0.4f, 14f, 12f));
                for (int k = 0; k < 4; k++)
                    RemixGeo.Beam(w, "Grid", cube, groupMats[(k + 1) % cols.Length], new Vector3(-side * 0.22f, -6f + k * 3.5f, -6f), new Vector3(-side * 0.22f, -6f + k * 3.5f, 6f), 0.06f);
                scrollers.Add(new Mover { t = w });
            }

        // Pixel characters floating over the road's sides, bobbing and turning.
        var sprites = Root("Sprites");
        string[][] arts = { Invader, Ghost, Pac };
        int n = 0;
        for (float z = -Behind + 8f; z < Far; z += Rand(14f, 20f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var s = PixelSprite(sprites, arts[n % 3], groupMats[n % cols.Length], new Vector3(side * Rand(4.5f, 6.5f), Rand(7f, 9.5f), z), 0.28f);
            floaters.Add(new Mover { t = s, basePos = s.localPosition, seed = Rand(0f, 6.28f), group = n % cols.Length });
        }
        // Spinning coins low along the road edges.
        accentMat = Neon(new Color(1f, 0.85f, 0.2f));
        var coins = Root("Coins");
        for (float z = -Behind; z < Far; z += 5f)
            foreach (int side in new[] { -1, 1 })
            {
                var c = Place("Coin", cyl, accentMat, coins, new Vector3(side * 7.2f, 1.4f, z), new Vector3(0.9f, 0.06f, 0.9f));
                floaters.Add(new Mover { t = c.transform, basePos = c.transform.localPosition, seed = z * 0.3f, group = -1 });
            }
        // Sky: a giant pixel moon (blocky disc) and stars.
        var moon = Mask(32, 32, (x, y) => ((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f)) < 0.2f ? 1f : 0f);
        moon.filterMode = FilterMode.Point;
        Color mc = Color.Lerp(secondary, Color.white, 0.4f); mc.a = 1f;
        glowSkyMat = SkyQuad("PixelMoon", new Vector3(80f, 70f, 330f), 45f, 45f, mc, moon, 0.9f);
        // Falling pixel confetti.
        particleMats = new[] { groupMats[0], groupMats[1], groupMats[2] };
        ParticleBox("PixelRain", 120, cube, particleMats, Vector3.one * 0.18f, new Vector2(2f, 4f));
    }

    // ---------- Candy ----------

    private void BuildCandy()
    {
        Color pink = new Color(1f, 0.45f, 0.75f), mint = new Color(0.4f, 1f, 0.8f), lemon = new Color(1f, 0.92f, 0.35f), white = new Color(1f, 0.95f, 0.97f);
        Group(pink); Group(mint); Group(lemon); Group(white);
        var stick = Lit(new Color(0.95f, 0.92f, 0.9f), 0.5f);

        var root = Root("Candy");
        int n = 0;
        for (float z = -Behind; z < Far; z += Rand(6f, 9f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            float x = side * Rand(8.5f, 12f);
            var p = Prop(root, "Treat", new Vector3(x, -0.5f, z));
            switch (n % 3)
            {
                case 0:     // lollipop: a stick and a swirl disc of rings
                {
                    float h = Rand(5f, 8f);
                    Place("Stick", cyl, stick, p, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.25f, h * 0.5f, 0.25f));
                    var disc = Prop(p, "Disc", new Vector3(0f, h + 1.4f, 0f));
                    for (int r = 0; r < 4; r++)
                    {
                        float rad = 0.4f + r * 0.38f;
                        int seg = 10 + r * 5;
                        for (int k = 0; k < seg; k++)
                        {
                            float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                            RemixGeo.Beam(disc, "Swirl", cube, groupMats[(r + (k % 2)) % 2 == 0 ? 0 : 3],
                                new Vector3(Mathf.Cos(a0) * rad, Mathf.Sin(a0) * rad, 0f), new Vector3(Mathf.Cos(a1) * rad, Mathf.Sin(a1) * rad, 0f), 0.32f);
                        }
                    }
                    floaters.Add(new Mover { t = disc, basePos = disc.localPosition, seed = Rand(0f, 6.28f), group = -2 });
                    break;
                }
                case 1:     // candy cane: striped segments up and a hook
                {
                    float h = Rand(5f, 7f);
                    Vector3 a = Vector3.zero;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 b = a + Vector3.up * h / 8f;
                        RemixGeo.Beam(p, "Cane", cube, groupMats[k % 2 == 0 ? 0 : 3], a, b, 0.45f);
                        a = b;
                    }
                    for (int k = 0; k < 6; k++)
                    {
                        float u0 = k * Mathf.PI / 6f, u1 = (k + 1) * Mathf.PI / 6f;
                        Vector3 c = new Vector3(-side * 0.8f, h, 0f);
                        RemixGeo.Beam(p, "Hook", cube, groupMats[k % 2 == 0 ? 3 : 0], c + new Vector3(side * Mathf.Cos(u0), Mathf.Sin(u0), 0f) * 0.8f,
                            c + new Vector3(side * Mathf.Cos(u1), Mathf.Sin(u1), 0f) * 0.8f, 0.45f);
                    }
                    break;
                }
                default:    // gumdrops: a cluster of glowing domes
                    for (int k = 0; k < 4; k++)
                    {
                        float s = Rand(0.9f, 1.8f);
                        var g = Place("Gum", sphere, groupMats[k % 3], p, new Vector3(Rand(-1.5f, 1.5f), s * 0.35f, Rand(-1.5f, 1.5f)), new Vector3(s, s * 0.9f, s));
                        pulsers.Add(new Mover { t = g.transform, r = g, group = k % 3, size = s });
                    }
                    break;
            }
            scrollers.Add(new Mover { t = p });
        }
        // Chocolate river edges: a brown glossy strip each side with a mint trim.
        var choco = Lit(new Color(0.22f, 0.1f, 0.05f), 0.9f);
        var riv = Root("Chocolate");
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var s = Prop(riv, "River", new Vector3(side * 5.6f, -0.48f, z + 6f));
                Place("Choc", cube, choco, s, Vector3.zero, new Vector3(4.6f, 0.06f, 12f));
                RemixGeo.Beam(s, "Trim", cube, groupMats[1], new Vector3(side * 2.3f, 0.06f, -6f), new Vector3(side * 2.3f, 0.06f, 6f), 0.14f);
                scrollers.Add(new Mover { t = s });
            }
        // Cotton-candy clouds in the sky.
        var soft = Sprite(128, 1.4f);
        for (int i = 0; i < 18; i++)
        {
            Color c = i % 2 == 0 ? pink : new Color(0.7f, 0.85f, 1f); c.a = Rand(0.5f, 0.8f);
            SkyQuad("Cloud", new Vector3(Rand(-260f, 260f), Rand(20f, 120f), Rand(250f, 360f)), Rand(40f, 90f), Rand(25f, 45f), c, soft, 0.85f);
        }
        // Sprinkles.
        particleMats = new[] { groupMats[0], groupMats[1], groupMats[2] };
        ParticleBox("Sprinkles", 160, cube, particleMats, new Vector3(0.06f, 0.24f, 0.06f), new Vector2(1.2f, 2.4f));
    }

    // ---------- NeoTokyo ----------

    private void BuildTokyo()
    {
        Color red = new Color(1f, 0.15f, 0.2f), sakura = new Color(1f, 0.55f, 0.8f), lantern = new Color(1f, 0.6f, 0.2f);
        Group(red); Group(sakura); Group(lantern); Group(secondary);
        var trunk = Lit(new Color(0.12f, 0.06f, 0.06f), 0.2f);
        var dark = Lit(new Color(0.03f, 0.03f, 0.05f), 0.6f);

        var root = Root("Tokyo");
        int n = 0;
        // Sakura trees and stone lanterns along both sides.
        for (float z = -Behind; z < Far; z += Rand(7f, 10f), n++)
            foreach (int side in new[] { -1, 1 })
            {
                var tr = Prop(root, "Sakura", new Vector3(side * Rand(8.5f, 11f), -0.5f, z + Rand(-1.5f, 1.5f)));
                Vector3 a = Vector3.zero;
                float lean = -side * Rand(0.1f, 0.3f);
                for (int k = 0; k < 4; k++) { Vector3 b = a + new Vector3(lean, 1.3f, 0f); RemixGeo.Beam(tr, "Trunk", cube, trunk, a, b, 0.35f - k * 0.05f); a = b; }
                for (int k = 0; k < 7; k++)
                {
                    float s = Rand(1.2f, 2.2f);
                    var bl = Place("Bloom", sphere, groupMats[1], tr, a + new Vector3(Rand(-1.6f, 1.6f), Rand(-0.4f, 1.2f), Rand(-1.4f, 1.4f)), Vector3.one * s);
                    pulsers.Add(new Mover { t = bl.transform, r = bl, group = 1, size = s });
                }
                scrollers.Add(new Mover { t = tr });
                if (n % 2 == 0)
                {
                    var l = Prop(root, "Lantern", new Vector3(side * 7.4f, -0.5f, z + 4f));
                    Place("Post", cube, dark, l, new Vector3(0f, 0.7f, 0f), new Vector3(0.35f, 1.4f, 0.35f));
                    var lamp = Place("Lamp", cube, groupMats[2], l, new Vector3(0f, 1.75f, 0f), new Vector3(0.6f, 0.6f, 0.6f));
                    Place("Roof", cube, dark, l, new Vector3(0f, 2.2f, 0f), new Vector3(0.95f, 0.15f, 0.95f));
                    scrollers.Add(new Mover { t = l });
                }
            }
        // Vertical kanji-like signs on tall dark blocks further out.
        n = 0;
        for (float z = -Behind; z < Far; z += Rand(9f, 13f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            float h = Rand(12f, 26f);
            var b = Prop(root, "Block", new Vector3(side * Rand(15f, 19f), -0.5f, z));
            Place("Body", cube, dark, b, new Vector3(0f, h * 0.5f, 0f), new Vector3(4f, h, 4f));
            var sign = Prop(b, "Sign", new Vector3(-side * 2.2f, Rand(3f, h * 0.5f), 0f));
            int g = n % 2 == 0 ? 0 : 3;
            for (int k = 0; k < 5; k++)
            {
                // A "glyph": a box and two strokes.
                float y = k * 1.3f;
                RemixGeo.Beam(sign, "G", cube, groupMats[g], new Vector3(0f, y, -0.4f), new Vector3(0f, y, 0.4f), 0.12f);
                RemixGeo.Beam(sign, "G", cube, groupMats[g], new Vector3(0f, y + 0.5f, 0f), new Vector3(0f, y - 0.3f, ((k & 1) == 0 ? 0.35f : -0.35f)), 0.1f);
            }
            pulsers.Add(new Mover { t = b, r = null, group = g, size = -1f });
            scrollers.Add(new Mover { t = b });
        }
        // Mount Fuji and a red moon in the sky.
        var fuji = Mask(256, 128, (x, y) => y < 1f - Mathf.Abs(x - 0.5f) * 2.2f ? (y > 0.82f - Mathf.Abs(x - 0.5f) * 2.2f ? 1f : 0.55f) : 0f);
        Color fc = Color.Lerp(secondary, Color.white, 0.2f); fc.a = 1f;
        SkyQuad("Fuji", new Vector3(-40f, 28f, 340f), 160f, 70f, fc, fuji, 0.6f);
        Color mc = red; mc.a = 1f;
        glowSkyMat = SkyQuad("Moon", new Vector3(110f, 85f, 330f), 30f, 30f, mc, Sprite(128, 0.6f), 1.1f);
        // Falling petals.
        particleMats = new[] { groupMats[1] };
        ParticleBox("Petals", 170, cube, particleMats, new Vector3(0.22f, 0.03f, 0.16f), new Vector2(0.8f, 1.6f));
    }

    // ---------- Ocean ----------

    private void BuildOcean()
    {
        Color kelp = new Color(0.2f, 1f, 0.55f), jelly = new Color(1f, 0.4f, 0.9f), fish = new Color(1f, 0.85f, 0.3f), coral = new Color(1f, 0.4f, 0.3f);
        Group(kelp); Group(jelly); Group(fish); Group(coral); Group(secondary);
        var sand = Lit(new Color(0.05f, 0.08f, 0.12f), 0.4f);
        var root = Root("Ocean");

        // Sea floor strips with coral clusters, kelp strands swaying.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var f = Prop(root, "Floor", new Vector3(side * 12f, -0.6f, z + 6f));
                Place("Sand", cube, sand, f, Vector3.zero, new Vector3(14f, 0.1f, 12f));
                for (int k = 0; k < 3; k++)
                {
                    var c = Prop(f, "Coral", new Vector3(Rand(-5f, 4f) * -side, 0f, Rand(-5f, 5f)));
                    for (int b = 0; b < 5; b++)
                    {
                        Vector3 tip = new Vector3(Rand(-1f, 1f), Rand(1.2f, 2.6f), Rand(-1f, 1f));
                        RemixGeo.Beam(c, "Branch", cube, groupMats[3], Vector3.zero, tip, 0.18f);
                        Place("Bud", sphere, groupMats[(b & 1) == 0 ? 3 : 4], c, tip, Vector3.one * 0.4f);
                    }
                }
                scrollers.Add(new Mover { t = f });
            }
        for (float z = -Behind; z < Far; z += 3.2f)
            foreach (int side in new[] { -1, 1 })
            {
                var k = Prop(root, "Kelp", new Vector3(side * Rand(7.5f, 10f), -0.5f, z + Rand(-1f, 1f)));
                int seg = rng.Next(6, 11);
                for (int i = 0; i < seg; i++)
                {
                    var s = Prop(k, "Seg", new Vector3(0f, i * 0.9f, 0f));
                    Place("Leaf", cube, groupMats[0], s, new Vector3(0f, 0.45f, 0f), new Vector3(0.12f, 0.9f, 0.5f));
                }
                floaters.Add(new Mover { t = k, basePos = k.localPosition, seed = Rand(0f, 6.28f), group = -3, size = seg });
            }
        // Jellyfish drifting over the sides: a glowing dome and tentacles, pulsing on the beat.
        for (float z = -Behind + 5f; z < Far; z += Rand(8f, 12f))
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var j = Prop(root, "Jelly", new Vector3(side * Rand(4.5f, 8f), Rand(5f, 10f), z));
            var dome = Place("Dome", sphere, groupMats[1], j, Vector3.zero, new Vector3(1.4f, 0.8f, 1.4f));
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                RemixGeo.Beam(j, "Tentacle", cube, groupMats[4], new Vector3(Mathf.Cos(a) * 0.45f, -0.2f, Mathf.Sin(a) * 0.45f), new Vector3(Mathf.Cos(a) * 0.6f, -2.2f, Mathf.Sin(a) * 0.6f), 0.06f);
            }
            floaters.Add(new Mover { t = j, r = dome, basePos = j.localPosition, seed = Rand(0f, 6.28f), group = -4 });
        }
        // A school of fish circling high over the road.
        var school = Root("School");
        for (int i = 0; i < 40; i++)
        {
            var fsh = Place("Fish", cube, groupMats[2], school, Vector3.zero, new Vector3(0.12f, 0.2f, 0.6f));
            particles.Add(new Mover { t = fsh.transform, seed = i * 0.157f, speed = Rand(0.8f, 1.2f), size = -9f, basePos = new Vector3(Rand(-1.5f, 1.5f), Rand(-1f, 1f), Rand(-1.5f, 1.5f)) });
        }
        // Light rays from the surface, a whale far off, bubbles rising.
        var ray = Mask(16, 128, (x, y) => (1f - Mathf.Abs(x - 0.5f) * 2f) * y);
        for (int i = 0; i < 8; i++)
        {
            Color c = Color.Lerp(secondary, Color.white, 0.6f); c.a = 0.35f;
            SkyQuad("Ray", new Vector3(Rand(-200f, 200f), 90f, Rand(260f, 340f)), Rand(8f, 18f), 140f, c, ray, 0.5f, Rand(-15f, 15f));
        }
        var whale = Mask(256, 96, (x, y) =>
        {
            float bx = (x - 0.45f) / 0.38f, by = (y - 0.5f) / 0.28f;
            float body = bx * bx + by * by < 1f ? 1f : 0f;
            float tail = x > 0.8f && Mathf.Abs(y - 0.5f) < (x - 0.8f) * 1.6f ? 1f : 0f;
            return Mathf.Max(body, tail);
        });
        Color wc = Color.Lerp(secondary, Color.white, 0.3f); wc.a = 0.9f;
        glowSkyMat = SkyQuad("Whale", new Vector3(-60f, 60f, 320f), 90f, 34f, wc, whale, 0.45f);
        particleMats = new[] { groupMats[4] };
        ParticleBox("Bubbles", 140, sphere, particleMats, Vector3.one * 0.16f, new Vector2(-2.6f, -1.2f));     // negative: rise
    }

    // ---------- Volcano ----------

    private void BuildVolcano()
    {
        Color lava = new Color(1f, 0.35f, 0.05f), ember = new Color(1f, 0.7f, 0.2f), magma = new Color(1f, 0.12f, 0.05f);
        Group(lava); Group(ember); Group(magma);
        rockMesh = Rock();
        var rock = Lit(new Color(0.08f, 0.06f, 0.06f), 0.15f);
        var obsidian = Lit(new Color(0.04f, 0.03f, 0.05f), 0.85f);
        var root = Root("Volcano");

        // Lava rivers on both sides of the road with glowing banks.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var r = Prop(root, "Lava", new Vector3(side * 6.2f, -0.5f, z + 6f));
                var flow = Place("Flow", cube, groupMats[0], r, Vector3.zero, new Vector3(5.6f, 0.06f, 12f));
                for (int k = 0; k < 4; k++)
                    Place("Crust", rockMesh, rock, r, new Vector3(Rand(-2.4f, 2.4f), 0.05f, Rand(-5.5f, 5.5f)), new Vector3(Rand(0.4f, 1f), 0.15f, Rand(0.4f, 1f)));
                pulsers.Add(new Mover { t = r, r = flow, group = 0, size = -1f });
                scrollers.Add(new Mover { t = r });
            }
        // Obsidian spires and boulders.
        int n = 0;
        for (float z = -Behind; z < Far; z += Rand(4f, 7f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var s = Prop(root, "Spire", new Vector3(side * Rand(10f, 16f), -0.5f, z));
            float h = Rand(4f, 14f);
            Place("Body", rockMesh, n % 3 == 0 ? obsidian : rock, s, new Vector3(0f, h * 0.45f, 0f), new Vector3(Rand(1.5f, 3f), h, Rand(1.5f, 3f)));
            RemixGeo.Beam(s, "Crack", cube, groupMats[2], new Vector3(-side * 0.8f, 0.2f, 0f), new Vector3(-side * 0.5f, h * 0.7f, Rand(-0.4f, 0.4f)), 0.08f);
            scrollers.Add(new Mover { t = s });
        }
        // Geysers: columns of glowing blobs that shoot up on the kick (group by beat).
        for (float z = -Behind + 3f; z < Far; z += 9f)
            foreach (int side in new[] { -1, 1 })
            {
                var gy = Prop(root, "Geyser", new Vector3(side * 8.4f, -0.5f, z + (side > 0 ? 4.5f : 0f)));
                for (int k = 0; k < 7; k++)
                    Place("Blob", sphere, groupMats[k < 2 ? 2 : 1], gy, new Vector3(0f, k * 0.9f, 0f), Vector3.one * (0.9f - k * 0.08f));
                columns.Add(new Mover { t = gy, seed = Rand(0f, 6.28f), group = (int)((z + Behind) / 9f) % 2, size = 1f });
            }
        // The volcano in the sky with a glowing crater and a smoke plume.
        var cone = Mask(256, 128, (x, y) => y < 0.92f - Mathf.Abs(x - 0.5f) * 1.9f ? 1f : 0f);
        Color cc = new Color(0.25f, 0.06f, 0.05f); cc.a = 1f;
        SkyQuad("Cone", new Vector3(0f, 30f, 345f), 180f, 80f, cc, cone, 0.8f);
        Color crater = magma; crater.a = 1f;
        glowSkyMat = SkyQuad("Crater", new Vector3(0f, 98f, 340f), 40f, 18f, crater, Sprite(128, 1.2f), 1.2f);
        var plume = Sprite(128, 1.6f);
        for (int i = 0; i < 10; i++)
        {
            Color c = new Color(0.25f, 0.12f, 0.12f); c.a = 0.5f;
            SkyQuad("Smoke", new Vector3(Rand(-50f, 50f), 120f + i * 14f, 338f), 40f + i * 6f, 25f + i * 3f, c, plume, 0.5f);
        }
        particleMats = new[] { groupMats[1], groupMats[0] };
        ParticleBox("Embers", 180, sphere, particleMats, Vector3.one * 0.12f, new Vector2(-2.2f, -0.8f));    // rise
    }

    // ---------- every frame ----------

    private void BuildBloomVolume()
    {
        bloomProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = bloomProfile.Add<Bloom>(true);
        bloom.intensity.Override(bloomPeak);
        bloom.scatter.Override(0.8f);
        bloom.threshold.Override(0.6f);
        var go = new GameObject("DropBloom");
        go.transform.SetParent(transform, false);
        bloomVolume = go.AddComponent<Volume>();
        bloomVolume.isGlobal = true;
        bloomVolume.priority = 50f;
        bloomVolume.sharedProfile = bloomProfile;
        bloomVolume.weight = 0f;
    }

    private void Scroll(Transform t, float delta)
    {
        var p = t.localPosition;
        p.z -= delta;
        if (p.z < -Behind) p.z += span;
        t.localPosition = p;
    }

    private void Update()
    {
        if (!built || mpb == null) return;
        float dt = Time.deltaTime;
        float delta = scrollSpeed * dt;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float kick = ClubBeat.Pulse;
        float t = ClubBeat.SongTime;
        float drop = Mathf.Clamp01((ClubBeat.Energy - 1f) / 0.6f);
        float barPulse = Mathf.Pow(1f - Mathf.Repeat(beats, 4f) / 4f, 3f);
        int beat = ClubBeat.BeatIndex;

        foreach (var s in scrollers) Scroll(s.t, delta);

        // Group lights: each group flares on its own beat of the bar; everything brighter on a drop.
        int on = groupMats.Count > 0 ? ((beat % groupMats.Count) + groupMats.Count) % groupMats.Count : 0;
        for (int g = 0; g < groupMats.Count; g++)
        {
            float level = 0.55f + 0.35f * barPulse * energy + 0.35f * drop + (g == on ? 0.8f * kick * Mathf.Max(0.5f, energy) : 0f);
            RemixGeo.SetEmission(groupMats[g], groupCols[g], Mathf.Min(MaxLevel, emission * level));
        }
        foreach (var p in pulsers)
        {
            if (p.size > 0f && p.t != null)
            {
                float s = p.size * (1f + (p.group == on % 3 ? 0.08f * kick : 0f));
                p.t.localScale = new Vector3(s, s * 0.9f, s);
            }
        }

        // Floaters.
        foreach (var f in floaters)
        {
            Scroll(f.t, delta);
            var p = f.t.localPosition;
            switch (f.group)
            {
                case -1:    // coins: spin
                    f.t.localRotation = Quaternion.Euler(90f, t * 180f + f.seed * 40f, 0f);
                    break;
                case -2:    // lollipop disc: slow spin
                    f.t.localRotation = Quaternion.Euler(0f, 0f, t * 40f + f.seed * 30f);
                    break;
                case -3:    // kelp: sway each segment a little more than the one below
                {
                    float sway = Mathf.Sin(t * 1.1f + f.seed + p.z * 0.05f);
                    for (int i = 0; i < f.t.childCount; i++)
                        f.t.GetChild(i).localRotation = Quaternion.Euler(sway * 4f * i * 0.4f, 0f, sway * 6f * i * 0.35f);
                    break;
                }
                case -4:    // jellyfish: bob and pulse on the beat
                    p.y = f.basePos.y + Mathf.Sin(t * 0.8f + f.seed) * 0.8f;
                    f.t.localPosition = p;
                    float pulse = 1f + 0.18f * kick;
                    f.t.localScale = new Vector3(pulse, 2f - pulse, pulse);
                    break;
                default:    // pixel sprites: bob and turn to face the road
                    p.y = f.basePos.y + Mathf.Sin(t * 1.5f + f.seed) * 0.5f + 0.35f * kick;
                    f.t.localPosition = p;
                    f.t.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.8f + f.seed) * 25f, 0f);
                    break;
            }
        }

        // Equalizer columns (Arcade) and geysers (Volcano).
        foreach (var c in columns)
        {
            Scroll(c.t, delta);
            if (skin == WorldSkin.Arcade)
            {
                float lvl = Mathf.Clamp01(0.25f + 0.35f * energy * c.size + 0.45f * kick * Mathf.Abs(Mathf.Sin(beats * 1.7f + c.seed)) + 0.2f * drop);
                int lit = Mathf.Max(1, Mathf.RoundToInt(lvl * c.t.childCount));
                for (int i = 0; i < c.t.childCount; i++) c.t.GetChild(i).gameObject.SetActive(i < lit);
            }
            else
            {
                bool fire = ((beat & 1) == c.group) && energy > 0.6f;
                float h = fire ? Mathf.Pow(1f - ClubBeat.BeatPhase, 0.6f) : 0.15f;
                c.t.localScale = new Vector3(1f, Mathf.Max(0.05f, h * (1f + 0.5f * drop)), 1f);
            }
        }

        // Particles.
        foreach (var q in particles)
        {
            if (q.size == -9f)          // fish school: a circling flock over the road
            {
                float a = t * 0.5f * q.speed + q.seed;
                var center = new Vector3(Mathf.Sin(t * 0.13f) * 4f, 12f + Mathf.Sin(t * 0.3f) * 1.5f, 30f + Mathf.Cos(t * 0.11f) * 8f);
                var pos = center + new Vector3(Mathf.Cos(a) * 6f, Mathf.Sin(a * 2f) * 1.2f, Mathf.Sin(a) * 6f) + q.basePos;
                q.t.localRotation = Quaternion.LookRotation(pos - q.t.localPosition + Vector3.forward * 1e-3f);
                q.t.localPosition = pos;
                continue;
            }
            var p = q.t.localPosition;
            float boxX = q.basePos.x, boxH = q.basePos.y, boxZ = q.basePos.z, y0 = q.size;
            p.y -= q.speed * dt * (1f + 0.6f * drop);
            p.x += Mathf.Sin(t * 1.2f + q.seed) * dt * 0.6f;
            p.z -= delta * 0.5f;
            if (p.y < y0) p.y += boxH;
            if (p.y > y0 + boxH) p.y -= boxH;
            if (p.z < 1f) p.z += boxZ - 1f;
            q.t.localPosition = p;
            q.t.localRotation = Quaternion.AngleAxis(t * 200f + q.seed * 60f, q.axis);
        }

        if (glowSkyMat != null) glowSkyMat.SetFloat("_Intensity", 0.8f + 0.3f * barPulse * energy + 0.4f * drop);
        if (bloomVolume != null) bloomVolume.weight = dropBloom * drop * (0.55f + 0.45f * kick);
    }
}
