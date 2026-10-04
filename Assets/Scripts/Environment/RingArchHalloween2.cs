using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// The video 8 ("Halloween 2") ring, one shape per skin, standing over the road (feet at about
/// |x| = 6.6 m, nothing inside that, so the notes read through it):
///
///   * WitchSwamp - a crooked branch arch: two gnarled boughs grow from the feet and meet in a
///     point, thorns sticking out of them, glowing drops of brew hanging from threads. A wave of
///     poison green climbs from both feet to the point every bar; the drops swell on the beat.
///   * MadLab - a tesla coil ring: a half ring of metal wound with a copper helix, an electrode
///     ball on each foot. A spark runs round the helix (a half beat a step), and on the strong
///     beats (every beat in a drop) the whole coil discharges white-blue.
///   * VampireCastle - a gothic lancet: two pointed arches, one inside the other, gold cusps hanging
///     from the inner one, a crimson rose under the point and a gold keystone. Light rises from
///     the feet to the point every bar; the keystone and the rose flash on the strong beats.
///
/// All of it is SongTweens, positioned from ClubBeat, so it is on the music in Play and in a recording.
public class RingArchHalloween2 : MonoBehaviour, IThemeAccent
{
    [SerializeField] private Halloween2Skin skin;
    [SerializeField] private Color color = new Color(0.6f, 0.1f, 1f);
    [SerializeField] private Color secondColor = new Color(0.4f, 1f, 0.1f);
    [SerializeField] private Color thirdColor = Color.white;
    [SerializeField, Range(0f, RemixGeo.MaxEmission)] private float emissionIntensity = 1.3f;

