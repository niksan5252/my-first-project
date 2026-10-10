using System.Collections.Generic;
using UnityEngine;

/// Video 14's rings, one per world (WonderSkin), all spanning the road with their feet 6.8 m out
/// and their top at ~8 m, so the lanes and every note's reach stay inside:
///   * Steampunk: the rim of a giant gear - a copper band, its teeth running round it on the beat;
///   * Festival: a red paifang gate - posts, two lintels with upturned golden ends, two lanterns;
///   * Crystal: an arch of crystal shards, amethyst and emerald, flaring in turn;
///   * Funfair: a marquee arch of bulbs, a chase running round it;
///   * Olympus: a Greek gate - two marble columns, a golden entablature and a pediment.
/// Light from ClubBeat (the song clock), so it is on the music in Play and in a recording.
public class RingArchWonders : MonoBehaviour, IThemeAccent
{
    [SerializeField] private WonderSkin skin;
    [SerializeField] private float halfBase = 6.8f;
    [SerializeField] private float apex = 8f;
    [SerializeField] private Color color = new Color(1f, 0.6f, 0.2f);
    [SerializeField, Range(0f, RemixGeo.MaxEmission)] private float emissionIntensity = 1.3f;

    private Material matA, matB, darkMat;
    private readonly List<MeshRenderer> chase = new List<MeshRenderer>();
    private readonly List<Transform> blooms = new List<Transform>();
    private readonly List<Transform> teeth = new List<Transform>();
    private MaterialPropertyBlock mpb;
    private bool ready;
    private float c0, r0, a0, a1;

    private Color SecondColor =>
        skin == WonderSkin.Steampunk ? new Color(1f, 0.82f, 0.4f)
        : skin == WonderSkin.Festival ? new Color(1f, 0.8f, 0.2f)
        : skin == WonderSkin.Crystal ? new Color(0.25f, 1f, 0.6f)
        : skin == WonderSkin.Funfair ? new Color(1f, 0.85f, 0.3f)
        : new Color(0.5f, 0.8f, 1f);

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        ApplyAccent(color, emissionIntensity);
        Mesh cube = RemixGeo.Primitive(PrimitiveType.Cube), sphere = RemixGeo.Primitive(PrimitiveType.Sphere), cyl = RemixGeo.Primitive(PrimitiveType.Cylinder);
        matA = RemixGeo.Neon(color, emissionIntensity);
        matB = RemixGeo.Neon(SecondColor, emissionIntensity);
        darkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        Color dark = skin == WonderSkin.Steampunk ? new Color(0.45f, 0.22f, 0.1f)
                   : skin == WonderSkin.Festival ? new Color(0.5f, 0.04f, 0.04f)
                   : skin == WonderSkin.Olympus ? new Color(0.85f, 0.85f, 0.88f)
                   : new Color(0.05f, 0.04f, 0.07f);
        darkMat.SetColor("_BaseColor", dark);
        darkMat.SetFloat("_Smoothness", 0.7f);
        darkMat.SetFloat("_Metallic", skin == WonderSkin.Steampunk ? 0.9f : 0f);

        // A circle through (+-halfBase, -0.5) and (0, apex), as RingArchWorlds.
        c0 = (apex * apex - halfBase * halfBase - 0.25f) / (2f * apex + 1f);
        r0 = apex - c0;
        a0 = Mathf.Atan2(-0.5f - c0, halfBase); a1 = Mathf.PI - a0;
        float c = c0, r = r0;
        Vector3 At(float a, float rad) => new Vector3(Mathf.Cos(a) * rad, c + Mathf.Sin(a) * rad, 0f);

