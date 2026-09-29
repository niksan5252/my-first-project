using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_HalloweenGraveyard's ring: a wrought-iron cemetery gate over the road. Two stone gate
/// pillars (the Cemetery Snap pack's) carry a pointed gothic arch of black iron - an outer and an
/// inner curve with bars between them, spear finials on the pillars and a cross at the apex, and a
/// pair of scroll curls in the spandrels. A thin line of ghostly light runs along the inside of the
/// arch in the ring's accent, and a flame burns on each pillar.
///
/// In time with the music (SongTweens, positioned from ClubBeat's song time): the light line flares
/// on every beat and swells harder through a drop, a surge runs up one side and down the other
/// every bar, and the flames flicker on the eighths. Scaled by ClubBeat's energy, capped below white.
///
/// The arch is a true pointed one - its rise above the springing (4.65 m) is more than its half
/// span (4.25 m), which is what the two-circle construction needs to meet in a point.
///
/// Clearances: the pillars' inner faces stand 3.9 m out and the iron springs from them at 3.35 m;
/// at 2.45 m up (the outer lanes' high notes, 2.25 m out) the nearest iron is the pillar, 1.6 m away.
public class RingArchGothicGate : MonoBehaviour, IThemeAccent
{
    private const float MaxEmission = 2.2f;
    private const int ArcSamples = 14;

    [SerializeField] private GameObject pillarModel;
    [SerializeField] private float pillarX = 4.35f;
    [SerializeField] private Vector3 pillarScale = new Vector3(2.6f, 2.9f, 2.6f);
    // The kit's stone multiplied down to moonlit grey-blue.
    [SerializeField] private Color pillarTint = new Color(0.42f, 0.46f, 0.56f);
    [SerializeField] private float springY = 3.35f;
    [SerializeField] private float apexY = 8.0f;
    [SerializeField] private float ironThickness = 0.09f;
    [SerializeField] private Color ironColor = new Color(0.02f, 0.022f, 0.03f);
    [SerializeField] private Color glowColor = new Color(0.45f, 1f, 0.3f);
    [SerializeField] private Color flameColor = new Color(1f, 0.5f, 0.08f);
    [SerializeField] private Texture2D flameMask;
    [SerializeField] private Shader glowShader;
    [SerializeField, Range(0f, MaxEmission)] private float emissionIntensity = 1.1f;

