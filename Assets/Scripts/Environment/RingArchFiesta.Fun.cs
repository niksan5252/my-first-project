using UnityEngine;

/// Video 19's rings: a candy-cane arch with a lollipop, a chrome diner portal with a burger on top,
/// a start gantry with its lights, a log gate with lanterns, crossed surfboards, an arch of fruit
/// under a pineapple, an emergency gate with a red/blue light bar.
public partial class RingArchFiesta
{
    private Color FunPrimary() =>
        IsNeon24 ? Neon24Primary()
        : IsNeon25 ? Neon25Primary()
        : skin == FiestaSkin.Candy ? new Color(1f, 0.3f, 0.7f)
        : skin == FiestaSkin.Diner ? new Color(1f, 0.1f, 0.25f)
        : skin == FiestaSkin.Karts ? new Color(1f, 0.15f, 0.15f)
        : skin == FiestaSkin.Camp ? new Color(1f, 0.55f, 0.15f)
        : skin == FiestaSkin.Beach ? new Color(1f, 0.45f, 0.35f)
        : skin == FiestaSkin.Fruit ? new Color(1f, 0.55f, 0.1f)
        : skin == FiestaSkin.Rescue ? new Color(1f, 0.12f, 0.1f)
        : HalloweenPrimary();

    private Color FunSecond() =>
        IsNeon24 ? Neon24Second()
        : IsNeon25 ? Neon25Second()
        : skin == FiestaSkin.Candy ? new Color(0.3f, 1f, 0.8f)
        : skin == FiestaSkin.Diner ? new Color(0.2f, 0.95f, 1f)
        : skin == FiestaSkin.Karts ? new Color(0.2f, 1f, 0.35f)
        : skin == FiestaSkin.Camp ? new Color(0.3f, 1f, 0.6f)
        : skin == FiestaSkin.Beach ? new Color(0.1f, 0.95f, 0.9f)
        : skin == FiestaSkin.Fruit ? new Color(0.5f, 1f, 0.2f)
        : skin == FiestaSkin.Rescue ? new Color(0.15f, 0.45f, 1f)
        : HalloweenSecond();

