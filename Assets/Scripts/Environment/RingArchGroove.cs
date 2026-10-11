using System.Collections.Generic;
using UnityEngine;

/// Video 16's rings, one per world (GrooveSkin): a mirror arch with a disco ball, a metro tunnel
/// mouth with signals, two palms meeting over the road, a hazard gantry with robot arms, a castle
/// gate with a portcullis. Feet 6.8 m out, top at ~8 m. Light from ClubBeat.
public class RingArchGroove : MonoBehaviour, IThemeAccent
{
    [SerializeField] private GrooveSkin skin;
    [SerializeField] private float halfBase = 6.8f;
    [SerializeField] private float apex = 8f;
    [SerializeField] private Color color = new Color(1f, 0.2f, 0.2f);
    [SerializeField, Range(0f, RemixGeo.MaxEmission)] private float emissionIntensity = 1.3f;

    private Material matA, matB, darkMat, lightMat;
    private readonly List<MeshRenderer> chase = new List<MeshRenderer>();
    private readonly List<Transform> blooms = new List<Transform>();
    private MaterialPropertyBlock mpb;
    private bool ready;

    private Color SecondColor =>
        skin == GrooveSkin.Disco ? new Color(0.1f, 0.9f, 1f)
        : skin == GrooveSkin.Subway ? new Color(0.2f, 1f, 0.4f)
        : skin == GrooveSkin.Miami ? new Color(0.2f, 0.95f, 1f)
        : skin == GrooveSkin.Robo ? new Color(0.1f, 0.9f, 1f)
        : new Color(1f, 0.8f, 0.25f);

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        ApplyAccent(color, emissionIntensity);
        Mesh cube = RemixGeo.Primitive(PrimitiveType.Cube), sphere = RemixGeo.Primitive(PrimitiveType.Sphere), cyl = RemixGeo.Primitive(PrimitiveType.Cylinder);
        matA = RemixGeo.Neon(color, emissionIntensity);
        matB = RemixGeo.Neon(SecondColor, emissionIntensity);
        darkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        Color dark = skin == GrooveSkin.Miami ? new Color(0.25f, 0.15f, 0.1f)
                   : skin == GrooveSkin.Castle ? new Color(0.25f, 0.24f, 0.23f)
                   : skin == GrooveSkin.Subway ? new Color(0.5f, 0.52f, 0.55f)
                   : new Color(0.07f, 0.07f, 0.09f);
        darkMat.SetColor("_BaseColor", dark);
        darkMat.SetFloat("_Smoothness", 0.4f);
        lightMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        lightMat.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.85f));

        float c = (apex * apex - halfBase * halfBase - 0.25f) / (2f * apex + 1f);
        float r = apex - c;
        float a0 = Mathf.Atan2(-0.5f - c, halfBase), a1 = Mathf.PI - a0;
        Vector3 At(float a, float rad) => new Vector3(Mathf.Cos(a) * rad, c + Mathf.Sin(a) * rad, 0f);

        switch (skin)
        {
            case GrooveSkin.Disco:
            {
                // A mirror arch: tiles round the circle, neon and chrome in turn, a ball on top.
                const int seg = 30;
                for (int i = 0; i <= seg; i++)
                {
                    Vector3 p = At(Mathf.Lerp(a0, a1, i / (float)seg), r);
                    chase.Add(RemixGeo.Beam(transform, "Tile", cube, i % 2 == 0 ? matA : matB, p, p, 0.5f));
                }
                var ball = new GameObject("Ball").transform;
                ball.SetParent(transform, false);
                ball.localPosition = new Vector3(0f, apex + 0.9f, -0.2f);
                RemixGeo.Beam(ball, "Core", sphere, lightMat, Vector3.zero, Vector3.zero, 1.2f);
                for (int k = 0; k < 10; k++)
                {
                    float a = k * Mathf.PI / 5f;
                    RemixGeo.Beam(ball, "Facet", cube, k % 2 == 0 ? matA : matB, new Vector3(Mathf.Cos(a), Mathf.Sin(a), -0.3f) * 0.6f, new Vector3(Mathf.Cos(a), Mathf.Sin(a), -0.3f) * 0.6f, 0.2f);
                }
                blooms.Add(ball);
                break;
            }
            case GrooveSkin.Subway:
            {
                // The tunnel mouth: a squared frame of tiles, signal lights on both sides.
                float x = halfBase - 0.3f, top = apex - 0.5f;
                RemixGeo.Beam(transform, "Side", cube, darkMat, new Vector3(-x, -0.5f, 0f), new Vector3(-x, top, 0f), 0.8f);
                RemixGeo.Beam(transform, "Side", cube, darkMat, new Vector3(x, -0.5f, 0f), new Vector3(x, top, 0f), 0.8f);
                RemixGeo.Beam(transform, "Top", cube, darkMat, new Vector3(-x, top, 0f), new Vector3(x, top, 0f), 0.8f);
                for (int i = 0; i < 14; i++) chase.Add(RemixGeo.Beam(transform, "Strip", cube, matA, new Vector3(Mathf.Lerp(-x + 0.5f, x - 0.5f, i / 13f), top - 0.45f, -0.42f), new Vector3(Mathf.Lerp(-x + 0.5f, x - 0.5f, i / 13f), top - 0.45f, -0.42f), 0.3f));
                foreach (int side in new[] { -1, 1 })
                {
                    RemixGeo.Beam(transform, "Signal", cube, darkMat, new Vector3(side * (x - 0.55f), 3.2f, -0.4f), new Vector3(side * (x - 0.55f), 4.6f, -0.4f), 0.45f);
                    blooms.Add(RemixGeo.Beam(transform, "Red", sphere, matA, new Vector3(side * (x - 0.55f), 4.3f, -0.65f), new Vector3(side * (x - 0.55f), 4.3f, -0.65f), 0.3f).transform);
                    blooms.Add(RemixGeo.Beam(transform, "Green", sphere, matB, new Vector3(side * (x - 0.55f), 3.5f, -0.65f), new Vector3(side * (x - 0.55f), 3.5f, -0.65f), 0.3f).transform);
                }
                break;
            }
            case GrooveSkin.Miami:
            {
                // Two palm trunks leaning in, their fronds meeting over the road, a neon bar.
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 a = new Vector3(side * halfBase, -0.5f, 0f);
                    for (int k = 0; k < 7; k++)
                    {
                        float u = (k + 1) / 7f;
                        Vector3 b = new Vector3(side * (halfBase - Mathf.Pow(u, 2f) * 3.2f), -0.5f + u * (apex - 0.2f), 0f);
                        RemixGeo.Beam(transform, "Trunk", cube, darkMat, a, b, 0.45f - k * 0.03f);
                        a = b;
                    }
                    for (int f = 0; f < 5; f++)
                    {
                        float ang = Mathf.Lerp(-0.3f, 1.3f, f / 4f) * Mathf.PI;
                        chase.Add(RemixGeo.Beam(transform, "Frond", cube, f % 2 == 0 ? matA : matB, a, a + new Vector3(-side * Mathf.Cos(ang) * 2.2f, Mathf.Sin(ang) * 0.8f - 0.6f, 0f), 0.18f));
                    }
                }
                blooms.Add(RemixGeo.Beam(transform, "Neon", cube, matA, new Vector3(-2.2f, apex - 1.4f, -0.3f), new Vector3(2.2f, apex - 1.4f, -0.3f), 0.16f).transform);
                break;
            }
            case GrooveSkin.Robo:
            {
                // A gantry with hazard stripes, two arms hanging from it.
                float x = halfBase - 0.3f, top = apex - 0.6f;
                RemixGeo.Beam(transform, "Post", cube, darkMat, new Vector3(-x, -0.5f, 0f), new Vector3(-x, top, 0f), 0.7f);
                RemixGeo.Beam(transform, "Post", cube, darkMat, new Vector3(x, -0.5f, 0f), new Vector3(x, top, 0f), 0.7f);
                for (int i = 0; i < 16; i++)
                {
                    float px = Mathf.Lerp(-x, x, (i + 0.5f) / 16f);
                    var m = i % 2 == 0 ? matA : darkMat;
                    var r0 = RemixGeo.Beam(transform, "Hazard", cube, m, new Vector3(px, top, 0f), new Vector3(px, top, 0f), 2f * x / 16f);
                    if (m == matA) chase.Add(r0);
                }
                foreach (int side in new[] { -1, 1 })
                {
                    var arm = new GameObject("Arm").transform;
                    arm.SetParent(transform, false);
                    arm.localPosition = new Vector3(side * 2.4f, top - 0.3f, -0.3f);
                    RemixGeo.Beam(arm, "Upper", cube, lightMat, Vector3.zero, new Vector3(-side * 0.6f, -1.2f, 0f), 0.25f);
                    RemixGeo.Beam(arm, "Fore", cube, lightMat, new Vector3(-side * 0.6f, -1.2f, 0f), new Vector3(0f, -2f, 0f), 0.2f);
                    RemixGeo.Beam(arm, "Claw", sphere, matB, new Vector3(0f, -2.1f, 0f), new Vector3(0f, -2.1f, 0f), 0.35f);
                    blooms.Add(arm);
                }
                break;
            }
            default:    // Castle
            {
                // A stone gate: towers either side, a portcullis grid in the arch, two banners.
                float x = halfBase - 0.3f, top = apex - 0.8f;
                foreach (int side in new[] { -1, 1 })
                {
                    RemixGeo.Beam(transform, "Tower", cube, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, top + 1.2f, 0f), 1.2f);
                    for (int k = 0; k < 2; k++) RemixGeo.Beam(transform, "Merlon", cube, darkMat, new Vector3(side * (x - 0.35f + k * 0.7f), top + 1.6f, 0f), new Vector3(side * (x - 0.35f + k * 0.7f), top + 1.6f, 0f), 0.4f);
                    var banner = RemixGeo.Beam(transform, "Banner", cube, matB, new Vector3(side * (x - 0.65f), top, -0.5f), new Vector3(side * (x - 0.65f), top - 2.6f, -0.5f), 0.9f);
                    banner.transform.localScale = new Vector3(0.9f, banner.transform.localScale.y, 0.06f);
                    blooms.Add(banner.transform);
                }
                RemixGeo.Beam(transform, "Lintel", cube, darkMat, new Vector3(-x, top + 0.6f, 0f), new Vector3(x, top + 0.6f, 0f), 1f);
                for (int i = 0; i < 9; i++)
                {
                    float px = Mathf.Lerp(-x + 1f, x - 1f, i / 8f);
                    chase.Add(RemixGeo.Beam(transform, "Bar", cube, matA, new Vector3(px, top, -0.2f), new Vector3(px, top - 1.4f, -0.2f), 0.12f));
                }
                chase.Add(RemixGeo.Beam(transform, "Cross", cube, matA, new Vector3(-x + 1f, top - 0.7f, -0.2f), new Vector3(x - 1f, top - 0.7f, -0.2f), 0.1f));
                break;
            }
        }
        ready = true;
    }

    private void OnDestroy()
    {
        if (matA != null) Destroy(matA);
        if (matB != null) Destroy(matB);
        if (darkMat != null) Destroy(darkMat);
        if (lightMat != null) Destroy(lightMat);
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        color = skin == GrooveSkin.Disco ? new Color(1f, 0.2f, 0.7f)
              : skin == GrooveSkin.Subway ? new Color(1f, 0.8f, 0.1f)
              : skin == GrooveSkin.Miami ? new Color(1f, 0.35f, 0.65f)
              : skin == GrooveSkin.Robo ? new Color(1f, 0.8f, 0.1f)
              : new Color(1f, 0.5f, 0.12f);
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
            float s = 1f + 0.25f * kick * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(i * 1.3f + beats * 0.5f)));
            blooms[i].localScale = Vector3.one * s * (skin == GrooveSkin.Subway ? 1f : 1f);
        }
    }
}
