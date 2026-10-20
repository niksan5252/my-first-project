using UnityEngine;

/// Video 24's five new worlds on FiestaEnvironment's machinery, around video 1's neon octagon
/// tunnel: Tropical Lagoon, Crystal Cave, Cyber Factory, Planet Ring, Firefly Jungle. Kenney CC0
/// models where the project has them (palms, lilies, flowers, plants, rocks, the factory kit),
/// primitives with a glow for the rest (flamingos, crystals, planets, lasers, vines).
public partial class FiestaEnvironment
{
    private void BuildNeon24()
    {
        switch (skin)
        {
            case FiestaSkin.Lagoon: BuildLagoon(); break;
            case FiestaSkin.CrystalCave: BuildCrystalCave(); break;
            case FiestaSkin.CyberFactory: BuildCyberFactory(); break;
            case FiestaSkin.PlanetRing: BuildPlanetRing(); break;
            default: BuildFireflyJungle(); break;
        }
    }

    // ---------- Tropical Lagoon ----------

    private void BuildLagoon()
    {
        Color pink = new Color(1f, 0.3f, 0.6f), turq = new Color(0.1f, 1f, 0.85f), sun = new Color(1f, 0.6f, 0.15f), violet = new Color(0.6f, 0.3f, 1f);
        Group(pink); Group(turq); Group(sun); Group(violet);
        var root = Root("Lagoon");
        Ground(root, Lit(new Color(0.11f, 0.08f, 0.07f), 0.3f), 8.5f, 4f);
        var water = Lit(new Color(0.02f, 0.16f, 0.2f), 0.55f, 0f, 0.7f);   // a dark teal glow (a mirror finish read as white)
        var flamingo = Lit(new Color(1f, 0.4f, 0.6f), 0.4f, 0f, 0.9f);
        var beak = Lit(new Color(0.1f, 0.05f, 0.05f), 0.3f);
        // The lagoon on both sides, a glowing surf line along its edge.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var w = Prop(root, "Water", new Vector3(side * 26f, -0.75f, z + 6f));
                Place("Water", cube, water, w, Vector3.zero, new Vector3(36f, 0.05f, 12f));
                Place("Surf", cube, G(1), w, new Vector3(-side * 15.8f, 0.04f, 0f), new Vector3(0.35f, 0.04f, 12f));
                scrollers.Add(new Mover { t = w });
            }
        // Palms on the sand strip, a neon ring round each trunk pulsing on its kick.
        string[] palms = { "tree_palmTall", "tree_palmBend", "tree_palmDetailedTall", "tree_palmShort" };
        int n = 0;
        for (float z = -Behind; z < Far; z += 4.5f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Palm", new Vector3(side * Rand(8.5f, 10f), -0.5f, z + (side > 0 ? 2.2f : 0f)));
                Kit(palms[rng.Next(palms.Length)], p, Vector3.zero, Rand(6.5f, 9.5f), Rand(0f, 360f), new Color(0.5f, 0.95f, 0.75f), 0.18f);
                var ring = Place("TrunkRing", cyl, G(n % 4), p, new Vector3(0f, 0.6f, 0f), new Vector3(0.7f, 0.04f, 0.7f));
                pulsers.Add(new Mover { t = ring.transform, group = n % 4, size = -1f });
                scrollers.Add(new Mover { t = p });
            }
        // Flamingos standing in the shallows, bobbing; glowing lilies on the water.
        for (float z = -Behind; z < Far; z += 5.5f)
        {
            int side = Side();
            var f = Prop(root, "Flamingo", new Vector3(side * Rand(12f, 17f), -0.7f, z));
            f.localRotation = Quaternion.Euler(0f, side > 0 ? -70f : 70f, 0f);
            RemixGeo.Beam(f, "Leg", cyl, flamingo, Vector3.zero, new Vector3(0f, 1.3f, 0f), 0.05f);
            Place("Body", sphere, flamingo, f, new Vector3(0f, 1.5f, 0f), new Vector3(0.55f, 0.45f, 0.9f));
            RemixGeo.Beam(f, "Neck", cyl, flamingo, new Vector3(0f, 1.6f, 0.35f), new Vector3(0f, 2.5f, 0.5f), 0.07f);
            Place("Head", sphere, flamingo, f, new Vector3(0f, 2.55f, 0.58f), Vector3.one * 0.22f);
            Place("Beak", cube, beak, f, new Vector3(0f, 2.5f, 0.75f), new Vector3(0.06f, 0.06f, 0.2f), new Vector3(30f, 0f, 0f));
            Bounce(f, 0.25f);
            scrollers.Add(new Mover { t = f });
        }
        for (float z = -Behind; z < Far; z += 2.8f)
        {
            var l = Kit(rng.NextDouble() < 0.5 ? "lily_large" : "lily_small", root, new Vector3(Side() * Rand(11f, 22f), -0.72f, z), Rand(0.3f, 0.6f), Rand(0f, 360f), new Color(0.6f, 1f, 0.8f), 1.2f);
            if (l != null) scrollers.Add(new Mover { t = l });
        }
        glowSkyMat = SkyQuad("Sunset", new Vector3(0f, 16f, 345f), 80f, 80f, new Color(1f, 0.45f, 0.3f, 1f), Sprite(128, 0.45f), 1.2f);
        SkyQuad("Dusk", new Vector3(0f, 28f, 350f), 280f, 60f, new Color(1f, 0.3f, 0.6f, 0.6f), Sprite(128, 1.5f), 0.65f);
        SkyQuad("Violet", new Vector3(0f, 85f, 352f), 300f, 70f, new Color(0.45f, 0.2f, 0.9f, 0.5f), Sprite(128, 1.5f), 0.5f);
        Fireworks(Root("Fireworks"), 0, 1);
        ParticleBox("Sparks", 110, sphere, new[] { G(0), G(1) }, Vector3.one * 0.08f, new Vector2(-0.4f, 0.2f));
    }

    // ---------- Crystal Cave ----------

    private Transform Crystal(Transform parent, Vector3 pos, float h, int group)
    {
        var c = Prop(parent, "Crystal", pos);
        int k = 3 + rng.Next(3);
        for (int i = 0; i < k; i++)
        {
            float hh = h * Rand(0.45f, 1f);
            float tilt = i == 0 ? 0f : Rand(14f, 38f);
            var piece = Prop(c, "Shard", Vector3.zero);
            piece.localRotation = Quaternion.Euler(tilt, Rand(0f, 360f), 0f);
            // a diamond prism: a box on its edge, pointed by a smaller one on top
            Place("Body", cube, G(group + i), piece, new Vector3(0f, hh * 0.45f, 0f), new Vector3(hh * 0.22f, hh * 0.9f, hh * 0.22f), new Vector3(0f, 45f, 0f));
            Place("Tip", cube, G(group + i), piece, new Vector3(0f, hh * 0.92f, 0f), new Vector3(hh * 0.15f, hh * 0.15f, hh * 0.15f), new Vector3(45f, 45f, 0f));
        }
        return c;
    }

    private void BuildCrystalCave()
    {
        Color cyan = new Color(0.2f, 0.95f, 1f), magenta = new Color(1f, 0.2f, 0.85f), violet = new Color(0.6f, 0.3f, 1f), ice = new Color(0.7f, 0.9f, 1f);
        Group(cyan); Group(magenta); Group(violet); Group(ice);
        var root = Root("Cave");
        var rock = Lit(new Color(0.07f, 0.06f, 0.1f), 0.35f, 0.1f);
        Ground(root, Lit(new Color(0.05f, 0.04f, 0.08f), 0.6f, 0.1f, 0.05f), 13f, 14f);
        // The cave walls: rocks piled high on both sides, and rock arches over the road now and then.
        for (float z = -Behind; z < Far; z += 3f)
            foreach (int side in new[] { -1, 1 })
            {
                var w = Prop(root, "Wall", new Vector3(side * Rand(13f, 16f), Rand(-1f, 4f), z + Rand(0f, 2f)));
                Place("Rock", rockMesh, rock, w, Vector3.zero, new Vector3(Rand(4f, 7f), Rand(8f, 16f), Rand(4f, 6f)), new Vector3(0f, Rand(0f, 360f), 0f));
                scrollers.Add(new Mover { t = w });
            }
        for (float z = -Behind; z < Far; z += 22f)
        {
            var a = Prop(root, "Arch", new Vector3(0f, 0f, z));
            for (int k = 0; k <= 8; k++)
            {
                float ang = Mathf.PI * k / 8f;
                Place("Stone", rockMesh, rock, a, new Vector3(Mathf.Cos(ang) * 12f, 1f + Mathf.Sin(ang) * 11f, 0f), Vector3.one * Rand(3f, 4.5f), new Vector3(Rand(0f, 360f), Rand(0f, 360f), 0f));
            }
            // stalactites hanging from the arch, glowing at the tips
            for (int k = 0; k < 6; k++)
            {
                float x = Rand(-8f, 8f);
                float y = 1f + Mathf.Sqrt(Mathf.Max(0f, 121f - x * x * 0.85f)) - 1f;
                RemixGeo.Beam(a, "Stalactite", cyl, rock, new Vector3(x, y, 0f), new Vector3(x, y - Rand(1.5f, 3f), 0f), Rand(0.15f, 0.3f));
                Place("Drop", sphere, G(k % 4), a, new Vector3(x, y - 3.1f, 0f), Vector3.one * 0.18f);
            }
            scrollers.Add(new Mover { t = a });
        }
        // Giant crystal clusters along the road, pulsing on their kick.
        int n = 0;
        for (float z = -Behind; z < Far; z += 3.2f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                if (rng.NextDouble() < 0.25) continue;
                var c = Crystal(root, new Vector3(side * Rand(7.5f, 11f), -0.5f, z + Rand(0f, 2f)), Rand(1.5f, 5f), n % 4);
                pulsers.Add(new Mover { t = c, group = n % 4, size = 1f });
                scrollers.Add(new Mover { t = c });
            }
        // A few huge ones far out, standing in the dark.
        for (float z = -Behind; z < Far; z += 15f)
        {
            var c = Crystal(root, new Vector3(Side() * Rand(18f, 24f), -1f, z), Rand(9f, 14f), rng.Next(4));
            scrollers.Add(new Mover { t = c });
        }
        glowSkyMat = SkyQuad("DeepGlow", new Vector3(0f, 10f, 345f), 70f, 50f, new Color(0.5f, 0.3f, 1f, 1f), Sprite(128, 0.6f), 0.9f);
        ParticleBox("Sparkles", 160, cube, new[] { G(0), G(1), G(3) }, Vector3.one * 0.07f, new Vector2(0.15f, 0.6f));
    }

    // ---------- Cyber Factory ----------

    private void BuildCyberFactory()
    {
        Color orange = new Color(1f, 0.55f, 0.05f), cyan = new Color(0.1f, 0.9f, 1f), red = new Color(1f, 0.1f, 0.2f), lime = new Color(0.5f, 1f, 0.2f);
        Group(orange); Group(cyan); Group(red); Group(lime);
        var root = Root("Factory");
        Ground(root, Lit(new Color(0.08f, 0.08f, 0.09f), 0.7f, 0.5f, 0.03f), 12f, 13f);
        var steel = Lit(new Color(0.25f, 0.26f, 0.3f), 0.6f, 0.7f);
        Color tint = new Color(0.8f, 0.85f, 0.95f);
        // Factory halls behind: tall structures and machines, windows glowing.
        string[] halls = { "structure-tall", "structure-yellow-tall", "structure-high", "structure-window-wide", "machine-fortified" };
        for (float z = -Behind; z < Far; z += 6f)
            foreach (int side in new[] { -1, 1 })
            {
                var h = Kit(halls[rng.Next(halls.Length)], root, new Vector3(side * Rand(17f, 20f), -0.5f, z), Rand(7f, 12f), side > 0 ? -90f : 90f, tint, 0.35f);
                if (h != null) scrollers.Add(new Mover { t = h });
            }
        // Conveyors along both sides with boxes riding them faster than the road.
        string[] belts = { "conveyor-long-stripe-sides", "conveyor-long-sides", "conveyor-stripe-sides" };
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 3f)
            {
                var b = Kit(belts[rng.Next(belts.Length)], root, new Vector3(side * 8.5f, -0.5f, z), 0.9f, 0f, tint, 0.4f);
                if (b != null) scrollers.Add(new Mover { t = b });
            }
        string[] boxes = { "box-small", "box-large", "box-wide", "box-long" };
        for (float z = -Behind; z < Far; z += 2.5f)
        {
            int side = Side();
            var c = Prop(root, "Crate", new Vector3(side * 8.5f, 0.35f, z));
            Kit(boxes[rng.Next(boxes.Length)], c, Vector3.zero, Rand(0.6f, 1f), Rand(0f, 30f), new Color(1f, 0.85f, 0.6f), 0.5f);
            riders.Add(new Mover { t = c, speed = 1.35f });
        }
        // Robot arms swinging on the beat, cogs spinning, presses pumping.
        string[] arms = { "robot-arm-a", "robot-arm-b" };
        int n = 0;
        for (float z = -Behind; z < Far; z += 7f, n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var a = Prop(root, "Arm", new Vector3(side * 11f, -0.5f, z));
            var yawT = Prop(a, "Yaw", Vector3.zero);
            yawT.localRotation = Quaternion.Euler(0f, side > 0 ? -90f : 90f, 0f);
            var swingT = Prop(yawT, "Swing", Vector3.zero);    // the swing sets this one's rotation, the yaw stays above it
            Kit(arms[n % 2], swingT, Vector3.zero, Rand(2.8f, 3.6f), 0f, tint, 0.45f);
            floaters.Add(new Mover { t = swingT, seed = side > 0 ? 2f : 0f, group = -14 });
            Place("Lamp", sphere, G(n % 4), a, new Vector3(0f, 3.8f, 0f), Vector3.one * 0.35f);
            scrollers.Add(new Mover { t = a });
        }
        string[] cogs = { "cog-a", "cog-b", "cog-c", "cog-d", "cog-e" };
        for (float z = -Behind; z < Far; z += 9f)
        {
            int side = Side();
            var holder = Prop(root, "Cog", new Vector3(side * Rand(13f, 15f), Rand(4f, 8f), z));
            var spin = Prop(holder, "Spin", Vector3.zero);
            Kit(cogs[rng.Next(cogs.Length)], spin, Vector3.zero, Rand(2.5f, 4.5f), 0f, new Color(1f, 0.7f, 0.3f), 0.6f);
            spin.localRotation = Quaternion.Euler(0f, 90f, 0f);
            floaters.Add(new Mover { t = spin, basePos = new Vector3(0f, 90f, 0f), speed = side * 60f, seed = Rand(0f, 6f), group = -18 });
            scrollers.Add(new Mover { t = holder });
        }
        for (float z = -Behind; z < Far; z += 11f)
        {
            int side = Side();
            var p = Prop(root, "Press", new Vector3(side * 10.5f, -0.5f, z));
            var pump = Prop(p, "Pump", Vector3.zero);           // the jet's scale goes on this one, the model's own size below it
            Kit(rng.NextDouble() < 0.5 ? "piston-round" : "piston-square", pump, Vector3.zero, 2.2f, 0f, tint, 0.4f);
            floaters.Add(new Mover { t = pump, seed = Rand(0f, 6f), group = -15 });
            scrollers.Add(new Mover { t = p });
        }
        // Overhead pipes across the road with warning lights flashing; catwalks.
        for (float z = -Behind; z < Far; z += 16f)
        {
            var o = Prop(root, "Overhead", new Vector3(0f, 0f, z));
            RemixGeo.Beam(o, "Pipe", cyl, steel, new Vector3(-16f, 9.5f, 0f), new Vector3(16f, 9.5f, 0f), 0.45f);
            RemixGeo.Beam(o, "PipeGlow", cyl, G(1), new Vector3(-16f, 8.8f, 0.6f), new Vector3(16f, 8.8f, 0.6f), 0.12f);
            for (int k = -3; k <= 3; k++)
                flashers.Add(Place("Warn", sphere, G(k % 2 == 0 ? 0 : 2), o, new Vector3(k * 4f, 9f, -0.5f), Vector3.one * 0.35f));
            scrollers.Add(new Mover { t = o });
        }
        // Laser beams sweeping over the sky.
        var lasers = Root("Lasers");
        for (int i = 0; i < 6; i++)
        {
            var l = Prop(lasers, "Laser", new Vector3((i - 2.5f) * 12f, 0f, 120f));
            RemixGeo.Beam(l, "Beam", cyl, G(i % 2 == 0 ? 2 : 3), Vector3.zero, new Vector3(0f, 140f, 0f), 0.35f);
            searchlights.Add(l);
        }
        glowSkyMat = SkyQuad("Smog", new Vector3(0f, 14f, 345f), 220f, 45f, new Color(1f, 0.45f, 0.1f, 0.8f), Sprite(128, 1.2f), 0.8f);
        ParticleBox("Steam", 70, sphere, new[] { Lit(new Color(0.6f, 0.65f, 0.7f), 0.2f, 0f, 0.4f) }, Vector3.one * 0.35f, new Vector2(-0.8f, -0.3f));
        ParticleBox("Embers", 90, cube, new[] { G(0), G(2) }, Vector3.one * 0.06f, new Vector2(-1.2f, -0.4f));
    }

    // ---------- Planet Ring ----------

    // A planet on the sky, beyond the fog (a 3D one out there is lost in it): a shaded disc, and a
    // ring as a flat ellipse across it.
    private void Planet(Vector3 pos, float r, Color body, Color ring, float tilt)
    {
        var disc = Mask(128, 128, (x, y) =>
        {
            float dx = x * 2f - 1f, dy = y * 2f - 1f;
            if (dx * dx + dy * dy > 1f) return 0f;
            return Mathf.Clamp01(0.3f + 0.7f * (0.55f - dx * 0.45f + dy * 0.35f));   // lit from the upper left
        });
        SkyQuad("Planet", pos, r, r, body, disc, 1f);
        if (ring.a > 0f)
        {
            var band = Mask(256, 64, (x, y) =>
            {
                float dx = x * 2f - 1f, dy = y * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                return d > 0.62f && d < 0.95f ? 0.85f - 0.4f * Mathf.Abs(d - 0.78f) / 0.17f : 0f;
            });
            SkyQuad("Ring", pos + new Vector3(0f, 0f, -1f), r * 2.2f, r * 0.55f, ring, band, 0.9f, tilt);
        }
    }

    private void BuildPlanetRing()
    {
        Color violet = new Color(0.6f, 0.3f, 1f), cyan = new Color(0.1f, 0.9f, 1f), pink = new Color(1f, 0.25f, 0.7f), gold = new Color(1f, 0.75f, 0.25f);
        Group(violet); Group(cyan); Group(pink); Group(gold);
        var root = Root("Space");
        var rock = Lit(new Color(0.18f, 0.15f, 0.2f), 0.3f);
        // Planets out beyond the road, in front and to the sides, turning slowly.
        Planet(new Vector3(-95f, 55f, 338f), 42f, new Color(0.65f, 0.4f, 1f, 1f), new Color(1f, 0.6f, 0.9f, 0.9f), 16f);
        Planet(new Vector3(105f, 30f, 336f), 24f, new Color(0.2f, 0.8f, 1f, 1f), new Color(0.4f, 1f, 1f, 0.9f), -22f);
        Planet(new Vector3(35f, 95f, 340f), 13f, new Color(1f, 0.55f, 0.25f, 1f), new Color(0f, 0f, 0f, 0f), 0f);
        Planet(new Vector3(-20f, 12f, 342f), 9f, new Color(1f, 0.35f, 0.7f, 1f), new Color(1f, 0.8f, 0.3f, 0.8f), 8f);
        // Asteroids drifting past on both sides and above, tumbling.
        for (float z = -Behind; z < Far; z += 2.2f)
        {
            int side = Side();
            var a = Prop(root, "Asteroid", new Vector3(side * Rand(9f, 26f), Rand(-6f, 14f), z));
            Place("Rock", rockMesh, rock, a, Vector3.zero, Vector3.one * Rand(0.6f, 2.6f), new Vector3(Rand(0f, 360f), Rand(0f, 360f), 0f));
            if (rng.NextDouble() < 0.3) Place("Glow", sphere, G(rng.Next(4)), a, new Vector3(0f, 0.6f, 0f), Vector3.one * 0.25f);
            floaters.Add(new Mover { t = a, basePos = new Vector3(Rand(0f, 360f), Rand(0f, 360f), 0f), speed = Rand(-40f, 40f), seed = Rand(0f, 6f), group = -18 });
            scrollers.Add(new Mover { t = a });
        }
        // Beacon buoys along the road edge, a light on each pulsing on its kick.
        int n = 0;
        for (float z = -Behind; z < Far; z += 6f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var b = Prop(root, "Beacon", new Vector3(side * 7.5f, -1.5f, z + (side > 0 ? 3f : 0f)));
                RemixGeo.Beam(b, "Mast", cyl, rock, Vector3.zero, new Vector3(0f, 2.4f, 0f), 0.08f);
                var lamp = Place("Lamp", sphere, G(n % 4), b, new Vector3(0f, 2.6f, 0f), Vector3.one * 0.4f);
                pulsers.Add(new Mover { t = lamp.transform, group = n % 4, size = 0.4f });
                scrollers.Add(new Mover { t = b });
            }
        // The nebula behind it all.
        var cloud = Sprite(128, 1.2f);
        SkyQuad("NebulaA", new Vector3(-60f, 40f, 350f), 160f, 90f, new Color(0.6f, 0.2f, 1f, 0.55f), cloud, 0.7f, 20f);
        SkyQuad("NebulaB", new Vector3(70f, 60f, 352f), 150f, 80f, new Color(0.1f, 0.8f, 1f, 0.45f), cloud, 0.6f, -15f);
        glowSkyMat = SkyQuad("Star", new Vector3(10f, 20f, 345f), 30f, 30f, new Color(1f, 0.85f, 0.7f, 1f), Sprite(128, 0.5f), 1f);
        Fireworks(Root("Fireworks"), 1, 2);
        ParticleBox("Stardust", 200, sphere, new[] { G(1), G(3) }, Vector3.one * 0.06f, new Vector2(-0.3f, 0.3f), 18f, -6f, 20f);
    }

    // ---------- Firefly Jungle ----------

    private void BuildFireflyJungle()
    {
        Color lime = new Color(0.5f, 1f, 0.2f), amber = new Color(1f, 0.75f, 0.2f), pink = new Color(1f, 0.3f, 0.7f), cyan = new Color(0.2f, 1f, 0.9f);
        Group(lime); Group(amber); Group(pink); Group(cyan);
        var root = Root("Jungle");
        Ground(root, Lit(new Color(0.03f, 0.06f, 0.03f), 0.3f), 12f, 13f);
        var vine = Lit(new Color(0.08f, 0.2f, 0.08f), 0.3f, 0f, 0.15f);
        Color leaf = new Color(0.35f, 0.8f, 0.45f);
        // The jungle: palms, oaks and bushes, dense, three rows deep.
        string[] trees = { "tree_palmTall", "tree_palmDetailedTall", "tree_oak", "tree_palmBend", "tree_palmDetailedShort" };
        string[] bushes = { "plant_bushLarge", "plant_bush", "plant_flatTall", "plant_flatShort" };
        for (float z = -Behind; z < Far; z += 2.6f)
            foreach (int side in new[] { -1, 1 })
            {
                for (int row = 0; row < 3; row++)
                {
                    var t = Prop(root, "Tree", new Vector3(side * (9f + row * 4f + Rand(0f, 2.5f)), -0.5f, z + Rand(0f, 2f)));
                    Kit(trees[rng.Next(trees.Length)], t, Vector3.zero, Rand(7f, 13f), Rand(0f, 360f), leaf, 0.12f);
                    scrollers.Add(new Mover { t = t });
                }
                var b = Kit(bushes[rng.Next(bushes.Length)], root, new Vector3(side * Rand(7f, 8.5f), -0.5f, z + Rand(0f, 2f)), Rand(0.8f, 1.8f), Rand(0f, 360f), leaf, 0.2f);
                if (b != null) scrollers.Add(new Mover { t = b });
            }
        // Giant glowing flowers and mushrooms along the path, pulsing on their kick.
        string[] flowers = { "flower_purpleA", "flower_redA", "flower_yellowA", "flower_purpleB", "flower_redB", "flower_yellowB" };
        int n = 0;
        for (float z = -Behind; z < Far; z += 3f, n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var f = Prop(root, "Flower", new Vector3(side * Rand(7.2f, 9f), -0.5f, z));
            Kit(flowers[rng.Next(flowers.Length)], f, Vector3.zero, Rand(1.8f, 3.4f), Rand(0f, 360f), Color.Lerp(groupCols[n % 4], Color.white, 0.35f), 0.6f);
            pulsers.Add(new Mover { t = f, group = n % 4, size = 1f });
            scrollers.Add(new Mover { t = f });
            if (n % 3 == 0)
            {
                var m = Kit(rng.NextDouble() < 0.5 ? "mushroom_redTall" : "mushroom_tanTall", root, new Vector3(-side * Rand(7.5f, 9f), -0.5f, z + 1.5f), Rand(1.2f, 2.2f), Rand(0f, 360f), new Color(0.6f, 1f, 1f), 1.1f);
                if (m != null) scrollers.Add(new Mover { t = m });
            }
        }
        // Vines across the road, hung with glowing beads that swing.
        for (float z = -Behind; z < Far; z += 13f)
            Garland(root, z, 8.5f, 11f, vine, vine, 9, (pt, k, pos) =>
            {
                RemixGeo.Beam(pt, "Vine", cyl, vine, pos, pos + new Vector3(0f, -Rand(1f, 2.5f), 0f), 0.05f);
                var bead = Place("Bead", sphere, G(k % 4), pt, pos + new Vector3(0f, -2.6f, 0f), Vector3.one * 0.25f);
                pulsers.Add(new Mover { t = bead.transform, group = k % 4, size = 0.25f });
            });
        glowSkyMat = SkyQuad("Moon", new Vector3(70f, 85f, 340f), 22f, 22f, new Color(0.85f, 1f, 0.9f, 1f), Sprite(128, 0.55f), 1f);
        SkyQuad("Mist", new Vector3(0f, 14f, 348f), 260f, 35f, new Color(0.2f, 0.8f, 0.4f, 0.45f), Sprite(128, 1.5f), 0.55f);
        Fireworks(Root("Fireworks"), 0, 2);
        ParticleBox("Fireflies", 260, sphere, new[] { G(0), G(1) }, Vector3.one * 0.09f, new Vector2(-0.3f, 0.3f));
    }
}
