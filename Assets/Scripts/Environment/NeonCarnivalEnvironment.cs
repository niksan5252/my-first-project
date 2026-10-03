using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Theme NeonCarnival's scenery (all three skins; the prefab's `skin` picks what is built):
///
///   * RioNight: the hills and the city in neon outline down both sides - sugar-loaf peaks with
///     blocks of buildings at their feet and lit windows - and garlands of bulbs strung over the
///     road, the odd and the even bulbs taking the beats in turn; searchlights sweeping the sky
///     over eight beats, and fireworks over the hills on the drops (one on every strong beat);
///   * Carnaval: feather fans standing at the sides, which spring open on the strong beats and
///     fold back (every beat in a drop), and a burst of confetti on every drop; streamers of
///     serpentine flying high over the road, neon pinwheels turning a step per beat, and flashes
///     on the strong beats of a drop;
///   * Copacabana: the promenade floor - wavy lines down the whole road, like the Copacabana
///     pavement - with a wave of light running down it every bar, and neon palms whose fronds sway
///     and whose crowns throw rays of light up; rolling neon waves of the sea beyond the palms,
///     splashes off them on the strong beats, and fireflies drifting over everything;
///   * every skin: two edge lines along the road and a global Volume that swells the bloom on a drop.
///
/// Everything scrolls with the tunnel and wraps behind the camera. Time and energy come from
/// ClubBeat; the movements are SongTweens (beats, on the song's clock). Colours: the theme's two
/// accents (ApplyPalette) and the skin's third colour.
public class NeonCarnivalEnvironment : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [SerializeField] private CarnivalSkin skin;
    [SerializeField] private Color thirdColor = Color.white;
    [SerializeField, Range(0f, 1f)] private float dropBloom = 0.4f;
    [SerializeField] private float bloomPeak = 5f;
    // A soft round mask (Kenney Light Masks circle_a) for every glowing particle: fireworks,
    // splashes, fireflies, streamers. Null = square particles.
    [SerializeField] private Texture2D glowTexture;

    private const float Behind = 12f;
    private const float MaxLevel = 2.4f;
    private const float HillChunk = 24f;      // one hill (and its city) per chunk, joined at the valleys
    private const float WaveLength = 6f;      // the promenade's wave, metres along the road
    private const float GarlandSpacing = 16f;
    private const float FanSpacing = 14f;
    private const float PalmSpacing = 15f;

    private Color primary = new Color(0.1f, 1f, 0.4f);
    private Color secondary = new Color(1f, 0.85f, 0.1f);
    private float emission = 1.3f;

    private float scrollSpeed, span;
    private bool built;
    private Mesh cube, sphere;
    private MaterialPropertyBlock mpb;
    private readonly List<Material> mats = new List<Material>();
    private readonly System.Random rng = new System.Random(7);
    private readonly SongTweens tweens = new SongTweens();
    private float kick, fanOpen, palmSway;

    // Anything that scrolls: a root moved along -Z and wrapped by `span`.
    private struct Scrolling { public Transform t; public float length; }
    private readonly List<Scrolling> scrollers = new List<Scrolling>();

    private readonly List<MeshRenderer> edgeLines = new List<MeshRenderer>();
    private Material hillMat, cityMat, windowMat;
    private readonly List<MeshRenderer> bulbs = new List<MeshRenderer>();
    private struct Fan { public Transform[] feathers; public MeshRenderer[] renderers; public int side; }
    private readonly List<Fan> fans = new List<Fan>();
    private ParticleSystem confetti;
    private Material confettiMat;
    private float lastBeatsSinceDrop = float.PositiveInfinity;
    private struct WaveLine { public MeshRenderer r; public Transform root; public int color; }
    private readonly List<WaveLine> waveLines = new List<WaveLine>();
    private struct Palm { public Transform crown, rays; public MeshRenderer[] trunk, fronds, rayRenderers; public float seed; }
    private readonly List<Palm> palms = new List<Palm>();
    private Volume bloomVolume;
    private VolumeProfile bloomProfile;

    // The second round of decorations, one set per look.
    private Material glowMat;
    private ParticleSystem fireworks, streamers, splashes, fireflies;
    private readonly List<Transform> searchlights = new List<Transform>();
    private readonly List<MeshRenderer> searchlightRenderers = new List<MeshRenderer>();
    private struct Pinwheel { public Transform spin; public MeshRenderer[] blades; public int dir; }
    private readonly List<Pinwheel> pinwheels = new List<Pinwheel>();
    private struct SeaRow { public Transform root; public Transform[] segs; public MeshRenderer[] rends; public float x, baseY, phase; }
    private readonly List<SeaRow> sea = new List<SeaRow>();
    private const float SeaChunk = 24f, SeaStep = 1.5f;
    private int lastStrongBeat = int.MinValue;
    private float sweep, spinStep, flash;

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
        // A whole number of hill chunks (so the outline closes on itself when it wraps), with a
        // chunk to spare so a long piece wraps out of sight beyond the fog.
        span = Mathf.Ceil((tunnelLength + Behind + 8f + HillChunk) / HillChunk) * HillChunk;
        mpb = new MaterialPropertyBlock();
        cube = RemixGeo.Primitive(PrimitiveType.Cube);
        sphere = RemixGeo.Primitive(PrimitiveType.Sphere);

        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutExpo), 1f);
        tweens.Loop(DOTween.Sequence()
            .Append(DOTween.To(x => fanOpen = x, 0f, 1f, 0.4f).SetEase(Ease.OutBack))
            .Append(DOTween.To(x => fanOpen = x, 1f, 0f, 1.6f).SetEase(Ease.InOutSine)), 2f);
        tweens.Loop(DOTween.To(x => palmSway = x, -1f, 1f, 2f).SetEase(Ease.InOutSine).SetLoops(2, LoopType.Yoyo), 4f);
        // Searchlights sweep out and back over eight beats; pinwheels step 45 degrees a beat.
        tweens.Loop(DOTween.To(x => sweep = x, -1f, 1f, 4f).SetEase(Ease.InOutSine).SetLoops(2, LoopType.Yoyo), 8f);
        tweens.Loop(DOTween.To(x => spinStep = x, 0f, 1f, 0.6f).SetEase(Ease.OutBack), 1f);
        // A flash: up at once on the beat, gone within half a beat.
        tweens.Loop(DOTween.Sequence().Append(DOTween.To(x => flash = x, 1f, 0f, 0.5f).SetEase(Ease.OutQuad)).AppendInterval(0.5f), 1f);

        BuildEdgeLines(tunnelLength);
        switch (skin)
        {
            case CarnivalSkin.RioNight: BuildHills(); BuildGarlands(); BuildSearchlights(tunnelLength); BuildFireworks(); break;
            case CarnivalSkin.Carnaval: BuildFans(); BuildConfetti(); BuildStreamers(); BuildPinwheels(); break;
            case CarnivalSkin.Copacabana: BuildPromenade(); BuildPalms(); BuildSea(); BuildSplashes(); BuildFireflies(); break;
        }
        BuildBloomVolume();
    }

    private void OnDestroy()
    {
        tweens.KillAll();
        foreach (var m in mats) if (m != null) Destroy(m);
        if (bloomProfile != null) Destroy(bloomProfile);
    }

    private Material Mat(Color c, float level = 1f)
    {
        var m = RemixGeo.Neon(c, emission * level);
        mats.Add(m);
        return m;
    }

    // `length` is how far the piece reaches down the road from its root: it wraps to the far end
    // only once all of it is behind the camera.
    private Transform Scroller(string name, float z, float length = 0f)
    {
        var t = new GameObject(name).transform;
        t.SetParent(transform, false);
        t.localPosition = new Vector3(0f, 0f, z);
        scrollers.Add(new Scrolling { t = t, length = length });
        return t;
    }

    // Spacing that divides the loop exactly, so there is no seam where it wraps.
    private float Even(float spacing, out int count)
    {
        count = Mathf.Max(1, Mathf.RoundToInt(span / spacing));
        return span / count;
    }

    private float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // --- building ---

    private void BuildEdgeLines(float tunnelLength)
    {
        var m = Mat(primary);
        foreach (float x in new[] { -5.2f, 5.2f })
            edgeLines.Add(RemixGeo.Beam(transform, "Edge", cube, m, new Vector3(x, 0.03f, -Behind), new Vector3(x, 0.03f, tunnelLength + 12f), 0.06f));
    }

    // RioNight: one hill per chunk and side, starting and ending at the valley height so the chunks
    // join into one ridge line; buildings at the foot of every other hill, windows as dots.
    private void BuildHills()
    {
        hillMat = Mat(primary);
        cityMat = Mat(secondary);
        windowMat = Mat(Color.Lerp(secondary, Color.white, 0.4f));
        int chunks = Mathf.RoundToInt(span / HillChunk);
        for (int c = 0; c < chunks; c++)
            for (int side = -1; side <= 1; side += 2)
            {
                var root = Scroller("Hill", -Behind + c * HillChunk, HillChunk);
                float x0 = side * 17f;
                float peak = Rand(6f, 13f), at = Rand(0.35f, 0.65f), sharp = Rand(0.6f, 1.4f);
                const int steps = 10;
                Vector3 prev = Vector3.zero;
                for (int i = 0; i <= steps; i++)
                {
                    float u = i / (float)steps;
                    // A sugar-loaf: steep on one face, rounder on the other.
                    float d = u < at ? u / at : (1f - u) / (1f - at);
                    float h = 1.5f + (peak - 1.5f) * Mathf.Pow(Mathf.Sin(d * Mathf.PI * 0.5f), sharp);
                    var p = new Vector3(x0 + side * Mathf.Sin(u * Mathf.PI) * 3f, h, u * HillChunk);
                    if (i > 0) RemixGeo.Beam(root, "Ridge", cube, hillMat, prev, p, 0.12f);
                    prev = p;
                }
                if (c % 2 == 1) continue;
                // The city at its foot: a row of block outlines with a few lit windows.
                float z = Rand(1f, 4f);
                while (z < HillChunk - 4f)
                {
                    float w = Rand(1.6f, 3.2f), h = Rand(2.5f, 7f);
                    float bx = side * Rand(10f, 13f);
                    var a = new Vector3(bx, 0f, z); var b = new Vector3(bx, h, z);
                    var c2 = new Vector3(bx, h, z + w); var d2 = new Vector3(bx, 0f, z + w);
                    RemixGeo.Beam(root, "Wall", cube, cityMat, a, b, 0.07f);
                    RemixGeo.Beam(root, "Roof", cube, cityMat, b, c2, 0.07f);
                    RemixGeo.Beam(root, "Wall", cube, cityMat, c2, d2, 0.07f);
                    for (float wy = 1f; wy < h - 0.6f; wy += 1.1f)
                        for (float wz = z + 0.5f; wz < z + w - 0.3f; wz += 0.8f)
                            if (rng.NextDouble() < 0.45)
                            {
                                var wp = new Vector3(bx, wy, wz);
                                RemixGeo.Beam(root, "Window", cube, windowMat, wp, wp, 0.16f);
                            }
                    z += w + Rand(0.4f, 1.5f);
                }
            }
    }

    // RioNight: strings of bulbs over the road, sagging between two poles.
    private void BuildGarlands()
    {
        var pole = Mat(Color.Lerp(primary, Color.black, 0.4f), 0.6f);
        var bulbMat = Mat(secondary);
        float spacing = Even(GarlandSpacing, out int count);
        for (int g = 0; g < count; g++)
        {
            var root = Scroller("Garland", -Behind + g * spacing);
            const float half = 6.4f, top = 8.6f, sag = 1.4f;
            RemixGeo.Beam(root, "Pole", cube, pole, new Vector3(-half, 0f, 0f), new Vector3(-half, top, 0f), 0.1f);
            RemixGeo.Beam(root, "Pole", cube, pole, new Vector3(half, 0f, 0f), new Vector3(half, top, 0f), 0.1f);
            const int n = 17;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n;
                var p = new Vector3(Mathf.Lerp(-half, half, u), top - sag * 4f * u * (1f - u), 0f);
                if (i > 0) RemixGeo.Beam(root, "Wire", cube, pole, prev, p, 0.025f);
                prev = p;
                bulbs.Add(RemixGeo.Beam(root, "Bulb", sphere, bulbMat, p + Vector3.down * 0.15f, p + Vector3.down * 0.15f, 0.24f));
            }
        }
    }

    // Carnaval: a fan of feathers at each side every few metres, each feather a beam with an eye at its tip.
    private void BuildFans()
    {
        var m = Mat(primary);
        float spacing = Even(FanSpacing, out int count);
        for (int f = 0; f < count; f++)
            for (int side = -1; side <= 1; side += 2)
            {
                var root = Scroller("Fan", -Behind + f * spacing + (side > 0 ? spacing * 0.5f : 0f));
                var bas = new GameObject("Base").transform;
                bas.SetParent(root, false);
                bas.localPosition = new Vector3(side * 8.5f, 0.6f, 0f);
                const int feathersPerFan = 9;
                var feathers = new Transform[feathersPerFan];
                var rends = new MeshRenderer[feathersPerFan * 2];
                float len = Rand(4.5f, 6.5f);
                for (int i = 0; i < feathersPerFan; i++)
                {
                    var pivot = new GameObject("Feather").transform;
                    pivot.SetParent(bas, false);
                    feathers[i] = pivot;
                    rends[i * 2] = RemixGeo.Beam(pivot, "Quill", cube, m, Vector3.zero, Vector3.up * len, 0.08f);
                    rends[i * 2 + 1] = RemixGeo.Beam(pivot, "Eye", sphere, m, Vector3.up * len, Vector3.up * len, 0.55f);
                }
                fans.Add(new Fan { feathers = feathers, renderers = rends, side = side });
            }
    }

    // Carnaval: confetti over the road, a burst on every drop and a light fall while a drop lasts.
    private void BuildConfetti()
    {
        var go = new GameObject("Confetti");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 11f, 22f);
        confetti = go.AddComponent<ParticleSystem>();
        confetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = confetti.main;
        main.duration = 1f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0.35f;
        main.maxParticles = 1500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        Color hdr(Color c) => c * 2.2f;
        main.startColor = new ParticleSystem.MinMaxGradient(Gradient3(hdr(primary), hdr(secondary), hdr(thirdColor))) { mode = ParticleSystemGradientMode.RandomColor };
        var emissionModule = confetti.emission;
        emissionModule.rateOverTime = 0f;
        var shape = confetti.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(18f, 1f, 30f);
        var rot = confetti.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
        rot.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
        rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        var noise = confetti.noise;
        noise.enabled = true;
        noise.strength = 1.2f;
        noise.frequency = 0.4f;
        var vel = confetti.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.z = new ParticleSystem.MinMaxCurve(-scrollSpeed, -scrollSpeed);     // the confetti stays with the road
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        confettiMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        confettiMat.SetColor("_BaseColor", Color.white);
        mats.Add(confettiMat);
        r.sharedMaterial = confettiMat;
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = RemixGeo.Primitive(PrimitiveType.Quad);
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        confetti.Play();
    }

    private static Gradient Gradient3(Color a, Color b, Color c)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(a, 0.33f), new GradientColorKey(b, 0.34f),
                          new GradientColorKey(b, 0.66f), new GradientColorKey(c, 0.67f), new GradientColorKey(c, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        g.mode = GradientMode.Fixed;
        return g;
    }

    // Copacabana: the promenade. Wavy lines down the road, three colours in turn; one chunk per
    // wavelength, so the waves run on unbroken as the chunks wrap.
    private void BuildPromenade()
    {
        var lineMats = new[] { Mat(primary, 0.6f), Mat(secondary, 0.6f), Mat(thirdColor, 0.6f) };
        int chunks = Mathf.RoundToInt(span / WaveLength);     // span is a multiple of HillChunk, and so of WaveLength
        const int steps = 8;
        for (int c = 0; c < chunks; c++)
        {
            var root = Scroller("Promenade", -Behind + c * WaveLength, WaveLength);
            int k = 0;
            for (float x0 = -11f; x0 <= 11.01f; x0 += 1.375f, k++)
            {
                int color = k % 3 == 1 ? 1 : (k % 3 == 2 ? 2 : 0);
                Vector3 prev = Vector3.zero;
                for (int i = 0; i <= steps; i++)
                {
                    float u = i / (float)steps;
                    var p = new Vector3(x0 + 0.45f * Mathf.Sin(u * Mathf.PI * 2f), 0.025f, u * WaveLength);
                    if (i > 0)
                    {
                        var r = RemixGeo.Beam(root, "Wave", cube, lineMats[color], prev, p, 0.05f);
                        waveLines.Add(new WaveLine { r = r, root = root, color = color });
                    }
                    prev = p;
                }
            }
        }
    }

    // Copacabana: neon palms down both sides - a leaning trunk of segments, a crown of drooping fronds.
    private void BuildPalms()
    {
        var trunkMat = Mat(thirdColor);
        var frondMat = Mat(primary);
        float spacing = Even(PalmSpacing, out int count);
        for (int p = 0; p < count; p++)
            for (int side = -1; side <= 1; side += 2)
            {
                var root = Scroller("Palm", -Behind + p * spacing + (side > 0 ? spacing * 0.5f : 0f));
                float x = side * Rand(8f, 10.5f), height = Rand(6.5f, 9f), lean = -side * Rand(0.8f, 1.8f);
                var trunk = new List<MeshRenderer>();
                Vector3 prev = new Vector3(x, 0f, 0f);
                const int segs = 6;
                for (int i = 1; i <= segs; i++)
                {
                    float u = i / (float)segs;
                    var q = new Vector3(x + lean * u * u, height * u, 0f);
                    trunk.Add(RemixGeo.Beam(root, "Trunk", cube, trunkMat, prev, q, 0.22f - 0.08f * u));
                    prev = q;
                }
                var crown = new GameObject("Crown").transform;
                crown.SetParent(root, false);
                crown.localPosition = prev;
                var fronds = new List<MeshRenderer>();
                const int count7 = 7;
                for (int f = 0; f < count7; f++)
                {
                    float yaw = f * 360f / count7 + Rand(-10f, 10f);
                    var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                    Vector3 a = Vector3.zero;
                    float len = Rand(2.6f, 3.6f);
                    for (int i = 1; i <= 4; i++)
                    {
                        float u = i / 4f;
                        var b = dir * (len * u) + Vector3.up * (0.6f * u - 1.9f * u * u);
                        fronds.Add(RemixGeo.Beam(crown, "Frond", cube, frondMat, a, b, 0.09f));
                        a = b;
                    }
                }
                // Rays: three long beams fanning up out of the crown, away from the road.
                var rays = new GameObject("Rays").transform;
                rays.SetParent(root, false);
                rays.localPosition = prev;
                var rayList = new List<MeshRenderer>();
                for (int k = 0; k < 3; k++)
                {
                    var dir = Quaternion.Euler(0f, 0f, side * (12f + k * 16f)) * Vector3.up;
                    rayList.Add(RemixGeo.Beam(rays, "Ray", cube, frondMat, Vector3.zero, dir * 11f, 0.05f));
                }
                palms.Add(new Palm { crown = crown, rays = rays, trunk = trunk.ToArray(), fronds = fronds.ToArray(),
                                     rayRenderers = rayList.ToArray(), seed = Rand(0f, 6.28f) });
            }
    }

    private void BuildBloomVolume()
    {
        if (dropBloom <= 0f) return;
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

    private void Update()
    {
        if (!built) return;
        float delta = scrollSpeed * Time.deltaTime;
        float beats = CarnivalLook.Beats;
        tweens.SeekLoops(beats);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float baseZ = transform.position.z;

        foreach (var s in scrollers)
        {
            var p = s.t.localPosition;
            p.z -= delta;
            if (p.z + s.length < -Behind) p.z += span;
            s.t.localPosition = p;
        }

        mpb.SetColor(RemixGeo.EmissionId, primary * Mathf.Min(MaxLevel, emission * (0.4f + 0.5f * kick * energy)));
        foreach (var r in edgeLines) r.SetPropertyBlock(mpb);

        switch (skin)
        {
            case CarnivalSkin.RioNight: UpdateRio(energy); UpdateSky(energy); break;
            case CarnivalSkin.Carnaval: UpdateCarnaval(energy); UpdateParade(energy); break;
            case CarnivalSkin.Copacabana: UpdateCopacabana(energy, baseZ); UpdateSea(energy, baseZ); break;
        }

        if (bloomVolume != null)
        {
            float drop = Mathf.Clamp01((ClubBeat.Energy - 1f) / 0.6f);
            bloomVolume.weight = dropBloom * drop * (0.55f + 0.45f * kick);
            // Carnaval's flashes: a short swell of the bloom on the strong beats of a drop.
            if (skin == CarnivalSkin.Carnaval && CarnivalLook.InDrop && CarnivalLook.StrongBeat)
                bloomVolume.weight = Mathf.Max(bloomVolume.weight, 0.7f * flash);
        }
    }

    /// True once per strong beat (beats one and three), on the frame it starts.
    private bool NewStrongBeat()
    {
        if (!CarnivalLook.StrongBeat || ClubBeat.BeatIndex == lastStrongBeat) return false;
        lastStrongBeat = ClubBeat.BeatIndex;
        return true;
    }

    // --- the second round: particles ---

    private Material GlowMat()
    {
        if (glowMat != null) return glowMat;
        glowMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "CarnivalGlow" };
        if (glowTexture != null) glowMat.SetTexture("_BaseMap", glowTexture);
        glowMat.SetColor("_BaseColor", Color.white);
        glowMat.SetFloat("_Surface", 1f);
        glowMat.SetFloat("_Blend", 2f);
        glowMat.SetOverrideTag("RenderType", "Transparent");
        glowMat.SetInt("_SrcBlend", (int)BlendMode.One);
        glowMat.SetInt("_DstBlend", (int)BlendMode.One);
        glowMat.SetInt("_ZWrite", 0);
        glowMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        glowMat.renderQueue = (int)RenderQueue.Transparent;
        mats.Add(glowMat);
        return glowMat;
    }

    // A glowing particle system: world space, nothing emitted until asked, additive soft dots.
    private ParticleSystem Glow(string name, Vector3 pos, int max, bool trails)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission;
        em.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = GlowMat();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (trails)
        {
            r.trailMaterial = GlowMat();
            var t = ps.trails;
            t.enabled = true;
            t.ratio = 1f;
            t.dieWithParticles = true;
            t.inheritParticleColor = true;
            t.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        }
        // Fade out over the life.
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        return ps;
    }

    private void RoadVelocity(ParticleSystem ps, float share)
    {
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.z = new ParticleSystem.MinMaxCurve(-scrollSpeed * share, -scrollSpeed * share);
    }

    // RioNight: searchlights behind the hills, long faint beams that sweep the sky.
    private void BuildSearchlights(float tunnelLength)
    {
        var m = Mat(thirdColor, 0.4f);
        float[] xs = { -30f, -22f, 22f, 30f };
        float[] zs = { tunnelLength * 0.45f, tunnelLength * 0.7f, tunnelLength * 0.55f, tunnelLength * 0.8f };
        for (int i = 0; i < xs.Length; i++)
        {
            var pivot = new GameObject("Searchlight").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3(xs[i], 0f, zs[i]);
            searchlightRenderers.Add(RemixGeo.Beam(pivot, "Beam", cube, m, Vector3.zero, Vector3.up * 70f, 0.6f));
            searchlights.Add(pivot);
        }
    }

    // RioNight: fireworks - a burst of sparks with short trails, high over the hills.
    private void BuildFireworks()
    {
        fireworks = Glow("Fireworks", new Vector3(0f, 18f, 60f), 3000, true);
        var main = fireworks.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 11f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
        main.gravityModifier = 0.5f;
        var shape = fireworks.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;
        var t = fireworks.trails;
        t.lifetime = new ParticleSystem.MinMaxCurve(0.25f);
        t.widthOverTrail = new ParticleSystem.MinMaxCurve(0.12f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        fireworks.Play();
    }

    private void LaunchFirework(int count)
    {
        float side = rng.NextDouble() < 0.5 ? -1f : 1f;
        fireworks.transform.localPosition = new Vector3(side * Rand(12f, 26f), Rand(14f, 22f), Rand(45f, 95f));
        var main = fireworks.main;
        main.startColor = CarnivalLook.Of(rng.Next(3), primary, secondary, thirdColor) * 2.5f;
        fireworks.Emit(count);
    }

    // Carnaval: serpentine - long trails curling high over the road and the sides.
    private void BuildStreamers()
    {
        streamers = Glow("Serpentine", new Vector3(0f, 9f, 40f), 120, true);
        var main = streamers.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.12f);
        main.gravityModifier = 0.08f;
        main.startColor = new ParticleSystem.MinMaxGradient(Gradient3(primary * 2f, secondary * 2f, thirdColor * 2f)) { mode = ParticleSystemGradientMode.RandomColor };
        var shape = streamers.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(30f, 3f, 50f);
        var noise = streamers.noise;
        noise.enabled = true;
        noise.strength = 2.5f;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.4f;
        var t = streamers.trails;
        t.lifetime = new ParticleSystem.MinMaxCurve(1.3f);
        t.widthOverTrail = new ParticleSystem.MinMaxCurve(0.14f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
        RoadVelocity(streamers, 0.5f);
        var em = streamers.emission;
        em.rateOverTime = 5f;
        streamers.Play();
    }

    // Carnaval: big neon pinwheels high at the sides, eight blades each, turning a step per beat.
    private void BuildPinwheels()
    {
        var m = Mat(primary);
        float spacing = Even(30f, out int count);
        for (int k = 0; k < count; k++)
            for (int side = -1; side <= 1; side += 2)
            {
                var root = Scroller("Pinwheel", -Behind + k * spacing + (side > 0 ? spacing * 0.5f : 0f));
                var spin = new GameObject("Spin").transform;
                spin.SetParent(root, false);
                spin.localPosition = new Vector3(side * 13f, 9.5f, 0f);
                spin.localRotation = Quaternion.Euler(0f, side * 35f, 0f);     // turned a little toward the road
                var hub = new GameObject("Hub").transform;
                hub.SetParent(spin, false);
                const int bladesN = 8;
                var blades = new MeshRenderer[bladesN * 2];
                for (int b = 0; b < bladesN; b++)
                {
                    var dir = Quaternion.Euler(0f, 0f, b * 360f / bladesN) * Vector3.up;
                    var dir2 = Quaternion.Euler(0f, 0f, b * 360f / bladesN + 22f) * Vector3.up;
                    blades[b * 2] = RemixGeo.Beam(hub, "Blade", cube, m, Vector3.zero, dir * 2.8f, 0.1f);
                    blades[b * 2 + 1] = RemixGeo.Beam(hub, "Edge", cube, m, dir * 2.8f, dir2 * 1.6f, 0.08f);
                }
                pinwheels.Add(new Pinwheel { spin = hub, blades = blades, dir = side });
            }
    }

    // Copacabana: the sea beyond the palms - rows of neon waves rolling toward the camera.
    private void BuildSea()
    {
        var m = Mat(primary, 0.7f);
        int chunks = Mathf.RoundToInt(span / SeaChunk);
        int per = Mathf.RoundToInt(SeaChunk / SeaStep);
        for (int c = 0; c < chunks; c++)
            for (int side = -1; side <= 1; side += 2)
                for (int row = 0; row < 2; row++)
                {
                    var root = Scroller("Sea", -Behind + c * SeaChunk, SeaChunk);
                    var segs = new Transform[per];
                    var rends = new MeshRenderer[per];
                    for (int i = 0; i < per; i++)
                    {
                        rends[i] = RemixGeo.Beam(root, "Wave", cube, m, Vector3.zero, Vector3.forward, 0.08f);
                        segs[i] = rends[i].transform;
                    }
                    sea.Add(new SeaRow { root = root, segs = segs, rends = rends, x = side * (15f + row * 3.5f),
                                         baseY = 0.4f + row * 0.5f, phase = row * 1.7f });
                }
    }

    // Copacabana: spray thrown up off the waves on the strong beats.
    private void BuildSplashes()
    {
        splashes = Glow("Splashes", new Vector3(16f, 0.8f, 30f), 1500, false);
        var main = splashes.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.gravityModifier = 1f;
        main.startColor = new ParticleSystem.MinMaxGradient(Gradient3(secondary * 2f, primary * 2f, secondary * 2f)) { mode = ParticleSystemGradientMode.RandomColor };
        var shape = splashes.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 25f;
        shape.radius = 0.6f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        RoadVelocity(splashes, 1f);
        splashes.Play();
    }

    // Copacabana: fireflies - small warm lights drifting high over the road and the beach.
    private void BuildFireflies()
    {
        fireflies = Glow("Fireflies", new Vector3(0f, 6.5f, 45f), 400, false);
        var main = fireflies.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(thirdColor, Color.white, 0.5f) * 2f, secondary * 1.6f);
        var shape = fireflies.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(34f, 5f, 80f);
        var noise = fireflies.noise;
        noise.enabled = true;
        noise.strength = 0.8f;
        noise.frequency = 0.5f;
        var size = fireflies.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.2f, 1f), new Keyframe(0.4f, 0.4f), new Keyframe(0.6f, 1f), new Keyframe(0.8f, 0.4f), new Keyframe(1f, 0f)));
        RoadVelocity(fireflies, 1f);
        var em = fireflies.emission;
        em.rateOverTime = 30f;
        fireflies.Play();
    }

    // --- the second round: every frame ---

    private void UpdateSky(float energy)
    {
        bool strong = NewStrongBeat();
        for (int i = 0; i < searchlights.Count; i++)
        {
            float dir = i % 2 == 0 ? 1f : -1f;
            searchlights[i].localRotation = Quaternion.Euler(sweep * 10f * dir - 8f, 0f, sweep * 28f * dir + (searchlights[i].localPosition.x < 0 ? -12f : 12f));
            float level = 0.25f + 0.35f * (CarnivalLook.StrongBeat ? kick : 0f) * Mathf.Min(energy, 1.3f);
            mpb.SetColor(RemixGeo.EmissionId, (i % 2 == 0 ? thirdColor : secondary) * Mathf.Min(MaxLevel, emission * level));
            searchlightRenderers[i].SetPropertyBlock(mpb);
        }
        if (fireworks == null) return;
        // The drop opens with a salvo; while it lasts, one firework every strong beat.
        float since = ClubBeat.BeatsSinceDrop;
        if (since < lastBeatsSinceDrop && since < 1f) for (int k = 0; k < 4; k++) LaunchFirework(140);
        else if (strong && CarnivalLook.InDrop) LaunchFirework(110);
        lastBeatsSinceDrop = since;
    }

    private void UpdateParade(float energy)
    {
        bool drop = CarnivalLook.InDrop;
        float turn = (Mathf.Floor(CarnivalLook.Beats) + spinStep) * 45f;
        for (int i = 0; i < pinwheels.Count; i++)
        {
            var p = pinwheels[i];
            p.spin.localRotation = Quaternion.Euler(0f, 0f, turn * p.dir);
            float level = 0.45f + 0.5f * kick * energy + (drop && CarnivalLook.StrongBeat ? 1.2f * flash : 0f);
            for (int b = 0; b < p.blades.Length; b++)
            {
                mpb.SetColor(RemixGeo.EmissionId, CarnivalLook.Of(b / 2 + i, primary, secondary, thirdColor) * Mathf.Min(MaxLevel, emission * level));
                p.blades[b].SetPropertyBlock(mpb);
            }
        }
        if (streamers != null)
        {
            var em = streamers.emission;
            em.rateOverTime = drop ? 12f : 5f;
        }
    }

    private void UpdateSea(float energy, float baseZ)
    {
        float t = ClubBeat.SongTime;
        float beat = Mathf.Max(0.2f, ClubBeat.SecondsPerBeat);
        const float k = Mathf.PI * 2f / 9f;                    // a crest every nine metres
        float w = Mathf.PI * 2f / (beat * 4f) * 1f;            // rolling one wavelength a bar
        foreach (var row in sea)
        {
            float rootZ = row.root.localPosition.z;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= row.segs.Length; i++)
            {
                float z = i * SeaStep;
                float y = row.baseY + 0.55f * Mathf.Sin(k * (z + rootZ + baseZ) + w * t + row.phase) * (0.8f + 0.25f * Mathf.Min(energy, 1.4f));
                var p = new Vector3(row.x, y, z);
                if (i > 0)
                {
                    var seg = row.segs[i - 1];
                    Vector3 edge = p - prev;
                    seg.localPosition = (p + prev) * 0.5f;
                    seg.localRotation = Quaternion.FromToRotation(Vector3.up, edge.normalized);
                    seg.localScale = new Vector3(0.08f, edge.magnitude + 0.08f, 0.08f);
                }
                prev = p;
            }
            float level = 0.35f + 0.5f * kick * energy;
            mpb.SetColor(RemixGeo.EmissionId, primary * Mathf.Min(MaxLevel, emission * level));
            foreach (var r in row.rends) r.SetPropertyBlock(mpb);
        }

        if (splashes != null && NewStrongBeat())
        {
            int bursts = CarnivalLook.InDrop ? 4 : 2;
            for (int b = 0; b < bursts; b++)
            {
                float side = b % 2 == 0 ? -1f : 1f;
                splashes.transform.localPosition = new Vector3(side * Rand(15f, 19f), 1f, Rand(12f, 60f));
                splashes.Emit(CarnivalLook.InDrop ? 45 : 25);
            }
        }
    }

    private void UpdateRio(float energy)
    {
        // The ridge breathes on the bar, the city on the beat.
        RemixGeo.SetEmission(hillMat, primary, Mathf.Min(MaxLevel, emission * (0.45f + 0.25f * kick * energy)));
        RemixGeo.SetEmission(cityMat, secondary, Mathf.Min(MaxLevel, emission * (0.35f + 0.45f * kick * energy)));
        bool odd = (ClubBeat.BeatIndex & 1) == 1;
        for (int i = 0; i < bulbs.Count; i++)
        {
            bool mine = ((i & 1) == 1) == odd;
            float level = mine ? 0.5f + 1.3f * kick * Mathf.Min(energy, 1.3f) : 0.18f;
            Color c = i % 3 == 0 ? primary : secondary;
            mpb.SetColor(RemixGeo.EmissionId, c * Mathf.Min(MaxLevel, emission * level));
            bulbs[i].SetPropertyBlock(mpb);
        }
    }

    private void UpdateCarnaval(float energy)
    {
        // Strong beats open the fans; in a drop every beat does.
        bool drop = CarnivalLook.InDrop;
        float open = (drop || CarnivalLook.StrongBeat) ? fanOpen : fanOpen * 0.35f;
        for (int f = 0; f < fans.Count; f++)
        {
            var fan = fans[f];
            int n = fan.feathers.Length;
            float spread = Mathf.Lerp(18f, 120f, open);
            for (int i = 0; i < n; i++)
            {
                float u = n > 1 ? i / (float)(n - 1) - 0.5f : 0f;
                // Leaning out away from the road, the fan opening in the plane across it.
                fan.feathers[i].localRotation = Quaternion.Euler(0f, 0f, -fan.side * 18f + u * spread);
                Color c = CarnivalLook.Of(i + f, primary, secondary, thirdColor);
                float level = 0.5f + 0.9f * open + 0.4f * kick * energy;
                mpb.SetColor(RemixGeo.EmissionId, c * Mathf.Min(MaxLevel, emission * level));
                fan.renderers[i * 2].SetPropertyBlock(mpb);
                mpb.SetColor(RemixGeo.EmissionId, Color.Lerp(c, Color.white, 0.3f) * Mathf.Min(MaxLevel, emission * (level + 0.3f)));
                fan.renderers[i * 2 + 1].SetPropertyBlock(mpb);
            }
        }

        if (confetti == null) return;
        // A new drop: BeatsSinceDrop starts again from 0.
        float since = ClubBeat.BeatsSinceDrop;
        if (since < lastBeatsSinceDrop && since < 1f) confetti.Emit(700);
        lastBeatsSinceDrop = since;
        var em = confetti.emission;
        em.rateOverTime = drop ? 60f * Mathf.Min(energy, 1.5f) : 0f;
    }

    private void UpdateCopacabana(float energy, float baseZ)
    {
        // The floor: dim waves, a wave of light running down the road every bar, a lift on the kick.
        foreach (var w in waveLines)
        {
            float z = w.root.localPosition.z + w.r.transform.localPosition.z + baseZ;
            float level = 0.2f + 0.25f * kick * energy + 1.6f * CarnivalLook.Wave(z);
            Color c = w.color == 0 ? primary : (w.color == 1 ? secondary : thirdColor);
            mpb.SetColor(RemixGeo.EmissionId, c * Mathf.Min(MaxLevel, emission * level));
            w.r.SetPropertyBlock(mpb);
        }
        // Palms: the crowns sway over four beats, the fronds flash on the kick.
        for (int i = 0; i < palms.Count; i++)
        {
            var p = palms[i];
            p.crown.localRotation = Quaternion.Euler(palmSway * 6f, Mathf.Sin(p.seed) * 20f, palmSway * 9f * (i % 2 == 0 ? 1f : -1f));
            mpb.SetColor(RemixGeo.EmissionId, (i % 2 == 0 ? primary : secondary) * Mathf.Min(MaxLevel, emission * (0.55f + 0.8f * kick * energy)));
            foreach (var r in p.fronds) r.SetPropertyBlock(mpb);
            mpb.SetColor(RemixGeo.EmissionId, thirdColor * Mathf.Min(MaxLevel, emission * (0.4f + 0.2f * kick)));
            foreach (var r in p.trunk) r.SetPropertyBlock(mpb);
            p.rays.localRotation = Quaternion.Euler(0f, 0f, palmSway * 10f * (i % 2 == 0 ? 1f : -1f));
            float rayLevel = 0.15f + 0.6f * kick * Mathf.Min(energy, 1.3f) + (CarnivalLook.InDrop ? 0.3f : 0f);
            mpb.SetColor(RemixGeo.EmissionId, Color.Lerp(primary, Color.white, 0.3f) * Mathf.Min(MaxLevel, emission * rayLevel));
            foreach (var r in p.rayRenderers) r.SetPropertyBlock(mpb);
        }
    }
}
