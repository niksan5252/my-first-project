using System.Collections.Generic;
using UnityEngine;

/// Video 20's seven Halloween worlds on FiestaEnvironment's machinery: Pumpkin, Haunted, Witch,
/// Zombie, Vampire, Swamp, Monster. Kenney models where the project has them (carved pumpkins,
/// skeletons, gravestones, lanterns, candles, fences, mushrooms, lilies, trees, cars), primitives
/// with a glow for the rest (ghosts, bats, cauldrons, brooms, zombies, coffins). Every world has
/// something in the sky: a moon, bats circling, and fireworks on the drops.
public partial class FiestaEnvironment
{
    private struct Flyer { public Transform t; public Transform wingL, wingR; public float seed, speed, radius, height, z; }
    private readonly List<Flyer> flyers = new List<Flyer>();      // bats and brooms circling over the road
    private readonly List<MeshRenderer> flicker = new List<MeshRenderer>();   // windows and lamps that flicker

    private void BuildHalloween()
    {
        switch (skin)
        {
            case FiestaSkin.Pumpkin: BuildPumpkin(); break;
            case FiestaSkin.Haunted: BuildHaunted(); break;
            case FiestaSkin.Witch: BuildWitch(); break;
            case FiestaSkin.Zombie: BuildZombie(); break;
            case FiestaSkin.Vampire: BuildVampire(); break;
            case FiestaSkin.Swamp: BuildSwamp(); break;
            default: BuildMonster(); break;
        }
    }

    private void HalloweenUpdate(float beats)
    {
        float t = Time.time;
        for (int i = 0; i < flyers.Count; i++)
        {
            var f = flyers[i];
            float a = t * f.speed + f.seed;
            f.t.localPosition = new Vector3(Mathf.Sin(a) * f.radius, f.height + Mathf.Sin(a * 2f) * 1.2f, f.z + Mathf.Cos(a * 0.5f) * 10f);
            f.t.localRotation = Quaternion.Euler(0f, Mathf.Cos(a) > 0f ? 90f : -90f, Mathf.Cos(a) * 20f);
            if (f.wingL != null)
            {
                float flap = Mathf.Sin(t * 14f + f.seed) * 35f;
                f.wingL.localRotation = Quaternion.Euler(0f, 0f, flap);
                f.wingR.localRotation = Quaternion.Euler(0f, 0f, -flap);
            }
        }
        if (flicker.Count > 0)
        {
            int step = Mathf.FloorToInt(beats * 2f);
            for (int i = 0; i < flicker.Count; i++)
            {
                float h = Mathf.Repeat(Mathf.Sin((i + 1) * 78.233f + step * 0.731f) * 43758.5453f, 1f);
                flicker[i].enabled = h > 0.18f;
            }
        }
    }

    // ---------- shared pieces ----------

    private void Bats(int count, Material body, float height = 12f)
    {
        var root = Root("Bats");
        for (int i = 0; i < count; i++)
        {
            var b = Prop(root, "Bat", Vector3.zero);
            Place("Body", sphere, body, b, Vector3.zero, new Vector3(0.35f, 0.3f, 0.5f));
            var wl = Prop(b, "WingL", new Vector3(-0.15f, 0f, 0f));
            var wr = Prop(b, "WingR", new Vector3(0.15f, 0f, 0f));
            Place("Wing", cube, body, wl, new Vector3(-0.6f, 0f, 0f), new Vector3(1.2f, 0.04f, 0.5f), new Vector3(0f, 0f, 10f));
            Place("Wing", cube, body, wr, new Vector3(0.6f, 0f, 0f), new Vector3(1.2f, 0.04f, 0.5f), new Vector3(0f, 0f, -10f));
            Place("Eye", sphere, G(0), b, new Vector3(0f, 0.05f, 0.25f), Vector3.one * 0.12f);
            flyers.Add(new Flyer { t = b, wingL = wl, wingR = wr, seed = Rand(0f, 6.28f), speed = Rand(0.25f, 0.5f), radius = Rand(8f, 14f), height = height + Rand(-2f, 4f), z = Rand(15f, 60f) });
        }
    }

    private Transform Ghost(Transform parent, Vector3 pos, float s, Material glow, Material eye)
    {
        var g = Prop(parent, "Ghost", pos);
        Place("Head", sphere, glow, g, new Vector3(0f, 1.6f * s, 0f), Vector3.one * 1.0f * s);
        Place("Body", cyl, glow, g, new Vector3(0f, 0.85f * s, 0f), new Vector3(1.0f, 0.75f, 1.0f) * s);
        for (int k = 0; k < 5; k++)
        {
            float a = k * Mathf.PI * 2f / 5f;
            Place("Tail", sphere, glow, g, new Vector3(Mathf.Cos(a) * 0.35f * s, 0.12f * s, Mathf.Sin(a) * 0.35f * s), Vector3.one * 0.35f * s);
        }
        foreach (int e in new[] { -1, 1 })
            Place("Eye", sphere, eye, g, new Vector3(e * 0.2f * s, 1.7f * s, -0.42f * s), new Vector3(0.18f, 0.26f, 0.08f) * s);
        Place("Mouth", sphere, eye, g, new Vector3(0f, 1.38f * s, -0.45f * s), new Vector3(0.22f, 0.15f, 0.06f) * s);
        Bounce(g, 0.4f);
        return g;
    }

