using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_Club's environment: a festival stage running along both sides of the road.
///
///   * LED walls - big screens facing the road, their diodes animated by ClubLedWall.shader
///     (patterns change every bar, flash on every beat, a Kenney pattern-pack tile as one of them);
///   * speaker stacks between the walls, their cones (a Kenney light-mask ring) pumping on the kick;
///   * lasers fanning up from the stacks across the tunnel, sweeping and switching colour per bar,
///     only really on while the room is loud;
///   * spotlights leaning in from the top of the walls (Kenney cone mask), slowly sweeping;
///   * strobes on the wall corners (Kenney circle mask) that fire on the beat when the room is loud.
///
/// Everything takes its timing from ClubBeat (added here), so the room moves on the chart's own
/// beats and harder on the drops. The stage scrolls with the tunnel at its speed and recycles,
/// like the cyberpunk street; the lights are all additive and capped, so nothing washes to white.
public class ClubEnvironmentDecorator : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    private const float MaxIntensity = 2.2f;

    [Header("Shaders (referenced so builds keep them)")]
    [SerializeField] private Shader ledShader;
    [SerializeField] private Shader glowShader;

    [Header("Masks and patterns (Kenney packs)")]
    [SerializeField] private Texture2D coneMask;
    [SerializeField] private Texture2D flareMask;
    [SerializeField] private Texture2D speakerMask;
    [SerializeField] private Texture2D[] ledPatterns;

    [Header("Layout")]
    [SerializeField] private float segmentSpacing = 12f;
    [SerializeField] private float wallX = 8.6f;
    [SerializeField] private Vector2 wallSize = new Vector2(9f, 6.4f);     // along the road, height
    [SerializeField] private float speakerX = 5.5f;
    [SerializeField] private float behindCamera = 20f;

    [Header("Lights")]
    [SerializeField] private float laserLength = 38f;
    [SerializeField] private float laserWidth = 0.07f;
    [SerializeField] private float spotLength = 11f;
    [SerializeField] private float spotWidth = 4.5f;

    private Color primary = new Color(1f, 0.45f, 0.05f);
    private Color secondary = new Color(0.55f, 0.15f, 1f);
    private float emission = 1.3f;

    private float scrollSpeed;
    private float span;
    private bool built;
    private Transform stage;
    private Material ledMat, cabinetMat, glowTemplate, groundMat;
    private Texture2D beamTex;
    private MaterialPropertyBlock mpb;
    private Camera cam;

    private struct Laser { public Transform t; public MeshRenderer r; public int side; public int index; public Transform emitter; }
    private struct Spot { public Transform t; public MeshRenderer r; public int side; public float seed; public Transform anchor; }
    private struct Flare { public Transform t; public MeshRenderer r; public int index; }
    private struct Cone { public Transform t; public MeshRenderer r; public float size; }

    private readonly List<Transform> segmentsList = new List<Transform>();
    private readonly List<Laser> lasers = new List<Laser>();
    private readonly List<Spot> spots = new List<Spot>();
    private readonly List<Flare> flares = new List<Flare>();
    private readonly List<Cone> cones = new List<Cone>();

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    public void ApplyPalette(Color primaryColor, Color secondaryColor, float emissionIntensity)
    {
        primary = primaryColor;
        secondary = secondaryColor;
        emission = Mathf.Min(emissionIntensity, 1.6f);
    }

    public void Initialize(float tunnelLength, float scrollSpeed)
    {
        if (built) return;
        built = true;
        this.scrollSpeed = scrollSpeed;
        if (GetComponent<ClubBeat>() == null) gameObject.AddComponent<ClubBeat>();
        mpb = new MaterialPropertyBlock();
        cam = Camera.main;

        ledMat = new Material(ledShader != null ? ledShader : Shader.Find("Tunnel/ClubLedWall"));
        ledMat.SetColor("_ColorA", primary);
        ledMat.SetColor("_ColorB", secondary);
        ledMat.SetFloat("_Intensity", Mathf.Min(emission, 1.4f));
        glowTemplate = new Material(glowShader != null ? glowShader : Shader.Find("Tunnel/ClubGlow"));
        cabinetMat = LitMaterial(new Color(0.035f, 0.03f, 0.045f), 0.35f);
        // Matte: a glossy ground mirrors the environment's grey sky and reads as a pale floor.
        groundMat = LitMaterial(new Color(0.012f, 0.009f, 0.018f), 0.1f);
        groundMat.SetFloat("_Metallic", 0f);
        groundMat.SetFloat("_EnvironmentReflections", 0f);
        groundMat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        beamTex = BeamTexture();

        stage = new GameObject("ClubStage").transform;
        stage.SetParent(transform, false);

        int count = Mathf.CeilToInt((tunnelLength + behindCamera + segmentSpacing * 2f) / segmentSpacing);
        span = count * segmentSpacing;
        for (int i = 0; i < count; i++) BuildSegment(i, -behindCamera + i * segmentSpacing);

        // The floor beyond the road, so the stage stands on something.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        Destroy(ground.GetComponent<Collider>());
        ground.name = "ClubGround";
        ground.transform.SetParent(transform, false);
        ground.transform.localPosition = new Vector3(0f, -0.03f, tunnelLength * 0.5f);
        ground.transform.localScale = new Vector3(6f, 1f, (tunnelLength + 80f) / 10f);
        var gr = ground.GetComponent<MeshRenderer>();
        gr.sharedMaterial = groundMat;
        gr.shadowCastingMode = ShadowCastingMode.Off;
    }

    private void BuildSegment(int index, float z)
    {
        var seg = new GameObject("Segment_" + index.ToString("00")).transform;
        seg.SetParent(stage, false);
        seg.localPosition = new Vector3(0f, 0f, z);
        segmentsList.Add(seg);

        for (int s = -1; s <= 1; s += 2)
        {
            // LED wall: the screen facing the road, in a dark frame.
            var frame = Box(seg, "WallFrame", new Vector3(s * (wallX + 0.25f), wallSize.y * 0.5f + 0.4f, 0f),
                            new Vector3(0.4f, wallSize.y + 0.5f, wallSize.x + 0.5f), cabinetMat);
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(screen.GetComponent<Collider>());
            screen.name = "LedWall";
            screen.transform.SetParent(seg, false);
            screen.transform.localPosition = new Vector3(s * wallX, wallSize.y * 0.5f + 0.4f, 0f);
            screen.transform.localRotation = Quaternion.Euler(0f, s > 0 ? 90f : -90f, 0f);
            screen.transform.localScale = new Vector3(wallSize.x, wallSize.y, 1f);
            var sr = screen.GetComponent<MeshRenderer>();
            sr.sharedMaterial = ledMat;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            mpb.Clear();
            mpb.SetFloat("_Seed", index * 2 + (s > 0 ? 1 : 0));
            if (ledPatterns != null && ledPatterns.Length > 0) mpb.SetTexture("_PatternTex", ledPatterns[(index + (s > 0 ? 1 : 0)) % ledPatterns.Length]);
            sr.SetPropertyBlock(mpb);

            // Strobes on the wall's top corners.
            for (int c = -1; c <= 1; c += 2)
            {
                var f = GlowQuad(seg, "Strobe", flareMask, secondary);
                f.transform.localPosition = new Vector3(s * (wallX - 0.3f), wallSize.y + 0.7f, c * wallSize.x * 0.45f);
                f.transform.localScale = Vector3.one * 1.6f;
                flares.Add(new Flare { t = f.transform, r = f, index = flares.Count });
            }

            // Spotlight from the top of the wall, leaning in over the road.
            var anchor = new GameObject("SpotAnchor").transform;
            anchor.SetParent(seg, false);
            anchor.localPosition = new Vector3(s * (wallX - 0.4f), wallSize.y + 0.6f, 0f);
            var spot = GlowQuad(anchor, "Spot", coneMask, primary);
            // Kenney's cones are narrow at the top of the texture; the beam starts at its source.
            spot.sharedMaterial.SetTextureScale("_MainTex", new Vector2(1f, -1f));
            spot.sharedMaterial.SetTextureOffset("_MainTex", new Vector2(0f, 1f));
            spots.Add(new Spot { t = spot.transform, r = spot, side = s, seed = index * 1.7f + s, anchor = anchor });

            // Speaker stack between the walls: a sub and two tops, cones facing the road.
            float sz = segmentSpacing * 0.5f;
            float y = 0f;
            float[] heights = { 1.15f, 0.85f, 0.85f };
            for (int k = 0; k < heights.Length; k++)
            {
                float h = heights[k];
                var cab = Box(seg, "Cabinet", new Vector3(s * speakerX, y + h * 0.5f, sz), new Vector3(0.9f, h * 0.96f, 1.2f), cabinetMat);
                int n = k == 0 ? 1 : 2;
                for (int j = 0; j < n; j++)
                {
                    float coneSize = k == 0 ? 0.95f : 0.55f;
                    var cone = GlowQuad(seg, "SpeakerCone", speakerMask, k == 0 ? primary : secondary);
                    cone.transform.localPosition = new Vector3(s * (speakerX - 0.46f), y + h * 0.5f, sz + (n == 1 ? 0f : (j - 0.5f) * 0.55f));
                    cone.transform.localRotation = Quaternion.Euler(0f, s > 0 ? -90f : 90f, 0f);
                    cone.transform.localScale = Vector3.one * coneSize;
                    cones.Add(new Cone { t = cone.transform, r = cone, size = coneSize });
                }
                y += h;
            }

            // Two lasers from the top of the stack.
            var emitter = new GameObject("LaserEmitter").transform;
            emitter.SetParent(seg, false);
            emitter.localPosition = new Vector3(s * speakerX, y + 0.1f, sz);
            for (int l = 0; l < 2; l++)
            {
                var beam = GlowQuad(emitter, "Laser", beamTex, l == 0 ? secondary : primary);
                lasers.Add(new Laser { t = beam.transform, r = beam, side = s, index = lasers.Count, emitter = emitter });
            }
        }
    }

    private void Update()
    {
        if (!built) return;
        if (scrollSpeed > 0f)
        {
            float delta = scrollSpeed * Time.deltaTime;
            foreach (var seg in segmentsList)
            {
                var p = seg.localPosition;
                p.z -= delta;
                if (p.z < -behindCamera - segmentSpacing) p.z += span;
                seg.localPosition = p;
            }
        }
    }

    private void LateUpdate()
    {
        if (!built) return;
        if (cam == null) cam = Camera.main;
        float energy = ClubBeat.Energy;
        float pulse = ClubBeat.Pulse;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        int bar = ClubBeat.Bar;
        Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;

        // Speaker cones pump on the kick.
        foreach (var c in cones)
        {
            float k = pulse * Mathf.Clamp01(energy);
            c.t.localScale = Vector3.one * c.size * (1f + 0.12f * k);
            SetGlow(c.r, null, emission * (0.35f + 0.9f * k));
        }

        // Lasers: a fan from each stack across the tunnel. They sweep over two bars, the pattern
        // flips every bar, and they only come up once the room is loud - full on a drop.
        float laserOn = Mathf.Clamp01((energy - 0.55f) / 0.45f);
        foreach (var l in lasers)
        {
            bool alt = ((bar + l.index) & 1) == 0;
            float sweep = Mathf.Sin((beats / 8f + l.index * 0.37f) * Mathf.PI * 2f);
            float lean = (l.index % 2 == 0 ? 35f : 60f) + sweep * 22f;       // from vertical, toward the road
            float yaw = (alt ? 1f : -1f) * (15f + 20f * Mathf.Sin(beats * 0.25f + l.index));
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(-l.side * Mathf.Sin(lean * Mathf.Deg2Rad), Mathf.Cos(lean * Mathf.Deg2Rad), 0f);
            // Flicker on the eighths while it is really going.
            float flick = energy > 1.05f && Mathf.Repeat(beats * 2f, 1f) > 0.5f ? 0.55f : 1f;
            AxisBillboard(l.t, l.emitter.position, dir, laserLength, laserWidth * (1f + laserOn), camPos);
            SetGlow(l.r, alt ? secondary : primary, MaxIntensity * 0.8f * laserOn * flick);
        }

        // Spotlights lean in over the road and sweep slowly; brighter with the energy.
        foreach (var sp in spots)
        {
            float a = Mathf.Sin(beats / 16f * Mathf.PI * 2f + sp.seed) * 18f;
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * new Vector3(-sp.side * 0.55f, -0.83f, 0.15f).normalized;
            AxisBillboard(sp.t, sp.anchor.position, dir, spotLength, spotWidth, camPos);
            SetGlow(sp.r, ((bar & 1) == 0) ? primary : secondary, 0.25f + 0.45f * Mathf.Clamp01(energy) + 0.25f * pulse * energy);
        }

        // Strobes fire on the beat when the room is loud (every beat on a drop, every other beat
        // otherwise), a quick flash; in the quiet stretches only on the bar line, dimly.
        int beatIndex = ClubBeat.BeatIndex;
        float flashLen = 0.08f / Mathf.Max(0.05f, ClubBeat.SecondsPerBeat);   // 80 ms, as a fraction of a beat
        foreach (var f in flares)
        {
            f.t.rotation = cam != null ? Quaternion.LookRotation(f.t.position - camPos) : Quaternion.identity;
            bool fire = energy > 1.05f ? ((beatIndex + f.index) & 1) == 0
                      : energy > 0.8f ? ((beatIndex + f.index) & 3) == 0
                      : (beatIndex & 3) == 0 && (f.index & 3) == 0;
            float lit = fire && ClubBeat.BeatPhase < flashLen ? 1f : 0f;
            Color strobe = Color.Lerp(secondary, Color.white, 0.3f);
            SetGlow(f.r, strobe, lit * Mathf.Min(MaxIntensity, 0.6f + 0.9f * energy));
        }
    }

    /// Lays a quad along `dir` from `origin`, turned about its own length to face the camera, so
    /// a flat beam reads as a beam from any angle.
    private static void AxisBillboard(Transform t, Vector3 origin, Vector3 dir, float length, float width, Vector3 camPos)
    {
        Vector3 center = origin + dir * (length * 0.5f);
        Vector3 toCam = (camPos - center);
        Vector3 side = Vector3.Cross(dir, toCam).normalized;
        if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
        Vector3 normal = Vector3.Cross(side, dir);
        t.position = center;
        // Quad: local up = +Y (length), local right = +X (width), faces -Z.
        t.rotation = Quaternion.LookRotation(-normal, dir);
        t.localScale = new Vector3(width, length, 1f);
    }

    private void SetGlow(MeshRenderer r, Color? color, float intensity)
    {
        r.GetPropertyBlock(mpb);
        if (color.HasValue) mpb.SetColor(ColorId, color.Value);
        mpb.SetFloat(IntensityId, Mathf.Clamp(intensity, 0f, MaxIntensity));
        r.SetPropertyBlock(mpb);
        r.enabled = intensity > 0.001f;
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

    private static Material LitMaterial(Color c, float smoothness)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", 0.2f);
        return m;
    }

    /// A laser's cross-section: bright in the middle of the width, soft at the edges, and fading
    /// out toward its far end.
    private static Texture2D BeamTexture()
    {
        const int w = 32, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h;
                float across = Mathf.Exp(-u * u * 18f);
                float along = Mathf.Pow(1f - v, 1.4f);
                byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(across * along) * 255f);
                px[y * w + x] = new Color32(b, b, b, 255);
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }
}
