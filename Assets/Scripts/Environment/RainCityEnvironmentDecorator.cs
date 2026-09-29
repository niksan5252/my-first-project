using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_AutumnRainCity's environment, "Rainy night city": an old-town street on a wet autumn
/// night, the finale of video 4 and the hardest-hitting segment. On the Halloween machinery
/// (GraveyardEnvironmentDecorator: lights and emission kicking on the beat, haze) it builds,
/// travelling with the road:
///
///   * house fronts either side, slate-blue and wet, their windows lit warm amber (Kenney light
///     masks) - a few flicker, all of them brighten on the beat;
///   * cafe and shop windows at street level under striped awnings, a neon sign blinking on every
///     beat, cafe tables under umbrellas;
///   * street lamps (Kenney city-kit-roads) with real lights, flares and cones of light in the rain;
///   * puddles on the pavement whose splash rings flash on every beat, harder in a drop;
///   * heavy rain falling all along the street, heavier through a drop, and lightning over the
///     roofs on the drop's strong beats.
public class RainCityEnvironmentDecorator : GraveyardEnvironmentDecorator
{
    [Header("City")]
    [SerializeField] private GameObject[] streetLamps;
    [SerializeField] private Texture2D[] windowMasks;
    [SerializeField] private Texture2D shopMask;
    [SerializeField] private Texture2D lampConeMask;
    [SerializeField] private Texture2D ringMask;
    [SerializeField] private Texture2D signMask;

    [Header("City colours and layout")]
    [SerializeField] private Color windowAmber = new Color(1f, 0.66f, 0.36f);
    [SerializeField] private Color[] signColors = { new Color(1f, 0.25f, 0.6f), new Color(0.25f, 0.85f, 1f), new Color(1f, 0.7f, 0.2f) };
    [SerializeField] private Color[] awningColors = { new Color(0.55f, 0.08f, 0.1f), new Color(0.08f, 0.3f, 0.2f), new Color(0.12f, 0.15f, 0.4f) };
    [SerializeField] private Color facade = new Color(0.09f, 0.1f, 0.15f);
    [SerializeField] private float frontX = 7.8f;

    private Material facadeMat, trimMat, awningMatA, puddleMat, umbrellaMat, tableMat;
    private readonly List<Material> awningMats = new List<Material>();
    private struct Win { public MeshRenderer r; public float seed; public bool flicker; }
    private readonly List<Win> windows = new List<Win>();
    private struct Sign { public MeshRenderer r; public int index; }
    private readonly List<Sign> signs = new List<Sign>();
    private struct Splash { public MeshRenderer r; public Transform t; public float seed; }
    private readonly List<Splash> splashes = new List<Splash>();
    private readonly List<MeshRenderer> cones = new List<MeshRenderer>();
    private Mesh umbrellaMesh;
    private ParticleSystem rain;
    private float lastStrike = -100f;
    private int lastStrikeBeat = int.MinValue;

