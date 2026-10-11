using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum LegendSkin
{
    Jurassic,   // the Jurassic: dinosaurs walking, ferns, a volcano, pterodactyls, glowing eggs
    Magic,      // a magic academy: flying books and candles, cauldrons, glowing runes, crystals, owls
    Starship,   // inside a starship: corridors, warp windows, consoles, drones, holograms
    Rome,       // the Colosseum: stands, chariots racing alongside, statues, banners, braziers
    Viking,     // the vikings: longships with shields in a fjord, rune stones, bonfires, ravens
}

/// Video 17's scenery ("Legends"): five worlds, one per level, in the manner of ElementsEnvironment -
/// everything near scrolls with the tunnel and wraps, the sky pieces sit beyond the fog
/// (Custom/Starfield), a global Volume pushes the bloom up on a drop, and all motion and light
/// come from ClubBeat (the song clock), so it is on the music in Play and in a recording.
public class LegendsEnvironment : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [SerializeField] private LegendSkin skin;
    [SerializeField, Range(0f, 1f)] private float dropBloom = 0.4f;
    [SerializeField] private float bloomPeak = 5f;
    [SerializeField] private int seed = 1717;

    private const float Behind = 12f;
    private const float MaxLevel = 2.4f;

    private Color primary = new Color(1f, 0.6f, 0.2f);
    private Color secondary = new Color(0.2f, 0.9f, 1f);
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
    private readonly List<Mover> columns = new List<Mover>();          // steam vents: shoot up on their beat
    private readonly List<Mover> bolts = new List<Mover>();            // lightning, shown for a moment on a beat
    private readonly List<Mover> ufos = new List<Mover>();             // alien saucers crossing over the road
    private readonly List<MeshRenderer> flashers = new List<MeshRenderer>();   // camera flashes in the stands
    private readonly List<Mover> riders = new List<Mover>();           // minecarts / coaster carts riding faster than the road
    private readonly List<Material> groupMats = new List<Material>();
    private readonly List<Color> groupCols = new List<Color>();
    private readonly List<Material> skyFlash = new List<Material>();   // fireworks bursts
    private Material glowSkyMat;
    private Material[] particleMats;
    private Volume bloomVolume;
    private VolumeProfile bloomProfile;
    private int lastFlashBeat = -1, flashPick = -1;
    private float flashUntil;

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
        rng = new System.Random(seed + (int)skin * 211);
        mpb = new MaterialPropertyBlock();
        cube = RemixGeo.Primitive(PrimitiveType.Cube);
        sphere = RemixGeo.Primitive(PrimitiveType.Sphere);
        cyl = RemixGeo.Primitive(PrimitiveType.Cylinder);
        rockMesh = Rock();
        switch (skin)
        {
            case LegendSkin.Jurassic: BuildJurassic(); break;
            case LegendSkin.Magic: BuildMagic(); break;
            case LegendSkin.Starship: BuildStarship(); break;
            case LegendSkin.Rome: BuildRome(); break;
            case LegendSkin.Viking: BuildViking(); break;
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
    private Material Lit(Color c, float smooth = 0.3f, float metal = 0f)
    {
        var m = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
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

    // A gear in the YZ plane (its axle along X, facing the road): a disc, a hub and teeth.
    private Transform Gear(Transform parent, Vector3 pos, float radius, int teeth, Material body, Material trim)
    {
        var g = Prop(parent, "Gear", pos);
        Place("Disc", cyl, body, g, Vector3.zero, new Vector3(radius * 2f, 0.12f, radius * 2f), new Vector3(0f, 0f, 90f));
        Place("Hub", cyl, trim, g, Vector3.zero, new Vector3(radius * 0.5f, 0.2f, radius * 0.5f), new Vector3(0f, 0f, 90f));
        for (int k = 0; k < teeth; k++)
        {
            float a = k * Mathf.PI * 2f / teeth;
            Vector3 d = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
            var tooth = Place("Tooth", cube, body, g, d * (radius + 0.12f), new Vector3(0.12f, 0.32f, radius * 0.28f));
            tooth.transform.localRotation = Quaternion.LookRotation(Vector3.right, d);
        }
        for (int k = 0; k < 4; k++)
        {
            float a = k * Mathf.PI / 2f;
            RemixGeo.Beam(g, "Spoke", cube, trim, Vector3.zero, new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a)) * radius * 0.85f, 0.1f);
        }
        return g;
    }

    // ---------- Jurassic ----------

    // A dinosaur from blocks: a body, a neck and head, a tail and four legs (or two for a raptor).
    private Transform Dino(Transform parent, Vector3 pos, float s, Material skin, Material eye, bool longNeck, int side)
    {
        var d = Prop(parent, "Dino", pos);
        Place("Body", sphere, skin, d, new Vector3(0f, 2.2f * s, 0f), new Vector3(1.8f, 1.6f, 3.4f) * s);
        Vector3 neck0 = new Vector3(0f, 2.6f * s, 1.4f * s), head = longNeck ? new Vector3(0f, 6.5f * s, 3.4f * s) : new Vector3(0f, 3.4f * s, 2.6f * s);
        RemixGeo.Beam(d, "Neck", cyl, skin, neck0, head, (longNeck ? 0.5f : 0.8f) * s);
        Place("Head", sphere, skin, d, head, new Vector3(0.7f, 0.6f, 1.2f) * s);
        Place("Eye", sphere, eye, d, head + new Vector3(-side * 0.32f * s, 0.12f * s, 0.2f * s), Vector3.one * 0.18f * s);
        RemixGeo.Beam(d, "Tail", cyl, skin, new Vector3(0f, 2.2f * s, -1.6f * s), new Vector3(0f, 1.2f * s, -5f * s), 0.45f * s);
        foreach (var l in longNeck ? new[] { new Vector3(-0.6f, 0f, 1f), new Vector3(0.6f, 0f, 1f), new Vector3(-0.6f, 0f, -1f), new Vector3(0.6f, 0f, -1f) } : new[] { new Vector3(-0.5f, 0f, -0.3f), new Vector3(0.5f, 0f, -0.3f) })
            RemixGeo.Beam(d, "Leg", cyl, skin, new Vector3(l.x, 1.6f, l.z) * s, new Vector3(l.x, 0f, l.z) * s, 0.45f * s);
        return d;
    }

    private void BuildJurassic()
    {
        Color lava = new Color(1f, 0.4f, 0.1f), leafGlow = new Color(0.4f, 1f, 0.3f), amber = new Color(1f, 0.75f, 0.2f), teal = new Color(0.1f, 0.9f, 0.8f);
        Group(lava); Group(leafGlow); Group(amber); Group(teal);
        var dinoSkin = Lit(new Color(0.12f, 0.16f, 0.1f), 0.3f);
        var dinoSkin2 = Lit(new Color(0.2f, 0.13f, 0.08f), 0.3f);
        var ground = Lit(new Color(0.06f, 0.08f, 0.04f), 0.3f);
        var fern = Lit(new Color(0.04f, 0.14f, 0.05f), 0.3f);
        var root = Root("Jurassic");

        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var g = Prop(root, "Ground", new Vector3(side * 12f, -0.55f, z + 6f));
                Place("Soil", cube, ground, g, Vector3.zero, new Vector3(12f, 0.06f, 12f));
                scrollers.Add(new Mover { t = g });
            }
        // Ferns and glowing eggs near the road.
        for (float z = -Behind; z < Far; z += 2.6f)
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var f = Prop(root, "Fern", new Vector3(side * Rand(7.2f, 9.5f), -0.5f, z));
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                RemixGeo.Beam(f, "Frond", cube, k % 3 == 0 ? groupMats[1] : fern, Vector3.zero, new Vector3(Mathf.Cos(a) * 1.4f, 1.6f, Mathf.Sin(a) * 1.4f), 0.16f);
            }
            if (rng.NextDouble() < 0.3)
                for (int k = 0; k < 3; k++)
                {
                    var egg = Place("Egg", sphere, groupMats[k == 0 ? 2 : 3], f, new Vector3(Rand(-0.5f, 0.5f), 0.3f, Rand(-0.5f, 0.5f)), new Vector3(0.4f, 0.55f, 0.4f));
                    pulsers.Add(new Mover { t = egg.transform, group = k == 0 ? 2 : 3, size = -1f });
                }
            scrollers.Add(new Mover { t = f });
        }
        // Dinosaurs walking alongside: long necks far out, raptors nearer.
        foreach (int side in new[] { -1, 1 })
            for (int k = 0; k < 3; k++)
            {
                bool big = k != 1;
                var d = Dino(root, new Vector3(side * (big ? Rand(16f, 20f) : 11f), -0.5f, Rand(10f, Far)), big ? Rand(1.3f, 1.8f) : 0.7f, k == 2 ? dinoSkin2 : dinoSkin, groupMats[2], big, side);
                riders.Add(new Mover { t = d, speed = big ? 0.8f : -0.3f });
            }
        // Tall cycad trees.
        for (float z = -Behind; z < Far; z += Rand(9f, 13f))
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var t = Prop(root, "Cycad", new Vector3(side * Rand(12f, 14f), -0.5f, z));
            float h = Rand(6f, 10f);
            RemixGeo.Beam(t, "Trunk", cyl, dinoSkin2, Vector3.zero, Vector3.up * h, 0.7f);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                RemixGeo.Beam(t, "Leaf", cube, fern, Vector3.up * h, Vector3.up * (h - 1.5f) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3.5f, 0.35f);
            }
            scrollers.Add(new Mover { t = t });
        }
        // Pterodactyls overhead; eruption bolts of lava on the drops.
        for (int i = 0; i < 3; i++)
        {
            var p = Prop(root, "Ptero", Vector3.zero);
            RemixGeo.Beam(p, "Wing", cube, dinoSkin2, new Vector3(-2f, 0.4f, -0.3f), Vector3.zero, 0.15f);
            RemixGeo.Beam(p, "Wing", cube, dinoSkin2, new Vector3(2f, 0.4f, -0.3f), Vector3.zero, 0.15f);
            RemixGeo.Beam(p, "Head", cube, dinoSkin2, Vector3.zero, new Vector3(0f, 0f, 1.2f), 0.25f);
            ufos.Add(new Mover { t = p, seed = i * 2.4f, speed = 0.1f + i * 0.04f });
        }
        for (float z = -Behind + 6f; z < Far; z += 12f)
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var b = Prop(root, "Lava", new Vector3(side * Rand(12f, 18f), 0f, z));
            for (int k = 0; k < 8; k++)
                Place("Blob", sphere, groupMats[0], b, new Vector3(Rand(-0.6f, 0.6f), k * 1.6f, 0f), Vector3.one * (1.2f - k * 0.1f));
            b.gameObject.SetActive(false);
            bolts.Add(new Mover { t = b });
        }
        var volcano = Mask(256, 128, (x, y) => y < 0.9f - Mathf.Abs(x - 0.5f) * 1.8f ? 1f : 0f);
        SkyQuad("Volcano", new Vector3(40f, 30f, 345f), 170f, 80f, new Color(0.12f, 0.06f, 0.05f, 1f), volcano, 0.8f);
        glowSkyMat = SkyQuad("Crater", new Vector3(40f, 100f, 340f), 35f, 18f, new Color(1f, 0.35f, 0.1f, 1f), Sprite(128, 1.2f), 1.2f);
        particleMats = new[] { groupMats[0], groupMats[2] };
        ParticleBox("Ash", 120, sphere, particleMats, Vector3.one * 0.07f, new Vector2(-1f, -0.3f));
    }

    // ---------- Magic academy ----------

    private void BuildMagic()
    {
        Color arcane = new Color(0.55f, 0.35f, 1f), candle = new Color(1f, 0.75f, 0.35f), potion = new Color(0.3f, 1f, 0.5f), rune = new Color(0.2f, 0.85f, 1f);
        Group(arcane); Group(candle); Group(potion); Group(rune);
        var stone = Lit(new Color(0.12f, 0.1f, 0.15f), 0.3f);
        var wood = Lit(new Color(0.2f, 0.12f, 0.07f), 0.35f);
        var iron = Lit(new Color(0.08f, 0.08f, 0.09f), 0.6f, 0.7f);
        var paper = Lit(new Color(0.7f, 0.65f, 0.55f), 0.2f);
        var root = Root("Magic");

        // Bookshelves along the walls, gothic windows above them.
        int n = 0;
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 6f, n++)
            {
                var s = Prop(root, "Shelf", new Vector3(side * 11.5f, -0.5f, z + 3f));
                Place("Case", cube, wood, s, new Vector3(0f, 3.5f, 0f), new Vector3(1.2f, 7f, 5.8f));
                for (int row = 0; row < 5; row++)
                    for (int k = 0; k < 7; k++)
                        Place("Book", cube, k % 4 == 0 ? groupMats[(row + k) % 4] : wood, s, new Vector3(-side * 0.5f, 0.7f + row * 1.35f, -2.5f + k * 0.8f), new Vector3(0.3f, Rand(0.8f, 1.1f), 0.6f));
                if (n % 2 == 0)
                {
                    var win = Place("Window", cube, groupMats[3], s, new Vector3(-side * 0.6f, 9.5f, 0f), new Vector3(0.05f, 3f, 1.6f));
                    pulsers.Add(new Mover { t = win.transform, group = 3, size = -1f });
                    RemixGeo.Beam(s, "Arch", cube, stone, new Vector3(-side * 0.6f, 11f, -0.8f), new Vector3(-side * 0.6f, 11.6f, 0f), 0.2f);
                    RemixGeo.Beam(s, "Arch", cube, stone, new Vector3(-side * 0.6f, 11f, 0.8f), new Vector3(-side * 0.6f, 11.6f, 0f), 0.2f);
                }
                scrollers.Add(new Mover { t = s });
            }
        // Cauldrons bubbling on fires, rune circles glowing on the floor.
        n = 0;
        for (float z = -Behind; z < Far; z += 7f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var c = Prop(root, "Cauldron", new Vector3(side * 8f, -0.5f, z + (side > 0 ? 3.5f : 0f)));
                Place("Pot", sphere, iron, c, new Vector3(0f, 0.8f, 0f), new Vector3(1.5f, 1.2f, 1.5f));
                var brew = Place("Brew", cyl, groupMats[n % 2 == 0 ? 2 : 0], c, new Vector3(0f, 1.25f, 0f), new Vector3(1.2f, 0.05f, 1.2f));
                pulsers.Add(new Mover { t = brew.transform, group = n % 2 == 0 ? 2 : 0, size = -1f });
                var fl = Place("Fire", sphere, groupMats[1], c, new Vector3(0f, 0.15f, 0f), new Vector3(0.5f, 0.5f, 0.5f));
                floaters.Add(new Mover { t = fl.transform, seed = Rand(0f, 6.28f), group = -5 });
                for (int k = 0; k < 12; k++)
                {
                    float a = k * Mathf.PI / 6f;
                    Place("Rune", cube, groupMats[3], c, new Vector3(Mathf.Cos(a) * 1.6f, -0.02f, Mathf.Sin(a) * 1.6f), new Vector3(0.25f, 0.02f, 0.08f), new Vector3(0f, -a * Mathf.Rad2Deg, 0f));
                }
                scrollers.Add(new Mover { t = c });
            }
        // Flying books flapping over the road; floating candles bobbing.
        for (int i = 0; i < 4; i++)
        {
            var b = Prop(root, "FlyingBook", Vector3.zero);
            RemixGeo.Beam(b, "Cover", cube, groupMats[i % 4], new Vector3(-0.6f, 0.25f, 0f), Vector3.zero, 0.5f);
            RemixGeo.Beam(b, "Cover", cube, groupMats[i % 4], new Vector3(0.6f, 0.25f, 0f), Vector3.zero, 0.5f);
            Place("Pages", cube, paper, b, new Vector3(0f, 0.05f, 0f), new Vector3(0.9f, 0.1f, 0.5f));
            ufos.Add(new Mover { t = b, seed = i * 1.7f, speed = 0.14f + i * 0.03f });
        }
        for (float z = -Behind; z < Far; z += 2.5f)
        {
            var c = Prop(root, "Candle", new Vector3(Rand(-9f, 9f), Rand(8f, 12f), z));
            Place("Wax", cyl, paper, c, Vector3.zero, new Vector3(0.18f, 0.35f, 0.18f));
            var fl = Place("Flame", sphere, groupMats[1], c, new Vector3(0f, 0.45f, 0f), new Vector3(0.12f, 0.22f, 0.12f));
            scrollers.Add(new Mover { t = c });
        }
        // Spell flashes on the drops: rune columns of light.
        for (float z = -Behind + 6f; z < Far; z += 12f)
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var b = Prop(root, "Spell", new Vector3(side * Rand(9f, 14f), 0f, z));
            Zigzag(b, groupMats[0], new Vector3(0f, 15f, 0f), new Vector3(0f, -0.5f, 0f), 6, 0.6f, 0.2f);
            b.gameObject.SetActive(false);
            bolts.Add(new Mover { t = b });
        }
        glowSkyMat = SkyQuad("Moon", new Vector3(80f, 85f, 340f), 26f, 26f, new Color(0.85f, 0.85f, 1f, 1f), Sprite(128, 0.5f), 1f);
        var stars = Mask(256, 128, (x, y) => Mathf.Sin(x * 913f + y * 337f) > 0.996f ? 1f : 0f);
        SkyQuad("Stars", new Vector3(0f, 90f, 352f), 280f, 120f, new Color(0.8f, 0.85f, 1f, 1f), stars, 0.7f);
        particleMats = new[] { groupMats[0], groupMats[3] };
        ParticleBox("Motes", 140, sphere, particleMats, Vector3.one * 0.07f, new Vector2(-0.5f, 0.5f), 14f, 0.5f, 12f);
    }

    // ---------- Starship ----------

    private void BuildStarship()
    {
        Color cyan = new Color(0.1f, 0.85f, 1f), orange = new Color(1f, 0.55f, 0.1f), white = new Color(0.85f, 0.95f, 1f), red = new Color(1f, 0.15f, 0.2f);
        Group(cyan); Group(orange); Group(white); Group(red);
        var hull = Lit(new Color(0.16f, 0.17f, 0.2f), 0.75f, 0.7f);
        var panel = Lit(new Color(0.08f, 0.08f, 0.1f), 0.6f);
        var root = Root("Starship");

        // Corridor walls of panels, light strips, windows onto warp space.
        int n = 0;
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 6f, n++)
            {
                var w = Prop(root, "Wall", new Vector3(side * 10f, 4f, z + 3f));
                Place("Panel", cube, hull, w, Vector3.zero, new Vector3(0.4f, 10f, 5.9f));
                RemixGeo.Beam(w, "Strip", cube, groupMats[0], new Vector3(-side * 0.22f, -3.6f, -2.9f), new Vector3(-side * 0.22f, -3.6f, 2.9f), 0.1f);
                RemixGeo.Beam(w, "Strip", cube, groupMats[n % 3 == 0 ? 1 : 0], new Vector3(-side * 0.22f, 4.4f, -2.9f), new Vector3(-side * 0.22f, 4.4f, 2.9f), 0.1f);
                if (n % 2 == 0)
                {
                    Place("Window", cube, panel, w, new Vector3(-side * 0.21f, 0.8f, 0f), new Vector3(0.05f, 3.2f, 4f));
                    for (int k = 0; k < 6; k++)
                        Place("Warp", cube, groupMats[2], w, new Vector3(-side * 0.24f, Rand(-0.6f, 2.2f), Rand(-1.8f, 1.8f)), new Vector3(0.02f, 0.04f, Rand(0.6f, 1.8f)));
                }
                else
                {
                    for (int k = 0; k < 8; k++)
                    {
                        var b = Place("Button", cube, groupMats[k % 4], w, new Vector3(-side * 0.22f, -1.2f + (k / 4) * 0.5f, -1.2f + (k % 4) * 0.8f), new Vector3(0.04f, 0.25f, 0.5f));
                        flashers.Add(b);
                    }
                }
                scrollers.Add(new Mover { t = w });
            }
        // Ribs over the corridor and floor grates beside the road.
        for (float z = -Behind; z < Far; z += 6f)
        {
            var r = Prop(root, "Rib", new Vector3(0f, 0f, z));
            RemixGeo.Beam(r, "Up", cube, hull, new Vector3(-9.5f, -0.5f, 0f), new Vector3(-8f, 9f, 0f), 0.6f);
            RemixGeo.Beam(r, "Up", cube, hull, new Vector3(9.5f, -0.5f, 0f), new Vector3(8f, 9f, 0f), 0.6f);
            RemixGeo.Beam(r, "Top", cube, hull, new Vector3(-8f, 9f, 0f), new Vector3(8f, 9f, 0f), 0.6f);
            RemixGeo.Beam(r, "Light", cube, groupMats[2], new Vector3(-3f, 8.65f, 0f), new Vector3(3f, 8.65f, 0f), 0.1f);
            scrollers.Add(new Mover { t = r });
        }
        // Drones patrolling; hologram globes over pedestals.
        for (int i = 0; i < 3; i++)
        {
            var d = Prop(root, "Drone", Vector3.zero);
            Place("Body", sphere, hull, d, Vector3.zero, new Vector3(1f, 0.5f, 1f));
            Place("Eye", sphere, groupMats[3], d, new Vector3(0f, 0f, 0.45f), Vector3.one * 0.25f);
            foreach (var p in new[] { new Vector3(-0.7f, 0.1f, 0f), new Vector3(0.7f, 0.1f, 0f) })
                Place("Rotor", cyl, groupMats[0], d, p, new Vector3(0.6f, 0.02f, 0.6f));
            ufos.Add(new Mover { t = d, seed = i * 2.1f, speed = 0.2f + i * 0.05f });
        }
        for (float z = -Behind + 4f; z < Far; z += 14f)
            foreach (int side in new[] { -1, 1 })
            {
                var h = Prop(root, "Holo", new Vector3(side * 7.8f, -0.5f, z + (side > 0 ? 7f : 0f)));
                Place("Pedestal", cyl, hull, h, new Vector3(0f, 0.5f, 0f), new Vector3(1f, 0.5f, 1f));
                var globe = Prop(h, "Globe", new Vector3(0f, 2.2f, 0f));
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 6f;
                    for (int s = 0; s < 8; s++)
                    {
                        float b0 = s * Mathf.PI / 4f, b1 = (s + 1) * Mathf.PI / 4f;
                        RemixGeo.Beam(globe, "Line", cube, groupMats[0], new Vector3(Mathf.Cos(b0) * Mathf.Cos(a), Mathf.Sin(b0), Mathf.Cos(b0) * Mathf.Sin(a)) * 0.9f, new Vector3(Mathf.Cos(b1) * Mathf.Cos(a), Mathf.Sin(b1), Mathf.Cos(b1) * Mathf.Sin(a)) * 0.9f, 0.04f);
                    }
                }
                floaters.Add(new Mover { t = globe, group = -8, speed = 50f });
                scrollers.Add(new Mover { t = h });
            }
        // Laser shots on the drops.
        for (float z = -Behind + 6f; z < Far; z += 10f)
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var b = Prop(root, "Laser", new Vector3(side * 8.5f, 0f, z));
            RemixGeo.Beam(b, "Shot", cube, groupMats[3], new Vector3(0f, 6f, -8f), new Vector3(0f, 6f, 8f), 0.15f);
            b.gameObject.SetActive(false);
            bolts.Add(new Mover { t = b });
        }
        glowSkyMat = SkyQuad("Warp", new Vector3(0f, 20f, 345f), 80f, 50f, new Color(0.6f, 0.85f, 1f, 0.9f), Mask(128, 128, (x, y) =>
        {
            float dx = x * 2f - 1f, dy = y * 2f - 1f, a = Mathf.Atan2(dy, dx), r = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 12f)), 30f) * Mathf.Clamp01(r * 1.4f) * (r < 1f ? 1f : 0f) + Mathf.Clamp01(1f - r * 5f);
        }), 0.9f);
        particleMats = new[] { groupMats[2], groupMats[0] };
        ParticleBox("WarpStreaks", 110, cube, particleMats, new Vector3(0.025f, 0.025f, 2.5f), new Vector2(-0.1f, 0.1f), 13f, 0.5f, 9f, 70f, false);
    }

    // ---------- Rome ----------

    private void BuildRome()
    {
        Color gold = new Color(1f, 0.78f, 0.25f), crimson = new Color(0.9f, 0.1f, 0.15f), fire = new Color(1f, 0.5f, 0.12f), purple = new Color(0.6f, 0.2f, 0.8f);
        Group(gold); Group(crimson); Group(fire); Group(purple);
        var marble = Lit(new Color(0.6f, 0.57f, 0.52f), 0.4f);
        var sandy = Lit(new Color(0.42f, 0.33f, 0.22f), 0.2f);
        var bronze = Lit(new Color(0.45f, 0.3f, 0.15f), 0.6f, 0.8f);
        var root = Root("Rome");

        // Arena sand and the chariot track beside the road.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var a = Prop(root, "Sand", new Vector3(side * 11f, -0.55f, z + 6f));
                Place("Ground", cube, sandy, a, Vector3.zero, new Vector3(9f, 0.06f, 12f));
                scrollers.Add(new Mover { t = a });
            }
        // The Colosseum's arcades: tiers of arches on both sides.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var w = Prop(root, "Arcade", new Vector3(side * 17f, -0.5f, z + 6f));
                for (int tier = 0; tier < 3; tier++)
                {
                    float y = tier * 4.5f;
                    Place("Floor", cube, marble, w, new Vector3(0f, y + 4.2f, 0f), new Vector3(2f, 0.6f, 12f));
                    for (int k = 0; k < 4; k++)
                    {
                        float zz = -4.5f + k * 3f;
                        Place("Pier", cube, marble, w, new Vector3(0f, y + 2f, zz - 1.2f), new Vector3(2f, 4f, 0.6f));
                        var glow = Place("Arch", cube, tier == 1 ? groupMats[2] : groupMats[0], w, new Vector3(-side * 0.9f, y + 3.6f, zz), new Vector3(0.05f, 0.25f, 1.6f));
                        if (tier == 1) pulsers.Add(new Mover { t = glow.transform, group = 2, size = -1f });
                    }
                }
                var banner = Prop(w, "Banner", new Vector3(-side * 1.05f, 13.4f, 0f));
                Place("Cloth", cube, groupMats[1], banner, new Vector3(0f, -1.5f, 0f), new Vector3(0.05f, 3f, 1.4f));
                Place("Eagle", sphere, groupMats[0], banner, new Vector3(-side * 0.04f, -1.2f, 0f), new Vector3(0.05f, 0.6f, 0.6f));
                floaters.Add(new Mover { t = banner, seed = Rand(0f, 6.28f), group = -6 });
                scrollers.Add(new Mover { t = w });
            }
        // Statues and braziers by the road.
        int n = 0;
        for (float z = -Behind; z < Far; z += 6f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                float zz = z + (side > 0 ? 3f : 0f);
                var p = Prop(root, n % 2 == 0 ? "Statue" : "Brazier", new Vector3(side * 7.6f, -0.5f, zz));
                Place("Plinth", cube, marble, p, new Vector3(0f, 0.6f, 0f), new Vector3(1.2f, 1.2f, 1.2f));
                if (n % 2 == 0)
                {
                    Place("Body", cube, marble, p, new Vector3(0f, 2.2f, 0f), new Vector3(0.7f, 2f, 0.45f));
                    Place("Head", sphere, marble, p, new Vector3(0f, 3.5f, 0f), Vector3.one * 0.5f);
                    Place("Laurel", cyl, groupMats[0], p, new Vector3(0f, 3.62f, 0f), new Vector3(0.55f, 0.04f, 0.55f));
                    RemixGeo.Beam(p, "Spear", cube, bronze, new Vector3(-side * 0.5f, 1.2f, 0f), new Vector3(-side * 0.5f, 4.6f, 0f), 0.08f);
                }
                else
                {
                    Place("Bowl", cyl, bronze, p, new Vector3(0f, 1.35f, 0f), new Vector3(0.9f, 0.15f, 0.9f));
                    var fl = Place("Fire", sphere, groupMats[2], p, new Vector3(0f, 1.8f, 0f), new Vector3(0.5f, 0.8f, 0.5f));
                    floaters.Add(new Mover { t = fl.transform, seed = Rand(0f, 6.28f), group = -5 });
                }
                scrollers.Add(new Mover { t = p });
            }
        // Chariots racing alongside.
        foreach (int side in new[] { -1, 1 })
            for (int k = 0; k < 2; k++)
            {
                var ch = Prop(root, "Chariot", new Vector3(side * 11f, -0.3f, Rand(10f, Far)));
                Place("Car", cube, bronze, ch, new Vector3(0f, 0.8f, 0f), new Vector3(1.4f, 1f, 1.2f));
                Place("Trim", cube, groupMats[0], ch, new Vector3(0f, 1.25f, 0.55f), new Vector3(1.45f, 0.1f, 0.1f));
                foreach (float wx in new[] { -0.8f, 0.8f }) Place("Wheel", cyl, bronze, ch, new Vector3(wx, 0.55f, -0.1f), new Vector3(1.1f, 0.08f, 1.1f), new Vector3(0f, 0f, 90f));
                for (int h = 0; h < 2; h++)
                {
                    float hx = h == 0 ? -0.4f : 0.4f;
                    Place("Horse", cube, k == 0 ? marble : sandy, ch, new Vector3(hx, 1.2f, 2.4f), new Vector3(0.45f, 0.8f, 1.8f));
                    RemixGeo.Beam(ch, "Neck", cube, k == 0 ? marble : sandy, new Vector3(hx, 1.4f, 3.1f), new Vector3(hx, 2.2f, 3.6f), 0.3f);
                }
                riders.Add(new Mover { t = ch, speed = k == 0 ? -0.7f : 0.4f });
            }
        glowSkyMat = SkyQuad("Sun", new Vector3(-50f, 45f, 345f), 50f, 50f, new Color(1f, 0.65f, 0.3f, 1f), Sprite(128, 0.45f), 1.1f);
        SkyQuad("Dust", new Vector3(0f, 20f, 350f), 260f, 50f, new Color(1f, 0.6f, 0.35f, 0.45f), Sprite(128, 1.5f), 0.6f);
        particleMats = new[] { groupMats[0] };
        ParticleBox("Petals", 110, cube, particleMats, new Vector3(0.12f, 0.02f, 0.08f), new Vector2(0.6f, 1.4f));
    }

    // ---------- Viking ----------

    private void BuildViking()
    {
        Color fire = new Color(1f, 0.5f, 0.12f), ice = new Color(0.45f, 0.8f, 1f), rune = new Color(0.3f, 1f, 0.75f), red = new Color(0.9f, 0.15f, 0.12f);
        Group(fire); Group(ice); Group(rune); Group(red);
        var wood = Lit(new Color(0.18f, 0.1f, 0.05f), 0.35f);
        var rock = Lit(new Color(0.15f, 0.16f, 0.18f), 0.25f);
        var water = Lit(new Color(0.02f, 0.05f, 0.08f), 0.95f, 0.2f);
        var snow = Lit(new Color(0.6f, 0.65f, 0.72f), 0.4f);
        var root = Root("Viking");

        // The fjord water on both sides; rune stones and bonfires on the shore by the road.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var w = Prop(root, "Fjord", new Vector3(side * 16f, -0.9f, z + 6f));
                Place("Water", cube, water, w, Vector3.zero, new Vector3(18f, 0.05f, 12f));
                scrollers.Add(new Mover { t = w });
            }
        int n = 0;
        for (float z = -Behind; z < Far; z += 6f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, n % 2 == 0 ? "RuneStone" : "Bonfire", new Vector3(side * 8f, -0.5f, z + (side > 0 ? 3f : 0f)));
                if (n % 2 == 0)
                {
                    Place("Stone", rockMesh, rock, p, new Vector3(0f, 1.6f, 0f), new Vector3(1.2f, 3.2f, 0.8f));
                    for (int k = 0; k < 4; k++)
                    {
                        var r = Place("Rune", cube, groupMats[2], p, new Vector3(-side * 0.42f, 0.8f + k * 0.6f, Rand(-0.2f, 0.2f)), new Vector3(0.04f, 0.35f, 0.08f), new Vector3(Rand(-30f, 30f), 0f, 0f));
                        pulsers.Add(new Mover { t = r.transform, group = 2, size = -1f });
                    }
                }
                else
                {
                    for (int k = 0; k < 5; k++)
                    {
                        float a = k * Mathf.PI * 2f / 5f;
                        RemixGeo.Beam(p, "Log", cyl, wood, new Vector3(Mathf.Cos(a) * 0.7f, 0f, Mathf.Sin(a) * 0.7f), new Vector3(0f, 1f, 0f), 0.18f);
                    }
                    var fl = Place("Fire", sphere, groupMats[0], p, new Vector3(0f, 1f, 0f), new Vector3(0.6f, 1f, 0.6f));
                    floaters.Add(new Mover { t = fl.transform, seed = Rand(0f, 6.28f), group = -5 });
                }
                scrollers.Add(new Mover { t = p });
            }
        // Longships rocking on the fjord: a hull, shields along the side, a striped sail, a dragon head.
        n = 0;
        for (float z = -Behind; z < Far; z += Rand(14f, 20f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var ship = Prop(root, "Longship", new Vector3(side * Rand(15f, 19f), -0.7f, z));
            Place("Hull", sphere, wood, ship, new Vector3(0f, 0.6f, 0f), new Vector3(2.6f, 1.4f, 12f));
            RemixGeo.Beam(ship, "Prow", cube, wood, new Vector3(0f, 0.8f, 5.6f), new Vector3(0f, 3.4f, 7f), 0.4f);
            Place("Dragon", sphere, groupMats[3], ship, new Vector3(0f, 3.6f, 7.2f), new Vector3(0.5f, 0.6f, 0.9f));
            for (int k = 0; k < 7; k++)
                Place("Shield", cyl, groupMats[k % 2 == 0 ? 3 : 1], ship, new Vector3(-side * 1.25f, 1.1f, -4.2f + k * 1.4f), new Vector3(0.9f, 0.05f, 0.9f), new Vector3(0f, 0f, 90f));
            RemixGeo.Beam(ship, "Mast", cyl, wood, new Vector3(0f, 0.8f, 0f), new Vector3(0f, 8f, 0f), 0.25f);
            for (int k = 0; k < 5; k++)
                Place("Sail", cube, k % 2 == 0 ? groupMats[3] : snow, ship, new Vector3(0f, 4.2f + k * 0.7f, 0f), new Vector3(0.06f, 0.7f, 4.2f));
            floaters.Add(new Mover { t = ship, basePos = ship.localPosition, seed = Rand(0f, 6.28f), group = -7 });
        }
        // Snowy mountains rising beyond the fjord; ravens overhead.
        for (float z = -Behind; z < Far; z += 9f)
            foreach (int side in new[] { -1, 1 })
            {
                var m = Prop(root, "Mountain", new Vector3(side * Rand(26f, 32f), -0.5f, z));
                float h = Rand(10f, 18f);
                Place("Rock", rockMesh, rock, m, new Vector3(0f, h * 0.4f, 0f), new Vector3(Rand(8f, 12f), h, Rand(7f, 10f)));
                Place("Snow", rockMesh, snow, m, new Vector3(0f, h * 0.82f, 0f), new Vector3(Rand(3f, 4.5f), h * 0.3f, Rand(3f, 4.5f)));
                scrollers.Add(new Mover { t = m });
            }
        for (int i = 0; i < 3; i++)
        {
            var r = Prop(root, "Raven", Vector3.zero);
            RemixGeo.Beam(r, "Wing", cube, rock, new Vector3(-1f, 0.3f, 0f), Vector3.zero, 0.12f);
            RemixGeo.Beam(r, "Wing", cube, rock, new Vector3(1f, 0.3f, 0f), Vector3.zero, 0.12f);
            Place("Body", sphere, rock, r, Vector3.zero, new Vector3(0.3f, 0.3f, 0.8f));
            ufos.Add(new Mover { t = r, seed = i * 2.2f, speed = 0.12f + i * 0.04f });
        }
        // Thor's lightning on the drops; the northern lights.
        for (float z = -Behind + 6f; z < Far; z += 12f)
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var b = Prop(root, "Thunder", new Vector3(side * Rand(11f, 18f), 0f, z));
            Zigzag(b, groupMats[1], new Vector3(0f, 17f, 0f), new Vector3(Rand(-2f, 2f), -0.5f, 0f), 8, 1.3f, 0.18f);
            b.gameObject.SetActive(false);
            bolts.Add(new Mover { t = b });
        }
        var wave = Mask(256, 64, (x, y) =>
        {
            float c = 0.5f + 0.25f * Mathf.Sin(x * 12f) + 0.1f * Mathf.Sin(x * 31f);
            return Mathf.Clamp01(1f - Mathf.Abs(y - c) * 4f) * (y < c ? 1f : 0.6f);
        });
        for (int i = 0; i < 3; i++)
            SkyQuad("Aurora", new Vector3(Rand(-100f, 100f), 75f + i * 14f, 335f), 180f, 40f, new Color(0.3f, 1f, 0.6f, 0.65f), wave, 0.6f, Rand(-6f, 6f));
        glowSkyMat = SkyQuad("Moon", new Vector3(110f, 95f, 340f), 22f, 22f, new Color(0.9f, 0.95f, 1f, 1f), Sprite(128, 0.55f), 1f);
        particleMats = new[] { groupMats[1] };
        ParticleBox("Snow", 180, sphere, particleMats, Vector3.one * 0.08f, new Vector2(0.8f, 1.8f));
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
        if (p.z > Far + 8f) p.z -= span;
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

        int on = groupMats.Count > 0 ? ((beat % groupMats.Count) + groupMats.Count) % groupMats.Count : 0;
        for (int g = 0; g < groupMats.Count; g++)
        {
            float level = 0.55f + 0.35f * barPulse * energy + 0.35f * drop + (g == on ? 0.8f * kick * Mathf.Max(0.5f, energy) : 0f);
            RemixGeo.SetEmission(groupMats[g], groupCols[g], Mathf.Min(MaxLevel, emission * level));
        }
        foreach (var p in pulsers)
            if (p.size > 0f && p.t != null)
                p.t.localScale = Vector3.one * p.size * (1f + (p.group == on ? 0.15f * kick : 0f));

        foreach (var f in floaters)
        {
            switch (f.group)
            {
                case -1:    // windmill wheel: turn about X
                    f.t.localRotation = Quaternion.Euler(t * f.speed * (1f + drop) + f.seed * 50f, 0f, 0f);
                    break;
                case -6:    // tentacles: scroll and sway, each segment a little more
                {
                    Scroll(f.t, delta);
                    float sway = Mathf.Sin(t * 1.6f + f.seed);
                    for (int i = 0; i < f.t.childCount; i++)
                        f.t.GetChild(i).localRotation = Quaternion.Euler(sway * 5f * i * 0.4f, 0f, Mathf.Cos(t * 1.3f + f.seed) * 6f * i * 0.4f);
                    break;
                }
                case -7:    // ships: scroll, rock and bob
                {
                    Scroll(f.t, delta);
                    var p = f.t.localPosition;
                    p.y = f.basePos.y + Mathf.Sin(t * 0.9f + f.seed) * 0.35f;
                    f.t.localPosition = p;
                    f.t.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.7f + f.seed) * 2f, 0f, Mathf.Sin(t * 0.9f + f.seed) * 6f);
                    break;
                }
                case -8:    // lighthouse beam: sweep round
                    f.t.localRotation = Quaternion.Euler(0f, t * f.speed, 0f);
                    break;
                case -9:    // waterfall: the drops run down and wrap
                    for (int i = 0; i < f.t.childCount; i++)
                    {
                        var c = f.t.GetChild(i);
                        var p = c.localPosition;
                        p.y = Mathf.Repeat(i * 1.1f - t * 6f - f.seed * 11f, 11f);
                        c.localPosition = p;
                    }
                    break;
                default:    // flames: flicker, flare on the kick
                {
                    float fl = 1f + 0.15f * Mathf.Sin(t * 17f + f.seed) + 0.35f * kick;
                    f.t.localScale = new Vector3(0.5f * fl, 0.8f * fl * (1f + 0.2f * Mathf.Sin(t * 11f + f.seed)), 0.5f * fl);
                    break;
                }
            }
        }

        // Riders: cars, the train - faster or slower than the road.
        foreach (var r in riders) Scroll(r.t, delta * r.speed);

        // UFOs sweep across the road high up, tilting into the turn.
        for (int i = 0; i < ufos.Count; i++)
        {
            var u = ufos[i];
            float a = t * u.speed * Mathf.PI * 2f + u.seed;
            u.t.localPosition = new Vector3(Mathf.Sin(a) * 13f, 10f + Mathf.Sin(a * 2f) * 1.2f + 0.4f * kick, 25f + i * 18f + Mathf.Cos(a * 0.5f) * 8f);
            u.t.localRotation = Quaternion.Euler(0f, t * 90f, Mathf.Cos(a) * 15f);
        }

        // Flashers: camera flashes in the stands (Racing) / cannon muzzles (Pirate) on the beat.
        if (flashers.Count > 0)
        {
            int seedBeat = beat * 7919;
            float frac = false ? (drop > 0.3f && ClubBeat.BeatPhase < 0.2f ? 0.25f : 0f) : 0.04f + 0.1f * energy + 0.15f * kick;
            for (int i = 0; i < flashers.Count; i++)
            {
                float h = Mathf.Repeat(Mathf.Sin((i + 1) * 12.9898f + seedBeat * 0.001f + (skin == LegendSkin.Starship ? Mathf.Floor(t * 8f) * 3.1f : 0f)) * 43758.5453f, 1f);
                flashers[i].enabled = h < frac;
            }
        }

        // Flashes on the drops (eruption bolts, spells, laser shots): one on every other beat.
        if (bolts.Count > 0)
        {
            foreach (var b in bolts) Scroll(b.t, delta);
            bool due = beat != lastFlashBeat && ClubBeat.BeatPhase < 0.25f && drop > 0.2f && (beat % 2) == 0;
            if (due)
            {
                lastFlashBeat = beat;
                flashUntil = Time.time + 0.15f;
                int pick = rng.Next(bolts.Count);
                for (int i = 0; i < bolts.Count; i++) bolts[i].t.gameObject.SetActive(i == pick);
            }
            else if (Time.time > flashUntil)
                foreach (var b in bolts) if (b.t.gameObject.activeSelf) b.t.gameObject.SetActive(false);
        }

        foreach (var q in particles)
        {
            var p = q.t.localPosition;
            float boxX = q.basePos.x, boxH = q.basePos.y, boxZ = q.basePos.z, y0 = q.size;
            p.y -= q.speed * dt * (1f + 0.6f * drop);
            p.x += (false ? 2.5f : Mathf.Sin(t * 1.2f + q.seed) * 0.6f) * dt;
            p.z -= delta * (skin == LegendSkin.Starship ? 1.4f : 0.5f);
            if (p.y < y0) p.y += boxH;
            if (p.y > y0 + boxH) p.y -= boxH;
            if (p.x > boxX) p.x -= boxX * 2f;
            if (p.x < -boxX) p.x += boxX * 2f;
            if (p.z < 1f) p.z += boxZ - 1f;
            q.t.localPosition = p;
            if (q.axis != Vector3.zero) q.t.localRotation = Quaternion.AngleAxis(t * 200f + q.seed * 60f, q.axis);
        }

        if (glowSkyMat != null) glowSkyMat.SetFloat("_Intensity", 0.8f + 0.3f * barPulse * energy + 0.4f * drop);
        if (bloomVolume != null) bloomVolume.weight = dropBloom * drop * (0.55f + 0.45f * kick);
    }
}
