using System.Collections.Generic;
using UnityEngine;

/// HyperCity's street level, filling the ground between the highway and the towers: a sidewalk with
/// glowing tile lines, a row of low shopfronts (lit windows, striped awnings, a sign over the door),
/// kiosks with screens, light bollards along the kerb, and a monorail on pillars over the sidewalk
/// with a lit train running past now and then.
public partial class HyperCityEnvironment
{
    private readonly List<Mover> street = new List<Mover>();
    private readonly List<Mover> bollards = new List<Mover>();
    private readonly List<Mover> trains = new List<Mover>();
    private readonly Material[] shopMats = new Material[3];
    private Material asphaltMat, laneDashMat, laneEdgeMat;
    private readonly List<Mover> groundCars = new List<Mover>();
    private Material walkMat, tileMat, awningMatA, awningMatB, kioskMat, bollardMat, railTrackMat, trainMat, trainWinMat;

    private void BuildStreet()
    {
        var root = Root("Street");
        float far = span - Behind;

        walkMat = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        walkMat.SetColor("_BaseColor", Color.Lerp(new Color(0.05f, 0.05f, 0.07f), primary, 0.05f));
        walkMat.SetFloat("_Smoothness", 0.75f);
        tileMat = Neon(Color.Lerp(primary, Color.white, 0.3f));
        shopMats[0] = Neon(Color.Lerp(secondary, Color.white, 0.35f));
        shopMats[1] = Neon(Color.Lerp(primary, Color.white, 0.35f));
        shopMats[2] = Neon(new Color(0.95f, 0.95f, 1f));
        awningMatA = Neon(primary);
        awningMatB = Neon(Color.white);
        kioskMat = Neon(Color.Lerp(secondary, primary, 0.5f));
        bollardMat = Neon(Color.Lerp(secondary, Color.white, 0.3f));
        var cyl = RemixGeo.Primitive(PrimitiveType.Cylinder);
        asphaltMat = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        asphaltMat.SetColor("_BaseColor", new Color(0.06f, 0.065f, 0.08f));
        asphaltMat.SetFloat("_Smoothness", 0.8f);
        laneDashMat = Neon(new Color(1f, 0.85f, 0.45f));
        laneEdgeMat = Neon(Color.Lerp(primary, Color.white, 0.4f));

        foreach (int side in new[] { -1, 1 })
        {
            // Side lanes of the highway between the road and the sidewalk: asphalt with a dashed
            // lane line and a solid edge line, and low cars driving in them.
            for (float z = -Behind; z < far; z += 12f)
            {
                var seg = new GameObject("SideLanes").transform;
                seg.SetParent(root, false);
                seg.localPosition = new Vector3(side * 5.4f, -0.47f, z + 6f);
                var slab = Place("Asphalt", cube, asphaltMat, seg);
                slab.transform.localScale = new Vector3(4.8f, 0.06f, 12f);
                for (int k = 0; k < 2; k++)
                    RemixGeo.Beam(seg, "Dash", cube, laneDashMat, new Vector3(0f, 0.05f, -5f + k * 6f), new Vector3(0f, 0.05f, -2f + k * 6f), 0.12f);
                RemixGeo.Beam(seg, "Edge", cube, laneEdgeMat, new Vector3(side * 2.1f, 0.05f, -6f), new Vector3(side * 2.1f, 0.05f, 6f), 0.1f);
                street.Add(new Mover { t = seg });
            }
            for (int c = 0; c < 6; c++)
            {
                var car = new GameObject("GroundCar").transform;
                car.SetParent(root, false);
                float lx = side * (c % 2 == 0 ? 4.3f : 6.5f);
                car.localPosition = new Vector3(lx, -0.05f, Rand(-Behind, far));
                var body = Place("Body", cube, carBodyMat, car);
                body.transform.localPosition = new Vector3(0f, 0.3f, 0f);
                body.transform.localScale = new Vector3(1.5f, 0.55f, 3.4f);
                var roof = Place("Roof", cube, carBodyMat, car);
                roof.transform.localPosition = new Vector3(0f, 0.75f, -0.2f);
                roof.transform.localScale = new Vector3(1.2f, 0.4f, 1.8f);
                RemixGeo.Beam(car, "Tail", cube, tailMat, new Vector3(-0.6f, 0.4f, -1.72f), new Vector3(0.6f, 0.4f, -1.72f), 0.1f);
                RemixGeo.Beam(car, "Under", cube, edgeMat, new Vector3(-0.6f, 0.05f, -1.5f), new Vector3(-0.6f, 0.05f, 1.5f), 0.06f);
                RemixGeo.Beam(car, "Under", cube, edgeMat, new Vector3(0.6f, 0.05f, -1.5f), new Vector3(0.6f, 0.05f, 1.5f), 0.06f);
                groundCars.Add(new Mover { t = car, speed = -Rand(5f, 11f) });
            }

            // Sidewalk slabs (12 m each, so they scroll and wrap like everything else) with tile lines.
            for (float z = -Behind; z < far; z += 12f)
            {
                var seg = new GameObject("Walk").transform;
                seg.SetParent(root, false);
                seg.localPosition = new Vector3(side * 12.9f, -0.42f, z + 6f);
                var slab = Place("Slab", cube, walkMat, seg);
                slab.transform.localScale = new Vector3(10.2f, 0.16f, 12f);
                for (int k = 0; k < 3; k++)
                    RemixGeo.Beam(seg, "Tile", cube, tileMat, new Vector3(-5f, 0.09f, -6f + k * 4f), new Vector3(5f, 0.09f, -6f + k * 4f), 0.04f);
                RemixGeo.Beam(seg, "Kerb", cube, tileMat, new Vector3(-side * 5.05f, 0.1f, -6f), new Vector3(-side * 5.05f, 0.1f, 6f), 0.06f);
                street.Add(new Mover { t = seg });
            }

            // Shopfronts in a row in front of the towers.
            int n = 0;
            for (float z = -Behind; z < far; n++)
            {
                float w = Rand(5f, 8f), h = Rand(3.2f, 4.6f);
                var shop = new GameObject("Shop").transform;
                shop.SetParent(root, false);
                shop.localPosition = new Vector3(side * 16.6f, -0.35f, z + w * 0.5f);
                var body = Place("Body", cube, bodyMat, shop);
                body.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
                body.transform.localScale = new Vector3(2.6f, h, w);
                float fx = -side * 1.32f;                                   // the front, towards the road
                var win = Place("Window", cube, shopMats[n % 3], shop);
                win.transform.localPosition = new Vector3(fx, 1.35f, 0f);
                win.transform.localScale = new Vector3(0.05f, 1.7f, w * 0.78f);
                // Striped awning, tilted down towards the road.
                int stripes = Mathf.Max(3, Mathf.RoundToInt(w / 0.9f));
                for (int k = 0; k < stripes; k++)
                {
                    float z0 = -w * 0.45f + k * w * 0.9f / stripes, z1 = z0 + w * 0.9f / stripes;
                    var a = Place("Awning", cube, k % 2 == 0 ? awningMatA : awningMatB, shop);
                    a.transform.localPosition = new Vector3(fx - side * 0.6f, 2.55f, (z0 + z1) * 0.5f);
                    a.transform.localScale = new Vector3(1.3f, 0.06f, z1 - z0);
                    a.transform.localRotation = Quaternion.Euler(0f, 0f, side * 18f);
                }
                var sign = Place("Sign", cube, shopMats[(n + 1) % 3], shop);
                sign.transform.localPosition = new Vector3(fx - side * 0.05f, h - 0.5f, 0f);
                sign.transform.localScale = new Vector3(0.12f, 0.55f, w * 0.5f);
                street.Add(new Mover { t = shop });
                z += w + Rand(0.4f, 1.2f);
            }

            // Kiosks with a screen towards the road, and light bollards along the kerb.
            for (float z = -Behind + Rand(2f, 8f); z < far; z += Rand(13f, 19f))
            {
                var k = new GameObject("Kiosk").transform;
                k.SetParent(root, false);
                k.localPosition = new Vector3(side * Rand(10.5f, 12.5f), -0.35f, z);
                var b = Place("Body", cube, bodyMat, k);
                b.transform.localPosition = new Vector3(0f, 1.1f, 0f);
                b.transform.localScale = new Vector3(1f, 2.2f, 1.2f);
                var scr = Place("Screen", cube, kioskMat, k);
                scr.transform.localPosition = new Vector3(-side * 0.52f, 1.35f, 0f);
                scr.transform.localScale = new Vector3(0.04f, 1.2f, 0.95f);
                street.Add(new Mover { t = k });
            }
            for (float z = -Behind; z < far; z += 3f)
            {
                var bo = new GameObject("Bollard").transform;
                bo.SetParent(root, false);
                bo.localPosition = new Vector3(side * 8.3f, -0.35f, z);
                var post = Place("Post", cyl, bodyMat, bo);
                post.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                post.transform.localScale = new Vector3(0.22f, 0.45f, 0.22f);
                var cap = Place("Cap", cyl, bollardMat, bo);
                cap.transform.localPosition = new Vector3(0f, 0.95f, 0f);
                cap.transform.localScale = new Vector3(0.24f, 0.06f, 0.24f);
                bollards.Add(new Mover { t = bo, r = cap });
            }

            // Monorail: a track over the sidewalk on pillars, and one train per side.
            railTrackMat = railTrackMat != null ? railTrackMat : Neon(Color.Lerp(primary, secondary, 0.5f));
            for (float z = -Behind; z < far; z += 12f)
            {
                var seg = new GameObject("Track").transform;
                seg.SetParent(root, false);
                seg.localPosition = new Vector3(side * 13.2f, 6.8f, z);
                var beam = Place("Beam", cube, bodyMat, seg);
                beam.transform.localPosition = new Vector3(0f, 0f, 6f);
                beam.transform.localScale = new Vector3(1.1f, 0.6f, 12f);
                RemixGeo.Beam(seg, "Edge", cube, railTrackMat, new Vector3(-0.58f, -0.3f, 0f), new Vector3(-0.58f, -0.3f, 12f), 0.07f);
                RemixGeo.Beam(seg, "Edge", cube, railTrackMat, new Vector3(0.58f, -0.3f, 0f), new Vector3(0.58f, -0.3f, 12f), 0.07f);
                if (((int)((z + Behind) / 12f)) % 2 == 0)
                {
                    var pil = Place("Pillar", cube, bodyMat, seg);
                    pil.transform.localPosition = new Vector3(0f, -3.7f, 0f);
                    pil.transform.localScale = new Vector3(0.6f, 6.8f, 0.6f);
                }
                street.Add(new Mover { t = seg });
            }
        }

        trainMat = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        trainMat.SetColor("_BaseColor", new Color(0.75f, 0.78f, 0.85f));
        trainMat.SetFloat("_Smoothness", 0.8f);
        trainMat.SetFloat("_Metallic", 0.5f);
        trainWinMat = Neon(Color.Lerp(secondary, Color.white, 0.5f));
        foreach (int side in new[] { -1, 1 })
        {
            var tr = new GameObject("Train").transform;
            tr.SetParent(root, false);
            for (int c = 0; c < 4; c++)
            {
                float zc = c * 6.6f;
                var car = Place("Car", cube, trainMat, tr);
                car.transform.localPosition = new Vector3(0f, 0f, zc);
                car.transform.localScale = new Vector3(1.5f, 1.6f, 6.2f);
                RemixGeo.Beam(tr, "Windows", cube, trainWinMat, new Vector3(-side * 0.77f, 0.25f, zc - 2.6f), new Vector3(-side * 0.77f, 0.25f, zc + 2.6f), 0.45f);
            }
            RemixGeo.Beam(tr, "Head", sphere, headMat, new Vector3(0f, 0.2f, 26.4f - 3.4f), new Vector3(0f, 0.2f, 26.4f - 3.4f), 0.5f);
            tr.localPosition = new Vector3(side * 13.2f, 7.9f, 400f);
            trains.Add(new Mover { t = tr, seed = side < 0 ? 0f : 0.5f, speed = side < 0 ? 38f : -30f });
        }
    }