    private Transform DeadTree(Transform parent, Vector3 pos, float h, Material bark)
    {
        var t = Prop(parent, "DeadTree", pos);
        RemixGeo.Beam(t, "Trunk", cyl, bark, Vector3.zero, new Vector3(Rand(-0.4f, 0.4f), h, 0f), 0.45f);
        for (int k = 0; k < 5; k++)
        {
            Vector3 a = new Vector3(0f, h * Rand(0.45f, 0.95f), 0f);
            Vector3 b = a + new Vector3(Rand(-2.5f, 2.5f), Rand(0.6f, 1.8f), Rand(-1.2f, 1.2f));
            RemixGeo.Beam(t, "Branch", cyl, bark, a, b, 0.16f);
            RemixGeo.Beam(t, "Twig", cyl, bark, b, b + new Vector3(Rand(-1f, 1f), Rand(0.3f, 0.9f), Rand(-0.5f, 0.5f)), 0.08f);
        }
        return t;
    }

    private void Moon(Color c, Vector3 pos, float size)
    {
        glowSkyMat = SkyQuad("Moon", pos, size, size, c, Sprite(128, 0.4f), 1.2f);
        SkyQuad("MoonHalo", pos + new Vector3(0f, 0f, 5f), size * 2.4f, size * 2.4f, new Color(c.r, c.g, c.b, 0.35f), Sprite(128, 2f), 0.6f);
    }

    private Material Glow(Color c, float k) => Lit(c * 0.4f, 0.3f, 0f, k);

    // ---------- 1 pumpkin patch ----------

