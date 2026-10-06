using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum CosmicSkin
{
    OrbitStation,       // deep blue space, cyan/violet neon, station trusses with nav lights, ringed planets
    AsteroidStorm,      // orange/red, tumbling rocks with glowing cracks, meteors on the drops, a molten planet
    NebulaHyperdrive,   // pink/gold/violet nebula, warp streaks that stretch on the drops, gold portals
}

/// Theme Cosmic Rush's scenery (video 10), one class for the three skins:
///
///   * every skin: a star dome and planets (PlanetGeneratorKit surface) far beyond the fog - both
///     shaders ignore fog on purpose (see CityNightSkyDecorator) - a ring around the big planet,
///     streaks of passing stars that stretch into warp lines as the energy rises, and a global
///     Volume pushing the bloom up on a drop;
///   * OrbitStation: two station trusses running along the road with red/green nav lights blinking
///     on the beat, and two slow station wheels at the sides;
///   * AsteroidStorm: low-poly rocks tumbling past with cracks glowing on the kick, meteors falling
///     across the sky on the strong beats of a drop;
///   * NebulaHyperdrive: huge soft nebula clouds breathing on the bar, gold portal rings around the
///     road, the warp streaks at their longest.
///
/// Time and energy come from ClubBeat (the song clock), so it is on the music in Play and in a recording.
public class CosmicEnvironment : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [SerializeField] private CosmicSkin skin;
    [SerializeField, Range(0f, 1f)] private float dropBloom = 0.4f;
    [SerializeField] private float bloomPeak = 5f;
    [SerializeField] private int seed = 1010;

    private const float Behind = 12f;
    private const float MaxLevel = 2.4f;

    private Color primary = new Color(0f, 0.9f, 1f);
    private Color secondary = new Color(0.6f, 0.2f, 1f);
    private float emission = 1.3f;
    private float scrollSpeed, span;
    private bool built;
    private System.Random rng;
    private Mesh cube, sphere, rockMesh;
    private MaterialPropertyBlock mpb;
    private readonly List<Object> owned = new List<Object>();

    private struct Mover { public Transform t; public MeshRenderer r; public float baseY, seed, size, speed; public Vector3 axis; public int group; }
    private readonly List<Mover> streaks = new List<Mover>();
    private readonly List<Mover> braces = new List<Mover>();
    private readonly List<Mover> navLights = new List<Mover>();
    private readonly List<Mover> rocks = new List<Mover>();
    private readonly List<Mover> portals = new List<Mover>();
    private readonly List<Transform> wheels = new List<Transform>();
    private struct Meteor { public Transform t; public Vector3 from, vel; public float start; }
    private readonly List<Meteor> meteors = new List<Meteor>();
    private int lastMeteorBeat = -1, meteorNext;
    private Material streakMat, trussMat, navRedMat, navGreenMat, rockMat, meteorMat, portalMat, wheelMat, cloudMat;
    private Transform clouds;
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
        rng = new System.Random(seed + (int)skin * 97);
        mpb = new MaterialPropertyBlock();
        cube = RemixGeo.Primitive(PrimitiveType.Cube);
        sphere = RemixGeo.Primitive(PrimitiveType.Sphere);

        BuildStarDome();
        BuildPlanets();
        BuildStreaks(skin == CosmicSkin.NebulaHyperdrive ? 160 : 70);
        switch (skin)
        {
            case CosmicSkin.OrbitStation: BuildTrusses(); BuildWheels(); break;
            case CosmicSkin.AsteroidStorm: BuildRocks(); BuildMeteors(); break;
            case CosmicSkin.NebulaHyperdrive: BuildClouds(); BuildPortals(); break;
        }
        BuildBloomVolume();
    }

    private void OnDestroy()
    {
        foreach (var o in owned) if (o != null) Destroy(o);
        if (bloomProfile != null) Destroy(bloomProfile);
    }

    private float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    private T Own<T>(T o) where T : Object { owned.Add(o); return o; }
    private Material Neon(Color c) => Own(RemixGeo.Neon(c, emission));

    // --- sky (no fog) ---

    // Quads for the Custom/Starfield shader: vertex colour = tint, alpha = brightness, uv2 = twinkle.
    private Mesh Quads(List<(Vector3 c, Vector3 right, Vector3 up, Color col, Vector2 phase)> qs, string name)
    {
        int n = qs.Count;
        var v = new Vector3[n * 4]; var uv = new Vector2[n * 4]; var uv2 = new Vector2[n * 4]; var col = new Color[n * 4]; var tri = new int[n * 6];
        for (int i = 0; i < n; i++)
        {
            var q = qs[i]; int k = i * 4;
            v[k] = q.c - q.right - q.up; v[k + 1] = q.c + q.right - q.up; v[k + 2] = q.c + q.right + q.up; v[k + 3] = q.c - q.right + q.up;
            uv[k] = new Vector2(0, 0); uv[k + 1] = new Vector2(1, 0); uv[k + 2] = new Vector2(1, 1); uv[k + 3] = new Vector2(0, 1);
            for (int j = 0; j < 4; j++) { uv2[k + j] = q.phase; col[k + j] = q.col; }
            int t = i * 6;
            tri[t] = k; tri[t + 1] = k + 2; tri[t + 2] = k + 1; tri[t + 3] = k; tri[t + 4] = k + 3; tri[t + 5] = k + 2;
        }
        var m = Own(new Mesh { name = name });
        m.indexFormat = v.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        m.vertices = v; m.uv = uv; m.uv2 = uv2; m.colors = col; m.triangles = tri;
        m.RecalculateBounds();
        return m;
    }

    private Material SkyMat(Texture2D tex, float intensity, float twinkle)
    {
        var m = Own(new Material(Shader.Find("Custom/Starfield")));
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Intensity", intensity);
        m.SetFloat("_TwinkleAmount", twinkle);
        m.SetFloat("_TwinkleSpeed", 1.4f);
        return m;
    }

    private MeshRenderer Place(string name, Mesh mesh, Material mat, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    // Radial sprite: falloff power p; ring = annulus between r0 and 1.
    private Texture2D Sprite(int size, float power, float ringInner = -1f)
    {
        var tex = Own(new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp });
        var px = new Color32[size * size];
        float h = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - h) / h, dy = (y + 0.5f - h) / h, r = Mathf.Sqrt(dx * dx + dy * dy);
                float a;
                if (ringInner < 0f) a = Mathf.Pow(Mathf.Clamp01(1f - r), power);
                else
                {
                    float mid = (ringInner + 1f) * 0.5f, w = (1f - ringInner) * 0.5f;
                    a = Mathf.Clamp01(1f - Mathf.Abs(r - mid) / w);
                    a *= 0.55f + 0.45f * Mathf.Sin(r * 90f) * Mathf.Sin(r * 37f);      // bands
                    a = Mathf.Clamp01(a);
                }
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private void BuildStarDome()
    {
        var qs = new List<(Vector3, Vector3, Vector3, Color, Vector2)>();
        for (int i = 0; i < 1400; i++)
        {
            float az = Rand(-0.25f, 1.25f) * Mathf.PI;
            float hgt = Rand(-0.25f, 1f);
            float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - hgt * hgt));
            var dir = new Vector3(Mathf.Cos(az) * ring, hgt, Mathf.Sin(az) * ring).normalized;
            float radius = Rand(260f, 460f);
            float mag = Mathf.Pow((float)rng.NextDouble(), 3.2f);
            float size = radius * 0.0045f * Mathf.Lerp(0.7f, 3f, mag);
            Color tint = Color.Lerp(Color.white, rng.NextDouble() < 0.5 ? primary : secondary, Rand(0f, 0.5f));
            tint.a = Mathf.Lerp(0.18f, 1f, mag);
            Vector3 n = -dir, tan = Vector3.Cross(n, Vector3.up).normalized;
            qs.Add((dir * radius, tan * size, Vector3.Cross(n, tan) * size, tint, new Vector2(Rand(0f, 1f), Rand(0.6f, 1.6f))));
        }
        Place("StarDome", Quads(qs, "CosmicStars"), SkyMat(Sprite(64, 4f), 1.4f, 0.4f));
    }

    private void Planet(Vector3 pos, float radius, Color ocean, Color land, float intensity, float scale, bool ring, float ringTilt)
    {
        var mat = Own(new Material(Shader.Find("Custom/PlanetSurfaceNeon")));
        mat.SetColor("_ColorOcean", ocean); mat.SetColor("_ColorLand", land);
        mat.SetFloat("_Intensity", intensity); mat.SetFloat("_Scale", scale);
        mat.SetFloat("_LandThreshold", 0.5f); mat.SetFloat("_EdgeSmoothness", 0.12f);
        mat.SetFloat("_BlendSharpness", 3f); mat.SetFloat("_Octaves", 4f);
        var mr = Place("Planet", sphere, mat);
        mr.transform.localPosition = pos;
        mr.transform.localScale = Vector3.one * radius * 2f;
        mr.transform.localRotation = Quaternion.Euler(Rand(0f, 360f), Rand(0f, 360f), 0f);
        wheels.Add(mr.transform);       // planets turn slowly too
        // A soft halo behind, and optionally a banded ring (both fog-free quads).
        var glow = new List<(Vector3, Vector3, Vector3, Color, Vector2)>();
        Color g = Color.Lerp(ocean, Color.white, 0.3f); g.a = 0.55f;
        glow.Add((Vector3.zero, Vector3.right * radius * 2.6f, Vector3.up * radius * 2.6f, g, Vector2.zero));
        var halo = Place("PlanetHalo", Quads(glow, "Halo"), SkyMat(Sprite(128, 2.2f), 0.7f, 0f));
        halo.transform.localPosition = pos;
        if (!ring) return;
        var rq = new List<(Vector3, Vector3, Vector3, Color, Vector2)>();
        Color rc = Color.Lerp(land, Color.white, 0.25f); rc.a = 0.9f;
        rq.Add((Vector3.zero, Vector3.right * radius * 2.4f, Vector3.forward * radius * 2.4f, rc, Vector2.zero));
        var rmr = Place("PlanetRing", Quads(rq, "Ring"), SkyMat(Sprite(256, 1f, 0.55f), 1.1f, 0f));
        rmr.transform.localPosition = pos;
        rmr.transform.localRotation = Quaternion.Euler(ringTilt, 0f, 18f);
    }

    private void BuildPlanets()
    {
        switch (skin)
        {
            case CosmicSkin.OrbitStation:
                Planet(new Vector3(-150f, 45f, 310f), 62f, new Color(0.15f, 0.25f, 0.9f), Color.Lerp(secondary, primary, 0.3f), 0.85f, 0.07f, true, 14f);
                Planet(new Vector3(120f, 85f, 270f), 16f, primary, new Color(0.8f, 0.95f, 1f), 0.9f, 0.2f, false, 0f);
                break;
            case CosmicSkin.AsteroidStorm:
                Planet(new Vector3(160f, 25f, 290f), 95f, new Color(1f, 0.25f, 0.02f), new Color(1f, 0.7f, 0.15f), 1.1f, 0.05f, false, 0f);
                Planet(new Vector3(-110f, 95f, 300f), 14f, new Color(0.5f, 0.3f, 0.25f), new Color(0.9f, 0.6f, 0.4f), 0.6f, 0.2f, false, 0f);
                break;
            default:
                Planet(new Vector3(-120f, 70f, 320f), 70f, new Color(0.95f, 0.3f, 0.75f), new Color(1f, 0.8f, 0.35f), 0.9f, 0.06f, true, 22f);
                break;
        }
    }

    private void BuildClouds()
    {
        var qs = new List<(Vector3, Vector3, Vector3, Color, Vector2)>();
        Color pink = new Color(1f, 0.3f, 0.7f), violet = new Color(0.5f, 0.2f, 1f), gold = new Color(1f, 0.75f, 0.3f);
        for (int i = 0; i < 26; i++)
        {
            var c = new Vector3(Rand(-260f, 260f), Rand(-30f, 170f), Rand(220f, 380f));
            float s = Rand(60f, 150f);
            Color col = i % 3 == 0 ? pink : i % 3 == 1 ? violet : gold;
            col = Color.Lerp(col, i % 2 == 0 ? primary : secondary, 0.3f); col.a = Rand(0.25f, 0.55f);
            qs.Add((c, Vector3.right * s, Vector3.up * s * Rand(0.5f, 0.9f), col, new Vector2(Rand(0f, 1f), Rand(0.2f, 0.5f))));
        }
        cloudMat = SkyMat(Sprite(128, 1.6f), 0.9f, 0.25f);
        clouds = Place("Nebula", Quads(qs, "NebulaClouds"), cloudMat).transform;
    }

    // --- near field (scrolls with the tunnel) ---

    private void BuildStreaks(int count)
    {
        var root = new GameObject("Streaks").transform;
        root.SetParent(transform, false);
        streakMat = Neon(Color.white);
        for (int i = 0; i < count; i++)
        {
            float a = Rand(-0.15f, 1.15f) * Mathf.PI;               // above and beside the road, not under it
            float r = Rand(12f, 28f);
            var p = new Vector3(Mathf.Cos(a) * r, 2.5f + Mathf.Sin(a) * r * 0.8f, Rand(-Behind, span - Behind));
            var mr = RemixGeo.Beam(root, "Streak", cube, streakMat, Vector3.zero, Vector3.forward, Rand(0.02f, 0.045f));
            mr.transform.localPosition = p;
            streaks.Add(new Mover { t = mr.transform, r = mr, seed = Rand(0f, 1f), speed = Rand(1.2f, 2.2f), group = i % 2 });
        }
    }

    private void BuildTrusses()
    {
        var root = new GameObject("Trusses").transform;
        root.SetParent(transform, false);
        trussMat = Neon(secondary);
        navRedMat = Neon(new Color(1f, 0.1f, 0.15f));
        navGreenMat = Neon(new Color(0.1f, 1f, 0.4f));
        float far = span - Behind;
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * 10.5f;
            foreach (float y in new[] { 0.6f, 3.4f })
                foreach (float dx in new[] { -0.7f, 0.7f })
                    RemixGeo.Beam(root, "Rail", cube, trussMat, new Vector3(x + dx, y, -Behind), new Vector3(x + dx, y, far + 12f), 0.07f);
            for (float z = -Behind; z < far; z += 4f)
            {
                var brace = new GameObject("Brace").transform;
                brace.SetParent(root, false);
                brace.localPosition = new Vector3(x, 0f, z);
                RemixGeo.Beam(brace, "D1", cube, trussMat, new Vector3(-0.7f, 0.6f, 0f), new Vector3(0.7f, 3.4f, 2f), 0.05f);
                RemixGeo.Beam(brace, "D2", cube, trussMat, new Vector3(0.7f, 0.6f, 2f), new Vector3(-0.7f, 3.4f, 4f), 0.05f);
                RemixGeo.Beam(brace, "Rung", cube, trussMat, new Vector3(-0.7f, 3.4f, 0f), new Vector3(0.7f, 3.4f, 0f), 0.05f);
                braces.Add(new Mover { t = brace });
                if (((int)((z + Behind) / 4f)) % 2 == 0)
                {
                    var nav = RemixGeo.Beam(root, "Nav", sphere, side < 0 ? navRedMat : navGreenMat, Vector3.zero, Vector3.zero, 0.28f);
                    nav.transform.localPosition = new Vector3(x - side * 0.8f, 3.6f, z);
                    navLights.Add(new Mover { t = nav.transform, r = nav, group = navLights.Count % 4 });
                }
            }
        }
    }

    // Station wheels: big spoked rings far out at the sides, turning slowly, rim lights chasing.
    private void BuildWheels()
    {
        wheelMat = Neon(primary);
        foreach (var (pos, rad) in new[] { (new Vector3(-48f, 22f, 120f), 16f), (new Vector3(55f, 30f, 150f), 12f) })
        {
            var w = new GameObject("Wheel").transform;
            w.SetParent(transform, false);
            w.localPosition = pos;
            w.localRotation = Quaternion.Euler(0f, pos.x < 0 ? 35f : -35f, 0f);
            const int seg = 20;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                RemixGeo.Beam(w, "Rim", cube, wheelMat, new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * rad, new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * rad, 0.5f);
                if (i % 5 == 0) RemixGeo.Beam(w, "Spoke", cube, wheelMat, Vector3.zero, new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * rad, 0.3f);
            }
            RemixGeo.Beam(w, "Hub", sphere, wheelMat, Vector3.zero, Vector3.zero, rad * 0.22f);
            wheels.Add(w);
        }
    }

    // A rock: the sphere mesh pushed in and out by a hash of the vertex POSITION, so the UV seam's
    // duplicate vertices move together and the rock stays closed. Flat-ish normals read low-poly.
    private Mesh Rock()
    {
        var src = sphere;
        var v = src.vertices;
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 p = v[i];
            float h = Mathf.Sin(p.x * 12.9898f + p.y * 78.233f + p.z * 37.719f) * 43758.5453f;
            h -= Mathf.Floor(h);
            float lump = 0.75f + 0.45f * h + 0.15f * Mathf.Sin(p.x * 7f) * Mathf.Cos(p.z * 5f);
            v[i] = p * lump;
        }
        var m = Own(new Mesh { name = "Rock" });
        m.vertices = v; m.triangles = src.triangles; m.uv = src.uv;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    private void BuildRocks()
    {
        var root = new GameObject("Asteroids").transform;
        root.SetParent(transform, false);
        rockMesh = Rock();
        rockMat = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        rockMat.SetColor("_BaseColor", new Color(0.16f, 0.11f, 0.09f));
        rockMat.SetFloat("_Smoothness", 0.15f);
        rockMat.EnableKeyword("_EMISSION");
        for (int i = 0; i < 46; i++)
        {
            int side = i % 2 == 0 ? -1 : 1;
            float size = Mathf.Pow(Rand(0f, 1f), 2f) * 3.2f + 0.6f;
            var p = new Vector3(side * Rand(8.5f, 26f), Rand(-1f, 14f), Rand(-Behind, span - Behind));
            var mr = Place("Rock", rockMesh, rockMat, root);
            mr.transform.localPosition = p;
            mr.transform.localScale = new Vector3(size, size * Rand(0.6f, 1f), size * Rand(0.7f, 1.2f));
            rocks.Add(new Mover { t = mr.transform, r = mr, baseY = p.y, seed = Rand(0f, 6.28f), size = size, speed = Rand(0.8f, 1.6f),
                                  axis = new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), Rand(-1f, 1f)).normalized, group = i % 3 });
        }
    }

    private void BuildMeteors()
    {
        meteorMat = Neon(new Color(1f, 0.6f, 0.2f));
        for (int i = 0; i < 6; i++)
        {
            var mr = RemixGeo.Beam(transform, "Meteor", cube, meteorMat, Vector3.zero, Vector3.forward * 9f, 0.35f);
            mr.enabled = false;
            meteors.Add(new Meteor { t = mr.transform, start = -10f });
        }
    }

    private void BuildPortals()
    {
        var root = new GameObject("Portals").transform;
        root.SetParent(transform, false);
        portalMat = Neon(new Color(1f, 0.78f, 0.3f));
        const int seg = 28;
        for (float z = -Behind + 6f; z < span - Behind; z += 36f)
        {
            var p = new GameObject("Portal").transform;
            p.SetParent(root, false);
            p.localPosition = new Vector3(0f, 3f, z);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                if (Mathf.Sin(a0) < -0.32f) continue;     // open at the bottom: the road passes under
                RemixGeo.Beam(p, "Arc", cube, portalMat, new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 10.5f, new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 10.5f, 0.22f);
            }
            portals.Add(new Mover { t = p, seed = Rand(0f, 6.28f) });
        }
    }

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

    // --- every frame ---

    private static void Scroll(Transform t, float delta, float span)
    {
        var p = t.localPosition;
        p.z -= delta;
        if (p.z < -Behind) p.z += span;
        t.localPosition = p;
    }

    private void Update()
    {
        if (!built) return;
        float dt = Time.deltaTime;
        float delta = scrollSpeed * dt;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float kick = ClubBeat.Pulse;
        float t = ClubBeat.SongTime;
        float drop = Mathf.Clamp01((ClubBeat.Energy - 1f) / 0.6f);
        float barPulse = Mathf.Pow(1f - Mathf.Repeat(beats, 4f) / 4f, 3f);

        // Passing stars: faster and longer with the energy; at a drop they become warp lines.
        float warp = skin == CosmicSkin.NebulaHyperdrive ? Mathf.Clamp01(0.25f + 0.75f * drop + 0.2f * (energy - 0.6f))
                   : 0.15f + 0.45f * drop;
        foreach (var s in streaks)
        {
            Scroll(s.t, delta * (1f + s.speed * (0.6f + 3f * warp)), span);
            var sc = s.t.localScale;
            sc.y = 0.6f + (2f + 16f * warp) * s.speed;
            s.t.localScale = sc;
        }
        Color streakCol = Color.Lerp(Color.white, skin == CosmicSkin.AsteroidStorm ? primary : secondary, 0.35f);
        RemixGeo.SetEmission(streakMat, streakCol, Mathf.Min(MaxLevel, emission * (0.35f + 0.9f * warp + 0.3f * kick * energy)));

        // Planets and wheels turn slowly.
        for (int i = 0; i < wheels.Count; i++)
            wheels[i].localRotation *= Quaternion.AngleAxis(dt * (i % 2 == 0 ? 2.5f : -4f), Vector3.forward);

        switch (skin)
        {
            case CosmicSkin.OrbitStation: UpdateStation(delta, kick, energy, barPulse); break;
            case CosmicSkin.AsteroidStorm: UpdateAsteroids(delta, dt, t, beats, kick, energy, drop); break;
            case CosmicSkin.NebulaHyperdrive: UpdateNebula(delta, t, kick, energy, barPulse, beats); break;
        }

        if (bloomVolume != null)
            bloomVolume.weight = dropBloom * drop * (0.55f + 0.45f * kick);
    }

    private void UpdateStation(float delta, float kick, float energy, float barPulse)
    {
        foreach (var b in braces) Scroll(b.t, delta, span);
        RemixGeo.SetEmission(trussMat, secondary, Mathf.Min(MaxLevel, emission * (0.4f + 0.35f * kick * energy + 0.3f * barPulse)));
        RemixGeo.SetEmission(wheelMat, primary, Mathf.Min(MaxLevel, emission * (0.5f + 0.5f * barPulse * energy)));
        int on = ((ClubBeat.BeatIndex % 4) + 4) % 4;
        foreach (var n in navLights)
        {
            Scroll(n.t, delta, span);
            float level = n.group == on ? 0.4f + 2f * kick : 0.15f;
            mpb.SetColor(RemixGeo.EmissionId, (n.r.sharedMaterial == navRedMat ? new Color(1f, 0.1f, 0.15f) : new Color(0.1f, 1f, 0.4f)) * Mathf.Min(MaxLevel, level));
            n.r.SetPropertyBlock(mpb);
        }
    }

    private void UpdateAsteroids(float delta, float dt, float t, float beats, float kick, float energy, float drop)
    {
        for (int i = 0; i < rocks.Count; i++)
        {
            var r = rocks[i];
            Scroll(r.t, delta * r.speed, span);
            var p = r.t.localPosition;
            p.y = r.baseY + Mathf.Sin(t * 0.4f + r.seed) * 0.6f;
            r.t.localPosition = p;
            r.t.localRotation = Quaternion.AngleAxis((t * 25f + r.seed * 50f) / Mathf.Max(0.8f, r.size * 0.5f), r.axis);
            // Cracks glow: a low ember, flaring on the kick, one group in turn per beat.
            int g = ((ClubBeat.BeatIndex % 3) + 3) % 3;
            float level = 0.25f + 0.35f * kick * energy + (r.group == g ? 0.9f * kick : 0f);
            mpb.SetColor(RemixGeo.EmissionId, Color.Lerp(primary, new Color(1f, 0.35f, 0.05f), 0.5f) * Mathf.Min(MaxLevel, emission * level * 0.6f));
            r.r.SetPropertyBlock(mpb);
        }

        // Meteors: on the strong beats of a drop (and every 4th bar otherwise), one falls across the sky.
        int beat = ClubBeat.BeatIndex;
        bool strong = ((beat % 2) + 2) % 2 == 0;
        if (beat != lastMeteorBeat && ClubBeat.BeatPhase < 0.3f && ((drop > 0.2f && strong) || ((beat % 16) + 16) % 16 == 0))
        {
            lastMeteorBeat = beat;
            var m = meteors[meteorNext];
            meteorNext = (meteorNext + 1) % meteors.Count;
            float side = Rand(-1f, 1f);
            m.from = new Vector3(side * 40f, Rand(35f, 55f), Rand(70f, 130f));
            m.vel = new Vector3(-side * Rand(20f, 40f), -Rand(28f, 40f), -Rand(20f, 45f));
            m.start = t;
            meteors[(meteorNext + meteors.Count - 1) % meteors.Count] = m;
        }
        foreach (var m in meteors)
        {
            float age = t - m.start;
            var mr = m.t.GetComponent<MeshRenderer>();
            bool on = age >= 0f && age < 1.4f;
            mr.enabled = on;
            if (!on) continue;
            m.t.localPosition = m.from + m.vel * age;
            m.t.localRotation = Quaternion.FromToRotation(Vector3.up, -m.vel.normalized);
            m.t.localScale = new Vector3(0.35f, 9f, 0.35f);
        }
        RemixGeo.SetEmission(meteorMat, new Color(1f, 0.65f, 0.25f), MaxLevel);
    }

    private void UpdateNebula(float delta, float t, float kick, float energy, float barPulse, float beats)
    {
        if (cloudMat != null) cloudMat.SetFloat("_Intensity", 0.7f + 0.35f * barPulse * energy + 0.2f * kick);
        if (clouds != null) clouds.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.05f) * 3f);
        foreach (var p in portals)
        {
            Scroll(p.t, delta, span);
            p.t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(beats * Mathf.PI / 8f + p.seed) * 6f);
            p.t.localScale = Vector3.one * (1f + 0.05f * kick * energy);
        }
        RemixGeo.SetEmission(portalMat, new Color(1f, 0.78f, 0.3f), Mathf.Min(MaxLevel, emission * (0.5f + 0.6f * kick * energy + 0.4f * barPulse)));
    }
}
