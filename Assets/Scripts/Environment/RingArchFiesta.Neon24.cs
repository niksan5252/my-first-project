using UnityEngine;

/// Video 24's rings, one shape per world instead of video 1's octagons (the user: "change the arches -
/// the style, the colours, how they look"): a bamboo arch with hibiscus flowers (Lagoon), a jagged
/// crystal arch (Crystal Cave), a hazard-striped steel gantry (Cyber Factory), a round portal with
/// orbiting moons (Planet Ring), two vines twisting into an arch with leaves (Firefly Jungle).
public partial class RingArchFiesta
{
    private bool IsNeon24 => skin == FiestaSkin.Lagoon || skin == FiestaSkin.CrystalCave || skin == FiestaSkin.CyberFactory
                          || skin == FiestaSkin.PlanetRing || skin == FiestaSkin.FireflyJungle;

    private Color Neon24Primary() =>
        skin == FiestaSkin.Lagoon ? new Color(1f, 0.3f, 0.6f)
        : skin == FiestaSkin.CrystalCave ? new Color(0.2f, 0.95f, 1f)
        : skin == FiestaSkin.CyberFactory ? new Color(1f, 0.55f, 0.05f)
        : skin == FiestaSkin.PlanetRing ? new Color(0.65f, 0.3f, 1f)
        : new Color(0.5f, 1f, 0.2f);

    private Color Neon24Second() =>
        skin == FiestaSkin.Lagoon ? new Color(0.1f, 1f, 0.85f)
        : skin == FiestaSkin.CrystalCave ? new Color(1f, 0.2f, 0.85f)
        : skin == FiestaSkin.CyberFactory ? new Color(0.1f, 0.9f, 1f)
        : skin == FiestaSkin.PlanetRing ? new Color(1f, 0.75f, 0.25f)
        : new Color(1f, 0.3f, 0.7f);

