using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_HalloweenHaunted's environment, "Haunted house": the road runs down the long gallery of a
/// gothic mansion. On the graveyard's machinery (GraveyardEnvironmentDecorator: night props, every
/// flame and emissive kicking on the beat, fog, bats) it builds, travelling with the road:
///
///   * the gallery's walls, dark violet, with tall pointed windows - lightning flashes behind them
///     (and across the sky) on the strong beats, on every bar through a drop;
///   * torn curtains either side of each window, swaying over the bar;
///   * portraits between the windows whose eyes glow poison green and flare on every beat;
///   * candelabra with ghostly green flames and a real green light, tables with evil books and
///     candles, coffins and a sarcophagus against the walls, cobwebs in the corners;
///   * ghosts (the Kenney kit's) floating translucent down the gallery toward the camera, jerking
///     on every beat and darting on a drop;
///   * low fog over the floor, violet and green.
///
/// The phonk segment: everything hits harder than in the other two (larger kick amounts), all of
/// it on the song clock and capped below white.
public class HauntedEnvironmentDecorator : GraveyardEnvironmentDecorator
{
    [Header("Mansion models")]
    [SerializeField] private GameObject candelabrum;
    [SerializeField] private GameObject pedestal;
    [SerializeField] private GameObject[] books;
    [SerializeField] private GameObject[] coffins;
    [SerializeField] private GameObject[] cobwebs;
    [SerializeField] private GameObject ghostModel;

    [Header("Mansion colours and layout")]
    [SerializeField] private Color violet = new Color(0.55f, 0.2f, 1f);
    [SerializeField] private Color ghostGreen = new Color(0.35f, 1f, 0.45f);
    [SerializeField] private Color lightning = new Color(0.7f, 0.8f, 1f);
    [SerializeField] private Color wallColor = new Color(0.045f, 0.03f, 0.065f);
    [SerializeField] private float wallX = 7.4f;
    [SerializeField] private float wallHeight = 9f;
    [SerializeField] private int ghostCount = 12;

    private Material wallMat, frameMat, curtainMat, pictureMat, ghostMat;
    private Texture2D windowTex, curtainTex, pictureTex;
    private readonly List<MeshRenderer> windows = new List<MeshRenderer>();
    private struct Curtain { public Transform t; public float seed; public Quaternion rest; }
    private readonly List<Curtain> curtains = new List<Curtain>();
    private readonly List<MeshRenderer> eyes = new List<MeshRenderer>();
    private class Ghost { public Transform t; public Vector3 home; public float seed, z; }
    private readonly List<Ghost> ghosts = new List<Ghost>();
    private float lastStrike = -100f;
    private int lastStrikeBeat = int.MinValue;
    private static readonly int FlashId = Shader.PropertyToID("_Flash");

