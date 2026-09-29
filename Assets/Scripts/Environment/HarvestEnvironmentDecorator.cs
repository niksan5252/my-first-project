using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_AutumnHarvest's environment, "Sunset over the harvest": the road runs between fields under
/// a huge orange sun sitting on the horizon (the theme's sky). On the Halloween machinery
/// (GraveyardEnvironmentDecorator: props, lights and emission kicking on the beat, haze, a sun that
/// breathes) it builds, travelling with the road:
///
///   * rows of wheat and corn either side (Kenney Nature Kit), golden in the low sun, swaying and
///     brightening on the beat;
///   * a pumpkin patch along the road's edge behind a wooden fence, some pumpkins glowing;
///   * haystacks, and wooden poles carrying paper lanterns in pink, orange and gold that swing
///     and flare on the beat, a chase running along them on the eighths;
///   * windmills out in the fields (procedural), their sails turning faster through a drop;
///   * golden dust in the air, and at the end of the segment a flock of birds lifting off the
///     fields and flying away into the sunset.
public class HarvestEnvironmentDecorator : GraveyardEnvironmentDecorator
{
    [Header("Harvest models")]
    [SerializeField] private GameObject[] wheat;
    [SerializeField] private GameObject[] corn;
    [SerializeField] private GameObject[] pumpkinCrops;
    [SerializeField] private GameObject[] haystacks;
    [SerializeField] private GameObject fence;

    [Header("Harvest colours")]
    [SerializeField] private Color[] lanternColors = { new Color(1f, 0.45f, 0.55f), new Color(1f, 0.55f, 0.15f), new Color(1f, 0.82f, 0.3f) };
    [SerializeField] private Color cropGold = new Color(1f, 0.75f, 0.3f);
    [SerializeField] private float birdsBeforeEnd = 18f;
    [SerializeField] private int birdCount = 46;

    private Material woodMat, sailMat, birdMat, cropMat;
    private Texture2D birdTex;
    private struct Lantern { public Transform t; public MeshRenderer glow; public Color color; public int index; public float seed; }
    private readonly List<Lantern> lanterns = new List<Lantern>();
    private readonly List<Transform> sails = new List<Transform>();
    private readonly List<Transform> cropSway = new List<Transform>();
    private class Bird { public Transform t; public Vector3 start, dir; public float delay, speed, flap, size; }
    private readonly List<Bird> birds = new List<Bird>();
    private float sailAngle;
    private bool flockStarted;
    private float flockStart;

