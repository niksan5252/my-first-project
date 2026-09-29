using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_HalloweenGraveyard's environment, "Graveyard under the moon": the road runs through a
/// cemetery at night, under a huge full moon (the theme's sky, Tunnel/GraveyardSky).
///
///   * the cemetery either side, travelling with the road: rows of gravestones and crosses, crypts
///     and obelisks further back, crooked dead trees beyond, an iron fence along the road's edge,
///     street lamps and lanterns, clusters of candles on the graves, glowing jack-o'-lanterns,
///     zombie hands, bones and poison-green mushrooms - all from the Kenney Graveyard Kit, the
///     Cemetery Snap pack and the Poly Halloween pack, their stone and wood cooled to moonlight;
///   * ground fog lying low among the graves (Kenney smoke) in moonlit blue;
///   * bats wheeling across the sky in front of the moon;
///   * light that comes from the scene: each lamp has a real point light and a flare, candles and
///     pumpkins their own flames and glow, the kits' emissive parts (lanterns, green tree
///     fungus, candle wax) theirs - the directional moonlight stays the theme's low one.
///
/// In time with the music (SongTweens: DOTween paused, durations in beats, positioned from
/// ClubBeat's song time) and scaled by ClubBeat.Energy: lamps, candles, pumpkins and every
/// emissive material kick on the beat; pumpkins, mushrooms and candle flames swell with it; the
/// fog flares on the beat through a drop and breathes over each bar otherwise; the moon's halo
/// pulses with the kick; bats scatter faster on a drop. Everything is capped below white.
public class GraveyardEnvironmentDecorator : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    private const float MaxGlow = 2.2f;

    [Header("Shaders and textures")]
    [SerializeField] private Shader glowShader;
    [SerializeField] private Texture2D flareMask;
    [SerializeField] private Texture2D flameTexture;
    [SerializeField] private Texture2D smokeTexture;
    [SerializeField] private Texture2D coneMask;

    [Header("Models (Kenney Graveyard Kit, Cemetery Snap, Poly Halloween)")]
    [SerializeField] private GameObject[] gravestones;
    [SerializeField] private GameObject[] crypts;
    [SerializeField] private GameObject[] trees;
    [SerializeField] private GameObject[] fences;
    [SerializeField] private GameObject[] ironFences;
    [SerializeField] private GameObject[] lamps;
    [SerializeField] private GameObject[] candles;
    [SerializeField] private GameObject[] pumpkins;
    [SerializeField] private GameObject[] details;

    [Header("Colours")]
    [SerializeField] private Color moonBlue = new Color(0.55f, 0.72f, 1f);
    [SerializeField] private Color flameOrange = new Color(1f, 0.5f, 0.1f);
    [SerializeField] private Color poisonGreen = new Color(0.42f, 1f, 0.22f);
    // The kits' daylight colours multiplied down to a moonlit night.
    [SerializeField] private Color stoneTint = new Color(0.5f, 0.55f, 0.68f);

    [Header("Layout")]
    [SerializeField] private float segmentSpacing = 8f;
    [SerializeField] private float behindCamera = 16f;
    [SerializeField] private float fenceX = 5.3f;
    [SerializeField] private Vector2 graveX = new Vector2(6.3f, 15f);
    [SerializeField] private Vector2 cryptX = new Vector2(12f, 19f);
    [SerializeField] private Vector2 treeX = new Vector2(15f, 30f);
    [SerializeField] private float lampX = 5.9f;
    [SerializeField] private float propScale = 1.9f;

    [Header("Lights")]
    [SerializeField] private float lampLightRange = 11f;
    [SerializeField] private float lampLightIntensity = 5f;

    [Header("Fog and bats")]
    [SerializeField] private int fogPerSegment = 3;
    [SerializeField] private int batCount = 34;

    private Color primary = new Color(0.42f, 1f, 0.22f);
    private Color secondary = new Color(1f, 0.5f, 0.08f);
    private float emission = 1.2f;

    private bool built;
    private float scrollSpeed, tunnelLength, span;
    private Camera cam;
    private Material glowTemplate, groundMat, batMat, sky, savedSky;
    private Texture2D batTex;
    private MaterialPropertyBlock mpb;
    private System.Random rng = new System.Random(31);
    private readonly SongTweens tweens = new SongTweens();

    // Written by the tweens.
    private float kick;        // 1 on the beat, falling over it
    private float eighth;      // flicker on the eighths
    private float bar;         // 0..1..0 over a bar

    private readonly List<Transform> segments = new List<Transform>();
    private struct Glow { public MeshRenderer r; public Color color; public float baseI, kickI; public bool face; }
    private readonly List<Glow> glows = new List<Glow>();
    private struct Swell { public Transform t; public Vector3 rest; public float amount; }
    private readonly List<Swell> swells = new List<Swell>();
    private struct LampLight { public Light light; }
    private readonly List<LampLight> lampLights = new List<LampLight>();
    private struct Fog { public Transform t; public MeshRenderer r; public float seed, y; }
    private readonly List<Fog> fogs = new List<Fog>();
    private class Bat { public Transform t; public float angle, radius, height, speed, flap, size; public Vector3 center; }
    private readonly List<Bat> bats = new List<Bat>();
    // Every material the props use, re-made once for the night (and for URP, where a pack ships
    // Built-in Standard ones): originals -> night copies, and the emissive ones' base emission.
    private readonly Dictionary<Material, Material> night = new Dictionary<Material, Material>();
    private readonly List<(Material m, Color e)> emissives = new List<(Material, Color)>();

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int MoonPulseId = Shader.PropertyToID("_MoonPulse");

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
        mpb = new MaterialPropertyBlock();
        cam = Camera.main;
        glowTemplate = new Material(glowShader != null ? glowShader : Shader.Find("Tunnel/ClubGlow"));

        // The theme's skybox is an asset; animate a copy so Play never writes into it.
        savedSky = RenderSettings.skybox;
        if (savedSky != null && savedSky.shader.name == "Tunnel/GraveyardSky")
        {
            sky = new Material(savedSky);
            RenderSettings.skybox = sky;
        }

        // The cemetery's soil, beyond the road on both sides.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        Destroy(ground.GetComponent<Collider>());
        ground.name = "Soil";
        ground.transform.SetParent(transform, false);
        ground.transform.localPosition = new Vector3(0f, -0.04f, tunnelLength * 0.5f);
        ground.transform.localScale = new Vector3(9f, 1f, (tunnelLength + 120f) / 10f);
        groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        groundMat.SetColor("_BaseColor", new Color(0.03f, 0.04f, 0.05f));
        groundMat.SetFloat("_Smoothness", 0.15f);
        var gr = ground.GetComponent<MeshRenderer>();
        gr.sharedMaterial = groundMat;
        gr.shadowCastingMode = ShadowCastingMode.Off;

        var stage = new GameObject("GraveyardStage").transform;
        stage.SetParent(transform, false);
        int count = Mathf.CeilToInt((tunnelLength + behindCamera + segmentSpacing * 2f) / segmentSpacing);
        span = count * segmentSpacing;
        for (int i = 0; i < count; i++) BuildSegment(stage, i, -behindCamera + i * segmentSpacing);

        batTex = BatTexture();
        batMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        batMat.SetTexture("_BaseMap", batTex);
        batMat.SetColor("_BaseColor", new Color(0.01f, 0.01f, 0.015f, 1f));
        batMat.SetFloat("_Surface", 1f);
        batMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        batMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        batMat.SetFloat("_ZWrite", 0f);
        batMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        batMat.renderQueue = (int)RenderQueue.Transparent;
        for (int i = 0; i < batCount; i++) bats.Add(NewBat(i));

        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutCubic), 1f);
        tweens.Loop(DOTween.To(x => eighth = x, 1f, 0.4f, 0.5f).SetEase(Ease.OutQuad), 0.5f);
        tweens.Loop(DOTween.To(x => bar = x, 0f, 1f, 2f).SetEase(Ease.InOutSine).SetLoops(2, LoopType.Yoyo), 4f);
    }

    // --- building ---

    private void BuildSegment(Transform stage, int index, float z)
    {
        var seg = new GameObject("Segment_" + index.ToString("00")).transform;
        seg.SetParent(stage, false);
        seg.localPosition = new Vector3(0f, 0f, z);
        segments.Add(seg);

        for (int s = -1; s <= 1; s += 2)
        {
            // The iron fence along the road's edge, 1 m panels scaled up.
            var fence = Pick(ironFences);
            if (fence != null)
                for (float f = -segmentSpacing * 0.5f; f < segmentSpacing * 0.5f - 0.1f; f += 1.5f)
                    Prop(seg, fence, new Vector3(s * fenceX, 0f, f + 0.75f), s > 0 ? -90f : 90f, 1.5f);

            // Gravestones and crosses in loose rows.
            int graves = 3 + rng.Next(3);
            for (int g = 0; g < graves; g++)
            {
                float x = s * Mathf.Lerp(graveX.x, graveX.y, (g + (float)rng.NextDouble() * 0.7f) / graves);
                var p = Prop(seg, Pick(gravestones), new Vector3(x, 0f, Rand(-3.5f, 3.5f)), Rand(-12f, 12f) + (s > 0 ? -90f : 90f) * 0f, propScale * Rand(0.85f, 1.25f));
                // Candles on some graves: the flame is a glow sprite that flickers on the eighths.
                if (p != null && rng.NextDouble() < 0.35)
                {
                    var c = Prop(seg, Pick(candles), p.localPosition + new Vector3(-s * 0.7f, 0f, -0.4f), Rand(0f, 360f), 2.2f);
                    if (c != null)
                    {
                        var fl = GlowQuad(seg, "CandleFlame", flameTexture, flameOrange, false);
                        fl.transform.localPosition = c.localPosition + new Vector3(0f, 0.75f, 0f);
                        fl.transform.localScale = Vector3.one * 0.55f;
                        glows.Add(new Glow { r = fl, color = flameOrange, baseI = 0.9f, kickI = 0.9f, face = true });
                        swells.Add(new Swell { t = fl.transform, rest = fl.transform.localScale, amount = 0.35f });
                    }
                }
            }

            // Crypts and obelisks further back, every third segment, sides staggered.
            if ((index + (s > 0 ? 1 : 0)) % 3 == 0)
                Prop(seg, Pick(crypts), new Vector3(s * Rand(cryptX.x, cryptX.y), 0f, Rand(-2f, 2f)), s > 0 ? -90f : 90f, propScale * Rand(1.1f, 1.4f));

            // Crooked dead trees beyond.
            if ((index + (s > 0 ? 0 : 1)) % 2 == 0)
                Prop(seg, Pick(trees), new Vector3(s * Rand(treeX.x, treeX.y), 0f, Rand(-3f, 3f)), Rand(0f, 360f), Rand(1.8f, 2.8f));

            // A stone wall section now and then behind the graves.
            if (index % 4 == (s > 0 ? 1 : 3))
                Prop(seg, Pick(fences), new Vector3(s * (graveX.y + 1.5f), 0f, 0f), s > 0 ? -90f : 90f, 2.2f);

            // A street lamp every other segment, alternating sides: a real light, a flare, a cone.
            if ((index % 2 == 0) == (s < 0))
            {
                var lamp = Prop(seg, Pick(lamps), new Vector3(s * lampX, 0f, 0f), s > 0 ? 180f : 0f, 1.6f);
                if (lamp != null)
                {
                    float top = 2.96f * 1.6f;
                    var head = GlowQuad(seg, "LampFlare", flareMask, flameOrange, false);
                    head.transform.localPosition = new Vector3(s * lampX, top - 0.3f, 0f);
                    head.transform.localScale = Vector3.one * 1.6f;
                    glows.Add(new Glow { r = head, color = flameOrange, baseI = 0.7f, kickI = 1.1f, face = true });
                    var go = new GameObject("LampLight");
                    go.transform.SetParent(seg, false);
                    go.transform.localPosition = new Vector3(s * (lampX - 0.4f), top - 0.4f, 0f);
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = flameOrange;
                    l.range = lampLightRange;
                    l.intensity = lampLightIntensity;
                    l.shadows = LightShadows.None;
                    lampLights.Add(new LampLight { light = l });
                }
            }

            // A glowing jack-o'-lantern by the fence every few segments.
            if (index % 3 == (s > 0 ? 2 : 0))
            {
                var pk = Prop(seg, Pick(pumpkins), new Vector3(s * (fenceX + 0.9f), 0f, Rand(-2f, 2f)), s > 0 ? -90f : 90f, 2.2f);
                if (pk != null)
                {
                    swells.Add(new Swell { t = pk, rest = pk.localScale, amount = 0.12f });
                    var gl = GlowQuad(seg, "PumpkinGlow", flareMask, flameOrange, false);
                    gl.transform.localPosition = pk.localPosition + new Vector3(0f, 0.5f, 0f);
                    gl.transform.localScale = Vector3.one * 3.2f;
                    glows.Add(new Glow { r = gl, color = flameOrange, baseI = 0.35f, kickI = 0.8f, face = true });
                }
            }

            // Details among the graves: hands from the ground, bones, poison mushrooms.
            if (rng.NextDouble() < 0.7)
            {
                var d = Prop(seg, Pick(details), new Vector3(s * Rand(graveX.x, graveX.x + 4f), 0f, Rand(-3.5f, 3.5f)), Rand(0f, 360f), propScale * 1.2f);
                if (d != null && d.name.Contains("Mushroom"))
                {
                    swells.Add(new Swell { t = d, rest = d.localScale, amount = 0.15f });
                    var gl = GlowQuad(seg, "MushroomGlow", flareMask, poisonGreen, false);
                    gl.transform.localPosition = d.localPosition + new Vector3(0f, 0.6f, 0f);
                    gl.transform.localScale = Vector3.one * 1.8f;
                    glows.Add(new Glow { r = gl, color = poisonGreen, baseI = 0.3f, kickI = 0.7f, face = true });
                }
            }

            // Ground fog: low sprites among the graves, moonlit blue with a ghost-green tinge.
            for (int k = 0; k < fogPerSegment; k++)
            {
                var sm = GlowQuad(seg, "GroundFog", smokeTexture, k == 0 ? Color.Lerp(moonBlue, poisonGreen, 0.35f) : moonBlue, false);
                float y = 0.5f + (float)rng.NextDouble() * 0.6f;
                sm.transform.localPosition = new Vector3(s * Rand(fenceX + 0.5f, graveX.y), y, Rand(-4f, 4f));
                sm.transform.localScale = new Vector3(Rand(6f, 9f), Rand(2.2f, 3.2f), 1f);
                fogs.Add(new Fog { t = sm.transform, r = sm, seed = index * 1.7f + k * 3.1f + s, y = y });
            }
        }
    }

    private Transform Prop(Transform parent, GameObject model, Vector3 pos, float yaw, float scale)
    {
        if (model == null) return null;
        var go = Instantiate(model, parent);
        go.name = model.name;
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one * scale;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
        foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = Night(mats[i]);
            r.sharedMaterials = mats;
        }
        return go.transform;
    }

    /// The night copy of a prop material: URP Lit (packs that ship Built-in Standard materials are
    /// converted), its colour cooled to moonlight; its emission kept, and pulsed later.
    private Material Night(Material src)
    {
        if (src == null) return null;
        if (night.TryGetValue(src, out var m)) return m;
        bool urp = src.shader != null && src.shader.name.StartsWith("Universal Render Pipeline");
        m = urp ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (!urp)
        {
            Texture tex = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            if (tex != null) m.SetTexture("_BaseMap", tex);
            if (src.HasProperty("_Color")) m.SetColor("_BaseColor", src.GetColor("_Color"));
            if (src.HasProperty("_Glossiness")) m.SetFloat("_Smoothness", src.GetFloat("_Glossiness") * 0.5f);
            if (src.IsKeywordEnabled("_EMISSION") && src.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor(EmissionId, src.GetColor(EmissionId));
                if (src.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", src.GetTexture("_EmissionMap"));
            }
        }
        if (m.HasProperty("_BaseColor"))
        {
            var c = m.GetColor("_BaseColor");
            m.SetColor("_BaseColor", new Color(c.r * stoneTint.r, c.g * stoneTint.g, c.b * stoneTint.b, c.a));
        }
        m.name = src.name + " (night)";
        if (m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor"))
        {
            var e = m.GetColor(EmissionId);
            float peak = Mathf.Max(e.r, Mathf.Max(e.g, e.b));
            if (peak > 0.01f) emissives.Add((m, e / Mathf.Max(1f, peak / 1.2f)));
        }
        night[src] = m;
        return m;
    }

    private MeshRenderer GlowQuad(Transform parent, string name, Texture2D mask, Color color, bool alphaMask)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = name;
        q.transform.SetParent(parent, false);
        var r = q.GetComponent<MeshRenderer>();
        var m = new Material(glowTemplate);
        if (mask != null) m.SetTexture("_MainTex", mask);
        m.SetFloat("_UseAlpha", alphaMask ? 1f : 0f);
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        mpb.Clear();
        mpb.SetColor(ColorId, color);
        mpb.SetFloat(IntensityId, 0f);
        r.SetPropertyBlock(mpb);
        return r;
    }

    private Bat NewBat(int i)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = "Bat";
        q.transform.SetParent(transform, false);
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = batMat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        // Most circle ahead, a few swarms in front of the moon.
        var b = new Bat
        {
            t = q.transform,
            center = new Vector3(Rand(-18f, 18f), 0f, Rand(35f, 110f)),
            angle = Rand(0f, Mathf.PI * 2f),
            radius = Rand(4f, 14f),
            height = Rand(7f, 20f),
            speed = Rand(0.5f, 1.1f) * (rng.NextDouble() < 0.5 ? -1f : 1f),
            flap = Rand(0f, 1f),
            size = Rand(0.9f, 1.8f),
        };
        return b;
    }

    // --- per frame ---

    private void OnDestroy()
    {
        tweens.KillAll();
        if (sky != null)
        {
            if (RenderSettings.skybox == sky) RenderSettings.skybox = savedSky;
            Destroy(sky);
        }
        foreach (var m in night.Values) if (m != null) Destroy(m);
        if (groundMat != null) Destroy(groundMat);
        if (batMat != null) Destroy(batMat);
        if (batTex != null) Destroy(batTex);
        if (glowTemplate != null) Destroy(glowTemplate);
        foreach (var g in glows) if (g.r != null) Destroy(g.r.sharedMaterial);
        foreach (var f in fogs) if (f.r != null) Destroy(f.r.sharedMaterial);
    }

    private void Update()
    {
        if (!built) return;
        float delta = scrollSpeed * Time.deltaTime;
        foreach (var seg in segments)
        {
            var p = seg.localPosition;
            p.z -= delta;
            if (p.z < -behindCamera - segmentSpacing) p.z += span;
            seg.localPosition = p;
        }
    }

    private void LateUpdate()
    {
        if (!built) return;
        tweens.SeekLoops(ClubBeat.BeatIndex + ClubBeat.BeatPhase);
        if (cam == null) cam = Camera.main;
        Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float drop = Mathf.Clamp01(energy - 1f) * 1.5f;
        float k = kick * Mathf.Clamp01(energy);

        foreach (var g in glows)
        {
            if (g.face) Face(g.r.transform, camPos);
            float flick = g.r.name == "CandleFlame" ? eighth : 1f;
            SetGlow(g.r, g.color, (g.baseI + g.kickI * k * (1f + drop)) * flick * emission / 1.2f);
        }
        foreach (var s in swells)
            s.t.localScale = s.rest * (1f + s.amount * k * (1f + 0.6f * drop));
        foreach (var l in lampLights)
            l.light.intensity = lampLightIntensity * (0.6f + 0.55f * k + 0.3f * drop * kick);
        // The kits' own emissive parts - lanterns, candle wax, the green fungus on the trees.
        foreach (var (m, e) in emissives)
            m.SetColor(EmissionId, Cap(e * (0.55f + 0.9f * k * (1f + 0.5f * drop))));

        // Fog: breathes over the bar, flares on the beat through a drop.
        foreach (var f in fogs)
        {
            Face(f.t, camPos);
            var p = f.t.localPosition;
            p.y = f.y + Mathf.Sin(ClubBeat.SongTime * 0.35f + f.seed) * 0.15f;
            f.t.localPosition = p;
            SetGlow(f.r, (Color?)null, 0.07f + 0.05f * bar + 0.05f * energy + 0.12f * k + drop * kick * 0.35f);
        }

        if (sky != null) sky.SetFloat(MoonPulseId, Mathf.Clamp01(0.25f * bar + 0.6f * k + 0.3f * drop * kick));

        float batSpeed = 1f + drop * 1.2f;
        foreach (var b in bats)
        {
            b.angle += b.speed * batSpeed * Time.deltaTime * 0.6f;
            b.flap += Time.deltaTime * (5f + 3f * batSpeed);
            var pos = b.center + new Vector3(Mathf.Cos(b.angle) * b.radius, b.height + Mathf.Sin(b.angle * 2f) * 1.2f, Mathf.Sin(b.angle) * b.radius * 0.5f);
            b.t.localPosition = pos;
            b.t.rotation = Quaternion.LookRotation(b.t.position - camPos);
            float w = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(b.flap * Mathf.PI));
            b.t.localScale = new Vector3(b.size * 1.6f, b.size * 0.8f * w, 1f);
        }
    }

    private void SetGlow(MeshRenderer r, Color? color, float intensity)
    {
        r.GetPropertyBlock(mpb);
        if (color.HasValue) mpb.SetColor(ColorId, color.Value);
        mpb.SetFloat(IntensityId, Mathf.Clamp(intensity, 0f, MaxGlow));
        r.SetPropertyBlock(mpb);
        r.enabled = intensity > 0.001f;
    }

    private static Color Cap(Color c)
    {
        float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        return peak > MaxGlow ? c * (MaxGlow / peak) : c;
    }

    private static void Face(Transform t, Vector3 camPos) => t.rotation = Quaternion.LookRotation(t.position - camPos);

    private GameObject Pick(GameObject[] list) => list == null || list.Length == 0 ? null : list[rng.Next(list.Length)];
    private float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    /// A bat, drawn in code: a small body and two scalloped wings, alpha only.
    private static Texture2D BatTexture()
    {
        const int w = 64, h = 32;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                float au = Mathf.Abs(u);
                // Body and ears.
                bool body = (u * u) / 0.012f + (v + 0.05f) * (v + 0.05f) / 0.35f < 1f;
                bool ears = au > 0.03f && au < 0.1f && v > 0.3f && v < 0.3f + (0.1f - au) * 3f + 0.1f;
                // Wing: top edge an arch from the shoulder to the tip, bottom edge scalloped.
                float top = 0.35f - au * 0.25f;
                float scallop = -0.05f - 0.25f * Mathf.Abs(Mathf.Sin(au * Mathf.PI * 2.5f)) + au * 0.1f;
                bool wing = au < 0.98f && v < top && v > scallop;
                byte a = (byte)((body || ears || wing) ? 255 : 0);
                px[y * w + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }
}
