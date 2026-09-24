using UnityEngine;
using UnityEngine.Rendering;

/// Theme_Desert's ring-arch shape: a wooden/bronze doorway gate frame (two posts + a top
/// lintel, open at the bottom) instead of the sci-fi theme's procedural octagon. This is what
/// actually makes Desert read as a distinct theme rather than the same ring shape recolored — a
/// flat recolor of the octagon was the explicit thing to avoid here.
///
/// Self-builds in Awake(), so a plain Instantiate() from NeonTunnelGenerator.BuildRings() is
/// enough (no separate Initialize call needed), and implements IThemeAccent so the tunnel still
/// recolors it per-ring, alternating the theme's two accent colors exactly like it does for the
/// procedural octagon rings. Being open at the bottom (no closing edge across the road at all)
/// also sidesteps the ring/road-overlap class of bug entirely, by construction.
public class RingArchWesternGate : MonoBehaviour, IThemeAccent
{
    private const float MaxEmissionIntensity = 2.5f;

    [SerializeField] private float archHalfWidth = 3.3f;
    [SerializeField] private float archHeight = 6.5f;
    [SerializeField] private float beamThickness = 0.3f;
    [SerializeField] private Color color = new Color(1f, 0.478f, 0.102f);
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float emissionIntensity = 1.4f;
    // Optional real diffuse detail on the beams themselves (metal trim/hardware), on top of the
    // neon emission edge. Only visible if the scene actually has a Light — see TunnelTheme's
    // lightIntensity, which is what makes this worth having at all for this theme.
    [SerializeField] private Texture2D beamBaseMap;
    [SerializeField] private Texture2D beamNormalMap;
    [SerializeField] private Vector2 beamTextureTiling = new Vector2(1f, 4f);

    private Material mat;
    private Mesh cubeMesh;

    private void Awake()
    {
        cubeMesh = GetSharedCubeMesh();
        mat = CreateNeonMaterial(color, emissionIntensity);

        CreateBeam(new Vector3(-archHalfWidth, 0f, 0f), new Vector3(-archHalfWidth, archHeight, 0f));
        CreateBeam(new Vector3(archHalfWidth, 0f, 0f), new Vector3(archHalfWidth, archHeight, 0f));
        CreateBeam(new Vector3(-archHalfWidth, archHeight, 0f), new Vector3(archHalfWidth, archHeight, 0f));
    }

    // Called by NeonTunnelGenerator.BuildRings() right after Instantiate(), alternating the
    // theme's two accent colors per ring — same contract as the procedural octagon path.
    public void ApplyAccent(Color newColor, float newIntensity)
    {
        color = newColor;
        emissionIntensity = newIntensity;
        if (mat != null) ApplyCappedEmission(mat, color, emissionIntensity);
    }

    private void CreateBeam(Vector3 a, Vector3 b)
    {
        var go = new GameObject("Beam");
        go.transform.SetParent(transform, false);

        Vector3 edge = b - a;
        float length = edge.magnitude;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, edge.normalized);
        go.transform.localScale = new Vector3(beamThickness, length, beamThickness);

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = cubeMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    private Mesh GetSharedCubeMesh()
    {
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        return mesh;
    }

    private Material CreateNeonMaterial(Color c, float intensity)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (beamBaseMap != null)
        {
            m.SetTexture("_BaseMap", beamBaseMap);
            m.SetTextureScale("_BaseMap", beamTextureTiling);
            m.SetColor("_BaseColor", Color.Lerp(Color.white, c, 0.35f)); // let real texture detail read, just tinted toward the accent hue
            if (beamNormalMap != null)
            {
                m.SetTexture("_BumpMap", beamNormalMap);
                m.SetTextureScale("_BumpMap", beamTextureTiling);
                m.EnableKeyword("_NORMALMAP");
            }
            m.SetFloat("_Smoothness", 0.5f);
            m.SetFloat("_Metallic", 0.6f);
        }
        else
        {
            m.SetColor("_BaseColor", c * 0.12f);
            m.SetFloat("_Smoothness", 0.15f);
            m.SetFloat("_Metallic", 0f);
        }
        ApplyCappedEmission(m, c, intensity);
        return m;
    }

    private void ApplyCappedEmission(Material m, Color c, float intensity)
    {
        float clamped = Mathf.Clamp(intensity, 0f, MaxEmissionIntensity);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor("_EmissionColor", c * clamped);
    }
}
