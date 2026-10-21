using UnityEngine;

/// Video 25's rings: an arch of bubbles over corals (Coral Reef), a natural stone arch with a glowing
/// seam (Sunset Canyon), a torii gate with paper lanterns (Sakura Night), a stepped Aztec gate under
/// a gold sun disc (El Dorado), a rainbow standing on clouds (Sky Kingdom).
public partial class RingArchFiesta
{
    private bool IsNeon25 => skin == FiestaSkin.CoralReef || skin == FiestaSkin.SunsetCanyon || skin == FiestaSkin.SakuraNight
                          || skin == FiestaSkin.ElDorado || skin == FiestaSkin.SkyKingdom;

    private Color Neon25Primary() =>
        skin == FiestaSkin.CoralReef ? new Color(1f, 0.35f, 0.55f)
        : skin == FiestaSkin.SunsetCanyon ? new Color(1f, 0.5f, 0.15f)
        : skin == FiestaSkin.SakuraNight ? new Color(1f, 0.12f, 0.18f)
        : skin == FiestaSkin.ElDorado ? new Color(1f, 0.75f, 0.15f)
        : new Color(1f, 0.3f, 0.3f);

    private Color Neon25Second() =>
        skin == FiestaSkin.CoralReef ? new Color(0.1f, 1f, 0.9f)
        : skin == FiestaSkin.SunsetCanyon ? new Color(0.1f, 0.9f, 0.85f)
        : skin == FiestaSkin.SakuraNight ? new Color(1f, 0.55f, 0.8f)
        : skin == FiestaSkin.ElDorado ? new Color(0.2f, 1f, 0.6f)
        : new Color(0.25f, 0.6f, 1f);

