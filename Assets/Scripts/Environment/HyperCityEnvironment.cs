using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum HyperCitySkin
{
    SunsetPink,     // pink/orange sunset, a huge striped sun on the horizon
    ToxicGreen,     // acid green on black, rooftop lasers firing on the kick, green smog
    RioCarnival,    // yellow/green/blue, billboards in carnival colours, confetti on the drops
    IceStorm,       // cold blue/white, snow streaks, lightning over the skyline on the drops
    GoldFinal,      // gold/crimson, fireworks over the city and sweeping rooftop lasers on the drops
}

/// Theme HyperCity's scenery (video 11): a neon megacity highway, one class for five skins.
///
///   * every skin: skyscrapers on both sides (dark bodies, neon corner edges, window bands that
///     light group by group on the beat, a beacon on the roof), billboards leaning over the road
///     that change colour on the bar, street lamps along the highway with a light wave rolling
///     towards the camera on every beat, a horizon glow beyond the fog, and a global Volume pushing
///     the bloom up on a drop;
///   * the skins' own effects - see HyperCitySkin.
///
/// Everything near scrolls with the tunnel; time and energy come from ClubBeat (the song clock), so
/// it is on the music in Play and in a recording.
public partial class HyperCityEnvironment : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [SerializeField] private HyperCitySkin skin;
    [SerializeField, Range(0f, 1f)] private float dropBloom = 0.4f;
    [SerializeField] private float bloomPeak = 5f;
    [SerializeField] private int seed = 1111;

    private const float Behind = 12f;
    private const float MaxLevel = 2.4f;
    private const int WindowGroups = 4;

    private Color primary = new Color(1f, 0.3f, 0.7f);
    private Color secondary = new Color(1f, 0.6f, 0.2f);
    private float emission = 1.3f;
    private float scrollSpeed, span;
    private bool built;
    private System.Random rng;
    private Mesh cube, sphere;
    private MaterialPropertyBlock mpb;
    private readonly List<Object> owned = new List<Object>();

    private struct Mover { public Transform t; public MeshRenderer r; public Vector3 basePos, vel, axis; public float seed, speed; public int group; }
    private readonly List<Mover> buildings = new List<Mover>();
    private readonly List<Mover> beacons = new List<Mover>();
    private readonly List<Mover> boards = new List<Mover>();
    private readonly List<Mover> lamps = new List<Mover>();
    private readonly List<Mover> poles = new List<Mover>();
    private readonly List<Mover> lasers = new List<Mover>();
    private readonly List<Mover> flakes = new List<Mover>();
    private Material bodyMat, edgeMat, beaconMat, poleMat, lampMat, laserMat, flakeMat;
    private readonly Material[] windowMats = new Material[WindowGroups];
    private readonly Material[] boardMats = new Material[3];
    private readonly Material[] confettiMats = new Material[3];
    private Material horizonMat;

    private struct Bolt { public Transform root; public MeshRenderer[] parts; public float start; }
    private readonly List<Bolt> bolts = new List<Bolt>();
    private Material boltMat;
    private int lastBoltBeat = -1, boltNext;

    private struct Spark { public Transform t; public Vector3 vel; }
    private struct Burst { public Spark[] sparks; public Vector3 origin; public float start; public Material mat; public Color col; }
    private readonly List<Burst> bursts = new List<Burst>();
    private int lastBurstBeat = -1, burstNext;

    private Volume bloomVolume;
    private VolumeProfile bloomProfile;

    private static readonly Color Gold = new Color(1f, 0.78f, 0.3f);

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
        rng = new System.Random(seed + (int)skin * 131);
        mpb = new MaterialPropertyBlock();
        cube = RemixGeo.Primitive(PrimitiveType.Cube);
        sphere = RemixGeo.Primitive(PrimitiveType.Sphere);

        BuildHorizon();
        BuildBuildings();
        BuildBillboards();
        BuildLamps();
        BuildDecor();
        switch (skin)
        {
            case HyperCitySkin.ToxicGreen: BuildLasers(false); break;
            case HyperCitySkin.RioCarnival: BuildConfetti(); break;
            case HyperCitySkin.IceStorm: BuildSnow(); BuildBolts(); break;
            case HyperCitySkin.GoldFinal: BuildLasers(true); BuildFireworks(); break;
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

    private MeshRenderer Place(string name, Mesh mesh, Material mat, Transform parent)
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

    private Transform Root(string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(transform, false);
        return t;
    }

    // --- horizon (no fog: Custom/Starfield, like CosmicEnvironment's sky) ---

    private Texture2D Sprite(int size, float power, bool stripes)
    {
        var tex = Own(new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp });
        var px = new Color32[size * size];
        float h = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - h) / h, dy = (y + 0.5f - h) / h, r = Mathf.Sqrt(dx * dx + dy * dy);
                float a;
                if (stripes)
                {
                    // The retro sun: a disc, cut by bands that thicken towards the bottom.
                    a = r < 0.96f ? 1f : Mathf.Clamp01((1f - r) / 0.04f);
                    float v = (dy + 1f) * 0.5f;
                    if (v < 0.5f && Mathf.Repeat(v * 12f, 1f) < 0.55f - v) a = 0f;
                }
                else a = Mathf.Pow(Mathf.Clamp01(1f - r), power);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private MeshRenderer SkyQuad(string name, Vector3 pos, float w, float h, Color col, Texture2D tex, float intensity)
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
        var mr = Place(name, m, mat, transform);
        mr.transform.localPosition = pos;
        return mr;
    }

    private void BuildHorizon()
    {
        Color glow = Color.Lerp(primary, secondary, 0.5f); glow.a = 0.75f;
        horizonMat = SkyQuad("HorizonGlow", new Vector3(0f, 10f, 380f), 420f, 120f, glow, Sprite(128, 1.4f, false), 0.9f).sharedMaterial;
        if (skin == HyperCitySkin.SunsetPink)
        {
            Color sun = Color.Lerp(secondary, new Color(1f, 0.85f, 0.4f), 0.4f); sun.a = 1f;
            SkyQuad("Sun", new Vector3(0f, 32f, 370f), 70f, 70f, sun, Sprite(256, 1f, true), 1.2f);
        }
        if (skin == HyperCitySkin.ToxicGreen)
        {
            var smog = Sprite(128, 1.8f, false);
            for (int i = 0; i < 8; i++)
            {
                Color c = Color.Lerp(primary, Color.black, 0.3f); c.a = Rand(0.25f, 0.45f);
                SkyQuad("Smog", new Vector3(Rand(-250f, 250f), Rand(5f, 60f), Rand(300f, 360f)), Rand(80f, 160f), Rand(40f, 70f), c, smog, 0.7f);
            }
        }
    }

    // --- near field (scrolls with the tunnel) ---

    private void BuildBuildings()
    {
        var root = Root("Buildings");
        bodyMat = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        bodyMat.SetColor("_BaseColor", Color.Lerp(new Color(0.03f, 0.03f, 0.05f), primary, 0.06f));
        bodyMat.SetFloat("_Smoothness", 0.6f);
        bodyMat.SetFloat("_Metallic", 0.3f);
        edgeMat = Neon(primary);
        beaconMat = Neon(new Color(1f, 0.15f, 0.2f));
        for (int g = 0; g < WindowGroups; g++)
            windowMats[g] = Neon(g % 2 == 0 ? Color.Lerp(secondary, Color.white, 0.25f) : Color.Lerp(primary, Color.white, 0.2f));

        float far = span - Behind;
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind + Rand(0f, 5f); z < far; z += Rand(7.5f, 11f))
            {
                float w = Rand(4.5f, 8f), d = Rand(4f, 7f), h = Rand(14f, 48f);
                float x = side * (Rand(18f, 26f) + w * 0.5f);
                var b = new GameObject("Tower").transform;
                b.SetParent(root, false);
                b.localPosition = new Vector3(x, 0f, z);
                var body = Place("Body", cube, bodyMat, b);
                body.transform.localPosition = new Vector3(0f, h * 0.5f - 0.5f, 0f);
                body.transform.localScale = new Vector3(w, h, d);
                float fx = -side * w * 0.5f;       // the face towards the road
                RemixGeo.Beam(b, "Edge", cube, edgeMat, new Vector3(fx, -0.5f, -d * 0.5f), new Vector3(fx, h - 0.5f, -d * 0.5f), 0.12f);
                RemixGeo.Beam(b, "Edge", cube, edgeMat, new Vector3(fx, -0.5f, d * 0.5f), new Vector3(fx, h - 0.5f, d * 0.5f), 0.12f);
                RemixGeo.Beam(b, "Roof", cube, edgeMat, new Vector3(fx, h - 0.5f, -d * 0.5f), new Vector3(fx, h - 0.5f, d * 0.5f), 0.12f);
                int start = rng.Next(WindowGroups);
                for (float y = 2.2f; y < h - 1.5f; y += 2.6f)
                {
                    int g = (start + (int)(y / 2.6f)) % WindowGroups;
                    RemixGeo.Beam(b, "Windows", cube, windowMats[g], new Vector3(fx - side * 0.05f, y, -d * 0.4f), new Vector3(fx - side * 0.05f, y, d * 0.4f), 0.32f);
                }
                buildings.Add(new Mover { t = b });
                if (rng.NextDouble() < 0.5)
                {
                    var bc = RemixGeo.Beam(b, "Beacon", sphere, beaconMat, new Vector3(0f, h, 0f), new Vector3(0f, h, 0f), 0.6f);
                    beacons.Add(new Mover { t = bc.transform, r = bc, group = beacons.Count % 2, basePos = new Vector3(0f, h, 0f) });
                }
            }
    }

    private void BuildBillboards()
    {
        var root = Root("Billboards");
        Color[] cols = skin == HyperCitySkin.RioCarnival
            ? new[] { new Color(1f, 0.85f, 0.1f), new Color(0.1f, 1f, 0.35f), new Color(0.1f, 0.5f, 1f) }
            : new[] { primary, secondary, Color.Lerp(primary, Color.white, 0.5f) };
        for (int i = 0; i < 3; i++) boardMats[i] = Neon(cols[i]);
        float far = span - Behind;
        int k = 0;
        for (float z = -Behind + 10f; z < far; z += Rand(24f, 34f), k++)
        {
            int side = k % 2 == 0 ? -1 : 1;
            var b = new GameObject("Billboard").transform;
            b.SetParent(root, false);
            b.localPosition = new Vector3(side * 12.5f, Rand(11.5f, 15f), z);
            b.localRotation = Quaternion.Euler(0f, side * 28f, 0f);
            var panel = Place("Panel", cube, boardMats[k % 3], b);
            panel.transform.localScale = new Vector3(6.5f, 3.4f, 0.15f);
            // Frame and the post down to the rooftops.
            RemixGeo.Beam(b, "Frame", cube, edgeMat, new Vector3(-3.35f, -1.8f, -0.1f), new Vector3(3.35f, -1.8f, -0.1f), 0.12f);
            RemixGeo.Beam(b, "Frame", cube, edgeMat, new Vector3(-3.35f, 1.8f, -0.1f), new Vector3(3.35f, 1.8f, -0.1f), 0.12f);
            RemixGeo.Beam(b, "Frame", cube, edgeMat, new Vector3(-3.35f, -1.8f, -0.1f), new Vector3(-3.35f, 1.8f, -0.1f), 0.12f);
            RemixGeo.Beam(b, "Frame", cube, edgeMat, new Vector3(3.35f, -1.8f, -0.1f), new Vector3(3.35f, 1.8f, -0.1f), 0.12f);
            boards.Add(new Mover { t = b, r = panel, group = k % 3 });
        }
    }

    private void BuildLamps()
    {
        var root = Root("Lamps");
        poleMat = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        poleMat.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.15f));
        lampMat = Neon(Color.white);
        float far = span - Behind;
        int i = 0;
        for (float z = -Behind; z < far; z += 12f, i++)
            foreach (int side in new[] { -1, 1 })
            {
                var p = new GameObject("Lamp").transform;
                p.SetParent(root, false);
                p.localPosition = new Vector3(side * 8.4f, 0f, z);
                RemixGeo.Beam(p, "Pole", cube, poleMat, new Vector3(0f, -0.5f, 0f), new Vector3(0f, 6.2f, 0f), 0.16f);
                RemixGeo.Beam(p, "Arm", cube, poleMat, new Vector3(0f, 6.2f, 0f), new Vector3(-side * 1.3f, 6.5f, 0f), 0.12f);
                var head = RemixGeo.Beam(p, "Head", cube, lampMat, new Vector3(-side * 1.05f, 6.35f, 0f), new Vector3(-side * 1.55f, 6.45f, 0f), 0.28f);
                poles.Add(new Mover { t = p });
                lamps.Add(new Mover { t = head.transform, r = head, seed = i });
            }
    }

    // Rooftop lasers: thin tall beams from the tops of the towers. Green ones fire on the kick in two
    // groups (ToxicGreen); gold ones sweep from side to side on the drops (GoldFinal).
    private void BuildLasers(bool sweep)
    {
        laserMat = Neon(sweep ? Gold : primary);
        int n = 0;
        foreach (var b in buildings)
        {
            if (n++ % 3 != 0) continue;
            var body = b.t.GetChild(0);
            float h = body.localScale.y;
            var pivot = new GameObject("Laser").transform;
            pivot.SetParent(b.t, false);
            pivot.localPosition = new Vector3(0f, h - 0.5f, 0f);
            var beam = RemixGeo.Beam(pivot, "Beam", cube, laserMat, Vector3.zero, Vector3.up * 70f, 0.18f);
            lasers.Add(new Mover { t = pivot, r = beam, group = lasers.Count % 2, seed = Rand(0f, 6.28f),
                                   basePos = new Vector3(b.t.localPosition.x < 0 ? 1f : -1f, 0f, 0f) });
        }
    }

    // Confetti: small tumbling flakes in carnival colours, falling through the space over the road;
    // brightest on the drops.
    private void BuildConfetti()
    {
        var root = Root("Confetti");
        Color[] cols = { new Color(1f, 0.85f, 0.1f), new Color(0.1f, 1f, 0.35f), new Color(1f, 0.2f, 0.6f) };
        for (int i = 0; i < 3; i++) confettiMats[i] = Neon(cols[i]);
        for (int i = 0; i < 180; i++)
        {
            var mr = Place("Flake", cube, confettiMats[i % 3], root);
            mr.transform.localScale = new Vector3(0.22f, 0.04f, 0.14f);
            var p = new Vector3(Rand(-13f, 13f), Rand(0f, 16f), Rand(2f, 70f));
            mr.transform.localPosition = p;
            flakes.Add(new Mover { t = mr.transform, r = mr, basePos = p, speed = Rand(1.2f, 2.6f), seed = Rand(0f, 6.28f),
                                   axis = new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), Rand(-1f, 1f)).normalized });
        }
    }

    // Snow: streaks driven diagonally across the view, faster on the drops.
    private void BuildSnow()
    {
        var root = Root("Snow");
        flakeMat = Neon(new Color(0.85f, 0.95f, 1f));
        for (int i = 0; i < 220; i++)
        {
            var mr = Place("Flake", sphere, flakeMat, root);
            mr.transform.localScale = new Vector3(0.06f, 0.35f, 0.06f);
            var p = new Vector3(Rand(-16f, 16f), Rand(0f, 18f), Rand(0f, 80f));
            mr.transform.localPosition = p;
            flakes.Add(new Mover { t = mr.transform, r = mr, basePos = p, speed = Rand(4f, 8f), seed = Rand(0f, 1f) });
        }
    }

    // Lightning: jagged chains over the skyline, struck on the strong beats of a drop.
    private void BuildBolts()
    {
        boltMat = Neon(new Color(0.75f, 0.9f, 1f));
        for (int b = 0; b < 4; b++)
        {
            var root = Root("Bolt");
            var parts = new MeshRenderer[9];
            Vector3 p = new Vector3(0f, 0f, 0f);
            for (int i = 0; i < parts.Length; i++)
            {
                Vector3 q = p + new Vector3(Rand(-3.5f, 3.5f), -Rand(4f, 6f), Rand(-1f, 1f));
                parts[i] = RemixGeo.Beam(root, "Seg", cube, boltMat, p, q, 0.35f);
                parts[i].enabled = false;
                p = q;
            }
            bolts.Add(new Bolt { root = root, parts = parts, start = -10f });
        }
    }

    // Fireworks: bursts of sparks over the city on the strong beats of a drop.
    private void BuildFireworks()
    {
        Color[] cols = { Gold, primary, Color.Lerp(secondary, Color.white, 0.3f) };
        for (int b = 0; b < 6; b++)
        {
            var root = Root("Burst");
            var mat = Neon(cols[b % 3]);
            var sparks = new Spark[28];
            for (int i = 0; i < sparks.Length; i++)
            {
                var mr = Place("Spark", sphere, mat, root);
                mr.transform.localScale = Vector3.one * 0.55f;
                mr.enabled = false;
                Vector3 dir = new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), Rand(-1f, 1f)).normalized;
                sparks[i] = new Spark { t = mr.transform, vel = dir * Rand(9f, 14f) };
            }
            bursts.Add(new Burst { sparks = sparks, start = -10f, mat = mat, col = cols[b % 3] });
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

    private void Scroll(Transform t, float delta)
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
        int beat = ClubBeat.BeatIndex;

        foreach (var b in buildings) Scroll(b.t, delta);
        foreach (var b in boards) Scroll(b.t, delta);
        foreach (var p in poles) Scroll(p.t, delta);

        // Towers: edges breathe on the bar; one window group in turn lights on each beat.
        RemixGeo.SetEmission(edgeMat, primary, Mathf.Min(MaxLevel, emission * (0.45f + 0.35f * barPulse * energy + 0.25f * kick * energy)));
        int on = ((beat % WindowGroups) + WindowGroups) % WindowGroups;
        for (int g = 0; g < WindowGroups; g++)
        {
            Color c = g % 2 == 0 ? Color.Lerp(secondary, Color.white, 0.25f) : Color.Lerp(primary, Color.white, 0.2f);
            float level = 0.3f + (g == on ? 1.1f * (0.4f + 0.6f * kick) * Mathf.Max(0.5f, energy) : 0f);
            RemixGeo.SetEmission(windowMats[g], c, Mathf.Min(MaxLevel, emission * level));
        }
        foreach (var bc in beacons)
        {
            float level = (((beat & 1) == bc.group) ? 0.3f + 1.6f * kick : 0.1f);
            mpb.SetColor(RemixGeo.EmissionId, new Color(1f, 0.15f, 0.2f) * Mathf.Min(MaxLevel, level));
            bc.r.SetPropertyBlock(mpb);
        }

        // Billboards: the colour moves on one board per bar.
        int bar = Mathf.FloorToInt(beats / 4f);
        for (int i = 0; i < boardMats.Length; i++)
        {
            float level = 0.55f + (((bar % 3) + 3) % 3 == i ? 0.9f * barPulse * Mathf.Max(0.6f, energy) : 0f);
            Color baseCol = skin == HyperCitySkin.RioCarnival
                ? (i == 0 ? new Color(1f, 0.85f, 0.1f) : i == 1 ? new Color(0.1f, 1f, 0.35f) : new Color(0.1f, 0.5f, 1f))
                : (i == 0 ? primary : i == 1 ? secondary : Color.Lerp(primary, Color.white, 0.5f));
            RemixGeo.SetEmission(boardMats[i], baseCol, Mathf.Min(MaxLevel, emission * level));
        }

        // Street lamps: a wave rolling towards the camera, one lamp-spacing a beat.
        Color lampCol = Color.Lerp(skin == HyperCitySkin.GoldFinal ? Gold : secondary, Color.white, 0.4f);
        foreach (var l in lamps)
        {
            float z = l.t.parent.localPosition.z;
            float phase = Mathf.Repeat(-z / 12f - beats, 4f);
            float wave = phase < 1f ? 1f - phase : 0f;
            float level = 0.35f + 1.4f * wave * Mathf.Max(0.5f, energy);
            mpb.SetColor(RemixGeo.EmissionId, lampCol * Mathf.Min(MaxLevel, emission * level));
            l.r.SetPropertyBlock(mpb);
        }

        UpdateDecor(dt, delta, t, beats, kick, energy, drop, barPulse);

        if (horizonMat != null) horizonMat.SetFloat("_Intensity", 0.75f + 0.25f * barPulse * energy + 0.25f * drop);

        switch (skin)
        {
            case HyperCitySkin.ToxicGreen: UpdateToxicLasers(kick, energy, beat); break;
            case HyperCitySkin.RioCarnival: UpdateConfetti(dt, t, drop, energy); break;
            case HyperCitySkin.IceStorm: UpdateSnow(dt, drop); UpdateBolts(t, beat, drop); break;
            case HyperCitySkin.GoldFinal: UpdateSweep(t, beats, kick, drop, energy); UpdateFireworks(t, beat, drop); break;
        }

        if (bloomVolume != null)
            bloomVolume.weight = dropBloom * drop * (0.55f + 0.45f * kick);
    }

    private void UpdateToxicLasers(float kick, float energy, int beat)
    {
        int g = ((beat % 2) + 2) % 2;
        foreach (var l in lasers)
        {
            l.r.enabled = energy > 0.55f;
            float level = l.group == g ? 0.4f + 1.8f * kick * energy : 0.15f;
            mpb.SetColor(RemixGeo.EmissionId, primary * Mathf.Min(MaxLevel, emission * level));
            l.r.SetPropertyBlock(mpb);
            l.t.localRotation = Quaternion.Euler(0f, 0f, l.basePos.x * 12f * Mathf.Sin(l.seed));
        }
    }

    private void UpdateSweep(float t, float beats, float kick, float drop, float energy)
    {
        foreach (var l in lasers)
        {
            l.r.enabled = drop > 0.05f || energy > 1f;
            l.t.localRotation = Quaternion.Euler(Mathf.Sin(beats * Mathf.PI / 4f + l.seed) * 15f, 0f, l.basePos.x * (20f + 25f * Mathf.Sin(beats * Mathf.PI / 8f + l.seed)));
        }
        RemixGeo.SetEmission(laserMat, Gold, Mathf.Min(MaxLevel, emission * (0.4f + 1.2f * drop + 0.4f * kick)));
    }

    private void UpdateConfetti(float dt, float t, float drop, float energy)
    {
        for (int i = 0; i < flakes.Count; i++)
        {
            var f = flakes[i];
            var p = f.t.localPosition;
            p.y -= f.speed * dt;
            p.x += Mathf.Sin(t * 1.3f + f.seed) * dt * 0.8f;
            p.z -= scrollSpeed * dt * 0.35f;
            if (p.y < -0.5f) { p.y += 16.5f; }
            if (p.z < 1f) p.z += 69f;
            f.t.localPosition = p;
            f.t.localRotation = Quaternion.AngleAxis(t * 240f + f.seed * 60f, f.axis);
        }
        float level = emission * (0.25f + 1.3f * drop + 0.2f * Mathf.Max(0f, energy - 0.8f));
        for (int i = 0; i < 3; i++)
        {
            Color c = i == 0 ? new Color(1f, 0.85f, 0.1f) : i == 1 ? new Color(0.1f, 1f, 0.35f) : new Color(1f, 0.2f, 0.6f);
            RemixGeo.SetEmission(confettiMats[i], c, Mathf.Min(MaxLevel, level));
        }
    }

    private void UpdateSnow(float dt, float drop)
    {
        float v = 1f + 1.6f * drop;
        foreach (var f in flakes)
        {
            var p = f.t.localPosition;
            p.y -= f.speed * v * dt;
            p.x += f.speed * 0.35f * v * dt;
            p.z -= scrollSpeed * dt * 0.6f;
            if (p.y < -0.5f) p.y += 18.5f;
            if (p.x > 16f) p.x -= 32f;
            if (p.z < 0f) p.z += 80f;
            f.t.localPosition = p;
            f.t.localRotation = Quaternion.Euler(0f, 0f, -20f);
        }
        RemixGeo.SetEmission(flakeMat, new Color(0.85f, 0.95f, 1f), Mathf.Min(MaxLevel, emission * (0.5f + 0.6f * drop)));
    }

    private void UpdateBolts(float t, int beat, float drop)
    {
        bool strong = ((beat % 2) + 2) % 2 == 0;
        if (beat != lastBoltBeat && ClubBeat.BeatPhase < 0.3f && ((drop > 0.25f && strong) || ((beat % 16) + 16) % 16 == 0))
        {
            lastBoltBeat = beat;
            var b = bolts[boltNext];
            boltNext = (boltNext + 1) % bolts.Count;
            b.root.localPosition = new Vector3(Rand(-60f, 60f), Rand(48f, 60f), Rand(90f, 150f));
            b.root.localRotation = Quaternion.Euler(0f, 0f, Rand(-12f, 12f));
            b.start = t;
            bolts[(boltNext + bolts.Count - 1) % bolts.Count] = b;
        }
        foreach (var b in bolts)
        {
            float age = t - b.start;
            bool on = age >= 0f && age < 0.28f && (age < 0.08f || age > 0.14f);   // a double flicker
            foreach (var p in b.parts) p.enabled = on;
        }
        RemixGeo.SetEmission(boltMat, new Color(0.75f, 0.9f, 1f), MaxLevel);
    }

    private void UpdateFireworks(float t, int beat, float drop)
    {
        bool strong = ((beat % 2) + 2) % 2 == 0;
        if (beat != lastBurstBeat && ClubBeat.BeatPhase < 0.3f && ((drop > 0.2f && strong) || ((beat % 16) + 16) % 16 == 0))
        {
            lastBurstBeat = beat;
            var b = bursts[burstNext];
            burstNext = (burstNext + 1) % bursts.Count;
            b.origin = new Vector3(Rand(-45f, 45f), Rand(28f, 46f), Rand(70f, 130f));
            b.start = t;
            bursts[(burstNext + bursts.Count - 1) % bursts.Count] = b;
        }
        foreach (var b in bursts)
        {
            float age = t - b.start;
            bool on = age >= 0f && age < 1.6f;
            float fade = on ? 1f - age / 1.6f : 0f;
            foreach (var s in b.sparks)
            {
                var mr = s.t.GetComponent<MeshRenderer>();
                mr.enabled = on;
                if (!on) continue;
                s.t.localPosition = b.origin + s.vel * age + Vector3.down * 4.5f * age * age;
                s.t.localScale = Vector3.one * (0.6f * fade + 0.15f);
            }
            if (on) RemixGeo.SetEmission(b.mat, b.col, Mathf.Min(MaxLevel, emission * 1.8f * fade + 0.2f));
        }
    }
}
