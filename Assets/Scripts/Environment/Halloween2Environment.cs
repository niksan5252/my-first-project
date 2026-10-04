using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Video 8 ("Halloween 2") scenery, all three skins (the prefab's `skin` picks what is built):
///
///   * WitchSwamp: crooked bare trees down both sides, their branches breathing on the bar; boiling
///     cauldrons at the roadside whose brew flares on the kick and throws green bubbles up on every
///     beat; clusters of glowing mushrooms lighting odd/even on alternate beats; swamp fog drifting
///     low beyond the road; flocks of bats crossing the sky, wings beating on the beat;
///   * MadLab: tesla coils in pairs across the road - a bolt of lightning jumps between their tops
///     on the strong beats (every beat in a drop) and sparks fly off them; tables of flasks whose
///     liquid boils in turn, beat by beat, with bubbles rising out of the necks; gantries of hanging
///     lamps over the road that blink in a pattern that changes every beat (all flash in a drop);
///   * VampireCastle: a colonnade of gothic pointed windows with stained glass down both sides, a
///     wave of light running through the glass every bar; candelabras with flickering flames that
///     flare on the kick and throw gold sparks on the strong beats of a drop; bats; a storm of rose
///     petals on every drop. The huge red moon is the theme's sky.
///   * every skin: two edge lines along the road and a global Volume that swells the bloom on a drop.
///
/// Nothing stands on the road or low over it - scenery keeps to |x| > 6 m, and what crosses over
/// the road (bolts, lamps, bats) is above the ring arches - so the notes stay readable. Everything
/// scrolls with the tunnel and wraps behind the camera. Time and energy come from ClubBeat; the
/// movements are SongTweens (beats, on the song's clock). Colours: the theme's two accents
/// (ApplyPalette) and the skin's third colour.
public class Halloween2Environment : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [SerializeField] private Halloween2Skin skin;
    [SerializeField] private Color thirdColor = Color.white;
    [SerializeField, Range(0f, 1f)] private float dropBloom = 0.45f;
    [SerializeField] private float bloomPeak = 5f;
    // A soft round mask (Kenney Light Masks circle_a) for every glowing particle. Null = square particles.
    [SerializeField] private Texture2D glowTexture;

    private const float Behind = 12f;
    private const float MaxLevel = 2.4f;
    private const float Chunk = 24f;

    private Color primary = new Color(0.6f, 0.1f, 1f);
    private Color secondary = new Color(0.4f, 1f, 0.1f);
    private float emission = 1.3f;

    private float scrollSpeed, span;
    private bool built;
    private Mesh cube, sphere;
    private MaterialPropertyBlock mpb;
    private readonly List<Material> mats = new List<Material>();
    private readonly System.Random rng = new System.Random(8);
    private readonly SongTweens tweens = new SongTweens();
    private float kick, flash, sway, boil;

    private struct Scrolling { public Transform t; public float length; }
    private readonly List<Scrolling> scrollers = new List<Scrolling>();
    private readonly List<MeshRenderer> edgeLines = new List<MeshRenderer>();
    private Material glowMat;
    private Volume bloomVolume;
    private VolumeProfile bloomProfile;
    private float lastBeatsSinceDrop = float.PositiveInfinity;
    private int lastBeat = int.MinValue;
    private Material sky, savedSky;
    private static readonly int MoonPulseId = Shader.PropertyToID("_MoonPulse");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");

    // Bats: a flock is a V of bats crossing the sky over eight beats.
    private struct Bat { public Transform left, right; }
    private struct Flock { public Transform root; public Bat[] bats; public MeshRenderer[] rends; public float offset, z, y; public int dir; }
    private readonly List<Flock> flocks = new List<Flock>();

    // Swamp.
    private struct Tree { public Transform[] branches; public MeshRenderer[] rends; public float seed; }
    private readonly List<Tree> trees = new List<Tree>();
    private readonly List<Transform> brews = new List<Transform>();
    private readonly List<MeshRenderer> brewRenderers = new List<MeshRenderer>();
    private readonly List<MeshRenderer> cauldronRims = new List<MeshRenderer>();
    private struct Shroom { public MeshRenderer[] caps; public int color; }
    private readonly List<Shroom> shrooms = new List<Shroom>();
    private ParticleSystem bubbles;

    // Lab.
    private struct Coil { public Transform root; public MeshRenderer[] body; public MeshRenderer top; public Transform[] bolt, groundL, groundR; public MeshRenderer[] boltRends; }
    private readonly List<Coil> coils = new List<Coil>();
    private const float CoilX = 9f, CoilTop = 6.6f;
    private struct Flask { public Transform liquid; public MeshRenderer liquidR, glassR; public Vector3 baseScale; public int color, phase; }
    private readonly List<Flask> flasks = new List<Flask>();
    private readonly List<MeshRenderer> lamps = new List<MeshRenderer>();
    private ParticleSystem sparks;

    // Castle.
    private struct Window { public Transform root; public MeshRenderer[] panes; public int[] colors; public MeshRenderer[] frame; }
    private readonly List<Window> windows = new List<Window>();
    private struct Candle { public Transform flame; public MeshRenderer flameR; public float seed; }
    private readonly List<Candle> candles = new List<Candle>();
    private ParticleSystem petals, embers;

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
        span = Mathf.Ceil((tunnelLength + Behind + 8f + Chunk) / Chunk) * Chunk;
        if (FindAnyObjectByType<ClubBeat>() == null) gameObject.AddComponent<ClubBeat>();
        mpb = new MaterialPropertyBlock();
        // The theme's sky (Tunnel/GraveyardSky) is an asset; the moon's pulse and the lightning
        // animate a copy so Play never writes into it.
        savedSky = RenderSettings.skybox;
        if (savedSky != null && savedSky.shader.name == "Tunnel/GraveyardSky")
        {
            sky = new Material(savedSky);
            RenderSettings.skybox = sky;
        }
        cube = RemixGeo.Primitive(PrimitiveType.Cube);
        sphere = RemixGeo.Primitive(PrimitiveType.Sphere);

        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutExpo), 1f);
        // A flash: up at once on the beat, gone within half a beat.
        tweens.Loop(DOTween.Sequence().Append(DOTween.To(x => flash = x, 1f, 0f, 0.5f).SetEase(Ease.OutQuad)).AppendInterval(0.5f), 1f);
        tweens.Loop(DOTween.To(x => sway = x, -1f, 1f, 4f).SetEase(Ease.InOutSine).SetLoops(2, LoopType.Yoyo), 8f);
        // The brew: swells on the beat, settles over the rest of it.
        tweens.Loop(DOTween.Sequence().Append(DOTween.To(x => boil = x, 0f, 1f, 0.25f).SetEase(Ease.OutBack))
            .Append(DOTween.To(x => boil = x, 1f, 0f, 0.75f).SetEase(Ease.InOutSine)), 1f);

        BuildEdgeLines(tunnelLength);
        switch (skin)
        {
            case Halloween2Skin.WitchSwamp: BuildTrees(); BuildCauldrons(); BuildMushrooms(); BuildFog(); BuildBats(3, primary); break;
            case Halloween2Skin.MadLab: BuildCoils(); BuildFlasks(); BuildLamps(); BuildSparks(); break;
            case Halloween2Skin.VampireCastle: BuildWindows(); BuildCandelabras(); BuildPetals(); BuildEmbers(); BuildBats(2, primary); break;
        }
        BuildBloomVolume();
    }

    private void OnDestroy()
    {
        tweens.KillAll();
        foreach (var m in mats) if (m != null) Destroy(m);
        if (bloomProfile != null) Destroy(bloomProfile);
        if (sky != null)
        {
            if (RenderSettings.skybox == sky) RenderSettings.skybox = savedSky;
            Destroy(sky);
        }
    }

    private Material Mat(Color c, float level = 1f)
    {
        var m = RemixGeo.Neon(c, emission * level);
        mats.Add(m);
        return m;
    }

    private Transform Scroller(string name, float z, float length = 0f)
    {
        var t = new GameObject(name).transform;
        t.SetParent(transform, false);
        t.localPosition = new Vector3(0f, 0f, z);
        scrollers.Add(new Scrolling { t = t, length = length });
        return t;
    }

    private float Even(float spacing, out int count)
    {
        count = Mathf.Max(1, Mathf.RoundToInt(span / spacing));
        return span / count;
    }

    private float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    private Color Of(int i) => CarnivalLook.Of(i, primary, secondary, thirdColor);

    private void Glow(MeshRenderer r, Color c, float level)
    {
        mpb.SetColor(RemixGeo.EmissionId, c * Mathf.Min(MaxLevel, emission * level));
        r.SetPropertyBlock(mpb);
    }

    // A ring of beams in the XZ plane (around Y) at height y.
    private void Ring(Transform root, Material m, Vector3 centre, float radius, int n, float thickness, List<MeshRenderer> into)
    {
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n, b = (i + 1) * Mathf.PI * 2f / n;
            var r = RemixGeo.Beam(root, "Ring", cube, m, centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius,
                                  centre + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radius, thickness);
            into?.Add(r);
        }
    }

    // A beam laid between two points again - for pieces that move every frame.
    private static void Place(Transform seg, Vector3 a, Vector3 b, float thickness)
    {
        Vector3 edge = b - a;
        float len = edge.magnitude;
        seg.localPosition = (a + b) * 0.5f;
        if (len > 1e-4f) seg.localRotation = Quaternion.FromToRotation(Vector3.up, edge / len);
        seg.localScale = new Vector3(thickness, len + thickness, thickness);
    }

    // --- every skin ---

    private void BuildEdgeLines(float tunnelLength)
    {
        var m = Mat(primary);
        foreach (float x in new[] { -5.2f, 5.2f })
            edgeLines.Add(RemixGeo.Beam(transform, "Edge", cube, m, new Vector3(x, 0.03f, -Behind), new Vector3(x, 0.03f, tunnelLength + 12f), 0.06f));
    }

    private void BuildBats(int count, Color c)
    {
        var body = Mat(Color.Lerp(c, Color.black, 0.2f));
        var eyes = Mat(skin == Halloween2Skin.VampireCastle ? secondary : thirdColor);
        for (int f = 0; f < count; f++)
        {
            var root = new GameObject("BatFlock").transform;
            root.SetParent(transform, false);
            int n = 5 + 2 * (f % 2);
            var bats = new Bat[n];
            var rends = new List<MeshRenderer>();
            for (int b = 0; b < n; b++)
            {
                // A V formation, the leader at the front.
                int rank = (b + 1) / 2, side = b % 2 == 0 ? 1 : -1;
                var bat = new GameObject("Bat").transform;
                bat.SetParent(root, false);
                bat.localPosition = new Vector3(-rank * 1.6f, Rand(-0.4f, 0.4f), side * rank * 1.3f);
                bat.localScale = Vector3.one * Rand(0.8f, 1.15f);
                rends.Add(RemixGeo.Beam(bat, "Body", sphere, body, new Vector3(-0.25f, 0f, 0f), new Vector3(0.25f, 0f, 0f), 0.32f));
                // The eyes keep their own material's glow; only the body and wings pulse.
                RemixGeo.Beam(bat, "Eye", sphere, eyes, new Vector3(0.3f, 0.08f, 0.08f), new Vector3(0.3f, 0.08f, 0.08f), 0.09f);
                RemixGeo.Beam(bat, "Eye", sphere, eyes, new Vector3(0.3f, 0.08f, -0.08f), new Vector3(0.3f, 0.08f, -0.08f), 0.09f);
                Transform Wing(int s)
                {
                    var w = new GameObject("Wing").transform;
                    w.SetParent(bat, false);
                    Vector3 a = new Vector3(0.05f, 0f, s * 0.9f), tip = new Vector3(-0.2f, 0.15f, s * 1.6f);
                    rends.Add(RemixGeo.Beam(w, "Bone", cube, body, Vector3.zero, a, 0.07f));
                    rends.Add(RemixGeo.Beam(w, "Bone", cube, body, a, tip, 0.06f));
                    rends.Add(RemixGeo.Beam(w, "Web", cube, body, tip, new Vector3(-0.35f, -0.1f, s * 0.9f), 0.05f));
                    rends.Add(RemixGeo.Beam(w, "Web", cube, body, new Vector3(-0.35f, -0.1f, s * 0.9f), new Vector3(-0.3f, 0f, s * 0.2f), 0.05f));
                    return w;
                }
                bats[b] = new Bat { left = Wing(1), right = Wing(-1) };
            }
            flocks.Add(new Flock { root = root, bats = bats, rends = rends.ToArray(), offset = f * 8f / count,
                                   z = 30f + f * 18f, y = 12f + f * 1.5f, dir = f % 2 == 0 ? 1 : -1 });
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

    private Material GlowMat()
    {
        if (glowMat != null) return glowMat;
        glowMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "Halloween2Glow" };
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

    // A glowing particle system: world space, nothing emitted until asked, additive soft dots that fade out.
    private ParticleSystem GlowSystem(string name, Vector3 pos, int max, bool trails)
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
            t.lifetime = new ParticleSystem.MinMaxCurve(0.15f);
            t.widthOverTrail = new ParticleSystem.MinMaxCurve(0.08f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        }
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
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

    private static Gradient Gradient3(Color a, Color b, Color c)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(a, 0.33f), new GradientColorKey(b, 0.34f),
                          new GradientColorKey(b, 0.66f), new GradientColorKey(c, 0.67f), new GradientColorKey(c, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        g.mode = GradientMode.Fixed;
        return g;
    }

    // Emits `count` particles of `ps` at a world point (world-space particles stay where they are).
    private static void EmitAt(ParticleSystem ps, Vector3 world, int count)
    {
        ps.transform.position = world;
        ps.Emit(count);
    }

    // --- WitchSwamp ---

    // Crooked bare trees: a bending trunk, three or four branches that fork once, all a little askew.
    private void BuildTrees()
    {
        var bark = Mat(Color.Lerp(primary, Color.black, 0.25f), 0.8f);
        float spacing = Even(13f, out int count);
        for (int k = 0; k < count; k++)
            for (int side = -1; side <= 1; side += 2)
            {
                var root = Scroller("Tree", -Behind + k * spacing + (side > 0 ? spacing * 0.5f : 0f) + Rand(-2f, 2f));
                float x = side * Rand(9.5f, 14f), height = Rand(6f, 9.5f);
                var rends = new List<MeshRenderer>();
                var branches = new List<Transform>();
                Vector3 prev = new Vector3(x, -0.2f, 0f);
                float lean = side * Rand(0.4f, 1.4f);
                const int segs = 5;
                for (int i = 1; i <= segs; i++)
                {
                    float u = i / (float)segs;
                    var q = new Vector3(x + lean * u + Rand(-0.35f, 0.35f), height * u, Rand(-0.4f, 0.4f));
                    rends.Add(RemixGeo.Beam(root, "Trunk", cube, bark, prev, q, 0.42f - 0.26f * u));
                    if (i >= 3)
                    {
                        // A branch from this joint, leaning out and up, crooked, forking once.
                        int nb = i == segs ? 2 : 1;
                        for (int b = 0; b < nb; b++)
                        {
                            var pivot = new GameObject("Branch").transform;
                            pivot.SetParent(root, false);
                            pivot.localPosition = q;
                            branches.Add(pivot);
                            float dirX = (b == 0 ? 1f : -0.6f) * side * Rand(0.6f, 1.2f);
                            Vector3 a = Vector3.zero;
                            float len = Rand(1.6f, 2.8f);
                            for (int s = 1; s <= 3; s++)
                            {
                                var c = a + new Vector3(dirX * len / 3f, Rand(0.15f, 0.6f) * len / 3f, Rand(-0.5f, 0.5f));
                                rends.Add(RemixGeo.Beam(pivot, "Branch", cube, bark, a, c, 0.13f - 0.03f * s));
                                if (s == 2)
                                    rends.Add(RemixGeo.Beam(pivot, "Twig", cube, bark, c, c + new Vector3(dirX * 0.4f, Rand(0.4f, 0.9f), Rand(-0.3f, 0.3f)), 0.05f));
                                a = c;
                            }
                        }
                    }
                    prev = q;
                }
                trees.Add(new Tree { branches = branches.ToArray(), rends = rends.ToArray(), seed = Rand(0f, 6.28f) });
            }
    }

    // Cauldrons at the roadside: an iron pot of rings, three legs, a pool of brew at the rim.
    private void BuildCauldrons()
    {
        var iron = Mat(Color.Lerp(primary, Color.white, 0.15f), 0.7f);
        var brew = Mat(secondary);
        float spacing = Even(19f, out int count);
        for (int k = 0; k < count; k++)
        {
            int side = k % 2 == 0 ? -1 : 1;
            var root = Scroller("Cauldron", -Behind + k * spacing);
            var c = new Vector3(side * 7.4f, 0f, 0f);
            Ring(root, iron, c + Vector3.up * 1.25f, 0.95f, 10, 0.1f, cauldronRims);
            Ring(root, iron, c + Vector3.up * 0.75f, 1.15f, 10, 0.08f, cauldronRims);
            Ring(root, iron, c + Vector3.up * 0.3f, 0.75f, 10, 0.08f, cauldronRims);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                cauldronRims.Add(RemixGeo.Beam(root, "Leg", cube, iron, c + d * 0.6f + Vector3.up * 0.3f, c + d * 0.85f, 0.08f));
            }
            var pool = RemixGeo.Beam(root, "Brew", sphere, brew, c + Vector3.up * 1.2f, c + Vector3.up * 1.2f, 1f);
            pool.transform.localScale = new Vector3(1.75f, 0.2f, 1.75f);
            brews.Add(pool.transform);
            brewRenderers.Add(pool);
        }
        bubbles = GlowSystem("Bubbles", Vector3.zero, 1200, false);
        var main = bubbles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
        main.gravityModifier = -0.15f;
        main.startColor = new ParticleSystem.MinMaxGradient(secondary * 2.2f, Color.Lerp(secondary, thirdColor, 0.5f) * 2f);
        var shape = bubbles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.5f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        RoadVelocity(bubbles, 1f);
        bubbles.Play();
    }

    // Glowing mushrooms: clusters at the roadside, a stem and a flattened cap each.
    private void BuildMushrooms()
    {
        var stem = Mat(Color.Lerp(thirdColor, Color.white, 0.3f), 0.35f);
        var capMats = new[] { Mat(thirdColor), Mat(secondary), Mat(primary) };
        float spacing = Even(7f, out int count);
        for (int k = 0; k < count; k++)
            for (int side = -1; side <= 1; side += 2)
            {
                var root = Scroller("Mushrooms", -Behind + k * spacing + (side > 0 ? spacing * 0.5f : 0f));
                int color = (k + (side > 0 ? 1 : 0)) % 3;
                int n = 2 + rng.Next(3);
                var caps = new MeshRenderer[n];
                for (int i = 0; i < n; i++)
                {
                    var b = new Vector3(side * Rand(6.2f, 8.2f), 0f, Rand(-1.2f, 1.2f));
                    float h = Rand(0.25f, 0.85f), w = Rand(0.35f, 0.75f);
                    RemixGeo.Beam(root, "Stem", cube, stem, b, b + Vector3.up * h, 0.08f + 0.06f * w);
                    caps[i] = RemixGeo.Beam(root, "Cap", sphere, capMats[color], b + Vector3.up * h, b + Vector3.up * h, 1f);
                    caps[i].transform.localScale = new Vector3(w, w * 0.42f, w);
                }
                shrooms.Add(new Shroom { caps = caps, color = color });
            }
    }

    // Swamp fog: big dim soft blobs drifting low beyond the road on both sides.
    private void BuildFog()
    {
        for (int side = -1; side <= 1; side += 2)
        {
            var fog = GlowSystem("SwampFog", new Vector3(side * 13f, 0.6f, 40f), 300, false);
            var main = fog.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(3.5f, 7f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(primary, thirdColor, 0.3f) * 0.16f, secondary * 0.1f);
            var shape = fog.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(13f, 0.8f, 100f);
            var noise = fog.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.2f;
            RoadVelocity(fog, 1f);
            var em = fog.emission;
            em.rateOverTime = 28f;
            fog.Play();
        }
    }

    // --- MadLab ---

    // Tesla coils in pairs across the road: a post, a copper helix round it, a ring and a ball on top;
    // a bolt across the road between the two tops, and one from each top down to the ground outside.
    private void BuildCoils()
    {
        var post = Mat(Color.Lerp(secondary, Color.black, 0.35f), 0.6f);
        var helix = Mat(primary);
        var ball = Mat(Color.Lerp(secondary, Color.white, 0.4f));
        var boltMat = Mat(Color.Lerp(secondary, Color.white, 0.5f));
        float spacing = Even(30f, out int count);
        for (int k = 0; k < count; k++)
        {
            var root = Scroller("CoilPair", -Behind + k * spacing);
            var body = new List<MeshRenderer>();
            MeshRenderer top = null;
            for (int side = -1; side <= 1; side += 2)
            {
                var b = new Vector3(side * CoilX, 0f, 0f);
                body.Add(RemixGeo.Beam(root, "Post", cube, post, b, b + Vector3.up * (CoilTop - 0.4f), 0.4f));
                const int turns = 7, per = 8;
                Vector3 prev = b + new Vector3(0.6f, 0.8f, 0f);
                for (int i = 1; i <= turns * per; i++)
                {
                    float a = i * Mathf.PI * 2f / per;
                    var p = b + new Vector3(Mathf.Cos(a) * 0.6f, 0.8f + i * (CoilTop - 2f) / (turns * per), Mathf.Sin(a) * 0.6f);
                    body.Add(RemixGeo.Beam(root, "Coil", cube, helix, prev, p, 0.07f));
                    prev = p;
                }
                Ring(root, helix, b + Vector3.up * (CoilTop - 0.6f), 0.9f, 12, 0.12f, body);
                top = RemixGeo.Beam(root, "Ball", sphere, ball, b + Vector3.up * CoilTop, b + Vector3.up * CoilTop, 0.8f);
                body.Add(top);
            }
            Transform[] Bolt(string name, int n, List<MeshRenderer> rends)
            {
                var segs = new Transform[n];
                for (int i = 0; i < n; i++)
                {
                    var r = RemixGeo.Beam(root, name, cube, boltMat, Vector3.zero, Vector3.up, 0.09f);
                    r.enabled = false;
                    rends.Add(r);
                    segs[i] = r.transform;
                }
                return segs;
            }
            var boltRends = new List<MeshRenderer>();
            var bolt = Bolt("Bolt", 14, boltRends);
            var gl = Bolt("GroundBolt", 6, boltRends);
            var gr = Bolt("GroundBolt", 6, boltRends);
            coils.Add(new Coil { root = root, body = body.ToArray(), top = top, bolt = bolt, groundL = gl, groundR = gr, boltRends = boltRends.ToArray() });
        }
    }

    // A fresh jagged path for a bolt from a to b, bowed up by `bow` in the middle.
    private void Zap(Transform[] segs, Vector3 a, Vector3 b, float bow, float jitter)
    {
        int n = segs.Length;
        Vector3 prev = a;
        for (int i = 1; i <= n; i++)
        {
            float u = i / (float)n;
            var p = Vector3.Lerp(a, b, u) + Vector3.up * (bow * Mathf.Sin(u * Mathf.PI));
            if (i < n) p += new Vector3(Rand(-jitter, jitter), Rand(-jitter, jitter), Rand(-jitter, jitter));
            Place(segs[i - 1], prev, p, 0.09f);
            prev = p;
        }
    }

    // Tables of flasks at the roadside: a round-bottomed glass, a neck, the liquid glowing inside.
    private void BuildFlasks()
    {
        var wood = Mat(Color.Lerp(secondary, Color.black, 0.5f), 0.4f);
        var glass = Mat(Color.Lerp(secondary, Color.white, 0.6f), 0.35f);
        var liquids = new[] { Mat(primary), Mat(secondary), Mat(thirdColor) };
        float spacing = Even(11f, out int count);
        for (int k = 0; k < count; k++)
        {
            int side = k % 2 == 0 ? 1 : -1;
            var root = Scroller("FlaskTable", -Behind + k * spacing);
            float x = side * 7.6f;
            RemixGeo.Beam(root, "Table", cube, wood, new Vector3(x, 1f, -1.5f), new Vector3(x, 1f, 1.5f), 0.12f).transform.localScale = new Vector3(1.3f, 3.1f, 0.12f);
            foreach (float lz in new[] { -1.3f, 1.3f })
                foreach (float lx in new[] { -0.5f, 0.5f })
                    RemixGeo.Beam(root, "Leg", cube, wood, new Vector3(x + lx, 0f, lz), new Vector3(x + lx, 0.95f, lz), 0.07f);
            int n = 3;
            for (int i = 0; i < n; i++)
            {
                float z = -1f + i * 1f, s = Rand(0.55f, 0.8f);
                var c = new Vector3(x + Rand(-0.2f, 0.2f), 1.06f + s * 0.5f, z);
                var glassR = RemixGeo.Beam(root, "Glass", sphere, glass, c, c, s);
                RemixGeo.Beam(root, "Neck", cube, glass, c + Vector3.up * s * 0.4f, c + Vector3.up * (s * 0.4f + Rand(0.4f, 0.8f)), 0.12f);
                int color = (k + i) % 3;
                var liquid = RemixGeo.Beam(root, "Liquid", sphere, liquids[color], c + Vector3.down * s * 0.08f, c + Vector3.down * s * 0.08f, s * 0.78f);
                flasks.Add(new Flask { liquid = liquid.transform, liquidR = liquid, glassR = glassR, baseScale = liquid.transform.localScale, color = color, phase = (k * 3 + i) % 4 });
            }
        }
        bubbles = GlowSystem("FlaskBubbles", Vector3.zero, 1000, false);
        var main = bubbles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.gravityModifier = -0.1f;
        main.startColor = new ParticleSystem.MinMaxGradient(Gradient3(primary * 2f, secondary * 2f, thirdColor * 2f)) { mode = ParticleSystemGradientMode.RandomColor };
        var shape = bubbles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.1f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        RoadVelocity(bubbles, 1f);
        bubbles.Play();
    }

    // Gantries of hanging lamps high over the road.
    private void BuildLamps()
    {
        var steel = Mat(Color.Lerp(secondary, Color.black, 0.45f), 0.5f);
        var bulb = Mat(Color.Lerp(primary, Color.white, 0.35f));
        float spacing = Even(20f, out int count);
        for (int k = 0; k < count; k++)
        {
            var root = Scroller("LampGantry", -Behind + k * spacing + 6f);
            const float top = 11.2f, half = 7f;
            RemixGeo.Beam(root, "Post", cube, steel, new Vector3(-half, 0f, 0f), new Vector3(-half, top, 0f), 0.18f);
            RemixGeo.Beam(root, "Post", cube, steel, new Vector3(half, 0f, 0f), new Vector3(half, top, 0f), 0.18f);
            RemixGeo.Beam(root, "Beam", cube, steel, new Vector3(-half, top, 0f), new Vector3(half, top, 0f), 0.16f);
            for (int i = 0; i < 6; i++)
            {
                float x = Mathf.Lerp(-5.5f, 5.5f, i / 5f);
                float drop = Rand(1.2f, 1.8f);
                RemixGeo.Beam(root, "Wire", cube, steel, new Vector3(x, top, 0f), new Vector3(x, top - drop, 0f), 0.03f);
                RemixGeo.Beam(root, "Shade", cube, steel, new Vector3(x - 0.3f, top - drop, 0f), new Vector3(x + 0.3f, top - drop, 0f), 0.08f);
                lamps.Add(RemixGeo.Beam(root, "Lamp", sphere, bulb, new Vector3(x, top - drop - 0.25f, 0f), new Vector3(x, top - drop - 0.25f, 0f), 0.38f));
            }
        }
    }

    private void BuildSparks()
    {
        sparks = GlowSystem("Sparks", Vector3.zero, 2000, true);
        var main = sparks.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
        main.gravityModifier = 1.2f;
        main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(secondary, Color.white, 0.6f) * 2.5f, thirdColor * 2.2f);
        var shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.4f;
        RoadVelocity(sparks, 1f);
        sparks.Play();
    }

    // --- VampireCastle ---

    // A colonnade of gothic pointed windows (equilateral arches) down both sides, each a frame of
    // gold mullions round panes of stained glass, a rose at the point.
    private void BuildWindows()
    {
        var gold = Mat(secondary, 0.8f);
        var stone = Mat(Color.Lerp(primary, Color.black, 0.75f), 0.25f);
        var glass = Mat(primary);
        float spacing = Even(9f, out int count);
        const float w = 3.4f, jamb = 5.2f;
        float apex = jamb + w * Mathf.Sin(60f * Mathf.Deg2Rad);
        for (int k = 0; k < count; k++)
            for (int side = -1; side <= 1; side += 2)
            {
                var root = Scroller("GothicWindow", -Behind + k * spacing + (side > 0 ? spacing * 0.5f : 0f), w);
                float x = side * 10.5f;
                Vector3 P(float z, float y) => new Vector3(x, y, z);
                var frame = new List<MeshRenderer>();
                // Pillars between the windows, and the jambs.
                RemixGeo.Beam(root, "Pillar", cube, stone, P(-0.5f, 0f), P(-0.5f, apex + 1.5f), 0.7f);
                frame.Add(RemixGeo.Beam(root, "Jamb", cube, gold, P(0f, 0.6f), P(0f, jamb), 0.12f));
                frame.Add(RemixGeo.Beam(root, "Jamb", cube, gold, P(w, 0.6f), P(w, jamb), 0.12f));
                frame.Add(RemixGeo.Beam(root, "Sill", cube, gold, P(0f, 0.6f), P(w, 0.6f), 0.12f));
                // The pointed head: two arcs of radius w, each centred on the opposite jamb's top.
                const int steps = 6;
                Vector3 l = P(0f, jamb), r = P(w, jamb);
                for (int i = 1; i <= steps; i++)
                {
                    float a = Mathf.Deg2Rad * (180f - 60f * i / steps);
                    var nl = P(w + w * Mathf.Cos(a), jamb + w * Mathf.Sin(a));
                    float b = Mathf.Deg2Rad * (60f * i / steps);
                    var nr = P(w * Mathf.Cos(b), jamb + w * Mathf.Sin(b));
                    frame.Add(RemixGeo.Beam(root, "Arch", cube, gold, l, nl, 0.12f));
                    frame.Add(RemixGeo.Beam(root, "Arch", cube, gold, r, nr, 0.12f));
                    l = nl; r = nr;
                }
                // The middle mullion and a transom.
                frame.Add(RemixGeo.Beam(root, "Mullion", cube, gold, P(w * 0.5f, 0.6f), P(w * 0.5f, jamb + 0.9f), 0.08f));
                frame.Add(RemixGeo.Beam(root, "Transom", cube, gold, P(0f, 3f), P(w, 3f), 0.08f));
                // Panes: two lights of four panes each, and the rose.
                var panes = new List<MeshRenderer>();
                var colors = new List<int>();
                for (int col = 0; col < 2; col++)
                    for (int row = 0; row < 4; row++)
                    {
                        float z0 = col * w * 0.5f + 0.12f, z1 = (col + 1) * w * 0.5f - 0.12f;
                        float y0 = 0.7f + row * (jamb - 0.6f) / 4f + 0.06f, y1 = 0.7f + (row + 1) * (jamb - 0.6f) / 4f - 0.08f;
                        var pane = RemixGeo.Beam(root, "Glass", cube, glass, P((z0 + z1) * 0.5f, y0), P((z0 + z1) * 0.5f, y1), 0.05f);
                        pane.transform.localScale = new Vector3(0.05f, y1 - y0, z1 - z0);
                        pane.transform.localRotation = Quaternion.identity;
                        panes.Add(pane);
                        colors.Add((row + col * 2 + k) % 3);
                    }
                var rose = RemixGeo.Beam(root, "Rose", sphere, glass, P(w * 0.5f, jamb + 1.6f), P(w * 0.5f, jamb + 1.6f), 1f);
                rose.transform.localScale = new Vector3(0.1f, 1.3f, 1.3f);
                panes.Add(rose);
                colors.Add(1);
                windows.Add(new Window { root = root, panes = panes.ToArray(), colors = colors.ToArray(), frame = frame.ToArray() });
            }
    }

    // Candelabras: a gold stand with five arms, a candle and a flame on each.
    private void BuildCandelabras()
    {
        var gold = Mat(secondary, 0.7f);
        var wax = Mat(new Color(1f, 0.92f, 0.8f), 0.45f);
        var fire = Mat(Color.Lerp(secondary, new Color(1f, 0.5f, 0.1f), 0.5f));
        float spacing = Even(12f, out int count);
        for (int k = 0; k < count; k++)
            for (int side = -1; side <= 1; side += 2)
            {
                var root = Scroller("Candelabra", -Behind + k * spacing + (side > 0 ? spacing * 0.5f : 0f) + 3f);
                var b = new Vector3(side * 7f, 0f, 0f);
                for (int f = 0; f < 3; f++)
                {
                    float a = f * Mathf.PI * 2f / 3f;
                    RemixGeo.Beam(root, "Foot", cube, gold, b + Vector3.up * 0.4f, b + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.5f, 0.07f);
                }
                RemixGeo.Beam(root, "Stem", cube, gold, b, b + Vector3.up * 2.3f, 0.1f);
                for (int arm = -2; arm <= 2; arm++)
                {
                    Vector3 tip = b + new Vector3(0f, 2.3f + (arm == 0 ? 0.5f : 0.15f * (2 - Mathf.Abs(arm))), arm * 0.42f);
                    if (arm != 0)
                    {
                        var elbow = b + new Vector3(0f, 1.9f, arm * 0.42f);
                        RemixGeo.Beam(root, "Arm", cube, gold, b + Vector3.up * 1.9f, elbow, 0.06f);
                        RemixGeo.Beam(root, "Arm", cube, gold, elbow, tip, 0.06f);
                    }
                    RemixGeo.Beam(root, "Candle", cube, wax, tip, tip + Vector3.up * 0.4f, 0.11f);
                    var flame = RemixGeo.Beam(root, "Flame", sphere, fire, tip + Vector3.up * 0.55f, tip + Vector3.up * 0.55f, 0.16f);
                    flame.transform.localScale = new Vector3(0.12f, 0.24f, 0.12f);
                    candles.Add(new Candle { flame = flame.transform, flameR = flame, seed = Rand(0f, 100f) });
                }
            }
    }

    // Rose petals: a storm on every drop, a light fall while the drop lasts.
    private void BuildPetals()
    {
        var go = new GameObject("RosePetals");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 12f, 25f);
        petals = go.AddComponent<ParticleSystem>();
        petals.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = petals.main;
        main.duration = 1f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.3f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0.22f;
        main.maxParticles = 1500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(Gradient3(primary * 1.6f, Color.Lerp(primary, Color.black, 0.3f) * 1.4f, thirdColor * 1.5f)) { mode = ParticleSystemGradientMode.RandomColor };
        var shape = petals.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(22f, 1f, 34f);
        var rot = petals.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-4f, 4f);
        rot.y = new ParticleSystem.MinMaxCurve(-4f, 4f);
        rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
        var noise = petals.noise;
        noise.enabled = true;
        noise.strength = 1.4f;
        noise.frequency = 0.35f;
        RoadVelocity(petals, 1f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "Petal" };
        m.SetColor("_BaseColor", Color.white);
        mats.Add(m);
        r.sharedMaterial = m;
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = RemixGeo.Primitive(PrimitiveType.Quad);
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        petals.Play();
    }

    private void BuildEmbers()
    {
        embers = GlowSystem("GoldSparks", Vector3.zero, 1500, true);
        var main = embers.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.13f);
        main.gravityModifier = -0.2f;
        main.startColor = new ParticleSystem.MinMaxGradient(secondary * 2.4f, new Color(1f, 0.55f, 0.15f) * 2.2f);
        var shape = embers.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;
        RoadVelocity(embers, 1f);
        embers.Play();
    }

    // --- every frame ---

    private void Update()
    {
        if (!built) return;
        float delta = scrollSpeed * Time.deltaTime;
        float beats = CarnivalLook.Beats;
        tweens.SeekLoops(beats);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        bool newBeat = ClubBeat.BeatIndex != lastBeat;
        lastBeat = ClubBeat.BeatIndex;

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
            case Halloween2Skin.WitchSwamp: UpdateSwamp(energy, newBeat); UpdateBats(beats, energy); break;
            case Halloween2Skin.MadLab: UpdateLab(energy, newBeat); break;
            case Halloween2Skin.VampireCastle: UpdateCastle(energy, newBeat); UpdateBats(beats, energy); break;
        }

        if (sky != null)
        {
            // The moon breathes on the bar and thumps on the kick; the lab's sky flashes with its lightning.
            float barPulse = Mathf.Pow(1f - CarnivalLook.BarPosition, 2f);
            if (skin == Halloween2Skin.MadLab)
                sky.SetFloat(FlashId, (CarnivalLook.InDrop || CarnivalLook.StrongBeat) ? flash * 0.45f : 0f);
            else
                sky.SetFloat(MoonPulseId, Mathf.Clamp01(0.25f * barPulse + 0.55f * kick * Mathf.Min(energy, 1.2f)));
        }

        if (bloomVolume != null)
        {
            float drop = Mathf.Clamp01((ClubBeat.Energy - 1f) / 0.6f);
            bloomVolume.weight = dropBloom * drop * (0.55f + 0.45f * kick);
            // The lab's lightning swells the bloom on the strong beats of a drop.
            if (skin == Halloween2Skin.MadLab && CarnivalLook.InDrop && CarnivalLook.StrongBeat)
                bloomVolume.weight = Mathf.Max(bloomVolume.weight, 0.75f * flash);
        }
    }

    private void UpdateBats(float beats, float energy)
    {
        // Each flock crosses the sky in eight beats, wings beating down on every beat (twice a beat in a drop).
        float beatPhase = CarnivalLook.InDrop ? Mathf.Repeat(beats * 2f, 1f) : ClubBeat.BeatPhase;
        float wing = Mathf.Lerp(45f, -40f, Mathf.Pow(1f - beatPhase, 2f));     // the down stroke on the beat
        foreach (var f in flocks)
        {
            float u = Mathf.Repeat(beats + f.offset, 8f) / 8f;
            float x = Mathf.Lerp(-36f, 36f, u) * f.dir;
            f.root.localPosition = new Vector3(x, f.y + Mathf.Sin(u * Mathf.PI) * 3f, f.z + Mathf.Sin(u * Mathf.PI * 2f) * 4f);
            f.root.localRotation = Quaternion.Euler(0f, f.dir > 0 ? 0f : 180f, Mathf.Sin(u * Mathf.PI * 4f) * 6f);
            foreach (var b in f.bats)
            {
                b.left.localRotation = Quaternion.Euler(-wing, 0f, 0f);
                b.right.localRotation = Quaternion.Euler(wing, 0f, 0f);
            }
            float level = 0.35f + 0.4f * kick * Mathf.Min(energy, 1.3f);
            foreach (var r in f.rends) Glow(r, primary, level);
        }
    }

    private void UpdateSwamp(float energy, bool newBeat)
    {
        // Trees: the branches sway over eight beats and breathe on the bar.
        float bar = Mathf.Pow(1f - CarnivalLook.BarPosition, 2f);
        for (int t = 0; t < trees.Count; t++)
        {
            var tree = trees[t];
            for (int b = 0; b < tree.branches.Length; b++)
                tree.branches[b].localRotation = Quaternion.Euler(sway * 4f * Mathf.Sin(tree.seed + b), 0f, sway * 6f * Mathf.Cos(tree.seed + b * 1.3f));
            float level = 0.25f + 0.35f * bar * Mathf.Min(energy, 1.3f);
            foreach (var r in tree.rends) Glow(r, primary, level);
        }
        // Cauldrons: the brew swells and flares on the beat.
        for (int i = 0; i < brews.Count; i++)
        {
            float s = 1f + 0.35f * boil * Mathf.Min(energy, 1.4f);
            brews[i].localScale = new Vector3(1.75f, 0.2f * s * s, 1.75f);
            Glow(brewRenderers[i], secondary, 0.6f + 1.1f * kick * Mathf.Min(energy, 1.4f));
        }
        float rim = 0.35f + 0.3f * kick * energy;
        foreach (var r in cauldronRims) Glow(r, Color.Lerp(primary, Color.white, 0.15f), rim);
        // Bubbles: half the cauldrons in turn on every beat, all of them in a drop.
        if (newBeat && bubbles != null)
            for (int i = 0; i < brews.Count; i++)
                if (CarnivalLook.InDrop || (i + ClubBeat.BeatIndex) % 2 == 0)
                    EmitAt(bubbles, brews[i].position, CarnivalLook.InDrop ? 14 : 8);
        // Mushrooms: odd and even clusters take the beats in turn.
        bool odd = (ClubBeat.BeatIndex & 1) == 1;
        for (int i = 0; i < shrooms.Count; i++)
        {
            bool mine = ((i & 1) == 1) == odd;
            float level = mine ? 0.45f + 1.2f * kick * Mathf.Min(energy, 1.3f) : 0.22f;
            foreach (var c in shrooms[i].caps) Glow(c, Of(shrooms[i].color + 2), level);
        }
    }

    private void UpdateLab(float energy, bool newBeat)
    {
        bool drop = CarnivalLook.InDrop;
        bool strike = drop || CarnivalLook.StrongBeat;
        float bolt = strike ? flash : 0f;
        foreach (var c in coils)
        {
            if (newBeat && strike)
            {
                Zap(c.bolt, new Vector3(-CoilX, CoilTop, 0f), new Vector3(CoilX, CoilTop, 0f), 4.5f, 0.55f);
                Zap(c.groundL, new Vector3(-CoilX, CoilTop, 0f), new Vector3(-CoilX - Rand(3f, 5f), 0f, Rand(-2f, 2f)), 0.5f, 0.4f);
                Zap(c.groundR, new Vector3(CoilX, CoilTop, 0f), new Vector3(CoilX + Rand(3f, 5f), 0f, Rand(-2f, 2f)), 0.5f, 0.4f);
                if (sparks != null)
                    for (int side = -1; side <= 1; side += 2)
                        EmitAt(sparks, c.root.TransformPoint(new Vector3(side * CoilX, CoilTop, 0f)), drop ? 30 : 16);
            }
            bool on = bolt > 0.08f;
            foreach (var r in c.boltRends)
            {
                r.enabled = on;
                if (on) Glow(r, Color.Lerp(secondary, Color.white, 0.5f), 0.8f + 1.6f * bolt);
            }
            float level = 0.4f + 0.35f * kick * energy;
            foreach (var r in c.body) Glow(r, primary, level);
            Glow(c.top, Color.Lerp(secondary, Color.white, 0.4f), 0.6f + 1.4f * bolt);
        }
        // Flasks: the liquid boils in four groups, one group a beat.
        int group = ClubBeat.BeatIndex & 3;
        for (int i = 0; i < flasks.Count; i++)
        {
            var f = flasks[i];
            bool mine = f.phase == group || drop;
            float b = mine ? boil : 0f;
            f.liquid.localScale = f.baseScale * (1f + 0.12f * b);
            Glow(f.liquidR, Of(f.color), 0.45f + 1.2f * b * Mathf.Min(energy, 1.3f));
            Glow(f.glassR, Color.Lerp(secondary, Color.white, 0.6f), 0.15f + 0.2f * b);
            if (newBeat && mine && bubbles != null) EmitAt(bubbles, f.liquid.position + Vector3.up * 0.6f, drop ? 6 : 4);
        }
        // Lamps: a pattern that changes every beat, a flicker off just after it; all flash in a drop.
        for (int i = 0; i < lamps.Count; i++)
        {
            int h = (i * 73856093) ^ (ClubBeat.BeatIndex * 19349663);
            bool lit = drop || ((h >> 3) & 3) != 0;
            bool flicker = !drop && ((h >> 7) & 7) == 0 && ClubBeat.BeatPhase > 0.45f && ClubBeat.BeatPhase < 0.6f;
            float level = lit && !flicker ? 0.35f + 1.2f * kick * Mathf.Min(energy, 1.3f) + (drop ? 0.5f * flash : 0f) : 0.05f;
            Glow(lamps[i], i % 3 == 0 ? thirdColor : Color.Lerp(primary, Color.white, 0.35f), level);
        }
    }

    private void UpdateCastle(float energy, bool newBeat)
    {
        bool drop = CarnivalLook.InDrop;
        float baseZ = transform.position.z;
        // The glass: dim, a wave of light running down the colonnade every bar, a lift on the kick.
        foreach (var w in windows)
        {
            float z = w.root.localPosition.z + baseZ;
            float level = 0.3f + 0.3f * kick * energy + 1.4f * CarnivalLook.Wave(z) + (drop ? 0.4f * flash : 0f);
            for (int i = 0; i < w.panes.Length; i++) Glow(w.panes[i], Of(w.colors[i]), level);
            foreach (var r in w.frame) Glow(r, secondary, 0.35f + 0.35f * kick * energy);
        }
        // Candles: a flicker of their own, a flare on the kick.
        float t = ClubBeat.SongTime;
        for (int i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            float flick = Mathf.PerlinNoise(c.seed, t * 6f);
            float s = 0.85f + 0.35f * flick + 0.45f * kick * Mathf.Min(energy, 1.3f);
            c.flame.localScale = new Vector3(0.12f, 0.24f * s, 0.12f) * (0.9f + 0.2f * flick);
            Glow(c.flameR, Color.Lerp(secondary, new Color(1f, 0.5f, 0.1f), 0.5f), 0.7f + 0.6f * flick + 0.9f * kick * energy);
        }
        if (embers != null && newBeat && drop && CarnivalLook.StrongBeat)
            for (int k = 0; k < 6 && candles.Count > 0; k++)
                EmitAt(embers, candles[rng.Next(candles.Count)].flame.position, 12);
        if (petals == null) return;
        float since = ClubBeat.BeatsSinceDrop;
        if (since < lastBeatsSinceDrop && since < 1f) petals.Emit(650);
        lastBeatsSinceDrop = since;
        var em = petals.emission;
        em.rateOverTime = drop ? 45f * Mathf.Min(energy, 1.5f) : 0f;
    }
}
