using System.Collections.Generic;
using UnityEngine;

/// Video 18's rings, one per world (FiestaSkin): a marigold arch under a sugar skull, a carnival
/// feather crown, a Havana colonial portal with a bulb-lit sign, a string of fiesta bulbs between
/// two cacti with a star piñata, a Moorish horseshoe arch in red and white. Feet 6.8 m out, top at
/// ~8 m. Light from ClubBeat; Kenney models (FiestaKit) for the candles, cacti and lanterns.
public partial class RingArchFiesta : MonoBehaviour, IThemeAccent
{
    [SerializeField] private FiestaSkin skin;
    [SerializeField] private float halfBase = 6.8f;
    [SerializeField] private float apex = 8f;
    [SerializeField] private Color color = new Color(1f, 0.5f, 0.1f);
    [SerializeField, Range(0f, RemixGeo.MaxEmission)] private float emissionIntensity = 1.3f;
    [SerializeField] private List<GameObject> kit = new List<GameObject>();

    private Material matA, matB, darkMat, lightMat;
    private readonly List<MeshRenderer> chase = new List<MeshRenderer>();
    private readonly List<Transform> blooms = new List<Transform>();
    private readonly List<Vector3> bloomScale = new List<Vector3>();
    private readonly List<Object> owned = new List<Object>();
    private MaterialPropertyBlock mpb;
    private bool ready;

    private Color SecondColor =>
        skin == FiestaSkin.Muertos ? new Color(1f, 0.15f, 0.6f)
        : skin == FiestaSkin.Carnival ? new Color(0.1f, 1f, 0.35f)
        : skin == FiestaSkin.Havana ? new Color(0.3f, 1f, 0.75f)
        : skin == FiestaSkin.Pinata ? new Color(0.1f, 0.85f, 1f)
        : skin == FiestaSkin.Flamenco ? new Color(1f, 0.92f, 0.8f)
        : FunSecond();

    private void Bloom(Transform t) { if (t == null) return; blooms.Add(t); bloomScale.Add(t.localScale); }

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        ApplyAccent(color, emissionIntensity);
        Mesh cube = RemixGeo.Primitive(PrimitiveType.Cube), sphere = RemixGeo.Primitive(PrimitiveType.Sphere), cyl = RemixGeo.Primitive(PrimitiveType.Cylinder);
        matA = RemixGeo.Neon(color, emissionIntensity);
        matB = RemixGeo.Neon(SecondColor, emissionIntensity);
        darkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        Color dark = skin == FiestaSkin.Havana ? new Color(0.45f, 0.75f, 0.68f)
                   : skin == FiestaSkin.Flamenco ? new Color(0.55f, 0.5f, 0.45f)
                   : skin == FiestaSkin.Pinata ? new Color(0.2f, 0.11f, 0.05f)
                   : new Color(0.07f, 0.05f, 0.09f);
        darkMat.SetColor("_BaseColor", dark);
        darkMat.SetFloat("_Smoothness", 0.4f);
        lightMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        lightMat.SetColor("_BaseColor", new Color(0.92f, 0.9f, 0.88f));
        lightMat.EnableKeyword("_EMISSION");
        lightMat.SetColor("_EmissionColor", new Color(0.25f, 0.24f, 0.23f));
        var props = new FiestaKit(kit, owned);

        float c = (apex * apex - halfBase * halfBase - 0.25f) / (2f * apex + 1f);
        float r = apex - c;
        float a0 = Mathf.Atan2(-0.5f - c, halfBase), a1 = Mathf.PI - a0;
        Vector3 At(float a, float rad) => new Vector3(Mathf.Cos(a) * rad, c + Mathf.Sin(a) * rad, 0f);

