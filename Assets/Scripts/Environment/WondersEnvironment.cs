using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum WonderSkin
{
    Steampunk,  // a steam factory: giant turning gears, copper pipes, pistons, steam vents on the kick, boilers, sparks
    Festival,   // a Chinese festival night: a dragon flying over the road, lanterns, pagodas, fireworks
    Crystal,    // a crystal cave: stalactites, glowing crystal clusters, an underground lake, minecarts, spores
    Funfair,    // a night funfair: ferris wheels, carousels, a roller coaster, bulb strings, tents, balloons
    Olympus,    // Mount Olympus: temples on clouds, colonnades, braziers, Zeus' lightning on the drops
}

/// Video 14's scenery ("Wonders"): five worlds, one per level, in the manner of ElementsEnvironment -
/// everything near scrolls with the tunnel and wraps, the sky pieces sit beyond the fog
/// (Custom/Starfield), a global Volume pushes the bloom up on a drop, and all motion and light
/// come from ClubBeat (the song clock), so it is on the music in Play and in a recording.
public class WondersEnvironment : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [SerializeField] private WonderSkin skin;
    [SerializeField, Range(0f, 1f)] private float dropBloom = 0.4f;
    [SerializeField] private float bloomPeak = 5f;
    [SerializeField] private int seed = 1414;

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
    private readonly List<Mover> dragon = new List<Mover>();           // the festival dragon's segments
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
        rng = new System.Random(seed + (int)skin * 191);
        mpb = new MaterialPropertyBlock();
        cube = RemixGeo.Primitive(PrimitiveType.Cube);
        sphere = RemixGeo.Primitive(PrimitiveType.Sphere);
        cyl = RemixGeo.Primitive(PrimitiveType.Cylinder);
        rockMesh = Rock();
        switch (skin)
        {
            case WonderSkin.Steampunk: BuildSteampunk(); break;
            case WonderSkin.Festival: BuildFestival(); break;
            case WonderSkin.Crystal: BuildCrystal(); break;
            case WonderSkin.Funfair: BuildFunfair(); break;
            case WonderSkin.Olympus: BuildOlympus(); break;
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

    // ---------- Steampunk ----------

    private void BuildSteampunk()
    {
        Color fire = new Color(1f, 0.5f, 0.12f), brass = new Color(1f, 0.78f, 0.35f), steamGlow = new Color(0.9f, 0.85f, 0.75f);
        Group(fire); Group(brass); Group(steamGlow);
        var iron = Lit(new Color(0.1f, 0.08f, 0.07f), 0.5f, 0.6f);
        var copper = Lit(new Color(0.55f, 0.27f, 0.12f), 0.65f, 0.9f);
        var brassMat = Lit(new Color(0.6f, 0.45f, 0.18f), 0.7f, 0.9f);
        var brick = Lit(new Color(0.16f, 0.07f, 0.05f), 0.2f);
        var steam = Lit(new Color(0.55f, 0.52f, 0.5f), 0.1f);
        var root = Root("Steampunk");

        // Brick walls on both sides with copper pipe runs.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var w = Prop(root, "Wall", new Vector3(side * 13f, 6f, z + 6f));
                Place("Brick", cube, brick, w, Vector3.zero, new Vector3(0.5f, 14f, 12f));
                for (int k = 0; k < 3; k++)
                    RemixGeo.Beam(w, "Pipe", cyl, copper, new Vector3(-side * 0.5f, -4f + k * 2.2f, -6f), new Vector3(-side * 0.5f, -4f + k * 2.2f, 6f), 0.35f);
                RemixGeo.Beam(w, "Riser", cyl, copper, new Vector3(-side * 0.6f, -6f, 2f), new Vector3(-side * 0.6f, 6f, 2f), 0.4f);
                Place("Valve", cyl, groupMats[1], w, new Vector3(-side * 0.85f, -1.6f, 2f), new Vector3(0.6f, 0.08f, 0.6f), new Vector3(0f, 0f, 90f));
                scrollers.Add(new Mover { t = w });
            }
        // Giant gears turning on the walls, meshing in pairs (one turns the other way).
        int n = 0;
        for (float z = -Behind; z < Far; z += Rand(9f, 13f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            float r = Rand(1.8f, 3f);
            var g = Gear(root, new Vector3(side * 12.3f, Rand(4f, 8f), z), r, 12 + (int)(r * 3f), copper, brassMat);
            floaters.Add(new Mover { t = g, basePos = g.localPosition, seed = Rand(0f, 6.28f), group = -1, speed = 30f / r });
            var g2 = Gear(root, new Vector3(side * 12.3f, g.localPosition.y + r + 0.9f, z + r * 0.9f), r * 0.55f, 9, brassMat, copper);
            floaters.Add(new Mover { t = g2, basePos = g2.localPosition, seed = 0f, group = -1, speed = -30f / (r * 0.55f) });
        }
        // Boilers with furnace windows; pistons pumping on the beat between them.
        n = 0;
        for (float z = -Behind; z < Far; z += 8f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                float zz = z + (side > 0 ? 4f : 0f);
                if ((n + (side > 0 ? 1 : 0)) % 2 == 0)
                {
                    var b = Prop(root, "Boiler", new Vector3(side * 9.2f, -0.5f, zz));
                    Place("Tank", cyl, copper, b, new Vector3(0f, 1.6f, 0f), new Vector3(2.2f, 1.6f, 2.2f));
                    Place("Cap", sphere, copper, b, new Vector3(0f, 3.2f, 0f), new Vector3(2.2f, 1f, 2.2f));
                    Place("Band", cyl, brassMat, b, new Vector3(0f, 2.2f, 0f), new Vector3(2.3f, 0.08f, 2.3f));
                    var win = Place("Furnace", cube, groupMats[0], b, new Vector3(-side * 1.08f, 0.9f, 0f), new Vector3(0.05f, 0.6f, 0.8f));
                    pulsers.Add(new Mover { t = win.transform, group = 0, size = -1f });
                    Place("Stack", cyl, iron, b, new Vector3(0f, 4.6f, 0f), new Vector3(0.45f, 1.2f, 0.45f));
                    scrollers.Add(new Mover { t = b });
                }
                else
                {
                    var p = Prop(root, "Piston", new Vector3(side * 8.6f, -0.5f, zz));
                    Place("Housing", cube, iron, p, new Vector3(0f, 0.6f, 0f), new Vector3(1.2f, 1.2f, 1.2f));
                    var rod = Prop(p, "Rod", new Vector3(0f, 1.2f, 0f));
                    Place("Shaft", cyl, brassMat, rod, new Vector3(0f, 1f, 0f), new Vector3(0.3f, 1f, 0.3f));
                    Place("Head", cyl, copper, rod, new Vector3(0f, 2f, 0f), new Vector3(0.9f, 0.18f, 0.9f));
                    floaters.Add(new Mover { t = rod, basePos = rod.localPosition, seed = n * 0.5f, group = -2 });
                    scrollers.Add(new Mover { t = p });
                }
            }
        // Steam vents: a column of puffs shooting up on their beat.
        for (float z = -Behind + 2f; z < Far; z += 7f)
            foreach (int side in new[] { -1, 1 })
            {
                var v = Prop(root, "Vent", new Vector3(side * 7.3f, -0.5f, z + (side > 0 ? 3.5f : 0f)));
                for (int k = 0; k < 6; k++)
                    Place("Puff", sphere, k == 0 ? groupMats[2] : steam, v, new Vector3(Rand(-0.15f, 0.15f), 0.4f + k * 0.85f, 0f), Vector3.one * (0.6f + k * 0.12f));
                columns.Add(new Mover { t = v, seed = Rand(0f, 6.28f), group = (int)((z + Behind) / 7f) % 2, size = 1f });
            }
        // Sky: a giant clock face and smokestacks.
        glowSkyMat = SkyQuad("Clock", new Vector3(0f, 80f, 340f), 55f, 55f, new Color(1f, 0.7f, 0.35f, 1f), Mask(128, 128, (x, y) =>
        {
            float dx = x * 2f - 1f, dy = y * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
            float rim = Mathf.Abs(r - 0.9f) < 0.04f ? 1f : 0f;
            float ticks = r > 0.72f && r < 0.84f && Mathf.Repeat(a / (Mathf.PI / 6f) + 0.5f, 1f) < 0.12f ? 1f : 0f;
            float hand1 = Mathf.Abs(dx) < 0.025f && dy > 0f && dy < 0.6f ? 1f : 0f;
            float hand2 = Mathf.Abs(dy) < 0.025f && dx > 0f && dx < 0.45f ? 1f : 0f;
            return Mathf.Max(Mathf.Max(rim, ticks), Mathf.Max(hand1, hand2)) + (r < 0.9f ? 0.12f : 0f);
        }), 0.9f);
        var stacks = Mask(256, 96, (x, y) =>
        {
            float s = Mathf.Repeat(x * 9f, 1f);
            float h = 0.35f + 0.5f * Mathf.Abs(Mathf.Sin(Mathf.Floor(x * 9f) * 2.3f));
            return (s > 0.4f && s < 0.6f && y < h) || y < 0.25f + 0.08f * Mathf.Sin(x * 30f) ? 1f : 0f;
        });
        SkyQuad("Stacks", new Vector3(0f, 22f, 348f), 260f, 55f, new Color(0.12f, 0.06f, 0.04f, 1f), stacks, 0.7f);
        particleMats = new[] { groupMats[0], groupMats[1] };
        ParticleBox("Sparks", 140, cube, particleMats, Vector3.one * 0.06f, new Vector2(3f, 7f));
    }

    // ---------- Festival ----------

    private void BuildFestival()
    {
        Color red = new Color(1f, 0.12f, 0.12f), gold = new Color(1f, 0.78f, 0.2f), teal = new Color(0.1f, 0.95f, 0.8f), pink = new Color(1f, 0.4f, 0.7f);
        Group(red); Group(gold); Group(teal); Group(pink);
        var lacquer = Lit(new Color(0.45f, 0.04f, 0.04f), 0.7f);
        var roofMat = Lit(new Color(0.06f, 0.08f, 0.07f), 0.6f);
        var root = Root("Festival");

        // Lantern poles along the road with strings of small lanterns between them, swaying.
        for (float z = -Behind; z < Far; z += 6f)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Pole", new Vector3(side * 7.8f, -0.5f, z));
                Place("Post", cyl, lacquer, p, new Vector3(0f, 2.8f, 0f), new Vector3(0.25f, 2.8f, 0.25f));
                var big = Place("Lantern", sphere, groupMats[0], p, new Vector3(-side * 0.7f, 4.6f, 0f), new Vector3(0.9f, 1.1f, 0.9f));
                Place("CapT", cyl, groupMats[1], p, new Vector3(-side * 0.7f, 5.2f, 0f), new Vector3(0.5f, 0.06f, 0.5f));
                Place("CapB", cyl, groupMats[1], p, new Vector3(-side * 0.7f, 4.0f, 0f), new Vector3(0.5f, 0.06f, 0.5f));
                RemixGeo.Beam(p, "Arm", cube, lacquer, new Vector3(0f, 5.4f, 0f), new Vector3(-side * 0.8f, 5.4f, 0f), 0.12f);
                pulsers.Add(new Mover { t = big.transform, group = 0, size = -1f });
                var str = Prop(p, "String", new Vector3(0f, 5.5f, 0f));
                for (int k = 1; k < 6; k++)
                {
                    float u = k / 6f;
                    var l = Place("Mini", sphere, groupMats[k % 2 == 0 ? 1 : 3], str, new Vector3(0f, -Mathf.Sin(u * Mathf.PI) * 0.9f, u * 6f), new Vector3(0.32f, 0.42f, 0.32f));
                }
                floaters.Add(new Mover { t = str, basePos = str.localPosition, seed = Rand(0f, 6.28f), group = -3 });
                scrollers.Add(new Mover { t = p });
            }
        // Pagodas further out: tiers with upturned golden eaves.
        int n = 0;
        for (float z = -Behind; z < Far; z += Rand(14f, 20f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var pg = Prop(root, "Pagoda", new Vector3(side * Rand(15f, 19f), -0.5f, z));
            int tiers = rng.Next(4, 7);
            float y = 0f, w = 5f;
            for (int k = 0; k < tiers; k++)
            {
                float h = 2.2f - k * 0.15f;
                Place("Tier", cube, lacquer, pg, new Vector3(0f, y + h * 0.5f, 0f), new Vector3(w * 0.7f, h, w * 0.7f));
                Place("Roof", cube, roofMat, pg, new Vector3(0f, y + h + 0.15f, 0f), new Vector3(w * 1.15f, 0.3f, w * 1.15f));
                foreach (var c in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) })
                    RemixGeo.Beam(pg, "Eave", cube, groupMats[1], new Vector3(c.x * w * 0.5f, y + h + 0.3f, c.z * w * 0.5f), new Vector3(c.x * w * 0.68f, y + h + 0.8f, c.z * w * 0.68f), 0.14f);
                Place("Glow", cube, groupMats[k % 2 == 0 ? 1 : 2], pg, new Vector3(-side * w * 0.36f, y + h * 0.5f, 0f), new Vector3(0.05f, h * 0.5f, w * 0.3f));
                y += h + 0.3f; w *= 0.84f;
            }
            Place("Spire", cyl, groupMats[1], pg, new Vector3(0f, y + 1f, 0f), new Vector3(0.15f, 1f, 0.15f));
            scrollers.Add(new Mover { t = pg });
        }
        // The dragon: a chain of segments winding along the road high above, head first.
        var dr = Root("Dragon");
        for (int i = 0; i < 34; i++)
        {
            float s = i == 0 ? 1.5f : Mathf.Lerp(1.1f, 0.35f, i / 34f);
            var seg = Place(i == 0 ? "Head" : "Body", sphere, groupMats[i == 0 ? 1 : (i % 3 == 0 ? 1 : 0)], dr, Vector3.zero, new Vector3(s, s, s * 1.3f));
            if (i == 0)
            {
                RemixGeo.Beam(seg.transform, "Horn", cube, groupMats[1], new Vector3(-0.25f, 0.4f, 0f), new Vector3(-0.45f, 0.9f, -0.4f), 0.12f);
                RemixGeo.Beam(seg.transform, "Horn", cube, groupMats[1], new Vector3(0.25f, 0.4f, 0f), new Vector3(0.45f, 0.9f, -0.4f), 0.12f);
            }
            else if (i % 4 == 0)
                RemixGeo.Beam(seg.transform, "Fin", cube, groupMats[2], new Vector3(0f, 0.45f, 0f), new Vector3(0f, 0.9f, -0.3f), 0.1f);
            dragon.Add(new Mover { t = seg.transform, seed = i });
        }
        // Fireworks: bursts in the sky that flash on the drops.
        var burst = Mask(128, 128, (x, y) =>
        {
            float dx = x * 2f - 1f, dy = y * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
            float rays = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 8f)), 20f) * (r < 0.95f ? 1f : 0f) * Mathf.Clamp01(r * 1.5f);
            return rays + Mathf.Clamp01(1f - r * 4f);
        });
        Color[] fw = { red, gold, teal, pink };
        for (int i = 0; i < 8; i++)
        {
            Color c = fw[i % 4]; c.a = 1f;
            skyFlash.Add(SkyQuad("Firework", new Vector3(Rand(-200f, 200f), Rand(50f, 120f), Rand(300f, 350f)), Rand(25f, 40f), Rand(25f, 40f), c, burst, 0f));
        }
        glowSkyMat = SkyQuad("Moon", new Vector3(-100f, 90f, 340f), 26f, 26f, new Color(1f, 0.85f, 0.5f, 1f), Sprite(128, 0.5f), 1f);
        particleMats = new[] { groupMats[1], groupMats[3] };
        ParticleBox("Confetti", 150, cube, particleMats, new Vector3(0.14f, 0.02f, 0.1f), new Vector2(0.8f, 1.8f));
    }

    // ---------- Crystal cave ----------

    private void BuildCrystal()
    {
        Color amethyst = new Color(0.75f, 0.3f, 1f), emerald = new Color(0.2f, 1f, 0.6f), ice = new Color(0.5f, 0.85f, 1f);
        Group(amethyst); Group(emerald); Group(ice);
        var rock = Lit(new Color(0.06f, 0.05f, 0.08f), 0.25f);
        var water = Lit(new Color(0.02f, 0.03f, 0.06f), 0.97f, 0.2f);
        var wood = Lit(new Color(0.18f, 0.11f, 0.06f), 0.3f);
        var rail = Lit(new Color(0.4f, 0.38f, 0.36f), 0.8f, 0.9f);
        var root = Root("Cave");

        // Cave walls and a ceiling of boulders, stalactites hanging from it.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 5f)
            {
                var w = Place("WallRock", rockMesh, rock, root, new Vector3(side * Rand(14f, 16f), Rand(2f, 8f), z), new Vector3(Rand(5f, 8f), Rand(8f, 14f), Rand(5f, 7f)));
                scrollers.Add(new Mover { t = w.transform });
            }
        for (float z = -Behind; z < Far; z += 3.5f)
        {
            var c = Prop(root, "Ceiling", new Vector3(Rand(-12f, 12f), Rand(15f, 17f), z));
            Place("Rock", rockMesh, rock, c, Vector3.zero, new Vector3(Rand(5f, 9f), Rand(2f, 3f), Rand(4f, 6f)));
            for (int k = 0; k < 3; k++)
            {
                float h = Rand(2f, 5f);
                Place("Stalactite", rockMesh, rock, c, new Vector3(Rand(-2.5f, 2.5f), -h * 0.6f, Rand(-1.5f, 1.5f)), new Vector3(Rand(0.5f, 0.9f), h, Rand(0.5f, 0.9f)));
                if (k == 0)
                {
                    var tip = Place("Tip", cube, groupMats[k % 3], c, new Vector3(0f, -h * 1.05f, 0f), new Vector3(0.2f, 0.6f, 0.2f), new Vector3(0f, 45f, 0f));
                    pulsers.Add(new Mover { t = tip.transform, group = 0, size = -1f });
                }
            }
            scrollers.Add(new Mover { t = c });
        }
        // Crystal clusters on both sides, glowing in turn.
        int n = 0;
        for (float z = -Behind; z < Far; z += Rand(4f, 6.5f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var cl = Prop(root, "Crystals", new Vector3(side * Rand(9.5f, 11.5f), -0.5f, z));
            int g = n % 3;
            for (int k = 0; k < 6; k++)
            {
                float h = Rand(1.2f, 4.5f);
                Place("Shard", cube, groupMats[g], cl, new Vector3(Rand(-0.8f, 0.8f), h * 0.45f, Rand(-0.8f, 0.8f)),
                      new Vector3(0.4f, h, 0.4f), new Vector3(Rand(-30f, 30f), Rand(0f, 90f), Rand(-30f, 30f)));
            }
            scrollers.Add(new Mover { t = cl });
        }
        // The underground lake on both sides, a glowing shoreline.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var l = Prop(root, "Lake", new Vector3(side * 8.6f, -0.55f, z + 6f));
                Place("Water", cube, water, l, Vector3.zero, new Vector3(3.2f, 0.05f, 12f));
                RemixGeo.Beam(l, "Shore", cube, groupMats[2], new Vector3(-side * 1.6f, 0.04f, -6f), new Vector3(-side * 1.6f, 0.04f, 6f), 0.08f);
                scrollers.Add(new Mover { t = l });
            }
        // Minecart rails beside the road, carts rolling along them faster than the road.
        foreach (int side in new[] { -1, 1 })
        {
            for (float z = -Behind; z < Far; z += 12f)
            {
                var t = Prop(root, "Rails", new Vector3(side * 6.4f, -0.48f, z + 6f));
                RemixGeo.Beam(t, "Rail", cube, rail, new Vector3(-0.45f, 0.08f, -6f), new Vector3(-0.45f, 0.08f, 6f), 0.07f);
                RemixGeo.Beam(t, "Rail", cube, rail, new Vector3(0.45f, 0.08f, -6f), new Vector3(0.45f, 0.08f, 6f), 0.07f);
                for (float s = -5.5f; s < 6f; s += 1.1f) Place("Sleeper", cube, wood, t, new Vector3(0f, 0.02f, s), new Vector3(1.3f, 0.06f, 0.25f));
                scrollers.Add(new Mover { t = t });
            }
            for (int k = 0; k < 3; k++)
            {
                var cart = Prop(root, "Cart", new Vector3(side * 6.4f, -0.3f, Rand(0f, Far)));
                Place("Box", cube, wood, cart, new Vector3(0f, 0.45f, 0f), new Vector3(1.1f, 0.7f, 1.5f));
                for (int g = 0; g < 4; g++)
                    Place("Gem", cube, groupMats[g % 3], cart, new Vector3(Rand(-0.35f, 0.35f), 0.85f, Rand(-0.5f, 0.5f)), Vector3.one * 0.3f, new Vector3(45f, Rand(0f, 90f), 45f));
                riders.Add(new Mover { t = cart, speed = side > 0 ? 1.8f : -0.6f });
            }
        }
        // Geodes: rocks split open, a glowing core inside.
        for (float z = -Behind + 4f; z < Far; z += Rand(11f, 16f))
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var g = Prop(root, "Geode", new Vector3(side * Rand(11.5f, 13f), Rand(0.5f, 3f), z));
            Place("Shell", rockMesh, rock, g, new Vector3(side * 0.4f, 0f, 0f), Vector3.one * 2.2f);
            var core = Place("Core", sphere, groupMats[rng.Next(3)], g, new Vector3(-side * 0.5f, 0f, 0f), Vector3.one * 1.3f);
            pulsers.Add(new Mover { t = core.transform, group = 1, size = 1.3f });
            scrollers.Add(new Mover { t = g });
        }
        glowSkyMat = SkyQuad("DeepGlow", new Vector3(0f, 20f, 340f), 120f, 60f, new Color(0.5f, 0.2f, 0.8f, 0.6f), Sprite(128, 1.5f), 0.6f);
        particleMats = new[] { groupMats[1], groupMats[0] };
        ParticleBox("Spores", 140, sphere, particleMats, Vector3.one * 0.08f, new Vector2(-0.7f, -0.2f));
    }

    // ---------- Funfair ----------

    private void BuildFunfair()
    {
        Color red = new Color(1f, 0.2f, 0.3f), yellow = new Color(1f, 0.85f, 0.2f), cyan = new Color(0.2f, 0.9f, 1f), green = new Color(0.4f, 1f, 0.4f), warm = new Color(1f, 0.75f, 0.45f);
        Group(red); Group(yellow); Group(cyan); Group(green); Group(warm);
        var steel = Lit(new Color(0.2f, 0.2f, 0.25f), 0.6f, 0.7f);
        var white = Lit(new Color(0.75f, 0.72f, 0.7f), 0.4f);
        var root = Root("Funfair");

        // Bulb strings along the road: posts with a chain of bulbs, chasing.
        for (float z = -Behind; z < Far; z += 5f)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "BulbPost", new Vector3(side * 7.6f, -0.5f, z));
                Place("Post", cyl, steel, p, new Vector3(0f, 2.2f, 0f), new Vector3(0.15f, 2.2f, 0.15f));
                for (int k = 0; k < 5; k++)
                {
                    float u = k / 5f;
                    var b = Place("Bulb", sphere, groupMats[((k + Mathf.FloorToInt(z * 0.2f)) % 5 + 5) % 5], p, new Vector3(0f, 4.4f - Mathf.Sin(u * Mathf.PI) * 0.6f, u * 5f), Vector3.one * 0.25f);
                }
                scrollers.Add(new Mover { t = p });
            }
        // Striped tents.
        int n = 0;
        for (float z = -Behind; z < Far; z += Rand(8f, 11f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var t = Prop(root, "Tent", new Vector3(side * Rand(10f, 11.5f), -0.5f, z));
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                Place("Stripe", cube, k % 2 == 0 ? groupMats[0] : white, t, new Vector3(Mathf.Cos(a) * 1.4f, 1.2f, Mathf.Sin(a) * 1.4f), new Vector3(1.1f, 2.4f, 0.1f), new Vector3(0f, -a * Mathf.Rad2Deg + 90f, 0f));
            }
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                RemixGeo.Beam(t, "Roof", cube, k % 2 == 0 ? groupMats[1] : groupMats[0], new Vector3(Mathf.Cos(a) * 1.6f, 2.4f, Mathf.Sin(a) * 1.6f), new Vector3(0f, 3.8f, 0f), 0.4f);
            }
            Place("Flag", cube, groupMats[2], t, new Vector3(0.2f, 4.3f, 0f), new Vector3(0.4f, 0.25f, 0.03f));
            scrollers.Add(new Mover { t = t });
        }
        // Ferris wheels far out, turning; carousels nearer, spinning.
        n = 0;
        for (float z = -Behind + 6f; z < Far; z += Rand(28f, 36f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var fw = Prop(root, "Ferris", new Vector3(side * 22f, 9f, z));
            RemixGeo.Beam(fw, "Leg", cube, steel, new Vector3(0f, 0f, 0f), new Vector3(0f, -9.5f, -4f), 0.3f);
            RemixGeo.Beam(fw, "Leg", cube, steel, new Vector3(0f, 0f, 0f), new Vector3(0f, -9.5f, 4f), 0.3f);
            var wheel = Prop(fw, "Wheel", Vector3.zero);
            float R = 8f;
            int seg = 24;
            for (int k = 0; k < seg; k++)
            {
                float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                RemixGeo.Beam(wheel, "Rim", cube, groupMats[k % 5], new Vector3(0f, Mathf.Sin(a0), Mathf.Cos(a0)) * R, new Vector3(0f, Mathf.Sin(a1), Mathf.Cos(a1)) * R, 0.2f);
                if (k % 3 == 0)
                {
                    RemixGeo.Beam(wheel, "Spoke", cube, steel, Vector3.zero, new Vector3(0f, Mathf.Sin(a0), Mathf.Cos(a0)) * R, 0.1f);
                    Place("Cabin", cube, groupMats[(k / 3) % 5], wheel, new Vector3(0f, Mathf.Sin(a0), Mathf.Cos(a0)) * R + Vector3.down * 0.7f, new Vector3(0.9f, 1f, 0.9f));
                }
            }
            floaters.Add(new Mover { t = wheel, seed = Rand(0f, 6.28f), group = -1, speed = 8f });
            scrollers.Add(new Mover { t = fw });
        }
        for (float z = -Behind + 10f; z < Far; z += Rand(22f, 30f))
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var c = Prop(root, "Carousel", new Vector3(side * 14.5f, -0.5f, z));
            var spin = Prop(c, "Spin", Vector3.zero);
            Place("Base", cyl, white, spin, new Vector3(0f, 0.2f, 0f), new Vector3(5f, 0.2f, 5f));
            for (int k = 0; k < 4; k++) Place("Roof", cyl, groupMats[k % 2 == 0 ? 0 : 1], spin, new Vector3(0f, 3.4f + k * 0.35f, 0f), new Vector3(5.4f - k * 1.3f, 0.18f, 5.4f - k * 1.3f));
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                Vector3 p = new Vector3(Mathf.Cos(a) * 2f, 0f, Mathf.Sin(a) * 2f);
                RemixGeo.Beam(spin, "Pole", cyl, groupMats[1], p + Vector3.up * 0.4f, p + Vector3.up * 3.4f, 0.08f);
                Place("Horse", cube, k % 2 == 0 ? white : groupMats[2], spin, p + Vector3.up * (1.3f + (k % 2) * 0.4f), new Vector3(0.35f, 0.5f, 0.8f), new Vector3(0f, -a * Mathf.Rad2Deg, 0f));
            }
            floaters.Add(new Mover { t = spin, group = -4, speed = 40f });
            scrollers.Add(new Mover { t = c });
        }
        // A roller coaster track waving high over one side, a train riding it.
        var coaster = Root("Coaster");
        for (float z = -Behind; z < Far; z += 1.5f)
        {
            float y0 = 9f + 3f * Mathf.Sin(z * 0.12f), y1 = 9f + 3f * Mathf.Sin((z + 1.5f) * 0.12f);
            var piece = Prop(coaster, "Track", new Vector3(11f, 0f, z));
            RemixGeo.Beam(piece, "Rail", cube, groupMats[2], new Vector3(-0.5f, y0, 0f), new Vector3(-0.5f, y1, 1.5f), 0.1f);
            RemixGeo.Beam(piece, "Rail", cube, groupMats[2], new Vector3(0.5f, y0, 0f), new Vector3(0.5f, y1, 1.5f), 0.1f);
            if (Mathf.RoundToInt(z / 1.5f) % 4 == 0) RemixGeo.Beam(piece, "Support", cube, steel, new Vector3(0f, y0, 0f), new Vector3(0f, -0.5f, 0f), 0.18f);
            scrollers.Add(new Mover { t = piece });
        }
        for (int k = 0; k < 4; k++)
        {
            var car = Place("Car", cube, groupMats[k % 2 == 0 ? 0 : 1], root, new Vector3(11f, 9.5f, 20f + k * 1.4f), new Vector3(1f, 0.7f, 1.2f));
            riders.Add(new Mover { t = car.transform, speed = 1.5f, group = 1, seed = k * 1.4f });
        }
        // Sky: a big moon and stars; balloons drifting up.
        glowSkyMat = SkyQuad("Moon", new Vector3(80f, 90f, 340f), 28f, 28f, new Color(1f, 0.95f, 0.8f, 1f), Sprite(128, 0.55f), 1f);
        var stars = Mask(256, 128, (x, y) => Mathf.Sin(x * 913f + y * 337f) > 0.997f ? 1f : 0f);
        SkyQuad("Stars", new Vector3(0f, 90f, 352f), 280f, 120f, new Color(1f, 1f, 1f, 1f), stars, 0.6f);
        particleMats = new[] { groupMats[0], groupMats[1], groupMats[2], groupMats[3] };
        ParticleBox("Balloons", 40, sphere, particleMats, new Vector3(0.5f, 0.6f, 0.5f), new Vector2(-1.2f, -0.6f), 13f, 2f, 18f, 70f, false);
    }

    // ---------- Olympus ----------

    private void BuildOlympus()
    {
        Color gold = new Color(1f, 0.8f, 0.3f), sky = new Color(0.45f, 0.75f, 1f), flame = new Color(1f, 0.55f, 0.15f);
        Group(gold); Group(sky); Group(flame);
        var marble = Lit(new Color(0.62f, 0.62f, 0.66f), 0.6f);
        var cloud = Lit(new Color(0.55f, 0.6f, 0.72f), 0.05f);
        var root = Root("Olympus");

        // Cloud banks all along both sides, below and around the road.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 4f)
            {
                var c = Prop(root, "Clouds", new Vector3(side * Rand(9f, 14f), -1.5f, z));
                for (int k = 0; k < 3; k++)
                    Place("Puff", sphere, cloud, c, new Vector3(Rand(-2f, 2f), Rand(-0.5f, 0.8f), Rand(-1.5f, 1.5f)), new Vector3(Rand(3f, 5f), Rand(1.5f, 2.5f), Rand(3f, 4.5f)));
                scrollers.Add(new Mover { t = c });
            }
        // A colonnade along the road: marble columns with golden capitals; braziers between.
        int n = 0;
        for (float z = -Behind; z < Far; z += 6f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var col = Prop(root, "Column", new Vector3(side * 7.8f, -0.5f, z + (side > 0 ? 3f : 0f)));
                Place("Base", cube, marble, col, new Vector3(0f, 0.2f, 0f), new Vector3(1.2f, 0.4f, 1.2f));
                Place("Shaft", cyl, marble, col, new Vector3(0f, 3.2f, 0f), new Vector3(0.75f, 2.8f, 0.75f));
                var cap = Place("Capital", cube, groupMats[0], col, new Vector3(0f, 6.15f, 0f), new Vector3(1.25f, 0.3f, 1.25f));
                pulsers.Add(new Mover { t = cap.transform, group = 0, size = -1f });
                scrollers.Add(new Mover { t = col });
                if (n % 2 == 1)
                {
                    var b = Prop(root, "Brazier", new Vector3(side * 6.8f, -0.5f, z + (side > 0 ? 0f : 3f)));
                    Place("Stand", cyl, marble, b, new Vector3(0f, 0.7f, 0f), new Vector3(0.25f, 0.7f, 0.25f));
                    Place("Bowl", cyl, groupMats[0], b, new Vector3(0f, 1.45f, 0f), new Vector3(0.8f, 0.12f, 0.8f));
                    var fl = Place("Fire", sphere, groupMats[2], b, new Vector3(0f, 1.9f, 0f), new Vector3(0.5f, 0.8f, 0.5f));
                    floaters.Add(new Mover { t = fl.transform, seed = Rand(0f, 6.28f), group = -5 });
                    scrollers.Add(new Mover { t = b });
                }
            }
        // Temples on cloud islands further out: steps, a ring of columns, a pediment.
        n = 0;
        for (float z = -Behind; z < Far; z += Rand(20f, 26f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var t = Prop(root, "Temple", new Vector3(side * Rand(18f, 22f), Rand(1f, 4f), z));
            for (int k = 0; k < 3; k++) Place("Step", cube, marble, t, new Vector3(0f, k * 0.4f, 0f), new Vector3(9f - k * 0.6f, 0.4f, 6f - k * 0.6f));
            for (int k = 0; k < 6; k++)
            {
                float x = -3.6f + k * 1.44f;
                Place("Col", cyl, marble, t, new Vector3(x, 3.4f, -2.2f), new Vector3(0.55f, 2.4f, 0.55f));
                Place("Col", cyl, marble, t, new Vector3(x, 3.4f, 2.2f), new Vector3(0.55f, 2.4f, 0.55f));
            }
            Place("Beam", cube, marble, t, new Vector3(0f, 6.1f, 0f), new Vector3(8.4f, 0.6f, 5.4f));
            RemixGeo.Beam(t, "Trim", cube, groupMats[0], new Vector3(-4.2f, 5.85f, -2.75f), new Vector3(4.2f, 5.85f, -2.75f), 0.1f);
            RemixGeo.Beam(t, "Pediment", cube, marble, new Vector3(-4.3f, 6.4f, 0f), new Vector3(0f, 8.2f, 0f), 0.5f);
            RemixGeo.Beam(t, "Pediment", cube, marble, new Vector3(4.3f, 6.4f, 0f), new Vector3(0f, 8.2f, 0f), 0.5f);
            for (int k = 0; k < 4; k++)
                Place("Isle", sphere, cloud, t, new Vector3(Rand(-4f, 4f), -1f, Rand(-3f, 3f)), new Vector3(Rand(4f, 6f), 1.8f, Rand(3f, 5f)));
            scrollers.Add(new Mover { t = t });
        }
        // Zeus' lightning: golden bolts from the sky, flashing on the drops.
        for (float z = -Behind + 6f; z < Far; z += 10f)
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var b = Prop(root, "Lightning", new Vector3(side * Rand(11f, 18f), 0f, z));
            Zigzag(b, groupMats[0], new Vector3(0f, 18f, 0f), new Vector3(Rand(-2f, 2f), -0.5f, 0f), 8, 1.3f, 0.18f);
            b.gameObject.SetActive(false);
            bolts.Add(new Mover { t = b });
        }
        // Sky: the sun with rays, golden cloud layers.
        glowSkyMat = SkyQuad("Sun", new Vector3(0f, 70f, 340f), 40f, 40f, new Color(1f, 0.9f, 0.6f, 1f), Sprite(128, 0.45f), 1.1f);
        var ray = Mask(16, 128, (x, y) => (1f - Mathf.Abs(x - 0.5f) * 2f) * (1f - y));
        for (int i = 0; i < 10; i++)
        {
            Color c = new Color(1f, 0.9f, 0.6f, 0.3f);
            SkyQuad("Ray", new Vector3(0f, 70f, 342f), 6f, 150f, c, ray, 0.4f, i * 36f);
        }
        var soft = Sprite(128, 1.3f);
        for (int i = 0; i < 16; i++)
        {
            Color c = i % 3 == 0 ? new Color(1f, 0.85f, 0.6f, 0.6f) : new Color(0.9f, 0.93f, 1f, 0.55f);
            SkyQuad("SkyCloud", new Vector3(Rand(-260f, 260f), Rand(10f, 60f), Rand(300f, 360f)), Rand(50f, 100f), Rand(20f, 35f), c, soft, 0.6f);
        }
        particleMats = new[] { groupMats[0] };
        ParticleBox("Sparkles", 120, sphere, particleMats, Vector3.one * 0.07f, new Vector2(-0.5f, 0.5f), 14f, 0.5f, 12f);
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
                case -1:    // gears / a ferris wheel: turn about X, a push on the kick
                    f.t.localRotation = Quaternion.Euler((t * f.speed + kick * Mathf.Sign(f.speed) * 6f) + f.seed * 50f, 0f, 0f);
                    if (skin == WonderSkin.Steampunk) Scroll(f.t, delta);
                    break;
                case -2:    // pistons: down on the beat, up between
                {
                    Scroll(f.t.parent, 0f);
                    var p = f.t.localPosition;
                    p.y = f.basePos.y + 0.9f * (1f - kick) * Mathf.Max(0.3f, energy * 0.8f);
                    f.t.localPosition = p;
                    break;
                }
                case -3:    // lantern strings: sway
                    f.t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.4f + f.seed) * 6f);
                    break;
                case -4:    // carousel: spin
                    f.t.localRotation = Quaternion.Euler(0f, t * f.speed * (1f + 0.5f * drop), 0f);
                    f.t.localPosition = new Vector3(0f, 0.15f * kick, 0f);
                    break;
                default:    // flames: flicker, flare on the kick
                {
                    float fl = 1f + 0.15f * Mathf.Sin(t * 17f + f.seed) + 0.35f * kick;
                    f.t.localScale = new Vector3(0.5f * fl, 0.8f * fl * (1f + 0.2f * Mathf.Sin(t * 11f + f.seed)), 0.5f * fl);
                    break;
                }
            }
        }

        // Steam vents shoot up on their beat.
        foreach (var c in columns)
        {
            Scroll(c.t, delta);
            bool fire = ((beat & 1) == c.group) && energy > 0.5f;
            float h = fire ? Mathf.Pow(1f - ClubBeat.BeatPhase, 0.6f) : 0.12f;
            c.t.localScale = new Vector3(1f, Mathf.Max(0.05f, h * (1f + 0.5f * drop)), 1f);
        }

        // Carts riding faster (or slower) than the road.
        foreach (var r in riders)
        {
            Scroll(r.t, delta * r.speed);
            if (skin == WonderSkin.Funfair)
            {
                var p = r.t.localPosition;
                p.y = 9.5f + 3f * Mathf.Sin(p.z * 0.12f);
                r.t.localPosition = p;
                r.t.localRotation = Quaternion.Euler(-Mathf.Atan(0.36f * Mathf.Cos(p.z * 0.12f)) * Mathf.Rad2Deg, 0f, 0f);
            }
        }

        // The dragon winds along the road above the lanes, head leading.
        if (dragon.Count > 0)
        {
            float speed = 1f + 0.6f * drop;
            Vector3 Path(float s)
            {
                float z = Mathf.Repeat(s * 9f, 110f) - 15f;
                return new Vector3(Mathf.Sin(s * 1.3f) * 5.5f, 10.5f + Mathf.Sin(s * 2.1f) * 1.6f + 0.5f * kick, z);
            }
            float head = t * 0.55f * speed;
            for (int i = 0; i < dragon.Count; i++)
            {
                float s = head - i * 0.075f;
                Vector3 p = Path(s), ahead = Path(s + 0.02f);
                var tr = dragon[i].t;
                tr.localPosition = p;
                Vector3 dir = ahead - p;
                if (dir.sqrMagnitude > 1e-6f && dir.sqrMagnitude < 100f) tr.localRotation = Quaternion.LookRotation(dir);
            }
        }

        // Lightning (Olympus) and fireworks (Festival): one flash on every other beat with energy.
        if (bolts.Count > 0 || skyFlash.Count > 0)
        {
            foreach (var b in bolts) Scroll(b.t, delta);
            bool due = beat != lastFlashBeat && ClubBeat.BeatPhase < 0.25f && energy > 0.6f && (beat % (drop > 0.3f ? 1 : 2)) == 0;
            if (due)
            {
                lastFlashBeat = beat;
                flashUntil = Time.time + (skyFlash.Count > 0 ? 0.5f : 0.12f);
                if (bolts.Count > 0)
                {
                    int pick = rng.Next(bolts.Count);
                    for (int i = 0; i < bolts.Count; i++) bolts[i].t.gameObject.SetActive(i == pick && drop > 0.2f);
                }
                if (skyFlash.Count > 0) flashPick = rng.Next(skyFlash.Count);
            }
            else if (Time.time > flashUntil)
                foreach (var b in bolts) if (b.t.gameObject.activeSelf) b.t.gameObject.SetActive(false);
            for (int i = 0; i < skyFlash.Count; i++)
            {
                float left = Mathf.Clamp01((flashUntil - Time.time) / 0.5f);
                skyFlash[i].SetFloat("_Intensity", i == flashPick ? 1.6f * left : 0f);
            }
        }

        foreach (var q in particles)
        {
            var p = q.t.localPosition;
            float boxX = q.basePos.x, boxH = q.basePos.y, boxZ = q.basePos.z, y0 = q.size;
            p.y -= q.speed * dt * (1f + 0.6f * drop);
            p.x += Mathf.Sin(t * 1.2f + q.seed) * dt * 0.6f;
            p.z -= delta * 0.5f;
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
