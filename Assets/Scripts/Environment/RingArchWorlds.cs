using System.Collections.Generic;
using UnityEngine;

/// Video 12's rings, one per world (WorldSkin), all spanning the road with their feet 6.8 m out and
/// their top at ~8 m, so the lanes and every note's reach stay inside:
///   * Arcade: a stepped pixel arch of blocks, a chasing light across it;
///   * Candy: a candy-cane arc, red and white stripes swapping on the beat;
///   * NeoTokyo: a torii gate (two posts, kasagi beam with upturned ends, nuki tie);
///   * Ocean: a ring of bubbles round the road, swelling on the kick;
///   * Volcano: a jagged obsidian arch with lava cracks flaring on the kick.
/// Light from ClubBeat (the song clock), so it is on the music in Play and in a recording.
public class RingArchWorlds : MonoBehaviour, IThemeAccent
{
    [SerializeField] private WorldSkin skin;
    [SerializeField] private float halfBase = 6.8f;
    [SerializeField] private float apex = 8f;
    [SerializeField] private Color color = new Color(1f, 0.3f, 0.7f);
    [SerializeField, Range(0f, RemixGeo.MaxEmission)] private float emissionIntensity = 1.3f;

    private Material matA, matB, darkMat;
    private readonly List<MeshRenderer> partsA = new List<MeshRenderer>(), partsB = new List<MeshRenderer>(), chase = new List<MeshRenderer>();
    private readonly List<Transform> bubbles = new List<Transform>();
    private MaterialPropertyBlock mpb;
    private bool ready;

    private static readonly Color White = new Color(1f, 0.95f, 0.95f);

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        Mesh cube = RemixGeo.Primitive(PrimitiveType.Cube), sphere = RemixGeo.Primitive(PrimitiveType.Sphere);
        Color b = skin == WorldSkin.Candy ? White : skin == WorldSkin.Volcano ? new Color(1f, 0.6f, 0.15f) : Color.Lerp(color, Color.white, 0.5f);
        matA = RemixGeo.Neon(color, emissionIntensity);
        matB = RemixGeo.Neon(b, emissionIntensity);
        darkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        darkMat.SetColor("_BaseColor", new Color(0.04f, 0.03f, 0.05f));
        darkMat.SetFloat("_Smoothness", 0.85f);

        // A circle through (+-halfBase, -0.5) and (0, apex), as RingArchCosmic.
        float c = (apex * apex - halfBase * halfBase - 0.25f) / (2f * apex + 1f);
        float r = apex - c;
        float a0 = Mathf.Atan2(-0.5f - c, halfBase), a1 = Mathf.PI - a0;
        Vector3 At(float a, float rad) => new Vector3(Mathf.Cos(a) * rad, c + Mathf.Sin(a) * rad, 0f);