        switch (skin)
        {
            case FiestaSkin.Muertos:
            {
                // An arch packed with marigolds, a sugar skull at the top, candles at the feet.
                const int seg = 22;
                for (int i = 0; i <= seg; i++)
                {
                    Vector3 p = At(Mathf.Lerp(a0, a1, i / (float)seg), r);
                    chase.Add(RemixGeo.Beam(transform, "Marigold", sphere, i % 2 == 0 ? matA : matB, p, p, 0.55f));
                }
                var skull = new GameObject("Skull").transform;
                skull.SetParent(transform, false);
                skull.localPosition = new Vector3(0f, apex + 0.6f, -0.3f);
                Place(skull, "Head", sphere, lightMat, Vector3.zero, new Vector3(1.6f, 1.5f, 1.2f));
                Place(skull, "Jaw", sphere, lightMat, new Vector3(0f, -0.65f, 0f), new Vector3(1.1f, 0.7f, 0.9f));
                foreach (int s in new[] { -1, 1 })
                {
                    Place(skull, "Eye", sphere, matB, new Vector3(s * 0.38f, 0.05f, -0.5f), new Vector3(0.5f, 0.5f, 0.3f));
                    Place(skull, "Petal", sphere, matA, new Vector3(s * 0.55f, 0.55f, -0.5f), new Vector3(0.22f, 0.22f, 0.1f));
                }
                Place(skull, "Nose", sphere, darkMat, new Vector3(0f, -0.3f, -0.58f), new Vector3(0.18f, 0.22f, 0.1f));
                Bloom(skull);
                foreach (int side in new[] { -1, 1 })
                    props.Spawn("candle-multiple", transform, new Vector3(side * (halfBase + 0.4f), -0.5f, -0.4f), 0.8f, 0f, new Color(1f, 0.85f, 0.7f), 0.3f);
                break;
            }
            case FiestaSkin.Carnival:
            {
                // A round arch with a crown of feathers fanning out over it, bulbs along the arch.
                const int seg = 16;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.Lerp(a0, a1, i / (float)seg), u1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                    RemixGeo.Beam(transform, "Arch", cube, darkMat, At(u0, r), At(u1, r), 0.5f);
                    Vector3 p = At(u0, r) + Vector3.back * 0.3f;
                    chase.Add(RemixGeo.Beam(transform, "Bulb", sphere, i % 2 == 0 ? matA : matB, p, p, 0.3f));
                }
                var crown = new GameObject("Crown").transform;
                crown.SetParent(transform, false);
                crown.localPosition = new Vector3(0f, apex - 0.4f, 0.2f);
                for (int k = 0; k < 15; k++)
                {
                    float a = Mathf.Lerp(-70f, 70f, k / 14f) * Mathf.Deg2Rad;
                    Vector3 d = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
                    chase.Add(RemixGeo.Beam(crown, "Feather", cube, k % 2 == 0 ? matA : matB, d * 0.6f, d * (k % 2 == 0 ? 4.5f : 3.6f), 0.3f));
                }
                Place(crown, "Jewel", sphere, matA, Vector3.back * 0.3f, Vector3.one * 1f);
                Bloom(crown);
                break;
            }
            case FiestaSkin.Havana:
            {
                // A colonial portal: two columns, a lintel and a sign framed in bulbs.
                float x = halfBase - 0.3f, top = apex - 1.4f;
                foreach (int side in new[] { -1, 1 })
                {
                    RemixGeo.Beam(transform, "Column", cyl, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, top, 0f), 0.9f);
                    RemixGeo.Beam(transform, "Capital", cube, lightMat, new Vector3(side * x, top, 0f), new Vector3(side * x, top + 0.3f, 0f), 1.3f);
                    props.Spawn("tree_palmShort", transform, new Vector3(side * (x + 1.3f), -0.5f, 0.5f), 4.5f, side * 40f, new Color(0.6f, 1f, 0.8f), 0.35f);
                }
                RemixGeo.Beam(transform, "Lintel", cube, lightMat, new Vector3(-x - 0.7f, top + 0.7f, 0f), new Vector3(x + 0.7f, top + 0.7f, 0f), 0.9f);
                var sign = new GameObject("Sign").transform;
                sign.SetParent(transform, false);
                sign.localPosition = new Vector3(0f, top + 2.1f, -0.2f);
                Place(sign, "Board", cube, darkMat, Vector3.zero, new Vector3(6f, 1.6f, 0.2f));
                for (int k = 0; k < 18; k++)
                {
                    float u = k / 18f * 4f;
                    Vector3 p = u < 1f ? new Vector3(Mathf.Lerp(-3f, 3f, u), 0.8f, -0.15f)
                              : u < 2f ? new Vector3(3f, Mathf.Lerp(0.8f, -0.8f, u - 1f), -0.15f)
                              : u < 3f ? new Vector3(Mathf.Lerp(3f, -3f, u - 2f), -0.8f, -0.15f)
                              : new Vector3(-3f, Mathf.Lerp(-0.8f, 0.8f, u - 3f), -0.15f);
                    chase.Add(Place(sign, "Bulb", sphere, matB, p, Vector3.one * 0.22f));
                }
                for (int k = 0; k < 6; k++)
                    Place(sign, "Letter", cube, matA, new Vector3(-2.2f + k * 0.88f, 0f, -0.15f), new Vector3(0.5f, 0.9f, 0.05f));
                Bloom(sign);
                break;
            }
            case FiestaSkin.Pinata:
            {
                // Two tall cacti holding a sagging string of bulbs, a star piñata hanging in the middle.
                float x = halfBase + 0.2f;
                foreach (int side in new[] { -1, 1 })
                {
                    RemixGeo.Beam(transform, "Pole", cube, darkMat, new Vector3(side * (x - 0.3f), -0.5f, -0.3f), new Vector3(side * (x - 0.3f), apex - 0.2f, -0.3f), 0.25f);
                    props.Spawn("cactus_tall", transform, new Vector3(side * (x + 1.2f), -0.5f, 0.3f), 4.2f, side * 90f, new Color(0.45f, 0.8f, 0.4f), 0.25f);
                }
                Vector3 prev = new Vector3(-x + 0.3f, apex - 0.3f, -0.3f);
                for (int k = 1; k <= 16; k++)
                {
                    float u = k / 16f;
                    Vector3 p = new Vector3(Mathf.Lerp(-x + 0.3f, x - 0.3f, u), apex - 0.3f - Mathf.Sin(u * Mathf.PI) * 1.2f, -0.3f);
                    RemixGeo.Beam(transform, "Cord", cube, darkMat, prev, p, 0.05f);
                    if (k < 16) chase.Add(Place(transform, "Bulb", sphere, k % 2 == 0 ? matA : matB, p + Vector3.down * 0.2f, Vector3.one * 0.35f));
                    prev = p;
                }
                var star = new GameObject("Pinata").transform;
                star.SetParent(transform, false);
                star.localPosition = new Vector3(0f, apex - 2.9f, -0.3f);
                Place(star, "Core", sphere, matA, Vector3.zero, Vector3.one * 0.8f);
                for (int k = 0; k < 7; k++)
                {
                    float a = k * Mathf.PI * 2f / 7f + Mathf.PI / 2f;
                    Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    RemixGeo.Beam(star, "Spike", cube, k % 2 == 0 ? matB : matA, d * 0.35f, d * 1.1f, 0.3f);
                }
                Bloom(star);
                break;
            }
            default:
                BuildFun(cube, sphere, cyl, props, c, r, a0, a1);
                break;
            case FiestaSkin.Flamenco:
            {
                // A Moorish horseshoe arch, alternating red and white voussoirs, lanterns at the springing.
                float x = halfBase - 0.2f;
                float cy = 4.6f, R = x - 0.4f;
                foreach (int side in new[] { -1, 1 })
                    RemixGeo.Beam(transform, "Pier", cube, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, apex + 0.6f, 0f), 1.1f);
                RemixGeo.Beam(transform, "Lintel", cube, darkMat, new Vector3(-x - 0.5f, apex + 0.6f, 0f), new Vector3(x + 0.5f, apex + 0.6f, 0f), 0.8f);
                const int seg = 18;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.Lerp(-25f, 205f, i / (float)seg) * Mathf.Deg2Rad, u1 = Mathf.Lerp(-25f, 205f, (i + 1) / (float)seg) * Mathf.Deg2Rad;
                    Vector3 p0 = new Vector3(Mathf.Cos(u0) * R, cy + Mathf.Sin(u0) * R * 0.62f, -0.5f), p1 = new Vector3(Mathf.Cos(u1) * R, cy + Mathf.Sin(u1) * R * 0.62f, -0.5f);
                    chase.Add(RemixGeo.Beam(transform, "Voussoir", cube, i % 2 == 0 ? matA : matB, p0, p1, 0.45f));
                }
                foreach (int side in new[] { -1, 1 })
                    Bloom(props.Spawn("lantern-candle", transform, new Vector3(side * (R - 0.2f), cy - 2.4f, -0.6f), 1.1f, 0f, new Color(1f, 0.85f, 0.6f), 1f));
                break;
            }
        }
        ready = true;
    }

    private MeshRenderer Place(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 scale)
    {
        var mr = RemixGeo.Beam(parent, name, mesh, mat, pos, pos, 1f);
        mr.transform.localScale = scale;
        return mr;
    }

    private void OnDestroy()
    {
        if (matA != null) Destroy(matA);
        if (matB != null) Destroy(matB);
        if (darkMat != null) Destroy(darkMat);
        if (lightMat != null) Destroy(lightMat);
        foreach (var o in owned) if (o != null) Destroy(o);
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        color = skin == FiestaSkin.Muertos ? new Color(1f, 0.55f, 0.05f)
              : skin == FiestaSkin.Carnival ? new Color(1f, 0.85f, 0.1f)
              : skin == FiestaSkin.Havana ? new Color(1f, 0.2f, 0.6f)
              : skin == FiestaSkin.Pinata ? new Color(1f, 0.15f, 0.65f)
              : skin == FiestaSkin.Flamenco ? new Color(1f, 0.1f, 0.15f)
              : FunPrimary();
        emissionIntensity = Mathf.Min(newIntensity, 1.4f);
    }

    private void Update()
    {
        if (!ready || mpb == null || matA == null) return;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float kick = ClubBeat.Pulse;
        float barPulse = Mathf.Pow(1f - Mathf.Repeat(beats, 4f) / 4f, 3f);
        float baseLevel = emissionIntensity * (0.4f + 0.4f * kick * energy + 0.25f * barPulse * energy);
        Color bCol = SecondColor;
        RemixGeo.SetEmission(matA, color, Mathf.Min(RemixGeo.MaxEmission, baseLevel));
        RemixGeo.SetEmission(matB, bCol, Mathf.Min(RemixGeo.MaxEmission, baseLevel * 0.9f));

        if (chase.Count > 0)
        {
            float head = Mathf.Repeat(beats, 4f) / 4f * chase.Count;
            for (int i = 0; i < chase.Count; i++)
            {
                float d = Mathf.Abs(head - i);
                d = Mathf.Min(d, chase.Count - d);
                float trail = d < 3f ? 1f - d / 3f : 0f;
                Color lc = Color.Lerp(chase[i].sharedMaterial == matA ? color : bCol, Color.white, 0.3f * trail);
                mpb.SetColor(RemixGeo.EmissionId, lc * Mathf.Min(RemixGeo.MaxEmission, emissionIntensity * (0.45f + 1.3f * trail * Mathf.Max(0.6f, energy))));
                chase[i].SetPropertyBlock(mpb);
            }
        }
        for (int i = 0; i < blooms.Count; i++)
        {
            float s = 1f + 0.15f * kick * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(i * 1.3f + beats * 0.5f)));
            blooms[i].localScale = bloomScale[i] * s;
            if (skin == FiestaSkin.Pinata) blooms[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(beats * Mathf.PI * 0.5f) * 12f);
        }
    }
}