    private void BuildNeon25Ring(Mesh cube, Mesh sphere, Mesh cyl, FiestaKit props, float c, float r, float a0, float a1)
    {
        Vector3 At(float a, float rad) => new Vector3(Mathf.Cos(a) * rad, c + Mathf.Sin(a) * rad, 0f);
        switch (skin)
        {
            case FiestaSkin.CoralReef:
            {
                // Bubbles of two sizes round the arch, corals branching up at both feet.
                const int seg = 20;
                for (int i = 0; i <= seg; i++)
                {
                    Vector3 p = At(Mathf.Lerp(a0, a1, i / (float)seg), r);
                    chase.Add(Place(transform, "Bubble", sphere, i % 2 == 0 ? matA : matB, p, Vector3.one * (i % 3 == 0 ? 0.75f : 0.45f)));
                }
                foreach (int side in new[] { -1, 1 })
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 b = new Vector3(side * (halfBase - 0.3f), -0.5f, 0f), top = b + new Vector3(side * (k - 1.5f) * 0.45f, 2.2f + k * 0.5f, 0f);
                        RemixGeo.Beam(transform, "Coral", cyl, k % 2 == 0 ? matA : matB, b, top, 0.22f);
                        Bloom(Place(transform, "Tip", sphere, matB, top, Vector3.one * 0.38f).transform);
                    }
                break;
            }
            case FiestaSkin.SunsetCanyon:
            {
                // Rough stone blocks round the arch, a glowing seam along its inside.
                const int seg = 14;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.Lerp(a0, a1, i / (float)seg), u1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                    var block = RemixGeo.Beam(transform, "Stone", cube, darkMat, At(u0, r + 0.35f), At(u1, r + 0.35f), 1.1f);
                    block.transform.localScale = Vector3.Scale(block.transform.localScale, new Vector3(1f + (i % 3) * 0.15f, 1f, 1f));
                    chase.Add(RemixGeo.Beam(transform, "Seam", cube, i % 2 == 0 ? matA : matB, At(u0, r - 0.3f), At(u1, r - 0.3f), 0.16f));
                }
                break;
            }
            case FiestaSkin.SakuraNight:
            {
                // A torii: two red pillars, the curved top beam and the tie beam under it, lanterns hanging.
                float x = halfBase - 0.5f, top = 7.4f;
                foreach (int side in new[] { -1, 1 })
                {
                    RemixGeo.Beam(transform, "Pillar", cyl, matA, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, top, 0f), 0.55f);
                    Place(transform, "Foot", cube, darkMat, new Vector3(side * x, -0.2f, 0f), new Vector3(0.9f, 0.6f, 0.9f));
                    Bloom(Place(transform, "Lantern", sphere, matB, new Vector3(side * (x - 1.6f), top - 2.2f, -0.2f), new Vector3(0.6f, 0.85f, 0.6f)).transform);
                }
                RemixGeo.Beam(transform, "Nuki", cube, matA, new Vector3(-x - 0.6f, top - 1.3f, 0f), new Vector3(x + 0.6f, top - 1.3f, 0f), 0.4f);
                const int seg = 10;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = i / (float)seg, u1 = (i + 1) / (float)seg;
                    Vector3 p0 = new Vector3(Mathf.Lerp(-x - 1.4f, x + 1.4f, u0), top + 0.35f + 0.5f * Mathf.Pow(Mathf.Abs(u0 * 2f - 1f), 2f), 0f);
                    Vector3 p1 = new Vector3(Mathf.Lerp(-x - 1.4f, x + 1.4f, u1), top + 0.35f + 0.5f * Mathf.Pow(Mathf.Abs(u1 * 2f - 1f), 2f), 0f);
                    RemixGeo.Beam(transform, "Kasagi", cube, darkMat, p0, p1, 0.55f);
                    chase.Add(RemixGeo.Beam(transform, "Glow", cube, i % 2 == 0 ? matA : matB, p0 + Vector3.down * 0.35f, p1 + Vector3.down * 0.35f, 0.12f));
                }
                break;
            }
            case FiestaSkin.ElDorado:
            {
                // Two stepped pillars, a lintel across, a gold sun disc on top; jade studs chasing.
                float x = halfBase - 0.6f, top = 6.8f;
                foreach (int side in new[] { -1, 1 })
                    for (int k = 0; k < 4; k++)
                    {
                        float w = 1.6f - k * 0.3f;
                        Place(transform, "Step", cube, darkMat, new Vector3(side * x, k * 1.9f + 0.45f, 0f), new Vector3(w, 1.9f, w));
                        chase.Add(Place(transform, "Stud", cube, k % 2 == 0 ? matA : matB, new Vector3(side * x, k * 1.9f + 0.45f, -w * 0.5f - 0.02f), new Vector3(w * 0.6f, 0.25f, 0.05f)));
                    }
                RemixGeo.Beam(transform, "Lintel", cube, darkMat, new Vector3(-x - 0.8f, top, 0f), new Vector3(x + 0.8f, top, 0f), 1.1f);
                for (int k = 0; k < 9; k++)
                    chase.Add(Place(transform, "Gold", cube, k % 2 == 0 ? matA : matB, new Vector3(Mathf.Lerp(-x, x, k / 8f), top, -0.56f), new Vector3(0.5f, 0.5f, 0.05f)));
                var sun = Place(transform, "Sun", cyl, matA, new Vector3(0f, top + 1.6f, 0f), new Vector3(2.2f, 0.1f, 2.2f));
                sun.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Bloom(sun.transform);
                break;
            }
            default:    // Sky Kingdom
            {
                // A rainbow in six bands, clouds at both feet.
                Color[] bands = { new Color(1f, 0.2f, 0.25f), new Color(1f, 0.6f, 0.1f), new Color(1f, 0.95f, 0.2f), new Color(0.3f, 1f, 0.4f), new Color(0.2f, 0.6f, 1f), new Color(0.6f, 0.3f, 1f) };
                const int seg = 18;
                for (int b = 0; b < bands.Length; b++)
                {
                    var mat = RemixGeo.Neon(bands[b], emissionIntensity);
                    owned.Add(mat);
                    float rad = r + 0.6f - b * 0.22f;
                    for (int i = 0; i < seg; i++)
                    {
                        float u0 = Mathf.Lerp(a0, a1, i / (float)seg), u1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                        var band = RemixGeo.Beam(transform, "Band", cube, mat, At(u0, rad), At(u1, rad), 0.22f);
                        if (b == 0) chase.Add(band);
                    }
                }
                foreach (int side in new[] { -1, 1 })
                    for (int k = 0; k < 4; k++)
                        Bloom(Place(transform, "Cloud", sphere, lightMat, new Vector3(side * (halfBase + (k - 1.5f) * 0.6f), 0.2f + (k % 2) * 0.5f, 0f), Vector3.one * (1.2f + (k % 2) * 0.4f)).transform);
                break;
            }
        }
    }
}