    private void BuildNeon24Ring(Mesh cube, Mesh sphere, Mesh cyl, FiestaKit props, float c, float r, float a0, float a1)
    {
        Vector3 At(float a, float rad) => new Vector3(Mathf.Cos(a) * rad, c + Mathf.Sin(a) * rad, 0f);
        switch (skin)
        {
            case FiestaSkin.Lagoon:
            {
                // Bamboo: a round arch of short canes, the joints lit, hibiscus at the top and both feet.
                const int seg = 18;
                for (int i = 0; i < seg; i++)
                {
                    Vector3 p0 = At(Mathf.Lerp(a0, a1, i / (float)seg), r), p1 = At(Mathf.Lerp(a0, a1, (i + 1) / (float)seg), r);
                    RemixGeo.Beam(transform, "Cane", cyl, darkMat, p0, p1, 0.32f);
                    chase.Add(Place(transform, "Joint", cyl, i % 2 == 0 ? matA : matB, p0, new Vector3(0.42f, 0.08f, 0.42f)));
                }
                foreach (float u in new[] { 0.08f, 0.5f, 0.92f })
                {
                    Vector3 p = At(Mathf.Lerp(a0, a1, u), r) + Vector3.back * 0.3f;
                    var f = new GameObject("Hibiscus").transform;
                    f.SetParent(transform, false);
                    f.localPosition = p;
                    for (int k = 0; k < 5; k++)
                    {
                        float a = k * Mathf.PI * 2f / 5f;
                        Place(f, "Petal", sphere, matA, new Vector3(Mathf.Cos(a) * 0.45f, Mathf.Sin(a) * 0.45f, 0f), new Vector3(0.6f, 0.6f, 0.15f));
                    }
                    Place(f, "Heart", sphere, matB, Vector3.back * 0.1f, Vector3.one * 0.3f);
                    Bloom(f);
                }
                break;
            }
            case FiestaSkin.CrystalCave:
            {
                // Crystal: the arch's outline broken into shards pointing in and out, a big one on top.
                const int seg = 22;
                for (int i = 0; i <= seg; i++)
                {
                    float a = Mathf.Lerp(a0, a1, i / (float)seg);
                    float len = i % 2 == 0 ? 1.6f : 0.9f;
                    Vector3 p = At(a, r), dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    var shard = Place(transform, "Shard", cube, i % 3 == 0 ? matB : matA, p, new Vector3(0.28f, len, 0.28f));
                    shard.transform.localRotation = Quaternion.FromToRotation(Vector3.up, i % 2 == 0 ? dir : -dir) * Quaternion.Euler(0f, 45f, 0f);
                    shard.transform.localPosition = p + (i % 2 == 0 ? dir : -dir) * len * 0.35f;
                    chase.Add(shard);
                }
                var top = Place(transform, "Crown", cube, matB, At(Mathf.PI * 0.5f, r) + Vector3.up * 0.9f, new Vector3(0.9f, 2.2f, 0.9f));
                top.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
                Bloom(top.transform);
                break;
            }
            case FiestaSkin.CyberFactory:
            {
                // A steel gantry: two square pillars and a beam, hazard stripes, warning lamps chasing.
                float x = halfBase - 0.4f, top = 7.2f;
                foreach (int side in new[] { -1, 1 })
                {
                    RemixGeo.Beam(transform, "Pillar", cube, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, top, 0f), 0.6f);
                    for (int k = 0; k < 7; k++)
                        chase.Add(Place(transform, "Stripe", cube, k % 2 == 0 ? matA : darkMat, new Vector3(side * (x - 0.31f), 0.3f + k * 0.5f, 0f), new Vector3(0.05f, 0.25f, 0.62f)));
                }
                RemixGeo.Beam(transform, "Beam", cube, darkMat, new Vector3(-x - 0.3f, top, 0f), new Vector3(x + 0.3f, top, 0f), 0.8f);
                const int stripes = 14;
                for (int k = 0; k < stripes; k++)
                {
                    float px = Mathf.Lerp(-x + 0.3f, x - 0.3f, (k + 0.5f) / stripes);
                    var s = Place(transform, "Hazard", cube, k % 2 == 0 ? matA : darkMat, new Vector3(px, top, -0.42f), new Vector3((2f * x - 0.6f) / stripes, 0.6f, 0.04f));
                    s.transform.localRotation = Quaternion.Euler(0f, 0f, 25f);
                    if (k % 2 == 0) chase.Add(s);
                }
                foreach (float px in new[] { -x * 0.6f, 0f, x * 0.6f })
                {
                    var lamp = Place(transform, "Lamp", sphere, matB, new Vector3(px, top - 0.6f, -0.2f), Vector3.one * 0.45f);
                    Bloom(lamp.transform);
                }
                break;
            }
            case FiestaSkin.PlanetRing:
            {
                // A round portal: a full ring of glowing segments round the road (its lower part under
                // the floor), two moons orbiting it.
                float cy = 3.6f, R = halfBase + 0.2f;
                const int seg = 32;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.PI * 2f * i / seg, u1 = Mathf.PI * 2f * (i + 1) / seg;
                    Vector3 p0 = new Vector3(Mathf.Cos(u0) * R, cy + Mathf.Sin(u0) * R, 0f), p1 = new Vector3(Mathf.Cos(u1) * R, cy + Mathf.Sin(u1) * R, 0f);
                    if (p0.y < -0.6f && p1.y < -0.6f) continue;
                    chase.Add(RemixGeo.Beam(transform, "Seg", cube, i % 4 == 0 ? matB : matA, p0, p1, 0.3f));
                }
                const int studs = 8;
                for (int i = 0; i < studs; i++)
                {
                    float u = Mathf.PI * (0.1f + 0.8f * i / (studs - 1));
                    Place(transform, "Stud", sphere, matB, new Vector3(Mathf.Cos(u) * (R + 0.35f), cy + Mathf.Sin(u) * (R + 0.35f), 0f), Vector3.one * 0.3f);
                }
                foreach (float u in new[] { 0.3f, 0.7f })
                {
                    var moon = Place(transform, "Moon", sphere, u < 0.5f ? matB : lightMat, new Vector3(Mathf.Cos(Mathf.PI * u) * (R + 1.1f), cy + Mathf.Sin(Mathf.PI * u) * (R + 1.1f), 0f), Vector3.one * 0.8f);
                    Bloom(moon.transform);
                }
                break;
            }
            default:    // Firefly Jungle
            {
                // Two vines twisting round each other into the arch, leaves along them, beads of light.
                const int seg = 26;
                for (int strand = 0; strand < 2; strand++)
                    for (int i = 0; i < seg; i++)
                    {
                        float u0 = i / (float)seg, u1 = (i + 1) / (float)seg;
                        float w0 = Mathf.Sin(u0 * Mathf.PI * 6f + strand * Mathf.PI) * 0.35f, w1 = Mathf.Sin(u1 * Mathf.PI * 6f + strand * Mathf.PI) * 0.35f;
                        Vector3 p0 = At(Mathf.Lerp(a0, a1, u0), r + w0) + Vector3.forward * w0, p1 = At(Mathf.Lerp(a0, a1, u1), r + w1) + Vector3.forward * w1;
                        RemixGeo.Beam(transform, "Vine", cyl, darkMat, p0, p1, 0.18f);
                        if (i % 3 == strand)
                        {
                            var leaf = Place(transform, "Leaf", sphere, matA, p0 + Vector3.back * 0.2f, new Vector3(0.55f, 0.22f, 0.1f));
                            leaf.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(a0, a1, u0) * Mathf.Rad2Deg + 40f * (strand == 0 ? 1f : -1f));
                            chase.Add(leaf);
                        }
                        if (i % 4 == 2 && strand == 0) chase.Add(Place(transform, "Bead", sphere, matB, p0 + new Vector3(0f, -0.5f, -0.1f), Vector3.one * 0.26f));
                    }
                var flower = Place(transform, "Flower", sphere, matB, At(Mathf.PI * 0.5f, r) + new Vector3(0f, 0.2f, -0.3f), new Vector3(1f, 1f, 0.3f));
                Bloom(flower.transform);
                break;
            }
        }
    }
}
