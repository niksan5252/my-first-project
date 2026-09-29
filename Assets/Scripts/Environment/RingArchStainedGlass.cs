using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_HalloweenHaunted's ring: a gothic doorway of the haunted mansion - a heavy pointed frame
/// of dark stone on two clustered piers, with a stained-glass rose in the arch's head: wedges of
/// violet, poison green and the ring's accent, lit from behind, leaded in black.
///
/// This is the phonk segment, so it hits hardest (SongTweens from ClubBeat): every wedge of glass
/// kicks on every beat and the rose turns its lit sector a step per eighth; on the strong beats
/// (1 and 3) the whole window flashes like lightning behind it, and harder through a drop. Every
/// value stays capped below white.
///
/// Clearances: the piers' inner faces stand 4.1 m out; the arch springs at 3.6 m.
public class RingArchStainedGlass : MonoBehaviour, IThemeAccent
{
    // Lower than the other arches' ceiling on purpose: three dozen roses stand one behind the other
    // straight over the road, and at 2.2 their glow merged at the vanishing point into one column
    // that the bloom blew out to a white blot. At 1.0 the core stays coloured.
    private const float MaxEmission = 1.0f;
    private const int Wedges = 10;

    [SerializeField] private float pierX = 4.45f;
    [SerializeField] private float springY = 3.6f;
    [SerializeField] private float apexY = 8.6f;
    [SerializeField] private Color stone = new Color(0.05f, 0.04f, 0.07f);
    [SerializeField] private Color violet = new Color(0.62f, 0.2f, 1f);
    [SerializeField] private Color ghostGreen = new Color(0.35f, 1f, 0.45f);
    [SerializeField] private Color accent = new Color(0.2f, 0.95f, 1f);
    [SerializeField, Range(0f, MaxEmission)] private float emissionIntensity = 1.0f;
    // The rose's glow fades out between these distances down the road (metres).
    [SerializeField] private float fadeFrom = 25f;
    [SerializeField] private float fadeTo = 60f;
    private bool fadedOut;

    private Mesh cube;
    private Material stoneMat, glassMat;
    private readonly List<MeshRenderer> glass = new List<MeshRenderer>();
    private readonly List<Color> glassColor = new List<Color>();
    private MaterialPropertyBlock mpb;
    private readonly SongTweens tweens = new SongTweens();
    private float kick, strong;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        stoneMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        stoneMat.SetColor("_BaseColor", stone);
        stoneMat.SetFloat("_Smoothness", 0.35f);
        glassMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        glassMat.SetColor("_BaseColor", Color.black);
        glassMat.SetFloat("_Smoothness", 0f);
        glassMat.SetFloat("_Cull", 0f);
        glassMat.SetFloat("_SpecularHighlights", 0f);
        glassMat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        glassMat.EnableKeyword("_EMISSION");

        // Clustered piers: a main shaft and two slim ones against it.
        for (int s = -1; s <= 1; s += 2)
        {
            Box(new Vector3(s * pierX, springY * 0.5f, 0f), new Vector3(0.6f, springY, 0.6f), stoneMat);
            Box(new Vector3(s * (pierX - 0.38f), springY * 0.5f, -0.1f), new Vector3(0.18f, springY, 0.18f), stoneMat);
            Box(new Vector3(s * pierX, springY + 0.15f, 0f), new Vector3(0.85f, 0.3f, 0.8f), stoneMat);   // capital
        }
        // The pointed arch in thick stone, and the rose of glass inside its head.
        var outer = Gothic(pierX, springY, apexY);
        for (int i = 0; i + 1 < outer.Count; i++) Bar(outer[i], outer[i + 1], 0.5f, stoneMat);
        var inner = Gothic(pierX - 0.55f, springY + 0.3f, apexY - 0.75f);
        for (int i = 0; i + 1 < inner.Count; i++) Bar(inner[i], inner[i + 1], 0.16f, stoneMat);

