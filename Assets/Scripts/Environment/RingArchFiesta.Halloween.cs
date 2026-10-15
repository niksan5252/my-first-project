using UnityEngine;

/// Video 20's Halloween rings: an arch of carved pumpkins, a cemetery gate with lanterns, a twisted
/// branch arch with potions under a witch hat, a "road closed" barrier, a gothic window arch with a
/// bat on top, a mossy log arch with wisps, and a party arch of bulbs under a pumpkin.
public partial class RingArchFiesta
{
    private Color HalloweenPrimary() =>
        skin == FiestaSkin.Pumpkin ? new Color(1f, 0.5f, 0.05f)
        : skin == FiestaSkin.Haunted ? new Color(0.55f, 1f, 0.95f)
        : skin == FiestaSkin.Witch ? new Color(0.3f, 1f, 0.3f)
        : skin == FiestaSkin.Zombie ? new Color(1f, 0.6f, 0.15f)
        : skin == FiestaSkin.Vampire ? new Color(1f, 0.05f, 0.15f)
        : skin == FiestaSkin.Swamp ? new Color(0.4f, 1f, 0.7f)
        : new Color(1f, 0.5f, 0.05f);

    private Color HalloweenSecond() =>
        skin == FiestaSkin.Pumpkin ? new Color(0.6f, 0.2f, 1f)
        : skin == FiestaSkin.Haunted ? new Color(0.6f, 0.3f, 1f)
        : skin == FiestaSkin.Witch ? new Color(0.7f, 0.3f, 1f)
        : skin == FiestaSkin.Zombie ? new Color(0.5f, 1f, 0.2f)
        : skin == FiestaSkin.Vampire ? new Color(0.55f, 0.2f, 1f)
        : skin == FiestaSkin.Swamp ? new Color(0.7f, 1f, 0.2f)
        : new Color(0.65f, 0.25f, 1f);