    protected override void BuildSegment(Transform stage, int index, float z)
    {
        EnsureMaterials();
        var seg = new GameObject("Segment_" + index.ToString("00")).transform;
        seg.SetParent(stage, false);
        seg.localPosition = new Vector3(0f, 0f, z);
        RegisterSegment(seg);
        float face = wallX - 0.31f;

        for (int s = -1; s <= 1; s += 2)
        {
            Box(seg, new Vector3(s * wallX, wallHeight * 0.5f, 0f), new Vector3(0.6f, wallHeight, segmentSpacing + 0.02f), wallMat);
            // A cornice along the top and a skirting at the floor, to give the wall its lines.
            Box(seg, new Vector3(s * (wallX - 0.35f), wallHeight - 0.4f, 0f), new Vector3(0.3f, 0.35f, segmentSpacing), frameMat);
            Box(seg, new Vector3(s * (wallX - 0.33f), 0.2f, 0f), new Vector3(0.25f, 0.4f, segmentSpacing), frameMat);
            Quaternion toRoad = Quaternion.Euler(0f, s > 0 ? 90f : -90f, 0f);

            if (index % 2 == 0)
            {
                // A tall pointed window: the glass is lit from outside, by the lightning.
                var w = Quad(seg, "Window", new Vector3(s * face, 4.4f, 0f), toRoad, new Vector2(2.6f, 5.6f), null);
                var m = new Material(glowTemplate);
                m.SetTexture("_MainTex", windowTex);
                m.SetFloat("_UseAlpha", 0f);
                w.sharedMaterial = m;
                SetGlow(w, lightning, 0.1f);
                windows.Add(w);
                for (int c = -1; c <= 1; c += 2)
                {
                    var cu = Quad(seg, "Curtain", new Vector3(s * (face - 0.05f), 4.6f, c * 1.75f), toRoad, new Vector2(1.1f, 6.2f), curtainMat);
                    curtains.Add(new Curtain { t = cu.transform, seed = index * 0.9f + c + s * 0.3f, rest = cu.transform.localRotation });
                }
            }
            else
            {
                // A portrait whose eyes glow and flare on the beat.
                Box(seg, new Vector3(s * (face + 0.02f), 4f, 0f), new Vector3(0.12f, 2.4f, 1.9f), frameMat);
                Quad(seg, "Portrait", new Vector3(s * (face - 0.06f), 4f, 0f), toRoad, new Vector2(1.6f, 2.1f), pictureMat);
                for (int e = -1; e <= 1; e += 2)
                {
                    var eye = GlowQuad(seg, "Eye", flareMask, ghostGreen, false);
                    eye.transform.localPosition = new Vector3(s * (face - 0.1f), 4.35f, e * 0.17f * -s);
                    eye.transform.localRotation = toRoad;
                    eye.transform.localScale = Vector3.one * 0.38f;
                    eyes.Add(eye);
                }
            }

            // Candelabra on pedestals, alternating sides: green flames and a green light.
            if ((index % 2 == 0) == (s > 0))
            {
                Vector3 at = new Vector3(s * (wallX - 1.4f), 0f, segmentSpacing * 0.5f - 0.5f);
                Prop(seg, pedestal, at, 0f, 1.7f);
                var cb = Prop(seg, candelabrum, at + new Vector3(0f, 1.9f, 0f), 0f, 3.4f);
                if (cb != null)
                {
                    for (int f = -1; f <= 1; f++)
                    {
                        var fl = GlowQuad(seg, "CandleFlame", flameTexture, ghostGreen, false);
                        fl.transform.localPosition = at + new Vector3(f * 0.3f, 2.75f, 0f);
                        fl.transform.localScale = Vector3.one * 0.5f;
                        glows.Add(new Glow { r = fl, color = ghostGreen, baseI = 0.9f, kickI = 1.3f, face = true });
                        swells.Add(new Swell { t = fl.transform, rest = fl.transform.localScale, amount = 0.6f });
                    }
                    var go = new GameObject("CandleLight");
                    go.transform.SetParent(seg, false);
                    go.transform.localPosition = at + new Vector3(0f, 2.9f, 0f);
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = ghostGreen;
                    l.range = lampLightRange;
                    l.intensity = lampLightIntensity;
                    l.shadows = LightShadows.None;
                    lampLights.Add(new LampLight { light = l });
                }
            }

            // A table with evil books and candles, every third segment.
            if ((index + (s > 0 ? 1 : 0)) % 3 == 0)
            {
                Vector3 at = new Vector3(s * (wallX - 1.2f), 0f, -2f);
                Box(seg, at + new Vector3(0f, 0.85f, 0f), new Vector3(1.1f, 0.12f, 2.2f), frameMat);
                for (int leg = 0; leg < 4; leg++)
                    Box(seg, at + new Vector3((leg % 2 - 0.5f) * 0.9f, 0.42f, (leg / 2 - 0.5f) * 2f), new Vector3(0.1f, 0.84f, 0.1f), frameMat);
                for (int b = 0; b < 2; b++) Prop(seg, Pick(books), at + new Vector3(Rand(-0.2f, 0.2f), 0.92f, -0.5f + b * 0.8f), Rand(0f, 360f), 2.6f);
                var cn = Prop(seg, Pick(candles), at + new Vector3(0f, 0.92f, 0.25f), 0f, 2.6f);
                if (cn != null)
                {
                    var fl = GlowQuad(seg, "CandleFlame", flameTexture, flameOrange, false);
                    fl.transform.localPosition = at + new Vector3(0f, 1.75f, 0.25f);
                    fl.transform.localScale = Vector3.one * 0.45f;
                    glows.Add(new Glow { r = fl, color = flameOrange, baseI = 0.8f, kickI = 1.2f, face = true });
                    swells.Add(new Swell { t = fl.transform, rest = fl.transform.localScale, amount = 0.6f });
                }
            }

            // Coffins against the walls.
            if ((index + (s > 0 ? 2 : 0)) % 4 == 1)
                Prop(seg, Pick(coffins), new Vector3(s * (wallX - 1.3f), 0f, 2f), 0f, 1.8f);

            // Cobwebs in the top corners.
            if (index % 2 == 1)
            {
                var web = Prop(seg, Pick(cobwebs), new Vector3(s * (wallX - 0.9f), wallHeight - 1.2f, 0f), s > 0 ? -90f : 90f, 2.2f);
                if (web != null) web.localRotation = Quaternion.Euler(0f, s > 0 ? 90f : -90f, 0f);
            }

            // Low fog over the floor, violet with a green one near the candles.
            for (int k = 0; k < fogPerSegment; k++)
            {
                var sm = GlowQuad(seg, "GroundFog", smokeTexture, k == 0 ? ghostGreen : violet, false);
                float y = 0.4f + (float)rng.NextDouble() * 0.5f;
                sm.transform.localPosition = new Vector3(s * Rand(3.4f, wallX - 0.6f), y, Rand(-4f, 4f));
                sm.transform.localScale = new Vector3(Rand(5f, 7f), Rand(1.8f, 2.6f), 1f);
                fogs.Add(new Fog { t = sm.transform, r = sm, seed = index * 1.7f + k * 3.1f + s, y = y });
            }
        }
        if (index == 0) BuildGhosts();
    }

