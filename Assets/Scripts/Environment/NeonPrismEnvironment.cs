using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Theme NeonPrism's scenery (all four skins; the prefab's `skin` picks the accent):
///
///   * a laser-grid floor: long lines along the road and a ladder of cross lines flowing toward the
///     camera, flaring on the beat;
///   * neon figures floating at the sides - wire cubes, pyramids and rings - flowing past, turning
///     with the music (a fixed angle per beat) and swelling on each kick, in four groups that take
///     the accent in turn;
///   * RedLaser: a fan of laser beams from the far end, flaring on the strong beats (every beat in a drop);
///   * Ultraviolet: glowing dots drifting through the room, twinkling (the dust is the theme's air particles);
///   * CyanPulse: the bar's wave of light runs over the floor's cross lines (and the rings, see RingArchPrism);
///   * Spectrum: every colour comes from PrismLook's phrase hue;
///   * every skin: a global Volume that pushes the bloom up on a drop (Spectrum the most).
///
/// Time and energy come from ClubBeat. Colours come from the theme's two accents (ApplyPalette).
public class NeonPrismEnvironment : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [SerializeField] private PrismSkin skin;
    // How far a drop pushes the bloom toward BloomPeak, 0..1.
    [SerializeField, Range(0f, 1f)] private float dropBloom = 0.35f;
    [SerializeField] private float bloomPeak = 5f;
    [SerializeField] private int figuresPerSide = 8;
    [SerializeField] private int dots = 70;
    [SerializeField] private int laserBeams = 11;

    private const float Behind = 12f;
    private const float GridHalfWidth = 12f;
    private const float CrossSpacing = 2f;
    private const float MaxLevel = 2.4f;

    private Color primary = new Color(0f, 1f, 0.85f);
    private Color secondary = new Color(0.1f, 0.35f, 1f);
    private float emission = 1.3f;

    private float scrollSpeed, span;
    private bool built;
    private Mesh cube, sphere;
    private MaterialPropertyBlock mpb;
    private Material gridMat, dotMat, laserRedMat, laserWhiteMat;
    private readonly Material[] figureMats = new Material[4];
    private readonly System.Random rng = new System.Random(6);

    private struct Cross { public Transform t; public MeshRenderer r; }
    private struct Figure { public Transform t; public float baseY, size, seed, turn; public Vector3 axis; public int group; }
    private struct Dot { public Transform t; public MeshRenderer r; public float baseY, seed; }

    private readonly List<Cross> crosses = new List<Cross>();
    private readonly List<Figure> figures = new List<Figure>();
    private readonly List<Dot> dotList = new List<Dot>();
    private readonly List<MeshRenderer> gridLong = new List<MeshRenderer>();
    private Transform laserPivot;
    private readonly List<MeshRenderer> lasers = new List<MeshRenderer>();
    private Volume bloomVolume;
    private VolumeProfile bloomProfile;

    public void ApplyPalette(Color primaryColor, Color secondaryColor, float emissionIntensity)
    {
        primary = primaryColor;
        secondary = secondaryColor;
        emission = Mathf.Min(emissionIntensity, 1.5f);
    }

    public void Initialize(float tunnelLength, float scrollSpeed)
    {
        if (built) return;
        built = true;
        this.scrollSpeed = scrollSpeed;
        span = tunnelLength + Behind + 8f;
        mpb = new MaterialPropertyBlock();
        cube = RemixGeo.Primitive(PrimitiveType.Cube);
        sphere = RemixGeo.Primitive(PrimitiveType.Sphere);

        BuildFloor(tunnelLength);
        BuildFigures();
        if (skin == PrismSkin.RedLaser) BuildLasers(tunnelLength);
        if (skin == PrismSkin.Ultraviolet) BuildDots();
        BuildBloomVolume();
    }

    private void OnDestroy()
    {
        foreach (var m in new[] { gridMat, dotMat, laserRedMat, laserWhiteMat }) if (m != null) Destroy(m);
        foreach (var m in figureMats) if (m != null) Destroy(m);
        if (bloomProfile != null) Destroy(bloomProfile);
    }

    // --- colour ---

    // slot 0 = primary, 1 = secondary; Spectrum ignores the palette and walks the wheel from the phrase hue.
    private Color Pick(int slot, float hueOffset, float value = 1f) =>
        skin == PrismSkin.Spectrum ? PrismLook.Spectrum(hueOffset + slot * 0.5f, value) : (slot == 0 ? primary : secondary) * value;

    // --- building ---

    private void BuildFloor(float tunnelLength)
    {
        var root = new GameObject("LaserFloor").transform;
        root.SetParent(transform, false);
        root.localPosition = new Vector3(0f, 0.03f, 0f);
        gridMat = RemixGeo.Neon(primary, emission);

        // Long lines along the road, a metre and a half apart, the outer ones a little thinner.
        for (float x = -GridHalfWidth; x <= GridHalfWidth + 0.01f; x += 1.5f)
            gridLong.Add(RemixGeo.Beam(root, "Line", cube, gridMat, new Vector3(x, 0f, -Behind), new Vector3(x, 0f, tunnelLength + 12f),
                                       Mathf.Abs(x) > 4.5f ? 0.035f : 0.05f));

        // The ladder across, scrolling with the tunnel.
        int count = Mathf.CeilToInt(span / CrossSpacing);
        for (int i = 0; i < count; i++)
        {
            var r = RemixGeo.Beam(root, "Cross", cube, gridMat, new Vector3(-GridHalfWidth, 0f, 0f), new Vector3(GridHalfWidth, 0f, 0f), 0.05f);
            r.transform.localPosition = new Vector3(0f, 0f, -Behind + i * CrossSpacing);
            crosses.Add(new Cross { t = r.transform, r = r });
        }
    }

    private void BuildFigures()
    {
        var root = new GameObject("Figures").transform;
        root.SetParent(transform, false);
        for (int g = 0; g < figureMats.Length; g++) figureMats[g] = RemixGeo.Neon(g % 2 == 0 ? primary : secondary, emission);

        int total = figuresPerSide * 2;
        for (int i = 0; i < total; i++)
        {
            int side = i % 2 == 0 ? -1 : 1;
            int slot = i / 2;
            int group = i % figureMats.Length;
            float size = 1.1f + (float)rng.NextDouble() * 1.2f;
            var fig = new GameObject("Figure_" + i).transform;
            fig.SetParent(root, false);
            float x = side * (7.8f + (float)rng.NextDouble() * 4f);
            float z = -Behind + (slot + (float)rng.NextDouble() * 0.8f) * (span / figuresPerSide);
            float y = 2f + (float)rng.NextDouble() * 5.5f;
            fig.localPosition = new Vector3(x, y, z);
            fig.localScale = Vector3.one * size;
            switch (i % 6 / 2)
            {
                case 0: BuildCube(fig, figureMats[group]); break;
                case 1: BuildPyramid(fig, figureMats[group]); break;
                default: BuildRings(fig, figureMats[group]); break;
            }
            figures.Add(new Figure
            {
                t = fig, baseY = y, size = size, seed = (float)rng.NextDouble() * 6.28f, group = group,
                axis = new Vector3((float)rng.NextDouble() + 0.3f, 1f, (float)rng.NextDouble() * 0.6f).normalized,
                turn = (side > 0 ? 1f : -1f) * (11f + (float)rng.NextDouble() * 9f),      // degrees per beat
            });
        }
    }

    private void Edges(Transform parent, Material m, Vector3[] pts, int[,] edges, float thickness)
    {
        for (int e = 0; e < edges.GetLength(0); e++)
            RemixGeo.Beam(parent, "Edge", cube, m, pts[edges[e, 0]], pts[edges[e, 1]], thickness);
    }

    private void BuildCube(Transform parent, Material m)
    {
        var p = new Vector3[8];
        for (int i = 0; i < 8; i++) p[i] = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
        var edges = new int[12, 2];
        int n = 0;
        for (int i = 0; i < 8; i++)
            for (int bit = 1; bit <= 4; bit <<= 1)
                if ((i & bit) == 0) { edges[n, 0] = i; edges[n, 1] = i | bit; n++; }
        Edges(parent, m, p, edges, 0.05f);
    }

    private void BuildPyramid(Transform parent, Material m)
    {
        var p = new[]
        {
            new Vector3(-0.55f, -0.5f, -0.55f), new Vector3(0.55f, -0.5f, -0.55f), new Vector3(0.55f, -0.5f, 0.55f),
            new Vector3(-0.55f, -0.5f, 0.55f), new Vector3(0f, 0.7f, 0f),
        };
        Edges(parent, m, p, new[,] { { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 }, { 0, 4 }, { 1, 4 }, { 2, 4 }, { 3, 4 } }, 0.05f);
    }

    // Two rings at right angles, like a gyroscope.
    private void BuildRings(Transform parent, Material m)
    {
        const int seg = 14;
        for (int plane = 0; plane < 2; plane++)
            for (int i = 0; i < seg; i++)
            {
                Vector3 At(int k)
                {
                    float a = k * Mathf.PI * 2f / seg;
                    return plane == 0 ? new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.6f : new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a)) * 0.6f;
                }
                RemixGeo.Beam(parent, "Ring", cube, m, At(i), At(i + 1), 0.05f);
            }
    }

    // A fan of beams from the far end, pointing back down the road over the camera.
    private void BuildLasers(float tunnelLength)
    {
        laserRedMat = RemixGeo.Neon(primary, emission);
        laserWhiteMat = RemixGeo.Neon(secondary, emission);
        laserPivot = new GameObject("LaserFan").transform;
        laserPivot.SetParent(transform, false);
        laserPivot.localPosition = new Vector3(0f, 0.4f, tunnelLength * 0.8f);
        for (int i = 0; i < laserBeams; i++)
        {
            float t = laserBeams > 1 ? i / (float)(laserBeams - 1) : 0.5f;
            float yaw = Mathf.Lerp(-36f, 36f, t);
            float pitch = 1f + 7f * (i % 3) / 2f;
            Vector3 dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.back;
            lasers.Add(RemixGeo.Beam(laserPivot, "Laser", cube, i % 2 == 0 ? laserRedMat : laserWhiteMat, Vector3.zero, dir * (tunnelLength * 0.8f + 20f), 0.07f));
        }
    }

    private void BuildDots()
    {
        var root = new GameObject("Dots").transform;
        root.SetParent(transform, false);
        dotMat = RemixGeo.Neon(primary, emission);
        for (int i = 0; i < dots; i++)
        {
            float side = rng.NextDouble() < 0.5 ? -1f : 1f;
            float x = side * (1.5f + (float)rng.NextDouble() * 10f);
            float y = 0.4f + (float)rng.NextDouble() * 8f;
            float z = -Behind + (float)rng.NextDouble() * span;
            float size = 0.1f + (float)rng.NextDouble() * 0.12f;
            var r = RemixGeo.Beam(root, "Dot", sphere, dotMat, new Vector3(x, y, z), new Vector3(x, y, z), size);
            dotList.Add(new Dot { t = r.transform, r = r, baseY = y, seed = (float)rng.NextDouble() * 6.28f });
        }
    }

    // A global Volume above the scene's own: on a drop its weight rises and the bloom swells toward bloomPeak.
    private void BuildBloomVolume()
    {
        if (dropBloom <= 0f) return;
        bloomProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = bloomProfile.Add<Bloom>(true);
        bloom.intensity.Override(bloomPeak);
        bloom.scatter.Override(0.8f);
        bloom.threshold.Override(0.6f);
        var go = new GameObject("DropBloom");
        go.transform.SetParent(transform, false);
        bloomVolume = go.AddComponent<Volume>();
        bloomVolume.isGlobal = true;
        bloomVolume.priority = 50f;
        bloomVolume.sharedProfile = bloomProfile;
        bloomVolume.weight = 0f;
    }

    // --- every frame ---

    private void Update()
    {
        if (!built) return;
        float delta = scrollSpeed * Time.deltaTime;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float kick = ClubBeat.Pulse;
        float t = ClubBeat.SongTime;
        float baseZ = transform.position.z;

        // Floor: the long lines breathe with the kick; each cross line carries the wave (CyanPulse) or its own hue.
        float longLevel = Mathf.Min(MaxLevel, emission * (0.35f + 0.35f * kick * energy));
        mpb.SetColor(RemixGeo.EmissionId, Pick(0, 0f) * longLevel);
        foreach (var r in gridLong) r.SetPropertyBlock(mpb);
        foreach (var c in crosses)
        {
            var p = c.t.localPosition;
            p.z -= delta;
            if (p.z < -Behind) p.z += span;
            c.t.localPosition = p;
            float level = 0.25f + 0.55f * kick * energy;
            if (skin == PrismSkin.CyanPulse) level += 1.8f * PrismLook.Wave(p.z + baseZ);
            mpb.SetColor(RemixGeo.EmissionId, Pick(0, p.z * 0.012f) * Mathf.Min(MaxLevel, emission * level));
            c.r.SetPropertyBlock(mpb);
        }

        // Figures: flow, bob, turn a fixed angle per beat, swell on the kick.
        for (int i = 0; i < figures.Count; i++)
        {
            var f = figures[i];
            var p = f.t.localPosition;
            p.z -= delta;
            if (p.z < -Behind) p.z += span;
            p.y = f.baseY + Mathf.Sin(t * 0.8f + f.seed) * 0.4f;
            f.t.localPosition = p;
            f.t.localRotation = Quaternion.AngleAxis(beats * f.turn + f.seed * 20f, f.axis);
            f.t.localScale = Vector3.one * (f.size * (1f + 0.22f * kick * Mathf.Min(energy, 1.3f)));
        }
        int accent = ((ClubBeat.BeatIndex % 4) + 4) % 4;
        for (int g = 0; g < figureMats.Length; g++)
        {
            float level = 0.45f + 0.3f * kick * energy + (g == accent ? 0.55f * kick : 0f);
            Color c = Pick(g % 2, g * 0.12f);
            RemixGeo.SetEmission(figureMats[g], c, Mathf.Min(MaxLevel, emission * level));
        }

        if (laserPivot != null) UpdateLasers(beats, kick, energy);
        if (dotMat != null) UpdateDots(delta, t, kick, energy);

        if (bloomVolume != null)
        {
            float drop = Mathf.Clamp01((ClubBeat.Energy - 1f) / 0.6f);
            bloomVolume.weight = dropBloom * drop * (0.55f + 0.45f * kick);
        }
    }

    private void UpdateLasers(float beats, float kick, float energy)
    {
        laserPivot.localRotation = Quaternion.Euler(0f, Mathf.Sin(beats * Mathf.PI / 8f) * 8f, 0f);
        bool drop = ClubBeat.Energy > 1.15f;
        // Strong beats only; in a drop, every beat.
        float flare = (drop || PrismLook.StrongBeat) ? kick : 0f;
        float level = Mathf.Min(MaxLevel, emission * (0.1f + 2f * flare * Mathf.Min(energy, 1.3f)));
        RemixGeo.SetEmission(laserRedMat, primary, level);
        RemixGeo.SetEmission(laserWhiteMat, secondary, level);
    }

    private void UpdateDots(float delta, float t, float kick, float energy)
    {
        for (int i = 0; i < dotList.Count; i++)
        {
            var d = dotList[i];
            var p = d.t.localPosition;
            p.z -= delta;
            if (p.z < -Behind) p.z += span;
            p.y = d.baseY + Mathf.Sin(t * 0.5f + d.seed) * 0.5f;
            d.t.localPosition = p;
            float twinkle = 0.5f + 0.5f * Mathf.Sin(t * 2.2f + d.seed * 5f);
            Color c = (i % 5 == 0) ? Color.Lerp(secondary, Color.white, 0.6f) : primary;     // the odd white-blue speck: blacklight on white
            mpb.SetColor(RemixGeo.EmissionId, c * Mathf.Min(MaxLevel, emission * (0.3f + 0.9f * twinkle + 0.4f * kick * energy)));
            d.r.SetPropertyBlock(mpb);
        }
    }
}
