using UnityEngine;

/// Video 25's five worlds on FiestaEnvironment's machinery: Coral Reef, Sunset Canyon, Sakura Night,
/// El Dorado, Sky Kingdom. Kenney CC0 models where the project has them (rocks, cliffs, cacti,
/// trees, statues, lanterns), primitives with a glow for the rest (corals, fish, jellyfish, kelp,
/// balloons, pyramids, islands, clouds, rainbows, waterfalls).
public partial class FiestaEnvironment
{
    private void BuildNeon25()
    {
        switch (skin)
        {
            case FiestaSkin.CoralReef: BuildCoralReef(); break;
            case FiestaSkin.SunsetCanyon: BuildSunsetCanyon(); break;
            case FiestaSkin.SakuraNight: BuildSakuraNight(); break;
            case FiestaSkin.ElDorado: BuildElDorado(); break;
            default: BuildSkyKingdom(); break;
        }
    }

    // ---------- Coral Reef ----------

    private Transform Coral(Transform parent, Vector3 pos, float h, int group)
    {
        var c = Prop(parent, "Coral", pos);
        int branches = 3 + rng.Next(4);
        for (int i = 0; i < branches; i++)
        {
            float a = Rand(0f, 360f) * Mathf.Deg2Rad, tilt = Rand(0.1f, 0.6f);
            Vector3 top = new Vector3(Mathf.Cos(a) * tilt * h * 0.5f, h * Rand(0.6f, 1f), Mathf.Sin(a) * tilt * h * 0.5f);
            RemixGeo.Beam(c, "Branch", cyl, G(group), Vector3.zero, top, h * 0.09f);
            Place("Tip", sphere, G(group + 1), c, top, Vector3.one * h * 0.16f);
        }
        return c;
    }

