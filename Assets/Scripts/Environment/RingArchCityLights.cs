using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_AutumnRainCity's ring: a wrought-steel arch over the old street like the ones that carry a
/// town's festival lights - two slim posts and a shallow arch of black steel strung with warm bulbs,
/// a neon plate hanging from its crown in the ring's accent.
///
/// The finale, so it hits hardest (SongTweens from ClubBeat): a chase runs over the bulbs on the
/// eighths, every bulb kicks on the beat, the plate flashes on beats 1 and 3 and blinks on every
/// beat through a drop. Capped below white.
///
/// Clearances: the posts stand 4.5 m out; the arch's lowest point over the lanes is 6 m up.
public class RingArchCityLights : MonoBehaviour, IThemeAccent
{
    private const float MaxEmission = 1.8f;
    private const int Bulbs = 22;

    [SerializeField] private Texture2D bulbMask;
    [SerializeField] private Texture2D plateMask;
    [SerializeField] private Shader glowShader;
    [SerializeField] private float postX = 4.5f;
    [SerializeField] private float springY = 5.6f;
    [SerializeField] private float crownY = 7.6f;
    [SerializeField] private Color steel = new Color(0.05f, 0.05f, 0.07f);
    [SerializeField] private Color bulbColor = new Color(1f, 0.68f, 0.3f);
    [SerializeField] private Color accent = new Color(1f, 0.2f, 0.8f);

    private Mesh cube;
    private Material steelMat;
    private readonly List<MeshRenderer> bulbs = new List<MeshRenderer>();
    private MeshRenderer plate;
    private MaterialPropertyBlock mpb;
    private readonly SongTweens tweens = new SongTweens();
    private float kick, strong;
    private Camera cam;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        steelMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        steelMat.SetColor("_BaseColor", steel);
        steelMat.SetFloat("_Metallic", 0.8f);
        steelMat.SetFloat("_Smoothness", 0.7f);

        for (int s = -1; s <= 1; s += 2)
        {
            Bar(new Vector3(s * postX, 0f, 0f), new Vector3(s * postX, springY + 0.3f, 0f), 0.16f);
            Bar(new Vector3(s * postX, springY - 0.8f, 0f), new Vector3(s * (postX - 0.8f), springY + 0.1f, 0f), 0.07f);
        }
        var pts = new List<Vector3>();
        for (int i = 0; i <= 16; i++)
        {
            float x = Mathf.Lerp(-postX, postX, i / 16f);
            float y = springY + (crownY - springY) * (1f - (x / postX) * (x / postX));
            pts.Add(new Vector3(x, y, 0f));
        }
        for (int i = 0; i + 1 < pts.Count; i++) { Bar(pts[i], pts[i + 1], 0.1f); Bar(pts[i] + Vector3.down * 0.35f, pts[i + 1] + Vector3.down * 0.35f, 0.05f); }
        for (int b = 0; b < Bulbs; b++)
        {
            float x = Mathf.Lerp(-postX + 0.3f, postX - 0.3f, b / (Bulbs - 1f));
            float y = springY + (crownY - springY) * (1f - (x / postX) * (x / postX)) - 0.18f;
            bulbs.Add(Glow(new Vector3(x, y, -0.08f), 0.55f, bulbMask, false));
        }
        plate = Glow(new Vector3(0f, crownY - 1.1f, -0.1f), 1.3f, plateMask, true);

        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutExpo), 1f);
        tweens.Loop(DOTween.To(x => strong = x, 1f, 0f, 2f).SetEase(Ease.OutExpo), 2f);
        cam = Camera.main;
    }

    private void OnDestroy()
    {
        tweens.KillAll();
        if (steelMat != null) Destroy(steelMat);
        foreach (var b in bulbs) if (b != null) Destroy(b.sharedMaterial);
        if (plate != null) Destroy(plate.sharedMaterial);
    }

    public void ApplyAccent(Color newColor, float newIntensity) => accent = newColor;

    private void Update()
    {
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        tweens.SeekLoops(beats);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float drop = Mathf.Clamp01(energy - 1f) * 1.6f;
        int chase = Mathf.FloorToInt(beats * 2f);
        if (cam == null) cam = Camera.main;
        for (int i = 0; i < bulbs.Count; i++)
        {
            bool lead = (i + chase) % 4 == 0;
            Set(bulbs[i], bulbColor, (lead ? 1.1f : 0.45f) + 0.8f * kick * energy * (1f + drop));
        }
        bool blink = drop > 0.2f ? kick > 0.3f : strong > 0.25f;
        Set(plate, accent, 0.35f + (blink ? 1.2f : 0.2f) * energy);
    }

    private void Set(MeshRenderer r, Color c, float intensity)
    {
        if (cam != null) r.transform.rotation = Quaternion.LookRotation(r.transform.position - cam.transform.position);
        mpb.Clear();
        mpb.SetColor(ColorId, c);
        mpb.SetFloat(IntensityId, Mathf.Min(intensity, MaxEmission));
        r.SetPropertyBlock(mpb);
    }

    private MeshRenderer Glow(Vector3 pos, float size, Texture2D mask, bool alphaMask)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = "Light";
        q.transform.SetParent(transform, false);
        q.transform.localPosition = pos;
        q.transform.localScale = Vector3.one * size;
        var r = q.GetComponent<MeshRenderer>();
        var m = new Material(glowShader != null ? glowShader : Shader.Find("Tunnel/ClubGlow"));
        if (mask != null) m.SetTexture("_MainTex", mask);
        // The particle-pack bulb is opaque (DXT1, shape in red); the blurred Kenney plate keeps its shape only in alpha.
        m.SetFloat("_UseAlpha", alphaMask ? 1f : 0f);
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return r;
    }

    private void Bar(Vector3 a, Vector3 b, float thickness)
    {
        var go = new GameObject("Steel");
        go.transform.SetParent(transform, false);
        Vector3 e = b - a;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, e.normalized);
        go.transform.localScale = new Vector3(thickness, e.magnitude + thickness * 0.5f, thickness);
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = steelMat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }
}