        switch (skin)
        {
            case WonderSkin.Steampunk:
            {
                // The band, rivets on it, and teeth that run round (placed every frame).
                const int seg = 24;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.Lerp(a0, a1, i / (float)seg), u1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                    RemixGeo.Beam(transform, "Band", cube, darkMat, At(u0, r), At(u1, r), 0.7f);
                    chase.Add(RemixGeo.Beam(transform, "Rivet", sphere, matB, At(u0, r - 0.38f), At(u0, r - 0.38f), 0.16f));
                }
                for (int i = 0; i < 18; i++)
                {
                    var tooth = RemixGeo.Beam(transform, "Tooth", cube, matA, Vector3.zero, Vector3.up * 0.6f, 0.45f).transform;
                    teeth.Add(tooth);
                }
                break;
            }
            case WonderSkin.Festival:
            {
                float x = halfBase - 0.5f, top = apex - 1f;
                RemixGeo.Beam(transform, "Post", cube, darkMat, new Vector3(-x, -0.5f, 0f), new Vector3(-x, top, 0f), 0.6f);
                RemixGeo.Beam(transform, "Post", cube, darkMat, new Vector3(x, -0.5f, 0f), new Vector3(x, top, 0f), 0.6f);
                chase.Add(RemixGeo.Beam(transform, "PostGlow", cube, matA, new Vector3(-x + 0.32f, -0.5f, -0.2f), new Vector3(-x + 0.32f, top - 0.2f, -0.2f), 0.1f));
                chase.Add(RemixGeo.Beam(transform, "PostGlow", cube, matA, new Vector3(x - 0.32f, -0.5f, -0.2f), new Vector3(x - 0.32f, top - 0.2f, -0.2f), 0.1f));
                RemixGeo.Beam(transform, "Lintel", cube, darkMat, new Vector3(-x - 0.6f, top - 1.4f, 0f), new Vector3(x + 0.6f, top - 1.4f, 0f), 0.45f);
                int seg = 12;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = -1f + 2f * i / seg, u1 = -1f + 2f * (i + 1) / seg;
                    Vector3 p0 = new Vector3(u0 * (x + 1.8f), top + 0.3f + 0.7f * u0 * u0 * u0 * u0, 0f), p1 = new Vector3(u1 * (x + 1.8f), top + 0.3f + 0.7f * u1 * u1 * u1 * u1, 0f);
                    RemixGeo.Beam(transform, "Roof", cube, darkMat, p0, p1, 0.55f);
                    chase.Add(RemixGeo.Beam(transform, "Gold", cube, matB, p0 + Vector3.up * 0.33f, p1 + Vector3.up * 0.33f, 0.12f));
                }
                RemixGeo.Beam(transform, "Plaque", cube, matB, new Vector3(-0.9f, top - 0.55f, -0.1f), new Vector3(0.9f, top - 0.55f, -0.1f), 0.8f);
                foreach (int side in new[] { -1, 1 })
                {
                    var l = new GameObject("Lantern").transform;
                    l.SetParent(transform, false);
                    l.localPosition = new Vector3(side * (x - 1.6f), top - 2.4f, -0.2f);
                    RemixGeo.Beam(l, "Body", sphere, matA, Vector3.zero, Vector3.zero, 0.8f);
                    RemixGeo.Beam(l, "Cord", cube, matB, Vector3.zero, Vector3.up * 1f, 0.05f);
                    blooms.Add(l);
                }
                break;
            }
            case WonderSkin.Crystal:
            {
                const int seg = 16;
                for (int i = 0; i <= seg; i++)
                {
                    float u = Mathf.Lerp(a0, a1, i / (float)seg);
                    Vector3 p = At(u, r);
                    Vector3 outDir = (p - new Vector3(0f, c, 0f)).normalized;
                    float len = (i % 2 == 0) ? 0.9f : 0.55f;
                    var m = i % 3 == 0 ? matB : matA;
                    var shard = RemixGeo.Beam(transform, "Shard", cube, m, p - outDir * 0.2f, p + outDir * len, 0.32f);
                    shard.transform.localRotation *= Quaternion.Euler(0f, 45f, 0f);
                    chase.Add(shard);
                    blooms.Add(shard.transform);
                }
                break;
            }
            case WonderSkin.Funfair:
            {
                float x = halfBase - 0.4f, top = apex - 1.2f;
                // The frame: two posts and an arched top, bulbs all round it.
                RemixGeo.Beam(transform, "Post", cube, darkMat, new Vector3(-x, -0.5f, 0f), new Vector3(-x, top, 0f), 0.5f);
                RemixGeo.Beam(transform, "Post", cube, darkMat, new Vector3(x, -0.5f, 0f), new Vector3(x, top, 0f), 0.5f);
                int seg = 16;
                Vector3 prev = new Vector3(-x, top, 0f);
                for (int i = 1; i <= seg; i++)
                {
                    float u = i / (float)seg;
                    Vector3 p = new Vector3(Mathf.Lerp(-x, x, u), top + Mathf.Sin(u * Mathf.PI) * 1.4f, 0f);
                    RemixGeo.Beam(transform, "Top", cube, darkMat, prev, p, 0.5f);
                    prev = p;
                }
                for (int i = 0; i < 9; i++) chase.Add(RemixGeo.Beam(transform, "Bulb", sphere, i % 2 == 0 ? matA : matB, new Vector3(-x, -0.2f + i * (top + 0.2f) / 9f, -0.3f), new Vector3(-x, -0.2f + i * (top + 0.2f) / 9f, -0.3f), 0.28f));
                for (int i = 0; i <= 20; i++)
                {
                    float u = i / 20f;
                    Vector3 p = new Vector3(Mathf.Lerp(-x, x, u), top + Mathf.Sin(u * Mathf.PI) * 1.4f, -0.3f);
                    chase.Add(RemixGeo.Beam(transform, "Bulb", sphere, i % 2 == 0 ? matB : matA, p, p, 0.28f));
                }
                for (int i = 8; i >= 0; i--) chase.Add(RemixGeo.Beam(transform, "Bulb", sphere, i % 2 == 0 ? matA : matB, new Vector3(x, -0.2f + i * (top + 0.2f) / 9f, -0.3f), new Vector3(x, -0.2f + i * (top + 0.2f) / 9f, -0.3f), 0.28f));
                // A star on top.
                var star = new GameObject("Star").transform;
                star.SetParent(transform, false);
                star.localPosition = new Vector3(0f, top + 2.1f, -0.3f);
                for (int k = 0; k < 5; k++)
                {
                    float a = Mathf.PI / 2f + k * Mathf.PI * 2f / 5f;
                    RemixGeo.Beam(star, "Ray", cube, matB, Vector3.zero, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.7f, 0.22f);
                }
                blooms.Add(star);
                break;
            }
            default:    // Olympus
            {
                float x = halfBase - 0.7f, top = apex - 1.6f;
                foreach (int side in new[] { -1, 1 })
                {
                    RemixGeo.Beam(transform, "Base", cube, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, -0.1f, 0f), 1.3f);
                    RemixGeo.Beam(transform, "Column", cyl, darkMat, new Vector3(side * x, -0.1f, 0f), new Vector3(side * x, top - 0.4f, 0f), 0.85f);
                    chase.Add(RemixGeo.Beam(transform, "Capital", cube, matA, new Vector3(side * x, top - 0.4f, 0f), new Vector3(side * x, top - 0.1f, 0f), 1.3f));
                }
                RemixGeo.Beam(transform, "Entablature", cube, darkMat, new Vector3(-x - 1f, top + 0.3f, 0f), new Vector3(x + 1f, top + 0.3f, 0f), 0.8f);
                chase.Add(RemixGeo.Beam(transform, "Frieze", cube, matA, new Vector3(-x - 1f, top - 0.05f, -0.42f), new Vector3(x + 1f, top - 0.05f, -0.42f), 0.12f));
                RemixGeo.Beam(transform, "Pediment", cube, darkMat, new Vector3(-x - 1f, top + 0.8f, 0f), new Vector3(0f, top + 2.6f, 0f), 0.45f);
                RemixGeo.Beam(transform, "Pediment", cube, darkMat, new Vector3(x + 1f, top + 0.8f, 0f), new Vector3(0f, top + 2.6f, 0f), 0.45f);
                chase.Add(RemixGeo.Beam(transform, "Gilt", cube, matA, new Vector3(-x - 1f, top + 1.05f, -0.25f), new Vector3(0f, top + 2.85f, -0.25f), 0.1f));
                chase.Add(RemixGeo.Beam(transform, "Gilt", cube, matA, new Vector3(x + 1f, top + 1.05f, -0.25f), new Vector3(0f, top + 2.85f, -0.25f), 0.1f));
                var disc = new GameObject("Laurel").transform;
                disc.SetParent(transform, false);
                disc.localPosition = new Vector3(0f, top + 1.45f, -0.3f);
                RemixGeo.Beam(disc, "Sun", sphere, matB, Vector3.zero, Vector3.zero, 0.55f);
                blooms.Add(disc);
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
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        // Each world keeps its own ring colour; the funfair takes the tunnel's alternating accents.
        color = skin == WonderSkin.Steampunk ? new Color(1f, 0.55f, 0.15f)
              : skin == WonderSkin.Festival ? new Color(1f, 0.15f, 0.12f)
              : skin == WonderSkin.Crystal ? new Color(0.75f, 0.3f, 1f)
              : skin == WonderSkin.Olympus ? new Color(1f, 0.8f, 0.3f)
              : newColor;
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
        if (skin == WonderSkin.Olympus) baseLevel *= 0.8f;
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
        // The gear's teeth run round the rim: a step forward on every beat, eased.
        if (teeth.Count > 0)
        {
            float step = (a1 - a0) / teeth.Count;
            float eased = Mathf.Floor(beats) + Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ClubBeat.BeatPhase * 3f));
            float phase = Mathf.Repeat(eased * 0.5f, 1f) * step;
            for (int i = 0; i < teeth.Count; i++)
            {
                float a = a0 + phase + i * step;
                if (a > a1) a -= (a1 - a0);
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                teeth[i].localPosition = new Vector3(0f, c0, 0f) + dir * (r0 + 0.55f);
                teeth[i].localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            }
        }
        for (int i = 0; i < blooms.Count; i++)
        {
            float s = 1f + 0.25f * kick * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(i * 1.3f + beats * 0.5f)));
            if (skin == WonderSkin.Crystal)
            {
                var ls = blooms[i].localScale;
                blooms[i].localScale = new Vector3(0.32f * s, ls.y, 0.32f * s);
            }
            else blooms[i].localScale = Vector3.one * s;
        }
    }
}
