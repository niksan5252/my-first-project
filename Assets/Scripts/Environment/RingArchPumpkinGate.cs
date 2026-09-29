using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_HalloweenPumpkin's ring: a wooden festival gate. Two posts and a crossbeam with corner
/// braces, a big jack-o'-lantern on top of each post, a string of lanterns (Poly Halloween) hung
/// across under the beam, and a strip of light along the beam's underside in the ring's accent.
///
/// On the song clock (SongTweens from ClubBeat): the light strip kicks on every beat and a flash
/// runs from one post to the other once a bar; the pumpkins swell and glow on the beat, harder in
/// a drop; the lantern string's own emission pulses with them. Capped below white.
///
/// Clearances: the posts' inner faces stand 4.25 m out, the beam's underside at 5.2 m.
public class RingArchPumpkinGate : MonoBehaviour, IThemeAccent
{
    private const float MaxEmission = 2.2f;

    [SerializeField] private GameObject pumpkinModel;
    [SerializeField] private GameObject garlandModel;
    [SerializeField] private Texture2D glowMask;
    [SerializeField] private Shader glowShader;
    [SerializeField] private float postX = 4.4f;
    [SerializeField] private float beamY = 5.4f;
    [SerializeField] private Color wood = new Color(0.16f, 0.09f, 0.05f);
    [SerializeField] private Color accent = new Color(1f, 0.5f, 0.08f);
    [SerializeField] private Color pumpkinGlow = new Color(1f, 0.5f, 0.1f);
    [SerializeField, Range(0f, MaxEmission)] private float emissionIntensity = 1.1f;

    // One night copy of each prop material, shared by every gate (there are three dozen of them).
    private static readonly Dictionary<Material, (Material m, Color e)> night = new Dictionary<Material, (Material, Color)>();

    private Mesh cube;
    private Material woodMat, stripMat;
    private readonly List<MeshRenderer> strip = new List<MeshRenderer>();
    private readonly List<Transform> pumpkins = new List<Transform>();
    private readonly List<MeshRenderer> glows = new List<MeshRenderer>();
    private MaterialPropertyBlock mpb;
    private readonly SongTweens tweens = new SongTweens();
    private float kick, run;
    private Camera cam;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        woodMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        woodMat.SetColor("_BaseColor", wood);
        woodMat.SetFloat("_Smoothness", 0.2f);
        stripMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        stripMat.SetColor("_BaseColor", Color.black);
        stripMat.EnableKeyword("_EMISSION");