    private void UpdateStreet(float dt, float delta, float t, float beats, float kick, float energy, float drop, float barPulse)
    {
        foreach (var s in street) Scroll(s.t, delta);
        float farZ = span - Behind;
        foreach (var c in groundCars)
        {
            var p = c.t.localPosition;
            p.z -= delta + c.speed * dt;             // slower than the road: they drive with us
            if (p.z < -Behind) p.z += span;
            if (p.z > farZ) p.z -= span;
            c.t.localPosition = p;
        }
        RemixGeo.SetEmission(laneDashMat, new Color(1f, 0.85f, 0.45f), Mathf.Min(MaxLevel, emission * 0.6f));
        RemixGeo.SetEmission(laneEdgeMat, Color.Lerp(primary, Color.white, 0.4f), Mathf.Min(MaxLevel, emission * (0.45f + 0.35f * kick * energy)));

        RemixGeo.SetEmission(tileMat, Color.Lerp(primary, Color.white, 0.3f), Mathf.Min(MaxLevel, emission * (0.2f + 0.35f * barPulse * energy)));
        int on = ((ClubBeat.BeatIndex % 3) + 3) % 3;
        for (int i = 0; i < 3; i++)
        {
            Color c = i == 0 ? Color.Lerp(secondary, Color.white, 0.35f) : i == 1 ? Color.Lerp(primary, Color.white, 0.35f) : new Color(0.95f, 0.95f, 1f);
            RemixGeo.SetEmission(shopMats[i], c, Mathf.Min(MaxLevel, emission * (0.55f + (i == on ? 0.5f * kick * energy : 0f))));
        }
        RemixGeo.SetEmission(awningMatA, primary, Mathf.Min(MaxLevel, emission * (0.45f + 0.3f * barPulse * energy)));
        RemixGeo.SetEmission(awningMatB, Color.white, Mathf.Min(MaxLevel, emission * 0.35f));
        RemixGeo.SetEmission(kioskMat, Color.Lerp(secondary, primary, 0.5f + 0.5f * Mathf.Sin(t * 2f)), Mathf.Min(MaxLevel, emission * (0.6f + 0.4f * kick)));
        RemixGeo.SetEmission(railTrackMat, Color.Lerp(primary, secondary, 0.5f), Mathf.Min(MaxLevel, emission * (0.4f + 0.3f * barPulse * energy)));
        RemixGeo.SetEmission(trainWinMat, Color.Lerp(secondary, Color.white, 0.5f), Mathf.Min(MaxLevel, emission * 1.1f));

        // Bollards: a quick wave along the kerb towards the camera, twice a beat.
        Color bc = Color.Lerp(secondary, Color.white, 0.3f);
        foreach (var b in bollards)
        {
            Scroll(b.t, delta);
            float ph = Mathf.Repeat(-b.t.localPosition.z / 3f * 0.125f - beats * 2f, 2f);
            float wave = ph < 0.4f ? 1f - ph / 0.4f : 0f;
            mpb.SetColor(RemixGeo.EmissionId, bc * Mathf.Min(MaxLevel, emission * (0.3f + 1.4f * wave * Mathf.Max(0.5f, energy))));
            b.r.SetPropertyBlock(mpb);
        }

        // Trains: each crosses the whole view about every 16 s, the two sides out of step.
        const float period = 16f;
        foreach (var tr in trains)
        {
            float ph = Mathf.Repeat(t / period + tr.seed, 1f);
            float len = span + 60f;
            float z = tr.speed > 0f ? len * ph - 30f : span - len * ph + 30f;
            var p = tr.t.localPosition;
            p.z = z - 13f;
            tr.t.localPosition = p;
        }
    }
}