    private void BuildPumpkin()
    {
        Color orange = new Color(1f, 0.5f, 0.05f), yellow = new Color(1f, 0.85f, 0.2f), purple = new Color(0.6f, 0.2f, 1f), green = new Color(0.4f, 1f, 0.2f);
        Group(orange); Group(yellow); Group(purple); Group(green);
        var root = Root("Pumpkin");
        Ground(root, Lit(new Color(0.08f, 0.05f, 0.03f), 0.2f));
        var straw = Lit(new Color(0.7f, 0.55f, 0.25f), 0.3f, 0f, 0.12f);
        var wood = Lit(new Color(0.2f, 0.12f, 0.06f), 0.3f);
        var sack = Lit(new Color(0.55f, 0.42f, 0.25f), 0.3f, 0f, 0.1f);
        // Rows of carved pumpkins glowing by the road.
        for (float z = -Behind; z < Far; z += 2.2f)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Kit("pumpkin-carved", root, new Vector3(side * Rand(7.3f, 8.6f), -0.5f, z + Rand(0f, 1.5f)), Rand(0.7f, 1.4f), side > 0 ? -90f + Rand(-30f, 30f) : 90f + Rand(-30f, 30f), Color.white, 0.9f);
                if (p != null) scrollers.Add(new Mover { t = p });
            }
        // Scarecrows, hay bales and autumn trees further out.
        int n = 0;
        for (float z = -Behind; z < Far; z += 6f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Field", new Vector3(side * Rand(10.5f, 12.5f), -0.5f, z + (side > 0 ? 3f : 0f)));
                switch ((n + (side > 0 ? 1 : 0)) % 3)
                {
                    case 0:
                    {
                        RemixGeo.Beam(p, "Pole", cyl, wood, Vector3.zero, new Vector3(0f, 3.2f, 0f), 0.15f);
                        RemixGeo.Beam(p, "Arms", cyl, wood, new Vector3(0f, 2.4f, -1.1f), new Vector3(0f, 2.4f, 1.1f), 0.12f);
                        Place("Shirt", cube, G(2), p, new Vector3(0f, 2.1f, 0f), new Vector3(0.5f, 0.9f, 0.8f));
                        Place("Head", sphere, sack, p, new Vector3(0f, 3.1f, 0f), Vector3.one * 0.6f);
                        Place("Brim", cyl, straw, p, new Vector3(0f, 3.35f, 0f), new Vector3(1f, 0.03f, 1f));
                        Place("Crown", cyl, straw, p, new Vector3(0f, 3.55f, 0f), new Vector3(0.45f, 0.2f, 0.45f));
                        foreach (int e in new[] { -1, 1 })
                            Place("Eye", sphere, G(0), p, new Vector3(-side * 0.27f, 3.15f, e * 0.13f), Vector3.one * 0.1f);
                        break;
                    }
                    case 1:
                        for (int k = 0; k < 3; k++)
                            Place("Hay", cube, straw, p, new Vector3(Rand(-0.3f, 0.3f), 0.35f + (k == 2 ? 0.7f : 0f), k == 2 ? 0f : -0.6f + k * 1.2f), new Vector3(1.1f, 0.7f, 1.2f));
                        Kit("pumpkin-carved", p, new Vector3(0f, 1.4f, 0f), 0.8f, side * -90f, Color.white, 1f);
                        break;
                    default:
                        Kit("tree_oak", p, Vector3.zero, Rand(5f, 7f), Rand(0f, 360f), new Color(1f, 0.55f, 0.2f), 0.25f);
                        break;
                }
                scrollers.Add(new Mover { t = p });
            }
        // A wooden fence by the road.
        for (float z = -Behind; z < Far; z += 3f)
            foreach (int side in new[] { -1, 1 })
            {
                var f = Prop(root, "Fence", new Vector3(side * 9.6f, -0.5f, z + 1.5f));
                RemixGeo.Beam(f, "Post", cube, wood, Vector3.zero, new Vector3(0f, 1.2f, 0f), 0.15f);
                RemixGeo.Beam(f, "Rail", cube, wood, new Vector3(0f, 0.9f, -1.5f), new Vector3(0f, 0.9f, 1.5f), 0.1f);
                RemixGeo.Beam(f, "Rail", cube, wood, new Vector3(0f, 0.45f, -1.5f), new Vector3(0f, 0.45f, 1.5f), 0.1f);
                scrollers.Add(new Mover { t = f });
            }
        Bats(8, Lit(new Color(0.05f, 0.03f, 0.06f), 0.3f));
        Fireworks(Root("Fireworks"), 0, 2);
        Moon(new Color(1f, 0.6f, 0.2f, 1f), new Vector3(-40f, 60f, 340f), 40f);
        SkyQuad("Dusk", new Vector3(0f, 22f, 350f), 260f, 50f, new Color(0.6f, 0.2f, 0.7f, 0.5f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Leaves", 150, cube, new[] { G(0), G(1), Lit(new Color(0.6f, 0.15f, 0.05f), 0.3f, 0f, 0.3f) }, new Vector3(0.18f, 0.02f, 0.12f), new Vector2(0.6f, 1.3f));
    }

    // ---------- 2 haunted graveyard ----------

    private void BuildHaunted()
    {
        Color ghost = new Color(0.55f, 1f, 0.95f), lantern = new Color(1f, 0.75f, 0.3f), purple = new Color(0.6f, 0.3f, 1f), green = new Color(0.3f, 1f, 0.5f);
        Group(ghost); Group(lantern); Group(purple); Group(green);
        var root = Root("Haunted");
        Ground(root, Lit(new Color(0.04f, 0.05f, 0.05f), 0.3f));
        var bark = Lit(new Color(0.08f, 0.07f, 0.08f), 0.3f);
        var ghostMat = Glow(ghost, 1.6f);
        var black = Lit(new Color(0.01f, 0.01f, 0.02f), 0.3f);
        string[] stones = { "gravestone-decorative", "gravestone-cross", "gravestone-round", "cross-column", "pillar-obelisk" };
        // Iron fence right by the road, then rows of graves.
        for (float z = -Behind; z < Far; z += 2f)
            foreach (int side in new[] { -1, 1 })
            {
                var f = Kit("iron-fence", root, new Vector3(side * 7.4f, -0.5f, z + 1f), 1.4f, 90f, new Color(0.6f, 0.6f, 0.75f), 0.2f);
                if (f != null) scrollers.Add(new Mover { t = f });
            }
        for (float z = -Behind; z < Far; z += 2.6f)
            foreach (int side in new[] { -1, 1 })
                for (int row = 0; row < 2; row++)
                {
                    var g = Kit(stones[rng.Next(stones.Length)], root, new Vector3(side * (9f + row * 2.4f + Rand(-0.3f, 0.3f)), -0.5f, z + Rand(0f, 1.6f)), Rand(1.3f, 2.4f), side * -90f + Rand(-15f, 15f), new Color(0.7f, 0.75f, 0.9f), 0.3f);
                    if (g != null) scrollers.Add(new Mover { t = g });
                }
        // Lamp posts with lanterns, candles on some graves, dead trees behind.
        for (float z = -Behind; z < Far; z += 10f)
            foreach (int side in new[] { -1, 1 })
            {
                var l = Kit("lightpost-double", root, new Vector3(side * 8.2f, -0.5f, z + 5f), 5f, side > 0 ? 0f : 180f, new Color(0.5f, 0.5f, 0.6f), 0.2f);
                if (l == null) continue;
                flicker.Add(Place("Glow", sphere, G(1), l, new Vector3(0f, 1.15f, 0.3f), Vector3.one * 0.12f));
                flicker.Add(Place("Glow", sphere, G(1), l, new Vector3(0f, 1.15f, -0.3f), Vector3.one * 0.12f));
                scrollers.Add(new Mover { t = l });
            }
        for (float z = -Behind; z < Far; z += 5f)
        {
            int side = Side();
            var c = Kit("candle-multiple", root, new Vector3(side * Rand(9.5f, 11f), -0.5f, z), 0.5f, 0f, new Color(1f, 0.85f, 0.7f), 0.35f);
            if (c != null) scrollers.Add(new Mover { t = c });
        }
        for (float z = -Behind; z < Far; z += 7f)
            foreach (int side in new[] { -1, 1 })
                scrollers.Add(new Mover { t = DeadTree(root, new Vector3(side * Rand(15f, 19f), -0.5f, z + Rand(0f, 3f)), Rand(5f, 8f), bark) });
        // Ghosts drifting between the graves.
        for (float z = -Behind; z < Far; z += 9f)
        {
            int side = Side();
            var g = Ghost(root, new Vector3(side * Rand(9.5f, 12f), Rand(0.5f, 2f), z), Rand(0.9f, 1.4f), ghostMat, black);
            g.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
            scrollers.Add(new Mover { t = g });
        }
        Bats(6, Lit(new Color(0.03f, 0.03f, 0.05f), 0.3f));
        Fireworks(Root("Fireworks"), 0, 2);
        Moon(new Color(0.85f, 0.95f, 1f, 1f), new Vector3(50f, 70f, 340f), 34f);
        SkyQuad("Mist", new Vector3(0f, 10f, 350f), 260f, 40f, new Color(0.4f, 0.8f, 0.8f, 0.45f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Mist", 120, sphere, new[] { G(0), G(2) }, Vector3.one * 0.08f, new Vector2(-0.3f, 0.3f));
    }

    // ---------- 3 witch forest ----------

    private void BuildWitch()
    {
        Color potion = new Color(0.3f, 1f, 0.3f), violet = new Color(0.7f, 0.3f, 1f), magenta = new Color(1f, 0.2f, 0.8f), amber = new Color(1f, 0.7f, 0.2f);
        Group(potion); Group(violet); Group(magenta); Group(amber);
        var root = Root("Witch");
        Ground(root, Lit(new Color(0.03f, 0.03f, 0.05f), 0.3f));
        var iron = Lit(new Color(0.05f, 0.05f, 0.06f), 0.5f, 0.5f);
        var wood = Lit(new Color(0.18f, 0.1f, 0.05f), 0.3f);
        var straw = Lit(new Color(0.65f, 0.5f, 0.25f), 0.3f, 0f, 0.1f);
        string[] pines = { "tree_pineTallA", "tree_pineRoundA", "tree_pineDefaultA" };
        for (float z = -Behind; z < Far; z += 3.2f)
            foreach (int side in new[] { -1, 1 })
            {
                var t = Kit(pines[rng.Next(pines.Length)], root, new Vector3(side * Rand(10f, 16f), -0.5f, z + Rand(0f, 2f)), Rand(7f, 12f), Rand(0f, 360f), new Color(0.35f, 0.3f, 0.6f), 0.12f);
                if (t != null) scrollers.Add(new Mover { t = t });
            }
        string[] shrooms = { "mushroom_red", "mushroom_redTall", "mushroom_tanTall" };
        for (float z = -Behind; z < Far; z += 1.8f)
        {
            int side = Side();
            var m = Kit(shrooms[rng.Next(shrooms.Length)], root, new Vector3(side * Rand(7.2f, 8.2f), -0.5f, z), Rand(0.6f, 1.4f), Rand(0f, 360f), new Color(0.8f, 0.5f, 1f), 1.1f);
            if (m != null) scrollers.Add(new Mover { t = m });
        }
        // Cauldrons bubbling green, potion shelves.
        int n = 0;
        for (float z = -Behind; z < Far; z += 7f, n++)
        {
            int side = n % 2 == 0 ? -1 : 1;
            var c = Prop(root, "Cauldron", new Vector3(side * 9f, -0.5f, z));
            Place("Pot", sphere, iron, c, new Vector3(0f, 0.75f, 0f), new Vector3(1.6f, 1.3f, 1.6f));
            Place("Brew", cyl, G(0), c, new Vector3(0f, 1.3f, 0f), new Vector3(1.25f, 0.03f, 1.25f));
            for (int k = 0; k < 4; k++)
            {
                var bub = Place("Bubble", sphere, G(0), c, new Vector3(Rand(-0.4f, 0.4f), 1.5f, Rand(-0.4f, 0.4f)), Vector3.one * Rand(0.15f, 0.3f));
                Bounce(bub.transform, Rand(0.4f, 0.9f));
            }
            Kit("campfire_logs", c, Vector3.zero, 0.4f, 0f, Color.white, 0.4f);
            var fire = Place("Fire", sphere, G(3), c, new Vector3(0f, 0.2f, 0f), new Vector3(0.6f, 0.3f, 0.6f));
            floaters.Add(new Mover { t = fire.transform, seed = Rand(0f, 6.28f), group = -5, size = 0.5f });
            for (int k = 0; k < 3; k++)
            {
                var bottle = Prop(c, "Potion", new Vector3(-side * 1.4f, 0f, -0.8f + k * 0.8f));
                Place("Glass", cyl, G(k + 1), bottle, new Vector3(0f, 0.35f, 0f), new Vector3(0.35f, 0.35f, 0.35f));
                Place("Neck", cyl, G(k + 1), bottle, new Vector3(0f, 0.8f, 0f), new Vector3(0.14f, 0.12f, 0.14f));
                pulsers.Add(new Mover { t = bottle, group = k + 1, size = 1f });
            }
            scrollers.Add(new Mover { t = c });
        }
        // Brooms flying over the road.
        var brooms = Root("Brooms");
        for (int i = 0; i < 4; i++)
        {
            var b = Prop(brooms, "Broom", Vector3.zero);
            RemixGeo.Beam(b, "Stick", cyl, wood, new Vector3(0f, 0f, -1.2f), new Vector3(0f, 0f, 1.2f), 0.08f);
            Place("Bristles", cyl, straw, b, new Vector3(0f, 0f, -1.35f), new Vector3(0.4f, 0.3f, 0.4f), new Vector3(90f, 0f, 0f));
            Place("Hat", cyl, G(1), b, new Vector3(0f, 0.55f, 0.1f), new Vector3(0.7f, 0.03f, 0.7f));
            Place("Cone", cyl, G(1), b, new Vector3(0f, 0.85f, 0.1f), new Vector3(0.3f, 0.3f, 0.3f));
            flyers.Add(new Flyer { t = b, seed = i * 1.6f, speed = 0.3f + i * 0.05f, radius = Rand(9f, 13f), height = Rand(9f, 13f), z = 20f + i * 12f });
        }
        Bats(5, Lit(new Color(0.04f, 0.02f, 0.06f), 0.3f));
        Fireworks(Root("Fireworks"), 0, 1);
        Moon(new Color(0.6f, 1f, 0.5f, 1f), new Vector3(-60f, 75f, 340f), 30f);
        SkyQuad("Haze", new Vector3(0f, 22f, 350f), 260f, 50f, new Color(0.5f, 0.2f, 0.8f, 0.5f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Sparks", 140, sphere, new[] { G(0), G(1), G(2) }, Vector3.one * 0.07f, new Vector2(-0.4f, 0.4f));
    }

    // ---------- 4 zombie town ----------

    private Transform Zombie(Transform parent, Vector3 pos, Material skinMat, Material cloth, Material eye)
    {
        var z = Prop(parent, "Zombie", pos);
        Place("Legs", cube, cloth, z, new Vector3(0f, 0.55f, 0f), new Vector3(0.45f, 1.1f, 0.3f));
        Place("Body", cube, cloth, z, new Vector3(0f, 1.45f, 0f), new Vector3(0.6f, 0.75f, 0.35f));
        Place("Head", cube, skinMat, z, new Vector3(0f, 2.05f, 0.05f), new Vector3(0.42f, 0.42f, 0.42f));
        foreach (int s in new[] { -1, 1 })
        {
            RemixGeo.Beam(z, "Arm", cube, skinMat, new Vector3(s * 0.38f, 1.7f, 0f), new Vector3(s * 0.3f, 1.65f, 0.75f), 0.16f);
            Place("Eye", sphere, eye, z, new Vector3(s * 0.1f, 2.1f, 0.27f), Vector3.one * 0.08f);
        }
        return z;
    }

    private void BuildZombie()
    {
        Color toxic = new Color(0.5f, 1f, 0.2f), red = new Color(1f, 0.15f, 0.1f), amber = new Color(1f, 0.6f, 0.15f), blue = new Color(0.2f, 0.5f, 1f);
        Group(toxic); Group(red); Group(amber); Group(blue);
        var root = Root("Zombie");
        Ground(root, Lit(new Color(0.05f, 0.05f, 0.05f), 0.7f, 0.2f));
        Material[] walls = { Lit(new Color(0.12f, 0.12f, 0.1f), 0.3f), Lit(new Color(0.14f, 0.1f, 0.08f), 0.3f), Lit(new Color(0.09f, 0.11f, 0.1f), 0.3f) };
        var skinMat = Lit(new Color(0.35f, 0.55f, 0.25f), 0.3f, 0f, 0.25f);
        var cloth = Lit(new Color(0.2f, 0.18f, 0.25f), 0.3f);
        int n = 0;
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; n++)
            {
                float w = Rand(6f, 9f), h = Rand(7f, 13f);
                var b = Prop(root, "House", new Vector3(side * 15f, -0.5f, z + w * 0.5f));
                Place("Wall", cube, walls[n % walls.Length], b, new Vector3(0f, h * 0.5f, 0f), new Vector3(6f, h, w - 0.4f));
                for (float y = 1.8f; y < h - 1f; y += 2.6f)
                    for (float wz = -w * 0.5f + 1.2f; wz < w * 0.5f - 0.8f; wz += 1.7f)
                    {
                        if (rng.NextDouble() < 0.35) continue;
                        var win = Place("Window", cube, rng.NextDouble() < 0.7 ? G(2) : G(0), b, new Vector3(-side * 3.02f, y, wz), new Vector3(0.03f, 1.1f, 0.9f));
                        if (rng.NextDouble() < 0.5) flicker.Add(win);
                    }
                Place("Boards", cube, cloth, b, new Vector3(-side * 3.06f, 1.2f, 0f), new Vector3(0.05f, 0.2f, w * 0.6f), new Vector3(Rand(-15f, 15f), 0f, 0f));
                scrollers.Add(new Mover { t = b });
                z += w;
            }
        // Wrecked cars on the kerb, cones, flickering street lamps.
        string[] cars = { "sedan", "suv", "police", "van", "taxi" };
        for (float z = -Behind; z < Far; z += 9f)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, "Wreck", new Vector3(side * 9.5f, -0.5f, z + (side > 0 ? 4.5f : 0f)));
                var car = Kit(cars[rng.Next(cars.Length)], p, Vector3.zero, 1.5f, Rand(-40f, 40f) + (side > 0 ? 180f : 0f), new Color(0.45f, 0.45f, 0.45f), 0.25f);
                if (car != null) car.localRotation *= Quaternion.Euler(0f, 0f, Rand(-12f, 12f));
                Kit("cone", p, new Vector3(-side * 1.6f, 0f, 1f), 0.8f, 0f, Color.white, 0.5f);
                var smoke = Place("Smoke", sphere, Lit(new Color(0.3f, 0.3f, 0.3f), 0.2f, 0f, 0.2f), p, new Vector3(0f, 1.9f, 0f), new Vector3(1f, 0.7f, 1f));
                Bounce(smoke.transform, 0.3f);
                scrollers.Add(new Mover { t = p });
            }
        for (float z = -Behind; z < Far; z += 11f)
            foreach (int side in new[] { -1, 1 })
            {
                var l = Kit("lightpost-double", root, new Vector3(side * 8f, -0.5f, z + 5.5f), 5.5f, side > 0 ? 0f : 180f, new Color(0.5f, 0.5f, 0.55f), 0.2f);
                if (l == null) continue;
                flicker.Add(Place("Glow", sphere, G(2), l, new Vector3(0f, 1.15f, 0.3f), Vector3.one * 0.12f));
                flicker.Add(Place("Glow", sphere, G(2), l, new Vector3(0f, 1.15f, -0.3f), Vector3.one * 0.12f));
                scrollers.Add(new Mover { t = l });
            }
        // Zombies shambling along the pavements toward the camera.
        foreach (int side in new[] { -1, 1 })
            for (int k = 0; k < 6; k++)
            {
                var zb = Zombie(root, new Vector3(side * Rand(10.5f, 12f), -0.5f, Rand(5f, Far)), skinMat, cloth, G(1));
                zb.localRotation = Quaternion.Euler(0f, 180f + Rand(-20f, 20f), 0f);
                floaters.Add(new Mover { t = zb, basePos = zb.localPosition, seed = Rand(0f, 6.28f), group = -11 });
                riders.Add(new Mover { t = zb, speed = 1.15f });
            }
        Bats(5, Lit(new Color(0.04f, 0.03f, 0.03f), 0.3f));
        Fireworks(Root("Fireworks"), 0, 1);
        Moon(new Color(1f, 0.35f, 0.25f, 1f), new Vector3(30f, 65f, 340f), 34f);
        SkyQuad("Smog", new Vector3(0f, 18f, 350f), 260f, 50f, new Color(0.4f, 0.7f, 0.2f, 0.45f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Ash", 130, cube, new[] { Lit(new Color(0.5f, 0.5f, 0.5f), 0.2f, 0f, 0.3f), G(0) }, new Vector3(0.08f, 0.02f, 0.08f), new Vector2(0.4f, 1f));
    }

    // ---------- 5 vampire castle ----------

    private void BuildVampire()
    {
        Color blood = new Color(1f, 0.05f, 0.15f), gold = new Color(1f, 0.75f, 0.3f), violet = new Color(0.55f, 0.2f, 1f), candle = new Color(1f, 0.6f, 0.2f);
        Group(blood); Group(gold); Group(violet); Group(candle);
        var root = Root("Vampire");
        Ground(root, Lit(new Color(0.05f, 0.02f, 0.03f), 0.7f));
        var stone = Lit(new Color(0.16f, 0.15f, 0.18f), 0.35f);
        var dark = Lit(new Color(0.06f, 0.05f, 0.07f), 0.4f);
        var lid = Lit(new Color(0.15f, 0.05f, 0.04f), 0.6f);
        // Castle walls with battlements and red gothic windows on both sides; towers now and then.
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 8f)
            {
                var w = Prop(root, "Wall", new Vector3(side * 14f, -0.5f, z + 4f));
                Place("Wall", cube, stone, w, new Vector3(0f, 5f, 0f), new Vector3(2.5f, 10f, 8f));
                for (int k = 0; k < 4; k++)
                    Place("Merlon", cube, stone, w, new Vector3(0f, 10.5f, -3f + k * 2f), new Vector3(2.5f, 1f, 1f));
                foreach (float wz in new[] { -2f, 2f })
                {
                    Place("Window", cube, G(0), w, new Vector3(-side * 1.27f, 5f, wz), new Vector3(0.03f, 3f, 1.2f));
                    Place("Arch", sphere, G(0), w, new Vector3(-side * 1.27f, 6.5f, wz), new Vector3(0.03f, 1.2f, 1.2f));
                    Place("Mullion", cube, dark, w, new Vector3(-side * 1.29f, 5.3f, wz), new Vector3(0.04f, 3.4f, 0.1f));
                }
                if (((int)(z / 8f)) % 3 == 0)
                {
                    Place("Tower", cyl, stone, w, new Vector3(-side * 0.5f, 7.5f, 0f), new Vector3(3.5f, 7.5f, 3.5f));
                    Place("Roof", cyl, G(2), w, new Vector3(-side * 0.5f, 15.5f, 0f), new Vector3(3.9f, 0.5f, 3.9f));
                    Place("Spire", cyl, dark, w, new Vector3(-side * 0.5f, 17f, 0f), new Vector3(1.2f, 1.5f, 1.2f));
                }
                scrollers.Add(new Mover { t = w });
            }
        // Candelabras and coffins along the road.
        int n = 0;
        for (float z = -Behind; z < Far; z += 5f, n++)
            foreach (int side in new[] { -1, 1 })
            {
                var p = Prop(root, n % 2 == 0 ? "Candelabra" : "Coffin", new Vector3(side * 8.6f, -0.5f, z + (side > 0 ? 2.5f : 0f)));
                if ((n + (side > 0 ? 1 : 0)) % 2 == 0)
                {
                    RemixGeo.Beam(p, "Stem", cyl, dark, Vector3.zero, new Vector3(0f, 2.4f, 0f), 0.12f);
                    RemixGeo.Beam(p, "Arms", cube, dark, new Vector3(0f, 2.4f, -0.7f), new Vector3(0f, 2.4f, 0.7f), 0.08f);
                    foreach (float cz in new[] { -0.7f, 0f, 0.7f })
                    {
                        Place("Candle", cyl, Lit(new Color(0.9f, 0.85f, 0.8f), 0.3f, 0f, 0.3f), p, new Vector3(0f, 2.65f, cz), new Vector3(0.12f, 0.25f, 0.12f));
                        var fl = Place("Flame", sphere, G(3), p, new Vector3(0f, 3f, cz), new Vector3(0.12f, 0.2f, 0.12f));
                        floaters.Add(new Mover { t = fl.transform, seed = Rand(0f, 6.28f), group = -5, size = 0.12f });
                    }
                }
                else
                {
                    Place("Coffin", cube, lid, p, new Vector3(0f, 0.4f, 0f), new Vector3(1f, 0.8f, 2.2f), new Vector3(0f, Rand(-20f, 20f), 0f));
                    Place("Cross", cube, G(1), p, new Vector3(0f, 0.82f, 0f), new Vector3(0.1f, 0.04f, 0.9f));
                    Place("Cross", cube, G(1), p, new Vector3(0f, 0.82f, 0.2f), new Vector3(0.5f, 0.04f, 0.1f));
                }
                scrollers.Add(new Mover { t = p });
            }
        Bats(16, Lit(new Color(0.03f, 0.01f, 0.02f), 0.3f), 10f);
        Fireworks(Root("Fireworks"), 0, 2);
        Moon(new Color(1f, 0.15f, 0.15f, 1f), new Vector3(0f, 55f, 340f), 42f);
        SkyQuad("Dusk", new Vector3(0f, 22f, 350f), 260f, 50f, new Color(0.6f, 0.05f, 0.2f, 0.5f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Petals", 140, cube, new[] { G(0), G(0), G(2) }, new Vector3(0.14f, 0.02f, 0.1f), new Vector2(0.5f, 1.2f));
    }

    // ---------- 6 haunted swamp ----------

    private void BuildSwamp()
    {
        Color wisp = new Color(0.4f, 1f, 0.7f), toxic = new Color(0.7f, 1f, 0.2f), violet = new Color(0.6f, 0.3f, 1f), amber = new Color(1f, 0.7f, 0.25f);
        Group(wisp); Group(toxic); Group(violet); Group(amber);
        var root = Root("Swamp");
        var water = Lit(new Color(0.02f, 0.06f, 0.04f), 0.95f, 0.2f, 0.1f);
        var bark = Lit(new Color(0.07f, 0.08f, 0.05f), 0.3f);
        var moss = Lit(new Color(0.15f, 0.3f, 0.12f), 0.3f, 0f, 0.15f);
        foreach (int side in new[] { -1, 1 })
            for (float z = -Behind; z < Far; z += 12f)
            {
                var w = Prop(root, "Water", new Vector3(side * 14f, -0.6f, z + 6f));
                Place("Water", cube, water, w, Vector3.zero, new Vector3(14f, 0.05f, 12f));
                Place("Bank", cube, moss, w, new Vector3(-side * 6.9f, 0.02f, 0f), new Vector3(0.8f, 0.08f, 12f));
                scrollers.Add(new Mover { t = w });
            }
        for (float z = -Behind; z < Far; z += 2.4f)
        {
            int side = Side();
            var l = Kit(rng.NextDouble() < 0.5 ? "lily_large" : "lily_large", root, new Vector3(side * Rand(8f, 14f), -0.55f, z), Rand(0.3f, 0.6f), Rand(0f, 360f), new Color(0.6f, 1f, 0.6f), 0.5f);
            if (l != null) scrollers.Add(new Mover { t = l });
        }
        for (float z = -Behind; z < Far; z += 4.5f)
            foreach (int side in new[] { -1, 1 })
            {
                var t = DeadTree(root, new Vector3(side * Rand(9f, 17f), -0.6f, z + Rand(0f, 2f)), Rand(5f, 9f), bark);
                // moss hanging from the branches
                for (int k = 0; k < 3; k++)
                    Place("Moss", cube, moss, t, new Vector3(Rand(-1.5f, 1.5f), Rand(3.5f, 6f), Rand(-0.6f, 0.6f)), new Vector3(0.1f, Rand(0.8f, 1.6f), 0.4f));
                scrollers.Add(new Mover { t = t });
            }
        // Will-o'-wisps bobbing over the water, old boats, glowing mushrooms on the bank.
        for (float z = -Behind; z < Far; z += 3.5f)
        {
            int side = Side();
            var w = Place("Wisp", sphere, G(rng.Next(3)), root, new Vector3(side * Rand(8.5f, 14f), Rand(0.6f, 2.2f), z), Vector3.one * Rand(0.25f, 0.45f));
            Bounce(w.transform, 0.6f);
            scrollers.Add(new Mover { t = w.transform });
        }
        for (float z = -Behind; z < Far; z += 14f)
        {
            var b = Prop(root, "Boat", new Vector3(Side() * Rand(11f, 15f), -0.5f, z));
            Kit("canoe", b, Vector3.zero, 0.6f, Rand(0f, 360f), new Color(0.5f, 0.45f, 0.4f), 0.3f);
            Kit("lantern-candle", b, new Vector3(0f, 0.3f, 0f), 0.6f, 0f, new Color(1f, 0.85f, 0.6f), 1f);
            Bounce(b, 0.12f);
            scrollers.Add(new Mover { t = b });
        }
        string[] shrooms = { "mushroom_red", "mushroom_tanTall", "mushroom_redTall" };
        for (float z = -Behind; z < Far; z += 2f)
        {
            int side = Side();
            var m = Kit(shrooms[rng.Next(shrooms.Length)], root, new Vector3(side * Rand(7.2f, 7.8f), -0.5f, z), Rand(0.5f, 1f), Rand(0f, 360f), new Color(0.5f, 1f, 0.8f), 1f);
            if (m != null) scrollers.Add(new Mover { t = m });
        }
        Bats(6, Lit(new Color(0.03f, 0.04f, 0.03f), 0.3f), 10f);
        Fireworks(Root("Fireworks"), 0, 2);
        Moon(new Color(0.85f, 1f, 0.8f, 1f), new Vector3(-50f, 65f, 340f), 30f);
        SkyQuad("Fog", new Vector3(0f, 8f, 350f), 260f, 40f, new Color(0.3f, 0.7f, 0.4f, 0.45f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Fireflies", 160, sphere, new[] { G(0), G(1) }, Vector3.one * 0.07f, new Vector2(-0.3f, 0.3f));
    }

    // ---------- 7 monster party ----------

    private void BuildMonster()
    {
        Color orange = new Color(1f, 0.5f, 0.05f), purple = new Color(0.65f, 0.25f, 1f), lime = new Color(0.5f, 1f, 0.2f), pink = new Color(1f, 0.25f, 0.75f), cyan = new Color(0.2f, 0.9f, 1f);
        Group(orange); Group(purple); Group(lime); Group(pink); Group(cyan);
        var root = Root("Monster");
        Ground(root, Lit(new Color(0.05f, 0.03f, 0.06f), 0.8f, 0.2f));
        var black = Lit(new Color(0.03f, 0.03f, 0.04f), 0.5f);
        // Skeletons dancing on the beat by the road, pumpkins between them.
        for (float z = -Behind; z < Far; z += 4f)
            foreach (int side in new[] { -1, 1 })
            {
                var d = Prop(root, "Dancer", new Vector3(side * Rand(8f, 9.5f), -0.5f, z + (side > 0 ? 2f : 0f)));
                Kit("character-skeleton", d, Vector3.zero, 2.2f, side * -90f + Rand(-30f, 30f), new Color(1f, 0.95f, 1f), 0.5f);
                Place("Hat", cyl, G(1), d, new Vector3(0f, 2.25f, 0f), new Vector3(0.9f, 0.03f, 0.9f));
                Place("HatTop", cyl, G(1), d, new Vector3(0f, 2.55f, 0f), new Vector3(0.35f, 0.3f, 0.35f));
                floaters.Add(new Mover { t = d, basePos = d.localPosition, seed = Rand(0f, 6.28f), group = -11 });
                scrollers.Add(new Mover { t = d });
                var p = Kit("pumpkin-carved", root, new Vector3(side * 7.3f, -0.5f, z + 1f), 0.9f, side * -90f, Color.white, 1f);
                if (p != null) scrollers.Add(new Mover { t = p });
            }
        // Speaker stacks with pulsing cones and giant lollipops.
        for (float z = -Behind; z < Far; z += 8f)
            foreach (int side in new[] { -1, 1 })
            {
                var s = Prop(root, "Stack", new Vector3(side * 12f, -0.5f, z + (side > 0 ? 4f : 0f)));
                for (int k = 0; k < 3; k++)
                {
                    Place("Box", cube, black, s, new Vector3(0f, 0.9f + k * 1.8f, 0f), new Vector3(1.6f, 1.75f, 1.6f));
                    var cone = Place("Cone", cyl, G(k + 1), s, new Vector3(-side * 0.82f, 0.9f + k * 1.8f, 0f), new Vector3(1.1f, 0.03f, 1.1f), new Vector3(0f, 0f, 90f));
                    pulsers.Add(new Mover { t = cone.transform, group = k + 1, size = -1f });
                }
                Kit("lollypop", s, new Vector3(0f, 0f, side * 2f), 6f, 0f, Color.white, 0.6f);
                scrollers.Add(new Mover { t = s });
            }
        // Lasers sweeping from the stacks.
        for (float z = -Behind; z < Far; z += 16f)
        {
            int side = Side();
            var l = Prop(root, "Laser", new Vector3(side * 12f, 5.5f, z));
            var beam = Prop(l, "Beam", Vector3.zero);
            RemixGeo.Beam(beam, "Light", cube, G(rng.Next(5)), Vector3.zero, new Vector3(0f, 22f, 0f), 0.12f);
            searchlights.Add(beam);
            scrollers.Add(new Mover { t = l });
        }
        Bats(6, Lit(new Color(0.05f, 0.02f, 0.07f), 0.3f));
        Fireworks(Root("Fireworks"), 0, 3);
        Fireworks(Root("Fireworks2"), 1, 2);
        Moon(new Color(1f, 0.85f, 0.4f, 1f), new Vector3(40f, 70f, 340f), 32f);
        SkyQuad("Glow", new Vector3(0f, 22f, 350f), 260f, 50f, new Color(0.7f, 0.2f, 0.9f, 0.5f), Sprite(128, 1.5f), 0.6f);
        ParticleBox("Confetti", 200, cube, new[] { G(0), G(1), G(2), G(3), G(4) }, new Vector3(0.14f, 0.02f, 0.09f), new Vector2(0.7f, 1.6f));
    }
}