        for (int s = -1; s <= 1; s += 2)
        {
            Box(new Vector3(s * postX, beamY * 0.5f, 0f), new Vector3(0.35f, beamY, 0.35f), woodMat);
            // A corner brace from the post to the beam.
            Bar(new Vector3(s * postX, beamY - 1.3f, 0f), new Vector3(s * (postX - 1.3f), beamY, 0f), 0.2f, woodMat);
            if (pumpkinModel != null)
            {
                var pk = Instantiate(pumpkinModel, transform);
                pk.transform.localPosition = new Vector3(s * postX, beamY + 0.25f, 0f);
                pk.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                pk.transform.localScale = Vector3.one * 1.9f;
                Night(pk);
                pumpkins.Add(pk.transform);
                if (glowMask != null) glows.Add(Glow(new Vector3(s * postX, beamY + 0.7f, -0.3f), 2.2f));
            }
        }
        Box(new Vector3(0f, beamY + 0.12f, 0f), new Vector3(postX * 2f + 0.8f, 0.3f, 0.35f), woodMat);
        // The light strip under the beam, in pieces so a flash can run along it.
        const int pieces = 16;
        for (int i = 0; i < pieces; i++)
        {
            float x0 = -postX + 0.2f + (postX * 2f - 0.4f) * i / pieces;
            float x1 = -postX + 0.2f + (postX * 2f - 0.4f) * (i + 1) / pieces;
            strip.Add(Bar(new Vector3(x0, beamY - 0.06f, -0.12f), new Vector3(x1, beamY - 0.06f, -0.12f), 0.07f, stripMat));
        }
        if (garlandModel != null)
        {
            var g = Instantiate(garlandModel, transform);
            // The string runs from its origin along -X for 5 m: stretched over the span.
            g.transform.localPosition = new Vector3(postX - 0.2f, beamY - 0.25f, -0.05f);
            g.transform.localScale = new Vector3((postX * 2f - 0.4f) / 5f, 1.3f, 1.3f);
            Night(g);
        }

        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutCubic), 1f);
        tweens.Loop(DOTween.To(x => run = x, 0f, 1f, 4f).SetEase(Ease.Linear), 4f);
        cam = Camera.main;
    }

    private void OnDestroy()
    {
        tweens.KillAll();
        if (woodMat != null) Destroy(woodMat);
        if (stripMat != null) Destroy(stripMat);
        foreach (var g in glows) if (g != null) Destroy(g.sharedMaterial);
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        accent = newColor;
        emissionIntensity = Mathf.Min(newIntensity, 1.3f);
    }

    private void Update()
    {
        tweens.SeekLoops(ClubBeat.BeatIndex + ClubBeat.BeatPhase);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.6f);
        float drop = Mathf.Clamp01(energy - 1f) * 1.6f;
        for (int i = 0; i < strip.Count; i++)
        {
            float d = Mathf.Abs((i + 0.5f) / strip.Count - run);
            float band = Mathf.Exp(-d * d * 80f);
            float k = emissionIntensity * (0.5f + 0.55f * kick * energy + band * (0.5f + drop));
            mpb.Clear();
            mpb.SetColor(EmissionId, accent * Mathf.Min(k, MaxEmission));
            strip[i].SetPropertyBlock(mpb);
        }
        float swell = 1f + 0.12f * kick * energy * (1f + drop);
        foreach (var p in pumpkins) p.localScale = Vector3.one * 1.9f * swell;
        if (cam == null) cam = Camera.main;
        foreach (var g in glows)
        {
            if (cam != null) g.transform.rotation = Quaternion.LookRotation(g.transform.position - cam.transform.position);
            mpb.Clear();
            mpb.SetColor(ColorId, pumpkinGlow);
            mpb.SetFloat(IntensityId, Mathf.Min(0.4f + 0.9f * kick * energy * (1f + drop), MaxEmission));
            g.SetPropertyBlock(mpb);
        }
        // The shared night materials: pulsed once per frame by whichever gate gets there first.
        if (Time.frameCount != pulsedFrame)
        {
            pulsedFrame = Time.frameCount;
            foreach (var kv in night)
                if (kv.Value.e.maxColorComponent > 0.01f)
                    kv.Value.m.SetColor(EmissionId, Cap(kv.Value.e * (0.6f + 0.9f * kick * energy * (1f + 0.5f * drop))));
        }
    }

    private static int pulsedFrame = -1;

    private static Color Cap(Color c)
    {
        float p = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        return p > MaxEmission ? c * (MaxEmission / p) : c;
    }

    /// The prop's materials swapped for URP Lit night copies (the Poly pack ships Built-in Standard).
    private static void Night(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                if (src == null) continue;
                if (!night.TryGetValue(src, out var n) || n.m == null)
                {
                    bool urp = src.shader != null && src.shader.name.StartsWith("Universal Render Pipeline");
                    var m = urp ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    Color e = Color.black;
                    if (!urp)
                    {
                        if (src.HasProperty("_MainTex") && src.GetTexture("_MainTex") != null) m.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
                        if (src.HasProperty("_Color")) m.SetColor("_BaseColor", src.GetColor("_Color"));
                        if (src.IsKeywordEnabled("_EMISSION"))
                        {
                            m.EnableKeyword("_EMISSION");
                            if (src.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", src.GetTexture("_EmissionMap"));
                            e = src.GetColor(EmissionId);
                        }
                    }
                    else if (m.IsKeywordEnabled("_EMISSION")) e = m.GetColor(EmissionId);
                    var bc = m.GetColor("_BaseColor");
                    m.SetColor("_BaseColor", new Color(bc.r * 0.75f, bc.g * 0.68f, bc.b * 0.7f, bc.a));
                    n = (m, e);
                    night[src] = n;
                }
                mats[i] = n.m;
            }
            r.sharedMaterials = mats;
        }
    }

    private MeshRenderer Glow(Vector3 pos, float size)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = "Glow";
        q.transform.SetParent(transform, false);
        q.transform.localPosition = pos;
        q.transform.localScale = Vector3.one * size;
        var r = q.GetComponent<MeshRenderer>();
        var m = new Material(glowShader != null ? glowShader : Shader.Find("Tunnel/ClubGlow"));
        m.SetTexture("_MainTex", glowMask);
        m.SetFloat("_UseAlpha", 0f);
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return r;
    }

    private void Box(Vector3 pos, Vector3 size, Material mat)
    {
        var go = new GameObject("Wood");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    private MeshRenderer Bar(Vector3 a, Vector3 b, float thickness, Material mat)
    {
        var go = new GameObject("Bar");
        go.transform.SetParent(transform, false);
        Vector3 e = b - a;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, e.normalized);
        go.transform.localScale = new Vector3(thickness, e.magnitude, thickness);
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return r;
    }
}
