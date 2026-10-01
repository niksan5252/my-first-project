using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// Theme_ToxicNeon's ring: two posts under a row of sharp triangular teeth - three peaks with
/// valleys between - where Phonk's gate has a house-shaped roof. A short fang hangs under each
/// peak. The peak tips are bulbs that take turns flaring, one per beat; the frame kicks on the
/// beat and swells on the bar (SongTweens, beats from ClubBeat).
///
/// Clearances: posts 4 m out, 3.4 m tall; the lowest valley is 3.8 m up, over the outer lanes'
/// high notes (2.25 m out, 2.45 m up) and a Dodge wall (3 m, 2.1 m).
public class RingArchToxicSpike : MonoBehaviour, IThemeAccent
{
    [SerializeField] private float halfWidth = 4f;
    [SerializeField] private float postHeight = 3.4f;
    [SerializeField] private float peakHeight = 5.9f;
    [SerializeField] private float valleyHeight = 3.8f;
    [SerializeField] private float beamThickness = 0.14f;
    [SerializeField] private Color color = new Color(0.22f, 1f, 0.08f);
    [SerializeField, Range(0f, RemixGeo.MaxEmission)] private float emissionIntensity = 1.3f;

    private Material frameMat, tipMat;
    private MeshRenderer[] frame, tips;
    private MaterialPropertyBlock mpb;
    private readonly SongTweens tweens = new SongTweens();
    private float kick, bar;

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        frameMat = RemixGeo.Neon(color, emissionIntensity);
        tipMat = RemixGeo.Neon(Color.Lerp(color, Color.white, 0.35f), emissionIntensity);
        Mesh cube = RemixGeo.Primitive(PrimitiveType.Cube), sphere = RemixGeo.Primitive(PrimitiveType.Sphere);

        // Outline: post, tooth, valley, tooth, valley, tooth, post.
        float third = halfWidth * 2f / 3f;
        var pts = new[]
        {
            new Vector3(-halfWidth, 0f, 0f), new Vector3(-halfWidth, postHeight, 0f),
            new Vector3(-third, peakHeight - 0.3f, 0f), new Vector3(-third * 0.5f, valleyHeight, 0f),
            new Vector3(0f, peakHeight, 0f), new Vector3(third * 0.5f, valleyHeight, 0f),
            new Vector3(third, peakHeight - 0.3f, 0f), new Vector3(halfWidth, postHeight, 0f),
            new Vector3(halfWidth, 0f, 0f),
        };
        var list = new List<MeshRenderer>();
        for (int i = 0; i < pts.Length - 1; i++)
            list.Add(RemixGeo.Beam(transform, "Edge", cube, frameMat, pts[i], pts[i + 1], beamThickness));
        int[] peaks = { 2, 4, 6 };
        foreach (int p in peaks)
            list.Add(RemixGeo.Beam(transform, "Fang", cube, frameMat, pts[p] + Vector3.down * 0.15f, pts[p] + Vector3.down * 1.1f, beamThickness * 0.5f));
        frame = list.ToArray();

        tips = new MeshRenderer[peaks.Length];
        for (int i = 0; i < peaks.Length; i++)
        {
            var bulb = RemixGeo.Beam(transform, "Tip", sphere, tipMat, pts[peaks[i]], pts[peaks[i]], 0.34f);
            bulb.transform.localPosition = pts[peaks[i]] + Vector3.back * 0.05f;
            tips[i] = bulb;
        }

        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutExpo), 1f);
        tweens.Loop(DOTween.To(x => bar = x, 1f, 0f, 4f).SetEase(Ease.OutExpo), 4f);
    }

    private void OnDestroy() { tweens.KillAll(); }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        color = newColor;
        emissionIntensity = Mathf.Min(newIntensity, 1.4f);
        if (frameMat == null) return;
        RemixGeo.SetEmission(frameMat, color, emissionIntensity);
        RemixGeo.SetEmission(tipMat, Color.Lerp(color, Color.white, 0.35f), emissionIntensity);
    }

    private void Update()
    {
        if (frame == null) return;
        tweens.SeekLoops(ClubBeat.BeatIndex + ClubBeat.BeatPhase);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float level = emissionIntensity * (0.5f + 0.4f * kick * energy + 0.3f * bar * energy);
        mpb.SetColor(RemixGeo.EmissionId, color * Mathf.Min(level, RemixGeo.MaxEmission));
        foreach (var r in frame) r.SetPropertyBlock(mpb);

        int lit = ((ClubBeat.BeatIndex % 3) + 3) % 3;
        Color tipColor = Color.Lerp(color, Color.white, 0.35f);
        for (int i = 0; i < tips.Length; i++)
        {
            float on = i == lit ? 0.35f + 0.65f * kick : 0.1f;
            mpb.SetColor(RemixGeo.EmissionId, tipColor * Mathf.Min(emissionIntensity * on * (0.7f + 0.5f * energy), RemixGeo.MaxEmission));
            tips[i].SetPropertyBlock(mpb);
        }
    }
}
