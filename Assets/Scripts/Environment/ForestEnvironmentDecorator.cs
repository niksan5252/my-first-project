using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_AutumnForest's environment, "Golden forest": the road runs through a beech-and-oak wood in
/// the low afternoon sun. On the Halloween machinery (GraveyardEnvironmentDecorator: props, flames
/// and emission kicking on the beat, fog, a sun that breathes) it builds, travelling with the road:
///
///   * two rows of big autumn trees either side (Kenney Nature Kit, *_fall), their crowns recoloured
///     per tree in gold, amber and crimson, glowing a little and brightening on every beat;
///   * bushes, stumps, logs, rocks and mushroom clusters under them; the mushrooms swell;
///   * shafts of sunlight slanting down through the crowns (Kenney light-mask streaks), flaring on
///     beats 1 and 3;
///   * a carpet of fallen leaves along the road's edges and golden haze in the undergrowth;
///   * leaves drifting down all the time, a gust of them on every beat of a drop; motes of light
///     floating in the sun.
public class ForestEnvironmentDecorator : GraveyardEnvironmentDecorator
{
    [Header("Forest models")]
    [SerializeField] private GameObject[] treeModels;
    [SerializeField] private GameObject[] bushes;
    [SerializeField] private GameObject[] groundProps;
    [SerializeField] private GameObject[] mushrooms;
    [SerializeField] private Texture2D[] rayMasks;

    [Header("Forest colours")]
    [SerializeField] private Color[] leafColors =
    {
        new Color(1f, 0.72f, 0.12f), new Color(1f, 0.45f, 0.08f), new Color(0.78f, 0.13f, 0.08f), new Color(0.95f, 0.6f, 0.15f),
    };
    [SerializeField] private Color sunlight = new Color(1f, 0.78f, 0.4f);
    [SerializeField, Range(0f, 0.8f)] private float leafGlow = 0.35f;

    private readonly Dictionary<Material, Material[]> leafVariants = new Dictionary<Material, Material[]>();
    private struct Ray { public MeshRenderer r; public float strength; public int index; }
    private readonly List<Ray> rays = new List<Ray>();
    private Material carpetMat;
    private Texture2D leafTex;
    private ParticleSystem falling;
    private int lastBurstBeat = int.MinValue;

    protected override void BuildSegment(Transform stage, int index, float z)
    {
        if (leafTex == null)
        {
            leafTex = PlaylistThemeBlender.LeafTexture();
            carpetMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            carpetMat.SetTexture("_BaseMap", leafTex);
            carpetMat.SetFloat("_AlphaClip", 1f);
            carpetMat.SetFloat("_Cutoff", 0.5f);
            carpetMat.EnableKeyword("_ALPHATEST_ON");
            carpetMat.SetFloat("_Cull", 0f);
        }
        var seg = new GameObject("Segment_" + index.ToString("00")).transform;
        seg.SetParent(stage, false);
        seg.localPosition = new Vector3(0f, 0f, z);
        RegisterSegment(seg);

        for (int s = -1; s <= 1; s += 2)
        {
            // Two rows of trees: one close behind the arches, one further back.
            for (int row = 0; row < 2; row++)
            {
                float x = s * (row == 0 ? Rand(6.2f, 8.5f) : Rand(11f, 17f));
                var tree = Prop(seg, Pick(treeModels), new Vector3(x, 0f, Rand(-3.5f, 3.5f)), Rand(0f, 360f), Rand(6.5f, 9.5f));
                if (tree != null) Autumn(tree);
            }
            var bush = Prop(seg, Pick(bushes), new Vector3(s * Rand(5.6f, 7f), 0f, Rand(-3.5f, 3.5f)), Rand(0f, 360f), Rand(2.5f, 3.6f));
            if (bush != null) Autumn(bush);
            if (rng.NextDouble() < 0.7) Prop(seg, Pick(groundProps), new Vector3(s * Rand(5.4f, 9f), 0f, Rand(-3.5f, 3.5f)), Rand(0f, 360f), Rand(2.5f, 3.5f));
            if (rng.NextDouble() < 0.5)
            {
                var m = Prop(seg, Pick(mushrooms), new Vector3(s * Rand(5.2f, 7.5f), 0f, Rand(-3.5f, 3.5f)), Rand(0f, 360f), Rand(3f, 4.5f));
                if (m != null)
                {
                    swells.Add(new Swell { t = m, rest = m.localScale, amount = 0.15f });
                    var gl = GlowQuad(seg, "MushroomGlow", flareMask, new Color(1f, 0.45f, 0.35f), false);
                    gl.transform.localPosition = m.localPosition + new Vector3(0f, 0.6f, 0f);
                    gl.transform.localScale = Vector3.one * 1.6f;
                    glows.Add(new Glow { r = gl, color = new Color(1f, 0.5f, 0.35f), baseI = 0.2f, kickI = 0.55f, face = true });
                }
            }

            // Sun shafts slanting down through the crowns.
            if (rayMasks != null && rayMasks.Length > 0 && (index + (s > 0 ? 1 : 0)) % 2 == 0)
            {
                var q = GlowQuad(seg, "SunShaft", rayMasks[rng.Next(rayMasks.Length)], sunlight, false);
                q.transform.localPosition = new Vector3(s * Rand(4.5f, 8f), 6f, Rand(-3f, 3f));
                q.transform.localRotation = Quaternion.Euler(0f, 0f, s * Rand(12f, 22f));
                q.transform.localScale = new Vector3(Rand(2.5f, 4f), 13f, 1f);
                rays.Add(new Ray { r = q, strength = Rand(0.5f, 1f), index = rays.Count });
            }

            // The leaf carpet along the road's edge.
            for (int k = 0; k < 7; k++)
            {
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(leaf.GetComponent<Collider>());
                leaf.name = "FallenLeaf";
                leaf.transform.SetParent(seg, false);
                leaf.transform.localPosition = new Vector3(s * Rand(2.6f, 5.5f), 0.015f, Rand(-4f, 4f));
                leaf.transform.localRotation = Quaternion.Euler(90f, Rand(0f, 360f), 0f);
                leaf.transform.localScale = Vector3.one * Rand(0.35f, 0.6f);
                var r = leaf.GetComponent<MeshRenderer>();
                r.sharedMaterial = carpetMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                mpb.Clear();
                mpb.SetColor("_BaseColor", leafColors[rng.Next(leafColors.Length)] * 0.8f);
                r.SetPropertyBlock(mpb);
            }

            // Floating motes of light, and golden haze in the undergrowth.
            if (rng.NextDouble() < 0.6)
            {
                var mote = GlowQuad(seg, "Mote", flareMask, sunlight, false);
                mote.transform.localPosition = new Vector3(s * Rand(4f, 7f), Rand(1.2f, 3.5f), Rand(-3.5f, 3.5f));
                mote.transform.localScale = Vector3.one * 0.35f;
                glows.Add(new Glow { r = mote, color = sunlight, baseI = 0.35f, kickI = 0.9f, face = true });
            }
            for (int k = 0; k < fogPerSegment; k++)
            {
                var sm = GlowQuad(seg, "GroundFog", smokeTexture, Color.Lerp(sunlight, moonBlue, 0.3f), false);
                float y = 0.6f + (float)rng.NextDouble() * 0.8f;
                sm.transform.localPosition = new Vector3(s * Rand(5f, 14f), y, Rand(-4f, 4f));
                sm.transform.localScale = new Vector3(Rand(6f, 9f), Rand(2.2f, 3.2f), 1f);
                fogs.Add(new Fog { t = sm.transform, r = sm, seed = index * 1.7f + k * 3.1f + s, y = y });
            }
        }
    }