    protected override void BuildSegment(Transform stage, int index, float z)
    {
        EnsureMaterials();
        var seg = new GameObject("Segment_" + index.ToString("00")).transform;
        seg.SetParent(stage, false);
        seg.localPosition = new Vector3(0f, 0f, z);
        RegisterSegment(seg);

        for (int s = -1; s <= 1; s += 2)
        {
            Quaternion toRoad = Quaternion.Euler(0f, s > 0 ? 90f : -90f, 0f);
            float height = Rand(9f, 16f);
            Box(seg, new Vector3(s * (frontX + 3f), height * 0.5f, 0f), new Vector3(6f, height, segmentSpacing - 0.15f), facadeMat);
            float face = frontX - 0.02f;
            // Pilasters between the houses and a cornice at every floor, so a front reads as a
            // house - storeys, bays - rather than one flat slab catching the lamplight.
            for (int pz = -1; pz <= 1; pz += 2)
                Box(seg, new Vector3(s * (frontX - 0.12f), height * 0.5f, pz * (segmentSpacing * 0.5f - 0.35f)), new Vector3(0.3f, height, 0.6f), trimMat);
            for (float fy = 3.6f; fy < height - 0.5f; fy += 2.6f)
                Box(seg, new Vector3(s * (frontX - 0.08f), fy, 0f), new Vector3(0.2f, 0.18f, segmentSpacing - 0.2f), trimMat);
            Box(seg, new Vector3(s * (frontX - 0.2f), height, 0f), new Vector3(0.5f, 0.35f, segmentSpacing), trimMat);

            // Upper windows: two columns, as many rows as the house has floors.
            for (int row = 0; row < Mathf.FloorToInt((height - 4f) / 2.6f); row++)
                for (int col = -1; col <= 1; col += 2)
                {
                    bool lit = rng.NextDouble() < 0.72;
                    var w = Glow(seg, "Window", windowMasks[rng.Next(windowMasks.Length)], new Vector3(s * face, 5.2f + row * 2.6f, col * 2f), toRoad, new Vector2(1.2f, 1.7f));
                    windows.Add(new Win { r = w, seed = index * 3.1f + row + col, flicker = lit && rng.NextDouble() < 0.12 });
                    if (!lit) w.enabled = false;
                }
            // The shop front: a big warm window under an awning, a neon sign beside it.
            windows.Add(new Win { r = Glow(seg, "ShopWindow", shopMask, new Vector3(s * (face - 0.01f), 1.6f, 0f), toRoad, new Vector2(5.2f, 2.4f)), seed = index + s });
            var awning = Box(seg, new Vector3(s * (frontX - 0.7f), 3.25f, 0f), new Vector3(1.5f, 0.12f, 5.6f), awningMats[(index + (s > 0 ? 1 : 0)) % awningMats.Count]);
            awning.localRotation = Quaternion.Euler(0f, 0f, s * 14f);
            var sign = Glow(seg, "Sign", signMask, new Vector3(s * (frontX - 0.9f), 4.2f, 3.2f), Quaternion.identity, new Vector2(1.1f, 1.1f));
            signs.Add(new Sign { r = sign, index = signs.Count });

            // A cafe table under an umbrella every other segment.
            if ((index + (s > 0 ? 1 : 0)) % 2 == 0)
            {
                Vector3 at = new Vector3(s * (frontX - 2.1f), 0f, -2.2f);
                Box(seg, at + new Vector3(0f, 0.75f, 0f), new Vector3(0.9f, 0.06f, 0.9f), tableMat);
                Box(seg, at + new Vector3(0f, 1.2f, 0f), new Vector3(0.05f, 2.4f, 0.05f), tableMat);
                var u = new GameObject("Umbrella");
                u.transform.SetParent(seg, false);
                u.transform.localPosition = at + new Vector3(0f, 2.35f, 0f);
                u.transform.localScale = new Vector3(1.4f, 0.5f, 1.4f);
                u.AddComponent<MeshFilter>().sharedMesh = umbrellaMesh;
                var ur = u.AddComponent<MeshRenderer>();
                ur.sharedMaterial = umbrellaMat;
                ur.shadowCastingMode = ShadowCastingMode.Off;
                swells.Add(new Swell { t = u.transform, rest = u.transform.localScale, amount = 0.06f });
            }

            // A street lamp every other segment, alternating sides: light, flare, a cone in the rain.
            if ((index % 2 == 0) == (s < 0))
            {
                var lamp = Prop(seg, Pick(streetLamps), new Vector3(s * 5.4f, 0f, 1f), s > 0 ? -90f : 90f, 5.2f);
                if (lamp != null)
                {
                    float top = 5.9f;
                    var head = GlowQuad(seg, "LampFlare", flareMask, windowAmber, false);
                    head.transform.localPosition = new Vector3(s * 4.3f, top, 1f);
                    head.transform.localScale = Vector3.one * 1.3f;
                    glows.Add(new Glow { r = head, color = windowAmber, baseI = 0.7f, kickI = 1.2f, face = true });
                    // A blurred Kenney mask keeps its soft falloff in alpha; its colour is flat white.
                    var cone = GlowQuad(seg, "LampCone", lampConeMask, windowAmber, true);
                    cone.transform.localPosition = new Vector3(s * 4.3f, top * 0.5f, 1f);
                    cone.transform.localScale = new Vector3(3.4f, top, 1f);
                    cone.sharedMaterial.SetTextureScale("_MainTex", new Vector2(1f, -1f));
                    cone.sharedMaterial.SetTextureOffset("_MainTex", new Vector2(0f, 1f));
                    cones.Add(cone);
                    var go = new GameObject("LampLight");
                    go.transform.SetParent(seg, false);
                    go.transform.localPosition = new Vector3(s * 4.3f, top - 0.3f, 1f);
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = windowAmber;
                    l.range = lampLightRange;
                    l.intensity = lampLightIntensity;
                    l.shadows = LightShadows.None;
                    lampLights.Add(new LampLight { light = l });
                }
            }

            // Puddles on the pavement, their splash rings flashing on the beat.
            for (int p = 0; p < 2; p++)
            {
                Vector3 at = new Vector3(s * Rand(3.4f, 6f), 0.02f, Rand(-3.5f, 3.5f));
                var puddle = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(puddle.GetComponent<Collider>());
                puddle.transform.SetParent(seg, false);
                puddle.transform.localPosition = at;
                puddle.transform.localRotation = Quaternion.Euler(90f, Rand(0f, 360f), 0f);
                puddle.transform.localScale = new Vector3(Rand(1.4f, 2.6f), Rand(0.9f, 1.6f), 1f);
                var pr = puddle.GetComponent<MeshRenderer>();
                pr.sharedMaterial = puddleMat;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                var ring = GlowQuad(seg, "Splash", ringMask, Color.Lerp(windowAmber, moonBlue, 0.5f), true);
                ring.transform.localPosition = at + new Vector3(0f, 0.03f, 0f);
                ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                splashes.Add(new Splash { r = ring, t = ring.transform, seed = (float)rng.NextDouble() });
            }

            // Cold haze in the street.
            for (int k = 0; k < fogPerSegment; k++)
            {
                var sm = GlowQuad(seg, "GroundFog", smokeTexture, moonBlue, false);
                float y = 0.6f + (float)rng.NextDouble() * 0.8f;
                sm.transform.localPosition = new Vector3(s * Rand(3.5f, frontX - 0.5f), y, Rand(-4f, 4f));
                sm.transform.localScale = new Vector3(Rand(5f, 7f), Rand(2f, 3f), 1f);
                fogs.Add(new Fog { t = sm.transform, r = sm, seed = index * 1.7f + k * 3.1f + s, y = y });
            }
        }
    }

