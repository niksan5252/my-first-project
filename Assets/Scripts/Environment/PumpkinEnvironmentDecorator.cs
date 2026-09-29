using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_HalloweenPumpkin's environment, "Pumpkin festival": a harvest fair on a warm Halloween
/// night, the road running between its stalls. It is the graveyard's machinery - props cooled to
/// night, the kits' emission and every flame kicking on the beat, fog, bats, a moon that breathes
/// (GraveyardEnvironmentDecorator) - with a festival built on it instead of a cemetery:
///
///   * heaps of jack-o'-lanterns along both sides, glowing and swelling on the beat;
///   * wooden poles with strings of lanterns between them (Poly Halloween), their bulbs chasing
///     along the string on the eighths, and bunting;
///   * bonfires - fire baskets with a real point light, flames that flicker on the eighths and
///     embers that burst up on every beat of a drop;
///   * hay bales stacked by the road, scarecrows, cauldrons with a green glow, candles, lanterns;
///   * sparks drifting up through the air the whole time, a burst of them on each drop's beats.
public class PumpkinEnvironmentDecorator : GraveyardEnvironmentDecorator
{
    [Header("Festival models")]
    [SerializeField] private GameObject[] litPumpkins;
    [SerializeField] private GameObject[] hay;
    [SerializeField] private GameObject[] scarecrows;
    [SerializeField] private GameObject[] garlands;
    [SerializeField] private GameObject[] bunting;
    [SerializeField] private GameObject fireBasket;
    [SerializeField] private GameObject cauldron;
    [SerializeField] private Texture2D sparkTexture;

    [Header("Festival colours and layout")]
    [SerializeField] private Color purple = new Color(0.62f, 0.25f, 1f);
    [SerializeField] private Color warmYellow = new Color(1f, 0.8f, 0.3f);
    [SerializeField] private float poleX = 5.2f;
    [SerializeField] private float poleHeight = 4.6f;
    [SerializeField] private Vector2 heapX = new Vector2(6.4f, 9f);

    private struct Bulb { public MeshRenderer r; public int index; }
    private readonly List<Bulb> bulbs = new List<Bulb>();
    private readonly List<ParticleSystem> embers = new List<ParticleSystem>();
    private ParticleSystem airSparks;
    private Material woodMat, sparkMat;
    private int lastBeat = int.MinValue;

