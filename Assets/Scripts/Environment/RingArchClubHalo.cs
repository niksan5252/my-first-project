using UnityEngine;
using UnityEngine.Rendering;

/// Theme_Club's ring: a round neon halo standing over the road, open at the bottom, with a row of
/// bulbs along it - the stage-light rings of a club tunnel instead of the sci-fi octagon. The tube
/// breathes with the beat and the bulbs chase around it, two steps per beat, both scaled by the
/// room's energy (ClubBeat), so the tunnel pumps harder on a drop.
///
/// Geometry: a circle of `radius` centred `centerHeight` above the road, drawn from `openAngle`
/// below the horizontal on one side, over the top, to the same on the other. At the defaults
/// (4.2 m, centre 1 m) it clears the outer lanes' highest notes (2.25, 2.45 -> 2.7 m from the
/// centre), a Dodge wall's top corner (3, 2.1 -> 3.2 m) and meets the ground outside the 6 m road.
///
/// Self-builds in Awake so NeonTunnelGenerator's plain Instantiate is enough, and implements
/// IThemeAccent so the tunnel alternates the theme's two colours ring by ring.
public class RingArchClubHalo : MonoBehaviour, IThemeAccent
{
    private const float MaxEmissionIntensity = 2.5f;

    [SerializeField] private float radius = 4.2f;
    [SerializeField] private float centerHeight = 1f;
    [SerializeField] private float openAngle = 14f;       // degrees below horizontal at each end
    [SerializeField] private int segments = 28;
    [SerializeField] private float tubeThickness = 0.16f;
    [SerializeField] private int bulbEvery = 2;
    [SerializeField] private float bulbSize = 0.2f;
    [SerializeField] private Color color = new Color(1f, 0.45f, 0.05f);
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float emissionIntensity = 1.3f;

    private Material tubeMat, bulbMat;
    private MeshRenderer[] tube, bulbs;
    private MaterialPropertyBlock mpb;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        tubeMat = NeonMaterial(color, emissionIntensity);
        bulbMat = NeonMaterial(Color.Lerp(color, Color.white, 0.25f), emissionIntensity);
        Mesh cube = PrimitiveMesh(PrimitiveType.Cube), sphere = PrimitiveMesh(PrimitiveType.Sphere);

        float a0 = -openAngle * Mathf.Deg2Rad, a1 = Mathf.PI + openAngle * Mathf.Deg2Rad;
        var center = new Vector3(0f, centerHeight, 0f);
        Vector3 P(float a) => center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
        tube = new MeshRenderer[segments];
        int bulbCount = segments / Mathf.Max(1, bulbEvery) + 1;
        bulbs = new MeshRenderer[bulbCount];
        int b = 0;
        for (int i = 0; i < segments; i++)
        {
            float ta = Mathf.Lerp(a0, a1, i / (float)segments), tb = Mathf.Lerp(a0, a1, (i + 1) / (float)segments);
            tube[i] = Part("Tube", cube, tubeMat, P(ta), P(tb), tubeThickness);
            if (i % bulbEvery == 0 && b < bulbCount)
            {
                var bulb = Part("Bulb", sphere, bulbMat, P(ta), P(ta), bulbSize);
                bulb.transform.localPosition = P(ta) + Vector3.back * 0.12f;
                bulbs[b++] = bulb;
            }
        }
        System.Array.Resize(ref bulbs, b);
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        color = newColor;
        emissionIntensity = Mathf.Min(newIntensity, 1.4f);
        if (tubeMat != null)
        {
            SetEmission(tubeMat, color, emissionIntensity);
            SetEmission(bulbMat, Color.Lerp(color, Color.white, 0.25f), emissionIntensity);
        }
    }

    private void Update()
    {
        if (tube == null) return;
        float energy = ClubBeat.Energy;
        // The tube breathes: a floor so it always reads as a ring, a kick on every beat on top.
        float tubeLevel = emissionIntensity * (0.55f + 0.45f * ClubBeat.Pulse * energy);
        mpb.SetColor(EmissionId, color * Mathf.Min(tubeLevel, MaxEmissionIntensity));
        foreach (var r in tube) r.SetPropertyBlock(mpb);

        // Bulbs chase around the halo, two steps per beat; the lit one and a fading tail.
        int step = Mathf.FloorToInt((ClubBeat.BeatIndex + ClubBeat.BeatPhase) * 2f);
        Color bulbColor = Color.Lerp(color, Color.white, 0.25f);
        for (int i = 0; i < bulbs.Length; i++)
        {
            int d = ((step - i) % 4 + 4) % 4;
            float lit = d == 0 ? 1f : d == 1 ? 0.45f : 0.12f;
            mpb.SetColor(EmissionId, bulbColor * Mathf.Min(emissionIntensity * lit * (0.6f + 0.6f * energy), MaxEmissionIntensity));
            bulbs[i].SetPropertyBlock(mpb);
        }
    }

    private MeshRenderer Part(string name, Mesh mesh, Material mat, Vector3 a, Vector3 b, float thickness)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        Vector3 edge = b - a;
        float length = edge.magnitude;
        go.transform.localPosition = (a + b) * 0.5f;
        if (length > 1e-4f)
        {
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, edge / length);
            go.transform.localScale = new Vector3(thickness, length * 1.04f, thickness);
        }
        else go.transform.localScale = Vector3.one * thickness;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    private static Mesh PrimitiveMesh(PrimitiveType type)
    {
        var temp = GameObject.CreatePrimitive(type);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        return mesh;
    }

    private static Material NeonMaterial(Color c, float intensity)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c * 0.12f);
        m.SetFloat("_Smoothness", 0.15f);
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
