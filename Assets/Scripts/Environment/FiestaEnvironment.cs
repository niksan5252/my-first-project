using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum FiestaSkin
{
    Muertos,    // Día de Muertos: papel picado over the road, marigolds, candles, ofrendas, dancing skeletons
    Carnival,   // Rio carnival: Copacabana wave pavement, palms, parade floats with feather fans, confetti
    Havana,     // Havana nights: pastel colonial facades, neon signs, old cars cruising, palms, a sunset
    Pinata,     // a Mexican fiesta: swinging star piñatas, cacti, sombreros, maracas, fruit stands
    Flamenco,   // Seville: Moorish walls with horseshoe arches, orange trees, fountains, flamenco fans
    // Video 19 (FiestaEnvironment.Fun.cs):
    Candy,      // a candy land: giant lollipops, cupcakes, standing donuts, ice creams, cotton-candy clouds
    Diner,      // a retro 50s drive-in: neon diners, burger and fries signs, cars cruising
    Karts,      // a kart race: grandstands, tyre stacks, cones, karts racing alongside, chequered flags
    Camp,       // a camp night: pine forest, tents and campfires, glowing mushrooms, a lake, the aurora
    Beach,      // a beach party: the sea, palms, umbrellas, bouncing beach balls, a lifeguard tower, sunset
    Fruit,      // a fruit paradise: giant fruit bouncing on the kick, smoothie cups, fruit garlands
    Rescue,     // a rescue city: fire trucks, ambulances and police with sirens on the beat, searchlights
    // Video 20, Halloween (FiestaEnvironment.Halloween.cs):
    Pumpkin,    // a pumpkin patch: carved pumpkins, scarecrows, autumn trees, a harvest moon
    Haunted,    // a haunted graveyard: gravestones, iron fences, lanterns, floating ghosts
    Witch,      // a witch forest: dark pines, glowing mushrooms, bubbling cauldrons, flying brooms
    Zombie,     // a zombie town: ruined houses with flickering windows, wrecked cars, shambling zombies
    Vampire,    // a vampire castle: battlements, red gothic windows, candles, coffins, bats, a blood moon
    Swamp,      // a haunted swamp: black water, lilies, dead trees, will-o'-wisps, old boats
    Monster,    // a monster party: dancing skeletons, speaker stacks, lasers, candy and pumpkins
    // Video 24, around video 1's neon tunnel (FiestaEnvironment.Neon24.cs):
    Lagoon,         // a tropical lagoon at sunset: palms with neon rings, flamingos, glowing lilies
    CrystalCave,    // a crystal cave: rock walls and arches, stalactites, giant glowing crystal clusters
    CyberFactory,   // a neon factory: conveyors with crates, robot arms, cogs, presses, lasers, steam
    PlanetRing,     // open space: ringed planets, tumbling asteroids, beacons, a nebula
    FireflyJungle,  // a night jungle: dense trees, giant glowing flowers, vines with beads, fireflies
}