    protected override void BuildSegment(Transform stage, int index, float z)
    {
        if (woodMat == null)
        {
            woodMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            woodMat.SetColor("_BaseColor", new Color(0.14f, 0.08f, 0.05f));
            woodMat.SetFloat("_Smoothness", 0.2f);
        }
        var seg = new GameObject("Segment_" + index.ToString("00")).transform;
        seg.SetParent(stage, false);
        seg.localPosition = new Vector3(0f, 0f, z);
        RegisterSegment(seg);

        for (int s = -1; s <= 1; s += 2)
        {
            // A pole at the road's edge, a string of lanterns from it to the next pole along.
            Pole(seg, new Vector3(s * poleX, 0f, 0f));
            var g = Pick(garlands);
            if (g != null)
            {
                var str = Prop(seg, g, new Vector3(s * poleX, poleHeight - 0.15f, segmentSpacing * 0.5f), -90f, 1f);
                if (str != null) str.localScale = new Vector3(segmentSpacing / 5f, 1.2f, 1.2f);
                // Bulbs of light along the string, chasing on the eighths.
                for (int b = 0; b < 6; b++)
                {
                    float t = (b + 0.5f) / 6f;
                    var q = GlowQuad(seg, "Bulb", flareMask, b % 2 == 0 ? warmYellow : flameOrange, false);
                    q.transform.localPosition = new Vector3(s * poleX, poleHeight - 0.45f - Mathf.Sin(t * Mathf.PI) * 0.45f, -segmentSpacing * 0.5f + t * segmentSpacing);
                    q.transform.localScale = Vector3.one * 0.7f;
                    bulbs.Add(new Bulb { r = q, index = bulbs.Count });
                }
            }
            if (index % 2 == 0)
            {
                var f = Pick(bunting);
                if (f != null)
                {
                    var bt = Prop(seg, f, new Vector3(s * (poleX + 0.2f), poleHeight - 1.0f, segmentSpacing * 0.5f), -90f, 1f);
                    if (bt != null) bt.localScale = new Vector3(segmentSpacing / 5f, 1.4f, 1.4f);
                }
            }

            // A heap of jack-o'-lanterns: a pyramid of lit and plain ones.
            if ((index + (s > 0 ? 1 : 0)) % 2 == 0) Heap(seg, new Vector3(s * Rand(heapX.x, heapX.y), 0f, Rand(-2.5f, 2.5f)));
            else
            {
                // Hay stacked by the road, a candle or two on it.
                var h = Pick(hay);
                for (int k = 0; k < 3; k++)
                {
                    var bale = Prop(seg, h, new Vector3(s * (6.6f + k % 2 * 0.9f), k == 2 ? 0.65f * 2.3f : 0f, (k - 1) * 0.9f), s > 0 ? -90f : 90f, 2.3f);
                    if (k == 2 && bale != null) Flame(seg, bale.localPosition + new Vector3(0f, 0.8f, 0f), 0.5f);
                }
            }

            // A bonfire every third segment, sides staggered: basket, flames, light, embers.
            if ((index + (s > 0 ? 0 : 1)) % 3 == 0) Bonfire(seg, new Vector3(s * 8.8f, 0f, Rand(-2f, 2f)));

            // Scarecrows and cauldrons further back.
            if ((index + (s > 0 ? 2 : 0)) % 4 == 0)
                Prop(seg, Pick(scarecrows), new Vector3(s * Rand(10f, 13f), 0f, Rand(-2f, 2f)), s > 0 ? -90f : 90f, 2.2f);
            if ((index + (s > 0 ? 1 : 3)) % 4 == 0 && cauldron != null)
            {
                var c = Prop(seg, cauldron, new Vector3(s * Rand(9.5f, 12f), 0f, Rand(-2f, 2f)), 0f, 2.4f);
                if (c != null)
                {
                    var gl = GlowQuad(seg, "CauldronGlow", flareMask, poisonGreen, false);
                    gl.transform.localPosition = c.localPosition + new Vector3(0f, 1.9f, 0f);
                    gl.transform.localScale = Vector3.one * 2.4f;
                    glows.Add(new Glow { r = gl, color = poisonGreen, baseI = 0.35f, kickI = 0.8f, face = true });
                    swells.Add(new Swell { t = gl.transform, rest = gl.transform.localScale, amount = 0.3f });
                }
            }

            // A lantern post with a real light every other segment.
            if ((index % 2 == 0) == (s > 0)) LampPost(seg, new Vector3(s * (poleX + 1.2f), 0f, segmentSpacing * 0.25f));

            // Low haze: purple, a warm one near the fires.
            for (int k = 0; k < fogPerSegment; k++)
            {
                var sm = GlowQuad(seg, "GroundFog", smokeTexture, k == 0 ? Color.Lerp(purple, flameOrange, 0.35f) : purple, false);
                float y = 0.5f + (float)rng.NextDouble() * 0.6f;
                sm.transform.localPosition = new Vector3(s * Rand(poleX + 0.5f, 14f), y, Rand(-4f, 4f));
                sm.transform.localScale = new Vector3(Rand(6f, 9f), Rand(2.2f, 3.2f), 1f);
                fogs.Add(new Fog { t = sm.transform, r = sm, seed = index * 1.7f + k * 3.1f + s, y = y });
            }
        }
    }

    private void Pole(Transform seg, Vector3 pos)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(p.GetComponent<Collider>());
        p.name = "Pole";
        p.transform.SetParent(seg, false);
        p.transform.localPosition = pos + new Vector3(0f, poleHeight * 0.5f, 0f);
        p.transform.localScale = new Vector3(0.22f, poleHeight, 0.22f);
        var r = p.GetComponent<MeshRenderer>();
        r.sharedMaterial = woodMat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    private void Heap(Transform seg, Vector3 at)
    {
        // Three rows, 4-3-1, each pumpkin a little different; the lit ones glow and all swell.
        int[] rows = { 4, 3, 1 };
        float size = 0.62f * 2.1f;
        for (int row = 0; row < rows.Length; row++)
            for (int i = 0; i < rows[row]; i++)
            {
                float x = (i - (rows[row] - 1) * 0.5f) * size * 0.95f;
                var pos = at + new Vector3(Rand(-0.1f, 0.1f), row * size * 0.72f, x);
                bool lit = rng.NextDouble() < 0.6;
                var pk = Prop(seg, lit ? Pick(litPumpkins) : Pick(pumpkins), pos, Rand(-60f, 60f) + (at.x > 0 ? -90f : 90f), 2.1f * Rand(0.9f, 1.1f));
                if (pk == null) continue;
                swells.Add(new Swell { t = pk, rest = pk.localScale, amount = 0.1f });
                if (lit && (row + i) % 2 == 0)
                {
                    var gl = GlowQuad(seg, "PumpkinGlow", flareMask, flameOrange, false);
                    gl.transform.localPosition = pos + new Vector3(0f, 0.45f, 0f);
                    gl.transform.localScale = Vector3.one * 2.2f;
                    glows.Add(new Glow { r = gl, color = flameOrange, baseI = 0.35f, kickI = 0.9f, face = true });
                }
            }
    }

    private void Flame(Transform seg, Vector3 pos, float size)
    {
        var fl = GlowQuad(seg, "CandleFlame", flameTexture, flameOrange, false);
        fl.transform.localPosition = pos;
        fl.transform.localScale = Vector3.one * size;
        glows.Add(new Glow { r = fl, color = flameOrange, baseI = 0.9f, kickI = 0.9f, face = true });
        swells.Add(new Swell { t = fl.transform, rest = fl.transform.localScale, amount = 0.35f });
    }