    private void BuildFun(Mesh cube, Mesh sphere, Mesh cyl, FiestaKit props, float c, float r, float a0, float a1)
    {
        if (IsNeon24) { BuildNeon24Ring(cube, sphere, cyl, props, c, r, a0, a1); return; }
        if (IsNeon25) { BuildNeon25Ring(cube, sphere, cyl, props, c, r, a0, a1); return; }
        Vector3 At(float a, float rad) => new Vector3(Mathf.Cos(a) * rad, c + Mathf.Sin(a) * rad, 0f);
        switch (skin)
        {
            case FiestaSkin.Candy:
            {
                const int seg = 20;
                var white = lightMat;
                for (int i = 0; i < seg; i++)
                {
                    float u0 = Mathf.Lerp(a0, a1, i / (float)seg), u1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                    var m = RemixGeo.Beam(transform, "Cane", cyl, i % 2 == 0 ? matA : white, At(u0, r), At(u1, r), 0.6f);
                    if (i % 2 == 0) chase.Add(m);
                }
                Bloom(props.Spawn("lollypop", transform, new Vector3(0f, c + r - 0.3f, -0.3f), 3.2f, 0f, Color.white, 0.6f));
                foreach (int side in new[] { -1, 1 })
                    props.Spawn("cupcake", transform, new Vector3(side * (halfBase + 0.6f), -0.5f, -0.3f), 1.4f, 0f, Color.white, 0.5f);
                break;
            }
            case FiestaSkin.Diner:
            {
                float x = halfBase - 0.3f, top = apex - 1.2f;
                foreach (int side in new[] { -1, 1 })
                    RemixGeo.Beam(transform, "Post", cube, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, top, 0f), 0.8f);
                RemixGeo.Beam(transform, "Lintel", cube, darkMat, new Vector3(-x - 0.4f, top + 0.4f, 0f), new Vector3(x + 0.4f, top + 0.4f, 0f), 0.9f);
                // A neon outline round the portal, chasing.
                Vector3[] pts = { new Vector3(-x + 0.5f, -0.3f, -0.45f), new Vector3(-x + 0.5f, top - 0.3f, -0.45f), new Vector3(x - 0.5f, top - 0.3f, -0.45f), new Vector3(x - 0.5f, -0.3f, -0.45f) };
                for (int e = 0; e < 3; e++)
                    for (int k = 0; k < 6; k++)
                    {
                        Vector3 p = Vector3.Lerp(pts[e], pts[e + 1], (k + 0.5f) / 6f);
                        chase.Add(Place(transform, "Tube", cube, k % 2 == 0 ? matA : matB, p, e == 1 ? new Vector3(0.9f, 0.14f, 0.1f) : new Vector3(0.14f, 0.9f, 0.1f)));
                    }
                Bloom(props.Spawn("burger-cheese-double", transform, new Vector3(0f, top + 0.9f, -0.2f), 2.2f, 0f, Color.white, 0.6f));
                break;
            }
            case FiestaSkin.Karts:
            {
                float x = halfBase - 0.2f, top = apex - 1f;
                foreach (int side in new[] { -1, 1 })
                    RemixGeo.Beam(transform, "Pylon", cube, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, top + 0.6f, 0f), 0.9f);
                RemixGeo.Beam(transform, "Gantry", cube, darkMat, new Vector3(-x - 0.5f, top, 0f), new Vector3(x + 0.5f, top, 0f), 1.2f);
                for (int k = 0; k < 12; k++)
                    Place(transform, "Check", cube, k % 2 == 0 ? lightMat : darkMat, new Vector3(-x + 0.6f + k * (2f * x - 1.2f) / 11f, top + 0.35f, -0.62f), new Vector3((2f * x - 1.2f) / 11f, 0.4f, 0.05f));
                for (int k = 0; k < 5; k++)
                    chase.Add(Place(transform, "Light", sphere, k < 3 ? matA : matB, new Vector3(-2.4f + k * 1.2f, top - 0.25f, -0.65f), Vector3.one * 0.6f));
                break;
            }
            case FiestaSkin.Camp:
            {
                float x = halfBase - 0.3f, top = apex - 0.8f;
                var wood = darkMat;
                foreach (int side in new[] { -1, 1 })
                    RemixGeo.Beam(transform, "Log", cyl, wood, new Vector3(side * x, -0.5f, 0f), new Vector3(side * (x - 0.4f), top + 0.5f, 0f), 0.7f);
                RemixGeo.Beam(transform, "Beam", cyl, wood, new Vector3(-x - 0.8f, top, 0f), new Vector3(x + 0.8f, top + 0.2f, 0f), 0.6f);
                for (int k = 0; k < 5; k++)
                {
                    float px = Mathf.Lerp(-x + 1.2f, x - 1.2f, k / 4f);
                    RemixGeo.Beam(transform, "Cord", cube, wood, new Vector3(px, top - 0.3f, -0.1f), new Vector3(px, top - 1f, -0.1f), 0.04f);
                    var l = props.Spawn("lantern-candle", transform, new Vector3(px, top - 1.8f, -0.1f), 0.8f, 0f, new Color(1f, 0.85f, 0.6f), 1f);
                    Bloom(l);
                    chase.Add(Place(transform, "Glow", sphere, k % 2 == 0 ? matA : matB, new Vector3(px, top - 1.4f, -0.35f), Vector3.one * 0.25f));
                }
                break;
            }
            case FiestaSkin.Beach:
            {
                // Two surfboards leaning in to cross at the top, bulbs along the edge.
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 a = new Vector3(side * (halfBase + 0.2f), -0.5f, 0f), b = new Vector3(side * 1.4f, apex + 0.8f, 0.1f * side);
                    var board = RemixGeo.Beam(transform, "Board", sphere, side < 0 ? matA : matB, a, b, 1f);
                    board.transform.localScale = new Vector3(0.6f, (b - a).magnitude, 0.12f);
                    for (int k = 1; k < 8; k++)
                        chase.Add(Place(transform, "Bulb", sphere, lightMat, Vector3.Lerp(a, b, k / 8f) + new Vector3(0f, 0f, -0.25f), Vector3.one * 0.25f));
                }
                Bloom(props.Spawn("coconut", transform, new Vector3(0f, apex + 1.3f, -0.4f), 0.9f, 0f, Color.white, 0.6f));
                break;
            }
            case FiestaSkin.Fruit:
            {
                const int seg = 18;
                string[] fruit = { "orange", "lemon", "apple", "strawberry" };
                for (int i = 0; i <= seg; i++)
                {
                    Vector3 p = At(Mathf.Lerp(a0, a1, i / (float)seg), r);
                    chase.Add(RemixGeo.Beam(transform, "Fruit", sphere, i % 2 == 0 ? matA : matB, p, p, 0.65f));
                    if (i % 3 == 1) props.Spawn(fruit[(i / 3) % fruit.Length], transform, p + new Vector3(0f, -0.3f, -0.5f), 0.7f, 0f, Color.white, 0.7f);
                }
                Bloom(props.Spawn("pineapple", transform, new Vector3(0f, c + r - 0.2f, -0.3f), 2.4f, 0f, Color.white, 0.6f));
                break;
            }
            case FiestaSkin.Rescue:
            {
                float x = halfBase - 0.3f, top = apex - 1f;
                foreach (int side in new[] { -1, 1 })
                    RemixGeo.Beam(transform, "Post", cube, darkMat, new Vector3(side * x, -0.5f, 0f), new Vector3(side * x, top, 0f), 0.7f);
                RemixGeo.Beam(transform, "Bar", cube, darkMat, new Vector3(-x - 0.4f, top, 0f), new Vector3(x + 0.4f, top, 0f), 0.9f);
                for (int k = 0; k < 10; k++)
                    chase.Add(Place(transform, "Light", cube, k % 2 == 0 ? matA : matB, new Vector3(-x + 0.8f + k * (2f * x - 1.6f) / 9f, top, -0.5f), new Vector3(0.9f, 0.45f, 0.1f)));
                foreach (int side in new[] { -1, 1 })
                    for (int k = 0; k < 6; k++)
                        Place(transform, "Stripe", cube, k % 2 == 0 ? matA : lightMat, new Vector3(side * x, 0.2f + k * 0.9f, -0.37f), new Vector3(0.72f, 0.45f, 0.04f));
                break;
            }
            default:
                BuildHalloweenRing(cube, sphere, cyl, props, c, r, a0, a1);
                break;
        }
    }
}