        switch (skin)
        {
            case WorldSkin.Arcade:
            {
                // Stepped arch: blocks along the circle snapped to a 0.7 m grid.
                const float px = 0.7f;
                var seen = new HashSet<Vector2Int>();
                for (int i = 0; i <= 80; i++)
                {
                    Vector3 p = At(Mathf.Lerp(a0, a1, i / 80f), r);
                    var k = new Vector2Int(Mathf.RoundToInt(p.x / px), Mathf.RoundToInt(p.y / px));
                    if (!seen.Add(k)) continue;
                    var mr = RemixGeo.Beam(transform, "Px", cube, (seen.Count & 1) == 0 ? matA : matB, new Vector3(k.x * px, k.y * px, 0f), new Vector3(k.x * px, k.y * px, 0f), px * 0.9f);
                    chase.Add(mr);
                }
                chase.Sort((x, y) => x.transform.localPosition.x.CompareTo(y.transform.localPosition.x));
                break;
            }
            case WorldSkin.Candy:
            {
                const int seg = 22;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.Lerp(a0, a1, i / (float)seg), u1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                    var mr = RemixGeo.Beam(transform, "Cane", cube, (i & 1) == 0 ? matA : matB, At(u0, r), At(u1, r), 0.5f);
                    ((i & 1) == 0 ? partsA : partsB).Add(mr);
                }
                break;
            }
            case WorldSkin.NeoTokyo:
            {
                float x = halfBase - 0.6f, top = apex - 0.6f;
                partsA.Add(RemixGeo.Beam(transform, "Post", cube, matA, new Vector3(-x, -0.5f, 0f), new Vector3(-x, top, 0f), 0.55f));
                partsA.Add(RemixGeo.Beam(transform, "Post", cube, matA, new Vector3(x, -0.5f, 0f), new Vector3(x, top, 0f), 0.55f));
                partsA.Add(RemixGeo.Beam(transform, "Nuki", cube, matA, new Vector3(-x - 0.8f, top - 1.5f, 0f), new Vector3(x + 0.8f, top - 1.5f, 0f), 0.35f));
                // Kasagi: the top beam, in segments rising at both ends.
                int seg = 10;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = -1f + 2f * i / seg, u1 = -1f + 2f * (i + 1) / seg;
                    Vector3 p0 = new Vector3(u0 * (x + 1.6f), top + 0.35f + 0.6f * u0 * u0, 0f), p1 = new Vector3(u1 * (x + 1.6f), top + 0.35f + 0.6f * u1 * u1, 0f);
                    partsB.Add(RemixGeo.Beam(transform, "Kasagi", cube, darkMat, p0, p1, 0.45f));
                    chase.Add(RemixGeo.Beam(transform, "Trim", cube, matB, p0 + Vector3.up * 0.28f, p1 + Vector3.up * 0.28f, 0.1f));
                }
                break;
            }
            case WorldSkin.Ocean:
            {
                const int n = 26;
                for (int i = 0; i < n; i++)
                {
                    float u = Mathf.Lerp(a0, a1, i / (float)(n - 1));
                    float s = 0.35f + 0.25f * Mathf.Abs(Mathf.Sin(i * 1.7f));
                    var mr = RemixGeo.Beam(transform, "Bubble", sphere, (i % 3) == 0 ? matB : matA, At(u, r), At(u, r), s);
                    bubbles.Add(mr.transform);
                    chase.Add(mr);
                }
                break;
            }
            default:    // Volcano
            {
                const int seg = 14;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.Lerp(a0, a1, i / (float)seg), u1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                    float j0 = (i % 2 == 0 ? 0.5f : -0.15f), j1 = ((i + 1) % 2 == 0 ? 0.5f : -0.15f);
                    partsB.Add(RemixGeo.Beam(transform, "Rock", cube, darkMat, At(u0, r + j0), At(u1, r + j1), 0.75f));
                    partsA.Add(RemixGeo.Beam(transform, "Crack", cube, matA, At(u0, r + j0 - 0.42f), At(u1, r + j1 - 0.42f), 0.14f));
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
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        // Arcade and Ocean take the tunnel's alternating accents; the others keep their own colour
        // (a red torii, a pink cane, orange lava).
        color = skin == WorldSkin.NeoTokyo ? new Color(1f, 0.12f, 0.15f)
              : skin == WorldSkin.Candy ? new Color(1f, 0.25f, 0.55f)
              : skin == WorldSkin.Volcano ? new Color(1f, 0.38f, 0.05f)
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
        int beat = ClubBeat.BeatIndex;
        float baseLevel = emissionIntensity * (0.4f + 0.4f * kick * energy + 0.25f * barPulse * energy);

        Color bCol = skin == WorldSkin.Candy ? White : skin == WorldSkin.Volcano ? new Color(1f, 0.6f, 0.15f) : Color.Lerp(color, Color.white, 0.5f);
        if (skin == WorldSkin.Candy && (beat & 1) == 1)
        {
            // Stripes swap on every other beat.
            RemixGeo.SetEmission(matA, bCol, Mathf.Min(RemixGeo.MaxEmission, baseLevel * 0.8f));
            RemixGeo.SetEmission(matB, color, Mathf.Min(RemixGeo.MaxEmission, baseLevel));
        }
        else
        {
            RemixGeo.SetEmission(matA, color, Mathf.Min(RemixGeo.MaxEmission, skin == WorldSkin.Volcano ? emissionIntensity * (0.5f + 1.2f * kick * energy) : baseLevel));
            RemixGeo.SetEmission(matB, bCol, Mathf.Min(RemixGeo.MaxEmission, baseLevel * 0.8f));
        }

        if (chase.Count > 0)
        {
            int bar = Mathf.FloorToInt(beats / 4f);
            float head = Mathf.Repeat(beats, 4f) / 4f * chase.Count;
            if ((bar & 1) == 1) head = chase.Count - head;
            for (int i = 0; i < chase.Count; i++)
            {
                float d = Mathf.Abs(head - i);
                float trail = d < 3f ? 1f - d / 3f : 0f;
                Color lc = Color.Lerp(chase[i].sharedMaterial == matA ? color : bCol, Color.white, 0.3f * trail);
                mpb.SetColor(RemixGeo.EmissionId, lc * Mathf.Min(RemixGeo.MaxEmission, emissionIntensity * (0.45f + 1.3f * trail * Mathf.Max(0.6f, energy))));
                chase[i].SetPropertyBlock(mpb);
            }
        }
        for (int i = 0; i < bubbles.Count; i++)
        {
            float s = 1f + 0.25f * kick * Mathf.Abs(Mathf.Sin(i * 1.3f + beats * 0.5f));
            bubbles[i].localScale = Vector3.one * (0.35f + 0.25f * Mathf.Abs(Mathf.Sin(i * 1.7f))) * s;
        }
    }
}
