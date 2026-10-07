using System.Collections.Generic;
using UnityEngine;

/// HyperCity's extra dressing, in every skin: flying traffic in three sky lanes, vertical neon signs
/// on the towers, a neon guard rail with pulsing chevrons along the highway, searchlights sweeping
/// the sky, a far skyline silhouette with lit windows on the horizon, drones hovering over the road
/// in pairs - and neon palms along the road in SunsetPink.
public partial class HyperCityEnvironment
{
    private readonly List<Mover> cars = new List<Mover>();
    private readonly List<Mover> signs = new List<Mover>();
    private readonly List<Mover> chevrons = new List<Mover>();
    private readonly List<Mover> rails = new List<Mover>();
    private readonly List<Mover> searchlights = new List<Mover>();
    private readonly List<Mover> drones = new List<Mover>();
    private readonly List<Mover> palms = new List<Mover>();
    private Material carBodyMat, headMat, tailMat, railMat, chevronMat, searchMat, droneMat, palmMat, palmLeafMat;
    private readonly Material[] signMats = new Material[3];

    private void BuildDecor()
    {
        BuildSkyline();
        BuildTraffic();
        BuildSigns();
        BuildRails();
        BuildSearchlights();
        BuildDrones();
        if (skin == HyperCitySkin.SunsetPink) BuildPalms();
    }

