using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_Synthwave's ring: a retro "portal" - a tall hexagonal frame drawn as a thin neon double
/// line, the outer stroke in the ring's accent and the inner in the theme's other colour, with a
/// small square node at each corner. Thin lines where the club's halos are round and the phonk
/// gates are heavy.
///
/// In time with the music, through SongTweens (DOTween, paused, positioned from the song clock):
/// the outer line kicks on every beat, the inner line answers on the off-beat; and the pulse the
/// ground sends rushing in along the road every bar (the _SynthWave0 global, set by
/// SynthwaveEnvironmentDecorator) lights each portal up as it passes through it. All of it scaled
/// by ClubBeat's energy.
///
/// Clearances: the frame stands 4 m out at the floor and leans out to 4.7 m at 2.4 m up, clear of
/// the 6 m road, the outer lanes' high notes (2.25 m out, 2.45 m up) and a Dodge wall (3 m, 2.1 m);
/// at 3.4 m up it is still 4.07 m out; the top bar is at 5.4 m.
public class RingArchSynthPortal : MonoBehaviour, IThemeAccent
{
    private const float MaxEmissionIntensity = 2.2f;

    [SerializeField] private float baseHalfWidth = 4f;
    [SerializeField] private float waistHalfWidth = 4.7f;
    [SerializeField] private float waistHeight = 2.4f;
    [SerializeField] private float topHalfWidth = 2.8f;
    [SerializeField] private float topHeight = 5.4f;
    [SerializeField] private float lineThickness = 0.09f;
    [SerializeField] private float innerInset = 0.34f;
    [SerializeField] private Color warm = new Color(1f, 0.42f, 0.22f);
    [SerializeField] private Color cool = new Color(0.55f, 0.25f, 1f);
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float emissionIntensity = 0.9f;

    private Color outerColor, innerColor;
    private MeshRenderer[] outer, inner, nodes;
    private MaterialPropertyBlock mpb;
    private readonly SongTweens tweens = new SongTweens();
    private float kick, answer;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int Wave0Id = Shader.PropertyToID("_SynthWave0");

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        outerColor = warm;
        innerColor = cool;
        var mat = NeonMaterial();
        Mesh cube = CubeMesh();

        Vector3[] Outline(float inset)
        {
            float k = inset;
            return new[]
            {
                new Vector3(-(baseHalfWidth - k), 0f, 0f),
                new Vector3(-(waistHalfWidth - k), waistHeight, 0f),
                new Vector3(-(topHalfWidth - k * 0.6f), topHeight - k, 0f),
                new Vector3(topHalfWidth - k * 0.6f, topHeight - k, 0f),
                new Vector3(waistHalfWidth - k, waistHeight, 0f),
                new Vector3(baseHalfWidth - k, 0f, 0f),
            };
        }
        var o = Outline(0f);
        var i = Outline(innerInset);
        outer = new MeshRenderer[o.Length - 1];
        inner = new MeshRenderer[i.Length - 1];
        for (int s = 0; s < o.Length - 1; s++)
        {
            outer[s] = Beam("Outer", cube, mat, o[s], o[s + 1], lineThickness);
            inner[s] = Beam("Inner", cube, mat, i[s], i[s + 1], lineThickness * 0.6f);
        }
        // Corner nodes on the outer line (not at the feet).
        nodes = new MeshRenderer[4];
        for (int n = 0; n < 4; n++)
        {
            var node = Beam("Node", cube, mat, o[n + 1] - Vector3.up * 0.09f, o[n + 1] + Vector3.up * 0.09f, 0.2f);
            node.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            nodes[n] = node;
        }

        // The outer line: a kick on the beat. The inner line: a flash on the off-beat.
        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutCubic), 1f);
        tweens.Loop(DOTween.Sequence()
            .AppendInterval(0.45f)
            .Append(DOTween.To(x => answer = x, 0f, 1f, 0.05f))
            .Append(DOTween.To(x => answer = x, 1f, 0f, 0.5f).SetEase(Ease.OutQuad)), 1f);
    }

    private void OnDestroy() => tweens.KillAll();

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        // The generator alternates the theme's two colours ring by ring; the inner line takes the
        // other one, so neighbouring portals swap their pair.
        bool isWarm = newColor.r >= newColor.b;
        outerColor = newColor;
        innerColor = isWarm ? cool : warm;
        emissionIntensity = Mathf.Min(newIntensity * 0.7f, 1.1f);
    }

    private void Update()
    {
        if (outer == null) return;
        tweens.SeekLoops(ClubBeat.BeatIndex + ClubBeat.BeatPhase);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.6f);

        // The ground's pulse rushing in along the road: light when it is at this portal.
        Vector4 wave = Shader.GetGlobalVector(Wave0Id);
        float dz = (transform.position.z - wave.x) / 5f;
        float rush = Mathf.Exp(-dz * dz) * Mathf.Clamp01(energy - 0.3f) * 1.3f;

        Set(outer, outerColor, emissionIntensity * (0.8f + 0.55f * kick * energy + rush));
        Set(inner, innerColor, emissionIntensity * (0.55f + 0.65f * answer * energy + rush * 0.7f));
        Set(nodes, outerColor, emissionIntensity * (0.9f + 0.8f * kick * energy + rush));
    }

    private void Set(MeshRenderer[] rs, Color c, float level)
    {
        mpb.SetColor(EmissionId, c * Mathf.Clamp(level, 0f, MaxEmissionIntensity));
        foreach (var r in rs) r.SetPropertyBlock(mpb);
    }

    private MeshRenderer Beam(string name, Mesh mesh, Material mat, Vector3 a, Vector3 b, float thickness)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        Vector3 edge = b - a;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, edge.normalized);
        go.transform.localScale = new Vector3(thickness, edge.magnitude + thickness, thickness);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    private static Mesh CubeMesh()
    {
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        return mesh;
    }

    private Material NeonMaterial()
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", Color.black);
        m.SetFloat("_Smoothness", 0.2f);
        m.SetFloat("_Metallic", 0f);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor(EmissionId, warm * emissionIntensity);
        return m;
    }
}
