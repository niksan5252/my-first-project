using System.Collections.Generic;
using UnityEngine;

/// Video 13's rings, one per world (ElementSkin), all spanning the road with their feet 6.8 m out
/// and their top at ~8 m, so the lanes and every note's reach stay inside:
///   * Jungle: an arch of twisted vines with flowers that open on the beat;
///   * Glacier: a hexagonal ice frame with crystal spikes at its corners;
///   * Desert: a pharaoh's gate - two sloping pylons, a lintel and a winged sun disc;
///   * Matrix: a square portal of double code lines, a light chasing round it;
///   * Storm: a dark storm ring with an electric zigzag along it that flares on the kick.
/// Light from ClubBeat (the song clock), so it is on the music in Play and in a recording.
public class RingArchElements : MonoBehaviour, IThemeAccent
{
    [SerializeField] private ElementSkin skin;
    [SerializeField] private float halfBase = 6.8f;
    [SerializeField] private float apex = 8f;
    [SerializeField] private Color color = new Color(0.3f, 1f, 0.4f);
    [SerializeField, Range(0f, RemixGeo.MaxEmission)] private float emissionIntensity = 1.3f;

    private Material matA, matB, darkMat;
    private readonly List<MeshRenderer> chase = new List<MeshRenderer>();
    private readonly List<Transform> blooms = new List<Transform>();
    private MaterialPropertyBlock mpb;
    private bool ready;

    private Color SecondColor =>
        skin == ElementSkin.Jungle ? new Color(1f, 0.25f, 0.75f)
        : skin == ElementSkin.Glacier ? new Color(0.85f, 0.95f, 1f)
        : skin == ElementSkin.Desert ? new Color(0.1f, 0.95f, 0.85f)
        : skin == ElementSkin.Matrix ? new Color(0.75f, 1f, 0.8f)
        : new Color(0.9f, 0.92f, 1f);

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        ApplyAccent(color, emissionIntensity);   // every world has its own ring colour
        Mesh cube = RemixGeo.Primitive(PrimitiveType.Cube), sphere = RemixGeo.Primitive(PrimitiveType.Sphere);
        matA = RemixGeo.Neon(color, emissionIntensity);
        matB = RemixGeo.Neon(SecondColor, emissionIntensity);
        darkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        darkMat.SetColor("_BaseColor", skin == ElementSkin.Desert ? new Color(0.25f, 0.17f, 0.1f) : new Color(0.04f, 0.04f, 0.06f));
        darkMat.SetFloat("_Smoothness", skin == ElementSkin.Desert ? 0.3f : 0.85f);

        // A circle through (+-halfBase, -0.5) and (0, apex), as RingArchWorlds.
        float c = (apex * apex - halfBase * halfBase - 0.25f) / (2f * apex + 1f);
        float r = apex - c;
        float a0 = Mathf.Atan2(-0.5f - c, halfBase), a1 = Mathf.PI - a0;
        Vector3 At(float a, float rad) => new Vector3(Mathf.Cos(a) * rad, c + Mathf.Sin(a) * rad, 0f);