    protected override void BuildSegment(Transform stage, int index, float z)
    {
        if (woodMat == null)
        {
            woodMat = Lit(new Color(0.3f, 0.18f, 0.1f), 0.2f);
            sailMat = Lit(new Color(0.85f, 0.75f, 0.6f), 0.1f);
        }
        var seg = new GameObject("Segment_" + index.ToString("00")).transform;
        seg.SetParent(stage, false);
        seg.localPosition = new Vector3(0f, 0f, z);
        RegisterSegment(seg);

        for (int s = -1; s <= 1; s += 2)
        {
            // The field: wheat or corn, in rows running along the road.
            bool isCorn = (index / 3 + (s > 0 ? 1 : 0)) % 2 == 0;
            for (int row = 0; row < 6; row++)
                for (int k = 0; k < 3; k++)
                {
                    var c = Prop(seg, Pick(isCorn ? corn : wheat), new Vector3(s * (7.2f + row * 2.3f + Rand(-0.3f, 0.3f)), 0f, -3f + k * 3f + Rand(-0.6f, 0.6f)),
                                 Rand(0f, 360f), isCorn ? Rand(3.2f, 4f) : Rand(3.6f, 4.6f));
                    if (c != null) { Golden(c); if ((row + k) % 3 == 0) cropSway.Add(c); }
                }
            // The fence along the road, the pumpkin patch behind it.
            if (fence != null)
                for (float f = -segmentSpacing * 0.5f; f < segmentSpacing * 0.5f - 0.1f; f += 2.4f)
                    Prop(seg, fence, new Vector3(s * 5.1f, 0f, f + 1.2f), 90f, 2.4f);
            for (int p = 0; p < 3; p++)
            {
                var pk = Prop(seg, Pick(pumpkinCrops), new Vector3(s * Rand(5.6f, 6.6f), 0f, -3f + p * 3f), Rand(0f, 360f), Rand(2.8f, 3.6f));
                if (pk == null) continue;
                swells.Add(new Swell { t = pk, rest = pk.localScale, amount = 0.1f });
                if (p == 1 && rng.NextDouble() < 0.5)
                {
                    var gl = GlowQuad(seg, "PumpkinGlow", flareMask, flameOrange, false);
                    gl.transform.localPosition = pk.localPosition + new Vector3(0f, 0.7f, 0f);
                    gl.transform.localScale = Vector3.one * 2f;
                    glows.Add(new Glow { r = gl, color = flameOrange, baseI = 0.3f, kickI = 0.8f, face = true });
                }
            }
            // Haystacks.
            if ((index + (s > 0 ? 2 : 0)) % 4 == 0)
            {
                var h = Pick(haystacks);
                for (int k = 0; k < 4; k++)
                    Prop(seg, h, new Vector3(s * (8.5f + (k % 2) * 1.5f), k >= 2 ? 0.9f * 2.3f : 0f, (k % 2 - 0.5f) * 1.2f + (k >= 2 ? 0.3f : 0f)), s > 0 ? -90f : 90f, 2.3f);
            }
            // A pole with paper lanterns, every segment, alternating sides.
            if ((index % 2 == 0) == (s < 0))
            {
                Box(seg, new Vector3(s * 5.6f, 2.2f, 0f), new Vector3(0.18f, 4.4f, 0.18f), woodMat);
                Box(seg, new Vector3(s * 5.1f, 4.2f, 0f), new Vector3(1.2f, 0.12f, 0.12f), woodMat);
                for (int l = 0; l < 3; l++)
                {
                    Color c = lanternColors[(index + l) % lanternColors.Length];
                    var holder = new GameObject("Lantern").transform;
                    holder.SetParent(seg, false);
                    holder.localPosition = new Vector3(s * (4.6f + l * 0.5f), 4.1f, 0f);
                    var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(body.GetComponent<Collider>());
                    body.transform.SetParent(holder, false);
                    body.transform.localPosition = new Vector3(0f, -0.45f, 0f);
                    body.transform.localScale = new Vector3(0.42f, 0.55f, 0.42f);
                    var br = body.GetComponent<MeshRenderer>();
                    var bm = Lit(c * 0.5f, 0.3f);
                    bm.EnableKeyword("_EMISSION");
                    bm.SetColor("_EmissionColor", c * 0.6f);
                    RegisterEmissive(bm, c * 0.6f);
                    br.sharedMaterial = bm;
                    br.shadowCastingMode = ShadowCastingMode.Off;
                    var gl = GlowQuad(seg, "LanternGlow", flareMask, c, false);
                    gl.transform.localPosition = holder.localPosition + new Vector3(0f, -0.45f, 0f);
                    gl.transform.localScale = Vector3.one * 1.3f;
                    lanterns.Add(new Lantern { t = holder, glow = gl, color = c, index = lanterns.Count, seed = index * 0.7f + l });
                }
            }
            // A windmill far out in the fields every fifth segment.
            if ((index + (s > 0 ? 2 : 0)) % 5 == 0) Windmill(seg, new Vector3(s * Rand(20f, 26f), 0f, Rand(-2f, 2f)), s);

            // Golden dust and haze.
            if (rng.NextDouble() < 0.6)
            {
                var mote = GlowQuad(seg, "Mote", flareMask, cropGold, false);
                mote.transform.localPosition = new Vector3(s * Rand(4f, 9f), Rand(1f, 3.5f), Rand(-3.5f, 3.5f));
                mote.transform.localScale = Vector3.one * 0.3f;
                glows.Add(new Glow { r = mote, color = cropGold, baseI = 0.3f, kickI = 0.8f, face = true });
            }
            for (int k = 0; k < fogPerSegment; k++)
            {
                var sm = GlowQuad(seg, "GroundFog", smokeTexture, k == 0 ? lanternColors[0] : moonBlue, false);
                float y = 0.7f + (float)rng.NextDouble() * 0.8f;
                sm.transform.localPosition = new Vector3(s * Rand(6f, 16f), y, Rand(-4f, 4f));
                sm.transform.localScale = new Vector3(Rand(7f, 10f), Rand(2.2f, 3.2f), 1f);
                fogs.Add(new Fog { t = sm.transform, r = sm, seed = index * 1.7f + k * 3.1f + s, y = y });
            }
        }
        if (index == 0) BuildBirds();
    }