    private void Bonfire(Transform seg, Vector3 pos)
    {
        Prop(seg, fireBasket, pos, 0f, 4.5f);
        for (int k = 0; k < 3; k++) Flame(seg, pos + new Vector3(Rand(-0.4f, 0.4f), 1.1f + k * 0.35f, Rand(-0.3f, 0.3f)), 1.6f - k * 0.35f);
        var go = new GameObject("FireLight");
        go.transform.SetParent(seg, false);
        go.transform.localPosition = pos + new Vector3(0f, 1.6f, 0f);
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = flameOrange;
        l.range = lampLightRange;
        l.intensity = lampLightIntensity;
        l.shadows = LightShadows.None;
        lampLights.Add(new LampLight { light = l });
        embers.Add(Sparks(seg, pos + new Vector3(0f, 1.2f, 0f), 0.6f, false));
    }

    private void LampPost(Transform seg, Vector3 pos)
    {
        var lamp = Prop(seg, Pick(lamps), pos, pos.x > 0 ? 180f : 0f, 1.5f);
        if (lamp == null) return;
        var head = GlowQuad(seg, "LampFlare", flareMask, warmYellow, false);
        head.transform.localPosition = pos + new Vector3(0f, 4.3f, 0f);
        head.transform.localScale = Vector3.one * 1.5f;
        glows.Add(new Glow { r = head, color = warmYellow, baseI = 0.6f, kickI = 1.0f, face = true });
        var go = new GameObject("LampLight");
        go.transform.SetParent(seg, false);
        go.transform.localPosition = pos + new Vector3(0f, 4.1f, 0f);
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = warmYellow;
        l.range = lampLightRange;
        l.intensity = lampLightIntensity * 0.8f;
        l.shadows = LightShadows.None;
        lampLights.Add(new LampLight { light = l });
    }

    /// Embers: tiny additive sparks rising from a fire (or, `air`, drifting up all along the road).
    private ParticleSystem Sparks(Transform parent, Vector3 pos, float radius, bool air)
    {
        if (sparkMat == null)
        {
            sparkMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            if (sparkTexture != null) sparkMat.SetTexture("_BaseMap", sparkTexture);
            sparkMat.SetFloat("_Surface", 1f);
            sparkMat.SetFloat("_Blend", 2f);   // additive
            sparkMat.SetColor("_BaseColor", new Color(1.6f, 1.0f, 0.4f, 1f));
            sparkMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            sparkMat.EnableKeyword("_BLENDMODE_ADD");
            sparkMat.SetFloat("_SrcBlend", (float)BlendMode.One);
            sparkMat.SetFloat("_DstBlend", (float)BlendMode.One);
            sparkMat.SetFloat("_ZWrite", 0f);
            sparkMat.renderQueue = (int)RenderQueue.Transparent;
        }
        var go = new GameObject(air ? "AirSparks" : "Embers");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(air ? 2.5f : 0.8f, air ? 4.5f : 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(air ? 0.4f : 1.5f, air ? 1.2f : 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(air ? 0.05f : 0.06f, air ? 0.12f : 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(flameOrange, warmYellow);
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = air ? 600 : 200;
        var em = ps.emission;
        em.rateOverTime = air ? 60f : 8f;
        var shape = ps.shape;
        if (air)
        {
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(26f, 0.5f, tunnelLength);
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = radius;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        }
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = air ? 0.4f : 0.8f;
        noise.frequency = 0.6f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = sparkMat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (airSparks == null && tunnelLength > 0f) airSparks = Sparks(transform, new Vector3(0f, 0.3f, tunnelLength * 0.5f), 0f, true);
        if (cam == null) return;
        Vector3 camPos = cam.transform.position;
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        bool drop = energy > 1.05f;
        // Garland bulbs chase along each string on the eighths, brighter on the beat.
        int step = Mathf.FloorToInt((ClubBeat.BeatIndex + ClubBeat.BeatPhase) * 2f);
        foreach (var b in bulbs)
        {
            Face(b.r.transform, camPos);
            bool lead = (b.index + step) % 3 == 0;
            SetGlow(b.r, null, (lead ? 1.3f : 0.45f) * (0.7f + 0.5f * kick * energy));
        }
        // Embers and air sparks: a burst on each beat of a drop, a smaller one on the beats of loud parts.
        int beat = ClubBeat.BeatIndex;
        if (beat != lastBeat)
        {
            lastBeat = beat;
            int n = drop ? 26 : energy > 0.8f ? 6 : 0;
            if (n > 0) foreach (var e in embers) e.Emit(n);
            if (drop && airSparks != null) airSparks.Emit(90);
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (woodMat != null) Destroy(woodMat);
        if (sparkMat != null) Destroy(sparkMat);
    }
}
