using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_Phonk's environment, "Night Drift": an underground road at night.
///
///   * concrete side walls, ceiling beams and bridge pillars (Kenney city-kit-roads) - the underpass;
///   * street lamps (Kenney light-curved) with a glow at the head, a cone of light and a long
///     reflection streak on the wet road beneath them;
///   * construction barriers (Kenney) along the road edge with warning lights on top;
///   * low smoke drifting over the ground (Brackeys smoke puff), flaring red on a drop;
///   * sparks thrown up from the road edge on the beat while the room is loud;
///   * speed lines streaking past, more of them the harder the track goes.
///
/// Timing from ClubBeat (added here if the scene has none): lamps kick on the beat, the warning
/// lights blink on a 3-3-2 pattern over each pair of beats - the tresillo phonk cowbells ride on -
/// and everything is scaled by the room's energy, planned from the chart. Additive lights through
/// ClubGlow, all capped; props are the kit's own lit materials, darkened for the night.
public class PhonkEnvironmentDecorator : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    private const float MaxIntensity = 2.2f;

    [Header("Shaders and textures")]
    [SerializeField] private Shader glowShader;
    [SerializeField] private Texture2D flareMask;
    [SerializeField] private Texture2D coneMask;
    [SerializeField] private Texture2D smokeTexture;

    [Header("Kenney props")]
    [SerializeField] private GameObject lampModel;
    [SerializeField] private GameObject pillarModel;
    [SerializeField] private GameObject barrierModel;
    [SerializeField] private GameObject warningLightModel;

    [Header("Layout")]
    [SerializeField] private float segmentSpacing = 12f;
    [SerializeField] private float wallX = 10.5f;
    [SerializeField] private float ceilingY = 7.4f;
    [SerializeField] private float lampX = 5.4f;
    [SerializeField] private float pillarX = 7.2f;
    [SerializeField] private float barrierX = 3.75f;
    [SerializeField] private float behindCamera = 20f;
    [SerializeField] private int speedLines = 48;

    private Color primary = new Color(0.92f, 0.04f, 0.1f);
    private Color secondary = new Color(0.72f, 0.8f, 1f);
    private float emission = 1.3f;

    private float scrollSpeed, span;
    private float savedReflection = 1f;
    private bool built;
    private Camera cam;
    private Material glowTemplate, concreteMat;
    private Texture2D streakTex;
    private MaterialPropertyBlock mpb;

    private struct Lamp { public MeshRenderer head, cone, streak; public int index; }
    private struct Blinker { public MeshRenderer r; public int index; }
    private struct Smoke { public Transform t; public MeshRenderer r; public float seed; }
    private struct Line { public Transform t; public MeshRenderer r; public float speed; }

    private readonly List<Transform> segments = new List<Transform>();
    private readonly List<Lamp> lamps = new List<Lamp>();
    private readonly List<Blinker> blinkers = new List<Blinker>();
    private readonly List<Smoke> smokes = new List<Smoke>();
    private readonly List<Line> lines = new List<Line>();
    private readonly List<MeshRenderer> wallLamps = new List<MeshRenderer>();
    private ParticleSystem[] sparks;
    private int lastSparkBeat = int.MinValue;
    private System.Random rng = new System.Random(7);

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

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
        if (FindAnyObjectByType<ClubBeat>() == null) gameObject.AddComponent<ClubBeat>();
        mpb = new MaterialPropertyBlock();
        cam = Camera.main;
        // Underground and at night: the environment's grey sky must not show in the wet road, or
        // the road reads as pale concrete. Put back when this environment goes.
        savedReflection = RenderSettings.reflectionIntensity;
        RenderSettings.reflectionIntensity = 0.15f;
        glowTemplate = new Material(glowShader != null ? glowShader : Shader.Find("Tunnel/ClubGlow"));
        concreteMat = Lit(new Color(0.05f, 0.05f, 0.06f), 0.25f);
        streakTex = StreakTexture();

        var stage = new GameObject("PhonkStage").transform;
        stage.SetParent(transform, false);
        int count = Mathf.CeilToInt((tunnelLength + behindCamera + segmentSpacing * 2f) / segmentSpacing);
        span = count * segmentSpacing;
        for (int i = 0; i < count; i++) BuildSegment(stage, i, -behindCamera + i * segmentSpacing);

        // Wet asphalt beyond the road: dark and smooth, so the lamps' streaks sit on something.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        Destroy(ground.GetComponent<Collider>());
        ground.name = "WetGround";
        ground.transform.SetParent(transform, false);
        ground.transform.localPosition = new Vector3(0f, -0.03f, tunnelLength * 0.5f);
        ground.transform.localScale = new Vector3(2.2f, 1f, (tunnelLength + 80f) / 10f);
        var gm = Lit(new Color(0.012f, 0.012f, 0.016f), 0.55f);
        gm.SetFloat("_EnvironmentReflections", 0f);
        gm.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        ground.GetComponent<MeshRenderer>().sharedMaterial = gm;

        for (int i = 0; i < speedLines; i++)
        {
            var q = GlowQuad(transform, "SpeedLine", streakTex, secondary);
            lines.Add(new Line { t = q.transform, r = q, speed = 2.2f + (float)rng.NextDouble() * 1.6f });
            ResetLine(lines[i].t, true, tunnelLength);
        }

        sparks = new[] { SparkSystem(-1), SparkSystem(1) };
    }

    private void BuildSegment(Transform stage, int index, float z)
    {
        var seg = new GameObject("Segment_" + index.ToString("00")).transform;
        seg.SetParent(stage, false);
        seg.localPosition = new Vector3(0f, 0f, z);
        segments.Add(seg);

        // Ceiling beam across the whole underpass.
        Box(seg, "CeilingBeam", new Vector3(0f, ceilingY, 0f), new Vector3(wallX * 2f + 0.6f, 0.7f, 1.4f), concreteMat);
        for (int s = -1; s <= 1; s += 2)
        {
            Box(seg, "Wall", new Vector3(s * wallX, ceilingY * 0.5f, 0f), new Vector3(0.6f, ceilingY + 0.2f, segmentSpacing), concreteMat);

            // Industrial lamp on the wall under the beam.
            var wl = GlowQuad(seg, "WallLamp", flareMask, secondary);
            wl.transform.localPosition = new Vector3(s * (wallX - 0.35f), ceilingY - 1.1f, 0f);
            wl.transform.localRotation = Quaternion.Euler(0f, s > 0 ? 90f : -90f, 0f);
            wl.transform.localScale = new Vector3(2.4f, 1.2f, 1f);
            wallLamps.Add(wl);

            Prop(seg, pillarModel, new Vector3(s * pillarX, 0f, 0f), 0f, new Vector3(12f, ceilingY / 0.5f, 12f), 0.35f);

            // Street lamp half a segment on, its arm reaching over the road.
            float lz = segmentSpacing * 0.5f;
            Prop(seg, lampModel, new Vector3(s * lampX, 0f, lz), s > 0 ? -90f : 90f, Vector3.one * 9f, 0.4f);
            var headPos = new Vector3(s * (lampX - 1.7f), 5.85f, lz);
            var head = GlowQuad(seg, "LampGlow", flareMask, index % 2 == 0 ? secondary : primary);
            head.transform.localPosition = headPos;
            head.transform.localScale = Vector3.one * 0.95f;
            var cone = GlowQuad(seg, "LampCone", coneMask, index % 2 == 0 ? secondary : primary);
            cone.transform.localPosition = new Vector3(headPos.x, headPos.y * 0.5f - 0.1f, lz);
            cone.transform.localScale = new Vector3(3.4f, headPos.y, 1f);
            cone.sharedMaterial.SetTextureScale("_MainTex", new Vector2(1f, -1f));
            cone.sharedMaterial.SetTextureOffset("_MainTex", new Vector2(0f, 1f));
            // The reflection on the wet road: a streak from under the lamp toward the viewer.
            var streak = GlowQuad(seg, "WetStreak", streakTex, index % 2 == 0 ? secondary : primary);
            streak.transform.localPosition = new Vector3(headPos.x, 0.02f, lz - 3.2f);
            streak.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            streak.transform.localScale = new Vector3(0.9f, 7f, 1f);
            // Bright end at the lamp: the texture is bright at v = 0, so flip it along the road.
            streak.sharedMaterial.SetTextureScale("_MainTex", new Vector2(1f, -1f));
            streak.sharedMaterial.SetTextureOffset("_MainTex", new Vector2(0f, 1f));
            lamps.Add(new Lamp { head = head, cone = cone, streak = streak, index = lamps.Count });

            // Barrier at the road edge with a warning light on it.
            Prop(seg, barrierModel, new Vector3(s * barrierX, 0f, 2f), 90f, Vector3.one * 7f, 0.45f);
            Prop(seg, warningLightModel, new Vector3(s * barrierX, 0.85f, 2f), 0f, Vector3.one * 5f, 0.5f);
            var wlight = GlowQuad(seg, "WarningLight", flareMask, primary);
            wlight.transform.localPosition = new Vector3(s * barrierX, 1.85f, 2f);
            wlight.transform.localScale = Vector3.one * 0.9f;
            blinkers.Add(new Blinker { r = wlight, index = blinkers.Count });

            // Low smoke over the ground beside the road.
            for (int k = 0; k < 2; k++)
            {
                var sm = GlowQuad(seg, "Smoke", smokeTexture, primary);
                sm.sharedMaterial.SetFloat("_UseAlpha", 1f);
                sm.transform.localPosition = new Vector3(s * (4.8f + k * 2.2f), 0.9f + k * 0.4f, k * 6f);
                sm.transform.localScale = Vector3.one * (4.5f + k);
                smokes.Add(new Smoke { t = sm.transform, r = sm, seed = index * 3.1f + k + s });
            }
        }
    }

    private void OnDestroy()
    {
        if (built) RenderSettings.reflectionIntensity = savedReflection;
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
        float energy = ClubBeat.Energy;
        foreach (var l in lines)
        {
            var p = l.t.localPosition;
            p.z -= delta * l.speed;
            l.t.localPosition = p;
            if (p.z < -6f) ResetLine(l.t, false, span - behindCamera);
        }
    }

    private void LateUpdate()
    {
        if (!built) return;
        if (cam == null) cam = Camera.main;
        Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
        float energy = ClubBeat.Energy;
        float pulse = ClubBeat.Pulse;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        bool drop = energy > 1.05f;

        // Lamps: a steady glow, a kick on every beat.
        foreach (var l in lamps)
        {
            Face(l.head.transform, camPos);
            float k = 0.55f + 0.45f * pulse * Mathf.Clamp01(energy);
            SetGlow(l.head, 1.2f + 0.8f * k);
            SetGlow(l.cone, 0.22f + 0.25f * k * energy);
            SetGlow(l.streak, 0.35f + 0.5f * k * energy);
        }

        // Warning lights on a 3-3-2 pattern across each pair of beats (eighth-note steps 0, 3, 6).
        int step = Mathf.FloorToInt(beats * 4f) % 8;
        bool tresillo = step == 0 || step == 3 || step == 6;
        float stepPhase = beats * 4f - Mathf.Floor(beats * 4f);
        foreach (var b in blinkers)
        {
            Face(b.r.transform, camPos);
            bool on = energy > 0.7f ? tresillo && stepPhase < 0.6f : step == 0 && stepPhase < 0.8f;
            SetGlow(b.r, on ? Mathf.Min(MaxIntensity, 0.8f + 0.9f * energy) : 0.05f);
        }

        // Wall lamps: dim hum, flicker with the kick.
        foreach (var w in wallLamps) SetGlow(w, 0.3f + 0.5f * pulse * energy);

        // Smoke: a faint dark-red haze, flaring on the beat when the track drops.
        foreach (var s in smokes)
        {
            Face(s.t, camPos);
            float drift = Mathf.Sin(ClubBeat.SongTime * 0.3f + s.seed) * 0.6f;
            var p = s.t.localPosition; p.y = 0.9f + drift * 0.3f; s.t.localPosition = p;
            float flare = drop ? pulse * (energy - 1f) * 1.6f : 0f;
            SetGlow(s.r, 0.08f + 0.06f * energy + flare);
        }

        // Speed lines: more of them, brighter, as the energy rises.
        int shown = Mathf.RoundToInt(lines.Count * Mathf.Clamp01((energy - 0.35f) / 0.9f));
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            AlongZ(l.t, camPos);
            SetGlow(l.r, i < shown ? 0.35f + 0.35f * energy : 0f);
        }

        // Sparks: a burst from the road edge on each beat of a drop, on every other beat while loud.
        int beat = ClubBeat.BeatIndex;
        if (beat != lastSparkBeat)
        {
            lastSparkBeat = beat;
            if (drop || (energy > 0.8f && (beat & 1) == 0))
                foreach (var ps in sparks) ps.Emit(drop ? 28 : 12);
        }
    }

    // --- building helpers ---

    private void ResetLine(Transform t, bool anywhere, float farZ)
    {
        float side = rng.NextDouble() < 0.5 ? -1f : 1f;
        float x = side * (3.4f + (float)rng.NextDouble() * 5f);
        float y = 0.6f + (float)rng.NextDouble() * 5.5f;
        float z = anywhere ? (float)rng.NextDouble() * farZ : farZ * (0.6f + 0.4f * (float)rng.NextDouble());
        t.localPosition = new Vector3(x, y, z);
        t.localScale = new Vector3(0.035f, 2.5f + (float)rng.NextDouble() * 3f, 1f);
    }

    private ParticleSystem SparkSystem(int side)
    {
        var go = new GameObject("Sparks");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(side * barrierX, 0.3f, 16f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        main.startColor = new ParticleSystem.MinMaxGradient(primary, Color.Lerp(secondary, primary, 0.3f));
        main.gravityModifier = 1.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;
        var emissionModule = ps.emission;
        emissionModule.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 28f;
        shape.radius = 0.2f;
        go.transform.localRotation = Quaternion.Euler(-60f, side > 0 ? -35f : 35f, 0f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        mat.SetColor("_BaseColor", Color.white * 1.6f);
        r.material = mat;
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.06f;
        r.lengthScale = 2f;
        r.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    private void Prop(Transform parent, GameObject model, Vector3 pos, float yaw, Vector3 scale, float tint)
    {
        if (model == null) return;
        var go = Instantiate(model, parent);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = scale;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            // Darkened for the night: the kit's colormap, multiplied down.
            foreach (var m in r.materials)
            {
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(tint, tint, tint * 1.05f, 1f));
                if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(tint, tint, tint * 1.05f, 1f));
            }
        }
    }

    private MeshRenderer GlowQuad(Transform parent, string name, Texture2D mask, Color color)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = name;
        q.transform.SetParent(parent, false);
        var r = q.GetComponent<MeshRenderer>();
        var m = new Material(glowTemplate);
        if (mask != null) m.SetTexture("_MainTex", mask);
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        mpb.Clear();
        mpb.SetColor(ColorId, color);
        mpb.SetFloat(IntensityId, 0f);
        r.SetPropertyBlock(mpb);
        return r;
    }

    private void SetGlow(MeshRenderer r, float intensity)
    {
        r.GetPropertyBlock(mpb);
        mpb.SetFloat(IntensityId, Mathf.Clamp(intensity, 0f, MaxIntensity));
        r.SetPropertyBlock(mpb);
        r.enabled = intensity > 0.001f;
    }

    private static void Face(Transform t, Vector3 camPos) => t.rotation = Quaternion.LookRotation(t.position - camPos);

    /// A speed line lies along the road and turns about that axis to face the camera.
    private static void AlongZ(Transform t, Vector3 camPos)
    {
        Vector3 toCam = camPos - t.position;
        Vector3 side = Vector3.Cross(Vector3.forward, toCam).normalized;
        Vector3 normal = Vector3.Cross(side, Vector3.forward);
        t.rotation = Quaternion.LookRotation(-normal, Vector3.forward);
    }

    private static Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(b.GetComponent<Collider>());
        b.name = name;
        b.transform.SetParent(parent, false);
        b.transform.localPosition = pos;
        b.transform.localScale = size;
        var r = b.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return b.transform;
    }

    private static Material Lit(Color c, float smoothness)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", 0f);
        return m;
    }

    /// A light streak: bright at one end (v = 0), fading along its length, soft across.
    private static Texture2D StreakTexture()
    {
        const int w = 32, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h;
                float a = Mathf.Exp(-u * u * 10f) * Mathf.Pow(1f - v, 1.6f);
                byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                px[y * w + x] = new Color32(b, b, b, b);
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }
}