    private void BuildCoralReef()
    {
        Color coral = new Color(1f, 0.35f, 0.5f), aqua = new Color(0.1f, 1f, 0.9f), sun = new Color(1f, 0.75f, 0.2f), violet = new Color(0.6f, 0.35f, 1f);
        Group(coral); Group(aqua); Group(sun); Group(violet);
        var root = Root("Reef");
        Ground(root, Lit(new Color(0.55f, 0.48f, 0.35f), 0.2f, 0f, 0.12f), 14f, 16f);
        var rock = Lit(new Color(0.12f, 0.16f, 0.22f), 0.3f);
        var kelp = Lit(new Color(0.1f, 0.45f, 0.25f), 0.3f, 0f, 0.35f);
        // Rocks and coral gardens along both sides, pulsing on their kick.
        int n = 0;
        for (float z = -Behind; z < Far; z += 2.6f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var r = Prop(root, "Rock", new Vector3(side * Rand(9f, 13f), -0.6f, z + Rand(0f, 2f)));
                Place("Stone", rockMesh, rock, r, Vector3.zero, new Vector3(Rand(1.5f, 3f), Rand(1f, 2.5f), Rand(1.5f, 3f)), new Vector3(0f, Rand(0f, 360f), 0f));
                var c = Coral(r, new Vector3(0f, Rand(0.6f, 1.4f), 0f), Rand(1.2f, 2.6f), (n + (side > 0 ? 1 : 0)) % 4);
                pulsers.Add(new Mover { t = c, group = n % 4, size = 1f });
                scrollers.Add(new Mover { t = r });
            }
        // Kelp swaying in tall strands further out.
        for (float z = -Behind; z < Far; z += 1.8f)
        {
            var k = Prop(root, "Kelp", new Vector3(Side() * Rand(14f, 22f), -0.6f, z));
            var sway = Prop(k, "Sway", Vector3.zero);
            float h = Rand(5f, 11f);
            for (int s = 0; s < 4; s++)
                RemixGeo.Beam(sway, "Leaf", cyl, kelp, new Vector3(Mathf.Sin(s) * 0.3f, h * s / 4f, 0f), new Vector3(Mathf.Sin(s + 1) * 0.3f, h * (s + 1) / 4f, 0f), 0.22f);
            floaters.Add(new Mover { t = sway, seed = Rand(0f, 6f), group = -12 });
            scrollers.Add(new Mover { t = k });
        }
        // Fish schools swimming alongside, jellyfish drifting up and down.
        for (float z = -Behind; z < Far; z += 4f)
        {
            var school = Prop(root, "School", new Vector3(Side() * Rand(7.5f, 16f), Rand(1f, 7f), z));
            int g = rng.Next(4);
            for (int f = 0; f < 5; f++)
            {
                var fish = Prop(school, "Fish", new Vector3(Rand(-1f, 1f), Rand(-0.6f, 0.6f), Rand(-1.2f, 1.2f)));
                Place("Body", sphere, G(g), fish, Vector3.zero, new Vector3(0.22f, 0.3f, 0.6f));
                Place("Tail", cube, G(g + 2), fish, new Vector3(0f, 0f, -0.38f), new Vector3(0.04f, 0.3f, 0.2f));
            }
            riders.Add(new Mover { t = school, speed = Rand(0.6f, 0.85f) });
        }
        var jelly = Neon(new Color(1f, 0.5f, 0.95f));
        for (float z = -Behind; z < Far; z += 7f)
        {
            var j = Prop(root, "Jelly", new Vector3(Side() * Rand(6.5f, 12f), Rand(3f, 8f), z));
            Place("Bell", sphere, jelly, j, Vector3.zero, new Vector3(0.9f, 0.55f, 0.9f));
            for (int t = 0; t < 4; t++) RemixGeo.Beam(j, "Tentacle", cyl, jelly, new Vector3((t - 1.5f) * 0.2f, -0.1f, 0f), new Vector3((t - 1.5f) * 0.25f, -1.4f, 0f), 0.04f);
            Bounce(j, 0.9f);
            scrollers.Add(new Mover { t = j });
        }
        // Sun rays from the surface far above, and the surface's glow.
        var ray = Mask(64, 256, (x, y) => Mathf.Pow(1f - Mathf.Abs(x * 2f - 1f), 2f) * y);
        for (int i = 0; i < 7; i++)
            SkyQuad("SunRay", new Vector3(Rand(-120f, 120f), 60f, 340f), Rand(6f, 14f), 110f, new Color(0.6f, 1f, 0.95f, 0.45f), ray, 0.6f, Rand(-15f, 15f));
        glowSkyMat = SkyQuad("Surface", new Vector3(0f, 95f, 350f), 320f, 50f, new Color(0.3f, 0.9f, 1f, 0.8f), Sprite(128, 1.4f), 0.8f);
        ParticleBox("Bubbles", 170, sphere, new[] { Lit(new Color(0.8f, 1f, 1f), 0.9f, 0f, 0.8f) }, Vector3.one * 0.09f, new Vector2(-1.4f, -0.6f));
    }

    // ---------- Sunset Canyon ----------