        switch (skin)
        {
            case ElementSkin.Jungle:
            {
                // Two vines twisting round the arc, flowers along it.
                const int seg = 28;
                for (int v = 0; v < 2; v++)
                    for (int i = 0; i < seg; i++)
                    {
                        float u0 = Mathf.Lerp(a0, a1, i / (float)seg), u1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                        float w0 = Mathf.Sin(i * 0.9f + v * Mathf.PI) * 0.3f, w1 = Mathf.Sin((i + 1) * 0.9f + v * Mathf.PI) * 0.3f;
                        Vector3 p0 = At(u0, r + w0) + Vector3.forward * w0, p1 = At(u1, r + w1) + Vector3.forward * w1;
                        chase.Add(RemixGeo.Beam(transform, "Vine", cube, matA, p0, p1, 0.28f));
                    }
                for (int i = 1; i < 9; i++)
                {
                    Vector3 p = At(Mathf.Lerp(a0, a1, i / 9f), r);
                    var f = new GameObject("Flower").transform;
                    f.SetParent(transform, false);
                    f.localPosition = p;
                    for (int k = 0; k < 5; k++)
                    {
                        float a = k * Mathf.PI * 2f / 5f;
                        RemixGeo.Beam(f, "Petal", sphere, matB, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.35f, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.35f, 0.38f);
                    }
                    RemixGeo.Beam(f, "Heart", sphere, matA, Vector3.zero, Vector3.zero, 0.25f);
                    blooms.Add(f);
                }
                break;
            }
            case ElementSkin.Glacier:
            {
                // A hexagon standing on its two lower corners at the road's edge.
                float R = 7.6f, cy = 3.6f;
                var pts = new List<Vector3>();
                for (int k = 0; k < 6; k++)
                {
                    float a = -Mathf.PI / 2f + Mathf.PI / 6f + k * Mathf.PI / 3f;      // flat bottom
                    pts.Add(new Vector3(Mathf.Cos(a) * R * 0.92f, cy + Mathf.Sin(a) * R * 0.6f, 0f));
                }
                for (int k = 1; k < 6; k++)   // skip the bottom edge (the road)
                {
                    Vector3 p0 = pts[k], p1 = pts[(k + 1) % 6];
                    if (k == 5) p1 = pts[0];
                    var e = RemixGeo.Beam(transform, "Ice", cube, matA, p0, p1, 0.42f);
                    chase.Add(e);
                }
                // Legs down to the road from the two bottom corners, and spikes out of every corner.
                RemixGeo.Beam(transform, "Leg", cube, matA, pts[0], new Vector3(pts[0].x, -0.5f, 0f), 0.42f);
                RemixGeo.Beam(transform, "Leg", cube, matA, pts[5], new Vector3(pts[5].x, -0.5f, 0f), 0.42f);
                for (int k = 1; k < 5; k++)
                {
                    Vector3 o = new Vector3(pts[k].x, pts[k].y - cy, 0f).normalized;
                    for (int s = -1; s <= 1; s++)
                    {
                        Vector3 dir = Quaternion.Euler(0f, 0f, s * 28f) * o;
                        var sp = RemixGeo.Beam(transform, "Spike", cube, matB, pts[k], pts[k] + dir * (s == 0 ? 1.6f : 1f), s == 0 ? 0.3f : 0.2f);
                        blooms.Add(sp.transform);
                    }
                }
                break;
            }
            case ElementSkin.Desert:
            {
                // Two pylons sloping in, a lintel across, gold trim, the winged sun in the middle.
                float top = apex - 1.2f;
                foreach (int side in new[] { -1, 1 })
                {
                    for (int k = 0; k < 6; k++)
                    {
                        float y = -0.5f + k * (top + 0.5f) / 6f, w = 2.2f - k * 0.18f;
                        var blk = RemixGeo.Beam(transform, "Pylon", cube, darkMat, new Vector3(side * (halfBase + 0.4f), y, 0f), new Vector3(side * (halfBase + 0.4f), y + (top + 0.5f) / 6f, 0f), w);
                        blk.transform.localScale = new Vector3(w, blk.transform.localScale.y, 1.2f);
                    }
                    chase.Add(RemixGeo.Beam(transform, "Trim", cube, matA, new Vector3(side * (halfBase - 0.75f), -0.5f, -0.65f), new Vector3(side * (halfBase - 0.4f), top, -0.65f), 0.14f));
                }
                RemixGeo.Beam(transform, "Lintel", cube, darkMat, new Vector3(-halfBase - 1.6f, top + 0.5f, 0f), new Vector3(halfBase + 1.6f, top + 0.5f, 0f), 1f);
                chase.Add(RemixGeo.Beam(transform, "LintelTrim", cube, matA, new Vector3(-halfBase - 1.4f, top - 0.05f, -0.55f), new Vector3(halfBase + 1.4f, top - 0.05f, -0.55f), 0.14f));
                var sun = new GameObject("WingedSun").transform;
                sun.SetParent(transform, false);
                sun.localPosition = new Vector3(0f, top + 0.55f, -0.6f);
                RemixGeo.Beam(sun, "Disc", sphere, matA, Vector3.zero, Vector3.zero, 0.85f);
                foreach (int side in new[] { -1, 1 })
                    for (int k = 0; k < 4; k++)
                        RemixGeo.Beam(sun, "Feather", cube, matB, new Vector3(side * 0.5f, 0.1f - k * 0.12f, 0f), new Vector3(side * (2.6f - k * 0.45f), 0.35f - k * 0.2f, 0f), 0.09f);
                blooms.Add(sun);
                break;
            }
            case ElementSkin.Matrix:
            {
                // A square portal: double lines round the posts and top, corner brackets.
                float x = halfBase - 0.3f, top = apex - 0.4f;
                foreach (float inset in new[] { 0f, 0.55f })
                {
                    Vector3 bl = new Vector3(-x + inset, -0.5f, 0f), tl = new Vector3(-x + inset, top - inset, 0f), tr = new Vector3(x - inset, top - inset, 0f), br = new Vector3(x - inset, -0.5f, 0f);
                    var m = inset == 0f ? matA : matB;
                    int segs = 6;
                    for (int k = 0; k < segs; k++) chase.Add(RemixGeo.Beam(transform, "Line", cube, m, Vector3.Lerp(bl, tl, k / (float)segs), Vector3.Lerp(bl, tl, (k + 1) / (float)segs), inset == 0f ? 0.22f : 0.1f));
                    for (int k = 0; k < segs * 2; k++) chase.Add(RemixGeo.Beam(transform, "Line", cube, m, Vector3.Lerp(tl, tr, k / (segs * 2f)), Vector3.Lerp(tl, tr, (k + 1) / (segs * 2f)), inset == 0f ? 0.22f : 0.1f));
                    for (int k = 0; k < segs; k++) chase.Add(RemixGeo.Beam(transform, "Line", cube, m, Vector3.Lerp(tr, br, k / (float)segs), Vector3.Lerp(tr, br, (k + 1) / (float)segs), inset == 0f ? 0.22f : 0.1f));
                }
                foreach (int sx in new[] { -1, 1 })
                {
                    Vector3 corner = new Vector3(sx * (x + 0.4f), top + 0.4f, 0f);
                    blooms.Add(RemixGeo.Beam(transform, "Bracket", cube, matB, corner, corner + new Vector3(-sx * 1.4f, 0f, 0f), 0.16f).transform);
                    blooms.Add(RemixGeo.Beam(transform, "Bracket", cube, matB, corner, corner + new Vector3(0f, -1.4f, 0f), 0.16f).transform);
                }
                break;
            }
            default:    // Storm
            {
                const int seg = 16;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.Lerp(a0, a1, i / (float)seg), u1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                    RemixGeo.Beam(transform, "Cloud", cube, darkMat, At(u0, r), At(u1, r), 0.8f);
                }
                // The electric line: zigzagging round the inside of the ring.
                const int zig = 30;
                Vector3 prev = At(a0, r - 0.55f);
                for (int i = 1; i <= zig; i++)
                {
                    float u = Mathf.Lerp(a0, a1, i / (float)zig);
                    Vector3 p = At(u, r - 0.55f + ((i & 1) == 0 ? 0.35f : -0.35f));
                    chase.Add(RemixGeo.Beam(transform, "Spark", cube, (i % 5) == 0 ? matB : matA, prev, p, 0.12f));
                    prev = p;
                }
                for (int i = 1; i < 6; i++)
                    blooms.Add(RemixGeo.Beam(transform, "Node", sphere, matB, At(Mathf.Lerp(a0, a1, i / 6f), r), At(Mathf.Lerp(a0, a1, i / 6f), r), 0.45f).transform);
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
        // Each world keeps its own ring colour: green vines, ice blue, gold, code green, electric blue.
        color = skin == ElementSkin.Jungle ? new Color(0.25f, 1f, 0.35f)
              : skin == ElementSkin.Glacier ? new Color(0.35f, 0.85f, 1f)
              : skin == ElementSkin.Desert ? new Color(1f, 0.72f, 0.18f)
              : skin == ElementSkin.Matrix ? new Color(0.15f, 1f, 0.35f)
              : new Color(0.45f, 0.6f, 1f);
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
        if (skin == ElementSkin.Glacier) baseLevel *= 0.55f;   // ice and white glare quickly

        float aLevel = skin == ElementSkin.Storm ? emissionIntensity * (0.35f + 1.4f * kick * energy) : baseLevel;
        RemixGeo.SetEmission(matA, color, Mathf.Min(RemixGeo.MaxEmission, aLevel));
        RemixGeo.SetEmission(matB, bCol, Mathf.Min(RemixGeo.MaxEmission, baseLevel * 0.9f));

        if (chase.Count > 0)
        {
            int bar = Mathf.FloorToInt(beats / 4f);
            float head = Mathf.Repeat(beats, 4f) / 4f * chase.Count;
            if ((bar & 1) == 1 && skin != ElementSkin.Matrix) head = chase.Count - head;
            for (int i = 0; i < chase.Count; i++)
            {
                float d = Mathf.Abs(head - i);
                if (skin == ElementSkin.Matrix) d = Mathf.Min(d, chase.Count - d);
                float trail = d < 3f ? 1f - d / 3f : 0f;
                float baseL = skin == ElementSkin.Storm ? 0.3f + 1.3f * kick * energy : skin == ElementSkin.Glacier ? 0.25f : 0.45f;
                Color lc = Color.Lerp(chase[i].sharedMaterial == matA ? color : bCol, Color.white, 0.3f * trail);
                mpb.SetColor(RemixGeo.EmissionId, lc * Mathf.Min(RemixGeo.MaxEmission, emissionIntensity * (baseL + (skin == ElementSkin.Glacier ? 0.8f : 1.3f) * trail * Mathf.Max(0.6f, energy))));
                chase[i].SetPropertyBlock(mpb);
            }
        }
        // Flowers open, spikes and brackets and nodes swell on the kick.
        for (int i = 0; i < blooms.Count; i++)
        {
            float s = 1f + (skin == ElementSkin.Jungle ? 0.35f : 0.18f) * kick * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(i * 1.3f + beats * 0.5f)));
            if (skin == ElementSkin.Glacier || skin == ElementSkin.Matrix)
            {
                var ls = blooms[i].localScale;
                // Beams are scaled along their length (y); thicken only.
                blooms[i].localScale = new Vector3(BaseThick(i) * s, ls.y, BaseThick(i) * s);
            }
            else blooms[i].localScale = Vector3.one * s * (skin == ElementSkin.Storm ? 0.45f : 1f);
        }
    }

    private readonly Dictionary<int, float> thick = new Dictionary<int, float>();
    private float BaseThick(int i)
    {
        if (!thick.TryGetValue(i, out float v)) { v = blooms[i].localScale.x; thick[i] = v; }
        return v;
    }
}
