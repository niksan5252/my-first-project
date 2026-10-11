using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum OdysseySkin
{
    Racing,     // a night race track: grandstands with camera flashes, tyre walls, kerbs, cars overtaking alongside
    Alien,      // an alien planet: giant glowing mushroom trees, crystal cacti, tentacle plants, UFOs, a ringed planet
    WildWest,   // the Wild West at sunset: mesas, neon cacti, saloons, a steam train racing alongside, windmills
    Pirate,     // a pirate cove at night: ships rocking at anchor, a lighthouse sweeping, docks, torches, cannons
    Aztec,      // a lost Aztec city: step pyramids, fire bowls, idol heads with burning eyes, waterfalls, sun stone
}

/// Video 15's scenery ("Odyssey"): five worlds, one per level, in the manner of ElementsEnvironment -
/// everything near scrolls with the tunnel and wraps, the sky pieces sit beyond the fog
/// (Custom/Starfield), a global Volume pushes the bloom up on a drop, and all motion and light
/// come from ClubBeat (the song clock), so it is on the music in Play and in a recording.
public class OdysseyEnvironment : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [SerializeField] private OdysseySkin skin;
    [SerializeField, Range(0f, 1f)] private float dropBloom = 0.4f;
    [SerializeField] private float bloomPeak = 5f;
    [SerializeField] private int seed = 1515;

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
            case OdysseySkin.Racing: BuildRacing(); break;
            case OdysseySkin.Alien: BuildAlien(); break;
            case OdysseySkin.WildWest: BuildWildWest(); break;
            case OdysseySkin.Pirate: BuildPirate(); break;
            case OdysseySkin.Aztec: BuildAztec(); break;
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

    // ---------- Racing ----------

    private void BuildRacing()
    {
        Color red = new Color(1f, 0.1f, 0.15f), white = new Color(1f, 1f, 1f), yellow = new Color(1f, 0.9f, 0.1f), cyan = new Color(0.1f, 0.9f, 1f);
        Group(red); Group(white); Group(yellow); Group(cyan);
        var asphalt = Lit(new Color(0.05f, 0.05f, 0.06f), 0.6f);
        var concrete = Lit(new Color(0.25f, 0.25f, 0.28f), 0.3f);
        var tyre = Lit(new Color(0.03f, 0.03f, 0.03f), 0.2f);
        var steel = Lit(new Color(0.3f, 0.3f, 0.35f), 0.6f, 0.7f);
        var crowd = Lit(new Color(0.12f, 0.1f, 0.14f), 0.2f);
        var root = Root("Racing");

        // Kerbs (red/white stripes) along both road edges.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 1.2f)
            {
                int k = Mathf.FloorToInt((z + Behind) / 1.2f);
                var kb = Place("Kerb", cube, k % 2 == 0 ? groupMats[0] : groupMats[1], root, new Vector3(side * 6.6f, -0.45f, z), new Vector3(0.7f, 0.08f, 1.15f));
                scrollers.Add(new Mover { t = kb.transform });
            }
        // Side lanes of asphalt where the cars race, tyre walls beyond them.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var l = Prop(root, "SideLane", new Vector3(side * 9.6f, -0.52f, z + 6f));
                Place("Asphalt", cube, asphalt, l, Vector3.zero, new Vector3(5f, 0.05f, 12f));
                RemixGeo.Beam(l, "Line", cube, groupMats[1], new Vector3(0f, 0.04f, -6f), new Vector3(0f, 0.04f, -2f), 0.1f);
                RemixGeo.Beam(l, "Line", cube, groupMats[1], new Vector3(0f, 0.04f, 1f), new Vector3(0f, 0.04f, 5f), 0.1f);
                for (float s = -5.5f; s < 6f; s += 1f)
                    for (int h = 0; h < 2; h++)
                        Place("Tyre", cyl, h == 1 && Mathf.RoundToInt(s) % 2 == 0 ? groupMats[0] : tyre, l, new Vector3(side * 2.8f, 0.2f + h * 0.38f, s), new Vector3(0.9f, 0.18f, 0.9f));
                scrollers.Add(new Mover { t = l });
            }
        // Grandstands: tiers of crowd with camera flashes, floodlight towers above them.
        n0 = 0;
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var g = Prop(root, "Stand", new Vector3(side * 16f, -0.5f, z + 6f));
                for (int k = 0; k < 6; k++)
                {
                    Place("Tier", cube, concrete, g, new Vector3(side * k * 0.9f, 0.5f + k * 0.8f, 0f), new Vector3(1f, 1f, 11.8f));
                    for (float s = -5.4f; s < 5.6f; s += 0.6f)
                    {
                        var fan = Place("Fan", cube, crowd, g, new Vector3(side * k * 0.9f, 1.25f + k * 0.8f, s + Rand(-0.1f, 0.1f)), new Vector3(0.35f, 0.5f, 0.35f));
                        if (rng.NextDouble() < 0.12)
                        {
                            var fl = Place("Flash", cube, groupMats[1], g, fan.transform.localPosition + new Vector3(-side * 0.2f, 0.25f, 0f), Vector3.one * 0.14f);
                            fl.enabled = false;
                            flashers.Add(fl);
                        }
                    }
                }
                RemixGeo.Beam(g, "Roof", cube, steel, new Vector3(side * 5.5f, 7.5f, -6f), new Vector3(side * 5.5f, 7.5f, 6f), 0.4f);
                if (n0++ % 2 == 0)
                {
                    RemixGeo.Beam(g, "Mast", cube, steel, new Vector3(side * 6f, 0f, 0f), new Vector3(side * 6f, 14f, 0f), 0.35f);
                    for (int k = 0; k < 6; k++)
                        Place("Lamp", cube, groupMats[1], g, new Vector3(side * 5.7f, 14f + (k / 3) * 0.6f, -0.8f + (k % 3) * 0.8f), new Vector3(0.15f, 0.5f, 0.6f));
                }
                scrollers.Add(new Mover { t = g });
            }
        // Cars racing in the side lanes: some overtake, some are overtaken.
        foreach (int side in new[] { -1, 1 })
            for (int k = 0; k < 3; k++)
            {
                var car = Prop(root, "Car", new Vector3(side * 9.6f, -0.3f, Rand(5f, Far)));
                var col = groupMats[(k + (side > 0 ? 1 : 0)) % 2 == 0 ? 0 : 3];
                Place("Body", cube, col, car, new Vector3(0f, 0.3f, 0f), new Vector3(1.6f, 0.35f, 3.8f));
                Place("Cockpit", cube, tyre, car, new Vector3(0f, 0.6f, -0.2f), new Vector3(0.8f, 0.35f, 1f));
                Place("Wing", cube, col, car, new Vector3(0f, 0.85f, -1.8f), new Vector3(1.8f, 0.08f, 0.5f));
                Place("Nose", cube, col, car, new Vector3(0f, 0.2f, 2.1f), new Vector3(1.8f, 0.06f, 0.4f));
                foreach (var w in new[] { new Vector3(-0.9f, 0.15f, 1.3f), new Vector3(0.9f, 0.15f, 1.3f), new Vector3(-0.9f, 0.15f, -1.3f), new Vector3(0.9f, 0.15f, -1.3f) })
                    Place("Wheel", cyl, tyre, car, w, new Vector3(0.55f, 0.2f, 0.55f), new Vector3(0f, 0f, 90f));
                Place("Tail", cube, groupMats[0], car, new Vector3(0f, 0.35f, -1.95f), new Vector3(1.2f, 0.1f, 0.05f));
                riders.Add(new Mover { t = car, speed = k == 0 ? -0.8f : (k == 1 ? 0.35f : -0.4f) });
            }
        // Sky: a checkered flag and floodlit haze.
        var flag = Mask(96, 64, (x, y) =>
        {
            float wave = 0.06f * Mathf.Sin(x * 9f);
            int cx = Mathf.FloorToInt(x * 8f), cy = Mathf.FloorToInt((y + wave) * 6f);
            return (cx + cy) % 2 == 0 ? 1f : 0.15f;
        });
        glowSkyMat = SkyQuad("Flag", new Vector3(90f, 85f, 340f), 50f, 33f, new Color(1f, 1f, 1f, 0.9f), flag, 0.6f, -8f);
        SkyQuad("Haze", new Vector3(0f, 25f, 345f), 200f, 60f, new Color(0.6f, 0.65f, 1f, 0.35f), Sprite(128, 1.4f), 0.5f);
        particleMats = new[] { groupMats[1] };
        ParticleBox("SpeedLines", 90, cube, particleMats, new Vector3(0.025f, 0.025f, 2.2f), new Vector2(-0.1f, 0.1f), 13f, 0.5f, 10f, 70f, false);
    }
    private int n0;

    // ---------- Alien ----------

    private void BuildAlien()
    {
        Color toxic = new Color(0.45f, 1f, 0.2f), violet = new Color(0.7f, 0.3f, 1f), teal = new Color(0.1f, 1f, 0.85f), pink = new Color(1f, 0.3f, 0.75f);
        Group(toxic); Group(violet); Group(teal); Group(pink);
        var ground = Lit(new Color(0.08f, 0.04f, 0.12f), 0.4f);
        var stalk = Lit(new Color(0.25f, 0.2f, 0.35f), 0.4f);
        var hull = Lit(new Color(0.5f, 0.52f, 0.6f), 0.85f, 0.8f);
        var root = Root("Alien");

        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var f = Prop(root, "Ground", new Vector3(side * 11f, -0.55f, z + 6f));
                Place("Soil", cube, ground, f, Vector3.zero, new Vector3(9f, 0.08f, 12f));
                for (int k = 0; k < 6; k++)
                    Place("Glow", sphere, groupMats[k % 4], f, new Vector3(Rand(-4f, 4f), 0.05f, Rand(-5.5f, 5.5f)), new Vector3(Rand(0.3f, 0.8f), 0.05f, Rand(0.3f, 0.8f)));
                scrollers.Add(new Mover { t = f });
            }
        // Mushroom trees: tall stalks, wide glowing caps with rings under them.
        int n = 0;
        for (float z = -Behind; z < Far; z += Rand(7f, 10f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var m = Prop(root, "Shroom", new Vector3(side * Rand(10f, 14f), -0.5f, z));
            float h = Rand(7f, 13f), w = Rand(4f, 7f);
            Vector3 a = Vector3.zero;
            for (int k = 0; k < 4; k++) { Vector3 b = a + new Vector3(-side * Rand(0f, 0.5f), h / 4f, 0f); RemixGeo.Beam(m, "Stalk", cyl, stalk, a, b, 0.9f - k * 0.12f); a = b; }
            int g = n % 4;
            var cap = Place("Cap", sphere, groupMats[g], m, a, new Vector3(w, w * 0.32f, w));
            pulsers.Add(new Mover { t = cap.transform, group = g, size = -1f });
            for (int k = 0; k < 3; k++)
                Place("Gill", cyl, groupMats[(g + 1) % 4], m, a + Vector3.down * (0.35f + k * 0.18f), new Vector3(w * (0.85f - k * 0.2f), 0.03f, w * (0.85f - k * 0.2f)));
            scrollers.Add(new Mover { t = m });
        }
        // Crystal cacti and tentacle plants near the road.
        for (float z = -Behind; z < Far; z += 3.2f)
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            if (rng.NextDouble() < 0.5)
            {
                var c = Prop(root, "Cactus", new Vector3(side * Rand(7.2f, 9f), -0.5f, z));
                for (int k = 0; k < 3; k++)
                {
                    float h = Rand(0.8f, 2.4f);
                    Place("Pod", sphere, groupMats[k == 0 ? 2 : 3], c, new Vector3(Rand(-0.5f, 0.5f), h * 0.5f, Rand(-0.5f, 0.5f)), new Vector3(0.45f, h, 0.45f));
                }
                scrollers.Add(new Mover { t = c });
            }
            else
            {
                var t = Prop(root, "Tentacle", new Vector3(side * Rand(7.5f, 9.5f), -0.5f, z));
                int seg = rng.Next(6, 10);
                for (int i = 0; i < seg; i++)
                {
                    var s = Prop(t, "Seg", new Vector3(0f, i * 0.55f, 0f));
                    Place("Ring", sphere, i % 2 == 0 ? groupMats[1] : stalk, s, new Vector3(0f, 0.27f, 0f), Vector3.one * (0.5f - i * 0.035f));
                }
                floaters.Add(new Mover { t = t, basePos = t.localPosition, seed = Rand(0f, 6.28f), group = -6 });
            }
        }
        // UFOs crossing over the road, a light beam under each.
        for (int i = 0; i < 3; i++)
        {
            var u = Prop(root, "UFO", Vector3.zero);
            Place("Saucer", sphere, hull, u, Vector3.zero, new Vector3(3.2f, 0.45f, 3.2f));
            Place("Dome", sphere, groupMats[2], u, new Vector3(0f, 0.3f, 0f), new Vector3(1.3f, 0.8f, 1.3f));
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                Place("Light", sphere, groupMats[k % 2 == 0 ? 0 : 3], u, new Vector3(Mathf.Cos(a) * 1.5f, -0.05f, Mathf.Sin(a) * 1.5f), Vector3.one * 0.22f);
            }
            ufos.Add(new Mover { t = u, seed = i * 2.1f, speed = 0.18f + i * 0.05f });
        }
        // Sky: a ringed planet and two moons.
        glowSkyMat = SkyQuad("Planet", new Vector3(-60f, 85f, 335f), 70f, 45f, new Color(0.75f, 0.5f, 1f, 1f), Mask(192, 128, (x, y) =>
        {
            float dx = (x - 0.5f) * 1.5f, dy = y - 0.5f;
            float body = dx * dx + dy * dy < 0.09f ? 0.85f : 0f;
            float rx = (x - 0.5f) / 0.48f, ry = (y - 0.5f) / 0.1f, rr = rx * rx + ry * ry;
            float ring = rr < 1f && rr > 0.62f && !(dy > 0f && dx * dx + dy * dy < 0.09f) ? 1f : 0f;
            return Mathf.Max(body, ring);
        }), 0.9f, -12f);
        SkyQuad("Moon", new Vector3(110f, 110f, 345f), 14f, 14f, new Color(0.5f, 1f, 0.7f, 1f), Sprite(64, 0.5f), 1f);
        SkyQuad("Moon", new Vector3(140f, 70f, 345f), 8f, 8f, new Color(1f, 0.6f, 0.8f, 1f), Sprite(64, 0.5f), 1f);
        particleMats = new[] { groupMats[0], groupMats[2] };
        ParticleBox("Spores", 130, sphere, particleMats, Vector3.one * 0.1f, new Vector2(-0.8f, -0.3f));
    }

    // ---------- Wild West ----------

    private void BuildWildWest()
    {
        Color sunset = new Color(1f, 0.45f, 0.15f), neonGreen = new Color(0.3f, 1f, 0.45f), turq = new Color(0.1f, 0.9f, 0.85f), pink = new Color(1f, 0.3f, 0.6f);
        Group(sunset); Group(neonGreen); Group(turq); Group(pink);
        var sand = Lit(new Color(0.35f, 0.17f, 0.08f), 0.2f);
        var redRock = Lit(new Color(0.4f, 0.13f, 0.07f), 0.2f);
        var wood = Lit(new Color(0.22f, 0.12f, 0.06f), 0.3f);
        var iron = Lit(new Color(0.08f, 0.08f, 0.09f), 0.6f, 0.7f);
        var root = Root("WildWest");

        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var s = Prop(root, "Sand", new Vector3(side * 11f, -0.55f, z + 6f));
                Place("Ground", cube, sand, s, Vector3.zero, new Vector3(9f, 0.06f, 12f));
                scrollers.Add(new Mover { t = s });
            }
        // Neon saguaro cacti near the road.
        int n = 0;
        for (float z = -Behind; z < Far; z += Rand(4f, 6f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var c = Prop(root, "Cactus", new Vector3(side * Rand(7.5f, 9f), -0.5f, z));
            float h = Rand(2.5f, 4.2f);
            var mat = groupMats[n % 3 == 0 ? 2 : 1];
            RemixGeo.Beam(c, "Trunk", cyl, mat, Vector3.zero, Vector3.up * h, 0.45f);
            float y1 = h * Rand(0.35f, 0.55f), y2 = h * Rand(0.5f, 0.7f);
            RemixGeo.Beam(c, "Arm", cyl, mat, new Vector3(0f, y1, 0f), new Vector3(-0.8f, y1, 0f), 0.3f);
            RemixGeo.Beam(c, "Arm", cyl, mat, new Vector3(-0.8f, y1, 0f), new Vector3(-0.8f, y1 + 1f, 0f), 0.3f);
            RemixGeo.Beam(c, "Arm", cyl, mat, new Vector3(0f, y2, 0f), new Vector3(0.7f, y2, 0f), 0.28f);
            RemixGeo.Beam(c, "Arm", cyl, mat, new Vector3(0.7f, y2, 0f), new Vector3(0.7f, y2 + 0.8f, 0f), 0.28f);
            scrollers.Add(new Mover { t = c });
        }
        // Saloons and shops: false fronts with neon signs, a porch, swinging doors.
        n = 0;
        for (float z = -Behind; z < Far; z += Rand(10f, 14f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var b = Prop(root, "Saloon", new Vector3(side * 13f, -0.5f, z));
            float h = Rand(5f, 7f);
            Place("Body", cube, wood, b, new Vector3(0f, h * 0.4f, 0f), new Vector3(5f, h * 0.8f, 6f));
            Place("Front", cube, wood, b, new Vector3(-side * 2.6f, h * 0.5f, 0f), new Vector3(0.3f, h, 6.4f));
            var sign = Place("Sign", cube, groupMats[n % 2 == 0 ? 3 : 2], b, new Vector3(-side * 2.8f, h * 0.82f, 0f), new Vector3(0.05f, 0.7f, 4f));
            pulsers.Add(new Mover { t = sign.transform, group = n % 2 == 0 ? 3 : 2, size = -1f });
            RemixGeo.Beam(b, "SignFrame", cube, groupMats[0], new Vector3(-side * 2.82f, h * 0.82f + 0.4f, -2.05f), new Vector3(-side * 2.82f, h * 0.82f + 0.4f, 2.05f), 0.06f);
            Place("Porch", cube, wood, b, new Vector3(-side * 3.4f, 2.6f, 0f), new Vector3(1.6f, 0.12f, 6.2f));
            RemixGeo.Beam(b, "Post", cube, wood, new Vector3(-side * 4.1f, -0.0f, -2.9f), new Vector3(-side * 4.1f, 2.6f, -2.9f), 0.18f);
            RemixGeo.Beam(b, "Post", cube, wood, new Vector3(-side * 4.1f, -0.0f, 2.9f), new Vector3(-side * 4.1f, 2.6f, 2.9f), 0.18f);
            Place("Door", cube, groupMats[0], b, new Vector3(-side * 2.8f, 1.1f, 0f), new Vector3(0.05f, 1.2f, 1.2f));
            scrollers.Add(new Mover { t = b });
        }
        // Windmills (water pumps): a lattice tower and a turning wheel.
        for (float z = -Behind + 8f; z < Far; z += Rand(22f, 30f))
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var wm = Prop(root, "Windmill", new Vector3(side * Rand(17f, 19f), -0.5f, z));
            RemixGeo.Beam(wm, "Leg", cube, iron, new Vector3(-1f, 0f, -1f), new Vector3(0f, 9f, 0f), 0.15f);
            RemixGeo.Beam(wm, "Leg", cube, iron, new Vector3(1f, 0f, -1f), new Vector3(0f, 9f, 0f), 0.15f);
            RemixGeo.Beam(wm, "Leg", cube, iron, new Vector3(0f, 0f, 1.2f), new Vector3(0f, 9f, 0f), 0.15f);
            var wheel = Prop(wm, "Wheel", new Vector3(-side * 0.3f, 9.2f, 0f));
            for (int k = 0; k < 12; k++)
            {
                float a = k * Mathf.PI / 6f;
                RemixGeo.Beam(wheel, "Blade", cube, k % 2 == 0 ? groupMats[0] : iron, Vector3.zero, new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a)) * 1.8f, 0.22f);
            }
            floaters.Add(new Mover { t = wheel, seed = Rand(0f, 6.28f), group = -1, speed = 70f });
            scrollers.Add(new Mover { t = wm });
        }
        // The railway and a steam train racing alongside, smoke puffing.
        for (float z = -Behind; z < Far; z += 12f)
        {
            var t = Prop(root, "Rails", new Vector3(-21f, -0.48f, z + 6f));
            RemixGeo.Beam(t, "Rail", cube, iron, new Vector3(-0.7f, 0.1f, -6f), new Vector3(-0.7f, 0.1f, 6f), 0.1f);
            RemixGeo.Beam(t, "Rail", cube, iron, new Vector3(0.7f, 0.1f, -6f), new Vector3(0.7f, 0.1f, 6f), 0.1f);
            for (float s = -5.5f; s < 6f; s += 1f) Place("Tie", cube, wood, t, new Vector3(0f, 0.02f, s), new Vector3(2.2f, 0.08f, 0.3f));
            scrollers.Add(new Mover { t = t });
        }
        var train = Prop(root, "Train", new Vector3(-21f, -0.3f, 30f));
        Place("Boiler", cyl, iron, train, new Vector3(0f, 1.6f, 2f), new Vector3(1.6f, 2.2f, 1.6f), new Vector3(90f, 0f, 0f));
        Place("Cab", cube, wood, train, new Vector3(0f, 1.9f, -1f), new Vector3(2f, 2.4f, 2f));
        Place("Stack", cyl, iron, train, new Vector3(0f, 3.1f, 3.4f), new Vector3(0.6f, 0.7f, 0.6f));
        Place("Lamp", sphere, groupMats[0], train, new Vector3(0f, 2.1f, 4.3f), Vector3.one * 0.6f);
        for (int k = 0; k < 4; k++)
        {
            Place("Car", cube, wood, train, new Vector3(0f, 1.5f, -4.5f - k * 4.2f), new Vector3(2f, 2f, 3.8f));
            Place("Window", cube, groupMats[0], train, new Vector3(1.02f, 1.8f, -4.5f - k * 4.2f), new Vector3(0.04f, 0.5f, 2.8f));
        }
        for (int k = 0; k < 5; k++)
        {
            var puff = Place("Smoke", sphere, Lit(new Color(0.4f, 0.35f, 0.33f), 0.05f), train, new Vector3(0f, 4f + k * 0.9f, 3.4f - k * 1.2f), Vector3.one * (0.8f + k * 0.3f));
            pulsers.Add(new Mover { t = puff.transform, group = k % 4, size = 0.8f + k * 0.3f });
        }
        riders.Add(new Mover { t = train, speed = -0.5f });
        // Sky: a huge banded sunset sun and mesas.
        glowSkyMat = SkyQuad("Sun", new Vector3(0f, 40f, 345f), 75f, 75f, new Color(1f, 0.55f, 0.2f, 1f), Mask(128, 128, (x, y) =>
        {
            float dx = x * 2f - 1f, dy = y * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
            float band = (Mathf.Repeat(y * 12f, 1f) < 0.3f && y < 0.4f) ? 0f : 1f;
            return (r < 0.95f ? 1f : 0f) * band;
        }), 1f);
        var mesas = Mask(256, 96, (x, y) =>
        {
            float h = 0.25f;
            if (x > 0.08f && x < 0.22f) h = 0.75f;
            if (x > 0.4f && x < 0.47f) h = 0.95f;
            if (x > 0.62f && x < 0.8f) h = 0.62f;
            if (x > 0.88f && x < 0.93f) h = 0.85f;
            return y < h ? 1f : 0f;
        });
        SkyQuad("Mesas", new Vector3(0f, 22f, 340f), 260f, 55f, new Color(0.25f, 0.06f, 0.06f, 1f), mesas, 0.8f);
        particleMats = new[] { groupMats[0] };
        ParticleBox("Dust", 120, cube, particleMats, Vector3.one * 0.05f, new Vector2(0.1f, 0.4f), 14f, 0f, 6f);
    }

    // ---------- Pirate ----------

    private void BuildPirate()
    {
        Color torch = new Color(1f, 0.55f, 0.15f), gold = new Color(1f, 0.8f, 0.25f), sea = new Color(0.1f, 0.6f, 1f), white = new Color(0.9f, 0.95f, 1f);
        Group(torch); Group(gold); Group(sea); Group(white);
        var water = Lit(new Color(0.01f, 0.04f, 0.09f), 0.95f, 0.2f);
        var wood = Lit(new Color(0.2f, 0.11f, 0.05f), 0.35f);
        var sail = Lit(new Color(0.6f, 0.57f, 0.5f), 0.1f);
        var iron = Lit(new Color(0.07f, 0.07f, 0.08f), 0.6f, 0.8f);
        var stone = Lit(new Color(0.3f, 0.3f, 0.32f), 0.25f);
        var root = Root("Pirate");

        // The sea on both sides, wave crests rolling.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var w = Prop(root, "Sea", new Vector3(side * 16f, -0.9f, z + 6f));
                Place("Water", cube, water, w, Vector3.zero, new Vector3(20f, 0.05f, 12f));
                for (int k = 0; k < 3; k++)
                    RemixGeo.Beam(w, "Crest", cube, groupMats[2], new Vector3(Rand(-9f, 9f), 0.06f, -5f + k * 4f), new Vector3(Rand(-9f, 9f), 0.06f, -3f + k * 4f), 0.06f);
                scrollers.Add(new Mover { t = w });
            }
        // Docks along the road: planks, posts, torches, barrels and treasure chests.
        int n = 0;
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 6f, n++)
            {
                var d = Prop(root, "Dock", new Vector3(side * 8.2f, -0.55f, z + 3f));
                Place("Planks", cube, wood, d, Vector3.zero, new Vector3(3f, 0.15f, 6f));
                RemixGeo.Beam(d, "Post", cyl, wood, new Vector3(side * 1.4f, -1f, -2.8f), new Vector3(side * 1.4f, 1.2f, -2.8f), 0.3f);
                if (n % 2 == 0)
                {
                    RemixGeo.Beam(d, "TorchPole", cube, wood, new Vector3(-side * 1.2f, 0f, 0f), new Vector3(-side * 1.2f, 2.2f, 0f), 0.12f);
                    var fl = Place("Fire", sphere, groupMats[0], d, new Vector3(-side * 1.2f, 2.55f, 0f), new Vector3(0.4f, 0.65f, 0.4f));
                    floaters.Add(new Mover { t = fl.transform, seed = Rand(0f, 6.28f), group = -5 });
                    Place("Barrel", cyl, wood, d, new Vector3(0.3f, 0.55f, 1.6f), new Vector3(0.8f, 0.5f, 0.8f));
                    Place("Hoop", cyl, iron, d, new Vector3(0.3f, 0.75f, 1.6f), new Vector3(0.84f, 0.04f, 0.84f));
                }
                else
                {
                    Place("Chest", cube, wood, d, new Vector3(0f, 0.4f, 0.5f), new Vector3(1f, 0.6f, 0.7f));
                    var goldPile = Place("Gold", sphere, groupMats[1], d, new Vector3(0f, 0.75f, 0.5f), new Vector3(0.85f, 0.35f, 0.6f));
                    pulsers.Add(new Mover { t = goldPile.transform, group = 1, size = -1f });
                    Place("Cannon", cyl, iron, d, new Vector3(side * 0.6f, 0.45f, -1.4f), new Vector3(0.35f, 0.7f, 0.35f), new Vector3(0f, 0f, side * 70f));
                    var muzzle = Place("Muzzle", sphere, groupMats[0], d, new Vector3(-side * 0.2f, 0.75f, -1.4f), Vector3.one * 0.7f);
                    muzzle.enabled = false;
                    flashers.Add(muzzle);
                }
                scrollers.Add(new Mover { t = d });
            }
        // Ships at anchor, rocking: a hull, two masts with sails, a flag, lit portholes.
        n = 0;
        for (float z = -Behind; z < Far; z += Rand(16f, 22f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var ship = Prop(root, "Ship", new Vector3(side * Rand(17f, 21f), -0.6f, z));
            Place("Hull", cube, wood, ship, new Vector3(0f, 0.9f, 0f), new Vector3(3.6f, 1.8f, 11f));
            Place("Bow", cube, wood, ship, new Vector3(0f, 1.3f, 6f), new Vector3(2.4f, 1.4f, 2.2f), new Vector3(0f, 45f, 0f));
            Place("Stern", cube, wood, ship, new Vector3(0f, 2.4f, -4.5f), new Vector3(3.6f, 1.4f, 2.2f));
            for (int k = 0; k < 5; k++)
                Place("Porthole", sphere, groupMats[0], ship, new Vector3(-side * 1.82f, 1.1f, -3.6f + k * 1.8f), new Vector3(0.05f, 0.3f, 0.3f));
            foreach (float mz in new[] { -1.8f, 2.4f })
            {
                RemixGeo.Beam(ship, "Mast", cyl, wood, new Vector3(0f, 1.8f, mz), new Vector3(0f, 11f, mz), 0.3f);
                Place("Sail", cube, sail, ship, new Vector3(0f, 6.5f, mz), new Vector3(0.08f, 3.2f, 3.6f));
                Place("Sail", cube, sail, ship, new Vector3(0f, 9.5f, mz), new Vector3(0.08f, 2f, 2.8f));
            }
            Place("Flag", cube, iron, ship, new Vector3(0f, 11.5f, 2.6f), new Vector3(0.04f, 0.6f, 1f));
            Place("Skull", sphere, groupMats[3], ship, new Vector3(-side * 0.05f, 11.5f, 2.6f), Vector3.one * 0.3f);
            floaters.Add(new Mover { t = ship, basePos = ship.localPosition, seed = Rand(0f, 6.28f), group = -7 });
        }
        // A lighthouse on a rock, its beam sweeping.
        for (float z = -Behind + 20f; z < Far; z += 70f)
        {
            var lh = Prop(root, "Lighthouse", new Vector3(24f, -1f, z));
            Place("Rock", rockMesh, stone, lh, Vector3.zero, new Vector3(8f, 3f, 8f));
            for (int k = 0; k < 6; k++)
                Place("Band", cyl, k % 2 == 0 ? Lit(new Color(0.7f, 0.68f, 0.65f), 0.3f) : groupMats[0], lh, new Vector3(0f, 2f + k * 2f, 0f), new Vector3(2.4f - k * 0.15f, 1f, 2.4f - k * 0.15f));
            Place("Lamp", sphere, groupMats[3], lh, new Vector3(0f, 14.4f, 0f), Vector3.one * 1.5f);
            var beam = Prop(lh, "Beam", new Vector3(0f, 14.4f, 0f));
            RemixGeo.Beam(beam, "Ray", cube, groupMats[3], Vector3.zero, new Vector3(0f, 0f, 30f), 0.6f);
            floaters.Add(new Mover { t = beam, group = -8, speed = 50f });
            scrollers.Add(new Mover { t = lh });
        }
        glowSkyMat = SkyQuad("Moon", new Vector3(-90f, 85f, 340f), 30f, 30f, new Color(0.9f, 0.95f, 1f, 1f), Sprite(128, 0.5f), 1f);
        var stars = Mask(256, 128, (x, y) => Mathf.Sin(x * 913f + y * 337f) > 0.997f ? 1f : 0f);
        SkyQuad("Stars", new Vector3(0f, 90f, 352f), 280f, 120f, new Color(1f, 1f, 1f, 1f), stars, 0.6f);
        particleMats = new[] { groupMats[3] };
        ParticleBox("Spray", 90, sphere, particleMats, Vector3.one * 0.06f, new Vector2(-0.4f, 0.4f), 14f, 0f, 5f);
    }

    // ---------- Aztec ----------

    private void BuildAztec()
    {
        Color jade = new Color(0.1f, 1f, 0.6f), gold = new Color(1f, 0.78f, 0.2f), fire = new Color(1f, 0.45f, 0.1f), water = new Color(0.4f, 0.85f, 1f);
        Group(jade); Group(gold); Group(fire); Group(water);
        var stone = Lit(new Color(0.3f, 0.27f, 0.22f), 0.2f);
        var moss = Lit(new Color(0.06f, 0.12f, 0.06f), 0.3f);
        var leaf = Lit(new Color(0.03f, 0.1f, 0.05f), 0.3f);
        var root = Root("Aztec");

        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var g = Prop(root, "Plaza", new Vector3(side * 10f, -0.55f, z + 6f));
                Place("Stones", cube, moss, g, Vector3.zero, new Vector3(8f, 0.08f, 12f));
                RemixGeo.Beam(g, "Inlay", cube, groupMats[0], new Vector3(-side * 3.7f, 0.06f, -6f), new Vector3(-side * 3.7f, 0.06f, 6f), 0.12f);
                scrollers.Add(new Mover { t = g });
            }
        // Fire bowls and idol heads along the road.
        int n = 0;
        for (float z = -Behind; z < Far; z += 5f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                float zz = z + (side > 0 ? 2.5f : 0f);
                if (n % 3 == 1)
                {
                    var h = Prop(root, "Idol", new Vector3(side * 8.6f, -0.5f, zz));
                    Place("Head", cube, stone, h, new Vector3(0f, 1.4f, 0f), new Vector3(1.8f, 2.8f, 1.6f));
                    Place("Brow", cube, stone, h, new Vector3(-side * 0.9f, 2.1f, 0f), new Vector3(0.3f, 0.3f, 1.7f));
                    foreach (float ez in new[] { -0.42f, 0.42f })
                    {
                        var eye = Place("Eye", cube, groupMats[2], h, new Vector3(-side * 0.92f, 1.75f, ez), new Vector3(0.05f, 0.3f, 0.35f));
                        pulsers.Add(new Mover { t = eye.transform, group = 2, size = -1f });
                    }
                    Place("Mouth", cube, groupMats[1], h, new Vector3(-side * 0.92f, 0.8f, 0f), new Vector3(0.05f, 0.25f, 0.9f));
                    Place("Crown", cube, groupMats[0], h, new Vector3(0f, 2.95f, 0f), new Vector3(2f, 0.3f, 1.8f));
                    scrollers.Add(new Mover { t = h });
                }
                else
                {
                    var b = Prop(root, "FireBowl", new Vector3(side * 7.4f, -0.5f, zz));
                    Place("Pillar", cube, stone, b, new Vector3(0f, 0.7f, 0f), new Vector3(0.6f, 1.4f, 0.6f));
                    Place("Bowl", cyl, groupMats[1], b, new Vector3(0f, 1.5f, 0f), new Vector3(0.9f, 0.15f, 0.9f));
                    var fl = Place("Fire", sphere, groupMats[2], b, new Vector3(0f, 1.95f, 0f), new Vector3(0.5f, 0.8f, 0.5f));
                    floaters.Add(new Mover { t = fl.transform, seed = Rand(0f, 6.28f), group = -5 });
                    scrollers.Add(new Mover { t = b });
                }
            }
        // Step pyramids with a temple on top and a golden stair down the middle.
        n = 0;
        for (float z = -Behind; z < Far; z += Rand(20f, 26f), n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var p = Prop(root, "Pyramid", new Vector3(side * Rand(20f, 24f), -0.5f, z));
            float size = Rand(14f, 18f), stepH = 1.3f;
            int steps = 7;
            for (int k = 0; k < steps; k++)
            {
                float w = size * (1f - k / (float)(steps + 2));
                Place("Step", cube, stone, p, new Vector3(0f, k * stepH + stepH * 0.5f, 0f), new Vector3(w, stepH, w));
                RemixGeo.Beam(p, "Trim", cube, groupMats[0], new Vector3(-side * w * 0.5f, k * stepH + stepH, -w * 0.5f), new Vector3(-side * w * 0.5f, k * stepH + stepH, w * 0.5f), 0.1f);
            }
            float top = steps * stepH;
            RemixGeo.Beam(p, "Stair", cube, groupMats[1], new Vector3(-side * size * 0.5f, 0f, 0f), new Vector3(-side * size * 0.5f * 0.23f, top, 0f), 1.2f);
            Place("Temple", cube, stone, p, new Vector3(0f, top + 1.5f, 0f), new Vector3(size * 0.25f, 3f, size * 0.25f));
            var door = Place("Door", cube, groupMats[2], p, new Vector3(-side * size * 0.126f, top + 1.2f, 0f), new Vector3(0.05f, 1.6f, 1.2f));
            pulsers.Add(new Mover { t = door.transform, group = 2, size = -1f });
            scrollers.Add(new Mover { t = p });
        }
        // Cliffs with waterfalls pouring down, and big jungle leaves.
        for (float z = -Behind + 4f; z < Far; z += Rand(16f, 22f))
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var c = Prop(root, "Cliff", new Vector3(side * 15f, -0.5f, z));
            Place("Rock", rockMesh, stone, c, new Vector3(side * 1.5f, 5f, 0f), new Vector3(6f, 12f, 8f));
            var fall = Prop(c, "Fall", new Vector3(-side * 1.2f, 0f, 0f));
            for (int k = 0; k < 10; k++)
                Place("Drop", cube, groupMats[3], fall, new Vector3(Rand(-0.3f, 0.3f), k * 1.1f, Rand(-0.8f, 0.8f)), new Vector3(0.1f, 0.9f, 0.18f));
            floaters.Add(new Mover { t = fall, group = -9, seed = Rand(0f, 1f) });
            for (int k = 0; k < 4; k++)
                Place("Leaf", sphere, leaf, c, new Vector3(-side * Rand(0.5f, 2.5f), Rand(0.5f, 2f), Rand(-3f, 3f)), new Vector3(3f, 0.15f, 1.2f), new Vector3(0f, Rand(0f, 180f), Rand(-30f, 30f)));
            scrollers.Add(new Mover { t = c });
        }
        // Golden serpent bolts on the drops.
        for (float z = -Behind + 6f; z < Far; z += 12f)
        {
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var b = Prop(root, "Serpent", new Vector3(side * Rand(11f, 16f), 0f, z));
            Zigzag(b, groupMats[1], new Vector3(0f, 16f, 0f), new Vector3(Rand(-2f, 2f), -0.5f, 0f), 10, 0.9f, 0.22f);
            b.gameObject.SetActive(false);
            bolts.Add(new Mover { t = b });
        }
        // Sky: the sun stone.
        glowSkyMat = SkyQuad("SunStone", new Vector3(0f, 75f, 340f), 55f, 55f, new Color(1f, 0.75f, 0.25f, 1f), Mask(128, 128, (x, y) =>
        {
            float dx = x * 2f - 1f, dy = y * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
            float rings = (Mathf.Abs(r - 0.9f) < 0.04f || Mathf.Abs(r - 0.62f) < 0.03f || Mathf.Abs(r - 0.35f) < 0.03f) ? 1f : 0f;
            float rays = r > 0.62f && r < 0.9f && Mathf.Repeat(a / (Mathf.PI / 8f), 1f) < 0.3f ? 0.8f : 0f;
            float core = r < 0.2f ? 1f : 0f;
            return Mathf.Max(rings, Mathf.Max(rays, core));
        }), 0.9f);
        particleMats = new[] { groupMats[1], groupMats[2] };
        ParticleBox("Embers", 120, sphere, particleMats, Vector3.one * 0.08f, new Vector2(-1.2f, -0.4f));
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
            float frac = skin == OdysseySkin.Pirate ? (drop > 0.3f && ClubBeat.BeatPhase < 0.2f ? 0.25f : 0f) : 0.04f + 0.1f * energy + 0.15f * kick;
            for (int i = 0; i < flashers.Count; i++)
            {
                float h = Mathf.Repeat(Mathf.Sin((i + 1) * 12.9898f + seedBeat * 0.001f + (skin == OdysseySkin.Racing ? Mathf.Floor(t * 8f) * 3.1f : 0f)) * 43758.5453f, 1f);
                flashers[i].enabled = h < frac;
            }
        }

        // Golden serpents (Aztec): one flashes on every other beat with a drop.
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
            p.x += (skin == OdysseySkin.WildWest ? 2.5f : Mathf.Sin(t * 1.2f + q.seed) * 0.6f) * dt;
            p.z -= delta * (skin == OdysseySkin.Racing ? 1.4f : 0.5f);
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