        // The rose: a ring of glass wedges round a small green eye, leaded by the gaps between them.
        Vector3 c = new Vector3(0f, apexY - 1.75f, 0.05f);
        float r0 = 0.25f, r1 = 1.0f;
        for (int w = 0; w < Wedges; w++)
        {
            float a0 = w * Mathf.PI * 2f / Wedges, a1 = (w + 0.82f) * Mathf.PI * 2f / Wedges;
            glass.Add(Wedge(c, r0, r1, a0, a1));
            glassColor.Add(w % 3 == 0 ? ghostGreen : w % 3 == 1 ? violet : accent);
        }
        glass.Add(Wedge(c, 0f, r0 * 0.8f, 0f, Mathf.PI * 2f));
        glassColor.Add(ghostGreen);

        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutExpo), 1f);
        tweens.Loop(DOTween.To(x => strong = x, 1f, 0f, 2f).SetEase(Ease.OutExpo), 2f);
    }

    private void OnDestroy()
    {
        tweens.KillAll();
        if (stoneMat != null) Destroy(stoneMat);
        if (glassMat != null) Destroy(glassMat);
        foreach (var g in glass) if (g != null) Destroy(g.GetComponent<MeshFilter>().sharedMesh);
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        accent = newColor;
        emissionIntensity = Mathf.Min(newIntensity, 1.3f);
        for (int i = 0; i < glassColor.Count; i++) if (i % 3 == 2 && i < Wedges) glassColor[i] = accent;
    }

    private void Update()
    {
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        tweens.SeekLoops(beats);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float drop = Mathf.Clamp01(energy - 1f) * 1.6f;
        int lit = Mathf.FloorToInt(beats * 2f) % Wedges;    // the turning sector, a step per eighth
        // Far roses go dark: down the road they all stand on one point of the screen, the
        // vanishing point, and their glow added up there into one blot for the bloom to blow out.
        float near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeFrom, fadeTo, transform.position.z));
        if (near <= 0.001f && fadedOut) return;
        fadedOut = near <= 0.001f;
        for (int i = 0; i < glass.Count; i++)
        {
            bool sector = i < Wedges && (i == lit || i == (lit + Wedges / 2) % Wedges);
            float k = emissionIntensity * (0.18f + 0.4f * kick * energy + (sector ? 0.3f : 0f) + strong * (0.25f + 0.35f * drop));
            mpb.SetColor(EmissionId, Cap(glassColor[i] * k) * near);
            glass[i].SetPropertyBlock(mpb);
        }
    }

    private static Color Cap(Color c)
    {
        float p = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        return p > MaxEmission ? c * (MaxEmission / p) : c;
    }

    private static List<Vector3> Gothic(float half, float spring, float apex)
    {
        float h = apex - spring;
        float cx = (h * h - half * half) / (2f * half);
        float radius = half + cx;
        var pts = new List<Vector3>();
        const int n = 12;
        for (int i = 0; i <= n; i++)
        {
            float a = Mathf.Lerp(0f, Mathf.Atan2(h, cx), i / (float)n);
            pts.Add(new Vector3(-(-cx + Mathf.Cos(a) * radius), spring + Mathf.Sin(a) * radius, 0f));
        }
        for (int i = n - 1; i >= 0; i--) pts.Add(new Vector3(-pts[i].x, pts[i].y, 0f));
        return pts;
    }

    private MeshRenderer Wedge(Vector3 c, float r0, float r1, float a0, float a1)
    {
        var go = new GameObject("Glass");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = c;
        var mesh = new Mesh { name = "GlassWedge" };
        const int steps = 8;
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)steps);
            v.Add(new Vector3(Mathf.Cos(a) * r0, Mathf.Sin(a) * r0, 0f));
            v.Add(new Vector3(Mathf.Cos(a) * r1, Mathf.Sin(a) * r1, 0f));
        }
        for (int i = 0; i < steps; i++)
        {
            int b = i * 2;
            // One face, drawn from both sides by the material (Cull Off). Two faces on shared
            // vertices averaged their normals to nothing, and the lighting of a zero normal blew
            // up into a white blot at the vanishing point.
            t.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
        }
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = glassMat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return r;
    }

    private void Box(Vector3 pos, Vector3 size, Material mat)
    {
        var go = new GameObject("Stone");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    private void Bar(Vector3 a, Vector3 b, float thickness, Material mat)
    {
        var go = new GameObject("Stone");
        go.transform.SetParent(transform, false);
        Vector3 e = b - a;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, e.normalized);
        go.transform.localScale = new Vector3(thickness, e.magnitude + thickness * 0.5f, thickness);
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }
}