    // A far skyline: a texture of tower outlines with window dots, on a fog-free sky quad.
    private void BuildSkyline()
    {
        const int W = 1024, H = 256;
        var tex = Own(new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp });
        var px = new Color32[W * H];
        var top = new int[W];
        int x = 0;
        while (x < W)
        {
            int w = rng.Next(14, 46), h = rng.Next(40, 230);
            for (int i = x; i < Mathf.Min(W, x + w); i++) top[i] = h;
            x += w + rng.Next(0, 6);
        }
        for (int yy = 0; yy < H; yy++)
            for (int xx = 0; xx < W; xx++)
            {
                byte a = 0, c = 0;
                if (yy < top[xx])
                {
                    a = 235; c = 30;                                         // dark body
                    bool window = (xx % 6 < 3) && (yy % 8 < 4) && ((xx * 7 + yy * 13) % 5 != 0) && yy < top[xx] - 4;
                    if (window && ((xx / 6 * 31 + yy / 8 * 17) % 3 == 0)) { c = 255; a = 255; }
                }
                px[yy * W + xx] = new Color32(c, c, c, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        Color col = Color.Lerp(primary, secondary, 0.35f); col.a = 1f;
        var mr = SkyQuad("Skyline", new Vector3(0f, 18f, 360f), 480f, 60f, col, tex, 0.55f);
        skylineMat = mr.sharedMaterial;
    }
    private Material skylineMat;

    // Flying cars: a dark body, white headlights in front, red tail lights behind, in three lanes high
    // over the sides; half fly with us, half come towards us.
    private void BuildTraffic()
    {
        var root = Root("Traffic");
        carBodyMat = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        carBodyMat.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.07f));
        headMat = Neon(new Color(0.9f, 0.95f, 1f));
        tailMat = Neon(new Color(1f, 0.1f, 0.15f));
        float far = span - Behind;
        float[] laneY = { 15f, 19f, 24f };
        for (int i = 0; i < 26; i++)
        {
            int side = i % 2 == 0 ? -1 : 1;
            int lane = i % 3;
            bool towards = (i / 2) % 2 == 0;
            var car = new GameObject("Car").transform;
            car.SetParent(root, false);
            car.localPosition = new Vector3(side * Rand(9f, 16f), laneY[lane] + Rand(-0.6f, 0.6f), Rand(-Behind, far));
            car.localRotation = towards ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            var body = Place("Body", cube, carBodyMat, car);
            body.transform.localScale = new Vector3(1.4f, 0.45f, 3f);
            RemixGeo.Beam(car, "Head", cube, headMat, new Vector3(-0.5f, 0f, 1.5f), new Vector3(0.5f, 0f, 1.5f), 0.14f);
            RemixGeo.Beam(car, "Tail", cube, tailMat, new Vector3(-0.55f, 0.05f, -1.52f), new Vector3(0.55f, 0.05f, -1.52f), 0.12f);
            RemixGeo.Beam(car, "Glow", cube, edgeMat, new Vector3(-0.72f, -0.24f, -1.3f), new Vector3(-0.72f, -0.24f, 1.3f), 0.06f);
            RemixGeo.Beam(car, "Glow", cube, edgeMat, new Vector3(0.72f, -0.24f, -1.3f), new Vector3(0.72f, -0.24f, 1.3f), 0.06f);
            cars.Add(new Mover { t = car, speed = towards ? Rand(10f, 18f) : -Rand(4f, 9f), seed = Rand(0f, 6.28f) });
        }
    }

    // Vertical neon signs on about half the towers' road-facing corners: a thin board of 3-5 lit
    // blocks, each tower's sign flickering on its own beat.
    private void BuildSigns()
    {
        signMats[0] = Neon(primary);
        signMats[1] = Neon(secondary);
        signMats[2] = Neon(Color.Lerp(primary, Color.white, 0.6f));
        foreach (var b in buildings)
        {
            if (rng.NextDouble() > 0.5) continue;
            var body = b.t.GetChild(0);
            float w = body.localScale.x, h = body.localScale.y, d = body.localScale.z;
            int side = b.t.localPosition.x < 0 ? -1 : 1;
            float fx = -side * (w * 0.5f + 0.6f);
            float y0 = Rand(4f, Mathf.Max(5f, h * 0.45f));
            int n = rng.Next(3, 6);
            var mat = signMats[rng.Next(3)];
            var sign = new GameObject("Sign").transform;
            sign.SetParent(b.t, false);
            sign.localPosition = new Vector3(fx, y0, -d * 0.5f + 0.4f);
            MeshRenderer last = null;
            for (int k = 0; k < n; k++)
                last = RemixGeo.Beam(sign, "Glyph", cube, mat, new Vector3(0f, k * 1.25f, 0f), new Vector3(0f, k * 1.25f + 0.85f, 0f), 0.9f);
            RemixGeo.Beam(sign, "Frame", cube, edgeMat, new Vector3(0f, -0.3f, -0.55f), new Vector3(0f, n * 1.25f, -0.55f), 0.08f);
            RemixGeo.Beam(sign, "Frame", cube, edgeMat, new Vector3(0f, -0.3f, 0.55f), new Vector3(0f, n * 1.25f, 0.55f), 0.08f);
            signs.Add(new Mover { t = sign, group = rng.Next(4), seed = Rand(0f, 1f) });
        }
    }

    // The guard rail: a neon line along each road edge at knee height, chevrons on it every 6 m
    // lighting in a wave that runs towards the camera.
    private void BuildRails()
    {
        var root = Root("GuardRail");
        railMat = Neon(Color.Lerp(secondary, Color.white, 0.2f));
        chevronMat = Neon(primary);
        float far = span - Behind;
        foreach (int side in new[] { -1, 1 })
        {
            for (float z = -Behind; z < far; z += 12f)
            {
                var seg = RemixGeo.Beam(root, "Rail", cube, railMat, new Vector3(side * 7.5f, 0.55f, z), new Vector3(side * 7.5f, 0.55f, z + 12f), 0.1f);
                rails.Add(new Mover { t = seg.transform });
            }
            for (float z = -Behind; z < far; z += 6f)
            {
                var c = new GameObject("Chevron").transform;
                c.SetParent(root, false);
                c.localPosition = new Vector3(side * 7.55f, 0.95f, z);
                MeshRenderer r1 = RemixGeo.Beam(c, "A", cube, chevronMat, new Vector3(0f, -0.35f, -0.3f), new Vector3(0f, 0f, 0.15f), 0.12f);
                RemixGeo.Beam(c, "B", cube, chevronMat, new Vector3(0f, 0.35f, -0.3f), new Vector3(0f, 0f, 0.15f), 0.12f);
                chevrons.Add(new Mover { t = c, r = r1 });
            }
        }
    }

    // Searchlights: long soft beams from the ground far out at the sides, sweeping the sky.
    private void BuildSearchlights()
    {
        searchMat = Own(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
        searchMat.SetFloat("_Surface", 1f);                      // transparent
        searchMat.SetFloat("_Blend", 2f);                        // additive
        searchMat.SetOverrideTag("RenderType", "Transparent");
        searchMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        searchMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        searchMat.SetInt("_ZWrite", 0);
        searchMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        searchMat.renderQueue = 3000;
        Color c = Color.Lerp(skin == HyperCitySkin.GoldFinal ? Gold : secondary, Color.white, 0.4f) * 0.18f;
        searchMat.SetColor("_BaseColor", c);
        var cyl = RemixGeo.Primitive(PrimitiveType.Cylinder);
        foreach (var (pos, ph) in new[] { (new Vector3(-38f, 0f, 70f), 0f), (new Vector3(42f, 0f, 90f), 1.7f), (new Vector3(-55f, 0f, 130f), 3.1f), (new Vector3(60f, 0f, 150f), 4.4f) })
        {
            var pivot = new GameObject("Searchlight").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = pos;
            var beam = Place("Beam", cyl, searchMat, pivot);
            beam.transform.localPosition = new Vector3(0f, 60f, 0f);
            beam.transform.localScale = new Vector3(2.6f, 60f, 2.6f);
            searchlights.Add(new Mover { t = pivot, r = beam, seed = ph, basePos = pos });
        }
    }

    // Drones: pairs of small glowing craft hovering over the road ahead, bobbing on the beat.
    private void BuildDrones()
    {
        var root = Root("Drones");
        droneMat = Neon(Color.Lerp(primary, Color.white, 0.4f));
        float far = span - Behind;
        int k = 0;
        for (float z = -Behind + 20f; z < far; z += 40f, k++)
            foreach (int side in new[] { -1, 1 })
            {
                var d = new GameObject("Drone").transform;
                d.SetParent(root, false);
                var basePos = new Vector3(side * Rand(3.5f, 5.5f), Rand(9.5f, 11.5f), z);
                d.localPosition = basePos;
                Place("Core", sphere, droneMat, d).transform.localScale = new Vector3(0.7f, 0.25f, 0.7f);
                foreach (var (ax, az) in new[] { (-0.6f, -0.6f), (0.6f, -0.6f), (-0.6f, 0.6f), (0.6f, 0.6f) })
                    RemixGeo.Beam(d, "Rotor", cube, edgeMat, new Vector3(ax - 0.2f, 0.1f, az), new Vector3(ax + 0.2f, 0.1f, az), 0.05f);
                drones.Add(new Mover { t = d, basePos = basePos, seed = Rand(0f, 6.28f), group = k % 2 });
            }
    }

    // Neon palms along the road (SunsetPink): a curved trunk of segments and drooping leaf strokes.
    private void BuildPalms()
    {
        var root = Root("Palms");
        palmMat = Neon(secondary);
        palmLeafMat = Neon(new Color(0.2f, 1f, 0.6f));
        float far = span - Behind;
        int k = 0;
        for (float z = -Behind + 6f; z < far; z += 24f, k++)
        {
            int side = k % 2 == 0 ? -1 : 1;
            var p = new GameObject("Palm").transform;
            p.SetParent(root, false);
            p.localPosition = new Vector3(side * 10f, -0.5f, z);
            Vector3 a = Vector3.zero;
            float lean = -side * 0.35f;
            for (int i = 0; i < 6; i++)
            {
                Vector3 b = a + new Vector3(lean * (i + 1) * 0.25f, 1.45f, 0f);
                RemixGeo.Beam(p, "Trunk", cube, palmMat, a, b, 0.32f - i * 0.03f);
                a = b;
            }
            for (int l = 0; l < 7; l++)
            {
                float ang = l * Mathf.PI * 2f / 7f;
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Vector3 mid = a + dir * 1.6f + Vector3.up * 0.5f;
                Vector3 tip = a + dir * 3f - Vector3.up * 0.6f;
                RemixGeo.Beam(p, "Leaf", cube, palmLeafMat, a, mid, 0.12f);
                RemixGeo.Beam(p, "Leaf", cube, palmLeafMat, mid, tip, 0.1f);
            }
            palms.Add(new Mover { t = p });
        }
    }

    private void UpdateDecor(float dt, float delta, float t, float beats, float kick, float energy, float drop, float barPulse)
    {
        float far = span - Behind;
        int beat = ClubBeat.BeatIndex;

        // Traffic: each car at its own speed relative to the road (towards us = faster).
        foreach (var c in cars)
        {
            var p = c.t.localPosition;
            p.z -= delta + c.speed * dt;
            if (p.z < -Behind) p.z += span;
            if (p.z > far) p.z -= span;
            p.y += Mathf.Sin(t * 1.2f + c.seed) * dt * 0.2f;
            c.t.localPosition = p;
        }
        RemixGeo.SetEmission(headMat, new Color(0.9f, 0.95f, 1f), Mathf.Min(MaxLevel, emission * 1.2f));
        RemixGeo.SetEmission(tailMat, new Color(1f, 0.1f, 0.15f), Mathf.Min(MaxLevel, emission * (0.8f + 0.5f * kick)));

        // Signs: each group flickers on in turn, all bright on a drop.
        int g = ((beat % 4) + 4) % 4;
        for (int i = 0; i < signMats.Length; i++)
        {
            Color c = i == 0 ? primary : i == 1 ? secondary : Color.Lerp(primary, Color.white, 0.6f);
            float level = 0.6f + 0.6f * barPulse * energy + 0.5f * drop + (i == g % 3 ? 0.5f * kick : 0f);
            RemixGeo.SetEmission(signMats[i], c, Mathf.Min(MaxLevel, emission * level));
        }
        foreach (var s in signs) s.t.gameObject.SetActive(!(s.group == g && kick > 0.85f && s.seed < 0.25f));   // a rare buzz-flicker

        // Guard rail and chevrons: a wave running towards the camera, one chevron a quarter beat.
        foreach (var r in rails) Scroll(r.t, delta);
        RemixGeo.SetEmission(railMat, Color.Lerp(secondary, Color.white, 0.2f), Mathf.Min(MaxLevel, emission * (0.5f + 0.3f * kick * energy)));
        foreach (var c in chevrons)
        {
            Scroll(c.t, delta);
            float ph = Mathf.Repeat(-c.t.localPosition.z / 6f * 0.25f - beats, 2f);
            float wave = ph < 0.5f ? 1f - ph * 2f : 0f;
            mpb.SetColor(RemixGeo.EmissionId, primary * Mathf.Min(MaxLevel, emission * (0.25f + 1.6f * wave * Mathf.Max(0.5f, energy))));
            c.r.SetPropertyBlock(mpb); c.t.GetChild(1).GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
        }

        // Searchlights: slow sweeps, crossing faster and brighter on a drop.
        foreach (var s in searchlights)
        {
            float sp = 0.35f + 0.6f * drop;
            s.t.localRotation = Quaternion.Euler(Mathf.Sin(t * sp * 0.7f + s.seed) * 18f, 0f, Mathf.Sin(t * sp + s.seed) * 32f - Mathf.Sign(s.basePos.x) * 12f);
        }
        Color sc = Color.Lerp(skin == HyperCitySkin.GoldFinal ? Gold : secondary, Color.white, 0.4f);
        searchMat.SetColor("_BaseColor", sc * (0.12f + 0.16f * drop + 0.05f * barPulse));

        // Drones: hover, a little hop on the beat (the pair in turn), lights pulse.
        foreach (var d in drones)
        {
            Scroll(d.t, delta);
            var p = d.t.localPosition;
            float hop = ((beat & 1) == d.group ? 0.35f * kick : 0f);
            p.y = d.basePos.y + Mathf.Sin(t * 1.6f + d.seed) * 0.25f + hop;
            p.x = d.basePos.x + Mathf.Sin(t * 0.7f + d.seed) * 0.4f;
            d.t.localPosition = p;
            d.t.localRotation = Quaternion.Euler(0f, t * 40f + d.seed * 50f, 0f);
        }
        RemixGeo.SetEmission(droneMat, Color.Lerp(primary, Color.white, 0.4f), Mathf.Min(MaxLevel, emission * (0.7f + 0.8f * kick * energy)));

        foreach (var p in palms) Scroll(p.t, delta);
        if (palmMat != null)
        {
            RemixGeo.SetEmission(palmMat, secondary, Mathf.Min(MaxLevel, emission * (0.5f + 0.3f * barPulse * energy)));
            RemixGeo.SetEmission(palmLeafMat, new Color(0.2f, 1f, 0.6f), Mathf.Min(MaxLevel, emission * (0.5f + 0.5f * kick * energy)));
        }
        if (skylineMat != null) skylineMat.SetFloat("_Intensity", 0.5f + 0.15f * barPulse * energy + 0.2f * drop);
    }
}
