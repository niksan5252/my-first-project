using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_Aurora's environment, "Feelings Fade": a road across a frozen waste at night.
///
///   * the sky (the theme's skybox, Tunnel/AuroraSky): a cold dark-blue night, stars, and curtains
///     of aurora - emerald at the lower edge, violet-pink at the top - over the far end of the road;
///   * the road as dark clear ice and a snowfield either side (Tunnel/AuroraIce), both mirroring the
///     same aurora, the ice with pale cracks frozen in, travelling with the road;
///   * clusters of ice crystals along both sides (procedural faceted prisms, Tunnel/AuroraCrystal),
///     lit from inside in emerald, violet or pale ice; they glow up softly on every kick, the glow
///     rolling in from the far end of the road towards the camera;
///   * light snow drifting down and streaming past with the road (Kenney particle-pack dot); its
///     swirl grows with the energy and whirls up through each drop;
///
/// A softer, more lyrical tunnel than the others: everything eases in and out (sine curves), nothing
/// strobes. Animation through SongTweens - DOTween tweens built paused, durations in beats,
/// positioned every frame from ClubBeat's song time - and scaled by ClubBeat.Energy.
public class AuroraEnvironmentDecorator : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [Header("Materials")]
    [SerializeField] private Material snowMaterial;
    [SerializeField] private Shader crystalShader;
    [SerializeField] private Texture2D snowflakeTexture;

    [Header("Aurora")]
    [SerializeField] private Color auroraLow = new Color(0.15f, 1f, 0.55f);
    [SerializeField] private Color auroraHigh = new Color(0.75f, 0.3f, 1f);
    [SerializeField] private float auroraDrift = 0.22f;      // phase per second of song
    [SerializeField] private float auroraSway = 0.35f;       // phase swing over two beats
    [SerializeField] private float auroraBaseGain = 0.6f;
    [SerializeField] private float auroraBeatGain = 0.22f;

    [Header("Crystals")]
    [SerializeField] private Vector2 crystalX = new Vector2(5.2f, 13f);
    [SerializeField] private float crystalSpacing = 6.5f;
    [SerializeField] private Color iceBlue = new Color(0.55f, 0.8f, 1f);
    [SerializeField] private float crystalBase = 0.45f;
    [SerializeField] private float crystalKick = 0.6f;
    [SerializeField] private float behindCamera = 12f;

    [Header("Snowfall")]
    [SerializeField] private float snowRate = 320f;
    [SerializeField] private float swirlCalm = 0.25f;
    [SerializeField] private float swirlDrop = 1.6f;

    [Header("Snowfield")]
    [SerializeField] private Vector2 snowSize = new Vector2(320f, 380f);
    [SerializeField] private float snowY = -0.08f;

    private Color primary = new Color(1f, 0.42f, 0.7f);
    private Color secondary = new Color(0.45f, 1f, 0.4f);
    private float emission = 1.2f;

    private bool built;
    private float scrollSpeed, tunnelLength;
    private Material sky, savedSky;
    private readonly SongTweens tweens = new SongTweens();

    // Written by the tweens, applied in LateUpdate.
    private float sway;          // -1..1 over two beats
    private float breath;        // 1 on the beat, easing away
    private float barSwell;      // 0..1..0 over a bar
    private float dropGlow;      // extra light through a drop
    private Tween dropTween;

    private class Cluster { public Transform t; public MeshRenderer r; public Color color; public float z; public float kick; }
    private readonly List<Cluster> clusters = new List<Cluster>();
    private float span;
    private ParticleSystem snow;
    private MaterialPropertyBlock mpb;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int GlowId = Shader.PropertyToID("_Glow");

    private static readonly int HorizonId = Shader.PropertyToID("_HorizonColor");
    private static readonly int FadeColorId = Shader.PropertyToID("_FadeColor");
    private static readonly int StarTimeId = Shader.PropertyToID("_StarTime");
    private static readonly int ScrollSpeedId = Shader.PropertyToID("_SynthScrollSpeed");
    private static readonly int PhaseId = Shader.PropertyToID("_AuroraPhase");
    private static readonly int WaveId = Shader.PropertyToID("_AuroraWave");
    private static readonly int GainId = Shader.PropertyToID("_AuroraGain");
    private static readonly int LowId = Shader.PropertyToID("_AuroraLow");
    private static readonly int HighId = Shader.PropertyToID("_AuroraHigh");

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

        savedSky = RenderSettings.skybox;
        if (savedSky != null && savedSky.shader.name == "Tunnel/AuroraSky")
        {
            sky = new Material(savedSky) { name = savedSky.name + " (live)" };
            sky.SetColor(HorizonId, RenderSettings.fogColor);
            RenderSettings.skybox = sky;
        }
        Shader.SetGlobalFloat(ScrollSpeedId, scrollSpeed);
        Shader.SetGlobalColor(LowId, auroraLow);
        Shader.SetGlobalColor(HighId, auroraHigh);
        Shader.SetGlobalFloat(GainId, auroraBaseGain);
        Shader.SetGlobalFloat(WaveId, 0.6f);

        mpb = new MaterialPropertyBlock();
        span = tunnelLength + behindCamera + 10f;
        BuildSnowfield();
        BuildCrystals();
        BuildSnowfall();
        BuildTweens();
    }

    private void BuildSnowfield()
    {
        if (snowMaterial == null) return;
        var field = GameObject.CreatePrimitive(PrimitiveType.Plane);
        Destroy(field.GetComponent<Collider>());
        field.name = "Snowfield";
        field.transform.SetParent(transform, false);
        field.transform.localPosition = new Vector3(0f, snowY, snowSize.y * 0.5f - 30f);
        field.transform.localScale = new Vector3(snowSize.x / 10f, 1f, snowSize.y / 10f);
        var r = field.GetComponent<MeshRenderer>();
        var m = new Material(snowMaterial);
        m.SetColor(FadeColorId, RenderSettings.fogColor);
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    private void BuildCrystals()
    {
        if (crystalShader == null) return;
        var rnd = new System.Random(13);
        var mat = new Material(crystalShader) { name = "IceCrystal" };
        var stage = new GameObject("Crystals").transform;
        stage.SetParent(transform, false);
        var meshes = new Mesh[5];
        for (int v = 0; v < meshes.Length; v++) meshes[v] = ClusterMesh(rnd);
        Color[] tints = { auroraLow, auroraHigh, iceBlue };
        int n = 0;
        for (int side = -1; side <= 1; side += 2)
            for (float z = 1f; z < span - behindCamera; z += crystalSpacing * (0.6f + 0.8f * (float)rnd.NextDouble()))
            {
                var go = new GameObject("CrystalCluster");
                go.transform.SetParent(stage, false);
                float x = side * Mathf.Lerp(crystalX.x, crystalX.y, (float)rnd.NextDouble());
                go.transform.localPosition = new Vector3(x, 0f, z);
                go.transform.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                // Bigger further from the road.
                float scale = Mathf.Lerp(0.8f, 1.9f, Mathf.InverseLerp(crystalX.x, crystalX.y, Mathf.Abs(x))) * (0.8f + 0.4f * (float)rnd.NextDouble());
                go.transform.localScale = Vector3.one * scale;
                go.AddComponent<MeshFilter>().sharedMesh = meshes[rnd.Next(meshes.Length)];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                clusters.Add(new Cluster { t = go.transform, r = r, color = tints[n++ % tints.Length], z = z });
            }
    }

    private void BuildSnowfall()
    {
        if (snowflakeTexture == null) return;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "Snow" };
        mat.SetTexture("_BaseMap", snowflakeTexture);
        mat.SetColor("_BaseColor", new Color(0.85f, 0.92f, 1f, 1f) * 1.3f);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)BlendMode.One);
        mat.SetInt("_DstBlend", (int)BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;

        var go = new GameObject("Snowfall");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 11f, 34f);
        snow = go.AddComponent<ParticleSystem>();
        snow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = snow.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.17f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 3000;
        main.prewarm = true;
        var emissionModule = snow.emission;
        emissionModule.rateOverTime = snowRate;
        var shape = snow.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(36f, 1f, 70f);
        var vel = snow.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        vel.y = new ParticleSystem.MinMaxCurve(-1.2f, -2.2f);
        vel.z = new ParticleSystem.MinMaxCurve(-scrollSpeed * 0.9f, -scrollSpeed * 0.9f);
        var noise = snow.noise;
        noise.enabled = true;
        noise.frequency = 0.35f;
        noise.scrollSpeed = 0.4f;
        noise.strength = swirlCalm;
        var col = snow.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        snow.Play();
    }

    /// A cluster of three to six hexagonal crystals with pointed tips, leaning outward from a
    /// common root. Flat-shaded (every face has its own vertices) so each facet catches the inner
    /// light differently.
    private static Mesh ClusterMesh(System.Random rnd)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        void Face(Vector3 a, Vector3 b, Vector3 c)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }
        int count = 3 + rnd.Next(4);
        for (int k = 0; k < count; k++)
        {
            float height = k == 0 ? 3.2f + 1.6f * (float)rnd.NextDouble() : 1.2f + 2.2f * (float)rnd.NextDouble();
            float radius = height * (0.12f + 0.05f * (float)rnd.NextDouble());
            var tilt = Quaternion.Euler((float)rnd.NextDouble() * 50f - 25f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 50f - 25f);
            if (k == 0) tilt = Quaternion.Euler((float)rnd.NextDouble() * 12f - 6f, 0f, (float)rnd.NextDouble() * 12f - 6f);
            var root = new Vector3((float)rnd.NextDouble() - 0.5f, -0.2f, (float)rnd.NextDouble() - 0.5f) * 0.6f;
            float shaft = height * 0.78f;
            var ring0 = new Vector3[6];
            var ring1 = new Vector3[6];
            for (int s = 0; s < 6; s++)
            {
                float ang = s * Mathf.PI / 3f;
                var o = new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius);
                ring0[s] = root + tilt * o;
                ring1[s] = root + tilt * (o * 0.92f + Vector3.up * shaft);
            }
            var tip = root + tilt * (Vector3.up * height);
            for (int s = 0; s < 6; s++)
            {
                int s2 = (s + 1) % 6;
                Face(ring0[s], ring1[s], ring0[s2]);
                Face(ring0[s2], ring1[s], ring1[s2]);
                Face(ring1[s], tip, ring1[s2]);
            }
        }
        var m = new Mesh { name = "CrystalCluster" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    private void BuildTweens()
    {
        // The curtains sway to and fro across every two beats, breathe on each beat and fold
        // deeper with the bar - all on sine eases, soft rather than punchy.
        tweens.Loop(DOTween.Sequence()
            .Append(DOTween.To(x => sway = x, -1f, 1f, 1f).SetEase(Ease.InOutSine))
            .Append(DOTween.To(x => sway = x, 1f, -1f, 1f).SetEase(Ease.InOutSine)), 2f);
        tweens.Loop(DOTween.Sequence()
            .Append(DOTween.To(x => breath = x, 0f, 1f, 0.15f).SetEase(Ease.OutSine))
            .Append(DOTween.To(x => breath = x, 1f, 0f, 0.85f).SetEase(Ease.InOutSine)), 1f);
        tweens.Loop(DOTween.Sequence()
            .Append(DOTween.To(x => barSwell = x, 0f, 1f, 2f).SetEase(Ease.InOutSine))
            .Append(DOTween.To(x => barSwell = x, 1f, 0f, 2f).SetEase(Ease.InOutSine)), 4f);
        // Crystals: a soft glow on each kick - a quick rise, a slow fade - arriving a little later
        // the nearer a cluster starts to the camera, so the light rolls in along the road.
        foreach (var c in clusters)
        {
            var cl = c;
            float delay = 0.35f * (1f - Mathf.Clamp01(cl.z / span));
            tweens.Loop(DOTween.Sequence()
                .Append(DOTween.To(x => cl.kick = x, 0f, 1f, 0.12f).SetEase(Ease.OutSine))
                .Append(DOTween.To(x => cl.kick = x, 1f, 0f, 0.88f).SetEase(Ease.InOutSine)), 1f, delay);
        }
        dropTween = tweens.Once(DOTween.Sequence()
            .Append(DOTween.To(x => dropGlow = x, 0f, 1f, 2f).SetEase(Ease.OutSine))
            .Append(DOTween.To(x => dropGlow = x, 1f, 0f, 14f).SetEase(Ease.InOutSine)));
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
        float sinceDrop = ClubBeat.BeatsSinceDrop;
        SongTweens.Seek(dropTween, float.IsInfinity(sinceDrop) ? -1f : sinceDrop);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.6f);
        float e = Mathf.Clamp01(energy);
        float t = ClubBeat.SongTime;

        Shader.SetGlobalFloat(PhaseId, t * auroraDrift + auroraSway * sway * (0.4f + 0.6f * e));
        Shader.SetGlobalFloat(WaveId, 0.5f + 0.5f * barSwell * e + 0.4f * dropGlow);
        Shader.SetGlobalFloat(GainId, auroraBaseGain * (0.7f + 0.3f * e) + auroraBeatGain * breath * e + 0.35f * dropGlow);

        if (sky != null) sky.SetFloat(StarTimeId, t);

        if (snow != null)
        {
            var noise = snow.noise;
            noise.strength = swirlCalm + 0.35f * e * barSwell + swirlDrop * dropGlow;
        }

        float travel = t * scrollSpeed;
        foreach (var c in clusters)
        {
            var pos = c.t.localPosition;
            pos.z = Mathf.Repeat(c.z - travel + behindCamera, span) - behindCamera;
            c.t.localPosition = pos;
            c.r.GetPropertyBlock(mpb);
            mpb.SetColor(ColorId, c.color);
            mpb.SetFloat(GlowId, crystalBase * (0.7f + 0.3f * e) + crystalKick * c.kick * e + 0.25f * dropGlow);
            c.r.SetPropertyBlock(mpb);
        }
    }
}
