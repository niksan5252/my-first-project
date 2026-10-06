using UnityEngine;

/// Theme Cosmic Rush's ring: a stargate arc over the road. A circle arc (centre ~1 m up, radius ~7 m)
/// that meets the ground 6.8 m out on each side and tops out at 8 m, so the lanes and every note's
/// reach stay inside; a row of lights along it chases round once a bar, the lit one flashing on
/// the kick. Skins:
///   * OrbitStation: a clean double arc, the lights in the second accent;
///   * AsteroidStorm: a jagged, broken arc (every other segment pushed out), embers instead of lights;
///   * NebulaHyperdrive: a gold arc with an inner ring turning a step per beat.
/// Light from ClubBeat (the song clock), so it is on the music in Play and in a recording.
public class RingArchCosmic : MonoBehaviour, IThemeAccent
{
    [SerializeField] private CosmicSkin skin;
    [SerializeField] private float halfBase = 6.8f;
    [SerializeField] private float apex = 8f;
    [SerializeField] private float thickness = 0.16f;
    [SerializeField] private Color color = new Color(0f, 0.9f, 1f);
    [SerializeField, Range(0f, RemixGeo.MaxEmission)] private float emissionIntensity = 1.3f;

    private const int Segments = 16;
    private const int Lights = 12;

    private Material arcMat, lightMat;
    private MeshRenderer[] arc, lights;
    private Transform inner;
    private MeshRenderer[] innerParts;
    private MaterialPropertyBlock mpb;

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        arcMat = RemixGeo.Neon(color, emissionIntensity);
        lightMat = RemixGeo.Neon(Color.white, emissionIntensity);
        Mesh cube = RemixGeo.Primitive(PrimitiveType.Cube), sphere = RemixGeo.Primitive(PrimitiveType.Sphere);

        // Circle through (+-halfBase, -0.5) and (0, apex).
        float c = (apex * apex - halfBase * halfBase - 0.25f) / (2f * apex + 1f);
        float r = apex - c;
        float a0 = Mathf.Atan2(-0.5f - c, halfBase), a1 = Mathf.PI - a0;
        Vector3 At(float a, float rad) => new Vector3(Mathf.Cos(a) * rad, c + Mathf.Sin(a) * rad, 0f);

        int rows = skin == CosmicSkin.OrbitStation ? 2 : 1;
        arc = new MeshRenderer[Segments * rows];
        for (int row = 0; row < rows; row++)
            for (int i = 0; i < Segments; i++)
            {
                float u0 = Mathf.Lerp(a0, a1, i / (float)Segments), u1 = Mathf.Lerp(a0, a1, (i + 1) / (float)Segments);
                float rad = r + row * 0.55f;
                float j0 = 0f, j1 = 0f;
                if (skin == CosmicSkin.AsteroidStorm) { j0 = (i % 2 == 0 ? 0.45f : -0.2f); j1 = ((i + 1) % 2 == 0 ? 0.45f : -0.2f); }
                arc[row * Segments + i] = RemixGeo.Beam(transform, "Arc", cube, arcMat, At(u0, rad + j0), At(u1, rad + j1),
                    skin == CosmicSkin.AsteroidStorm && i % 4 == 3 ? thickness * 0.5f : thickness);
            }

        lights = new MeshRenderer[Lights];
        for (int i = 0; i < Lights; i++)
        {
            float u = Mathf.Lerp(a0, a1, (i + 0.5f) / Lights);
            lights[i] = RemixGeo.Beam(transform, "Light", sphere, lightMat, At(u, r - 0.35f), At(u, r - 0.35f), skin == CosmicSkin.AsteroidStorm ? 0.22f : 0.3f);
        }

        if (skin == CosmicSkin.NebulaHyperdrive)
        {
            inner = new GameObject("Inner").transform;
            inner.SetParent(transform, false);
            inner.localPosition = new Vector3(0f, c, 0f);
            const int seg = 6;
            float ir = r * 0.82f;
            for (int i = 0; i < seg; i++)
            {
                float u0 = i * Mathf.PI * 2f / seg, u1 = (i + 0.55f) * Mathf.PI * 2f / seg;    // six broken arcs
                for (int k = 0; k < 3; k++)
                {
                    float b0 = Mathf.Lerp(u0, u1, k / 3f), b1 = Mathf.Lerp(u0, u1, (k + 1) / 3f);
                    RemixGeo.Beam(inner, "Inner", cube, lightMat, new Vector3(Mathf.Cos(b0), Mathf.Sin(b0), 0f) * ir, new Vector3(Mathf.Cos(b1), Mathf.Sin(b1), 0f) * ir, thickness * 0.6f);
                }
            }
            innerParts = inner.GetComponentsInChildren<MeshRenderer>();
        }
    }

    private void OnDestroy()
    {
        if (arcMat != null) Destroy(arcMat);
        if (lightMat != null) Destroy(lightMat);
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        color = newColor;
        emissionIntensity = Mathf.Min(newIntensity, 1.4f);
    }

    private void Update()
    {
        if (arc == null) return;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float kick = ClubBeat.Pulse;
        float barPulse = Mathf.Pow(1f - Mathf.Repeat(beats, 4f) / 4f, 3f);

        Color c = skin == CosmicSkin.NebulaHyperdrive ? Color.Lerp(color, new Color(1f, 0.8f, 0.35f), 0.5f) : color;
        float level = Mathf.Min(RemixGeo.MaxEmission, emissionIntensity * (0.45f + 0.45f * kick * energy + 0.3f * barPulse * energy));
        mpb.SetColor(RemixGeo.EmissionId, c * level);
        foreach (var r in arc) r.SetPropertyBlock(mpb);

        // The chase: once round per bar; the lit light and its trail.
        float head = Mathf.Repeat(beats, 4f) / 4f * Lights;
        Color lc = skin == CosmicSkin.AsteroidStorm ? new Color(1f, 0.45f, 0.1f) : Color.Lerp(color, Color.white, 0.5f);
        for (int i = 0; i < Lights; i++)
        {
            float d = Mathf.Repeat(head - i, Lights);
            float trail = d < 3f ? 1f - d / 3f : 0f;
            float l = Mathf.Min(RemixGeo.MaxEmission, emissionIntensity * (0.55f + 1.4f * trail * (0.6f + 0.4f * kick) * Mathf.Max(0.6f, energy)));
            mpb.SetColor(RemixGeo.EmissionId, lc * l);
            lights[i].SetPropertyBlock(mpb);
        }

        if (inner != null)
        {
            int steps = Mathf.FloorToInt(beats);
            inner.localRotation = Quaternion.Euler(0f, 0f, steps * 15f + 15f * (1f - Mathf.Pow(1f - ClubBeat.BeatPhase, 4f)));
            mpb.SetColor(RemixGeo.EmissionId, new Color(1f, 0.85f, 0.5f) * Mathf.Min(RemixGeo.MaxEmission, emissionIntensity * (0.5f + 0.8f * kick * energy)));
            foreach (var r in innerParts) r.SetPropertyBlock(mpb);
        }
    }
}
