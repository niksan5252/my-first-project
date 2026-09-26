using UnityEngine;
using UnityEngine.Rendering;

/// Theme_Phonk's ring: an angular gate - two upright posts and a sharp chevron roof - with a row
/// of short vertical neon strips on each post, like the lamp rails of an underground road. Hard
/// edges where the club's halos are round.
///
/// In time with the music (ClubBeat): the frame kicks on every beat; the strips light up like a
/// level meter with the room's energy; and on some beats a ring's lamps drop out for a moment -
/// a different few rings each beat, picked by a hash of the ring and the beat, so the tunnel
/// flickers like failing lights rather than blinking in unison.
///
/// Clearances: the posts stand 4 m either side of the centre, clear of the 6 m road, the outer
/// lanes' high notes (2.25 m out, 2.45 m up) and a Dodge wall (3 m, 2.1 m); the roof starts at 3.4 m.
public class RingArchPhonkGate : MonoBehaviour, IThemeAccent
{
    private const float MaxEmissionIntensity = 2.5f;

    [SerializeField] private float halfWidth = 4f;
    [SerializeField] private float postHeight = 3.4f;
    [SerializeField] private float apexHeight = 5.6f;
    [SerializeField] private float beamThickness = 0.14f;
    [SerializeField] private int stripsPerPost = 4;
    [SerializeField] private Color color = new Color(0.95f, 0.05f, 0.1f);
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float emissionIntensity = 1.3f;

    private Material frameMat, stripMat;
    private MeshRenderer[] frame, strips;
    private MaterialPropertyBlock mpb;
    private int ringSeed;
    private static int nextSeed;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        ringSeed = nextSeed++;
        frameMat = NeonMaterial(color, emissionIntensity);
        stripMat = NeonMaterial(color, emissionIntensity);
        Mesh cube = CubeMesh();

        var l0 = new Vector3(-halfWidth, 0f, 0f); var l1 = new Vector3(-halfWidth, postHeight, 0f);
        var r0 = new Vector3(halfWidth, 0f, 0f); var r1 = new Vector3(halfWidth, postHeight, 0f);
        var apex = new Vector3(0f, apexHeight, 0f);
        // The outline, then a thinner inner line following it - the double stroke is what makes
        // an angular frame read as a lamp fixture rather than scaffolding.
        var inner = 0.32f;
        var il1 = new Vector3(-halfWidth + inner, postHeight - 0.1f, 0f); var ir1 = new Vector3(halfWidth - inner, postHeight - 0.1f, 0f);
        var iapex = new Vector3(0f, apexHeight - inner * 1.2f, 0f);
        frame = new[]
        {
            Beam("Post", cube, frameMat, l0, l1, beamThickness), Beam("Post", cube, frameMat, r0, r1, beamThickness),
            Beam("Roof", cube, frameMat, l1, apex, beamThickness), Beam("Roof", cube, frameMat, r1, apex, beamThickness),
            Beam("Inner", cube, frameMat, il1, iapex, beamThickness * 0.5f), Beam("Inner", cube, frameMat, ir1, iapex, beamThickness * 0.5f),
        };

        strips = new MeshRenderer[stripsPerPost * 2];
        for (int s = 0; s < 2; s++)
            for (int i = 0; i < stripsPerPost; i++)
            {
                float x = (s == 0 ? -1f : 1f) * (halfWidth - 0.28f);
                float y0 = 0.35f + i * (postHeight - 0.5f) / stripsPerPost;
                strips[s * stripsPerPost + i] = Beam("Strip", cube, stripMat, new Vector3(x, y0, 0f), new Vector3(x, y0 + 0.45f, 0f), 0.07f);
            }
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        color = newColor;
        emissionIntensity = Mathf.Min(newIntensity, 1.4f);
        if (frameMat != null)
        {
            SetEmission(frameMat, color, emissionIntensity);
            SetEmission(stripMat, color, emissionIntensity);
        }
    }

    private void Update()
    {
        if (frame == null) return;
        float energy = ClubBeat.Energy;
        int beat = ClubBeat.BeatIndex;

        // Lamps that drop out: on a loud beat roughly one ring in six, for the first third of it.
        bool dropout = energy > 0.7f && Hash(ringSeed, beat) < 0.16f && ClubBeat.BeatPhase < 0.33f;
        float kick = ClubBeat.Pulse * energy;
        float level = dropout ? 0.08f : emissionIntensity * (0.5f + 0.5f * kick);
        mpb.SetColor(EmissionId, color * Mathf.Min(level, MaxEmissionIntensity));
        foreach (var r in frame) r.SetPropertyBlock(mpb);

        // The strips are a level meter: how many are lit follows the energy and the kick.
        float lit = Mathf.Clamp01(0.25f + 0.45f * energy + 0.3f * kick) * stripsPerPost;
        for (int i = 0; i < strips.Length; i++)
        {
            int k = i % stripsPerPost;
            float on = dropout ? 0f : Mathf.Clamp01(lit - k);
            mpb.SetColor(EmissionId, color * Mathf.Min(emissionIntensity * (0.12f + 0.88f * on), MaxEmissionIntensity));
            strips[i].SetPropertyBlock(mpb);
        }
    }

    private static float Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xffff) / 65535f;
        }
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

    private static Material NeonMaterial(Color c, float intensity)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c * 0.1f);
        m.SetFloat("_Smoothness", 0.2f);
        m.SetFloat("_Metallic", 0f);
        SetEmission(m, c, intensity);
        return m;
    }

    private static void SetEmission(Material m, Color c, float intensity)
    {
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor(EmissionId, c * Mathf.Clamp(intensity, 0f, MaxEmissionIntensity));
    }
}