    private void BuildSunsetCanyon()
    {
        Color orange = new Color(1f, 0.5f, 0.15f), pink = new Color(1f, 0.3f, 0.55f), gold = new Color(1f, 0.8f, 0.3f), teal = new Color(0.1f, 0.9f, 0.85f);
        Group(orange); Group(pink); Group(gold); Group(teal);
        var root = Root("Canyon");
        Ground(root, Lit(new Color(0.42f, 0.2f, 0.12f), 0.2f, 0f, 0.05f), 14f, 16f);
        Color rockTint = new Color(1f, 0.55f, 0.4f);
        // Red mesas: tall cliffs stacked into walls, further out the big buttes.
        string[] cliffs = { "cliff_large_rock", "cliff_large_stone", "rock_tallA" };
        for (float z = -Behind; z < Far; z += 5f)
            foreach (int side in new[] { -1, 1 })
            {
                var c = Kit(cliffs[rng.Next(cliffs.Length)], root, new Vector3(side * Rand(15f, 19f), -0.6f, z), Rand(8f, 15f), Rand(0f, 360f), rockTint, 0.12f);
                if (c != null) scrollers.Add(new Mover { t = c });
            }
        var butte = Lit(new Color(0.45f, 0.18f, 0.12f), 0.2f, 0f, 0.1f);
        for (float z = -Behind; z < Far; z += 18f)
        {
            var b = Prop(root, "Butte", new Vector3(Side() * Rand(26f, 34f), -0.6f, z));
            Place("Body", cube, butte, b, new Vector3(0f, 9f, 0f), new Vector3(Rand(8f, 14f), 18f, Rand(8f, 12f)));
            Place("Cap", cube, butte, b, new Vector3(0f, 18.5f, 0f), new Vector3(Rand(10f, 15f), 1.2f, Rand(10f, 13f)));
            scrollers.Add(new Mover { t = b });
        }
        // Cacti along the road with a glowing flower on top, tumbleweed-like bushes.
        int n = 0;
        for (float z = -Behind; z < Far; z += 3f, n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var c = Prop(root, "Cactus", new Vector3(side * Rand(7.5f, 10.5f), -0.5f, z));
            Kit(rng.NextDouble() < 0.6 ? "cactus_tall" : "cactus_short", c, Vector3.zero, Rand(2f, 4f), Rand(0f, 360f), new Color(0.6f, 1f, 0.6f), 0.25f);
            var f = Place("Bloom", sphere, G(n % 4), c, new Vector3(0f, 0.5f, 0f), Vector3.one * 0.35f);
            pulsers.Add(new Mover { t = f.transform, group = n % 4, size = 0.35f });
            scrollers.Add(new Mover { t = c });
        }
        // Hot-air balloons drifting up over the canyon.
        var basket = Lit(new Color(0.35f, 0.22f, 0.12f), 0.3f);
        for (float z = 10f; z < Far; z += 14f)
        {
            var b = Prop(root, "Balloon", new Vector3(Side() * Rand(10f, 24f), Rand(8f, 16f), z));
            int g = rng.Next(4);
            for (int k = 0; k < 6; k++)
                Place("Panel", sphere, k % 2 == 0 ? G(g) : G(g + 1), b, Vector3.zero, new Vector3(2.6f - k * 0.02f, 3.2f, 2.6f - k * 0.02f), new Vector3(0f, k * 30f, 0f));
            Place("Basket", cube, basket, b, new Vector3(0f, -2.6f, 0f), new Vector3(0.8f, 0.6f, 0.8f));
            Place("Burner", sphere, G(2), b, new Vector3(0f, -1.9f, 0f), Vector3.one * 0.3f);
            Bounce(b, 1.2f);
            scrollers.Add(new Mover { t = b });
        }
        glowSkyMat = SkyQuad("Sun", new Vector3(0f, 14f, 345f), 70f, 70f, new Color(1f, 0.55f, 0.2f, 1f), Sprite(128, 0.45f), 1.25f);
        SkyQuad("Dusk", new Vector3(0f, 26f, 350f), 300f, 60f, new Color(1f, 0.35f, 0.4f, 0.6f), Sprite(128, 1.5f), 0.7f);
        SkyQuad("High", new Vector3(0f, 80f, 352f), 320f, 70f, new Color(0.55f, 0.25f, 0.75f, 0.5f), Sprite(128, 1.5f), 0.5f);
        Fireworks(Root("Fireworks"), 0, 1);
        ParticleBox("Dust", 90, sphere, new[] { G(2) }, Vector3.one * 0.06f, new Vector2(-0.2f, 0.2f));
    }

    // ---------- Sakura Night ----------

