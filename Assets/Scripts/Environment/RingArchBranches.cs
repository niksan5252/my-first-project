using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_AutumnForest's ring: two young trunks either side of the road whose branches bend over it
/// and twine together into an arch, heavy with autumn leaves (Kenney Nature Kit bushes, recoloured),
/// with small lanterns hung under the crown in the ring's accent.
///
/// On the song clock (SongTweens from ClubBeat): the leaf clumps glow and swell a little on every
/// beat, a warm flash runs over the crown once a bar, the lanterns kick on the beat - harder in a
/// drop, capped below white.
///
/// Clearances: the trunks stand 4.5 m out; the crown's underside is above 5.6 m over the road.
public class RingArchBranches : MonoBehaviour, IThemeAccent
{
    private const float MaxEmission = 1.6f;

    [SerializeField] private GameObject leafClump;
    [SerializeField] private Texture2D glowMask;
    [SerializeField] private Shader glowShader;
    [SerializeField] private float trunkX = 4.6f;
    [SerializeField] private float crownY = 7.2f;
    [SerializeField] private Color bark = new Color(0.36f, 0.22f, 0.12f);
    [SerializeField] private Color[] leafColors =
    {
        new Color(1f, 0.72f, 0.12f), new Color(1f, 0.42f, 0.07f), new Color(0.75f, 0.12f, 0.07f),
    };
    [SerializeField] private Color accent = new Color(0.1f, 0.9f, 0.85f);

    // One set of leaf materials shared by every arch.
    private static Material[] leafMats;
    private static Color[] leafEmission;
    private static int pulsedFrame = -1;

    private Mesh cube;
    private Material barkMat;
    private readonly List<Transform> clumps = new List<Transform>();
    private readonly List<Vector3> clumpRest = new List<Vector3>();
    private readonly List<float> clumpAlong = new List<float>();
    private readonly List<MeshRenderer> lanterns = new List<MeshRenderer>();
    private MaterialPropertyBlock mpb;
    private readonly SongTweens tweens = new SongTweens();
    private float kick, run;
    private Camera cam;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        barkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        barkMat.SetColor("_BaseColor", bark);
        barkMat.SetFloat("_Smoothness", 0.15f);
        EnsureLeafMaterials();

        // Each side: a trunk rising, then a bough curving over toward the middle and past it, so
        // the two interlace at the top.
        for (int s = -1; s <= 1; s += 2)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= 10; i++)
            {
                float t = i / 10f;
                float x = s * Mathf.Lerp(trunkX, -0.9f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.35f) / 0.65f)));
                float y = t < 0.45f ? Mathf.Lerp(0f, crownY - 1.2f, t / 0.45f) : crownY - 1.2f + Mathf.Sin((t - 0.45f) / 0.55f * Mathf.PI) * 1.2f;
                pts.Add(new Vector3(x, y, s * 0.15f));
            }
            for (int i = 0; i + 1 < pts.Count; i++)
                Bar(pts[i], pts[i + 1], Mathf.Lerp(0.5f, 0.16f, i / 10f), barkMat);
            // A side branch off each bough.
            Bar(pts[5], pts[5] + new Vector3(s * 1.2f, 1.4f, 0.2f), 0.14f, barkMat);
            // Leaf clumps along the upper bough.
            for (int i = 4; i <= 10; i += 2)
            {
                if (leafClump == null) break;
                var c = Instantiate(leafClump, transform);
                c.transform.localPosition = pts[i] + new Vector3(0f, 0.3f, 0f);
                c.transform.localRotation = Quaternion.Euler(0f, (i * 57 + s * 30) % 360, 0f);
                c.transform.localScale = Vector3.one * (4.2f + (i % 3) * 0.7f);
                int pick = (i / 2 + (s > 0 ? 1 : 0)) % leafMats.Length;
                foreach (var r in c.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++) mats[k] = leafMats[pick];
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
                clumps.Add(c.transform);
                clumpRest.Add(c.transform.localScale);
                clumpAlong.Add(s < 0 ? i / 20f : 1f - i / 20f);
            }
            // A lantern hanging under the crown.
            if (glowMask != null)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.name = "Lantern";
                q.transform.SetParent(transform, false);
                q.transform.localPosition = new Vector3(s * 1.8f, crownY - 1.9f, -0.1f);
                q.transform.localScale = Vector3.one * 0.9f;
                var r = q.GetComponent<MeshRenderer>();
                var m = new Material(glowShader != null ? glowShader : Shader.Find("Tunnel/ClubGlow"));
                m.SetTexture("_MainTex", glowMask);
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off;
                lanterns.Add(r);
            }
        }

        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutCubic), 1f);
        tweens.Loop(DOTween.To(x => run = x, 0f, 1f, 4f).SetEase(Ease.Linear), 4f);
        cam = Camera.main;
    }

    private void EnsureLeafMaterials()
    {
        if (leafMats != null && leafMats[0] != null) return;
        leafMats = new Material[leafColors.Length];
        leafEmission = new Color[leafColors.Length];
        for (int i = 0; i < leafColors.Length; i++)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", leafColors[i]);
            m.SetFloat("_Smoothness", 0.2f);
            m.EnableKeyword("_EMISSION");
            leafEmission[i] = leafColors[i] * 0.4f;
            m.SetColor(EmissionId, leafEmission[i]);
            leafMats[i] = m;
        }
    }

    private void OnDestroy()
    {
        tweens.KillAll();
        if (barkMat != null) Destroy(barkMat);
        foreach (var l in lanterns) if (l != null) Destroy(l.sharedMaterial);
    }

    public void ApplyAccent(Color newColor, float newIntensity) => accent = newColor;

    private void Update()
    {
        tweens.SeekLoops(ClubBeat.BeatIndex + ClubBeat.BeatPhase);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float drop = Mathf.Clamp01(energy - 1f) * 1.6f;
        for (int i = 0; i < clumps.Count; i++)
        {
            float d = Mathf.Abs(clumpAlong[i] - run);
            float band = Mathf.Exp(-d * d * 30f);
            clumps[i].localScale = clumpRest[i] * (1f + (0.06f + 0.05f * drop) * kick * energy + 0.04f * band);
        }
        if (Time.frameCount != pulsedFrame)
        {
            pulsedFrame = Time.frameCount;
            for (int i = 0; i < leafMats.Length; i++)
                leafMats[i].SetColor(EmissionId, leafEmission[i] * Mathf.Min(MaxEmission, 0.7f + 1.4f * kick * energy * (1f + 0.5f * drop)));
        }
        if (cam == null) cam = Camera.main;
        foreach (var l in lanterns)
        {
            if (cam != null) l.transform.rotation = Quaternion.LookRotation(l.transform.position - cam.transform.position);
            mpb.Clear();
            mpb.SetColor(ColorId, accent);
            mpb.SetFloat(IntensityId, Mathf.Min(0.5f + 1.1f * kick * energy * (1f + drop), MaxEmission));
            l.SetPropertyBlock(mpb);
        }
    }

    private void Bar(Vector3 a, Vector3 b, float thickness, Material mat)
    {
        var go = new GameObject("Bough");
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