    // One lit piece: where it sits along the arch (0 at a foot, 1 at the point) and which colour it takes.
    private struct Piece { public MeshRenderer r; public float u; public int color; }
    private readonly List<Piece> pieces = new List<Piece>();
    private readonly List<Transform> swells = new List<Transform>();
    private readonly List<Vector3> swellScales = new List<Vector3>();
    private Material mat;
    private MaterialPropertyBlock mpb;
    private readonly SongTweens tweens = new SongTweens();
    private float kick, flash, sway;
    private Transform pivot;
    private Mesh cube, sphere;
    private readonly System.Random rng = new System.Random(13);

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        mat = RemixGeo.Neon(color, emissionIntensity);
        cube = RemixGeo.Primitive(PrimitiveType.Cube);
        sphere = RemixGeo.Primitive(PrimitiveType.Sphere);
        pivot = new GameObject("Arch").transform;
        pivot.SetParent(transform, false);
        switch (skin)
        {
            case Halloween2Skin.WitchSwamp: BuildBranches(); break;
            case Halloween2Skin.MadLab: BuildCoil(); break;
            case Halloween2Skin.VampireCastle: BuildLancet(); break;
        }
        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutExpo), 1f);
        tweens.Loop(DOTween.Sequence().Append(DOTween.To(x => flash = x, 1f, 0f, 0.45f).SetEase(Ease.OutQuad)).AppendInterval(0.55f), 1f);
        tweens.Loop(DOTween.To(x => sway = x, -1f, 1f, 4f).SetEase(Ease.InOutSine).SetLoops(2, LoopType.Yoyo), 8f);
    }

    private void OnDestroy()
    {
        tweens.KillAll();
        if (mat != null) Destroy(mat);
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        // Rings alternate the theme's two accents; here the accent only says which colour leads.
        if (newColor != color) { var t = color; color = secondColor; secondColor = t; }
        emissionIntensity = Mathf.Min(newIntensity, 1.4f);
    }

    private float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    private MeshRenderer Add(Mesh mesh, Vector3 a, Vector3 b, float thickness, float u, int c)
    {
        var r = RemixGeo.Beam(pivot, "Piece", mesh, mat, a, b, thickness);
        pieces.Add(new Piece { r = r, u = u, color = c });
        return r;
    }

    private void Swell(MeshRenderer r)
    {
        swells.Add(r.transform);
        swellScales.Add(r.transform.localScale);
    }

    // Two boughs from the feet to the point, each bent by a couple of slow waves and a little noise.
    private void BuildBranches()
    {
        const float foot = 6.7f, top = 8.6f;
        const int steps = 14;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 prev = new Vector3(side * foot, -0.3f, 0f);
            for (int i = 1; i <= steps; i++)
            {
                float u = i / (float)steps;
                // From the foot up and in, an ogee-ish bend, wobbling like an old branch.
                float x = side * (foot * Mathf.Cos(u * Mathf.PI * 0.5f) + 0.45f * Mathf.Sin(u * 9f + side));
                float y = top * Mathf.Sin(u * Mathf.PI * 0.5f) + 0.3f * Mathf.Sin(u * 13f);
                if (i == steps) { x = 0f; y = top + 0.4f; }
                var p = new Vector3(x, y, Rand(-0.15f, 0.15f));
                Add(cube, prev, p, 0.3f - 0.17f * u, u, 0);
                // Thorns: short spikes out of the outer side, every other joint.
                if (i % 2 == 0 && i < steps)
                {
                    var outward = new Vector3(p.x, p.y - 2f, 0f).normalized;
                    Add(cube, p, p + outward * Rand(0.45f, 0.8f) + Vector3.up * 0.2f, 0.06f, u, 1);
                }
                // Twigs curling up off the bough near the feet and halfway.
                if (i == 4 || i == 8)
                {
                    var t1 = p + new Vector3(side * 0.9f, 0.7f, 0f);
                    Add(cube, p, t1, 0.08f, u, 0);
                    Add(cube, t1, t1 + new Vector3(side * 0.2f, 0.6f, 0f), 0.05f, u, 0);
                }
                // Drops of brew hanging inside the arch.
                if (i == 6 || i == 10)
                {
                    float hang = Rand(0.9f, 1.6f);
                    Add(cube, p, p + Vector3.down * hang, 0.025f, u, 1);
                    var drop = Add(sphere, p + Vector3.down * (hang + 0.2f), p + Vector3.down * (hang + 0.2f), 0.38f, u, 1);
                    Swell(drop);
                }
                prev = p;
            }
        }
        // The knot where the boughs meet.
        Swell(Add(sphere, new Vector3(0f, top + 0.4f, 0f), new Vector3(0f, top + 0.4f, 0f), 0.6f, 1f, 2));
    }

    // A half ring of metal (two rails) wound with a helix, an electrode ball on each foot.
    private void BuildCoil()
    {
        const float r = 7.1f, tube = 0.45f;
        const int along = 60, per = 3;           // helix turns: one every `per` steps round the half ring
        Vector3 At(float deg, float rad) => new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad) * rad, Mathf.Sin(deg * Mathf.Deg2Rad) * rad - 0.3f, 0f);
        for (int i = 0; i < 20; i++)
        {
            float a = 180f * i / 20, b = 180f * (i + 1) / 20;
            float u = 1f - Mathf.Abs(i + 0.5f - 10f) / 10f;
            Add(cube, At(a, r - tube * 0.6f), At(b, r - tube * 0.6f), 0.1f, u, 0);
            Add(cube, At(a, r + tube * 0.6f), At(b, r + tube * 0.6f), 0.1f, u, 0);
        }
        Vector3 prev = Vector3.zero;
        for (int i = 0; i <= along * per; i++)
        {
            float deg = 180f * i / (along * per);
            float phase = i * Mathf.PI * 2f / per;
            Vector3 radial = new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad), 0f);
            var p = At(deg, r) + (radial * Mathf.Cos(phase) + Vector3.forward * Mathf.Sin(phase)) * tube;
            // u here is the position round the ring, 0 at the right foot, 1 at the left: the spark's path.
            if (i > 0) Add(cube, prev, p, 0.07f, deg / 180f, 1);
            prev = p;
        }
        foreach (float deg in new[] { 0f, 180f })
        {
            var c = At(deg, r) + Vector3.down * 0.0f;
            Add(cube, c + Vector3.down * 0.4f, c + Vector3.up * 0.2f, 0.5f, deg / 180f, 0);
            Swell(Add(sphere, c + Vector3.up * 0.55f, c + Vector3.up * 0.55f, 0.75f, deg / 180f, 2));
        }
    }

    // Two pointed arches, one inside the other: each side a circular arc centred on the far side of
    // the centre line, so the two arcs meet in a point.
    private void BuildLancet()
    {
        const float jamb = 1.4f, radius = 8.3f, cx = 1.7f;   // outer foot at x = cx - radius = -6.6
        float apexOut = jamb + Mathf.Sqrt(radius * radius - cx * cx);
        const int steps = 10;
        foreach (float rad in new[] { radius, radius - 0.85f })
        {
            bool outer = rad == radius;
            float apex = jamb + Mathf.Sqrt(rad * rad - cx * cx);
            for (int side = -1; side <= 1; side += 2)
            {
                // Left arc: centre (cx, jamb), from angle 180 to the apex angle.
                float a0 = 180f, a1 = Mathf.Rad2Deg * Mathf.Acos(-cx / rad);
                Vector3 Arc(float deg) => new Vector3(side * -(cx + Mathf.Cos(deg * Mathf.Deg2Rad) * rad), jamb + Mathf.Sin(deg * Mathf.Deg2Rad) * rad, 0f);
                Vector3 baseP = new Vector3(side * (rad - cx), -0.3f, 0f);
                Add(cube, baseP, Arc(a0), outer ? 0.28f : 0.14f, 0f, outer ? 0 : 1);
                Vector3 prev = Arc(a0);
                for (int i = 1; i <= steps; i++)
                {
                    float u = i / (float)steps;
                    var p = i == steps ? new Vector3(0f, apex, 0f) : Arc(Mathf.Lerp(a0, a1, u));
                    Add(cube, prev, p, outer ? 0.26f : 0.13f, u, outer ? 0 : 1);
                    // Cusps: little gold points hanging in from the inner arch.
                    if (!outer && i % 2 == 1 && i < steps - 1)
                    {
                        var mid = (prev + p) * 0.5f;
                        var inward = (new Vector3(0f, jamb + 1f, 0f) - mid).normalized;
                        Add(cube, mid, mid + inward * 0.55f, 0.07f, u, 1);
                        Add(sphere, mid + inward * 0.6f, mid + inward * 0.6f, 0.16f, u, 1);
                    }
                    prev = p;
                }
                // Pinnacles on the outer feet.
                if (outer)
                {
                    var f = Arc(a0);
                    Add(cube, f, f + Vector3.up * 1.6f + new Vector3(side * 0.6f, 0f, 0f), 0.12f, 0.1f, 1);
                }
            }
        }
        // The rose under the point: a ring of petals round a heart, and the keystone gem on top.
        var centre = new Vector3(0f, apexOut - 2.4f, 0f);
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.PI * 2f / 8f, b = (k + 1) * Mathf.PI * 2f / 8f;
            Add(cube, centre + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.85f, centre + new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0f) * 0.85f, 0.08f, 0.95f, 1);
            Add(cube, centre, centre + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.85f, 0.05f, 0.95f, 2);
        }
        Swell(Add(sphere, centre, centre, 0.5f, 1f, 2));
        Swell(Add(sphere, new Vector3(0f, apexOut + 0.35f, 0f), new Vector3(0f, apexOut + 0.35f, 0f), 0.55f, 1f, 1));
    }

    private Color ColorOf(int c) => c == 0 ? color : (c == 1 ? secondColor : thirdColor);

    private void Update()
    {
        if (pieces.Count == 0) return;
        float beats = CarnivalLook.Beats;
        tweens.SeekLoops(beats);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        bool strike = CarnivalLook.InDrop || CarnivalLook.StrongBeat;
        float bar = CarnivalLook.BarPosition;
        float baseLevel = emissionIntensity * (0.4f + 0.35f * kick * energy);

        foreach (var p in pieces)
        {
            float level = baseLevel;
            Color c = ColorOf(p.color);
            switch (skin)
            {
                case Halloween2Skin.WitchSwamp:
                    // A wave climbing from the feet to the point over the bar.
                    level += emissionIntensity * 1.1f * Mathf.Exp(-Mathf.Pow((p.u - bar * 1.2f) / 0.12f, 2f)) * Mathf.Min(energy + 0.3f, 1.3f);
                    break;
                case Halloween2Skin.MadLab:
                    if (p.color == 1)
                    {
                        // The spark: round the helix and back, a half beat a step of 1/8 of the ring.
                        float pos = Mathf.PingPong(beats / 4f, 1f);
                        level += emissionIntensity * 1.3f * Mathf.Exp(-Mathf.Pow((p.u - pos) / 0.05f, 2f));
                        if (strike) { level += emissionIntensity * 1.2f * flash; c = Color.Lerp(c, Color.white, 0.6f * flash); }
                    }
                    break;
                case Halloween2Skin.VampireCastle:
                    level += emissionIntensity * 0.9f * Mathf.Exp(-Mathf.Pow((p.u - bar * 1.15f) / 0.15f, 2f)) * Mathf.Min(energy + 0.3f, 1.3f);
                    if (p.u >= 0.95f && strike) level += emissionIntensity * 1.2f * flash;
                    break;
            }
            mpb.SetColor(RemixGeo.EmissionId, c * Mathf.Min(level, RemixGeo.MaxEmission));
            p.r.SetPropertyBlock(mpb);
        }

        // Drops, electrodes, rose and keystone swell on the beat.
        for (int i = 0; i < swells.Count; i++)
            swells[i].localScale = swellScales[i] * (1f + 0.3f * kick * Mathf.Min(energy, 1.3f) + (strike ? 0.2f * flash : 0f));

        // The branch arch creaks a little; the others stand still.
        pivot.localRotation = skin == Halloween2Skin.WitchSwamp ? Quaternion.Euler(0f, 0f, sway * 1.2f) : Quaternion.identity;
    }
}