    private MeshRenderer Glow(Transform seg, string name, Texture2D mask, Vector3 pos, Quaternion rot, Vector2 size)
    {
        var q = GlowQuad(seg, name, mask, name == "Sign" ? signColors[signs.Count % signColors.Length] : windowAmber, true);
        q.transform.localPosition = pos;
        q.transform.localRotation = rot;
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        return q;
    }

    private void EnsureMaterials()
    {
        if (facadeMat != null) return;
        facadeMat = Lit(facade, 0.35f);
        trimMat = Lit(facade * 0.55f, 0.65f);
        foreach (var c in awningColors) awningMats.Add(Lit(c, 0.6f));
        puddleMat = Lit(new Color(0.02f, 0.025f, 0.04f), 0.97f);
        puddleMat.SetFloat("_Metallic", 0.6f);
        umbrellaMat = Lit(new Color(0.5f, 0.07f, 0.12f), 0.7f);
        umbrellaMat.SetFloat("_Cull", 0f);
        tableMat = Lit(new Color(0.12f, 0.1f, 0.1f), 0.5f);
        umbrellaMesh = Cone(10);
        if (groundMat != null) { groundMat.SetFloat("_Smoothness", 0.85f); groundMat.SetFloat("_Metallic", 0.3f); }
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (cam == null) return;
        if (rain == null && tunnelLength > 0f) rain = Rain();
        Vector3 camPos = cam.transform.position;
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float drop = Mathf.Clamp01(energy - 1f) * 1.6f;
        float t = ClubBeat.SongTime;
        float slam = kick * energy * (1f + drop);
        int beat = ClubBeat.BeatIndex;

        // Lightning over the roofs on the drop's beats 1 and 3, and on every other bar's first beat.
        if (beat != lastStrikeBeat)
        {
            lastStrikeBeat = beat;
            int inBar = ((beat % 4) + 4) % 4;
            bool strike = drop > 0.2f ? (inBar == 0 || inBar == 2) : (inBar == 0 && (beat / 4) % 4 == 0 && energy > 0.9f);
            if (strike) lastStrike = t - ClubBeat.BeatPhase * ClubBeat.SecondsPerBeat;
        }
        float since = t - lastStrike;
        float flash = since < 0f ? 0f : Mathf.Clamp01(Mathf.Exp(-since / 0.08f) + 0.5f * (since > 0.12f ? Mathf.Exp(-(since - 0.12f) / 0.1f) : 0f));
        if (sky != null) sky.SetFloat("_Flash", flash * 0.6f);

        foreach (var w in windows)
        {
            if (!w.r.enabled) continue;
            float f = w.flicker ? (Mathf.PerlinNoise(t * 7f, w.seed) > 0.35f ? 1f : 0.25f) : 1f;
            SetGlow(w.r, null, (0.3f + 0.45f * slam + 0.4f * flash) * f);
        }
        foreach (var sg in signs)
        {
            Face(sg.r.transform, camPos);
            bool on = ((beat + sg.index) % 2 == 0) || drop > 0.2f;
            SetGlow(sg.r, null, on ? 0.9f + 0.9f * slam : 0.15f);
        }
        foreach (var c in cones) SetGlow(c, null, 0.18f + 0.3f * slam);
        foreach (var sp in splashes)
        {
            // A ring that spreads from nothing on the beat and fades over it.
            float ph = Mathf.Repeat(ClubBeat.BeatPhase + sp.seed * 0.25f, 1f);
            sp.t.localScale = Vector3.one * (0.3f + 1.6f * ph);
            SetGlow(sp.r, null, (1f - ph) * (0.25f + 0.8f * energy * (1f + drop)));
        }
        // The street haze fades out near the lens: a sprite passing a couple of metres from the
        // camera on the pavement otherwise washes half the frame.
        foreach (var f in fogs)
        {
            float near = Mathf.Clamp01((Vector3.Distance(f.t.position, camPos) - 7f) / 10f);
            SetGlow(f.r, null, (0.05f + 0.22f * slam + 0.25f * flash) * near);
        }
        foreach (var l in lampLights) l.light.intensity = lampLightIntensity * (0.45f + 1.0f * slam + 0.5f * flash);
        if (rain != null)
        {
            var em = rain.emission;
            em.rateOverTime = 1800f + 1800f * drop;
        }
    }