    private void BuildGhosts()
    {
        if (ghostModel == null) return;
        for (int i = 0; i < ghostCount; i++)
        {
            var g = Instantiate(ghostModel, transform);
            g.name = "Ghost";
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = ghostMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            float side = i % 2 == 0 ? -1f : 1f;
            var gh = new Ghost
            {
                t = g.transform, seed = i * 1.37f, z = 12f + i * 11f,
                home = new Vector3(side * Rand(3.6f, 5.6f), Rand(2.6f, 5.5f), 0f),
            };
            g.transform.localScale = Vector3.one * Rand(2.2f, 3.2f);
            ghosts.Add(gh);
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

        // Lightning: on beat 1 of every other bar, and every bar through a drop (plus beat 3 when
        // the drop is at its hardest). A strike is two strokes and fades in a quarter of a second.
        int beat = ClubBeat.BeatIndex;
        if (beat != lastStrikeBeat)
        {
            lastStrikeBeat = beat;
            int inBar = ((beat % 4) + 4) % 4;
            int bar = Mathf.FloorToInt(beat / 4f);
            bool strike = inBar == 0 && (drop > 0.2f || bar % 2 == 0) || (inBar == 2 && drop > 0.8f);
            if (strike && energy > 0.5f) lastStrike = t - ClubBeat.BeatPhase * ClubBeat.SecondsPerBeat;
        }
        float since = t - lastStrike;
        float flash = since < 0f ? 0f : Mathf.Exp(-since / 0.09f) + 0.6f * (since > 0.13f ? Mathf.Exp(-(since - 0.13f) / 0.12f) : 0f);
        flash = Mathf.Clamp01(flash);
        foreach (var w in windows) SetGlow(w, null, 0.12f + 0.2f * kick * energy + flash * 1.6f);

        // The phonk hits harder than the other two segments: the floor fog and the candelabra's
        // green light slam on every beat (overriding the base's gentler values), and the fog
        // catches the lightning too.
        float slam = kick * energy * (1f + drop);
        foreach (var f in fogs) SetGlow(f.r, null, 0.06f + 0.34f * slam + 0.25f * flash);
        foreach (var l in lampLights) l.light.intensity = lampLightIntensity * (0.35f + 1.1f * slam);
        if (sky != null) sky.SetFloat(FlashId, flash * 0.55f);

        // Curtains sway over the bar; portraits' eyes flare on every beat.
        foreach (var c in curtains)
            c.t.localRotation = c.rest * Quaternion.Euler(Mathf.Sin(t * 1.3f + c.seed) * 4f + bar * 3f, 0f, 0f);
        foreach (var e in eyes) SetGlow(e, null, 0.5f + 1.5f * kick * energy * (1f + 0.5f * drop));

        // Ghosts drift toward the camera slower than the road, bob, and jerk on every beat.
        float dz = scrollSpeed * 0.35f * Time.deltaTime * (1f + drop);
        foreach (var g in ghosts)
        {
            g.z -= dz;
            if (g.z < -4f) g.z += tunnelLength;
            float jerk = kick * energy * (1f + drop);
            Vector3 p = g.home + new Vector3(Mathf.Sin(t * 0.9f + g.seed) * 0.6f + Mathf.Sin(g.seed * 13f + beat) * 0.35f * jerk,
                                             Mathf.Sin(t * 1.7f + g.seed) * 0.4f + 0.25f * jerk, g.z);
            g.t.localPosition = p;
            g.t.rotation = Quaternion.LookRotation(camPos - g.t.position) * Quaternion.Euler(0f, 0f, Mathf.Sin(g.seed * 7f + beat) * 12f * jerk);
        }
        if (ghostMat != null) ghostMat.SetColor("_BaseColor", new Color(0.55f, 1f, 0.7f, Mathf.Clamp01(0.28f + 0.3f * kick * energy + 0.3f * flash)));
    }

    private void EnsureMaterials()
    {
        if (wallMat != null) return;
        wallMat = Lit(wallColor, 0.3f);
        frameMat = Lit(new Color(0.09f, 0.06f, 0.05f), 0.4f);
        windowTex = WindowTexture();
        curtainTex = CurtainTexture();
        pictureTex = PictureTexture();
        curtainMat = Transparent(curtainTex, new Color(0.2f, 0.03f, 0.14f, 1f));
        pictureMat = Transparent(pictureTex, Color.white);
        ghostMat = Transparent(null, new Color(0.55f, 1f, 0.7f, 0.35f));
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        foreach (var o in new Object[] { wallMat, frameMat, curtainMat, pictureMat, ghostMat, windowTex, curtainTex, pictureTex })
            if (o != null) Destroy(o);
        foreach (var w in windows) if (w != null) Destroy(w.sharedMaterial);
    }

    // --- building helpers ---

    private static Material Lit(Color c, float smooth)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        return m;
    }

