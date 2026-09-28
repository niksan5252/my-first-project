using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_Ocean's ring: an arch of light like a wave frozen over the road - a tall half-ellipse
/// drawn as a glowing ribbon, the sea's aqua, with a thinner coral ribbon inside it. A ripple runs
/// round both, as if the arch were water: the ribbons' radius is pushed in and out by a wave that
/// travels from one foot over the top to the other.
///
/// In time with the music, through SongTweens (DOTween, paused, positioned from the song clock):
/// the ripple goes round once every two beats, swells on every beat and the ribbons flare with it;
/// the inner ribbon runs its ripple the other way. Scaled by ClubBeat's energy.
///
/// Clearances: the feet stand 4.6 m out; at 2.1 m up (a Dodge wall's top) the arch is 4.2 m out, at
/// 2.45 m (the outer lanes' high notes, 2.25 m out) 4.06 m; the top is at 5.3 m.
public class RingArchOceanWave : MonoBehaviour, IThemeAccent
{
    private const float MaxEmissionIntensity = 2.2f;
    private const int Samples = 48;

    [SerializeField] private float radiusX = 4.6f;
    [SerializeField] private float radiusY = 5.3f;
    [SerializeField] private float outerWidth = 0.11f;
    [SerializeField] private float innerWidth = 0.05f;
    [SerializeField] private float innerInset = 0.32f;
    [SerializeField] private float rippleWaves = 3f;        // wavelengths along the arch
    [SerializeField] private float rippleAmplitude = 0.12f; // metres
    [SerializeField] private Color aqua = new Color(0.2f, 0.9f, 0.85f);
    [SerializeField] private Color coral = new Color(1f, 0.38f, 0.45f);
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float emissionIntensity = 1f;

    private Mesh outerMesh, innerMesh;
    private Vector3[] outerVerts, innerVerts;
    private MeshRenderer outerR, innerR;
    private MaterialPropertyBlock mpb;
    private readonly SongTweens tweens = new SongTweens();
    private float ripple, swell;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        var mat = NeonMaterial();
        outerR = Ribbon("Outer", mat, out outerMesh, out outerVerts);
        innerR = Ribbon("Inner", mat, out innerMesh, out innerVerts);

        tweens.Loop(DOTween.To(x => ripple = x, 0f, 1f, 2f).SetEase(Ease.Linear), 2f);
        tweens.Loop(DOTween.To(x => swell = x, 1f, 0f, 1f).SetEase(Ease.OutCubic), 1f);
        UpdateRibbons(0f, 0f);
    }

    private void OnDestroy() => tweens.KillAll();

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        // Every arch is the sea's aqua with a coral lining, whatever accent the generator
        // alternates in: the theme's own two colours are kept for the cues.
        emissionIntensity = Mathf.Min(newIntensity * 0.8f, 1.1f);
    }

    private void Update()
    {
        if (outerMesh == null) return;
        tweens.SeekLoops(ClubBeat.BeatIndex + ClubBeat.BeatPhase);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.6f);
        float amp = rippleAmplitude * (0.35f + 0.65f * swell * Mathf.Clamp01(energy));
        UpdateRibbons(ripple, amp);

        mpb.SetColor(EmissionId, aqua * Mathf.Min(emissionIntensity * (0.95f + 0.6f * swell * energy), MaxEmissionIntensity));
        outerR.SetPropertyBlock(mpb);
        mpb.SetColor(EmissionId, coral * Mathf.Min(emissionIntensity * (0.75f + 0.5f * swell * energy), MaxEmissionIntensity));
        innerR.SetPropertyBlock(mpb);
    }

    private void UpdateRibbons(float phase, float amp)
    {
        Fill(outerVerts, radiusX, radiusY, outerWidth, phase, amp);
        Fill(innerVerts, radiusX - innerInset, radiusY - innerInset, innerWidth, -phase, amp * 0.7f);
        outerMesh.vertices = outerVerts;
        innerMesh.vertices = innerVerts;
    }

    /// A ribbon along the half-ellipse, its radius rippled; two vertices per sample, across it.
    private void Fill(Vector3[] v, float rx, float ry, float width, float phase, float amp)
    {
        for (int i = 0; i <= Samples; i++)
        {
            float f = i / (float)Samples;
            float th = f * Mathf.PI;
            var dir = new Vector2(Mathf.Cos(th), Mathf.Sin(th));
            // The feet stay planted; the ripple grows away from them.
            float wave = Mathf.Sin((f * rippleWaves - phase) * Mathf.PI * 2f) * amp * Mathf.Sin(th);
            var p = new Vector2(dir.x * rx, dir.y * ry) + dir * wave;
            var n = new Vector2(dir.x / rx, dir.y / ry).normalized;   // ellipse normal
            v[i * 2] = new Vector3(p.x - n.x * width, p.y - n.y * width, 0f);
            v[i * 2 + 1] = new Vector3(p.x + n.x * width, p.y + n.y * width, 0f);
        }
    }

    private MeshRenderer Ribbon(string name, Material mat, out Mesh mesh, out Vector3[] verts)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        verts = new Vector3[(Samples + 1) * 2];
        var tris = new int[Samples * 6];
        for (int i = 0; i < Samples; i++)
        {
            int a = i * 2;
            tris[i * 6] = a; tris[i * 6 + 1] = a + 1; tris[i * 6 + 2] = a + 2;
            tris[i * 6 + 3] = a + 1; tris[i * 6 + 4] = a + 3; tris[i * 6 + 5] = a + 2;
        }
        mesh = new Mesh { name = "WaveRibbon" };
        mesh.MarkDynamic();
        mesh.vertices = verts;
        mesh.triangles = tris;
        // Fixed bounds covering the ripple, so the generator's frustum test stays valid.
        mesh.bounds = new Bounds(new Vector3(0f, radiusY * 0.5f, 0f), new Vector3(radiusX * 2f + 1f, radiusY + 1f, 0.5f));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    private Material NeonMaterial()
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", Color.black);
        m.SetFloat("_Smoothness", 0.2f);
        m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor(EmissionId, aqua * emissionIntensity);
        return m;
    }
}