    private ParticleSystem Rain()
    {
        var go = new GameObject("Rain");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 14f, tunnelLength * 0.35f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 1.1f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
        main.gravityModifier = 2.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 8000;
        main.startColor = new Color(0.6f, 0.7f, 1f, 0.5f);
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(22f, 0.1f, tunnelLength * 0.7f);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.5f);
        vel.y = new ParticleSystem.MinMaxCurve(-6f);
        vel.z = new ParticleSystem.MinMaxCurve(-scrollSpeed * 0.9f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.EnableKeyword("_BLENDMODE_ADD");
        m.SetFloat("_SrcBlend", (float)BlendMode.One);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.renderQueue = (int)RenderQueue.Transparent;
        r.sharedMaterial = m;
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.04f;
        r.lengthScale = 4f;
        r.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        foreach (var o in new Object[] { facadeMat, trimMat, puddleMat, umbrellaMat, tableMat, umbrellaMesh }) if (o != null) Destroy(o);
        foreach (var m in awningMats) if (m != null) Destroy(m);
    }

    private static Material Lit(Color c, float smooth)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        return m;
    }

    private static Transform Box(Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(b.GetComponent<Collider>());
        b.transform.SetParent(parent, false);
        b.transform.localPosition = pos;
        b.transform.localScale = size;
        var r = b.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return b.transform;
    }

    /// An umbrella's canopy: a shallow cone of `n` panels, apex up, radius and height 1.
    private static Mesh Cone(int n)
    {
        var m = new Mesh { name = "Umbrella" };
        var v = new List<Vector3> { new Vector3(0f, 1f, 0f) };
        var tri = new List<int>();
        for (int i = 0; i <= n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            v.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
        }
        for (int i = 1; i <= n; i++) tri.AddRange(new[] { 0, i + 1, i });
        m.SetVertices(v);
        m.SetTriangles(tri, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}
