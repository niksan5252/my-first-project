using System.Collections.Generic;
using UnityEngine;

/// Video 17's rings, one per world (LegendSkin): a dinosaur ribcage, a gothic rune arch, a starship
/// bulkhead hatch, a Roman triumphal arch, dragon-headed viking posts with shields. Feet 6.8 m out,
/// top at ~8 m. Light from ClubBeat.
public class RingArchLegends : MonoBehaviour, IThemeAccent
{
    [SerializeField] private LegendSkin skin;
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
        skin == LegendSkin.Jurassic ? new Color(0.4f, 1f, 0.3f)
        : skin == LegendSkin.Magic ? new Color(0.2f, 0.85f, 1f)
        : skin == LegendSkin.Starship ? new Color(1f, 0.55f, 0.1f)
        : skin == LegendSkin.Rome ? new Color(0.9f, 0.1f, 0.15f)
        : new Color(0.45f, 0.8f, 1f);

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        ApplyAccent(color, emissionIntensity);
        Mesh cube = RemixGeo.Primitive(PrimitiveType.Cube), sphere = RemixGeo.Primitive(PrimitiveType.Sphere), cyl = RemixGeo.Primitive(PrimitiveType.Cylinder);
        matA = RemixGeo.Neon(color, emissionIntensity);
        matB = RemixGeo.Neon(SecondColor, emissionIntensity);
        darkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        Color dark = skin == LegendSkin.Rome ? new Color(0.6f, 0.57f, 0.52f)
                   : skin == LegendSkin.Viking ? new Color(0.2f, 0.11f, 0.05f)
                   : skin == LegendSkin.Starship ? new Color(0.16f, 0.17f, 0.2f)
                   : skin == LegendSkin.Magic ? new Color(0.12f, 0.1f, 0.15f)
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
            case LegendSkin.Jurassic:
            {
                // A dinosaur's ribcage: bone ribs round the road, a spine along the top.
                const int seg = 14;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.Lerp(a0, a1, i / (float)seg), u1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                    RemixGeo.Beam(transform, "Bone", cube, lightMat, At(u0, r), At(u1, r), 0.55f);
                }
                for (int i = 1; i < 10; i++)
                {
                    Vector3 p = At(Mathf.Lerp(a0, a1, i / 10f), r);
                    chase.Add(RemixGeo.Beam(transform, "Vertebra", sphere, i % 2 == 0 ? matA : matB, p + Vector3.back * 0.1f, p + Vector3.back * 0.1f, 0.45f));
                }
                foreach (int side in new[] { -1, 1 })
                    blooms.Add(RemixGeo.Beam(transform, "Tusk", cube, matA, new Vector3(side * (halfBase - 0.2f), 0.5f, -0.2f), new Vector3(side * (halfBase - 1.2f), 2f, -0.2f), 0.3f).transform);
                break;
            }
            case LegendSkin.Magic:
            {
                // A gothic pointed arch with runes glowing along it.
                float x = halfBase - 0.3f;
                foreach (int side in new[] { -1, 1 })
                {
                    RemixGeo.Beam(transform, "Pillar", cube, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, 4.5f, 0f), 0.8f);
                    Vector3 a = new Vector3(side * x, 4.5f, 0f);
                    for (int k = 1; k <= 8; k++)
                    {
                        float u = k / 8f;
                        Vector3 b = new Vector3(side * x * (1f - Mathf.Pow(u, 1.6f)), 4.5f + Mathf.Sin(u * Mathf.PI * 0.5f) * (apex - 3.6f), 0f);
                        RemixGeo.Beam(transform, "Arch", cube, darkMat, a, b, 0.7f);
                        chase.Add(RemixGeo.Beam(transform, "Rune", cube, k % 2 == 0 ? matA : matB, Vector3.Lerp(a, b, 0.5f) + Vector3.back * 0.38f, Vector3.Lerp(a, b, 0.5f) + Vector3.back * 0.38f, 0.22f));
                        a = b;
                    }
                }
                var orb = new GameObject("Orb").transform;
                orb.SetParent(transform, false);
                orb.localPosition = new Vector3(0f, apex + 0.6f, -0.3f);
                RemixGeo.Beam(orb, "Core", sphere, matB, Vector3.zero, Vector3.zero, 0.7f);
                blooms.Add(orb);
                break;
            }
            case LegendSkin.Starship:
            {
                // A bulkhead hatch: an octagon frame with chevrons and status lights.
                float R = halfBase, cy = 3.2f;
                var pts = new List<Vector3>();
                for (int k = 0; k < 8; k++)
                {
                    float a = Mathf.PI / 8f + k * Mathf.PI / 4f;
                    pts.Add(new Vector3(Mathf.Cos(a) * R, cy + Mathf.Sin(a) * R * 0.75f, 0f));
                }
                for (int k = 0; k < 8; k++)
                {
                    if (k == 5) continue;    // the bottom edge is the road
                    RemixGeo.Beam(transform, "Frame", cube, darkMat, pts[k], pts[(k + 1) % 8], 0.7f);
                    chase.Add(RemixGeo.Beam(transform, "Light", cube, k % 2 == 0 ? matA : matB, pts[k] + Vector3.back * 0.38f, Vector3.Lerp(pts[k], pts[(k + 1) % 8], 0.5f) + Vector3.back * 0.38f, 0.12f));
                }
                RemixGeo.Beam(transform, "Foot", cube, darkMat, pts[5], new Vector3(pts[5].x, -0.5f, 0f), 0.7f);
                RemixGeo.Beam(transform, "Foot", cube, darkMat, pts[6], new Vector3(pts[6].x, -0.5f, 0f), 0.7f);
                for (int k = 0; k < 3; k++)
                    blooms.Add(RemixGeo.Beam(transform, "Status", sphere, k == 1 ? matB : matA, new Vector3(-0.8f + k * 0.8f, cy + R * 0.75f + 0.2f, -0.4f), new Vector3(-0.8f + k * 0.8f, cy + R * 0.75f + 0.2f, -0.4f), 0.3f).transform);
                break;
            }
            case LegendSkin.Rome:
            {
                // A triumphal arch: two piers, a round arch, an attic with a gold frieze, a quadriga on top.
                float x = halfBase - 0.4f, spring = 4.2f;
                foreach (int side in new[] { -1, 1 })
                    RemixGeo.Beam(transform, "Pier", cube, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, apex - 0.4f, 0f), 1.4f);
                const int seg = 14;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.PI * i / seg, u1 = Mathf.PI * (i + 1) / seg;
                    Vector3 p0 = new Vector3(Mathf.Cos(u0) * (x - 0.7f), spring + Mathf.Sin(u0) * 2.4f, -0.1f), p1 = new Vector3(Mathf.Cos(u1) * (x - 0.7f), spring + Mathf.Sin(u1) * 2.4f, -0.1f);
                    chase.Add(RemixGeo.Beam(transform, "Voussoir", cube, i % 2 == 0 ? matA : matB, p0 + Vector3.back * 0.6f, p1 + Vector3.back * 0.6f, 0.2f));
                }
                RemixGeo.Beam(transform, "Attic", cube, darkMat, new Vector3(-x - 0.7f, apex, 0f), new Vector3(x + 0.7f, apex, 0f), 1.2f);
                chase.Add(RemixGeo.Beam(transform, "Frieze", cube, matA, new Vector3(-x - 0.7f, apex - 0.45f, -0.62f), new Vector3(x + 0.7f, apex - 0.45f, -0.62f), 0.12f));
                var quad = new GameObject("Quadriga").transform;
                quad.SetParent(transform, false);
                quad.localPosition = new Vector3(0f, apex + 1.1f, 0f);
                RemixGeo.Beam(quad, "Car", cube, matA, new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), 0.7f);
                for (int k = 0; k < 4; k++) RemixGeo.Beam(quad, "Horse", cube, matB, new Vector3(-1.2f + k * 0.8f, 0.2f, -0.6f), new Vector3(-1.2f + k * 0.8f, 0.9f, -0.9f), 0.3f);
                blooms.Add(quad);
                break;
            }
            default:    // Viking
            {
                // Two dragon-headed posts curving in, a carved beam, shields hung on it.
                float x = halfBase - 0.3f, top = apex - 1.2f;
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 a = new Vector3(side * x, -0.5f, 0f);
                    for (int k = 1; k <= 6; k++)
                    {
                        float u = k / 6f;
                        Vector3 b = new Vector3(side * (x - Mathf.Pow(u, 3f) * 1.6f), -0.5f + u * (top + 1.6f), 0f);
                        RemixGeo.Beam(transform, "Post", cube, darkMat, a, b, 0.6f);
                        a = b;
                    }
                    var head = RemixGeo.Beam(transform, "DragonHead", sphere, matA, a + new Vector3(-side * 0.5f, 0.3f, 0f), a + new Vector3(-side * 0.5f, 0.3f, 0f), 0.7f);
                    blooms.Add(head.transform);
                }
                RemixGeo.Beam(transform, "Beam", cube, darkMat, new Vector3(-x, top, 0f), new Vector3(x, top, 0f), 0.6f);
                for (int i = 0; i < 6; i++)
                {
                    float px = Mathf.Lerp(-x + 1.2f, x - 1.2f, i / 5f);
                    var sh = RemixGeo.Beam(transform, "Shield", sphere, i % 2 == 0 ? matA : matB, new Vector3(px, top - 0.7f, -0.4f), new Vector3(px, top - 0.7f, -0.4f), 0.9f);
                    sh.transform.localScale = new Vector3(0.9f, 0.9f, 0.12f);   // a flat round shield
                    chase.Add(sh);
                }
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
        color = skin == LegendSkin.Jurassic ? new Color(1f, 0.45f, 0.1f)
              : skin == LegendSkin.Magic ? new Color(0.55f, 0.35f, 1f)
              : skin == LegendSkin.Starship ? new Color(0.1f, 0.85f, 1f)
              : skin == LegendSkin.Rome ? new Color(1f, 0.78f, 0.25f)
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
            blooms[i].localScale = Vector3.one * s * (1f);
        }
    }
}
