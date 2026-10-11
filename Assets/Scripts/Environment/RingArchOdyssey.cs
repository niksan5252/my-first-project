using System.Collections.Generic;
using UnityEngine;

/// Video 15's rings, one per world (OdysseySkin), all spanning the road with their feet 6.8 m out
/// and their top at ~8 m, so the lanes and every note's reach stay inside:
///   * Racing: a finish gantry - posts, a chequered beam, a row of start lights running;
///   * Alien: curved ribs like tentacles round the road, glowing nodes pulsing;
///   * WildWest: a ranch gate - wooden posts, a crossbeam, a neon horseshoe, neon stars;
///   * Pirate: two masts with crossed ropes, an anchor on top, two lanterns;
///   * Aztec: a stepped stone gate with jade trim and a golden sun disc.
/// Light from ClubBeat (the song clock), so it is on the music in Play and in a recording.
public class RingArchOdyssey : MonoBehaviour, IThemeAccent
{
    [SerializeField] private OdysseySkin skin;
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
        skin == OdysseySkin.Racing ? new Color(1f, 0.9f, 0.1f)
        : skin == OdysseySkin.Alien ? new Color(0.7f, 0.3f, 1f)
        : skin == OdysseySkin.WildWest ? new Color(1f, 0.3f, 0.6f)
        : skin == OdysseySkin.Pirate ? new Color(1f, 0.55f, 0.15f)
        : new Color(1f, 0.78f, 0.2f);

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        ApplyAccent(color, emissionIntensity);
        Mesh cube = RemixGeo.Primitive(PrimitiveType.Cube), sphere = RemixGeo.Primitive(PrimitiveType.Sphere), cyl = RemixGeo.Primitive(PrimitiveType.Cylinder);
        matA = RemixGeo.Neon(color, emissionIntensity);
        matB = RemixGeo.Neon(SecondColor, emissionIntensity);
        darkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        Color dark = skin == OdysseySkin.Racing ? new Color(0.06f, 0.06f, 0.07f)
                   : skin == OdysseySkin.WildWest || skin == OdysseySkin.Pirate ? new Color(0.24f, 0.13f, 0.06f)
                   : skin == OdysseySkin.Aztec ? new Color(0.32f, 0.29f, 0.24f)
                   : new Color(0.12f, 0.07f, 0.18f);
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
            case OdysseySkin.Racing:
            {
                float x = halfBase - 0.4f, top = apex - 1f;
                RemixGeo.Beam(transform, "Post", cube, darkMat, new Vector3(-x, -0.5f, 0f), new Vector3(-x, top, 0f), 0.6f);
                RemixGeo.Beam(transform, "Post", cube, darkMat, new Vector3(x, -0.5f, 0f), new Vector3(x, top, 0f), 0.6f);
                // The chequered beam: two rows of squares.
                int cols = 16;
                for (int i = 0; i < cols; i++)
                    for (int row = 0; row < 2; row++)
                    {
                        float px = Mathf.Lerp(-x, x, (i + 0.5f) / cols);
                        var sq = (i + row) % 2 == 0 ? lightMat : darkMat;
                        RemixGeo.Beam(transform, "Check", cube, sq, new Vector3(px, top - 0.3f + row * 0.6f, 0f), new Vector3(px, top - 0.3f + row * 0.6f, 0f), 2f * x / cols);
                    }
                // Start lights along the bottom of the beam, running.
                for (int i = 0; i < 10; i++)
                    chase.Add(RemixGeo.Beam(transform, "Light", sphere, i % 2 == 0 ? matA : matB, new Vector3(Mathf.Lerp(-x + 0.8f, x - 0.8f, i / 9f), top - 0.9f, -0.35f), new Vector3(Mathf.Lerp(-x + 0.8f, x - 0.8f, i / 9f), top - 0.9f, -0.35f), 0.38f));
                chase.Add(RemixGeo.Beam(transform, "PostStrip", cube, matA, new Vector3(-x + 0.32f, -0.5f, -0.2f), new Vector3(-x + 0.32f, top - 0.6f, -0.2f), 0.1f));
                chase.Add(RemixGeo.Beam(transform, "PostStrip", cube, matA, new Vector3(x - 0.32f, -0.5f, -0.2f), new Vector3(x - 0.32f, top - 0.6f, -0.2f), 0.1f));
                break;
            }
            case OdysseySkin.Alien:
            {
                // Three ribs, wavy, the middle one forward; nodes on the middle one.
                const int seg = 22;
                for (int rib = -1; rib <= 1; rib++)
                    for (int i = 0; i < seg; i++)
                    {
                        float u0 = Mathf.Lerp(a0, a1, i / (float)seg), u1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                        float w0 = Mathf.Sin(i * 0.8f + rib) * 0.35f, w1 = Mathf.Sin((i + 1) * 0.8f + rib) * 0.35f;
                        Vector3 p0 = At(u0, r + w0 + rib * 0.2f) + Vector3.forward * rib * 0.6f, p1 = At(u1, r + w1 + rib * 0.2f) + Vector3.forward * rib * 0.6f;
                        chase.Add(RemixGeo.Beam(transform, "Rib", cube, rib == 0 ? matA : matB, p0, p1, rib == 0 ? 0.3f : 0.18f));
                    }
                for (int i = 1; i < 8; i++)
                    blooms.Add(RemixGeo.Beam(transform, "Node", sphere, matB, At(Mathf.Lerp(a0, a1, i / 8f), r), At(Mathf.Lerp(a0, a1, i / 8f), r), 0.5f).transform);
                break;
            }
            case OdysseySkin.WildWest:
            {
                float x = halfBase - 0.4f, top = apex - 1.4f;
                RemixGeo.Beam(transform, "Post", cube, darkMat, new Vector3(-x, -0.5f, 0f), new Vector3(-x, top + 0.6f, 0f), 0.7f);
                RemixGeo.Beam(transform, "Post", cube, darkMat, new Vector3(x, -0.5f, 0f), new Vector3(x, top + 0.6f, 0f), 0.7f);
                RemixGeo.Beam(transform, "Beam", cube, darkMat, new Vector3(-x - 0.5f, top, 0f), new Vector3(x + 0.5f, top, 0f), 0.55f);
                RemixGeo.Beam(transform, "Brace", cube, darkMat, new Vector3(-x, top - 1.6f, 0f), new Vector3(-x + 1.6f, top, 0f), 0.25f);
                RemixGeo.Beam(transform, "Brace", cube, darkMat, new Vector3(x, top - 1.6f, 0f), new Vector3(x - 1.6f, top, 0f), 0.25f);
                chase.Add(RemixGeo.Beam(transform, "Neon", cube, matA, new Vector3(-x - 0.4f, top - 0.33f, -0.3f), new Vector3(x + 0.4f, top - 0.33f, -0.3f), 0.08f));
                // The horseshoe above the beam, open side up.
                const int seg = 12;
                Vector3 hc = new Vector3(0f, top + 1.1f, -0.1f);
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.Lerp(-0.15f * Mathf.PI, 1.15f * Mathf.PI, i / (float)seg) + Mathf.PI, u1 = Mathf.Lerp(-0.15f * Mathf.PI, 1.15f * Mathf.PI, (i + 1) / (float)seg) + Mathf.PI;
                    chase.Add(RemixGeo.Beam(transform, "Shoe", cube, matB, hc + new Vector3(Mathf.Cos(u0), Mathf.Sin(u0), 0f) * 0.7f, hc + new Vector3(Mathf.Cos(u1), Mathf.Sin(u1), 0f) * 0.7f, 0.2f));
                }
                foreach (int side in new[] { -1, 1 })
                {
                    var star = new GameObject("Star").transform;
                    star.SetParent(transform, false);
                    star.localPosition = new Vector3(side * x, top + 1.2f, -0.2f);
                    for (int k = 0; k < 5; k++)
                    {
                        float a = Mathf.PI / 2f + k * Mathf.PI * 2f / 5f;
                        RemixGeo.Beam(star, "Ray", cube, matA, Vector3.zero, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.55f, 0.18f);
                    }
                    blooms.Add(star);
                }
                break;
            }
            case OdysseySkin.Pirate:
            {
                float x = halfBase - 0.3f, top = apex;
                RemixGeo.Beam(transform, "Mast", cyl, darkMat, new Vector3(-x, -0.5f, 0f), new Vector3(-x, top, 0f), 0.45f);
                RemixGeo.Beam(transform, "Mast", cyl, darkMat, new Vector3(x, -0.5f, 0f), new Vector3(x, top, 0f), 0.45f);
                RemixGeo.Beam(transform, "Yard", cyl, darkMat, new Vector3(-x - 0.6f, top - 0.6f, 0f), new Vector3(x + 0.6f, top - 0.6f, 0f), 0.3f);
                // Ropes: a sagging line of glowing knots between the mast tops, and two diagonals.
                const int seg = 14;
                for (int i = 0; i <= seg; i++)
                {
                    float u = i / (float)seg;
                    Vector3 p = new Vector3(Mathf.Lerp(-x, x, u), top - 1.2f - Mathf.Sin(u * Mathf.PI) * 0.9f, -0.2f);
                    chase.Add(RemixGeo.Beam(transform, "Knot", sphere, matB, p, p, 0.22f));
                }
                RemixGeo.Beam(transform, "Rope", cube, darkMat, new Vector3(-x, top - 0.6f, -0.1f), new Vector3(-x + 2.2f, -0.5f, -0.1f), 0.08f);
                RemixGeo.Beam(transform, "Rope", cube, darkMat, new Vector3(x, top - 0.6f, -0.1f), new Vector3(x - 2.2f, -0.5f, -0.1f), 0.08f);
                // The anchor on top.
                var anchor = new GameObject("Anchor").transform;
                anchor.SetParent(transform, false);
                anchor.localPosition = new Vector3(0f, top + 0.4f, -0.2f);
                RemixGeo.Beam(anchor, "Shank", cube, matA, new Vector3(0f, 1.1f, 0f), new Vector3(0f, -0.6f, 0f), 0.18f);
                RemixGeo.Beam(anchor, "Stock", cube, matA, new Vector3(-0.6f, 0.8f, 0f), new Vector3(0.6f, 0.8f, 0f), 0.15f);
                for (int k = 0; k < 6; k++)
                {
                    float u0 = Mathf.PI + k * Mathf.PI / 6f, u1 = Mathf.PI + (k + 1) * Mathf.PI / 6f;
                    RemixGeo.Beam(anchor, "Fluke", cube, matA, new Vector3(Mathf.Cos(u0), Mathf.Sin(u0) * 0.6f - 0.3f, 0f) * 0.8f, new Vector3(Mathf.Cos(u1), Mathf.Sin(u1) * 0.6f - 0.3f, 0f) * 0.8f, 0.15f);
                }
                RemixGeo.Beam(anchor, "Ring", sphere, matA, new Vector3(0f, 1.25f, 0f), new Vector3(0f, 1.25f, 0f), 0.3f);
                blooms.Add(anchor);
                foreach (int side in new[] { -1, 1 })
                {
                    var l = new GameObject("Lantern").transform;
                    l.SetParent(transform, false);
                    l.localPosition = new Vector3(side * (x - 0.6f), top - 2.4f, -0.3f);
                    RemixGeo.Beam(l, "Glass", cube, matB, Vector3.zero, Vector3.zero, 0.45f);
                    blooms.Add(l);
                }
                break;
            }
            default:    // Aztec
            {
                float x = halfBase - 0.5f, top = apex - 1.6f;
                foreach (int side in new[] { -1, 1 })
                {
                    for (int k = 0; k < 5; k++)
                    {
                        float y0 = -0.5f + k * (top + 0.5f) / 5f, y1 = y0 + (top + 0.5f) / 5f;
                        RemixGeo.Beam(transform, "Block", cube, darkMat, new Vector3(side * x, y0, 0f), new Vector3(side * x, y1 - 0.05f, 0f), 1.1f - k * 0.06f);
                        chase.Add(RemixGeo.Beam(transform, "Jade", cube, matA, new Vector3(side * (x - 0.56f), y1 - 0.1f, -0.3f), new Vector3(side * (x - 0.56f), y1 - 0.1f, -0.3f), 0.18f));
                    }
                }
                // A stepped lintel: three blocks rising to the middle.
                for (int k = 0; k < 3; k++)
                {
                    float w = x + 1f - k * 1.6f, y = top + 0.35f + k * 0.6f;
                    RemixGeo.Beam(transform, "Lintel", cube, darkMat, new Vector3(-w, y, 0f), new Vector3(w, y, 0f), 0.6f);
                    chase.Add(RemixGeo.Beam(transform, "Trim", cube, matA, new Vector3(-w, y - 0.33f, -0.32f), new Vector3(w, y - 0.33f, -0.32f), 0.08f));
                }
                var sun = new GameObject("Sun").transform;
                sun.SetParent(transform, false);
                sun.localPosition = new Vector3(0f, top + 2.6f, -0.35f);
                RemixGeo.Beam(sun, "Disc", sphere, matB, Vector3.zero, Vector3.zero, 1.1f);
                for (int k = 0; k < 12; k++)
                {
                    float a = k * Mathf.PI / 6f;
                    RemixGeo.Beam(sun, "Ray", cube, matB, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.65f, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 1.15f, 0.15f);
                }
                blooms.Add(sun);
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
        color = skin == OdysseySkin.Racing ? new Color(1f, 0.12f, 0.15f)
              : skin == OdysseySkin.Alien ? new Color(0.45f, 1f, 0.2f)
              : skin == OdysseySkin.WildWest ? new Color(0.3f, 1f, 0.85f)
              : skin == OdysseySkin.Pirate ? new Color(1f, 0.8f, 0.25f)
              : new Color(0.1f, 1f, 0.6f);
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
            blooms[i].localScale = Vector3.one * s * (skin == OdysseySkin.Alien ? 0.5f : 1f);
        }
    }
}
