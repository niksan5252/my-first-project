using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum GrooveSkin
{
    Disco,      // a 70s roller disco: lit dance floor tiles, mirror balls, sweeping spotlights, speaker stacks
    Subway,     // the metro underground: tiled walls, pillars, trains rushing by, graffiti, station signs
    Miami,      // Miami beach at sunset: palms, lifeguard towers, the ocean, convertibles, a striped sun
    Robo,       // a robot factory: arms assembling on the beat, conveyors, welding sparks, a giant mech
    Castle,     // a knight's castle: battlements, towers, banners, torches, statues, fireballs on the drops
}

/// Video 16's scenery ("Groove"): five worlds, one per level, in the manner of ElementsEnvironment -
/// everything near scrolls with the tunnel and wraps, the sky pieces sit beyond the fog
/// (Custom/Starfield), a global Volume pushes the bloom up on a drop, and all motion and light
/// come from ClubBeat (the song clock), so it is on the music in Play and in a recording.
public class GrooveEnvironment : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [SerializeField] private GrooveSkin skin;
    [SerializeField, Range(0f, 1f)] private float dropBloom = 0.4f;
    [SerializeField] private float bloomPeak = 5f;
    [SerializeField] private int seed = 1616;

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
            case GrooveSkin.Disco: BuildDisco(); break;
            case GrooveSkin.Subway: BuildSubway(); break;
            case GrooveSkin.Miami: BuildMiami(); break;
            case GrooveSkin.Robo: BuildRobo(); break;
            case GrooveSkin.Castle: BuildCastle(); break;
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

    // ---------- Disco ----------

    private void BuildDisco()
    {
        Color pink = new Color(1f, 0.2f, 0.7f), cyan = new Color(0.1f, 0.9f, 1f), yellow = new Color(1f, 0.85f, 0.2f), violet = new Color(0.6f, 0.3f, 1f);
        Group(pink); Group(cyan); Group(yellow); Group(violet);
        var dark = Lit(new Color(0.03f, 0.02f, 0.05f), 0.8f);
        var chrome = Lit(new Color(0.8f, 0.8f, 0.85f), 0.95f, 1f);
        var root = Root("Disco");

        // Lit dance floor tiles on both sides, flashing in patterns on the beat.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 6f)
            {
                var f = Prop(root, "Floor", new Vector3(side * 10f, -0.55f, z + 3f));
                Place("Base", cube, dark, f, Vector3.zero, new Vector3(6.2f, 0.05f, 6.2f));
                for (int x = 0; x < 4; x++)
                    for (int y = 0; y < 4; y++)
                    {
                        var tile = Place("Tile", cube, groupMats[(x + y) % 4], f, new Vector3(-2.25f + x * 1.5f, 0.04f, -2.25f + y * 1.5f), new Vector3(1.4f, 0.04f, 1.4f));
                        flashers.Add(tile);
                    }
                scrollers.Add(new Mover { t = f });
            }
        // Speaker stacks pulsing on the kick, mirror balls turning above the road edges.
        int n = 0;
        for (float z = -Behind; z < Far; z += 8f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var sp = Prop(root, "Speakers", new Vector3(side * 7.6f, -0.5f, z + (side > 0 ? 4f : 0f)));
                for (int k = 0; k < 3; k++)
                {
                    Place("Box", cube, dark, sp, new Vector3(0f, 0.7f + k * 1.4f, 0f), new Vector3(1.3f, 1.35f, 1.1f));
                    var cone = Place("Cone", cyl, groupMats[(k + n) % 4], sp, new Vector3(-side * 0.66f, 0.7f + k * 1.4f, 0f), new Vector3(0.9f, 0.04f, 0.9f), new Vector3(0f, 0f, 90f));
                    pulsers.Add(new Mover { t = cone.transform, group = (k + n) % 4, size = 0.9f });
                }
                scrollers.Add(new Mover { t = sp });
            }
        for (float z = -Behind + 4f; z < Far; z += 16f)
            foreach (int side in new[] { -1, 1 })
            {
                var b = Prop(root, "MirrorBall", new Vector3(side * 6f, 9.5f, z + (side > 0 ? 8f : 0f)));
                RemixGeo.Beam(b, "Wire", cube, chrome, Vector3.zero, Vector3.up * 4f, 0.04f);
                var ball = Prop(b, "Ball", Vector3.zero);
                Place("Core", sphere, chrome, ball, Vector3.zero, Vector3.one * 1.6f);
                for (int k = 0; k < 18; k++)
                {
                    var d = Random.onUnitSphere;
                    Place("Facet", cube, groupMats[k % 4], ball, d * 0.8f, Vector3.one * 0.18f);
                }
                floaters.Add(new Mover { t = ball, group = -8, speed = 60f });
                scrollers.Add(new Mover { t = b });
            }
        // Spotlights on the walls sweeping their beams.
        for (float z = -Behind; z < Far; z += 10f)
            foreach (int side in new[] { -1, 1 })
            {
                var w = Prop(root, "Wall", new Vector3(side * 14f, 6f, z + 5f));
                Place("Panel", cube, dark, w, Vector3.zero, new Vector3(0.4f, 14f, 10f));
                RemixGeo.Beam(w, "Stripe", cube, groupMats[n++ % 4], new Vector3(-side * 0.22f, -3f, -5f), new Vector3(-side * 0.22f, -3f, 5f), 0.15f);
                var pivot = Prop(w, "Spot", new Vector3(-side * 0.5f, 5f, 0f));
                RemixGeo.Beam(pivot, "Beam", cube, groupMats[(n + 1) % 4], Vector3.zero, new Vector3(-side * 9f, -6f, 0f), 0.25f);
                floaters.Add(new Mover { t = pivot, group = -8, speed = side * 40f });
                scrollers.Add(new Mover { t = w });
            }
        glowSkyMat = SkyQuad("Ball", new Vector3(0f, 80f, 340f), 45f, 45f, new Color(0.9f, 0.85f, 1f, 1f), Mask(128, 128, (x, y) =>
        {
            float dx = x * 2f - 1f, dy = y * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
            float grid = (Mathf.Repeat(x * 14f, 1f) < 0.12f || Mathf.Repeat(y * 14f, 1f) < 0.12f) ? 0.35f : 1f;
            return r < 0.95f ? grid : 0f;
        }), 0.8f);
        particleMats = new[] { groupMats[0], groupMats[1], groupMats[2] };
        ParticleBox("Sparkle", 140, cube, particleMats, Vector3.one * 0.07f, new Vector2(0.4f, 1.2f));
    }

    // ---------- Subway ----------

    private void BuildSubway()
    {
        Color sign = new Color(1f, 0.8f, 0.1f), red = new Color(1f, 0.15f, 0.2f), green = new Color(0.2f, 1f, 0.4f), cyan = new Color(0.1f, 0.85f, 1f), pink = new Color(1f, 0.3f, 0.8f);
        Group(sign); Group(red); Group(green); Group(cyan); Group(pink);
        var tile = Lit(new Color(0.55f, 0.58f, 0.6f), 0.7f);
        var concrete = Lit(new Color(0.12f, 0.12f, 0.13f), 0.3f);
        var steel = Lit(new Color(0.35f, 0.36f, 0.4f), 0.7f, 0.8f);
        var train = Lit(new Color(0.6f, 0.62f, 0.66f), 0.8f, 0.6f);
        var root = Root("Subway");

        // Tiled walls with a coloured band and graffiti pieces.
        int n = 0;
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 8f, n++)
            {
                var w = Prop(root, "Wall", new Vector3(side * 13.5f, 3.5f, z + 4f));
                Place("Tiles", cube, tile, w, Vector3.zero, new Vector3(0.4f, 9f, 8f));
                RemixGeo.Beam(w, "Band", cube, groupMats[n % 2 == 0 ? 3 : 0], new Vector3(-side * 0.22f, 0.5f, -4f), new Vector3(-side * 0.22f, 0.5f, 4f), 0.5f);
                for (int k = 0; k < 4; k++)
                    Place("Tag", sphere, groupMats[rng.Next(5)], w, new Vector3(-side * 0.22f, Rand(-3.5f, -1f), Rand(-3.5f, 3.5f)), new Vector3(0.05f, Rand(0.6f, 1.6f), Rand(1f, 2.5f)));
                if (n % 3 == 0)
                {
                    var s = Place("Sign", cube, groupMats[0], w, new Vector3(-side * 0.25f, 2.5f, 0f), new Vector3(0.05f, 0.8f, 3f));
                    pulsers.Add(new Mover { t = s.transform, group = 0, size = -1f });
                }
                scrollers.Add(new Mover { t = w });
            }
        // Pillars between the road and the tracks, a lamp on each that flickers.
        for (float z = -Behind; z < Far; z += 5f)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Pillar", new Vector3(side * 7.3f, -0.5f, z));
                Place("Column", cube, concrete, p, new Vector3(0f, 4f, 0f), new Vector3(0.7f, 8f, 0.7f));
                Place("Stripe", cube, groupMats[0], p, new Vector3(0f, 0.6f, 0f), new Vector3(0.72f, 0.25f, 0.72f));
                var lamp = Place("Lamp", cube, groupMats[3], p, new Vector3(-side * 0.38f, 5.5f, 0f), new Vector3(0.08f, 0.2f, 0.6f));
                flashers.Add(lamp);
                scrollers.Add(new Mover { t = p });
            }
        // Tracks and trains rushing by in both directions.
        foreach (int side in new[] { -1, 1 })
        {
            for (float z = -Behind; z < Far; z += 12f)
            {
                var t = Prop(root, "Track", new Vector3(side * 10.2f, -0.5f, z + 6f));
                RemixGeo.Beam(t, "Rail", cube, steel, new Vector3(-0.7f, 0.1f, -6f), new Vector3(-0.7f, 0.1f, 6f), 0.1f);
                RemixGeo.Beam(t, "Rail", cube, steel, new Vector3(0.7f, 0.1f, -6f), new Vector3(0.7f, 0.1f, 6f), 0.1f);
                RemixGeo.Beam(t, "Third", cube, groupMats[1], new Vector3(side * 1.1f, 0.15f, -6f), new Vector3(side * 1.1f, 0.15f, 6f), 0.06f);
                scrollers.Add(new Mover { t = t });
            }
            var tr = Prop(root, "Train", new Vector3(side * 10.2f, -0.3f, Rand(10f, Far)));
            for (int k = 0; k < 4; k++)
            {
                Place("Car", cube, train, tr, new Vector3(0f, 1.6f, -k * 9f), new Vector3(2.6f, 3f, 8.6f));
                Place("Windows", cube, groupMats[0], tr, new Vector3(-side * 1.32f, 2f, -k * 9f), new Vector3(0.04f, 0.8f, 7.5f));
                Place("Stripe", cube, groupMats[side > 0 ? 1 : 3], tr, new Vector3(-side * 1.32f, 1f, -k * 9f), new Vector3(0.04f, 0.2f, 8.4f));
            }
            Place("Headlight", sphere, groupMats[0], tr, new Vector3(0f, 1.2f, 4.4f), Vector3.one * 0.5f);
            riders.Add(new Mover { t = tr, speed = side > 0 ? 3f : -1.5f });
        }
        glowSkyMat = SkyQuad("TunnelLight", new Vector3(0f, 10f, 345f), 60f, 40f, new Color(1f, 0.9f, 0.6f, 0.8f), Sprite(128, 1.2f), 0.7f);
        particleMats = new[] { groupMats[0], groupMats[1] };
        ParticleBox("Sparks", 100, cube, particleMats, Vector3.one * 0.05f, new Vector2(3f, 7f), 13f, 0f, 3f);
    }

    // ---------- Miami ----------

    private void BuildMiami()
    {
        Color pink = new Color(1f, 0.35f, 0.65f), cyan = new Color(0.2f, 0.95f, 1f), sun = new Color(1f, 0.6f, 0.25f), mint = new Color(0.4f, 1f, 0.75f);
        Group(pink); Group(cyan); Group(sun); Group(mint);
        var sand = Lit(new Color(0.45f, 0.32f, 0.25f), 0.25f);
        var sea = Lit(new Color(0.05f, 0.1f, 0.2f), 0.95f, 0.2f);
        var trunk = Lit(new Color(0.25f, 0.15f, 0.1f), 0.3f);
        var frond = Lit(new Color(0.05f, 0.18f, 0.12f), 0.3f);
        var pastel = Lit(new Color(0.9f, 0.6f, 0.7f), 0.4f);
        var root = Root("Miami");

        // The beach on the right, the ocean on the left with rolling crests.
        for (float z = -Behind; z < Far; z += 12f)
        {
            var b = Prop(root, "Beach", new Vector3(11f, -0.55f, z + 6f));
            Place("Sand", cube, sand, b, Vector3.zero, new Vector3(9f, 0.06f, 12f));
            scrollers.Add(new Mover { t = b });
            var s = Prop(root, "Ocean", new Vector3(-16f, -0.8f, z + 6f));
            Place("Water", cube, sea, s, Vector3.zero, new Vector3(20f, 0.05f, 12f));
            for (int k = 0; k < 3; k++)
                RemixGeo.Beam(s, "Crest", cube, groupMats[1], new Vector3(Rand(-8f, 9f), 0.06f, -5f + k * 4f), new Vector3(Rand(-8f, 9f), 0.06f, -3.5f + k * 4f), 0.08f);
            scrollers.Add(new Mover { t = s });
        }
        // Palms along both sides, swaying.
        int n = 0;
        for (float z = -Behind; z < Far; z += 5f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Palm", new Vector3(side * Rand(7.6f, 8.6f), -0.5f, z + (side > 0 ? 2.5f : 0f)));
                float lean = -side * Rand(0.15f, 0.3f);
                const int seg = 6;
                for (int k = 0; k < seg; k++)
                {
                    // Segments side by side under the palm (the sway turns each a little more).
                    var s = Prop(p, "Seg", new Vector3(lean * k, 1.2f * k, 0f));
                    Vector3 b = new Vector3(lean, 1.2f, 0f);
                    RemixGeo.Beam(s, "Trunk", cyl, trunk, Vector3.zero, b, 0.32f - k * 0.025f);
                    if (k == seg - 1)
                        for (int fr = 0; fr < 7; fr++)
                        {
                            float ang = fr * Mathf.PI * 2f / 7f;
                            RemixGeo.Beam(s, "Frond", cube, fr % 2 == 0 ? frond : groupMats[3], b, b + new Vector3(Mathf.Cos(ang) * 2.4f, -0.9f, Mathf.Sin(ang) * 2.4f), 0.22f);
                        }
                }
                floaters.Add(new Mover { t = p, seed = Rand(0f, 6.28f), group = -6 });
            }        // Lifeguard towers on the beach, neon signs.
        for (float z = -Behind; z < Far; z += Rand(14f, 18f))
        {
            var t = Prop(root, "Tower", new Vector3(13f, -0.5f, z));
            foreach (var l in new[] { new Vector3(-0.8f, 0f, -0.8f), new Vector3(0.8f, 0f, -0.8f), new Vector3(-0.8f, 0f, 0.8f), new Vector3(0.8f, 0f, 0.8f) })
                RemixGeo.Beam(t, "Stilt", cube, pastel, l, l * 0.6f + Vector3.up * 2.5f, 0.15f);
            Place("Hut", cube, pastel, t, new Vector3(0f, 3.3f, 0f), new Vector3(2f, 1.6f, 2f));
            Place("Roof", cube, groupMats[0], t, new Vector3(0f, 4.3f, 0f), new Vector3(2.4f, 0.25f, 2.4f));
            var s = Place("Neon", cube, groupMats[1], t, new Vector3(-1.02f, 3.3f, 0f), new Vector3(0.04f, 0.4f, 1.6f));
            pulsers.Add(new Mover { t = s.transform, group = 1, size = -1f });
            scrollers.Add(new Mover { t = t });
        }
        // Convertibles cruising the beach road, gulls overhead.
        for (int k = 0; k < 3; k++)
        {
            var car = Prop(root, "Convertible", new Vector3(10f, -0.3f, Rand(10f, Far)));
            Place("Body", cube, groupMats[k % 2 == 0 ? 0 : 1], car, new Vector3(0f, 0.45f, 0f), new Vector3(1.9f, 0.55f, 4.4f));
            Place("Screen", cube, pastel, car, new Vector3(0f, 0.95f, 0.6f), new Vector3(1.7f, 0.5f, 0.06f), new Vector3(-25f, 0f, 0f));
            foreach (var w in new[] { new Vector3(-0.95f, 0.2f, 1.4f), new Vector3(0.95f, 0.2f, 1.4f), new Vector3(-0.95f, 0.2f, -1.4f), new Vector3(0.95f, 0.2f, -1.4f) })
                Place("Wheel", cyl, trunk, car, w, new Vector3(0.6f, 0.15f, 0.6f), new Vector3(0f, 0f, 90f));
            riders.Add(new Mover { t = car, speed = k == 1 ? 0.4f : -0.6f });
        }
        for (int i = 0; i < 3; i++)
        {
            var g = Prop(root, "Gull", Vector3.zero);
            RemixGeo.Beam(g, "Wing", cube, pastel, new Vector3(-0.9f, 0.3f, 0f), Vector3.zero, 0.12f);
            RemixGeo.Beam(g, "Wing", cube, pastel, new Vector3(0.9f, 0.3f, 0f), Vector3.zero, 0.12f);
            ufos.Add(new Mover { t = g, seed = i * 2.3f, speed = 0.12f + i * 0.04f });
        }
        glowSkyMat = SkyQuad("Sun", new Vector3(-30f, 35f, 345f), 70f, 70f, new Color(1f, 0.45f, 0.5f, 1f), Mask(128, 128, (x, y) =>
        {
            float dx = x * 2f - 1f, dy = y * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
            float band = (Mathf.Repeat(y * 10f, 1f) < 0.35f && y < 0.5f) ? 0f : 1f;
            return (r < 0.95f ? 1f : 0f) * band;
        }), 1f);
        SkyQuad("Glow", new Vector3(0f, 20f, 350f), 260f, 50f, new Color(1f, 0.4f, 0.6f, 0.5f), Sprite(128, 1.5f), 0.6f);
        particleMats = new[] { groupMats[0], groupMats[1] };
        ParticleBox("Glints", 90, sphere, particleMats, Vector3.one * 0.07f, new Vector2(-0.3f, 0.3f), 14f, 0.5f, 8f);
    }

    // ---------- Robo factory ----------

    private void BuildRobo()
    {
        Color hazard = new Color(1f, 0.8f, 0.1f), cyan = new Color(0.1f, 0.9f, 1f), red = new Color(1f, 0.2f, 0.15f), weld = new Color(0.7f, 0.85f, 1f);
        Group(hazard); Group(cyan); Group(red); Group(weld);
        var metal = Lit(new Color(0.22f, 0.23f, 0.26f), 0.7f, 0.8f);
        var dark = Lit(new Color(0.06f, 0.06f, 0.07f), 0.5f);
        var crate = Lit(new Color(0.35f, 0.25f, 0.15f), 0.3f);
        var root = Root("Robo");

        // Conveyor belts on both sides with crates riding, hazard stripes on their edges.
        foreach (int side in new[] { -1, 1 })
        {
            for (float z = -Behind; z < Far; z += 6f)
            {
                var c = Prop(root, "Conveyor", new Vector3(side * 8.4f, -0.5f, z + 3f));
                Place("Belt", cube, dark, c, new Vector3(0f, 0.8f, 0f), new Vector3(1.8f, 0.2f, 6f));
                for (int k = 0; k < 6; k++)
                    Place("Hazard", cube, k % 2 == 0 ? groupMats[0] : dark, c, new Vector3(-side * 0.95f, 0.8f, -2.5f + k), new Vector3(0.1f, 0.22f, 1f));
                RemixGeo.Beam(c, "Leg", cube, metal, new Vector3(0f, 0f, -2.8f), new Vector3(0f, 0.7f, -2.8f), 0.2f);
                scrollers.Add(new Mover { t = c });
            }
            for (int k = 0; k < 8; k++)
            {
                var box = Place("Crate", cube, crate, root, new Vector3(side * 8.4f, 0.75f, Rand(0f, Far)), Vector3.one * 0.9f);
                riders.Add(new Mover { t = box.transform, speed = 0.6f });
            }
        }
        // Robot arms: a base, two segments and a claw, turning to the beat, welding sparks.
        int n = 0;
        for (float z = -Behind; z < Far; z += 7f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var arm = Prop(root, "Arm", new Vector3(side * 10.6f, -0.5f, z + (side > 0 ? 3.5f : 0f)));
                Place("Base", cyl, metal, arm, new Vector3(0f, 0.4f, 0f), new Vector3(1.2f, 0.4f, 1.2f));
                var turn = Prop(arm, "Turn", new Vector3(0f, 0.8f, 0f));
                RemixGeo.Beam(turn, "Upper", cube, groupMats[0], Vector3.zero, new Vector3(-side * 1f, 2.6f, 0f), 0.35f);
                RemixGeo.Beam(turn, "Fore", cube, metal, new Vector3(-side * 1f, 2.6f, 0f), new Vector3(-side * 2.6f, 1.4f, 0f), 0.28f);
                Place("Joint", sphere, groupMats[1], turn, new Vector3(-side * 1f, 2.6f, 0f), Vector3.one * 0.45f);
                var tip = Place("Torch", sphere, groupMats[3], turn, new Vector3(-side * 2.7f, 1.3f, 0f), Vector3.one * 0.35f);
                flashers.Add(tip);
                floaters.Add(new Mover { t = turn, group = -8, speed = (n % 2 == 0 ? 1f : -1f) * 35f });
                scrollers.Add(new Mover { t = arm });
            }
        // The factory walls: girders and warning lights.
        for (float z = -Behind; z < Far; z += 12f)
            foreach (int side in new[] { -1, 1 })
            {
                var w = Prop(root, "Wall", new Vector3(side * 15f, 7f, z + 6f));
                Place("Panel", cube, dark, w, Vector3.zero, new Vector3(0.5f, 16f, 12f));
                for (int k = 0; k < 3; k++) RemixGeo.Beam(w, "Girder", cube, metal, new Vector3(-side * 0.4f, -6f + k * 5f, -6f), new Vector3(-side * 0.4f, -6f + k * 5f, 6f), 0.4f);
                var beacon = Place("Beacon", sphere, groupMats[2], w, new Vector3(-side * 0.5f, 3.5f, 0f), Vector3.one * 0.6f);
                pulsers.Add(new Mover { t = beacon.transform, group = 2, size = 0.6f });
                scrollers.Add(new Mover { t = w });
            }
        // Sky: a giant mech silhouette with glowing eyes.
        glowSkyMat = SkyQuad("Mech", new Vector3(0f, 55f, 345f), 70f, 90f, new Color(0.2f, 0.25f, 0.3f, 1f), Mask(96, 128, (x, y) =>
        {
            bool head = Mathf.Abs(x - 0.5f) < 0.09f && y > 0.78f && y < 0.9f;
            bool body = Mathf.Abs(x - 0.5f) < 0.2f && y > 0.45f && y < 0.78f;
            bool arms = (Mathf.Abs(x - 0.24f) < 0.06f || Mathf.Abs(x - 0.76f) < 0.06f) && y > 0.35f && y < 0.75f;
            bool legs = (Mathf.Abs(x - 0.4f) < 0.07f || Mathf.Abs(x - 0.6f) < 0.07f) && y < 0.45f;
            return head || body || arms || legs ? 1f : 0f;
        }), 0.6f);
        SkyQuad("Eyes", new Vector3(0f, 98f, 344f), 10f, 2.5f, new Color(1f, 0.2f, 0.15f, 1f), Sprite(64, 0.6f), 1.2f);
        particleMats = new[] { groupMats[0], groupMats[3] };
        ParticleBox("Sparks", 130, cube, particleMats, Vector3.one * 0.05f, new Vector2(3f, 8f));
    }

    // ---------- Castle ----------

    private void BuildCastle()
    {
        Color torch = new Color(1f, 0.5f, 0.12f), royal = new Color(0.25f, 0.4f, 1f), crimson = new Color(1f, 0.15f, 0.2f), gold = new Color(1f, 0.8f, 0.25f);
        Group(torch); Group(royal); Group(crimson); Group(gold);
        var stone = Lit(new Color(0.26f, 0.25f, 0.24f), 0.2f);
        var darkStone = Lit(new Color(0.12f, 0.12f, 0.13f), 0.2f);
        var steel = Lit(new Color(0.55f, 0.56f, 0.6f), 0.85f, 0.9f);
        var root = Root("Castle");

        // Castle walls with battlements, banners hanging, torches between them.
        int n = 0;
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 8f, n++)
            {
                var w = Prop(root, "Wall", new Vector3(side * 11.5f, -0.5f, z + 4f));
                Place("Body", cube, stone, w, new Vector3(0f, 4f, 0f), new Vector3(2f, 8f, 8f));
                for (int k = 0; k < 4; k++)
                    Place("Merlon", cube, stone, w, new Vector3(0f, 8.5f, -3f + k * 2f), new Vector3(2f, 1f, 1f));
                var banner = Prop(w, "Banner", new Vector3(-side * 1.05f, 7.4f, 0f));
                var cloth = Place("Cloth", cube, groupMats[n % 2 == 0 ? 1 : 2], banner, new Vector3(0f, -1.6f, 0f), new Vector3(0.05f, 3.2f, 1.6f));
                Place("Emblem", sphere, groupMats[3], banner, new Vector3(-side * 0.04f, -1.4f, 0f), new Vector3(0.05f, 0.6f, 0.6f));
                floaters.Add(new Mover { t = banner, seed = Rand(0f, 6.28f), group = -6 });
                if (n % 2 == 0)
                {
                    RemixGeo.Beam(w, "Sconce", cube, darkStone, new Vector3(-side * 1.05f, 3f, 3f), new Vector3(-side * 1.5f, 3.4f, 3f), 0.15f);
                    var fl = Place("Fire", sphere, groupMats[0], w, new Vector3(-side * 1.5f, 3.8f, 3f), new Vector3(0.4f, 0.65f, 0.4f));
                    floaters.Add(new Mover { t = fl.transform, seed = Rand(0f, 6.28f), group = -5 });
                }
                scrollers.Add(new Mover { t = w });
            }
        // Towers with conical roofs further out.
        n = 0;
        for (float z = -Behind; z < Far; z += Rand(14f, 20f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var t = Prop(root, "Tower", new Vector3(side * Rand(17f, 20f), -0.5f, z));
            float h = Rand(12f, 18f);
            Place("Body", cyl, stone, t, new Vector3(0f, h * 0.5f, 0f), new Vector3(4f, h * 0.5f, 4f));
            for (int k = 0; k < 4; k++) Place("Roof", cyl, groupMats[side < 0 ? 1 : 2], t, new Vector3(0f, h + 0.5f + k * 0.9f, 0f), new Vector3(4.6f - k * 1.15f, 0.45f, 4.6f - k * 1.15f));
            var win = Place("Window", cube, groupMats[0], t, new Vector3(-side * 2f, h * 0.7f, 0f), new Vector3(0.05f, 1.2f, 0.6f));
            pulsers.Add(new Mover { t = win.transform, group = 0, size = -1f });
            Place("Flag", cube, groupMats[3], t, new Vector3(0f, h + 4.8f, 0.5f), new Vector3(0.05f, 0.6f, 1f));
            scrollers.Add(new Mover { t = t });
        }
        // Knight statues on plinths by the road.
        for (float z = -Behind + 3f; z < Far; z += 12f)
            foreach (int side in new[] { -1, 1 })
            {
                var k = Prop(root, "Knight", new Vector3(side * 7.8f, -0.5f, z + (side > 0 ? 6f : 0f)));
                Place("Plinth", cube, darkStone, k, new Vector3(0f, 0.5f, 0f), new Vector3(1.2f, 1f, 1.2f));
                Place("Body", cube, steel, k, new Vector3(0f, 2f, 0f), new Vector3(0.8f, 2f, 0.5f));
                Place("Helm", sphere, steel, k, new Vector3(0f, 3.3f, 0f), Vector3.one * 0.55f);
                Place("Visor", cube, groupMats[0], k, new Vector3(-side * 0.27f, 3.3f, 0f), new Vector3(0.04f, 0.08f, 0.35f));
                RemixGeo.Beam(k, "Sword", cube, steel, new Vector3(-side * 0.5f, 1.2f, 0f), new Vector3(-side * 0.5f, 3.6f, 0f), 0.1f);
                scrollers.Add(new Mover { t = k });
            }
        // Fireballs from the catapults on the drops: an arc of fire across the sky.
        for (float z = -Behind + 6f; z < Far; z += 12f)
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var b = Prop(root, "Fireball", new Vector3(side * 14f, 0f, z));
            for (int k = 0; k < 9; k++)
            {
                float u = k / 8f;
                Place("Fire", sphere, groupMats[k == 8 ? 3 : 0], b, new Vector3(-side * u * 18f, 4f + Mathf.Sin(u * Mathf.PI) * 9f, 0f), Vector3.one * (0.4f + u * 0.7f));
            }
            b.gameObject.SetActive(false);
            bolts.Add(new Mover { t = b });
        }
        glowSkyMat = SkyQuad("Moon", new Vector3(90f, 85f, 340f), 30f, 30f, new Color(0.9f, 0.9f, 1f, 1f), Sprite(128, 0.5f), 1f);
        var keep = Mask(256, 96, (x, y) =>
        {
            float h = 0.3f;
            if (x > 0.3f && x < 0.7f) h = 0.55f;
            if ((x > 0.25f && x < 0.3f) || (x > 0.7f && x < 0.75f) || (x > 0.47f && x < 0.53f)) h = 0.85f;
            if (h > 0.5f && Mathf.Repeat(x * 60f, 1f) < 0.4f && y > h - 0.05f) return 0f;
            return y < h ? 1f : 0f;
        });
        SkyQuad("Keep", new Vector3(0f, 25f, 345f), 220f, 60f, new Color(0.08f, 0.07f, 0.12f, 1f), keep, 0.8f);
        particleMats = new[] { groupMats[0] };
        ParticleBox("Embers", 110, sphere, particleMats, Vector3.one * 0.07f, new Vector2(-1.2f, -0.4f));
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
                float h = Mathf.Repeat(Mathf.Sin((i + 1) * 12.9898f + seedBeat * 0.001f + (skin == GrooveSkin.Disco ? Mathf.Floor(t * 8f) * 3.1f : 0f)) * 43758.5453f, 1f);
                flashers[i].enabled = h < frac;
            }
        }

        // Fireballs (Castle): one flies on every other beat with a drop.
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
            p.z -= delta * (skin == GrooveSkin.Disco ? 1.4f : 0.5f);
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
