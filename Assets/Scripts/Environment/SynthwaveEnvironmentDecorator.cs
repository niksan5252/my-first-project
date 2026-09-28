using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_Synthwave's environment, "Second Wind": a retro sunset drive.
///
///   * the sky (the theme's skybox, Tunnel/SynthwaveSky): indigo to violet to a sunset band, stars,
///     and a huge slit sun on the horizon straight down the road;
///   * a neon grid plain running to the horizon either side of the road (Tunnel/SynthGrid; the
///     road itself is the theme's floor material, the same shader) - it runs towards the camera at
///     the tunnel's speed, flashes on every beat and sends a pulse rushing in along it every bar;
///   * wireframe mountains either side, far out - a ridged height-field under the same grid
///     shader, lines draped over the slopes - left open in the middle for the sun; on each drop a
///     wave of light climbs them from the foot to the peaks;
///   * palm silhouettes in two rows along the road (procedural - no palm in the imported kits),
///     black against the sunset and fading into the horizon haze with distance; they travel at the
///     tunnel's speed and their crowns sway gently over two bars, each palm on its own phase;
///
/// Everything in time with the music goes through SongTweens: DOTween tweens built paused, with
/// durations in beats, positioned every frame from ClubBeat's song time (SongClock, the chart's
/// grid) - never by DOTween's own clock, so a recording stays on the beat. How hard things go is
/// ClubBeat.Energy, planned from the chart: idle in the intro and breakdowns, full with the notes,
/// boosted on each drop.
public class SynthwaveEnvironmentDecorator : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [Header("Materials")]
    [SerializeField] private Material groundMaterial;
    [SerializeField] private Material mountainMaterial;

    [Header("Ground")]
    [SerializeField] private Vector2 groundSize = new Vector2(420f, 700f);
    [SerializeField] private float groundY = -0.08f;
    [SerializeField] private float gridFlash = 0.9f;
    [SerializeField] private float gridWave = 1.6f;

    [Header("Mountains")]
    [SerializeField] private Vector2 mountainX = new Vector2(34f, 200f);     // inner and outer edge
    [SerializeField] private Vector2 mountainZ = new Vector2(150f, 470f);    // near and far edge
    [SerializeField] private float mountainHeight = 46f;
    [SerializeField] private int mountainSeed = 11;
    [SerializeField] private float dropWaveBeats = 8f;
    [SerializeField] private float dropWaveTop = 62f;

    [Header("Palms")]
    [SerializeField] private float palmInnerX = 9.5f;
    [SerializeField] private float palmInnerSpacing = 16f;
    [SerializeField] private float palmOuterX = 21f;
    [SerializeField] private float palmOuterSpacing = 23f;
    [SerializeField] private Color palmColor = new Color(0.018f, 0.005f, 0.035f);
    [SerializeField] private float palmSwayDegrees = 3.5f;
    [SerializeField] private float palmSwayBeats = 8f;
    [SerializeField] private float behindCamera = 24f;

    [Header("Sky")]
    [SerializeField] private float sunGlowIdle = 0.35f;
    [SerializeField] private float sunGlowKick = 0.55f;

    private Color primary = new Color(1f, 0.42f, 0.22f);
    private Color secondary = new Color(0.55f, 0.25f, 1f);
    private float emission = 1.2f;

    private bool built;
    private float scrollSpeed, tunnelLength;
    private Material sky, savedSky;
    private readonly SongTweens tweens = new SongTweens();

    // Values the tweens write; applied to materials in LateUpdate.
    private float sunKick;      // 1 on the beat, falling through it
    private float slitDrift;    // 0..1 per bar
    private float gridKick;     // 1 on the beat
    private float gridWavePos;  // world Z of the pulse running in along the ground, per bar
    private float mountainWaveY, mountainWaveGain;   // the drop wave: height and strength
    private float mountainBar;  // a faint swell on each downbeat
    private Tween dropWave;

    private class Palm { public Transform root, crown; public float z; public float sway; public float lean; }
    private readonly System.Collections.Generic.List<Palm> palms = new System.Collections.Generic.List<Palm>();
    private float palmSpan;

    private static readonly int SunGlowId = Shader.PropertyToID("_SunGlow");
    private static readonly int SunScrollId = Shader.PropertyToID("_SunScroll");
    private static readonly int StarTimeId = Shader.PropertyToID("_StarTime");
    private static readonly int HorizonId = Shader.PropertyToID("_HorizonColor");
    private static readonly int ScrollSpeedId = Shader.PropertyToID("_SynthScrollSpeed");
    private static readonly int Pulse0Id = Shader.PropertyToID("_SynthPulse0");
    private static readonly int Wave0Id = Shader.PropertyToID("_SynthWave0");
    private static readonly int Pulse1Id = Shader.PropertyToID("_SynthPulse1");
    private static readonly int Wave1Id = Shader.PropertyToID("_SynthWave1");

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
        this.tunnelLength = tunnelLength;
        this.scrollSpeed = scrollSpeed;
        if (FindAnyObjectByType<ClubBeat>() == null) gameObject.AddComponent<ClubBeat>();

        // The theme's skybox is an asset; animate a copy so Play never writes into it.
        savedSky = RenderSettings.skybox;
        if (savedSky != null && savedSky.shader.name == "Tunnel/SynthwaveSky")
        {
            sky = new Material(savedSky) { name = savedSky.name + " (live)" };
            sky.SetColor(HorizonId, RenderSettings.fogColor);
            RenderSettings.skybox = sky;
        }

        Shader.SetGlobalFloat(ScrollSpeedId, scrollSpeed);
        BuildGround();
        BuildMountains();
        BuildPalms();
        BuildTweens();
    }

    private void BuildGround()
    {
        if (groundMaterial == null) return;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        Destroy(ground.GetComponent<Collider>());
        ground.name = "GridPlain";
        ground.transform.SetParent(transform, false);
        ground.transform.localPosition = new Vector3(0f, groundY, groundSize.y * 0.5f - 40f);
        ground.transform.localScale = new Vector3(groundSize.x / 10f, 1f, groundSize.y / 10f);
        var r = ground.GetComponent<MeshRenderer>();
        var m = new Material(groundMaterial);
        m.SetColor(HorizonId, RenderSettings.fogColor);
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    private void BuildMountains()
    {
        if (mountainMaterial == null) return;
        var m = new Material(mountainMaterial);
        m.SetColor(HorizonId, RenderSettings.fogColor);
        for (int side = -1; side <= 1; side += 2)
        {
            var go = new GameObject(side < 0 ? "Mountains_L" : "Mountains_R");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MountainMesh(side, mountainSeed + (side > 0 ? 17 : 0));
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    /// A ridged height-field between mountainX and mountainZ on one side of the road: low at its
    /// inner and near edges so the range rises out of the plain, highest well off to the side.
    /// Coarse on purpose - the facets are part of the wireframe look.
    private Mesh MountainMesh(int side, int seed)
    {
        const int nx = 56, nz = 40;
        var rnd = new System.Random(seed);
        float ox = (float)rnd.NextDouble() * 100f, oz = (float)rnd.NextDouble() * 100f;
        var verts = new Vector3[nx * nz];
        for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                float u = ix / (nx - 1f), v = iz / (nz - 1f);
                float x = Mathf.Lerp(mountainX.x, mountainX.y, u);
                float z = Mathf.Lerp(mountainZ.x, mountainZ.y, v);
                float ridge = 0f, amp = 1f, freq = 0.012f, norm = 0f;
                for (int o = 0; o < 4; o++)
                {
                    float n = Mathf.PerlinNoise(ox + x * freq, oz + z * freq);
                    ridge += (1f - Mathf.Abs(n * 2f - 1f)) * amp;
                    norm += amp;
                    amp *= 0.5f;
                    freq *= 2.1f;
                }
                ridge = Mathf.Pow(ridge / norm, 2.2f);
                float envelope = Smooth(0f, 0.22f, u) * (0.45f + 0.55f * Smooth(0.05f, 0.55f, u))
                               * Smooth(0f, 0.2f, v) * Smooth(1f, 0.75f, v);
                verts[iz * nx + ix] = new Vector3(side * x, ridge * envelope * mountainHeight * 1.6f, z);
            }
        var tris = new int[(nx - 1) * (nz - 1) * 6];
        int t = 0;
        for (int iz = 0; iz < nz - 1; iz++)
            for (int ix = 0; ix < nx - 1; ix++)
            {
                int a = iz * nx + ix, b = a + 1, c = a + nx, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        var mesh = new Mesh { name = "Mountains" };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    private void BuildPalms()
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "PalmSilhouette" };
        mat.SetColor("_BaseColor", palmColor);
        mat.SetFloat("_Cull", 0f);   // fronds are single sheets, seen from both sides
        var rnd = new System.Random(5);
        var trunks = new Mesh[3];
        var crowns = new Mesh[3];
        for (int v = 0; v < 3; v++)
        {
            trunks[v] = TrunkMesh(7f + v * 1.4f, 0.8f + 0.5f * v, rnd);
            crowns[v] = CrownMesh(8 + v, 3.6f + v * 0.4f, rnd);
        }
        palmSpan = tunnelLength + behindCamera + palmInnerSpacing;
        var stage = new GameObject("Palms").transform;
        stage.SetParent(transform, false);
        void Row(float x, float spacing, float scale, float start)
        {
            for (int side = -1; side <= 1; side += 2)
                for (float z = start; z < palmSpan - behindCamera; z += spacing)
                {
                    int v = rnd.Next(3);
                    var root = new GameObject("Palm").transform;
                    root.SetParent(stage, false);
                    float jitterX = (float)rnd.NextDouble() * 2f - 1f;
                    root.localPosition = new Vector3(side * (x + jitterX), 0f, z);
                    // Trunks lean away from the road, the way roadside palms are drawn.
                    root.localRotation = Quaternion.Euler(0f, side > 0 ? 0f : 180f, 0f);
                    root.localScale = Vector3.one * scale * (0.9f + 0.25f * (float)rnd.NextDouble());
                    MeshPart(root, "Trunk", trunks[v], mat);
                    var crown = MeshPart(root, "Crown", crowns[v], mat);
                    crown.localPosition = TrunkTop(7f + v * 1.4f, 0.8f + 0.5f * v);
                    palms.Add(new Palm { root = root, crown = crown, z = z, lean = (float)rnd.NextDouble() * 360f });
                }
        }
        Row(palmInnerX, palmInnerSpacing, 1f, 6f);
        Row(palmOuterX, palmOuterSpacing, 1.25f, 14f);
    }

    private static Transform MeshPart(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    private static Vector3 TrunkTop(float height, float bend) => new Vector3(bend, height, 0f);

    /// A tapering trunk curving out by `bend` metres over its height, ringed like bark.
    private static Mesh TrunkMesh(float height, float bend, System.Random rnd)
    {
        const int rings = 14, sides = 7;
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        for (int i = 0; i <= rings; i++)
        {
            float t = i / (float)rings;
            var c = new Vector3(bend * t * t, height * t, 0f);
            float r = Mathf.Lerp(0.3f, 0.15f, t) * (1f + 0.12f * (i % 2));
            for (int k = 0; k < sides; k++)
            {
                float a = k * Mathf.PI * 2f / sides;
                verts.Add(c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }
        for (int i = 0; i < rings; i++)
            for (int k = 0; k < sides; k++)
            {
                int a = i * sides + k, b = i * sides + (k + 1) % sides, c = a + sides, d = b + sides;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        var m = new Mesh { name = "PalmTrunk" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    /// Fronds radiating from the crown: each rises, then arcs over and droops, a strip of
    /// alternating wide and narrow steps along the spine so the edge reads as leaflets.
    private static Mesh CrownMesh(int fronds, float length, System.Random rnd)
    {
        const int steps = 18;
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        for (int f = 0; f < fronds; f++)
        {
            float yaw = (f + 0.35f * (float)rnd.NextDouble()) * Mathf.PI * 2f / fronds;
            var dir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            float len = length * (0.8f + 0.35f * (float)rnd.NextDouble());
            float rise = 25f + 25f * (float)rnd.NextDouble();
            var p = Vector3.zero;
            int baseIndex = verts.Count;
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                float pitch = Mathf.Lerp(rise, -70f, t * t) * Mathf.Deg2Rad;
                var along = dir * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
                if (s > 0) p += along * (len / steps);
                float w = 0.55f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp(t, 0.04f, 0.97f))), 0.6f) * (s % 2 == 0 ? 1f : 0.45f);
                var droop = Vector3.down * w * 0.35f;
                verts.Add(p + side * w + droop);
                verts.Add(p - side * w + droop);
            }
            for (int s = 0; s < steps; s++)
            {
                int a = baseIndex + s * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }
        var m = new Mesh { name = "PalmCrown" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    private static float Smooth(float a, float b, float x)
    {
        float k = Mathf.Clamp01((x - a) / (b - a));
        return k * k * (3f - 2f * k);
    }

    private void BuildTweens()
    {
        // Sun: a swell on every beat, fast attack, slow release.
        tweens.Loop(DOTween.To(x => sunKick = x, 1f, 0f, 1f).SetEase(Ease.OutCubic), 1f);
        // The slits drift down by one stripe per bar.
        tweens.Loop(DOTween.To(x => slitDrift = x, 0f, 1f, 4f).SetEase(Ease.Linear), 4f);
        // Ground: a flash on every beat; a pulse from the horizon to the camera across each bar,
        // arriving on the next downbeat.
        tweens.Loop(DOTween.To(x => gridKick = x, 1f, 0f, 1f).SetEase(Ease.OutQuad), 1f);
        tweens.Loop(DOTween.To(x => gridWavePos = x, tunnelLength, -4f, 4f).SetEase(Ease.InQuad), 4f);
        // Mountains: a faint swell on each downbeat; on a drop, a wave climbing from the foot to
        // the peaks over dropWaveBeats, flaring up and dying away as it goes.
        tweens.Loop(DOTween.To(x => mountainBar = x, 1f, 0f, 4f).SetEase(Ease.OutCubic), 4f);
        // Palms: a slow sway over palmSwayBeats, there and back, each palm on its own phase.
        for (int i = 0; i < palms.Count; i++)
        {
            var palm = palms[i];
            tweens.Loop(DOTween.Sequence()
                .Append(DOTween.To(x => palm.sway = x, -1f, 1f, palmSwayBeats * 0.5f).SetEase(Ease.InOutSine))
                .Append(DOTween.To(x => palm.sway = x, 1f, -1f, palmSwayBeats * 0.5f).SetEase(Ease.InOutSine)),
                palmSwayBeats, (i * 1.37f) % palmSwayBeats);
        }
        dropWave = tweens.Once(DOTween.Sequence()
            .Join(DOTween.To(x => mountainWaveY = x, -6f, dropWaveTop, dropWaveBeats).SetEase(Ease.OutSine))
            .Join(DOTween.To(x => mountainWaveGain = x, 0f, 2.4f, dropWaveBeats * 0.12f).SetEase(Ease.OutQuad))
            .Insert(dropWaveBeats * 0.35f, DOTween.To(x => mountainWaveGain = x, 2.4f, 0f, dropWaveBeats * 0.65f).SetEase(Ease.InQuad)));
    }

    private void OnDestroy()
    {
        tweens.KillAll();
        if (sky != null)
        {
            if (RenderSettings.skybox == sky) RenderSettings.skybox = savedSky;
            Destroy(sky);
        }
    }

    private void LateUpdate()
    {
        if (!built) return;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        tweens.SeekLoops(beats);
        float energy = ClubBeat.Energy;

        if (sky != null)
        {
            sky.SetFloat(SunGlowId, sunGlowIdle + sunGlowKick * sunKick * Mathf.Clamp(energy, 0.3f, 1.6f));
            sky.SetFloat(SunScrollId, slitDrift);
            sky.SetFloat(StarTimeId, ClubBeat.SongTime);
        }

        float e = Mathf.Clamp(energy, 0f, 1.6f);
        Shader.SetGlobalFloat(Pulse0Id, gridFlash * gridKick * e);
        Shader.SetGlobalVector(Wave0Id, new Vector4(gridWavePos, 6f, gridWave * Mathf.Clamp01(energy - 0.3f), 0f));

        // Palms travel with the road on the song clock, recycling past the camera.
        float travel = ClubBeat.SongTime * scrollSpeed;
        float swayAmp = palmSwayDegrees * (0.6f + 0.4f * Mathf.Clamp01(energy));
        foreach (var palm in palms)
        {
            var pos = palm.root.localPosition;
            pos.z = Mathf.Repeat(palm.z - travel + behindCamera, palmSpan) - behindCamera;
            palm.root.localPosition = pos;
            palm.crown.localRotation = Quaternion.Euler(palm.sway * swayAmp * 0.4f, palm.lean, palm.sway * swayAmp);
        }

        SongTweens.Seek(dropWave, ClubBeat.BeatsSinceDrop);
        Shader.SetGlobalFloat(Pulse1Id, 0.25f * mountainBar * e);
        Shader.SetGlobalVector(Wave1Id, new Vector4(mountainWaveY, 7f, mountainWaveGain, 0f));
    }
}