    private void BuildSakuraNight()
    {
        Color sakura = new Color(1f, 0.45f, 0.75f), lantern = new Color(1f, 0.55f, 0.15f), moon = new Color(0.8f, 0.9f, 1f), violet = new Color(0.6f, 0.35f, 1f);
        Group(sakura); Group(lantern); Group(moon); Group(violet);
        var root = Root("Sakura");
        Ground(root, Lit(new Color(0.06f, 0.05f, 0.09f), 0.4f), 12f, 13f);
        var wood = Lit(new Color(0.18f, 0.06f, 0.05f), 0.4f);
        var water = Lit(new Color(0.02f, 0.03f, 0.08f), 0.9f, 0.2f, 0.2f);
        // Cherry trees in bloom, two rows deep, glowing pink.
        string[] trees = { "tree_oak", "tree_default", "tree_detailed", "tree_fat" };
        for (float z = -Behind; z < Far; z += 3.2f)
            foreach (int side in new[] { -1, 1 })
                for (int row = 0; row < 2; row++)
                {
                    var t = Prop(root, "Cherry", new Vector3(side * (row == 0 ? Rand(8.5f, 10f) : Rand(13f, 17f)), -0.5f, z + Rand(0f, 2.5f)));
                    var m = Kit(trees[rng.Next(trees.Length)], t, Vector3.zero, Rand(5f, 8f), Rand(0f, 360f), new Color(1f, 0.4f, 0.7f), 0.28f);
                    if (m == null) Place("Crown", sphere, G(0), t, new Vector3(0f, 4f, 0f), Vector3.one * Rand(3f, 4.5f));
                    scrollers.Add(new Mover { t = t });
                }
        // Stone lanterns along the path, paper lanterns on cords across it.
        var stone = Lit(new Color(0.35f, 0.35f, 0.38f), 0.3f);
        int n = 0;
        for (float z = -Behind; z < Far; z += 4f, n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var l = Prop(root, "StoneLantern", new Vector3(side * 7.2f, -0.5f, z));
            RemixGeo.Beam(l, "Post", cube, stone, Vector3.zero, new Vector3(0f, 1.1f, 0f), 0.3f);
            Place("Light", cube, G(1), l, new Vector3(0f, 1.35f, 0f), new Vector3(0.5f, 0.4f, 0.5f));
            Place("Roof", cube, stone, l, new Vector3(0f, 1.65f, 0f), new Vector3(0.9f, 0.15f, 0.9f), new Vector3(0f, 45f, 0f));
            scrollers.Add(new Mover { t = l });
        }
        for (float z = -Behind; z < Far; z += 11f)
            Garland(root, z, 7.5f, 9.5f, wood, wood, 7, (pt, k, pos) =>
            {
                var lamp = Place("PaperLantern", sphere, G(k % 2 == 0 ? 1 : 0), pt, pos + new Vector3(0f, -0.6f, 0f), new Vector3(0.6f, 0.8f, 0.6f));
                pulsers.Add(new Mover { t = lamp.transform, group = k % 2 == 0 ? 1 : 0, size = -1f });
            });
        // A still pond on the right reflecting it all, a pagoda far behind.
        for (float z = -Behind; z < Far; z += 12f)
        {
            var w = Prop(root, "Pond", new Vector3(24f, -0.75f, z + 6f));
            Place("Water", cube, water, w, Vector3.zero, new Vector3(16f, 0.05f, 12f));
            scrollers.Add(new Mover { t = w });
        }
        var pag = Prop(Root("Pagoda"), "Pagoda", new Vector3(-60f, -1f, 260f));
        for (int k = 0; k < 5; k++)
        {
            float y = k * 9f, wdt = 22f - k * 3.5f;
            Place("Floor", cube, wood, pag, new Vector3(0f, y + 3f, 0f), new Vector3(wdt * 0.7f, 6f, wdt * 0.7f));
            Place("Eave", cube, G(1), pag, new Vector3(0f, y + 6.3f, 0f), new Vector3(wdt, 0.6f, wdt));
        }
        glowSkyMat = SkyQuad("Moon", new Vector3(80f, 90f, 340f), 30f, 30f, new Color(0.95f, 0.95f, 1f, 1f), Sprite(128, 0.55f), 1.05f);
        SkyQuad("Haze", new Vector3(0f, 20f, 348f), 300f, 45f, new Color(0.55f, 0.25f, 0.8f, 0.5f), Sprite(128, 1.5f), 0.55f);
        Fireworks(Root("Fireworks"), 0, 3);
        ParticleBox("Petals", 260, cube, new[] { G(0) }, new Vector3(0.14f, 0.02f, 0.1f), new Vector2(0.4f, 1.1f));
    }