    private void BuildHalloweenRing(Mesh cube, Mesh sphere, Mesh cyl, FiestaKit props, float c, float r, float a0, float a1)
    {
        Vector3 At(float a, float rad) => new Vector3(Mathf.Cos(a) * rad, c + Mathf.Sin(a) * rad, 0f);
        switch (skin)
        {
            case FiestaSkin.Pumpkin:
            {
                const int seg = 14;
                for (int i = 0; i <= seg; i++)
                {
                    Vector3 p = At(Mathf.Lerp(a0, a1, i / (float)seg), r);
                    var pk = props.Spawn("pumpkin-carved", transform, p + Vector3.down * 0.45f, 0.9f, 180f, Color.white, 1f);
                    chase.Add(Place(transform, "Glow", sphere, i % 2 == 0 ? matA : matB, p + new Vector3(0f, 0f, 0.3f), Vector3.one * 0.5f));
                    if (i == seg / 2) Bloom(pk);
                }
                break;
            }
            case FiestaSkin.Haunted:
            {
                float x = halfBase - 0.2f;
                foreach (int side in new[] { -1, 1 })
                {
                    RemixGeo.Beam(transform, "Pillar", cube, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, 5f, 0f), 1f);
                    Bloom(props.Spawn("lantern-candle", transform, new Vector3(side * x, 5f, 0f), 1f, 0f, new Color(1f, 0.85f, 0.6f), 1f));
                }
                const int seg = 16;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.PI * i / seg, u1 = Mathf.PI * (i + 1) / seg;
                    Vector3 p0 = new Vector3(Mathf.Cos(u0) * (x - 0.6f), 4.6f + Mathf.Sin(u0) * 2.8f, 0f), p1 = new Vector3(Mathf.Cos(u1) * (x - 0.6f), 4.6f + Mathf.Sin(u1) * 2.8f, 0f);
                    RemixGeo.Beam(transform, "Iron", cube, darkMat, p0, p1, 0.18f);
                    chase.Add(Place(transform, "Spark", sphere, i % 2 == 0 ? matA : matB, p0 + Vector3.back * 0.15f, Vector3.one * 0.2f));
                }
                for (int k = 0; k < 9; k++)
                {
                    float px = Mathf.Lerp(-x + 1f, x - 1f, k / 8f);
                    float top = 4.6f + Mathf.Sqrt(Mathf.Max(0f, 1f - (px * px) / ((x - 0.6f) * (x - 0.6f)))) * 2.8f;
                    RemixGeo.Beam(transform, "Spike", cube, darkMat, new Vector3(px, top, 0f), new Vector3(px, top + 0.6f, 0f), 0.1f);
                }
                var skull = Place(transform, "Skull", sphere, lightMat, new Vector3(0f, 8f, -0.2f), new Vector3(1f, 0.9f, 0.8f));
                foreach (int e in new[] { -1, 1 }) Place(transform, "Eye", sphere, matA, new Vector3(e * 0.22f, 8.05f, -0.55f), Vector3.one * 0.22f);
                Bloom(skull.transform);
                break;
            }
            case FiestaSkin.Witch:
            {
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 a = new Vector3(side * (halfBase - 0.2f), -0.5f, 0f);
                    for (int k = 1; k <= 7; k++)
                    {
                        float u = k / 7f;
                        Vector3 b = new Vector3(side * (halfBase - 0.2f) * (1f - u * 0.92f) + Mathf.Sin(k * 2.1f) * 0.4f, -0.5f + Mathf.Sin(u * Mathf.PI * 0.5f) * (apex + 0.3f), Mathf.Cos(k * 1.7f) * 0.2f);
                        RemixGeo.Beam(transform, "Branch", cyl, darkMat, a, b, 0.45f - u * 0.2f);
                        if (k % 2 == 0)
                        {
                            RemixGeo.Beam(transform, "Cord", cube, darkMat, b, b + Vector3.down * 0.8f, 0.03f);
                            chase.Add(Place(transform, "Potion", sphere, k % 4 == 0 ? matA : matB, b + Vector3.down * 1f, new Vector3(0.35f, 0.45f, 0.35f)));
                        }
                        a = b;
                    }
                }
                var hat = new GameObject("Hat").transform;
                hat.SetParent(transform, false);
                hat.localPosition = new Vector3(0f, apex + 0.3f, 0f);
                Place(hat, "Brim", cyl, darkMat, Vector3.zero, new Vector3(2.4f, 0.04f, 2.4f));
                for (int k = 0; k < 5; k++) Place(hat, "Crown", cyl, darkMat, new Vector3(0f, 0.25f + k * 0.35f, 0f), new Vector3(1.2f - k * 0.22f, 0.18f, 1.2f - k * 0.22f));
                Place(hat, "Band", cyl, matB, new Vector3(0f, 0.2f, 0f), new Vector3(1.24f, 0.08f, 1.24f));
                Bloom(hat);
                break;
            }
            case FiestaSkin.Zombie:
            {
                float x = halfBase - 0.2f, top = apex - 1.6f;
                foreach (int side in new[] { -1, 1 })
                {
                    RemixGeo.Beam(transform, "Post", cube, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, top + 0.8f, 0f), 0.6f);
                    for (int k = 0; k < 6; k++)
                        Place(transform, "Hazard", cube, k % 2 == 0 ? matA : darkMat, new Vector3(side * x, 0f + k * 0.8f, -0.32f), new Vector3(0.62f, 0.4f, 0.04f));
                    Bloom(Place(transform, "Beacon", sphere, matA, new Vector3(side * x, top + 1.1f, 0f), Vector3.one * 0.5f).transform);
                }
                // A tilted barrier board with diagonal stripes.
                var board = new GameObject("Board").transform;
                board.SetParent(transform, false);
                board.localPosition = new Vector3(0f, top, -0.1f);
                board.localRotation = Quaternion.Euler(0f, 0f, 4f);
                Place(board, "Back", cube, darkMat, Vector3.zero, new Vector3(2f * x + 0.6f, 1.2f, 0.2f));
                for (int k = 0; k < 9; k++)
                    chase.Add(Place(board, "Stripe", cube, k % 2 == 0 ? matA : lightMat, new Vector3(-x + 0.6f + k * (2f * x - 1.2f) / 8f, 0f, -0.12f), new Vector3(0.7f, 1f, 0.03f)));
                break;
            }
            case FiestaSkin.Vampire:
            {
                float x = halfBase - 0.3f;
                foreach (int side in new[] { -1, 1 })
                {
                    RemixGeo.Beam(transform, "Pillar", cube, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, 4.5f, 0f), 0.9f);
                    Vector3 a = new Vector3(side * x, 4.5f, 0f);
                    for (int k = 1; k <= 8; k++)
                    {
                        float u = k / 8f;
                        Vector3 b = new Vector3(side * x * (1f - Mathf.Pow(u, 1.6f)), 4.5f + Mathf.Sin(u * Mathf.PI * 0.5f) * (apex - 3.4f), 0f);
                        RemixGeo.Beam(transform, "Arch", cube, darkMat, a, b, 0.7f);
                        chase.Add(RemixGeo.Beam(transform, "Glass", cube, k % 2 == 0 ? matA : matB, Vector3.Lerp(a, b, 0.5f) + Vector3.back * 0.38f, Vector3.Lerp(a, b, 0.5f) + Vector3.back * 0.38f, 0.25f));
                        a = b;
                    }
                }
                var bat = new GameObject("Bat").transform;
                bat.SetParent(transform, false);
                bat.localPosition = new Vector3(0f, apex + 1f, -0.3f);
                Place(bat, "Body", sphere, darkMat, Vector3.zero, new Vector3(0.6f, 0.7f, 0.4f));
                foreach (int s in new[] { -1, 1 })
                {
                    Place(bat, "Wing", cube, darkMat, new Vector3(s * 1f, 0.15f, 0f), new Vector3(1.5f, 0.5f, 0.06f));
                    Place(bat, "Eye", sphere, matA, new Vector3(s * 0.12f, 0.15f, -0.2f), Vector3.one * 0.12f);
                }
                Bloom(bat);
                break;
            }
            case FiestaSkin.Swamp:
            {
                float x = halfBase - 0.2f, top = apex - 1f;
                foreach (int side in new[] { -1, 1 })
                    RemixGeo.Beam(transform, "Log", cyl, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * (x - 0.8f), top, 0f), 0.75f);
                RemixGeo.Beam(transform, "Lintel", cyl, darkMat, new Vector3(-x - 0.3f, top - 0.2f, 0f), new Vector3(x + 0.3f, top + 0.3f, 0f), 0.65f);
                var moss = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                moss.SetColor("_BaseColor", new Color(0.15f, 0.3f, 0.12f));
                owned.Add(moss);
                for (int k = 0; k < 9; k++)
                {
                    float px = Mathf.Lerp(-x + 0.6f, x - 0.6f, k / 8f);
                    Place(transform, "Moss", cube, moss, new Vector3(px, top - 0.9f, -0.2f), new Vector3(0.15f, Random.Range(0.8f, 1.6f), 0.4f));
                    if (k % 2 == 0) chase.Add(Place(transform, "Wisp", sphere, k % 4 == 0 ? matA : matB, new Vector3(px, top - 1.9f, -0.3f), Vector3.one * 0.35f));
                }
                break;
            }
            default:    // Monster party
            {
                const int seg = 20;
                for (int i = 0; i <= seg; i++)
                {
                    Vector3 p = At(Mathf.Lerp(a0, a1, i / (float)seg), r);
                    chase.Add(RemixGeo.Beam(transform, "Bulb", sphere, i % 3 == 0 ? matA : (i % 3 == 1 ? matB : lightMat), p, p, 0.5f));
                    if (i < seg) RemixGeo.Beam(transform, "Truss", cube, darkMat, At(Mathf.Lerp(a0, a1, i / (float)seg), r + 0.3f), At(Mathf.Lerp(a0, a1, (i + 1) / (float)seg), r + 0.3f), 0.25f);
                }
                Bloom(props.Spawn("pumpkin-carved", transform, new Vector3(0f, c + r + 0.2f, -0.2f), 1.8f, 180f, Color.white, 1f));
                foreach (int side in new[] { -1, 1 })
                    props.Spawn("character-skeleton", transform, new Vector3(side * (halfBase + 0.8f), -0.5f, 0f), 2.2f, 180f + side * 20f, Color.white, 0.5f);
                break;
            }
        }
    }
}