    private void Golden(Transform t)
    {
        if (cropMat == null)
        {
            cropMat = Lit(cropGold, 0.2f);
            cropMat.EnableKeyword("_EMISSION");
            cropMat.SetColor("_EmissionColor", cropGold * 0.3f);
            RegisterEmissive(cropMat, cropGold * 0.3f);
        }
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string n = mats[i] != null ? mats[i].name.ToLowerInvariant() : "";
                if (n.Contains("grass") || n.Contains("corn") || n.Contains("wood") || n.Contains("default") || n.Contains("leaf")) mats[i] = cropMat;
            }
            r.sharedMaterials = mats;
        }
    }

    private void Windmill(Transform seg, Vector3 at, int side)
    {
        var mill = new GameObject("Windmill").transform;
        mill.SetParent(seg, false);
        mill.localPosition = at;
        for (int i = 0; i < 5; i++)
        {
            float w = Mathf.Lerp(3.2f, 1.8f, i / 4f);
            Box(mill, new Vector3(0f, 1.2f + i * 2.4f, 0f), new Vector3(w, 2.4f, w), sailMat);
        }
        Box(mill, new Vector3(0f, 12.9f, 0f), new Vector3(2.4f, 1.2f, 2.8f), woodMat);
        var hub = new GameObject("Sails").transform;
        hub.SetParent(mill, false);
        hub.localPosition = new Vector3(0f, 12.4f, -1.6f);
        for (int b = 0; b < 4; b++)
        {
            var arm = new GameObject("Sail").transform;
            arm.SetParent(hub, false);
            arm.localRotation = Quaternion.Euler(0f, 0f, b * 90f);
            Box(arm, new Vector3(0f, 3.6f, 0f), new Vector3(0.25f, 7.2f, 0.15f), woodMat);
            Box(arm, new Vector3(0.7f, 4.2f, 0.05f), new Vector3(1.2f, 5.6f, 0.06f), sailMat);
        }
        sails.Add(hub);
    }

    private void BuildBirds()
    {
        birdTex = BirdTexture();
        birdMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        birdMat.SetTexture("_BaseMap", birdTex);
        birdMat.SetColor("_BaseColor", new Color(0.08f, 0.04f, 0.06f, 1f));
        birdMat.SetFloat("_Surface", 1f);
        birdMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        birdMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        birdMat.SetFloat("_ZWrite", 0f);
        birdMat.SetFloat("_Cull", 0f);
        birdMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        birdMat.renderQueue = (int)RenderQueue.Transparent;
        for (int i = 0; i < birdCount; i++)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "Bird";
            q.transform.SetParent(transform, false);
            q.GetComponent<MeshRenderer>().sharedMaterial = birdMat;
            q.SetActive(false);
            float side = i % 2 == 0 ? -1f : 1f;
            birds.Add(new Bird
            {
                t = q.transform,
                start = new Vector3(side * Rand(6f, 16f), Rand(0.8f, 2f), Rand(12f, 40f)),
                dir = new Vector3(-side * Rand(0.1f, 0.4f), Rand(0.35f, 0.6f), 1f).normalized,
                delay = Rand(0f, 3f), speed = Rand(7f, 11f), flap = Rand(0f, 1f), size = Rand(0.5f, 0.9f),
            });
        }
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (cam == null) return;
        Vector3 camPos = cam.transform.position;
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float drop = Mathf.Clamp01(energy - 1f) * 1.6f;
        float t = ClubBeat.SongTime;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        int chase = Mathf.FloorToInt(beats * 2f);
        foreach (var l in lanterns)
        {
            // Swing a little, flare on the beat, a chase running along them on the eighths.
            l.t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.6f + l.seed) * 6f + kick * energy * 4f);
            Face(l.glow.transform, camPos);
            bool lead = (l.index + chase) % 3 == 0;
            SetGlow(l.glow, null, (lead ? 0.9f : 0.35f) + 0.9f * kick * energy * (1f + drop));
        }
        foreach (var c in cropSway)
            c.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.2f + c.localPosition.x * 0.3f + c.localPosition.z) * 5f + kick * energy * 3f, c.localEulerAngles.y, 0f);
        // The fields breathe with the beat: the sun flares, the haze lifts on every beat.
        if (sky != null) sky.SetFloat("_MoonPulse", Mathf.Clamp01(0.25f + 0.75f * kick * energy));
        foreach (var f in fogs) SetGlow(f.r, null, 0.06f + 0.22f * kick * energy * (1f + drop));
        sailAngle += Time.deltaTime * (35f + 50f * energy * (1f + drop) + 60f * kick * energy);
        foreach (var h in sails) h.localRotation = Quaternion.Euler(0f, 0f, sailAngle);

        // The flock: once, in the segment's last seconds, off the fields and away into the sunset.
        var playlist = PlaylistDirector.Active != null ? PlaylistDirector.Active.Timeline : null;
        if (!flockStarted && playlist != null)
        {
            var span = playlist.spans[playlist.SegmentAt(t)];
            if (t > span.audioEnd - birdsBeforeEnd && t < span.audioEnd) { flockStarted = true; flockStart = t; }
        }
        if (flockStarted)
            foreach (var b in birds)
            {
                float since = t - flockStart - b.delay;
                if (since < 0f) continue;
                if (!b.t.gameObject.activeSelf) b.t.gameObject.SetActive(true);
                b.t.localPosition = b.start + b.dir * b.speed * since + new Vector3(0f, Mathf.Sin(since * 2f + b.flap * 6f) * 0.3f, -scrollSpeed * 0.2f * since);
                b.t.rotation = Quaternion.LookRotation(b.t.position - camPos);
                float w = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin((since * 3f + b.flap) * Mathf.PI));
                b.t.localScale = new Vector3(b.size * 1.8f, b.size * 0.9f * w, 1f);
            }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        foreach (var o in new Object[] { woodMat, sailMat, birdMat, cropMat, birdTex }) if (o != null) Destroy(o);
    }

    private static Material Lit(Color c, float smooth)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        return m;
    }

    private static void Box(Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(b.GetComponent<Collider>());
        b.transform.SetParent(parent, false);
        b.transform.localPosition = pos;
        b.transform.localScale = size;
        var r = b.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    /// A bird in silhouette, the gull's shallow "M": alpha only.
    private static Texture2D BirdTexture()
    {
        const int w = 64, h = 32;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                float au = Mathf.Abs(u);
                float wing = 0.15f + 0.55f * Mathf.Sin(Mathf.Min(1f, au / 0.55f) * Mathf.PI * 0.5f) - 0.5f * Mathf.Max(0f, au - 0.55f);
                bool on = Mathf.Abs(v - wing) < 0.14f * (1f - au * 0.6f) && au < 0.95f || (au < 0.08f && v > -0.1f && v < 0.3f);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(on ? 255 : 0));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }
}
