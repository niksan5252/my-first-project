using DG.Tweening;
using UnityEngine;

/// Theme NeonPrism's ring: a triangular portal. Two long beams meet over the road at an apex 8 m
/// up (they start 6.5 m out on each side, so the lanes and every note's reach stay inside), and a
/// small triangle - the prism - hangs under the apex and turns. The whole frame sways a couple
/// of degrees over eight beats and ticks on each beat; the prism steps 60 degrees every two beats.
/// All of it is SongTweens, positioned from ClubBeat, so it is on the music in Play and in a recording.
///
/// The skin (PrismSkin) only changes how the light behaves: RedLaser flashes on the strong beats,
/// CyanPulse carries a wave of light down the tunnel every bar, Spectrum takes its colour from the
/// phrase hue (rippling with depth), Ultraviolet keeps a low blacklight glow.
public class RingArchPrism : MonoBehaviour, IThemeAccent
{
    [SerializeField] private PrismSkin skin;
    [SerializeField] private float halfBase = 6.5f;
    [SerializeField] private float apexHeight = 8f;
    [SerializeField] private float legDepth = 0.8f;          // the beams run this far under the road, out of sight
    [SerializeField] private float prismRadius = 1.5f;
    [SerializeField] private float prismHeight = 5.3f;
    [SerializeField] private float beamThickness = 0.15f;
    [SerializeField] private Color color = new Color(0f, 1f, 0.85f);
    [SerializeField, Range(0f, RemixGeo.MaxEmission)] private float emissionIntensity = 1.3f;

    private const float SwayDegrees = 2.2f, TickDegrees = 1.3f;

    private Material frameMat, prismMat;
    private MeshRenderer[] frame, prism;
    private MeshRenderer gem;
    private Transform framePivot, prismPivot;
    private MaterialPropertyBlock mpb;
    private readonly SongTweens tweens = new SongTweens();
    private float kick, bar, sway, tick, spin;

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        frameMat = RemixGeo.Neon(color, emissionIntensity);
        prismMat = RemixGeo.Neon(Color.Lerp(color, Color.white, 0.4f), emissionIntensity);
        Mesh cube = RemixGeo.Primitive(PrimitiveType.Cube), sphere = RemixGeo.Primitive(PrimitiveType.Sphere);

        // The frame sways about a point at mid road height, so it never lifts off the ground much.
        const float pivotY = 2.2f;
        framePivot = new GameObject("Frame").transform;
        framePivot.SetParent(transform, false);
        framePivot.localPosition = new Vector3(0f, pivotY, 0f);
        Vector3 Up(float x, float y) => new Vector3(x, y - pivotY, 0f);

        float footX = halfBase * (1f + legDepth / apexHeight);
        frame = new[]
        {
            RemixGeo.Beam(framePivot, "LegL", cube, frameMat, Up(-footX, -legDepth), Up(0f, apexHeight), beamThickness),
            RemixGeo.Beam(framePivot, "LegR", cube, frameMat, Up(footX, -legDepth), Up(0f, apexHeight), beamThickness),
        };

        // The prism: an equilateral triangle turning about its own centre.
        prismPivot = new GameObject("Prism").transform;
        prismPivot.SetParent(framePivot, false);
        prismPivot.localPosition = Up(0f, prismHeight);
        var corners = new Vector3[3];
        for (int i = 0; i < 3; i++)
        {
            float a = (90f + i * 120f) * Mathf.Deg2Rad;
            corners[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * prismRadius;
        }
        prism = new MeshRenderer[3];
        for (int i = 0; i < 3; i++)
            prism[i] = RemixGeo.Beam(prismPivot, "Side", cube, prismMat, corners[i], corners[(i + 1) % 3], beamThickness * 0.75f);
        gem = RemixGeo.Beam(prismPivot, "Gem", sphere, prismMat, Vector3.zero, Vector3.zero, 0.38f);

        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutExpo), 1f);
        tweens.Loop(DOTween.To(x => bar = x, 1f, 0f, 4f).SetEase(Ease.OutExpo), 4f);
        tweens.Loop(DOTween.To(x => sway = x, -SwayDegrees, SwayDegrees, 4f).SetEase(Ease.InOutSine).SetLoops(2, LoopType.Yoyo), 8f);
        tweens.Loop(DOTween.To(x => tick = x, TickDegrees, 0f, 1f).SetEase(Ease.OutExpo), 1f);
        tweens.Loop(DOTween.Sequence()
            .Append(DOTween.To(x => spin = x, 0f, 60f, 1f).SetEase(Ease.OutBack))
            .AppendInterval(1f), 2f);
    }

    private void OnDestroy()
    {
        tweens.KillAll();
        if (frameMat != null) Destroy(frameMat);
        if (prismMat != null) Destroy(prismMat);
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        color = newColor;
        emissionIntensity = Mathf.Min(newIntensity, 1.4f);
    }

    private void Update()
    {
        if (frame == null) return;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        tweens.SeekLoops(beats);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);

        float z = transform.position.z;
        Color c = color;
        if (skin == PrismSkin.Spectrum)
        {
            Color.RGBToHSV(color, out float h, out _, out _);
            c = PrismLook.Spectrum(h + z * 0.012f);
        }

        float level = emissionIntensity * (0.45f + 0.4f * kick * energy + 0.25f * bar * energy);
        float prismLevel = emissionIntensity * (0.7f + 0.8f * kick * energy);
        switch (skin)
        {
            case PrismSkin.CyanPulse: level += emissionIntensity * 1.4f * PrismLook.Wave(z); break;
            case PrismSkin.RedLaser:
                if (PrismLook.StrongBeat) { level += emissionIntensity * 0.9f * kick; prismLevel *= 1.25f; }
                break;
            case PrismSkin.Ultraviolet: level *= 0.8f; break;
        }
        level = Mathf.Min(level, RemixGeo.MaxEmission);
        prismLevel = Mathf.Min(prismLevel, RemixGeo.MaxEmission);

        mpb.SetColor(RemixGeo.EmissionId, c * level);
        foreach (var r in frame) r.SetPropertyBlock(mpb);
        mpb.SetColor(RemixGeo.EmissionId, Color.Lerp(c, Color.white, 0.4f) * prismLevel);
        foreach (var r in prism) r.SetPropertyBlock(mpb);
        gem.SetPropertyBlock(mpb);

        framePivot.localRotation = Quaternion.Euler(0f, 0f, sway + ((ClubBeat.BeatIndex & 1) == 0 ? tick : -tick));
        int steps = Mathf.FloorToInt(beats * 0.5f);
        prismPivot.localRotation = Quaternion.Euler(0f, 0f, 60f * (((steps % 6) + 6) % 6) + spin);
    }
}
