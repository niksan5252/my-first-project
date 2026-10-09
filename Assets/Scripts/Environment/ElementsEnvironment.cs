using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum ElementSkin
{
    Jungle,     // neon jungle at night: giant trees, huge leaves, hanging vines, glowing mushrooms, fireflies
    Glacier,    // ice world: crystal clusters, icebergs, ice pillars, aurora in the sky, snowfall
    Desert,     // desert of the pharaohs: pyramids, obelisks with glowing glyphs, torches, dunes, a big sun, sand
    Matrix,     // inside the code: falling code columns, wireframe cubes, circuit traces, server towers
    Storm,      // the storm: floating rock islands, thunderclouds, lightning on the beat, tesla towers, rain
}

/// Video 13's scenery ("Five Elements"): five worlds, one per level, in the manner of
/// WorldsEnvironment - everything near scrolls with the tunnel and wraps, the sky pieces sit beyond
/// the fog (Custom/Starfield), a global Volume pushes the bloom up on a drop, and all motion and
/// light come from ClubBeat (the song clock), so it is on the music in Play and in a recording.
public class ElementsEnvironment : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [SerializeField] private ElementSkin skin;
    [SerializeField, Range(0f, 1f)] private float dropBloom = 0.4f;
    [SerializeField] private float bloomPeak = 5f;
    [SerializeField] private int seed = 1313;

    private const float Behind = 12f;
    private const float MaxLevel = 2.4f;

    private Color primary = new Color(0.3f, 1f, 0.4f);
    private Color secondary = new Color(1f, 0.3f, 0.8f);
    private float emission = 1.3f;
    private float scrollSpeed, span;
    private bool built;
    private System.Random rng;
    private Mesh cube, sphere, cyl, rockMesh;
    private MaterialPropertyBlock mpb;
    private readonly List<Object> owned = new List<Object>();

    private struct Mover { public Transform t; public MeshRenderer r; public Vector3 basePos, axis; public float seed, speed, size; public int group; }
    private readonly List<Mover> scrollers = new List<Mover>();        // plain scrolling props
    private readonly List<Mover> pulsers = new List<Mover>();          // scale pulse on the kick of their group
    private readonly List<Mover> floaters = new List<Mover>();         // scroll + a motion by group code
    private readonly List<Mover> particles = new List<Mover>();        // fall/rise within a box in front of the camera
    private readonly List<Mover> columns = new List<Mover>();          // code rain columns
    private readonly List<MeshRenderer[]> columnRenderers = new List<MeshRenderer[]>();
    private readonly List<Mover> bolts = new List<Mover>();            // lightning, shown for a moment on a beat
    private readonly List<Material> groupMats = new List<Material>();
    private readonly List<Color> groupCols = new List<Color>();
    private readonly List<Material> skyWaves = new List<Material>();   // aurora ribbons / storm flashes
    private Material glowSkyMat;
    private Material[] particleMats;
    private Volume bloomVolume;
    private VolumeProfile bloomProfile;
    private int lastBoltBeat = -1;
    private float boltUntil;

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
        rng = new System.Random(seed + (int)skin * 173);
        mpb = new MaterialPropertyBlock();
        cube = RemixGeo.Primitive(PrimitiveType.Cube);
        sphere = RemixGeo.Primitive(PrimitiveType.Sphere);
        cyl = RemixGeo.Primitive(PrimitiveType.Cylinder);
        rockMesh = Rock();
        switch (skin)
        {
            case ElementSkin.Jungle: BuildJungle(); break;
            case ElementSkin.Glacier: BuildGlacier(); break;
            case ElementSkin.Desert: BuildDesert(); break;
            case ElementSkin.Matrix: BuildMatrix(); break;
            case ElementSkin.Storm: BuildStorm(); break;
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

    private MeshRenderer Place(string name, Mesh mesh, Material mat, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler = default)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : transform, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
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
        return Mask(size, size, (x, y) =>
        {
            float dx = x * 2f - 1f, dy = y * 2f - 1f;
            return Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), power);
        });
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

    // Particles that live in a box in front of the camera and wrap inside it. speed > 0 falls.
    private void ParticleBox(string name, int count, Mesh mesh, Material[] mats, Vector3 scale, Vector2 speed, float x = 14f, float y0 = 0f, float y1 = 16f, float z1 = 70f, bool spin = true)
    {
        var root = Root(name);
        for (int i = 0; i < count; i++)
        {
            var p = new Vector3(Rand(-x, x), Rand(y0, y1), Rand(1f, z1));
            var mr = Place("P", mesh, mats[i % mats.Length], root, p, scale);
            particles.Add(new Mover { t = mr.transform, r = mr, basePos = new Vector3(x, y1 - y0, z1), speed = Rand(speed.x, speed.y), seed = Rand(0f, 6.28f),
                                      axis = spin ? new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), Rand(-1f, 1f)).normalized : Vector3.zero, size = y0 });
        }
    }

    // A zigzag bolt from `top` down to `bottom`, as beams on `parent`.
    private void Zigzag(Transform parent, Material mat, Vector3 top, Vector3 bottom, int steps, float jitter, float width)
    {
        Vector3 a = top;
        for (int k = 1; k <= steps; k++)
        {
            Vector3 b = Vector3.Lerp(top, bottom, k / (float)steps);
            if (k < steps) b += new Vector3(Rand(-jitter, jitter), 0f, Rand(-jitter, jitter) * 0.4f);
            RemixGeo.Beam(parent, "Bolt", cube, mat, a, b, width);
            a = b;
        }
    }

    // ---------- Jungle ----------

    private void BuildJungle()
    {
        Color leaf = new Color(0.2f, 1f, 0.35f), lime = new Color(0.75f, 1f, 0.2f), flower = new Color(1f, 0.25f, 0.75f), fire = new Color(1f, 0.9f, 0.35f);
        Group(leaf); Group(lime); Group(flower); Group(fire);
        var bark = Lit(new Color(0.08f, 0.05f, 0.04f), 0.2f);
        var canopy = Lit(new Color(0.02f, 0.07f, 0.04f), 0.3f);
        var moss = Lit(new Color(0.02f, 0.06f, 0.03f), 0.5f);
        var root = Root("Jungle");

        // Moss banks on both sides with a glowing root line.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var b = Prop(root, "Bank", new Vector3(side * 10f, -0.55f, z + 6f));
                Place("Moss", cube, moss, b, Vector3.zero, new Vector3(10f, 0.1f, 12f));
                RemixGeo.Beam(b, "Root", cube, groupMats[1], new Vector3(-side * 4.6f, 0.08f, -6f), new Vector3(-side * 4.2f, 0.08f, 6f), 0.12f);
                scrollers.Add(new Mover { t = b });
            }
        // Giant trees: a thick trunk with a dark canopy and glowing leaf fronds reaching over the road.
        int n = 0;
        for (float z = -Behind; z < Far; z += Rand(9f, 13f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var tr = Prop(root, "Tree", new Vector3(side * Rand(11f, 14f), -0.5f, z));
            float h = Rand(13f, 18f);
            Place("Trunk", cyl, bark, tr, new Vector3(0f, h * 0.5f, 0f), new Vector3(1.6f, h * 0.5f, 1.6f));
            for (int k = 0; k < 5; k++)
                Place("Canopy", sphere, canopy, tr, new Vector3(Rand(-3f, 3f), h + Rand(-1f, 2f), Rand(-3f, 3f)), new Vector3(Rand(4f, 7f), Rand(2.5f, 4f), Rand(4f, 7f)));
            // Fronds: long leaves out toward the road, tilted down.
            for (int k = 0; k < 4; k++)
            {
                var f = Place("Frond", sphere, groupMats[k % 2], tr, new Vector3(-side * 2.8f, h - 2f - k * 1.6f, Rand(-2f, 2f)),
                              new Vector3(5f, 0.12f, 1.3f), new Vector3(0f, Rand(-25f, 25f), side * Rand(12f, 25f)));
                pulsers.Add(new Mover { t = f.transform, group = k % 2, size = -1f });
            }
            scrollers.Add(new Mover { t = tr });
        }
        // Hanging vines from above the road's edges, swaying, with flowers on them.
        for (float z = -Behind; z < Far; z += 3.5f)
            foreach (int side in new[] { -1, 1 })
            {
                if (rng.NextDouble() < 0.35) continue;
                var v = Prop(root, "Vine", new Vector3(side * Rand(6.5f, 8.5f), Rand(11f, 13f), z + Rand(-1f, 1f)));
                int seg = rng.Next(5, 9);
                for (int i = 0; i < seg; i++)
                {
                    var s = Prop(v, "Seg", new Vector3(0f, -i * 0.9f, 0f));
                    Place("Leaf", cube, groupMats[0], s, new Vector3(0f, -0.45f, 0f), new Vector3(0.08f, 0.9f, 0.08f));
                    if (i % 2 == 1) Place("Bud", sphere, groupMats[2], s, new Vector3(0.12f, -0.6f, 0f), Vector3.one * 0.32f);
                }
                floaters.Add(new Mover { t = v, basePos = v.localPosition, seed = Rand(0f, 6.28f), group = -1 });
            }
        // Glowing mushrooms in clusters near the road.
        var stemMat = Lit(new Color(0.8f, 0.8f, 0.7f), 0.3f);
        for (float z = -Behind; z < Far; z += Rand(4f, 6f))
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var c = Prop(root, "Shrooms", new Vector3(side * Rand(7f, 8.5f), -0.5f, z));
            for (int k = 0; k < 3; k++)
            {
                float s = Rand(0.5f, 1.3f);
                Vector3 p = new Vector3(Rand(-0.8f, 0.8f), 0f, Rand(-0.8f, 0.8f));
                Place("Stem", cyl, stemMat, c, p + Vector3.up * s * 0.6f, new Vector3(0.15f * s, s * 0.6f, 0.15f * s));
                var cap = Place("Cap", sphere, groupMats[k == 0 ? 2 : 1], c, p + Vector3.up * s * 1.2f, new Vector3(s, s * 0.45f, s));
                pulsers.Add(new Mover { t = cap.transform, group = k == 0 ? 2 : 1, size = s });
            }
            scrollers.Add(new Mover { t = c });
        }
        // Sky: a big green moon and a dark tree line on the horizon.
        var line = Mask(256, 64, (x, y) => y < 0.35f + 0.25f * Mathf.Abs(Mathf.Sin(x * 41f)) * Mathf.Abs(Mathf.Sin(x * 7.3f + 1f)) + 0.1f * Mathf.Sin(x * 90f) ? 1f : 0f);
        SkyQuad("TreeLine", new Vector3(0f, 18f, 345f), 260f, 50f, new Color(0.02f, 0.12f, 0.06f, 1f), line, 0.6f);
        Color mc = Color.Lerp(lime, Color.white, 0.5f); mc.a = 1f;
        glowSkyMat = SkyQuad("Moon", new Vector3(-90f, 85f, 335f), 32f, 32f, mc, Sprite(128, 0.5f), 1f);
        // Fireflies drifting slowly.
        particleMats = new[] { groupMats[3], groupMats[1] };
        ParticleBox("Fireflies", 130, sphere, particleMats, Vector3.one * 0.12f, new Vector2(-0.4f, 0.4f), 14f, 0.5f, 12f);
    }

    // ---------- Glacier ----------

    private void BuildGlacier()
    {
        Color ice = new Color(0.4f, 0.9f, 1f), white = new Color(0.85f, 0.95f, 1f), aurora = new Color(0.3f, 1f, 0.6f), violet = new Color(0.7f, 0.4f, 1f);
        Group(ice); Group(white); Group(aurora); Group(violet);
        var snow = Lit(new Color(0.55f, 0.65f, 0.75f), 0.6f);
        var berg = Lit(new Color(0.25f, 0.45f, 0.6f), 0.9f);
        var root = Root("Glacier");

        // Snow banks along the road.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 6f)
            {
                var b = Place("Snow", sphere, snow, root, new Vector3(side * Rand(9.5f, 11.5f), -1.2f, z + Rand(-1f, 1f)), new Vector3(Rand(4.5f, 6.5f), Rand(3f, 4.5f), Rand(6f, 8f)));
                scrollers.Add(new Mover { t = b.transform });
            }
        // Crystal clusters: tall glowing shards fanning out of the ground.
        int n = 0;
        for (float z = -Behind; z < Far; z += Rand(5f, 8f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var c = Prop(root, "Crystals", new Vector3(side * Rand(7.5f, 9.5f), -0.5f, z));
            int g = n % 3 == 2 ? 3 : 0;
            for (int k = 0; k < 5; k++)
            {
                float h = Rand(1.5f, 4.5f);
                var s = Place("Shard", cube, groupMats[k == 0 ? 1 : g], c, new Vector3(Rand(-0.6f, 0.6f), h * 0.45f, Rand(-0.6f, 0.6f)),
                              new Vector3(0.35f, h, 0.35f), new Vector3(Rand(-25f, 25f), Rand(0f, 90f), Rand(-25f, 25f)));
                pulsers.Add(new Mover { t = s.transform, group = g, size = -1f });
            }
            scrollers.Add(new Mover { t = c });
        }
        // Icebergs and ice pillars further out.
        n = 0;
        for (float z = -Behind; z < Far; z += Rand(8f, 12f), n++)
        {
            int side = n % 2 == 0 ? 1 : -1;
            var b = Prop(root, "Berg", new Vector3(side * Rand(14f, 20f), -0.5f, z));
            float h = Rand(5f, 14f);
            Place("Body", rockMesh, berg, b, new Vector3(0f, h * 0.4f, 0f), new Vector3(Rand(4f, 7f), h, Rand(4f, 7f)));
            RemixGeo.Beam(b, "Vein", cube, groupMats[0], new Vector3(-side * 1.6f, 0.5f, 0f), new Vector3(-side * 1.2f, h * 0.75f, Rand(-1f, 1f)), 0.1f);
            if (n % 2 == 0)
            {
                var p = Prop(root, "Pillar", new Vector3(-side * 12f, -0.5f, z + 5f));
                Place("Ice", cyl, berg, p, new Vector3(0f, 5f, 0f), new Vector3(1.2f, 5f, 1.2f));
                var cap = Place("Glow", sphere, groupMats[2], p, new Vector3(0f, 10.6f, 0f), Vector3.one * 1.3f);
                pulsers.Add(new Mover { t = cap.transform, group = 2, size = 1.3f });
                scrollers.Add(new Mover { t = p });
            }
            scrollers.Add(new Mover { t = b });
        }
        // Aurora: wavy ribbons in the sky, breathing in turn.
        var wave = Mask(256, 64, (x, y) =>
        {
            float c = 0.5f + 0.25f * Mathf.Sin(x * 12f) + 0.1f * Mathf.Sin(x * 31f);
            return Mathf.Clamp01(1f - Mathf.Abs(y - c) * 4f) * (y < c ? 1f : 0.6f);
        });
        for (int i = 0; i < 5; i++)
        {
            Color c = i % 2 == 0 ? aurora : violet; c.a = 0.75f;
            skyWaves.Add(SkyQuad("Aurora", new Vector3(Rand(-120f, 120f), 70f + i * 14f, 330f + i * 4f), 180f, 40f, c, wave, 0.7f, Rand(-6f, 6f)));
        }
        Color mc = white; mc.a = 1f;
        glowSkyMat = SkyQuad("Moon", new Vector3(120f, 95f, 340f), 22f, 22f, mc, Sprite(128, 0.6f), 1f);
        var peaks = Mask(256, 96, (x, y) => y < 0.3f + 0.55f * Mathf.Abs(Mathf.Sin(x * 9f + 0.4f)) * (0.6f + 0.4f * Mathf.Sin(x * 3.1f)) ? 1f : 0f);
        SkyQuad("Peaks", new Vector3(0f, 25f, 348f), 260f, 60f, new Color(0.35f, 0.5f, 0.7f, 1f), peaks, 0.5f);
        // Snowfall.
        particleMats = new[] { groupMats[1] };
        ParticleBox("Snow", 220, sphere, particleMats, Vector3.one * 0.09f, new Vector2(0.8f, 1.8f));
    }

    // ---------- Desert ----------

    private void BuildDesert()
    {
        Color gold = new Color(1f, 0.75f, 0.2f), turq = new Color(0.1f, 0.95f, 0.85f), flame = new Color(1f, 0.45f, 0.1f);
        Group(gold); Group(turq); Group(flame);
        var sand = Lit(new Color(0.35f, 0.24f, 0.13f), 0.25f);
        var stone = Lit(new Color(0.22f, 0.16f, 0.1f), 0.35f);
        var root = Root("Desert");

        // Dunes along the road.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 7f)
            {
                var d = Place("Dune", sphere, sand, root, new Vector3(side * Rand(11f, 15f), -1.2f, z + Rand(-1f, 1f)), new Vector3(Rand(8f, 12f), Rand(2.5f, 4f), Rand(8f, 10f)));
                scrollers.Add(new Mover { t = d.transform });
            }
        // Obelisks with glowing glyph strips, torches between them.
        int n = 0;
        for (float z = -Behind; z < Far; z += 8f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                float zz = z + (side > 0 ? 4f : 0f);
                var o = Prop(root, "Obelisk", new Vector3(side * 8.2f, -0.5f, zz));
                float h = 8f;
                Place("Base", cube, stone, o, new Vector3(0f, 0.3f, 0f), new Vector3(1.8f, 0.6f, 1.8f));
                Place("Shaft", cube, stone, o, new Vector3(0f, h * 0.5f, 0f), new Vector3(1.1f, h, 1.1f));
                Place("Tip", cube, groupMats[0], o, new Vector3(0f, h + 0.35f, 0f), new Vector3(0.75f, 0.75f, 0.75f), new Vector3(45f, 0f, 45f));
                for (int k = 0; k < 6; k++)
                    RemixGeo.Beam(o, "Glyph", cube, groupMats[1], new Vector3(-side * 0.56f, 1.2f + k * 1.1f, -0.25f), new Vector3(-side * 0.56f, 1.2f + k * 1.1f, (k & 1) == 0 ? 0.25f : 0.05f), 0.12f);
                pulsers.Add(new Mover { t = o, group = 1, size = -1f });
                scrollers.Add(new Mover { t = o });
                if (n % 2 == 0)
                {
                    var t = Prop(root, "Torch", new Vector3(side * 6.9f, -0.5f, zz + 2f));
                    Place("Pole", cyl, stone, t, new Vector3(0f, 1f, 0f), new Vector3(0.18f, 1f, 0.18f));
                    Place("Bowl", cyl, stone, t, new Vector3(0f, 2.05f, 0f), new Vector3(0.6f, 0.1f, 0.6f));
                    var fl = Place("Fire", sphere, groupMats[2], t, new Vector3(0f, 2.5f, 0f), new Vector3(0.5f, 0.8f, 0.5f));
                    floaters.Add(new Mover { t = fl.transform, seed = Rand(0f, 6.28f), group = -2, size = 1f });
                    scrollers.Add(new Mover { t = t });
                }
            }
        // Pyramids out in the sand: stepped, with glowing edges.
        n = 0;
        for (float z = -Behind; z < Far; z += Rand(20f, 28f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var p = Prop(root, "Pyramid", new Vector3(side * Rand(22f, 26f), -0.5f, z));
            float size = Rand(14f, 18f);
            int steps = 8;
            for (int k = 0; k < steps; k++)
            {
                float w = size * (1f - k / (float)steps);
                Place("Step", cube, stone, p, new Vector3(0f, k * size * 0.08f + size * 0.04f, 0f), new Vector3(w, size * 0.08f, w));
            }
            float top = steps * size * 0.08f;
            foreach (var c in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) })
                RemixGeo.Beam(p, "Edge", cube, groupMats[0], c * size * 0.5f, new Vector3(0f, top, 0f), 0.18f);
            Place("Cap", cube, groupMats[0], p, new Vector3(0f, top, 0f), Vector3.one * 1.1f, new Vector3(0f, 45f, 0f));
            scrollers.Add(new Mover { t = p });
        }
        // Sky: a huge low sun with a band, distant pyramids.
        Color sc = new Color(1f, 0.6f, 0.2f, 1f);
        glowSkyMat = SkyQuad("Sun", new Vector3(0f, 45f, 345f), 70f, 70f, sc, Mask(128, 128, (x, y) =>
        {
            float dx = x * 2f - 1f, dy = y * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
            float band = (Mathf.Repeat(y * 10f, 1f) < 0.25f && y < 0.45f) ? 0f : 1f;
            return (r < 0.95f ? 1f : 0f) * band;
        }), 1f);
        var pyr = Mask(256, 96, (x, y) =>
        {
            float a = 1f - Mathf.Abs(x - 0.3f) * 3.2f, b = 0.7f - Mathf.Abs(x - 0.62f) * 3f, c = 0.45f - Mathf.Abs(x - 0.85f) * 3f;
            return y < Mathf.Max(a, Mathf.Max(b, c)) ? 1f : 0f;
        });
        SkyQuad("FarPyramids", new Vector3(30f, 22f, 335f), 220f, 55f, new Color(0.3f, 0.12f, 0.06f, 1f), pyr, 0.7f);
        // Drifting sand.
        particleMats = new[] { groupMats[0] };
        ParticleBox("Sand", 160, cube, particleMats, new Vector3(0.05f, 0.05f, 0.18f), new Vector2(0.2f, 0.6f), 14f, 0f, 6f);
    }

    // ---------- Matrix ----------

    private void BuildMatrix()
    {
        Color code = new Color(0.15f, 1f, 0.35f), bright = new Color(0.7f, 1f, 0.75f), cyan = new Color(0.1f, 0.85f, 1f);
        Group(code); Group(bright); Group(cyan);
        var dark = Lit(new Color(0.01f, 0.03f, 0.02f), 0.7f);
        var root = Root("Matrix");

        // Code walls: columns of glyph blocks on both sides, a bright head running down each.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 1.4f)
            {
                var col = Prop(root, "Code", new Vector3(side * Rand(10f, 11.5f), -0.3f, z));
                int count = 12;
                for (int k = 0; k < count; k++)
                    Place("Glyph", cube, groupMats[0], col, new Vector3(0f, k * 0.62f, 0f), new Vector3(0.12f, 0.42f, Rand(0.25f, 0.5f)));
                columns.Add(new Mover { t = col, seed = Rand(0f, 30f), speed = Rand(5f, 10f), size = count });
                columnRenderers.Add(col.GetComponentsInChildren<MeshRenderer>());
            }
        // Dark back walls behind the code.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var w = Prop(root, "Wall", new Vector3(side * 12.6f, 6f, z + 6f));
                Place("Body", cube, dark, w, Vector3.zero, new Vector3(0.4f, 14f, 12f));
                scrollers.Add(new Mover { t = w });
            }
        // Circuit traces on the ground beside the road: right-angle lines with pads.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 6f)
            {
                var c = Prop(root, "Circuit", new Vector3(side * 7f, -0.48f, z));
                float x0 = 0f, z0 = -3f;
                for (int k = 0; k < 4; k++)
                {
                    float x1 = x0 + side * Rand(0.5f, 1.5f) * ((k & 1) == 0 ? 1f : -0.5f), z1 = z0 + Rand(1f, 2f);
                    RemixGeo.Beam(c, "Trace", cube, groupMats[k % 2 == 0 ? 0 : 2], new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), 0.08f);
                    RemixGeo.Beam(c, "Trace", cube, groupMats[k % 2 == 0 ? 0 : 2], new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), 0.08f);
                    Place("Pad", cube, groupMats[1], c, new Vector3(x1, 0f, z1), new Vector3(0.25f, 0.04f, 0.25f));
                    x0 = x1; z0 = z1;
                }
                pulsers.Add(new Mover { t = c, group = side > 0 ? 0 : 2, size = -1f });
                scrollers.Add(new Mover { t = c });
            }
        // Server towers with blinking lights, further out.
        int n = 0;
        for (float z = -Behind; z < Far; z += Rand(10f, 14f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var s = Prop(root, "Server", new Vector3(side * Rand(16f, 20f), -0.5f, z));
            float h = Rand(10f, 22f);
            Place("Body", cube, dark, s, new Vector3(0f, h * 0.5f, 0f), new Vector3(3f, h, 3f));
            for (float y = 1f; y < h - 0.5f; y += 0.9f)
                Place("Led", cube, groupMats[rng.NextDouble() < 0.7 ? 0 : 2], s, new Vector3(-side * 1.52f, y, Rand(-1f, 1f)), new Vector3(0.05f, 0.12f, Rand(0.3f, 1.2f)));
            scrollers.Add(new Mover { t = s });
        }
        // Wireframe cubes floating and turning over the sides.
        for (float z = -Behind + 6f; z < Far; z += Rand(10f, 15f))
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var w = Prop(root, "Wire", new Vector3(side * Rand(5f, 7f), Rand(7f, 10f), z));
            float e = Rand(0.9f, 1.5f);
            var corners = new List<Vector3>();
            for (int i = 0; i < 8; i++) corners.Add(new Vector3((i & 1) == 0 ? -e : e, (i & 2) == 0 ? -e : e, (i & 4) == 0 ? -e : e));
            for (int i = 0; i < 8; i++)
                for (int j = i + 1; j < 8; j++)
                {
                    int d = i ^ j;
                    if (d == 1 || d == 2 || d == 4) RemixGeo.Beam(w, "Edge", cube, groupMats[2], corners[i], corners[j], 0.07f);
                }
            Place("Core", cube, groupMats[1], w, Vector3.zero, Vector3.one * e * 0.5f);
            floaters.Add(new Mover { t = w, basePos = w.localPosition, seed = Rand(0f, 6.28f), group = -3 });
        }
        // Sky: a horizon grid and a big wireframe-like ring.
        var grid = Mask(256, 128, (x, y) => (Mathf.Repeat(x * 32f, 1f) < 0.06f || Mathf.Repeat(Mathf.Pow(y, 0.5f) * 12f, 1f) < 0.08f) ? (1f - y) : 0f);
        SkyQuad("Grid", new Vector3(0f, 10f, 350f), 280f, 60f, new Color(0.1f, 0.8f, 0.3f, 0.8f), grid, 0.5f);
        glowSkyMat = SkyQuad("Ring", new Vector3(0f, 75f, 340f), 55f, 55f, new Color(0.3f, 1f, 0.5f, 1f), Mask(128, 128, (x, y) =>
        {
            float dx = x * 2f - 1f, dy = y * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
            return (Mathf.Abs(r - 0.85f) < 0.04f || Mathf.Abs(r - 0.6f) < 0.02f) ? 1f : 0f;
        }), 0.9f);
        // Falling bits.
        particleMats = new[] { groupMats[0], groupMats[1] };
        ParticleBox("Bits", 160, cube, particleMats, new Vector3(0.06f, 0.3f, 0.06f), new Vector2(4f, 8f), 14f, 0f, 16f, 70f, false);
    }

    // ---------- Storm ----------

    private void BuildStorm()
    {
        Color volt = new Color(0.45f, 0.6f, 1f), white = new Color(0.9f, 0.92f, 1f), plasma = new Color(0.8f, 0.3f, 1f);
        Group(volt); Group(white); Group(plasma);
        var rock = Lit(new Color(0.09f, 0.08f, 0.11f), 0.2f);
        var grass = Lit(new Color(0.06f, 0.12f, 0.1f), 0.3f);
        var cloud = Lit(new Color(0.07f, 0.07f, 0.11f), 0.1f);
        var metal = Lit(new Color(0.12f, 0.12f, 0.15f), 0.8f);
        var root = Root("Storm");

        // Floating rock islands, bobbing over both sides.
        int n = 0;
        for (float z = -Behind; z < Far; z += Rand(7f, 11f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var isl = Prop(root, "Island", new Vector3(side * Rand(10f, 15f), Rand(2f, 9f), z));
            float s = Rand(2.5f, 4.5f);
            Place("Top", cyl, grass, isl, Vector3.zero, new Vector3(s * 2f, 0.25f, s * 2f));
            Place("Under", rockMesh, rock, isl, new Vector3(0f, -s * 0.8f, 0f), new Vector3(s * 1.9f, s * 1.6f, s * 1.9f));
            for (int k = 0; k < 3; k++)
                RemixGeo.Beam(isl, "Crack", cube, groupMats[2], new Vector3(Rand(-s, s) * 0.6f, -0.3f, -side * s * 0.9f), new Vector3(Rand(-s, s) * 0.3f, -s * 1.4f, -side * s * 0.5f), 0.07f);
            floaters.Add(new Mover { t = isl, basePos = isl.localPosition, seed = Rand(0f, 6.28f), group = -4 });
        }
        // Tesla towers along the road: a lattice mast with a sphere that flashes on its beat.
        n = 0;
        for (float z = -Behind; z < Far; z += 10f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var t = Prop(root, "Tesla", new Vector3(side * 8f, -0.5f, z + (side > 0 ? 5f : 0f)));
                float h = 7f;
                RemixGeo.Beam(t, "Leg", cube, metal, new Vector3(-0.6f, 0f, -0.6f), new Vector3(0f, h, 0f), 0.15f);
                RemixGeo.Beam(t, "Leg", cube, metal, new Vector3(0.6f, 0f, -0.6f), new Vector3(0f, h, 0f), 0.15f);
                RemixGeo.Beam(t, "Leg", cube, metal, new Vector3(0f, 0f, 0.7f), new Vector3(0f, h, 0f), 0.15f);
                for (int k = 1; k < 4; k++) Place("Coil", cyl, groupMats[0], t, new Vector3(0f, h - k * 1.4f, 0f), new Vector3(0.9f - k * 0.1f, 0.05f, 0.9f - k * 0.1f));
                var orb = Place("Orb", sphere, groupMats[n % 2 == 0 ? 1 : 2], t, new Vector3(0f, h + 0.6f, 0f), Vector3.one * 1.1f);
                pulsers.Add(new Mover { t = orb.transform, group = n % 2 == 0 ? 1 : 2, size = 1.1f });
                scrollers.Add(new Mover { t = t });
            }
        // Thunderclouds low over the outer sides.
        for (float z = -Behind; z < Far; z += 6f)
            foreach (int side in new[] { -1, 1 })
            {
                var c = Prop(root, "Cloud", new Vector3(side * Rand(15f, 22f), Rand(13f, 17f), z));
                for (int k = 0; k < 4; k++)
                    Place("Puff", sphere, cloud, c, new Vector3(Rand(-3f, 3f), Rand(-0.8f, 0.8f), Rand(-2.5f, 2.5f)), new Vector3(Rand(4f, 7f), Rand(2f, 3.5f), Rand(4f, 6f)));
                scrollers.Add(new Mover { t = c.transform });
            }
        // Lightning bolts: zigzags from the clouds to the ground, hidden until a beat calls them.
        var boltMat = groupMats[1];
        for (float z = -Behind + 8f; z < Far; z += 9f)
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var b = Prop(root, "Lightning", new Vector3(side * Rand(10f, 18f), 0f, z));
            Zigzag(b, boltMat, new Vector3(0f, 15f, 0f), new Vector3(Rand(-2f, 2f), -0.5f, 0f), 8, 1.4f, 0.16f);
            Zigzag(b, groupMats[0], new Vector3(Rand(-1f, 1f), 9f, 0f), new Vector3(-side * Rand(2f, 4f), 3f, 0f), 4, 0.8f, 0.08f);
            b.gameObject.SetActive(false);
            bolts.Add(new Mover { t = b });
        }
        // Sky: storm cloud layers that flash, a far lightning bolt.
        var soft = Sprite(128, 1.3f);
        for (int i = 0; i < 14; i++)
        {
            Color c = new Color(0.25f, 0.25f, 0.4f, Rand(0.5f, 0.8f));
            skyWaves.Add(SkyQuad("StormCloud", new Vector3(Rand(-260f, 260f), Rand(40f, 130f), Rand(300f, 360f)), Rand(50f, 100f), Rand(25f, 45f), c, soft, 0.4f));
        }
        var far = Mask(64, 256, (x, y) =>
        {
            float c = 0.5f + 0.18f * Mathf.Sin(y * 23f) + 0.08f * Mathf.Sin(y * 61f);
            return Mathf.Abs(x - c) < 0.04f ? 1f : 0f;
        });
        glowSkyMat = SkyQuad("FarBolt", new Vector3(-70f, 60f, 330f), 20f, 80f, new Color(0.8f, 0.85f, 1f, 1f), far, 0.2f);
        // Rain: fast thin streaks.
        particleMats = new[] { groupMats[0] };
        ParticleBox("Rain", 240, cube, particleMats, new Vector3(0.025f, 0.6f, 0.025f), new Vector2(14f, 20f), 14f, 0f, 16f, 70f, false);
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
                float s = p.size * (1f + (p.group == on ? 0.15f * kick : 0f));
                p.t.localScale = new Vector3(s, skin == ElementSkin.Jungle ? s * 0.45f : s, s);
            }
        }

        foreach (var f in floaters)
        {
            if (f.group != -2) Scroll(f.t, delta);
            var p = f.t.localPosition;
            switch (f.group)
            {
                case -1:    // vines: sway, each segment a little more than the one above
                {
                    float sway = Mathf.Sin(t * 1.3f + f.seed + p.z * 0.05f);
                    for (int i = 0; i < f.t.childCount; i++)
                        f.t.GetChild(i).localRotation = Quaternion.Euler(sway * 3f * i * 0.4f, 0f, sway * 5f * i * 0.4f);
                    break;
                }
                case -2:    // torch flame: flicker, flare on the kick
                {
                    float fl = 1f + 0.15f * Mathf.Sin(t * 17f + f.seed) + 0.35f * kick;
                    f.t.localScale = new Vector3(0.5f * fl, 0.8f * fl * (1f + 0.2f * Mathf.Sin(t * 11f + f.seed)), 0.5f * fl);
                    break;
                }
                case -3:    // wireframe cubes: bob and tumble, jump a bit on the kick
                    p.y = f.basePos.y + Mathf.Sin(t * 1.2f + f.seed) * 0.6f + 0.4f * kick;
                    f.t.localPosition = p;
                    f.t.localRotation = Quaternion.Euler(t * 30f + f.seed * 40f, t * 45f + f.seed * 20f, 0f);
                    break;
                default:    // islands: slow bob and turn
                    p.y = f.basePos.y + Mathf.Sin(t * 0.6f + f.seed) * 0.9f;
                    f.t.localPosition = p;
                    f.t.localRotation = Quaternion.Euler(0f, t * 6f + f.seed * 50f, Mathf.Sin(t * 0.4f + f.seed) * 4f);
                    break;
            }
        }

        // Code rain: a bright head runs down each column, a fading tail behind it.
        if (columns.Count > 0)
        {
            Color c0 = groupCols[0], c1 = groupCols[1];
            float speedUp = 1f + 0.8f * drop;
            for (int ci = 0; ci < columns.Count; ci++)
            {
                var c = columns[ci];
                var rs = columnRenderers[ci];
                Scroll(c.t, delta);
                int count = rs.Length;
                float head = count + 4f - Mathf.Repeat(c.seed + t * c.speed * speedUp, count + 8f);
                for (int i = 0; i < count; i++)
                {
                    float d = i - head;   // above the head is the tail
                    float lvl = d < 0f ? 0.05f : d < 1f ? 2.2f : Mathf.Max(0.05f, 1.3f - d * 0.18f);
                    var col = d >= 0f && d < 1f ? c1 : c0;
                    mpb.SetColor(RemixGeo.EmissionId, col * Mathf.Min(MaxLevel, emission * lvl * (0.7f + 0.3f * energy)));
                    rs[i].SetPropertyBlock(mpb);
                }
            }
        }

        // Lightning: on every other beat with energy, one bolt flashes for a moment.
        if (bolts.Count > 0)
        {
            foreach (var b in bolts) Scroll(b.t, delta);
            if (beat != lastBoltBeat && ClubBeat.BeatPhase < 0.25f && energy > 0.6f && (beat % (drop > 0.3f ? 1 : 2)) == 0)
            {
                lastBoltBeat = beat;
                boltUntil = Time.time + 0.12f;
                int pick = rng.Next(bolts.Count), pick2 = drop > 0.3f ? rng.Next(bolts.Count) : -1;
                for (int i = 0; i < bolts.Count; i++) bolts[i].t.gameObject.SetActive(i == pick || i == pick2);
            }
            else if (Time.time > boltUntil)
                foreach (var b in bolts) if (b.t.gameObject.activeSelf) b.t.gameObject.SetActive(false);
        }

        // Particles.
        foreach (var q in particles)
        {
            var p = q.t.localPosition;
            float boxX = q.basePos.x, boxH = q.basePos.y, boxZ = q.basePos.z, y0 = q.size;
            p.y -= q.speed * dt * (1f + 0.6f * drop);
            float drift = skin == ElementSkin.Desert ? 3f : skin == ElementSkin.Storm ? -1.5f : 0.6f;
            p.x += (skin == ElementSkin.Desert ? 1f : Mathf.Sin(t * 1.2f + q.seed)) * dt * drift;
            p.z -= delta * 0.5f;
            if (p.y < y0) p.y += boxH;
            if (p.y > y0 + boxH) p.y -= boxH;
            if (p.x > boxX) p.x -= boxX * 2f;
            if (p.x < -boxX) p.x += boxX * 2f;
            if (p.z < 1f) p.z += boxZ - 1f;
            q.t.localPosition = p;
            if (q.axis != Vector3.zero) q.t.localRotation = Quaternion.AngleAxis(t * 200f + q.seed * 60f, q.axis);
        }

        // Sky.
        bool flash = skin == ElementSkin.Storm && Time.time < boltUntil;
        for (int i = 0; i < skyWaves.Count; i++)
        {
            float lvl = skin == ElementSkin.Storm
                ? 0.4f + (flash && (i % 3) == (lastBoltBeat % 3 + 3) % 3 ? 1.6f : 0f)
                : 0.45f + 0.35f * Mathf.Sin(t * 0.7f + i * 1.3f) + 0.25f * barPulse * energy + 0.3f * drop;
            skyWaves[i].SetFloat("_Intensity", Mathf.Max(0.1f, lvl));
        }
        if (glowSkyMat != null)
            glowSkyMat.SetFloat("_Intensity", skin == ElementSkin.Storm ? (flash ? 1.4f : 0.15f) : 0.8f + 0.3f * barPulse * energy + 0.4f * drop);
        if (bloomVolume != null) bloomVolume.weight = dropBloom * drop * (0.55f + 0.45f * kick);
    }
}