    private Mesh cube;
    private Material ironMat, glowMat;
    private readonly List<MeshRenderer> glowLine = new List<MeshRenderer>();
    private readonly List<float> glowAlong = new List<float>();   // 0 at the left foot, 1 at the right
    private readonly List<MeshRenderer> flames = new List<MeshRenderer>();
    private MaterialPropertyBlock mpb;
    private readonly SongTweens tweens = new SongTweens();
    private float kick, surge, flicker;
    private Camera cam;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    public Color GlowColor { get => glowColor; set => glowColor = value; }

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);

        ironMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        ironMat.SetColor("_BaseColor", ironColor);
        ironMat.SetFloat("_Metallic", 0.8f);
        ironMat.SetFloat("_Smoothness", 0.45f);
        glowMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        glowMat.SetColor("_BaseColor", Color.black);
        glowMat.EnableKeyword("_EMISSION");
        glowMat.SetColor(EmissionId, glowColor);

        for (int s = -1; s <= 1; s += 2)
        {
            if (pillarModel != null)
            {
                var p = Instantiate(pillarModel, transform);
                p.transform.localPosition = new Vector3(s * pillarX, 0f, 0f);
                p.transform.localScale = pillarScale;
                foreach (var r in p.GetComponentsInChildren<Renderer>())
                {
                    foreach (var m in r.materials) if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", pillarTint);
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
            }
            // Spear finial on the pillar, and the flame beside it.
            Bar(new Vector3(s * pillarX, springY, 0f), new Vector3(s * pillarX, springY + 0.7f, 0f), ironThickness, ironMat);
            Bar(new Vector3(s * pillarX - 0.12f, springY + 0.55f, 0f), new Vector3(s * pillarX + 0.12f, springY + 0.55f, 0f), ironThickness * 0.8f, ironMat);
            if (flameMask != null)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.name = "Flame";
                q.transform.SetParent(transform, false);
                q.transform.localPosition = new Vector3(s * (pillarX + 0.05f), springY + 0.95f, -0.05f);
                q.transform.localScale = new Vector3(0.8f, 1.1f, 1f);
                var r = q.GetComponent<MeshRenderer>();
                var m = new Material(glowShader != null ? glowShader : Shader.Find("Tunnel/ClubGlow"));
                m.SetTexture("_MainTex", flameMask);
                m.SetFloat("_UseAlpha", 0f);   // Kenney particle-pack sprites are opaque (DXT1): the shape is in the colour
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off;
                flames.Add(r);
            }
        }

        // The pointed arch: two circular arcs, each centred on the OTHER springing point's side,
        // meeting at the apex - the classic equilateral-ish gothic shape.
        var outer = GothicCurve(pillarX - 0.1f, springY, apexY);
        var inner = GothicCurve(pillarX - 0.55f, springY + 0.15f, apexY - 0.55f);
        Polyline(outer, ironThickness * 1.3f, ironMat, null);
        Polyline(inner, ironThickness, ironMat, null);
        for (int i = 1; i < outer.Count - 1; i += 2) Bar(outer[i], inner[i], ironThickness * 0.7f, ironMat);
        // The light line just inside the inner curve.
        var light = GothicCurve(pillarX - 0.68f, springY + 0.2f, apexY - 0.7f);
        Polyline(light, 0.05f, glowMat, glowLine);

        // Cross at the apex.
        Bar(new Vector3(0f, apexY, 0f), new Vector3(0f, apexY + 1.0f, 0f), ironThickness * 1.2f, ironMat);
        Bar(new Vector3(-0.32f, apexY + 0.68f, 0f), new Vector3(0.32f, apexY + 0.68f, 0f), ironThickness * 1.2f, ironMat);
        // Scroll curls in the spandrels.
        for (int s = -1; s <= 1; s += 2) Curl(new Vector3(s * (pillarX - 1.05f), springY + 1.25f, 0f), 0.42f, s);

        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutCubic), 1f);
        tweens.Loop(DOTween.To(x => surge = x, 0f, 1f, 4f).SetEase(Ease.InOutSine), 4f);
        tweens.Loop(DOTween.To(x => flicker = x, 1f, 0.55f, 0.5f).SetEase(Ease.OutQuad), 0.5f);
        cam = Camera.main;
    }

    private void OnDestroy()
    {
        tweens.KillAll();
        if (ironMat != null) Destroy(ironMat);
        if (glowMat != null) Destroy(glowMat);
        // The pillars' materials were instanced per ring (r.materials) to take the tint.
        foreach (var r in GetComponentsInChildren<Renderer>()) if (r != null && r.gameObject.name != "Iron") foreach (var m in r.materials) if (m != null && m != glowMat && m != ironMat) Destroy(m);
        foreach (var f in flames) if (f != null) Destroy(f.sharedMaterial);
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        // The iron glows the theme's two accents in turn, one per ring: ghost green and pumpkin.
        glowColor = newColor;
        emissionIntensity = Mathf.Min(newIntensity, 1.3f);
    }

    private void Update()
    {
        tweens.SeekLoops(ClubBeat.BeatIndex + ClubBeat.BeatPhase);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.6f);
        float drop = Mathf.Clamp01(energy - 1f) * 1.6f;
        for (int i = 0; i < glowLine.Count; i++)
        {
            // The surge: a bright band running up the left side and down the right once a bar.
            float d = Mathf.Abs(glowAlong[i] - surge);
            float band = Mathf.Exp(-d * d * 60f);
            float k = emissionIntensity * (0.55f + 0.5f * kick * energy + band * (0.4f + drop) );
            mpb.SetColor(EmissionId, glowColor * Mathf.Min(k, MaxEmission));
            glowLine[i].SetPropertyBlock(mpb);
        }
        if (cam == null) cam = Camera.main;
        foreach (var f in flames)
        {
            if (cam != null) f.transform.rotation = Quaternion.LookRotation(f.transform.position - cam.transform.position);
            mpb.Clear();
            mpb.SetColor(ColorId, flameColor);
            mpb.SetFloat(IntensityId, Mathf.Min(0.7f + 0.55f * flicker * energy + 0.5f * kick * drop, MaxEmission));
            f.SetPropertyBlock(mpb);
        }
    }

    /// Points of a pointed arch from (-half, spring) over the apex to (+half, spring).
    private static List<Vector3> GothicCurve(float half, float spring, float apex)
    {
        // Each side is an arc of a circle centred on the far side at spring height; its radius
        // is chosen so it passes through both the foot and the apex.
        float h = apex - spring;
        float cx = (h * h - half * half) / (2f * half);  // centre x (for the right-hand arc: at -cx)
        float radius = half + cx;
        var pts = new List<Vector3>();
        for (int i = 0; i <= ArcSamples; i++)
        {
            float t = i / (float)ArcSamples;
            float a0 = 0f, a1 = Mathf.Atan2(h, cx);
            float a = Mathf.Lerp(a0, a1, t);
            pts.Add(new Vector3(-(-cx + Mathf.Cos(a) * radius), spring + Mathf.Sin(a) * radius, 0f));
        }
        for (int i = ArcSamples - 1; i >= 0; i--)
        {
            var p = pts[i];
            pts.Add(new Vector3(-p.x, p.y, 0f));
        }
        return pts;
    }

    private void Polyline(List<Vector3> pts, float thickness, Material mat, List<MeshRenderer> collect)
    {
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var r = Bar(pts[i], pts[i + 1], thickness, mat);
            if (collect != null)
            {
                collect.Add(r);
                glowAlong.Add((i + 0.5f) / (pts.Count - 1));
            }
        }
    }

    private void Curl(Vector3 c, float radius, int side)
    {
        const int n = 12;
        Vector3 prev = c + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= n; i++)
        {
            float a = i / (float)n * Mathf.PI * 1.7f;
            float r = radius * (1f - 0.45f * i / n);
            var p = c + new Vector3(Mathf.Cos(a) * r * side, Mathf.Sin(a) * r, 0f);
            Bar(prev, p, ironThickness * 0.7f, ironMat);
            prev = p;
        }
    }

    private MeshRenderer Bar(Vector3 a, Vector3 b, float thickness, Material mat)
    {
        var go = new GameObject("Iron");
        go.transform.SetParent(transform, false);
        Vector3 e = b - a;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, e.normalized);
        go.transform.localScale = new Vector3(thickness, e.magnitude + thickness * 0.5f, thickness);
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }
}