    // ---------- El Dorado ----------

    private void Pyramid(Transform parent, Vector3 pos, float baseW, int steps, Material stone, Material glow)
    {
        var p = Prop(parent, "Pyramid", pos);
        for (int k = 0; k < steps; k++)
        {
            float w = baseW * (1f - k / (float)(steps + 1));
            Place("Step", cube, stone, p, new Vector3(0f, k * 2f + 1f, 0f), new Vector3(w, 2f, w));
            Place("Trim", cube, glow, p, new Vector3(0f, k * 2f + 2.02f, 0f), new Vector3(w + 0.1f, 0.12f, w + 0.1f));
        }
        Place("Temple", cube, glow, p, new Vector3(0f, steps * 2f + 1.5f, 0f), new Vector3(baseW * 0.18f, 3f, baseW * 0.18f));
        scrollers.Add(new Mover { t = p });
    }

    private void BuildElDorado()
    {
        Color gold = new Color(1f, 0.75f, 0.15f), jade = new Color(0.2f, 1f, 0.6f), fire = new Color(1f, 0.4f, 0.08f), ruby = new Color(1f, 0.15f, 0.3f);
        Group(gold); Group(jade); Group(fire); Group(ruby);
        var root = Root("ElDorado");
        Ground(root, Lit(new Color(0.18f, 0.13f, 0.06f), 0.4f, 0.3f), 12f, 13f);
        var stone = Lit(new Color(0.55f, 0.4f, 0.15f), 0.5f, 0.6f, 0.15f);
        Color goldTint = new Color(1f, 0.8f, 0.35f);
        // Golden pyramids out on both sides, the jungle at their feet.
        for (float z = -Behind; z < Far; z += 26f)
            foreach (int side in new[] { -1, 1 })
                Pyramid(root, new Vector3(side * Rand(22f, 30f), -0.6f, z + (side > 0 ? 13f : 0f)), Rand(14f, 20f), 5 + rng.Next(3), stone, G(1));
        // Statues, columns and obelisks along the road, torches burning between them.
        string[] statues = { "statue_head", "statue_column", "statue_obelisk", "statue_block", "statue_ring" };
        int n = 0;
        for (float z = -Behind; z < Far; z += 3.5f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var s = Prop(root, "Statue", new Vector3(side * Rand(8.5f, 11f), -0.5f, z + (side > 0 ? 1.7f : 0f)));
                Kit(statues[rng.Next(statues.Length)], s, Vector3.zero, Rand(2.5f, 5f), side > 0 ? -90f : 90f, goldTint, 0.45f);
                scrollers.Add(new Mover { t = s });
            }
        for (float z = -Behind; z < Far; z += 4f)
            foreach (int side in new[] { -1, 1 })
            {
                var t = Prop(root, "Torch", new Vector3(side * 7.3f, -0.5f, z));
                RemixGeo.Beam(t, "Pole", cyl, stone, Vector3.zero, new Vector3(0f, 2.2f, 0f), 0.12f);
                Place("Bowl", cyl, G(0), t, new Vector3(0f, 2.25f, 0f), new Vector3(0.45f, 0.1f, 0.45f));
                var fl = Place("Flame", sphere, G(2), t, new Vector3(0f, 2.6f, 0f), new Vector3(0.35f, 0.55f, 0.35f));
                floaters.Add(new Mover { t = fl.transform, seed = Rand(0f, 6.28f), group = -5, size = 0.35f });
                scrollers.Add(new Mover { t = t });
            }
        // Treasure: gold coins and gems scattered and spinning, a giant sun disc behind it all.
        for (float z = -Behind; z < Far; z += 2.2f)
        {
            var c = Prop(root, "Treasure", new Vector3(Side() * Rand(11f, 14f), Rand(0f, 1.2f), z));
            bool gem = rng.NextDouble() < 0.35;
            var r = Place(gem ? "Gem" : "Coin", gem ? cube : cyl, gem ? G(rng.NextDouble() < 0.5 ? 1 : 3) : G(0), c, Vector3.zero,
                          gem ? Vector3.one * 0.45f : new Vector3(0.7f, 0.06f, 0.7f), gem ? new Vector3(45f, 0f, 45f) : new Vector3(90f, 0f, 0f));
            floaters.Add(new Mover { t = c, speed = Rand(60f, 140f), seed = Rand(0f, 6f), group = -16 });
            scrollers.Add(new Mover { t = c });
        }
        glowSkyMat = SkyQuad("SunDisc", new Vector3(0f, 30f, 345f), 60f, 60f, new Color(1f, 0.75f, 0.2f, 1f), Sprite(128, 0.35f), 1.2f);
        var rays = Mask(256, 256, (x, y) =>
        {
            float dx = x * 2f - 1f, dy = y * 2f - 1f, a = Mathf.Atan2(dy, dx), d = Mathf.Sqrt(dx * dx + dy * dy);
            return d > 1f ? 0f : Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 8f)), 6f) * (1f - d);
        });
        SkyQuad("SunRays", new Vector3(0f, 30f, 347f), 150f, 150f, new Color(1f, 0.6f, 0.15f, 0.6f), rays, 0.7f);
        SkyQuad("Dusk", new Vector3(0f, 12f, 350f), 300f, 40f, new Color(0.9f, 0.35f, 0.1f, 0.5f), Sprite(128, 1.5f), 0.6f);
        Fireworks(Root("Fireworks"), 0, 2);
        ParticleBox("GoldDust", 160, cube, new[] { G(0) }, Vector3.one * 0.06f, new Vector2(0.2f, 0.7f));
    }

    // ---------- Sky Kingdom ----------

    private void BuildSkyKingdom()
    {
        Color sky = new Color(0.3f, 0.75f, 1f), pink = new Color(1f, 0.45f, 0.8f), gold = new Color(1f, 0.85f, 0.3f), mint = new Color(0.4f, 1f, 0.7f);
        Group(sky); Group(pink); Group(gold); Group(mint);
        var root = Root("Sky");
        var cloud = Lit(new Color(1f, 1f, 1f), 0.2f, 0f, 0.55f);
        var earth = Lit(new Color(0.35f, 0.25f, 0.2f), 0.3f);
        var grass = Lit(new Color(0.3f, 0.75f, 0.35f), 0.3f, 0f, 0.25f);
        var falls = Lit(new Color(0.75f, 0.95f, 1f), 0.6f, 0f, 1.1f);
        // No ground: a sea of clouds under the road.
        for (float z = -Behind; z < Far; z += 3f)
            foreach (int side in new[] { -1, 1 })
            {
                var c = Prop(root, "Cloud", new Vector3(side * Rand(8f, 26f), Rand(-4f, -1.5f), z + Rand(0f, 2f)));
                for (int k = 0; k < 4; k++) Place("Puff", sphere, cloud, c, new Vector3(Rand(-2f, 2f), Rand(-0.3f, 0.5f), Rand(-1.5f, 1.5f)), Vector3.one * Rand(2f, 4f));
                scrollers.Add(new Mover { t = c });
            }
        // Floating islands with trees and a waterfall pouring off, bobbing slowly.
        string[] trees = { "tree_oak", "tree_default", "tree_palmTall", "tree_pineRoundA" };
        for (float z = -Behind; z < Far; z += 9f)
        {
            int side = Side();
            var isl = Prop(root, "Island", new Vector3(side * Rand(12f, 22f), Rand(4f, 12f), z));
            float r = Rand(2.5f, 4.5f);
            Place("Rock", rockMesh, earth, isl, new Vector3(0f, -r * 0.6f, 0f), new Vector3(r * 1.6f, r * 1.4f, r * 1.6f), new Vector3(180f, Rand(0f, 360f), 0f));
            Place("Grass", cyl, grass, isl, Vector3.zero, new Vector3(r * 2f, 0.25f, r * 2f));
            Kit(trees[rng.Next(trees.Length)], isl, new Vector3(Rand(-r * 0.4f, r * 0.4f), 0.1f, 0f), Rand(3f, 5f), Rand(0f, 360f), new Color(0.6f, 1f, 0.7f), 0.3f);
            RemixGeo.Beam(isl, "Waterfall", cube, falls, new Vector3(r * 0.9f, 0f, 0f), new Vector3(r * 0.9f, -14f, 0f), 0.6f);
            Bounce(isl, 0.6f);
            scrollers.Add(new Mover { t = isl });
        }
        // Rainbows arching over the far side, gold stars hanging in the air, pulsing.
        Color[] bands = { new Color(1f, 0.2f, 0.25f), new Color(1f, 0.6f, 0.1f), new Color(1f, 0.95f, 0.2f), new Color(0.3f, 1f, 0.4f), new Color(0.2f, 0.6f, 1f), new Color(0.6f, 0.3f, 1f) };
        for (float z = 20f; z < Far; z += 40f)
        {
            var rb = Prop(root, "Rainbow", new Vector3(Side() * Rand(25f, 35f), -3f, z));
            rb.localRotation = Quaternion.Euler(0f, 90f, 0f);
            for (int b = 0; b < bands.Length; b++)
            {
                var mat = Neon(bands[b]);
                float R = 14f - b * 0.8f;
                for (int s = 0; s < 14; s++)
                {
                    float a0 = Mathf.PI * s / 14f, a1 = Mathf.PI * (s + 1) / 14f;
                    RemixGeo.Beam(rb, "Band", cube, mat, new Vector3(Mathf.Cos(a0) * R, Mathf.Sin(a0) * R, 0f), new Vector3(Mathf.Cos(a1) * R, Mathf.Sin(a1) * R, 0f), 0.75f);
                }
            }
            scrollers.Add(new Mover { t = rb });
        }
        int n = 0;
        for (float z = -Behind; z < Far; z += 3.5f, n++)
        {
            var st = Prop(root, "Star", new Vector3(Side() * Rand(7f, 11f), Rand(4f, 9f), z));
            Place("Star", cube, G(2), st, Vector3.zero, Vector3.one * 0.45f, new Vector3(0f, 0f, 45f));
            Place("Star2", cube, G(2), st, Vector3.zero, Vector3.one * 0.45f);
            pulsers.Add(new Mover { t = st, group = n % 4, size = 1f });
            floaters.Add(new Mover { t = st, speed = 90f, seed = Rand(0f, 6f), group = -16 });
            scrollers.Add(new Mover { t = st });
        }
        glowSkyMat = SkyQuad("Sun", new Vector3(-40f, 60f, 345f), 40f, 40f, new Color(1f, 0.95f, 0.7f, 1f), Sprite(128, 0.45f), 1.1f);
        SkyQuad("Blue", new Vector3(0f, 60f, 352f), 340f, 140f, new Color(0.35f, 0.65f, 1f, 0.75f), Sprite(128, 1.8f), 0.75f);
        SkyQuad("Horizon", new Vector3(0f, 5f, 350f), 320f, 30f, new Color(1f, 0.7f, 0.85f, 0.6f), Sprite(128, 1.5f), 0.6f);
        Fireworks(Root("Fireworks"), 1, 2);
        ParticleBox("Sparkles", 140, cube, new[] { G(2), G(1) }, Vector3.one * 0.07f, new Vector2(-0.3f, 0.3f));
    }
}