    /// Recolours a tree's or bush's leaves to one of the autumn colours - one per instance, so the
    /// wood is gold, amber and crimson in patches - with a faint glow that kicks on the beat.
    private void Autumn(Transform t)
    {
        int pick = rng.Next(leafColors.Length);
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                string n = m.name.ToLowerInvariant();
                if (!(n.Contains("leaf") || n.Contains("grass"))) continue;
                if (!leafVariants.TryGetValue(m, out var variants))
                {
                    variants = new Material[leafColors.Length];
                    for (int v = 0; v < variants.Length; v++)
                    {
                        var copy = new Material(m);
                        copy.SetColor("_BaseColor", leafColors[v]);
                        copy.EnableKeyword("_EMISSION");
                        Color e = leafColors[v] * leafGlow;
                        copy.SetColor("_EmissionColor", e);
                        RegisterEmissive(copy, e);
                        variants[v] = copy;
                    }
                    leafVariants[m] = variants;
                }
                mats[i] = variants[pick];
            }
            r.sharedMaterials = mats;
        }
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (cam == null) return;
        if (falling == null && tunnelLength > 0f) falling = FallingLeaves();
        Vector3 camPos = cam.transform.position;
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float drop = Mathf.Clamp01(energy - 1f) * 1.6f;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        // Sun shafts: flare on beats 1 and 3, sway slowly.
        float strong = Mathf.Pow(1f - Mathf.Repeat(beats, 2f) / 2f, 3f);
        foreach (var r in rays)
        {
            var e = r.r.transform.eulerAngles;
            SetGlow(r.r, null, r.strength * (0.12f + 0.3f * strong * energy + 0.15f * kick * drop));
        }
        int beat = ClubBeat.BeatIndex;
        if (beat != lastBurstBeat)
        {
            lastBurstBeat = beat;
            if (falling != null && energy > 0.8f) falling.Emit(drop > 0.1f ? 60 : 12);
        }
    }

    private ParticleSystem FallingLeaves()
    {
        var go = new GameObject("FallingLeaves");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 11f, tunnelLength * 0.4f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0.08f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 1500;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(leafColors[0], 0f), new GradientColorKey(leafColors[1], 0.5f), new GradientColorKey(leafColors[2], 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(grad) { mode = ParticleSystemGradientMode.RandomColor };
        var em = ps.emission;
        em.rateOverTime = 70f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(22f, 1f, tunnelLength);
        // Drift down along the road toward the camera, fluttering.
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        vel.y = new ParticleSystem.MinMaxCurve(-1.2f, -0.6f);
        vel.z = new ParticleSystem.MinMaxCurve(-scrollSpeed * 0.8f, -scrollSpeed * 0.6f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.8f;
        noise.frequency = 0.5f;
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        m.SetTexture("_BaseMap", leafTex);
        m.SetFloat("_Surface", 1f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.renderQueue = (int)RenderQueue.Transparent;
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        foreach (var v in leafVariants.Values) foreach (var m in v) if (m != null) Destroy(m);
        if (carpetMat != null) Destroy(carpetMat);
        if (leafTex != null) Destroy(leafTex);
    }
}