/// Video 18's scenery ("Fiesta"): five Latin worlds, one per level, in the manner of LegendsEnvironment -
/// everything near scrolls with the tunnel and wraps, the sky pieces sit beyond the fog
/// (Custom/Starfield), a global Volume pushes the bloom up on a drop, and all motion and light come
/// from ClubBeat (the song clock). The props are Kenney CC0 models (FiestaKit) where a model fits
/// (palms, cacti, cars, candles, skeletons, fruit), built from primitives where none does.
public partial class FiestaEnvironment : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [SerializeField] private FiestaSkin skin;
    [SerializeField, Range(0f, 1f)] private float dropBloom = 0.4f;
    [SerializeField] private float bloomPeak = 5f;
    [SerializeField] private int seed = 1818;
    [SerializeField] private List<GameObject> kit = new List<GameObject>();

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
    private FiestaKit props;
    private readonly List<Object> owned = new List<Object>();

    private struct Mover { public Transform t; public Vector3 basePos, axis; public float seed, speed, size; public int group; }
    private readonly List<Mover> scrollers = new List<Mover>();        // plain scrolling props
    private readonly List<Mover> pulsers = new List<Mover>();          // scale pulse on the kick of their group
    private readonly List<Mover> floaters = new List<Mover>();         // a motion by group code
    private readonly List<Mover> particles = new List<Mover>();        // confetti / petals in a box in front of the camera
    private readonly List<Mover> bursts = new List<Mover>();           // fireworks, shown for a moment on a drop beat
    private readonly List<Mover> riders = new List<Mover>();           // floats, cars - faster or slower than the road
    private readonly List<MeshRenderer> flashers = new List<MeshRenderer>();   // bulbs and neon letters blinking
    private readonly List<Material> groupMats = new List<Material>();
    private readonly List<Color> groupCols = new List<Color>();
    private Material glowSkyMat;
    private Volume bloomVolume;
    private VolumeProfile bloomProfile;
    private int lastBurstBeat = -1;
    private float burstUntil;

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
        props = new FiestaKit(kit, owned);
        switch (skin)
        {
            case FiestaSkin.Muertos: BuildMuertos(); break;
            case FiestaSkin.Carnival: BuildCarnival(); break;
            case FiestaSkin.Havana: BuildHavana(); break;
            case FiestaSkin.Pinata: BuildPinata(); break;
            case FiestaSkin.Flamenco: BuildFlamenco(); break;
            case FiestaSkin.Lagoon: case FiestaSkin.CrystalCave: case FiestaSkin.CyberFactory:
            case FiestaSkin.PlanetRing: case FiestaSkin.FireflyJungle: BuildNeon24(); break;
            default: BuildFun(); break;
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
    private int Side() => rng.NextDouble() < 0.5 ? -1 : 1;
    private T Own<T>(T o) where T : Object { owned.Add(o); return o; }
    private Material Neon(Color c) => Own(RemixGeo.Neon(c, emission));
    private Material Lit(Color c, float smooth = 0.3f, float metal = 0f, float glow = 0f)
    {
        var m = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        if (glow > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * glow);
        }
        return m;
    }
    private int Group(Color c) { groupMats.Add(Neon(c)); groupCols.Add(c); return groupMats.Count - 1; }
    private Material G(int i) => groupMats[((i % groupMats.Count) + groupMats.Count) % groupMats.Count];

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

    private Transform Kit(string name, Transform parent, Vector3 pos, float height, float yaw, Color tint, float glow)
        => props.Spawn(name, parent, pos, height, yaw, tint, glow);

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

    private void ParticleBox(string name, int count, Mesh mesh, Material[] mats, Vector3 scale, Vector2 speed, float x = 14f, float y0 = 0f, float y1 = 16f, float z1 = 70f)
    {
        var root = Root(name);
        for (int i = 0; i < count; i++)
        {
            var p = new Vector3(Rand(-x, x), Rand(y0, y1), Rand(1f, z1));
            var mr = Place("P", mesh, mats[i % mats.Length], root, p, scale);
            particles.Add(new Mover { t = mr.transform, basePos = new Vector3(x, y1 - y0, z1), speed = Rand(speed.x, speed.y), seed = Rand(0f, 6.28f),
                                      axis = new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), Rand(-1f, 1f)).normalized, size = y0 });
        }
    }

    private void Ground(Transform root, Material mat, float x = 12f, float width = 12f)
    {
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var g = Prop(root, "Ground", new Vector3(side * x, -0.55f, z + 6f));
                Place("Soil", cube, mat, g, Vector3.zero, new Vector3(width, 0.06f, 12f));
                scrollers.Add(new Mover { t = g });
            }
    }

    // A string across the road at `y` between two poles, with things hung along it by `hang(k, pos)`.
    private Transform Garland(Transform root, float z, float y, float half, Material pole, Material cord, int count, System.Action<Transform, int, Vector3> hang)
    {
        var g = Prop(root, "Garland", new Vector3(0f, 0f, z));
        foreach (int side in new[] { -1, 1 })
            RemixGeo.Beam(g, "Pole", cyl, pole, new Vector3(side * half, -0.5f, 0f), new Vector3(side * half, y + 0.4f, 0f), 0.18f);
        Vector3 prev = new Vector3(-half, y, 0f);
        for (int k = 1; k <= count + 1; k++)
        {
            float u = k / (float)(count + 1);
            Vector3 p = new Vector3(Mathf.Lerp(-half, half, u), y - Mathf.Sin(u * Mathf.PI) * 1.1f, 0f);
            RemixGeo.Beam(g, "Cord", cube, cord, prev, p, 0.04f);
            if (k <= count) hang(g, k - 1, p);
            prev = p;
        }
        scrollers.Add(new Mover { t = g });
        return g;
    }

    // A firework: neon rays from a centre, hidden until a drop beat picks it.
    private void Firework(Transform root, Vector3 pos, Material a, Material b)
    {
        var f = Prop(root, "Firework", pos);
        int n = 16;
        for (int k = 0; k < n; k++)
        {
            float u = k * Mathf.PI * 2f / n, v = Rand(-0.5f, 0.5f);
            Vector3 d = new Vector3(Mathf.Cos(u) * Mathf.Cos(v), Mathf.Sin(u) * Mathf.Cos(v), Mathf.Sin(v));
            RemixGeo.Beam(f, "Ray", cube, k % 2 == 0 ? a : b, d * 0.8f, d * Rand(3f, 4.2f), 0.12f);
            Place("Spark", sphere, k % 2 == 0 ? b : a, f, d * 4.4f, Vector3.one * 0.35f);
        }
        f.gameObject.SetActive(false);
        bursts.Add(new Mover { t = f });
    }

    private void Fireworks(Transform root, int a, int b)
    {
        for (float z = 20f; z < Far; z += 16f)
            Firework(root, new Vector3(Side() * Rand(9f, 18f), Rand(14f, 20f), z), G(a), G(b));
    }

    // ---------- Día de Muertos ----------

    private void BuildMuertos()
    {
        Color marigold = new Color(1f, 0.55f, 0.05f), pink = new Color(1f, 0.15f, 0.6f), violet = new Color(0.6f, 0.25f, 1f), teal = new Color(0.1f, 0.95f, 0.85f), yellow = new Color(1f, 0.85f, 0.15f);
        Group(marigold); Group(pink); Group(violet); Group(teal); Group(yellow);
        var soil = Lit(new Color(0.06f, 0.03f, 0.07f), 0.3f);
        var wood = Lit(new Color(0.16f, 0.09f, 0.05f), 0.3f);
        var cloth = Lit(new Color(0.5f, 0.08f, 0.35f), 0.2f, 0f, 0.25f);
        var root = Root("Muertos");
        Ground(root, soil);

        // Papel picado: rows of cut-paper flags across the road, swaying.
        for (float z = -Behind + 4f; z < Far; z += 9f)
        {
            var g = Garland(root, z, 9.6f, 8.6f, wood, wood, 11, (parent, k, p) =>
            {
                var flag = Prop(parent, "Flag", p);
                Place("Paper", cube, G(k), flag, new Vector3(0f, -0.65f, 0f), new Vector3(1.05f, 1.2f, 0.02f));
                Place("Cut", cube, cloth, flag, new Vector3(0f, -0.6f, -0.02f), new Vector3(0.45f, 0.45f, 0.01f), new Vector3(0f, 0f, 45f));
                floaters.Add(new Mover { t = flag, seed = Rand(0f, 6.28f) + k * 0.4f, group = -10 });
            });
        }
        // Marigold beds and candle clusters right by the road.
        for (float z = -Behind; z < Far; z += 3f)
            foreach (int side in new[] { -1, 1 })
            {
                var bed = Prop(root, "Marigolds", new Vector3(side * Rand(7.4f, 8.4f), -0.5f, z + Rand(0f, 1.5f)));
                for (int k = 0; k < 4; k++)
                    Kit(k % 2 == 0 ? "flower_yellowA" : "flower_redA", bed, new Vector3(Rand(-0.6f, 0.6f), 0f, Rand(-0.6f, 0.6f)), Rand(0.7f, 1.1f), Rand(0f, 360f), new Color(1f, 0.6f, 0.2f), 0.6f);
                if (rng.NextDouble() < 0.45)
                {
                    var c = Kit("candle-multiple", bed, new Vector3(-side * 0.9f, 0f, 0f), 0.6f, Rand(0f, 360f), new Color(1f, 0.85f, 0.7f), 0.3f);
                    var fl = Place("Flame", sphere, G(0), bed, new Vector3(-side * 0.9f, 0.9f, 0f), new Vector3(0.18f, 0.3f, 0.18f));
                    floaters.Add(new Mover { t = fl.transform, seed = Rand(0f, 6.28f), group = -5, size = 0.18f });
                }
                scrollers.Add(new Mover { t = bed });
            }
        // Ofrendas (stepped altars with candles and photo frames) and graves with crosses, alternating.
        int n = 0;
        for (float z = -Behind; z < Far; z += 7f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, n % 2 == 0 ? "Ofrenda" : "Grave", new Vector3(side * 11.5f, -0.5f, z + (side > 0 ? 3.5f : 0f)));
                if (n % 2 == 0)
                {
                    for (int s = 0; s < 3; s++)
                    {
                        Place("Step", cube, cloth, p, new Vector3(side * s * 0.6f, 0.45f + s * 0.9f, 0f), new Vector3(1.4f, 0.9f, 3.6f - s * 0.8f));
                        Place("Trim", cube, G(s + 1), p, new Vector3(side * s * 0.6f - side * 0.71f, 0.85f + s * 0.9f, 0f), new Vector3(0.04f, 0.1f, 3.6f - s * 0.8f));
                        for (int k = 0; k < 3 - s; k++)
                            Kit("candle", p, new Vector3(side * s * 0.6f - side * 0.4f, 0.9f + s * 0.9f, -1.1f + k * 1.1f + s * 0.4f), 0.45f, 0f, new Color(1f, 0.85f, 0.7f), 0.35f);
                    }
                    var frame = Place("Photo", cube, G(4), p, new Vector3(side * 1.2f - side * 0.5f, 3.7f, 0f), new Vector3(0.05f, 1f, 0.8f));
                    pulsers.Add(new Mover { t = frame.transform, group = 4, size = -1f });
                    var arch = Prop(p, "Arch", new Vector3(side * 0.6f, 0f, 0f));
                    for (int k = 0; k < 9; k++)
                    {
                        float u = k / 8f * Mathf.PI;
                        Place("Bloom", sphere, G(k % 2 == 0 ? 0 : 4), arch, new Vector3(0f, 2.8f + Mathf.Sin(u) * 2.2f, Mathf.Cos(u) * 1.9f), Vector3.one * 0.38f);
                    }
                }
                else
                {
                    Kit(rng.NextDouble() < 0.5 ? "gravestone-decorative" : "gravestone-cross", p, Vector3.zero, Rand(1.6f, 2.2f), side * -90f, new Color(0.8f, 0.7f, 1f), 0.35f);
                    Kit("urn-round", p, new Vector3(-side * 1f, 0f, 1f), 0.8f, 0f, new Color(1f, 0.6f, 0.4f), 0.5f);
                    Kit("lantern-candle", p, new Vector3(-side * 1f, 0f, -1f), 0.9f, 0f, Color.white, 1f);
                }
                scrollers.Add(new Mover { t = p });
            }
        // Skeletons dancing to the kick between the altars; lamp posts.
        for (float z = -Behind + 2f; z < Far; z += 8f)
        {
            int side = Side();
            var sk = Prop(root, "Dancer", new Vector3(side * Rand(9.2f, 10f), -0.5f, z));
            Kit("character-skeleton", sk, Vector3.zero, 2.3f, side * -90f + Rand(-30f, 30f), new Color(1f, 0.95f, 0.9f), 0.45f);
            Place("Sombrero", cyl, G(1), sk, new Vector3(0f, 2.35f, 0f), new Vector3(1.3f, 0.04f, 1.3f));
            floaters.Add(new Mover { t = sk, basePos = sk.localPosition, seed = Rand(0f, 6.28f), group = -11 });
            scrollers.Add(new Mover { t = sk });
        }
        for (float z = -Behind; z < Far; z += 14f)
            foreach (int side in new[] { -1, 1 })
            {
                var l = Kit("lightpost-double", root, new Vector3(side * 9f, -0.5f, z + 7f), 5.5f, side > 0 ? 0f : 180f, new Color(0.7f, 0.6f, 0.9f), 0.25f);
                if (l == null) continue;
                Place("Glow", sphere, G(3), l, new Vector3(0f, 1.15f, 0.3f), Vector3.one * 0.12f);
                Place("Glow", sphere, G(3), l, new Vector3(0f, 1.15f, -0.3f), Vector3.one * 0.12f);
                scrollers.Add(new Mover { t = l });
            }
        Fireworks(root, 1, 4);
        glowSkyMat = SkyQuad("Moon", new Vector3(80f, 85f, 340f), 26f, 26f, new Color(1f, 0.85f, 0.7f, 1f), Sprite(128, 0.55f), 1f);
        SkyQuad("Haze", new Vector3(0f, 25f, 350f), 260f, 55f, new Color(0.7f, 0.2f, 0.8f, 0.5f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Petals", 140, cube, new[] { G(0), G(4) }, new Vector3(0.14f, 0.02f, 0.1f), new Vector2(0.6f, 1.4f));
    }

    // ---------- Rio Carnival ----------

    private Texture2D WaveTile()
    {
        var tex = Own(new Texture2D(128, 128, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat });
        var px = new Color32[128 * 128];
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                float u = x / 128f, v = y / 128f;
                float w = Mathf.Sin((v * 2f + Mathf.Sin(u * Mathf.PI * 2f) * 0.25f) * Mathf.PI * 2f);
                byte c = (byte)(w > 0f ? 235 : 20);
                px[y * 128 + x] = new Color32(c, c, c, 255);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private void BuildCarnival()
    {
        Color green = new Color(0.1f, 1f, 0.35f), yellow = new Color(1f, 0.85f, 0.1f), turq = new Color(0.1f, 0.9f, 1f), pink = new Color(1f, 0.2f, 0.65f), orange = new Color(1f, 0.45f, 0.1f);
        Group(green); Group(yellow); Group(turq); Group(pink); Group(orange);
        var pave = Lit(new Color(0.35f, 0.33f, 0.3f), 0.5f);
        pave.SetTexture("_BaseMap", WaveTile());
        pave.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
        var dark = Lit(new Color(0.08f, 0.06f, 0.1f), 0.4f);
        var gold = Lit(new Color(0.8f, 0.6f, 0.2f), 0.6f, 0.7f, 0.2f);
        var root = Root("Carnival");
        Ground(root, pave, 11f, 9f);

        // Palms along the promenade, coconut and cocktail kiosks between them.
        for (float z = -Behind; z < Far; z += 6f)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Palm", new Vector3(side * Rand(8.4f, 9.6f), -0.5f, z + (side > 0 ? 3f : 0f) + Rand(-1f, 1f)));
                string m = rng.NextDouble() < 0.5 ? "tree_palmTall" : "tree_palmDetailedTall";
                Kit(m, p, Vector3.zero, Rand(7f, 9.5f), Rand(0f, 360f), new Color(0.55f, 0.85f, 0.6f), 0.12f);
                if (rng.NextDouble() < 0.3)
                {
                    Place("Kiosk", cube, dark, p, new Vector3(side * 1.6f, 0.5f, 0f), new Vector3(1.4f, 1f, 1.4f));
                    Place("Trim", cube, G(2), p, new Vector3(side * 1.6f, 1.02f, 0f), new Vector3(1.45f, 0.06f, 1.45f));
                    Kit("cocktail", p, new Vector3(side * 1.6f, 1.05f, -0.3f), 0.6f, 0f, Color.white, 0.7f);
                    Kit("coconut", p, new Vector3(side * 1.6f, 1.05f, 0.35f), 0.35f, 0f, Color.white, 0.6f);
                }
                scrollers.Add(new Mover { t = p });
            }
        // Bulb strings over the parade route.
        for (float z = -Behind + 3f; z < Far; z += 10f)
            Garland(root, z, 10f, 9f, gold, dark, 14, (parent, k, p) =>
            {
                var b = Place("Bulb", sphere, G(k), parent, p + Vector3.down * 0.2f, Vector3.one * 0.32f);
                flashers.Add(b);
            });
        // Parade floats with giant feather fans, rolling slower than the road.
        foreach (int side in new[] { -1, 1 })
            for (int k = 0; k < 3; k++)
            {
                var f = Prop(root, "Float", new Vector3(side * 12.5f, -0.5f, Rand(10f, Far)));
                Place("Deck", cube, dark, f, new Vector3(0f, 0.8f, 0f), new Vector3(3.6f, 1.2f, 7f));
                Place("Skirt", cube, G(k + 1), f, new Vector3(-side * 1.81f, 0.8f, 0f), new Vector3(0.04f, 0.25f, 7f));
                var fan = Prop(f, "Fan", new Vector3(0f, 2.2f, 0.5f));
                for (int r = 0; r < 13; r++)
                {
                    float a = Mathf.Lerp(-80f, 80f, r / 12f) * Mathf.Deg2Rad;
                    Vector3 d = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                    RemixGeo.Beam(fan, "Feather", cube, G(r % 3), d * 0.5f, d * Rand(5f, 6.5f), 0.32f);
                    Place("Tip", sphere, G(3 + r % 2), fan, d * 6.6f, Vector3.one * 0.55f);
                }
                Place("Crown", sphere, gold, f, new Vector3(0f, 2.4f, 0.5f), Vector3.one * 1.4f);
                floaters.Add(new Mover { t = fan, seed = Rand(0f, 6.28f), group = -13 });
                for (int d = 0; d < 3; d++)
                {
                    var dancer = Prop(f, "Dancer", new Vector3(Rand(-1f, 1f), 1.4f, -2.5f + d * 1.6f));
                    Place("Body", cyl, G(d + 3), dancer, new Vector3(0f, 0.7f, 0f), new Vector3(0.4f, 0.7f, 0.4f));
                    Place("Head", sphere, gold, dancer, new Vector3(0f, 1.6f, 0f), Vector3.one * 0.35f);
                    for (int r = 0; r < 5; r++)
                    {
                        float a = Mathf.Lerp(-60f, 60f, r / 4f) * Mathf.Deg2Rad;
                        RemixGeo.Beam(dancer, "Plume", cube, G(r), new Vector3(0f, 1.7f, 0f), new Vector3(0f, 1.7f + Mathf.Cos(a) * 1.3f, Mathf.Sin(a) * 1.3f), 0.1f);
                    }
                    floaters.Add(new Mover { t = dancer, basePos = dancer.localPosition, seed = Rand(0f, 6.28f), group = -11 });
                }
                riders.Add(new Mover { t = f, speed = k == 1 ? 0.5f : 0.75f });
            }
        // Sugarloaf-like peaks beyond the bay.
        for (float z = -Behind; z < Far; z += 11f)
            foreach (int side in new[] { -1, 1 })
            {
                var m = Prop(root, "Peak", new Vector3(side * Rand(28f, 34f), -0.5f, z));
                float h = Rand(12f, 22f);
                Place("Rock", rockMesh, dark, m, new Vector3(0f, h * 0.45f, 0f), new Vector3(Rand(6f, 9f), h, Rand(6f, 9f)));
                scrollers.Add(new Mover { t = m });
            }
        Fireworks(root, 1, 3);
        glowSkyMat = SkyQuad("Glow", new Vector3(0f, 30f, 345f), 120f, 40f, new Color(1f, 0.4f, 0.6f, 0.9f), Sprite(128, 0.8f), 0.9f);
        ParticleBox("Confetti", 200, cube, new[] { G(0), G(1), G(2), G(3), G(4) }, new Vector3(0.14f, 0.02f, 0.09f), new Vector2(0.7f, 1.6f));
    }

    // ---------- Havana nights ----------

    private void BuildHavana()
    {
        Color pink = new Color(1f, 0.2f, 0.6f), mint = new Color(0.3f, 1f, 0.75f), amber = new Color(1f, 0.7f, 0.25f), sky = new Color(0.3f, 0.75f, 1f), coral = new Color(1f, 0.45f, 0.35f);
        Group(pink); Group(mint); Group(amber); Group(sky); Group(coral);
        var street = Lit(new Color(0.07f, 0.06f, 0.06f), 0.75f, 0.2f);
        var iron = Lit(new Color(0.05f, 0.05f, 0.06f), 0.5f, 0.6f);
        var root = Root("Havana");
        Ground(root, street);
        Color[] pastel =
        {
            new Color(0.55f, 0.9f, 0.8f), new Color(1f, 0.6f, 0.55f), new Color(1f, 0.85f, 0.5f), new Color(0.6f, 0.75f, 1f), new Color(1f, 0.65f, 0.85f),
        };
        var walls = new Material[pastel.Length];
        for (int i = 0; i < pastel.Length; i++) walls[i] = Lit(pastel[i] * 0.55f, 0.25f, 0f, 0.12f);

        // Colonial facades on both sides: pastel walls, glowing windows, balconies, neon signs.
        int n = 0;
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; n++)
            {
                float w = Rand(6f, 8.5f), h = Rand(8f, 13f);
                var b = Prop(root, "House", new Vector3(side * 15f, -0.5f, z + w * 0.5f));
                Place("Wall", cube, walls[n % walls.Length], b, new Vector3(0f, h * 0.5f, 0f), new Vector3(6f, h, w - 0.3f));
                Place("Cornice", cube, iron, b, new Vector3(-side * 3.05f, h - 0.2f, 0f), new Vector3(0.3f, 0.4f, w - 0.1f));
                int floors = Mathf.FloorToInt((h - 1.5f) / 3f);
                for (int f = 0; f < floors; f++)
                    for (float wz = -w * 0.5f + 1.3f; wz < w * 0.5f - 0.8f; wz += 1.9f)
                    {
                        var win = Place("Window", cube, rng.NextDouble() < 0.7 ? G(2) : iron, b, new Vector3(-side * 3.02f, 1.6f + f * 3f, wz), new Vector3(0.04f, 1.7f, 0.85f));
                        if (f > 0 && rng.NextDouble() < 0.5)
                        {
                            Place("Balcony", cube, iron, b, new Vector3(-side * 3.4f, 0.75f + f * 3f, wz), new Vector3(0.8f, 0.08f, 1.3f));
                            Place("Rail", cube, iron, b, new Vector3(-side * 3.78f, 1.15f + f * 3f, wz), new Vector3(0.04f, 0.7f, 1.3f));
                        }
                    }
                if (n % 2 == 0)
                {
                    var sign = Prop(b, "Sign", new Vector3(-side * 3.15f, Rand(3.5f, h - 2.5f), 0f));
                    float sw = Mathf.Min(w - 2f, 4.5f);
                    var col = G(n % 2 == 0 ? (n % 4 == 0 ? 0 : 3) : 1);
                    foreach (var e in new[] { (new Vector3(0f, 0.6f, -sw * 0.5f), new Vector3(0f, 0.6f, sw * 0.5f)), (new Vector3(0f, -0.6f, -sw * 0.5f), new Vector3(0f, -0.6f, sw * 0.5f)),
                                              (new Vector3(0f, -0.6f, -sw * 0.5f), new Vector3(0f, 0.6f, -sw * 0.5f)), (new Vector3(0f, -0.6f, sw * 0.5f), new Vector3(0f, 0.6f, sw * 0.5f)) })
                        RemixGeo.Beam(sign, "Neon", cube, col, e.Item1, e.Item2, 0.1f);
                    for (int k = 0; k < 5; k++)   // the "letters": blinking bars
                        flashers.Add(Place("Letter", cube, G(k % 2 == 0 ? 1 : 4), sign, new Vector3(0f, 0f, Mathf.Lerp(-sw * 0.35f, sw * 0.35f, k / 4f)), new Vector3(0.06f, 0.75f, sw * 0.11f)));
                }
                scrollers.Add(new Mover { t = b });
                z += w;
            }
        // Palms and street lamps along the kerb.
        for (float z = -Behind; z < Far; z += 7f)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Kerb", new Vector3(side * 8.2f, -0.5f, z + (side > 0 ? 3.5f : 0f)));
                if ((int)(z / 7f) % 2 == 0) Kit("tree_palmBend", p, Vector3.zero, Rand(6f, 7.5f), Rand(0f, 360f), new Color(0.6f, 1f, 0.8f), 0.3f);
                else
                {
                    Kit("lightpost-double", p, Vector3.zero, 5f, side > 0 ? 0f : 180f, new Color(0.5f, 0.5f, 0.55f), 0.2f);
                    Place("Lamp", sphere, G(2), p, new Vector3(0f, 4.6f, 0.95f), Vector3.one * 0.35f);
                    Place("Lamp", sphere, G(2), p, new Vector3(0f, 4.6f, -0.95f), Vector3.one * 0.35f);
                }
                scrollers.Add(new Mover { t = p });
            }
        // Old cars cruising the side lanes and parked at the kerb, in candy colours.
        string[] cars = { "sedan", "sedan-sports", "taxi", "hatchback-sports", "suv", "van" };
        Color[] paint = { new Color(0.4f, 1f, 0.9f), new Color(1f, 0.5f, 0.7f), new Color(1f, 0.9f, 0.4f), new Color(0.6f, 0.7f, 1f), new Color(1f, 0.55f, 0.4f), Color.white };
        int ci = 0;
        foreach (int side in new[] { -1, 1 })
            for (int k = 0; k < 4; k++, ci++)
            {
                var c = Prop(root, "Car", new Vector3(side * 10.5f, -0.5f, Rand(5f, Far)));
                Kit(cars[ci % cars.Length], c, Vector3.zero, 1.7f, side > 0 ? 180f : 0f, paint[ci % paint.Length], 0.55f);
                foreach (float hz in new[] { -0.5f, 0.5f })
                    Place("Headlight", sphere, G(2), c, new Vector3(hz * 1.1f, 0.75f, side > 0 ? -1.7f : 1.7f), Vector3.one * 0.25f);
                riders.Add(new Mover { t = c, speed = k % 2 == 0 ? 0.55f : 1.5f });
            }
        Fireworks(root, 0, 3);
        glowSkyMat = SkyQuad("Sunset", new Vector3(0f, 22f, 345f), 70f, 70f, new Color(1f, 0.45f, 0.3f, 1f), Sprite(128, 0.45f), 1.1f);
        SkyQuad("Dusk", new Vector3(0f, 30f, 350f), 260f, 60f, new Color(1f, 0.3f, 0.55f, 0.55f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Fireflies", 90, sphere, new[] { G(2), G(1) }, Vector3.one * 0.07f, new Vector2(-0.3f, 0.3f));
    }

    // ---------- Fiesta piñata ----------

    private Transform Pinata(Transform parent, Vector3 pos, float s)
    {
        var p = Prop(parent, "Pinata", pos);
        var body = Prop(p, "Body", new Vector3(0f, -1.6f * s, 0f));
        Place("Core", sphere, G(0), body, Vector3.zero, Vector3.one * 1.1f * s);
        for (int k = 0; k < 7; k++)
        {
            float a = k * Mathf.PI * 2f / 7f + Mathf.PI / 2f;
            Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            RemixGeo.Beam(body, "Spike", cube, G(k + 1), d * 0.5f * s, d * 1.5f * s, 0.42f * s);
            Place("Tassel", cube, G(k + 3), body, d * 1.62f * s + Vector3.down * 0.35f * s, new Vector3(0.08f, 0.6f, 0.08f) * s);
        }
        RemixGeo.Beam(p, "Rope", cube, G(4), Vector3.zero, new Vector3(0f, -1.1f * s, 0f), 0.04f);
        floaters.Add(new Mover { t = p, seed = Rand(0f, 6.28f), group = -12 });
        return p;
    }

    private Transform Sombrero(Transform parent, Vector3 pos, float s, Material straw, Material band)
    {
        var h = Prop(parent, "Sombrero", pos);
        Place("Brim", cyl, straw, h, Vector3.zero, new Vector3(2.4f, 0.04f, 2.4f) * s);
        for (int k = 0; k < 4; k++) Place("Crown", cyl, straw, h, new Vector3(0f, (0.18f + k * 0.22f) * s, 0f), new Vector3(1f - k * 0.17f, 0.12f, 1f - k * 0.17f) * s);
        Place("Band", cyl, band, h, new Vector3(0f, 0.15f * s, 0f), new Vector3(1.04f, 0.06f, 1.04f) * s);
        Place("Rim", cyl, band, h, new Vector3(0f, 0.02f * s, 0f), new Vector3(2.42f, 0.03f, 2.42f) * s);
        return h;
    }

    private void BuildPinata()
    {
        Color magenta = new Color(1f, 0.15f, 0.65f), lime = new Color(0.55f, 1f, 0.2f), orange = new Color(1f, 0.5f, 0.05f), cyan = new Color(0.1f, 0.85f, 1f), yellow = new Color(1f, 0.9f, 0.15f), purple = new Color(0.65f, 0.3f, 1f);
        Group(magenta); Group(lime); Group(orange); Group(cyan); Group(yellow); Group(purple);
        var sand = Lit(new Color(0.22f, 0.13f, 0.08f), 0.2f);
        var straw = Lit(new Color(0.75f, 0.58f, 0.3f), 0.3f, 0f, 0.15f);
        var wood = Lit(new Color(0.2f, 0.11f, 0.05f), 0.3f);
        var root = Root("Pinata");
        Ground(root, sand);

        // Bulb strings across the road with star piñatas swinging under every other one.
        int g = 0;
        for (float z = -Behind + 3f; z < Far; z += 8f, g++)
        {
            bool withPinata = g % 2 == 0;
            Garland(root, z, 10.4f, 8.8f, wood, wood, 12, (parent, k, p) =>
            {
                var b = Place("Bulb", sphere, G(k), parent, p + Vector3.down * 0.2f, Vector3.one * 0.3f);
                flashers.Add(b);
                if (withPinata && (k == 3 || k == 8)) Pinata(parent, p, 0.9f);
            });
        }
        // Cacti, giant sombreros and maracas by the road.
        int n = 0;
        for (float z = -Behind; z < Far; z += 5f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Roadside", new Vector3(side * Rand(7.8f, 9f), -0.5f, z + (side > 0 ? 2.5f : 0f)));
                switch ((n + (side > 0 ? 1 : 0)) % 3)
                {
                    case 0:
                        Kit(rng.NextDouble() < 0.6 ? "cactus_tall" : "cactus_short", p, Vector3.zero, Rand(2.2f, 3.2f), Rand(0f, 360f), new Color(0.45f, 0.8f, 0.4f), 0.18f);
                        Kit("flower_redA", p, new Vector3(0.8f, 0f, 0.4f), 0.8f, 0f, new Color(1f, 0.5f, 0.8f), 0.6f);
                        break;
                    case 1:
                    {
                        var s = Sombrero(p, new Vector3(0f, 0.2f, 0f), 1.4f, straw, G(n));
                        s.localRotation = Quaternion.Euler(Rand(-10f, 10f), 0f, side * 12f);
                        floaters.Add(new Mover { t = s, seed = Rand(0f, 6.28f), group = -16, speed = Rand(20f, 40f) });
                        break;
                    }
                    default:
                    {
                        RemixGeo.Beam(p, "Post", cyl, wood, new Vector3(0f, -0.1f, 0f), new Vector3(0f, 2.4f, 0f), 0.15f);
                        foreach (int m in new[] { -1, 1 })
                        {
                            var mar = Prop(p, "Maraca", new Vector3(m * 0.35f, 2.4f, 0f));
                            RemixGeo.Beam(mar, "Handle", cyl, wood, Vector3.zero, new Vector3(0f, 0.6f, 0f), 0.09f);
                            Place("Head", sphere, G(n + (m > 0 ? 2 : 0)), mar, new Vector3(0f, 0.9f, 0f), new Vector3(0.55f, 0.7f, 0.55f));
                            floaters.Add(new Mover { t = mar, seed = m > 0 ? Mathf.PI : 0f, group = -14 });
                        }
                        break;
                    }
                }
                scrollers.Add(new Mover { t = p });
            }
        // Fruit stands further out: tables piled with giant fruit and tacos.
        string[] fruit = { "watermelon", "pineapple", "lemon", "orange", "taco", "grapes", "banana" };
        for (float z = -Behind; z < Far; z += 9f)
            foreach (int side in new[] { -1, 1 })
            {
                var st = Prop(root, "Stand", new Vector3(side * 11.5f, -0.5f, z + (side > 0 ? 4.5f : 0f)));
                Place("Table", cube, wood, st, new Vector3(0f, 0.9f, 0f), new Vector3(2f, 0.15f, 3.4f));
                foreach (float lz in new[] { -1.5f, 1.5f })
                    RemixGeo.Beam(st, "Leg", cube, wood, new Vector3(0f, 0f, lz), new Vector3(0f, 0.9f, lz), 0.15f);
                Place("Awning", cube, G((int)z), st, new Vector3(0f, 3.1f, 0f), new Vector3(2.4f, 0.08f, 3.8f), new Vector3(0f, 0f, -side * 12f));
                foreach (float lz in new[] { -1.7f, 1.7f })
                    RemixGeo.Beam(st, "Pole", cyl, wood, new Vector3(side * 1f, 0f, lz), new Vector3(side * 1f, 3.2f, lz), 0.1f);
                for (int k = 0; k < 3; k++)
                {
                    var f = Kit(fruit[rng.Next(fruit.Length)], st, new Vector3(Rand(-0.4f, 0.4f), 0.98f, -1f + k), Rand(0.6f, 0.9f), Rand(0f, 360f), Color.white, 0.6f);
                    if (f != null) pulsers.Add(new Mover { t = f, group = k, size = f.localScale.x });
                }
                scrollers.Add(new Mover { t = st });
            }
        // Mesas on the horizon.
        for (float z = -Behind; z < Far; z += 10f)
            foreach (int side in new[] { -1, 1 })
            {
                var m = Prop(root, "Mesa", new Vector3(side * Rand(26f, 32f), -0.5f, z));
                float h = Rand(6f, 11f);
                Place("Rock", cube, Lit(new Color(0.3f, 0.12f, 0.08f), 0.2f), m, new Vector3(0f, h * 0.5f, 0f), new Vector3(Rand(7f, 11f), h, Rand(6f, 9f)));
                scrollers.Add(new Mover { t = m });
            }
        Fireworks(root, 0, 4);
        glowSkyMat = SkyQuad("Sun", new Vector3(-40f, 35f, 345f), 55f, 55f, new Color(1f, 0.55f, 0.2f, 1f), Sprite(128, 0.45f), 1.1f);
        SkyQuad("Dusk", new Vector3(0f, 25f, 350f), 260f, 55f, new Color(1f, 0.35f, 0.5f, 0.5f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Confetti", 200, cube, new[] { G(0), G(1), G(2), G(3), G(4), G(5) }, new Vector3(0.14f, 0.02f, 0.09f), new Vector2(0.7f, 1.6f));
    }

    // ---------- Flamenco Seville ----------

    private void BuildFlamenco()
    {
        Color red = new Color(1f, 0.1f, 0.15f), gold = new Color(1f, 0.75f, 0.25f), blue = new Color(0.15f, 0.5f, 1f), orange = new Color(1f, 0.5f, 0.08f), white = new Color(1f, 0.92f, 0.8f);
        Group(red); Group(gold); Group(blue); Group(orange); Group(white);
        var tiles = Lit(new Color(0.18f, 0.08f, 0.05f), 0.6f);
        var lime = Lit(new Color(0.55f, 0.5f, 0.45f), 0.25f, 0f, 0.08f);
        var recess = Lit(new Color(0.04f, 0.02f, 0.03f), 0.2f);
        var black = Lit(new Color(0.03f, 0.02f, 0.02f), 0.6f);
        var water = Lit(new Color(0.05f, 0.25f, 0.4f), 0.95f, 0.1f, 0.4f);
        var root = Root("Flamenco");
        Ground(root, tiles);

        // Whitewashed Moorish walls, a horseshoe arch every 6 m with red-and-white voussoirs, a tile band.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 6f)
            {
                var w = Prop(root, "Wall", new Vector3(side * 13.5f, -0.5f, z + 3f));
                Place("Wall", cube, lime, w, new Vector3(0f, 4.5f, 0f), new Vector3(2f, 9f, 6f));
                Place("Tiles", cube, G(2), w, new Vector3(-side * 1.01f, 0.6f, 0f), new Vector3(0.03f, 0.25f, 6f));
                Place("Recess", cube, recess, w, new Vector3(-side * 1.01f, 2.4f, 0f), new Vector3(0.03f, 4.8f, 2.6f));
                const int seg = 12;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.Lerp(-35f, 215f, i / (float)seg) * Mathf.Deg2Rad, u1 = Mathf.Lerp(-35f, 215f, (i + 1) / (float)seg) * Mathf.Deg2Rad;
                    Vector3 p0 = new Vector3(-side * 1.05f, 4.8f + Mathf.Sin(u0) * 1.6f, Mathf.Cos(u0) * 1.6f), p1 = new Vector3(-side * 1.05f, 4.8f + Mathf.Sin(u1) * 1.6f, Mathf.Cos(u1) * 1.6f);
                    RemixGeo.Beam(w, "Voussoir", cube, i % 2 == 0 ? G(0) : G(4), p0, p1, 0.35f);
                }
                var fan = Prop(w, "Fan", new Vector3(-side * 1.1f, 7.6f, 0f));
                for (int r = 0; r < 11; r++)
                {
                    float a = Mathf.Lerp(-75f, 75f, r / 10f) * Mathf.Deg2Rad;
                    Vector3 d = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                    RemixGeo.Beam(fan, "Rib", cube, r % 2 == 0 ? G(0) : black, Vector3.zero, d * 1.4f, 0.22f);
                }
                floaters.Add(new Mover { t = fan, seed = z * 0.3f, group = -13 });
                var lantern = Kit("lantern-candle", w, new Vector3(-side * 1.4f, 7.2f - 1.2f, side > 0 ? 2.6f : -2.6f), 1f, 0f, new Color(1f, 0.85f, 0.6f), 1f);
                if (lantern != null) pulsers.Add(new Mover { t = lantern, group = 3, size = lantern.localScale.x });
                scrollers.Add(new Mover { t = w });
            }
        // Orange trees with glowing fruit, and fountains, alternating by the road.
        int n = 0;
        for (float z = -Behind; z < Far; z += 5f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, n % 2 == 0 ? "OrangeTree" : "Fountain", new Vector3(side * 8.6f, -0.5f, z + (side > 0 ? 2.5f : 0f)));
                if (n % 2 == 0)
                {
                    Kit("tree_oak", p, Vector3.zero, Rand(4.5f, 5.5f), Rand(0f, 360f), new Color(0.35f, 0.6f, 0.35f), 0.15f);
                    for (int k = 0; k < 7; k++)
                    {
                        float a = Rand(0f, 6.28f);
                        Kit("orange", p, new Vector3(Mathf.Cos(a) * Rand(0.8f, 1.6f), Rand(2.6f, 4.2f), Mathf.Sin(a) * Rand(0.8f, 1.6f)), 0.35f, 0f, new Color(1f, 0.8f, 0.6f), 1.1f);
                    }
                    Place("Pot", cyl, G(2), p, new Vector3(0f, 0.25f, 0f), new Vector3(1.2f, 0.25f, 1.2f));
                }
                else
                {
                    Place("Basin", cyl, lime, p, new Vector3(0f, 0.3f, 0f), new Vector3(2.4f, 0.3f, 2.4f));
                    Place("Water", cyl, water, p, new Vector3(0f, 0.58f, 0f), new Vector3(2.2f, 0.02f, 2.2f));
                    Place("Rim", cyl, G(2), p, new Vector3(0f, 0.61f, 0f), new Vector3(2.44f, 0.02f, 2.44f));
                    RemixGeo.Beam(p, "Stem", cyl, lime, new Vector3(0f, 0.5f, 0f), new Vector3(0f, 1.5f, 0f), 0.25f);
                    var jet = Prop(p, "Jet", new Vector3(0f, 1.5f, 0f));
                    Place("Spray", cyl, G(4), jet, new Vector3(0f, 0.6f, 0f), new Vector3(0.12f, 0.6f, 0.12f));
                    floaters.Add(new Mover { t = jet, seed = Rand(0f, 6.28f), group = -15 });
                }
                scrollers.Add(new Mover { t = p });
            }
        // The Giralda-like tower and cathedral roofs beyond the walls.
        for (float z = -Behind; z < Far; z += 18f)
        {
            int side = Side();
            var t = Prop(root, "Tower", new Vector3(side * Rand(22f, 28f), -0.5f, z));
            float h = Rand(16f, 22f);
            Place("Shaft", cube, lime, t, new Vector3(0f, h * 0.5f, 0f), new Vector3(3.5f, h, 3.5f));
            Place("Belfry", cube, G(1), t, new Vector3(0f, h + 1.4f, 0f), new Vector3(2.6f, 2.8f, 2.6f));
            Place("Spire", cube, lime, t, new Vector3(0f, h + 3.6f, 0f), new Vector3(1.4f, 1.6f, 1.4f));
            scrollers.Add(new Mover { t = t });
        }
        Fireworks(root, 0, 1);
        glowSkyMat = SkyQuad("Moon", new Vector3(-70f, 80f, 340f), 26f, 26f, new Color(1f, 0.9f, 0.75f, 1f), Sprite(128, 0.55f), 1f);
        SkyQuad("Dusk", new Vector3(0f, 25f, 350f), 260f, 55f, new Color(0.9f, 0.25f, 0.2f, 0.5f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Petals", 150, cube, new[] { G(0), G(0), G(3) }, new Vector3(0.14f, 0.02f, 0.1f), new Vector2(0.5f, 1.2f));
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
                p.t.localScale = Vector3.one * p.size * (1f + (p.group % Mathf.Max(1, groupMats.Count) == on ? 0.18f * kick : 0f));

        float swing = Mathf.Sin(beats * Mathf.PI);   // one swing per beat, there and back over two
        foreach (var f in floaters)
        {
            switch (f.group)
            {
                case -10:   // papel picado flag: flutters about its cord
                    f.t.localRotation = Quaternion.Euler(Mathf.Sin(t * 2.3f + f.seed) * 14f + kick * 6f, 0f, Mathf.Sin(t * 1.7f + f.seed) * 4f);
                    break;
                case -11:   // dancer: hops on the kick, sways on the beat
                {
                    var p = f.basePos;
                    p.y += 0.35f * kick * Mathf.Max(0.5f, energy);
                    var lp = f.t.localPosition;
                    lp.y = p.y;
                    f.t.localPosition = lp;
                    f.t.localRotation = Quaternion.Euler(0f, f.t.localEulerAngles.y, swing * 10f * Mathf.Sin(f.seed + 1f));
                    break;
                }
                case -12:   // piñata: swings on its rope, turning slowly
                    f.t.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.4f + f.seed) * 10f, t * 25f + f.seed * 40f, Mathf.Sin(t * 1.9f + f.seed) * 16f);
                    break;
                case -13:   // fan: opens and closes on the bar
                    f.t.localScale = new Vector3(1f, 1f, 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(beats * Mathf.PI * 0.5f + f.seed)));
                    break;
                case -14:   // maraca: shakes on every beat
                    f.t.localRotation = Quaternion.Euler(0f, 0f, swing * 28f * (f.seed > 1f ? -1f : 1f) * (0.6f + 0.4f * energy));
                    break;
                case -15:   // fountain jet: jumps on the kick
                    f.t.localScale = new Vector3(1f, 0.8f + 1.4f * kick * Mathf.Max(0.5f, energy) + 0.3f * Mathf.Sin(t * 5f + f.seed), 1f);
                    break;
                case -17:   // bounce: hops on the kick from its base height (giant fruit, beach balls, floating sweets)
                {
                    var lp = f.t.localPosition;
                    lp.y = f.basePos.y + f.speed * Mathf.Max(0.4f, energy) * kick + 0.15f * Mathf.Sin(t * 1.3f + f.seed);
                    f.t.localPosition = lp;
                    break;
                }
                case -18:   // spin about the road axis (a standing donut rolling, a searchlight sweeping)
                    f.t.localRotation = Quaternion.Euler(f.basePos.x, f.basePos.y, t * f.speed + f.seed * 50f);
                    break;
                case -16:   // sombrero: spins slowly
                {
                    var e = f.t.localEulerAngles;
                    f.t.localRotation = Quaternion.Euler(e.x, t * f.speed + f.seed * 50f, e.z);
                    break;
                }
                default:    // flames: flicker, flare on the kick
                {
                    float fl = 1f + 0.15f * Mathf.Sin(t * 17f + f.seed) + 0.35f * kick;
                    float s = f.size > 0f ? f.size : 0.5f;
                    f.t.localScale = new Vector3(s * fl, s * 1.6f * fl * (1f + 0.2f * Mathf.Sin(t * 11f + f.seed)), s * fl);
                    break;
                }
            }
        }

        foreach (var r in riders) Scroll(r.t, delta * r.speed);

        if (flashers.Count > 0)
        {
            float frac = 0.55f + 0.2f * energy + 0.25f * kick;
            int seedBeat = beat * 7919;
            for (int i = 0; i < flashers.Count; i++)
            {
                float h = Mathf.Repeat(Mathf.Sin((i + 1) * 12.9898f + seedBeat * 0.001f) * 43758.5453f, 1f);
                flashers[i].enabled = h < frac;
            }
        }

        // Fireworks on the drops: one on every other beat.
        if (bursts.Count > 0)
        {
            foreach (var b in bursts) Scroll(b.t, delta);
            bool due = beat != lastBurstBeat && ClubBeat.BeatPhase < 0.25f && drop > 0.2f && (beat % 2) == 0;
            if (due)
            {
                lastBurstBeat = beat;
                burstUntil = Time.time + 0.3f;
                int pick = rng.Next(bursts.Count);
                for (int i = 0; i < bursts.Count; i++) bursts[i].t.gameObject.SetActive(i == pick);
            }
            else if (Time.time > burstUntil)
                foreach (var b in bursts) if (b.t.gameObject.activeSelf) b.t.gameObject.SetActive(false);
            foreach (var b in bursts)
                if (b.t.gameObject.activeSelf) b.t.localScale = Vector3.one * (0.6f + 0.4f * Mathf.Clamp01(1f - (burstUntil - Time.time) / 0.3f));
        }

        foreach (var q in particles)
        {
            var p = q.t.localPosition;
            float boxX = q.basePos.x, boxH = q.basePos.y, boxZ = q.basePos.z, y0 = q.size;
            p.y -= q.speed * dt * (1f + 0.6f * drop);
            p.x += Mathf.Sin(t * 1.2f + q.seed) * 0.6f * dt;
            p.z -= delta * 0.5f;
            if (p.y < y0) p.y += boxH;
            if (p.y > y0 + boxH) p.y -= boxH;
            if (p.x > boxX) p.x -= boxX * 2f;
            if (p.x < -boxX) p.x += boxX * 2f;
            if (p.z < 1f) p.z += boxZ - 1f;
            q.t.localPosition = p;
            q.t.localRotation = Quaternion.AngleAxis(t * 200f + q.seed * 60f, q.axis);
        }

        FunUpdate(beats);
        if (glowSkyMat != null) glowSkyMat.SetFloat("_Intensity", 0.8f + 0.3f * barPulse * energy + 0.4f * drop);
        if (bloomVolume != null) bloomVolume.weight = dropBloom * drop * (0.55f + 0.45f * kick);
    }
}
