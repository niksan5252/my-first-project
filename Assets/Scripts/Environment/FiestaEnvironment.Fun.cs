using System.Collections.Generic;
using UnityEngine;

/// Video 19's seven worlds on FiestaEnvironment's machinery (scrollers, riders, floaters, bursts,
/// flashers, the bloom on drops): Candy, Diner, Karts, Camp, Beach, Fruit, Rescue. Kenney CC0
/// models wherever one fits (sweets, fast food, karts and emergency vehicles, pines, tents,
/// campfires, mushrooms, fruit), primitives for the rest.
public partial class FiestaEnvironment
{
    // Rescue: the light bars on the emergency vehicles, red and blue, swapping every half beat.
    private readonly List<MeshRenderer> sirenRed = new List<MeshRenderer>();
    private readonly List<MeshRenderer> sirenBlue = new List<MeshRenderer>();
    private readonly List<Transform> searchlights = new List<Transform>();   // Rescue: beams swaying over the sky

    private void BuildFun()
    {
        switch (skin)
        {
            case FiestaSkin.Candy: BuildCandy(); break;
            case FiestaSkin.Diner: BuildDiner(); break;
            case FiestaSkin.Karts: BuildKarts(); break;
            case FiestaSkin.Camp: BuildCamp(); break;
            case FiestaSkin.Beach: BuildBeach(); break;
            case FiestaSkin.Fruit: BuildFruit(); break;
            default: BuildRescue(); break;
        }
    }