    private static Material Transparent(Texture2D tex, Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (tex != null) m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
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

    private static MeshRenderer Quad(Transform parent, string name, Vector3 pos, Quaternion rot, Vector2 size, Material mat)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = name;
        q.transform.SetParent(parent, false);
        q.transform.localPosition = pos;
        q.transform.localRotation = rot;
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = q.GetComponent<MeshRenderer>();
        if (mat != null) r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return r;
    }

    /// A tall pointed window: light glass (red channel) in a black frame, split by two mullions
    /// and a transom, with a round light in its head.
    private static Texture2D WindowTexture()
    {
        const int w = 64, h = 128;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h;
                float half = 0.86f;
                // Pointed head from v = 0.62 up: the half-width narrows along two arcs.
                if (v > 0.62f) half *= Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((v - 0.62f) / 0.34f, 2f))) * 0.6f + 0.4f * (1f - (v - 0.62f) / 0.38f);
                bool glass = Mathf.Abs(u) < half && v > 0.04f && v < 0.97f;
                bool lead = Mathf.Abs(u) < 0.05f || Mathf.Abs(Mathf.Abs(u) - 0.45f) < 0.04f || Mathf.Abs(v - 0.5f) < 0.015f;
                float rose = Mathf.Abs(new Vector2(u, (v - 0.78f) * 2f).magnitude - 0.28f);
                if (v > 0.62f && rose < 0.04f) lead = true;
                float edge = glass ? Mathf.Clamp01((half - Mathf.Abs(u)) * 12f) : 0f;
                float c = glass && !lead ? (0.55f + 0.45f * v) * edge : 0f;
                byte b = (byte)Mathf.RoundToInt(c * 255f);
                px[y * w + x] = new Color32(b, b, b, 255);
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    /// A curtain hanging in folds, its bottom edge torn: alpha only.
    private static Texture2D CurtainTexture()
    {
        const int w = 32, h = 128;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        var rng = new System.Random(5);
        var tear = new float[w];
        for (int x = 0; x < w; x++) tear[x] = 0.05f + (float)rng.NextDouble() * 0.28f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                float fold = 0.65f + 0.35f * Mathf.Sin(u * Mathf.PI * 6f);
                bool cloth = v > tear[x] && !(v < 0.5f && (int)(u * 7f + v * 3f) % 5 == 0 && rng.NextDouble() < 0.02);
                byte g = (byte)Mathf.RoundToInt(fold * 255f);
                px[y * w + x] = new Color32(g, g, g, (byte)(cloth ? 235 : 0));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    /// A dark old portrait: a gloomy ground and a pale, faceless head and shoulders; the eyes are
    /// added as separate lights.
    private static Texture2D PictureTexture()
    {
        const int w = 64, h = 84;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h;
                Color c = Color.Lerp(new Color(0.03f, 0.05f, 0.04f), new Color(0.08f, 0.05f, 0.1f), v);
                float head = (u * u) / 0.07f + Mathf.Pow(v - 0.64f, 2f) / 0.022f;
                float shoulders = (u * u) / 0.5f + Mathf.Pow(v - 0.12f, 2f) / 0.08f;
                if (head < 1f) c = Color.Lerp(new Color(0.35f, 0.33f, 0.3f), c, head * head);
                else if (shoulders < 1f && v < 0.4f) c = new Color(0.06f, 0.03f, 0.08f);
                px[y * w + x] = c;
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }
}