    private void FunUpdate(float beats)
    {
        for (int i = 0; i < searchlights.Count; i++)
            searchlights[i].localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 0.5f + i) * 20f, 0f, Mathf.Sin(Time.time * 0.7f + i * 1.7f) * 40f);
        if (sirenRed.Count == 0) return;
        bool red = Mathf.Repeat(beats, 1f) < 0.5f;
        foreach (var r in sirenRed) if (r != null) r.enabled = red;
        foreach (var r in sirenBlue) if (r != null) r.enabled = !red;
    }

    private void Bounce(Transform t, float height)
    {
        if (t == null) return;
        floaters.Add(new Mover { t = t, basePos = t.localPosition, seed = Rand(0f, 6.28f), speed = height, group = -17 });
    }

    // A vehicle on a side lane: oncoming ones (faster than the road) face the camera.
    private void Vehicle(Transform root, string model, float x, float speed, float height, Color tint, float glow, bool siren = false)
    {
        var c = Prop(root, "Vehicle", new Vector3(x, -0.5f, Rand(5f, Far)));
        bool oncoming = speed > 1f;
        var body = Kit(model, c, Vector3.zero, height, oncoming ? 180f : 0f, tint, glow);
        float front = oncoming ? -1.6f : 1.6f;
        foreach (float hx in new[] { -0.45f, 0.45f })
            Place("Headlight", sphere, G(2), c, new Vector3(hx, 0.6f, front), Vector3.one * 0.22f);
        if (siren)
        {
            sirenRed.Add(Place("SirenR", cube, Neon(new Color(1f, 0.08f, 0.08f)), c, new Vector3(-0.3f, height + 0.08f, 0f), new Vector3(0.45f, 0.16f, 0.3f)));
            sirenBlue.Add(Place("SirenB", cube, Neon(new Color(0.1f, 0.35f, 1f)), c, new Vector3(0.3f, height + 0.08f, 0f), new Vector3(0.45f, 0.16f, 0.3f)));
        }
        riders.Add(new Mover { t = c, speed = speed });
    }

    // ---------- Candy land ----------

    private void BuildCandy()
    {
        Color pink = new Color(1f, 0.3f, 0.7f), mint = new Color(0.3f, 1f, 0.8f), lemon = new Color(1f, 0.9f, 0.3f), grape = new Color(0.7f, 0.4f, 1f), sky = new Color(0.4f, 0.8f, 1f);
        Group(pink); Group(mint); Group(lemon); Group(grape); Group(sky);
        var root = Root("Candy");
        Ground(root, Lit(new Color(0.35f, 0.12f, 0.25f), 0.6f, 0f, 0.15f));
        string[] small = { "cupcake", "ice-cream", "sundae", "muffin", "cake-birthday", "popsicle" };
        int n = 0;
        for (float z = -Behind; z < Far; z += 4.5f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Sweet", new Vector3(side * Rand(7.8f, 9.2f), -0.5f, z + (side > 0 ? 2.2f : 0f)));
                if ((n + (side > 0 ? 1 : 0)) % 3 == 0)
                    Kit("lollypop", p, Vector3.zero, Rand(5f, 7f), Rand(0f, 360f), Color.white, 0.55f);
                else
                    Bounce(Kit(small[rng.Next(small.Length)], p, Vector3.zero, Rand(1.8f, 2.8f), Rand(0f, 360f), Color.white, 0.5f), 0.25f);
                scrollers.Add(new Mover { t = p });
            }
        // Giant donuts standing on their rims further out, rolling.
        for (float z = -Behind; z < Far; z += 11f)
            foreach (int side in new[] { -1, 1 })
            {
                var d = Prop(root, "Donut", new Vector3(side * Rand(13f, 15f), 2.8f, z + (side > 0 ? 5.5f : 0f)));
                var m = Kit(rng.NextDouble() < 0.5 ? "donut-sprinkles" : "donut-chocolate", d, Vector3.zero, 1.3f, 0f, Color.white, 0.5f);
                if (m != null) { m.localRotation = Quaternion.Euler(0f, 0f, 90f); m.localPosition = new Vector3(-0.6f, 0f, 0f) * m.localScale.x * 0.2f; }
                floaters.Add(new Mover { t = d, basePos = new Vector3(0f, 90f, 0f), seed = Rand(0f, 6.28f), speed = side * 60f, group = -18 });
                scrollers.Add(new Mover { t = d });
            }
        // Candy-cane poles with a bulb on top, close to the road.
        for (float z = -Behind; z < Far; z += 9f)
            foreach (int side in new[] { -1, 1 })
            {
                var cane = Prop(root, "Cane", new Vector3(side * 7.2f, -0.5f, z + 4.5f));
                for (int k = 0; k < 8; k++)
                    Place("Stripe", cyl, k % 2 == 0 ? Lit(new Color(0.9f, 0.9f, 0.9f), 0.6f, 0f, 0.2f) : G(0), cane, new Vector3(0f, 0.3f + k * 0.55f, 0f), new Vector3(0.35f, 0.275f, 0.35f), new Vector3(0f, 0f, 12f));
                pulsers.Add(new Mover { t = Place("Ball", sphere, G(2), cane, new Vector3(0f, 4.9f, 0f), Vector3.one * 0.6f).transform, group = 2, size = 0.6f });
                scrollers.Add(new Mover { t = cane });
            }
        // Cotton-candy clouds on the horizon.
        for (float z = -Behind; z < Far; z += 10f)
            foreach (int side in new[] { -1, 1 })
            {
                var cl = Prop(root, "Cloud", new Vector3(side * Rand(24f, 32f), Rand(6f, 12f), z));
                for (int k = 0; k < 5; k++)
                    Place("Puff", sphere, Lit(k % 2 == 0 ? new Color(1f, 0.7f, 0.85f) : new Color(0.75f, 0.85f, 1f), 0.2f, 0f, 0.35f), cl, new Vector3(Rand(-3f, 3f), Rand(-1f, 1f), Rand(-2f, 2f)), Vector3.one * Rand(2.5f, 4.5f));
                scrollers.Add(new Mover { t = cl });
            }
        Fireworks(Root("Fireworks"), 0, 1);
        glowSkyMat = SkyQuad("Sun", new Vector3(0f, 30f, 345f), 60f, 60f, new Color(1f, 0.6f, 0.8f, 1f), Sprite(128, 0.45f), 1.1f);
        SkyQuad("Haze", new Vector3(0f, 25f, 350f), 260f, 55f, new Color(1f, 0.5f, 0.8f, 0.5f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Sprinkles", 200, cube, new[] { G(0), G(1), G(2), G(3), G(4) }, new Vector3(0.05f, 0.18f, 0.05f), new Vector2(0.6f, 1.4f));
    }

    // ---------- Retro diner ----------

    private void BuildDiner()
    {
        Color red = new Color(1f, 0.1f, 0.25f), aqua = new Color(0.2f, 0.95f, 1f), amber = new Color(1f, 0.7f, 0.25f), pink = new Color(1f, 0.35f, 0.75f);
        Group(red); Group(aqua); Group(amber); Group(pink);
        var root = Root("Diner");
        Ground(root, Lit(new Color(0.05f, 0.05f, 0.06f), 0.8f, 0.2f));
        var chrome = Lit(new Color(0.6f, 0.62f, 0.66f), 0.9f, 0.9f, 0.05f);
        var tileA = Lit(new Color(0.85f, 0.85f, 0.85f), 0.5f, 0f, 0.12f);
        var tileB = Lit(new Color(0.05f, 0.05f, 0.05f), 0.5f);
        var wall = Lit(new Color(0.55f, 0.85f, 0.8f) * 0.5f, 0.4f, 0f, 0.1f);
        int n = 0;
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 13f, n++)
            {
                var d = Prop(root, "Diner", new Vector3(side * 14f, -0.5f, z + 6f));
                Place("Body", cube, wall, d, new Vector3(0f, 2.2f, 0f), new Vector3(5f, 4.4f, 10f));
                for (int k = 0; k < 10; k++)
                    Place("Check", cube, k % 2 == 0 ? tileA : tileB, d, new Vector3(-side * 2.52f, 0.4f, -4.5f + k), new Vector3(0.04f, 0.8f, 1f));
                Place("Chrome", cube, chrome, d, new Vector3(-side * 2.55f, 1.0f, 0f), new Vector3(0.08f, 0.15f, 10f));
                for (int w = 0; w < 4; w++)
                    Place("Window", cube, G(2), d, new Vector3(-side * 2.53f, 2.4f, -3.6f + w * 2.4f), new Vector3(0.03f, 1.4f, 1.8f));
                Place("Roof", cube, G(n % 2 == 0 ? 0 : 1), d, new Vector3(-side * 2.6f, 4.45f, 0f), new Vector3(0.12f, 0.12f, 10.1f));
                var sign = Prop(d, "Sign", new Vector3(0f, 6.2f, 0f));
                Place("Board", cube, tileB, sign, Vector3.zero, new Vector3(0.3f, 1.4f, 6f));
                for (int k = 0; k < 5; k++)
                    flashers.Add(Place("Letter", cube, G(k % 2 == 0 ? 0 : 3), sign, new Vector3(-side * 0.2f, 0f, -2.2f + k * 1.1f), new Vector3(0.05f, 1f, 0.7f)));
                scrollers.Add(new Mover { t = d });
            }
        // Fast-food signs on poles by the road, turning: a burger, fries, a soda, a hot dog.
        string[] food = { "burger-cheese-double", "fries", "soda", "hot-dog", "pizza", "frappe" };
        int f = 0;
        for (float z = -Behind; z < Far; z += 7f)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "FoodSign", new Vector3(side * 8.4f, -0.5f, z + (side > 0 ? 3.5f : 0f)));
                RemixGeo.Beam(p, "Pole", cyl, chrome, Vector3.zero, new Vector3(0f, 4.2f, 0f), 0.15f);
                var spin = Prop(p, "Spin", new Vector3(0f, 4.3f, 0f));
                Kit(food[f++ % food.Length], spin, Vector3.zero, 2f, 0f, Color.white, 0.55f);
                floaters.Add(new Mover { t = spin, seed = Rand(0f, 6.28f), speed = 45f, group = -16 });
                scrollers.Add(new Mover { t = p });
            }
        string[] cars = { "sedan", "sedan-sports", "taxi", "hatchback-sports", "suv", "police" };
        Color[] paint = { new Color(1f, 0.5f, 0.6f), new Color(0.5f, 1f, 0.95f), new Color(1f, 0.95f, 0.5f), new Color(0.7f, 0.7f, 1f), Color.white, Color.white };
        int ci = 0;
        foreach (int side in new[] { -1, 1 })
            for (int k = 0; k < 4; k++, ci++)
                Vehicle(root, cars[ci % cars.Length], side * 10.5f, k % 2 == 0 ? 0.5f : 1.6f, 1.6f, paint[ci % paint.Length], 0.5f);
        Fireworks(Root("Fireworks"), 0, 1);
        glowSkyMat = SkyQuad("Moon", new Vector3(60f, 80f, 340f), 24f, 24f, new Color(1f, 0.9f, 0.8f, 1f), Sprite(128, 0.55f), 1f);
        SkyQuad("Glow", new Vector3(0f, 22f, 350f), 260f, 50f, new Color(0.9f, 0.2f, 0.5f, 0.5f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Sparks", 80, sphere, new[] { G(1), G(3) }, Vector3.one * 0.07f, new Vector2(-0.3f, 0.3f));
    }

    // ---------- Kart race ----------

    private void BuildKarts()
    {
        Color red = new Color(1f, 0.15f, 0.15f), yellow = new Color(1f, 0.85f, 0.1f), green = new Color(0.2f, 1f, 0.35f), blue = new Color(0.2f, 0.6f, 1f), white = new Color(1f, 1f, 1f);
        Group(red); Group(yellow); Group(green); Group(blue); Group(white);
        var root = Root("Karts");
        Ground(root, Lit(new Color(0.12f, 0.2f, 0.08f), 0.3f, 0f, 0.1f));
        var black = Lit(new Color(0.03f, 0.03f, 0.03f), 0.4f);
        var stand = Lit(new Color(0.25f, 0.27f, 0.3f), 0.4f, 0.3f, 0.06f);
        // Red-and-white kerbs right by the road; tyre stacks and cones.
        for (float z = -Behind; z < Far; z += 2f)
            foreach (int side in new[] { -1, 1 })
            {
                var k = Prop(root, "Kerb", new Vector3(side * 7.1f, -0.48f, z + 1f));
                Place("Block", cube, ((int)(z / 2f) % 2 == 0) ? G(0) : Lit(new Color(0.85f, 0.85f, 0.85f), 0.4f, 0f, 0.25f), k, Vector3.zero, new Vector3(0.6f, 0.1f, 2f));
                scrollers.Add(new Mover { t = k });
            }
        for (float z = -Behind; z < Far; z += 6f)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Trackside", new Vector3(side * 8.3f, -0.5f, z + (side > 0 ? 3f : 0f)));
                if (((int)(z / 6f) + (side > 0 ? 1 : 0)) % 2 == 0)
                    for (int s = 0; s < 3; s++)
                        for (int h = 0; h < 3; h++)
                            Place("Tyre", cyl, h == 2 ? G(s) : black, p, new Vector3(0f, 0.2f + h * 0.4f, -0.9f + s * 0.9f), new Vector3(0.85f, 0.2f, 0.85f));
                else
                    for (int c = 0; c < 3; c++) Kit("cone", p, new Vector3(Rand(-0.6f, 0.6f), 0f, -1f + c), 0.9f, 0f, Color.white, 0.6f);
                scrollers.Add(new Mover { t = p });
            }
        // Grandstands with a flashing crowd.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var g = Prop(root, "Grandstand", new Vector3(side * 16f, -0.5f, z + 6f));
                for (int row = 0; row < 5; row++)
                {
                    Place("Row", cube, stand, g, new Vector3(side * row * 0.9f, 0.5f + row * 0.8f, 0f), new Vector3(0.9f, 1f + row * 1.6f, 11.8f));
                    for (int k = 0; k < 8; k++)
                        flashers.Add(Place("Fan", sphere, G(k + row), g, new Vector3(side * row * 0.9f - side * 0.2f, 1.3f + row * 0.8f + row * 0.8f, -5f + k * 1.4f), Vector3.one * 0.3f));
                }
                Place("Roof", cube, G(1), g, new Vector3(side * 2f, 9f, 0f), new Vector3(5f, 0.2f, 12f));
                scrollers.Add(new Mover { t = g });
            }
        // Karts and race cars on side tracks.
        string[] karts = { "kart-oobi", "kart-oodi", "kart-ooli", "kart-oopi", "kart-oozi", "race", "race-future" };
        int ci = 0;
        foreach (int side in new[] { -1, 1 })
            for (int k = 0; k < 4; k++, ci++)
                Vehicle(root, karts[ci % karts.Length], side * Rand(10.5f, 12f), Rand(0.3f, 0.7f), 1.1f, Color.white, 0.55f);
        // Chequered flags across the road, fluttering.
        for (float z = -Behind + 4f; z < Far; z += 12f)
            Garland(root, z, 10f, 9f, stand, black, 10, (parent, k, p) =>
            {
                var flag = Prop(parent, "Flag", p);
                for (int a = 0; a < 2; a++)
                    for (int b = 0; b < 2; b++)
                        Place("Check", cube, (a + b + k) % 2 == 0 ? Lit(new Color(0.9f, 0.9f, 0.9f), 0.4f, 0f, 0.4f) : black, flag, new Vector3(-0.25f + a * 0.5f, -0.35f - b * 0.5f, 0f), new Vector3(0.5f, 0.5f, 0.02f));
                floaters.Add(new Mover { t = flag, seed = Rand(0f, 6.28f) + k * 0.4f, group = -10 });
            });
        Fireworks(Root("Fireworks"), 1, 3);
        glowSkyMat = SkyQuad("Sun", new Vector3(-50f, 30f, 345f), 55f, 55f, new Color(1f, 0.6f, 0.3f, 1f), Sprite(128, 0.45f), 1.1f);
        SkyQuad("Dusk", new Vector3(0f, 25f, 350f), 260f, 55f, new Color(1f, 0.4f, 0.3f, 0.5f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Confetti", 160, cube, new[] { G(0), G(1), G(2), G(3), G(4) }, new Vector3(0.14f, 0.02f, 0.09f), new Vector2(0.7f, 1.6f));
    }

    // ---------- Camp night ----------

    private void BuildCamp()
    {
        Color fire = new Color(1f, 0.5f, 0.1f), shroom = new Color(0.2f, 1f, 0.8f), aurora = new Color(0.3f, 1f, 0.5f), violet = new Color(0.65f, 0.35f, 1f);
        Group(fire); Group(shroom); Group(aurora); Group(violet);
        var root = Root("Camp");
        Ground(root, Lit(new Color(0.04f, 0.06f, 0.04f), 0.3f));
        var water = Lit(new Color(0.02f, 0.05f, 0.09f), 0.95f, 0.2f, 0.1f);
        string[] pines = { "tree_pineTallA", "tree_pineRoundA", "tree_pineDefaultA" };
        // The pine forest, dense, two rows deep.
        for (float z = -Behind; z < Far; z += 3.5f)
            foreach (int side in new[] { -1, 1 })
                for (int row = 0; row < 2; row++)
                {
                    var t = Prop(root, "Pine", new Vector3(side * (row == 0 ? Rand(9.5f, 11f) : Rand(14f, 18f)), -0.5f, z + Rand(0f, 3f)));
                    Kit(pines[rng.Next(pines.Length)], t, Vector3.zero, Rand(7f, 12f), Rand(0f, 360f), new Color(0.4f, 0.75f, 0.55f), 0.12f);
                    scrollers.Add(new Mover { t = t });
                }
        // Tents with a campfire in front, log stacks; glowing mushrooms along the path.
        int n = 0;
        for (float z = -Behind; z < Far; z += 8f, n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var c = Prop(root, "Camp", new Vector3(side * 8.4f, -0.5f, z));
            Kit(n % 3 == 0 ? "tent_smallClosed" : "tent_detailedOpen", c, new Vector3(side * 0.8f, 0f, 0f), 2.4f, side * -90f, new Color(1f, 0.85f, 0.7f), 0.3f);
            Kit("campfire_stones", c, new Vector3(-side * 1.3f, 0f, 1.2f), 0.5f, 0f, Color.white, 0.4f);
            Kit("campfire_logs", c, new Vector3(-side * 1.3f, 0f, 1.2f), 0.6f, 0f, Color.white, 0.4f);
            var fl = Place("Flame", sphere, G(0), c, new Vector3(-side * 1.3f, 0.7f, 1.2f), new Vector3(0.4f, 0.6f, 0.4f));
            floaters.Add(new Mover { t = fl.transform, seed = Rand(0f, 6.28f), group = -5, size = 0.4f });
            if (n % 2 == 1) Kit("log_stack", c, new Vector3(side * 0.6f, 0f, -2.2f), 0.9f, 0f, Color.white, 0.3f);
            scrollers.Add(new Mover { t = c });
        }
        string[] shrooms = { "mushroom_red", "mushroom_redTall", "mushroom_tanTall" };
        for (float z = -Behind; z < Far; z += 2.4f)
        {
            int side = Side();
            var m = Kit(shrooms[rng.Next(shrooms.Length)], root, new Vector3(side * Rand(7.2f, 7.8f), -0.5f, z), Rand(0.6f, 1.2f), Rand(0f, 360f), new Color(0.6f, 1f, 1f), 1.1f);
            if (m != null) scrollers.Add(new Mover { t = m });
        }
        // A lake on the right with canoes bobbing.
        for (float z = -Behind; z < Far; z += 12f)
        {
            var w = Prop(root, "Lake", new Vector3(22f, -0.8f, z + 6f));
            Place("Water", cube, water, w, Vector3.zero, new Vector3(14f, 0.05f, 12f));
            scrollers.Add(new Mover { t = w });
        }
        for (float z = -Behind; z < Far; z += 15f)
        {
            var b = Prop(root, "Canoe", new Vector3(Rand(19f, 24f), -0.7f, z));
            Kit("canoe", b, Vector3.zero, 0.6f, Rand(0f, 360f), new Color(1f, 0.7f, 0.5f), 0.4f);
            Bounce(b, 0.15f);
            scrollers.Add(new Mover { t = b });
        }
        var wave = Mask(256, 64, (x, y) =>
        {
            float cc = 0.5f + 0.25f * Mathf.Sin(x * 12f) + 0.1f * Mathf.Sin(x * 31f);
            return Mathf.Clamp01(1f - Mathf.Abs(y - cc) * 4f) * (y < cc ? 1f : 0.6f);
        });
        for (int i = 0; i < 3; i++)
            SkyQuad("Aurora", new Vector3(Rand(-100f, 100f), 75f + i * 14f, 335f), 180f, 40f, i % 2 == 0 ? new Color(0.3f, 1f, 0.6f, 0.7f) : new Color(0.6f, 0.4f, 1f, 0.6f), wave, 0.7f, Rand(-6f, 6f));
        glowSkyMat = SkyQuad("Moon", new Vector3(-90f, 90f, 340f), 22f, 22f, new Color(0.9f, 0.95f, 1f, 1f), Sprite(128, 0.55f), 1f);
        Fireworks(Root("Fireworks"), 2, 3);
        ParticleBox("Fireflies", 140, sphere, new[] { G(0), G(2) }, Vector3.one * 0.08f, new Vector2(-0.3f, 0.3f));
    }

    // ---------- Beach party ----------

    private void BuildBeach()
    {
        Color coral = new Color(1f, 0.45f, 0.35f), turq = new Color(0.1f, 0.95f, 0.9f), sun = new Color(1f, 0.8f, 0.2f), pink = new Color(1f, 0.3f, 0.7f), blue = new Color(0.25f, 0.55f, 1f);
        Group(coral); Group(turq); Group(sun); Group(pink); Group(blue);
        var root = Root("Beach");
        Ground(root, Lit(new Color(0.45f, 0.36f, 0.24f), 0.2f, 0f, 0.08f), 11f, 9f);
        var sea = Lit(new Color(0.02f, 0.12f, 0.2f), 0.95f, 0.1f, 0.15f);
        var white = Lit(new Color(0.9f, 0.9f, 0.9f), 0.4f, 0f, 0.2f);
        var wood = Lit(new Color(0.35f, 0.22f, 0.12f), 0.3f);
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var w = Prop(root, "Sea", new Vector3(side * 25f, -0.75f, z + 6f));
                Place("Water", cube, sea, w, Vector3.zero, new Vector3(18f, 0.05f, 12f));
                Place("Surf", cube, G(1), w, new Vector3(-side * 8.6f, 0.04f, 0f), new Vector3(0.4f, 0.04f, 12f));
                scrollers.Add(new Mover { t = w });
            }
        // Umbrellas with towels, palms, kiosks.
        int n = 0;
        for (float z = -Behind; z < Far; z += 5f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Beach", new Vector3(side * Rand(8f, 9.5f), -0.5f, z + (side > 0 ? 2.5f : 0f)));
                switch ((n + (side > 0 ? 1 : 0)) % 3)
                {
                    case 0:
                        RemixGeo.Beam(p, "Pole", cyl, white, Vector3.zero, new Vector3(0f, 2.6f, 0f), 0.08f);
                        for (int k = 0; k < 8; k++)
                            Place("Canopy", cube, k % 2 == 0 ? G(n % 5) : white, p, new Vector3(0f, 2.55f, 0f), new Vector3(0.9f, 0.08f, 2.6f), new Vector3(0f, k * 22.5f, 12f));
                        Place("Towel", cube, G((n + 2) % 5), p, new Vector3(-side * 1.1f, -0.47f + 0.5f, 0.5f), new Vector3(0.9f, 0.02f, 1.8f));
                        break;
                    case 1:
                        Kit(rng.NextDouble() < 0.5 ? "tree_palmTall" : "tree_palmDetailedTall", p, Vector3.zero, Rand(7f, 9f), Rand(0f, 360f), new Color(0.55f, 0.85f, 0.6f), 0.12f);
                        Kit("coconut", p, new Vector3(0.6f, 0f, 0.4f), 0.4f, 0f, Color.white, 0.4f);
                        break;
                    default:
                        Place("Table", cyl, white, p, new Vector3(0f, 0.9f, 0f), new Vector3(1.1f, 0.05f, 1.1f));
                        RemixGeo.Beam(p, "Leg", cyl, white, Vector3.zero, new Vector3(0f, 0.9f, 0f), 0.1f);
                        Kit("cocktail", p, new Vector3(0f, 0.95f, -0.2f), 0.6f, 0f, Color.white, 0.7f);
                        Kit("soda-glass", p, new Vector3(0.2f, 0.95f, 0.25f), 0.45f, 0f, Color.white, 0.6f);
                        break;
                }
                scrollers.Add(new Mover { t = p });
            }
        // Beach balls bouncing high on the kick; a lifeguard tower now and then.
        for (float z = -Behind; z < Far; z += 6f)
        {
            int side = Side();
            var b = Prop(root, "Ball", new Vector3(side * Rand(10.5f, 12.5f), Rand(0.3f, 1.2f), z));
            for (int k = 0; k < 3; k++)
                Place("Panel", sphere, G(k * 2), b, Vector3.zero, new Vector3(0.9f - k * 0.02f, 0.9f, 0.9f - k * 0.02f), new Vector3(0f, k * 60f, 0f));
            Bounce(b, 1.6f);
            scrollers.Add(new Mover { t = b });
        }
        for (float z = -Behind + 10f; z < Far; z += 26f)
        {
            int side = Side();
            var t = Prop(root, "Lifeguard", new Vector3(side * 13f, -0.5f, z));
            foreach (var l in new[] { new Vector3(-0.8f, 0f, -0.8f), new Vector3(0.8f, 0f, -0.8f), new Vector3(-0.8f, 0f, 0.8f), new Vector3(0.8f, 0f, 0.8f) })
                RemixGeo.Beam(t, "Leg", cube, white, l, new Vector3(l.x * 0.6f, 3f, l.z * 0.6f), 0.15f);
            Place("Hut", cube, G(0), t, new Vector3(0f, 3.8f, 0f), new Vector3(1.8f, 1.5f, 1.8f));
            Place("Roof", cube, white, t, new Vector3(0f, 4.7f, 0f), new Vector3(2.3f, 0.2f, 2.3f));
            pulsers.Add(new Mover { t = Place("Flag", cube, G(2), t, new Vector3(0f, 5.6f, 0.3f), new Vector3(0.05f, 0.6f, 0.9f)).transform, group = 2, size = -1f });
            scrollers.Add(new Mover { t = t });
        }
        Fireworks(Root("Fireworks"), 0, 1);
        glowSkyMat = SkyQuad("Sunset", new Vector3(0f, 18f, 345f), 75f, 75f, new Color(1f, 0.5f, 0.25f, 1f), Sprite(128, 0.45f), 1.15f);
        SkyQuad("Dusk", new Vector3(0f, 30f, 350f), 260f, 60f, new Color(1f, 0.35f, 0.5f, 0.55f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Bubbles", 90, sphere, new[] { G(1), G(4) }, Vector3.one * 0.1f, new Vector2(-0.6f, -0.2f));
    }

    // ---------- Fruit paradise ----------

    private void BuildFruit()
    {
        Color lime = new Color(0.5f, 1f, 0.2f), orange = new Color(1f, 0.55f, 0.1f), berry = new Color(1f, 0.15f, 0.45f), grape = new Color(0.65f, 0.3f, 1f), lemon = new Color(1f, 0.95f, 0.2f);
        Group(lime); Group(orange); Group(berry); Group(grape); Group(lemon);
        var root = Root("Fruit");
        Ground(root, Lit(new Color(0.08f, 0.2f, 0.06f), 0.3f, 0f, 0.1f));
        string[] fruit = { "watermelon", "pineapple", "banana", "cherries", "strawberry", "grapes", "lemon", "apple", "orange", "coconut" };
        int n = 0;
        for (float z = -Behind; z < Far; z += 4f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Fruit", new Vector3(side * Rand(8f, 10f), -0.5f, z + (side > 0 ? 2f : 0f)));
                var m = Kit(fruit[(n * 3 + (side > 0 ? 1 : 0)) % fruit.Length], p, Vector3.zero, Rand(2f, 3.4f), Rand(0f, 360f), Color.white, 0.6f);
                Bounce(m, Rand(0.4f, 0.8f));
                scrollers.Add(new Mover { t = p });
            }
        // Smoothie cups and juice glasses on tall stands further out; bushes with flowers.
        string[] cups = { "frappe", "soda-glass", "cocktail", "whipped-cream" };
        for (float z = -Behind; z < Far; z += 9f)
            foreach (int side in new[] { -1, 1 })
            {
                var s = Prop(root, "Stand", new Vector3(side * 13f, -0.5f, z + (side > 0 ? 4.5f : 0f)));
                RemixGeo.Beam(s, "Post", cyl, G(n++ % 5), Vector3.zero, new Vector3(0f, 3f, 0f), 0.4f);
                var spin = Prop(s, "Spin", new Vector3(0f, 3.05f, 0f));
                Kit(cups[n % cups.Length], spin, Vector3.zero, 3f, 0f, Color.white, 0.6f);
                floaters.Add(new Mover { t = spin, seed = Rand(0f, 6.28f), speed = 35f, group = -16 });
                Kit("plant_bushLarge", s, new Vector3(side * 1.5f, 0f, 1.5f), 1.4f, Rand(0f, 360f), new Color(0.5f, 1f, 0.5f), 0.3f);
                Kit("flower_redA", s, new Vector3(side * 1.2f, 0f, -1.4f), 1f, 0f, new Color(1f, 0.6f, 0.8f), 0.6f);
                scrollers.Add(new Mover { t = s });
            }
        // Fruit garlands across the road: little fruit hanging from a vine.
        string[] small = { "cherries", "lemon", "strawberry", "apple", "orange" };
        for (float z = -Behind + 3f; z < Far; z += 10f)
            Garland(root, z, 10f, 9f, Lit(new Color(0.2f, 0.35f, 0.1f), 0.3f), G(0), 9, (parent, k, p) =>
            {
                var f = Kit(small[k % small.Length], parent, p + Vector3.down * 0.7f, 0.7f, 0f, Color.white, 0.8f);
                if (f != null) floaters.Add(new Mover { t = f, seed = Rand(0f, 6.28f) + k, group = -10 });
            });
        Fireworks(Root("Fireworks"), 1, 2);
        glowSkyMat = SkyQuad("Sun", new Vector3(30f, 40f, 345f), 55f, 55f, new Color(1f, 0.85f, 0.3f, 1f), Sprite(128, 0.45f), 1.1f);
        SkyQuad("Haze", new Vector3(0f, 25f, 350f), 260f, 55f, new Color(0.4f, 1f, 0.6f, 0.4f), Sprite(128, 1.5f), 0.55f);
        ParticleBox("Juice", 160, sphere, new[] { G(0), G(1), G(2), G(3), G(4) }, Vector3.one * 0.09f, new Vector2(0.6f, 1.4f));
    }

    // ---------- Rescue city ----------

    private void BuildRescue()
    {
        Color red = new Color(1f, 0.12f, 0.1f), blue = new Color(0.15f, 0.45f, 1f), amber = new Color(1f, 0.65f, 0.15f), white = new Color(0.9f, 0.95f, 1f);
        Group(red); Group(blue); Group(amber); Group(white);
        var root = Root("Rescue");
        Ground(root, Lit(new Color(0.05f, 0.05f, 0.07f), 0.85f, 0.2f));
        Material[] walls =
        {
            Lit(new Color(0.12f, 0.13f, 0.18f), 0.4f), Lit(new Color(0.18f, 0.12f, 0.1f), 0.4f), Lit(new Color(0.1f, 0.15f, 0.16f), 0.4f),
        };
        int n = 0;
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; n++)
            {
                float w = Rand(6f, 9f), h = Rand(10f, 22f);
                var b = Prop(root, "Tower", new Vector3(side * 16f, -0.5f, z + w * 0.5f));
                Place("Wall", cube, walls[n % walls.Length], b, new Vector3(0f, h * 0.5f, 0f), new Vector3(6f, h, w - 0.4f));
                for (float y = 1.8f; y < h - 1f; y += 2.4f)
                    for (float wz = -w * 0.5f + 1.2f; wz < w * 0.5f - 0.8f; wz += 1.6f)
                        if (rng.NextDouble() < 0.55)
                            Place("Window", cube, rng.NextDouble() < 0.8 ? G(2) : G(3), b, new Vector3(-side * 3.02f, y, wz), new Vector3(0.03f, 1.1f, 0.9f));
                scrollers.Add(new Mover { t = b });
                z += w;
            }
        // Hydrants, cones and barriers by the kerb; the street lamps.
        for (float z = -Behind; z < Far; z += 6f)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Kerb", new Vector3(side * 8.2f, -0.5f, z + (side > 0 ? 3f : 0f)));
                if (((int)(z / 6f) + (side > 0 ? 1 : 0)) % 2 == 0)
                {
                    Place("Hydrant", cyl, G(0), p, new Vector3(0f, 0.45f, 0f), new Vector3(0.4f, 0.45f, 0.4f));
                    Place("Cap", sphere, G(0), p, new Vector3(0f, 0.95f, 0f), Vector3.one * 0.4f);
                    for (int c = 0; c < 2; c++) Kit("cone", p, new Vector3(-side * 0.8f, 0f, -0.6f + c * 1.2f), 0.8f, 0f, Color.white, 0.6f);
                }
                else
                {
                    Kit("lightpost-double", p, Vector3.zero, 5.5f, side > 0 ? 0f : 180f, new Color(0.6f, 0.6f, 0.7f), 0.2f);
                    Place("Lamp", sphere, G(3), p, new Vector3(0f, 5f, 1.05f), Vector3.one * 0.35f);
                    Place("Lamp", sphere, G(3), p, new Vector3(0f, 5f, -1.05f), Vector3.one * 0.35f);
                }
                scrollers.Add(new Mover { t = p });
            }
        // Emergency vehicles with sirens racing past on both sides.
        string[] trucks = { "firetruck", "ambulance", "police", "firetruck", "police", "ambulance" };
        int ci = 0;
        foreach (int side in new[] { -1, 1 })
            for (int k = 0; k < 3; k++, ci++)
                Vehicle(root, trucks[ci % trucks.Length], side * 10.8f, k == 1 ? 1.7f : 0.45f, trucks[ci % trucks.Length] == "police" ? 1.6f : 2.4f, Color.white, 0.5f, true);
        // Searchlights sweeping the sky from the rooftops.
        for (float z = -Behind; z < Far; z += 20f)
        {
            int side = Side();
            var s = Prop(root, "Searchlight", new Vector3(side * 13f, 12f, z));
            var beam = Prop(s, "Beam", Vector3.zero);
            RemixGeo.Beam(beam, "Light", cube, G(3), Vector3.zero, new Vector3(0f, 18f, 0f), 0.5f);
            searchlights.Add(beam);
            scrollers.Add(new Mover { t = s });
        }
        Fireworks(Root("Fireworks"), 0, 1);
        glowSkyMat = SkyQuad("Moon", new Vector3(70f, 85f, 340f), 22f, 22f, new Color(0.85f, 0.9f, 1f, 1f), Sprite(128, 0.55f), 1f);
        SkyQuad("Glow", new Vector3(0f, 22f, 350f), 260f, 50f, new Color(0.2f, 0.35f, 1f, 0.5f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Embers", 100, sphere, new[] { G(0), G(2) }, Vector3.one * 0.07f, new Vector2(-0.6f, -0.2f));
    }
}
