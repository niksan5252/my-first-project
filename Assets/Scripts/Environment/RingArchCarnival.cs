using DG.Tweening;
using UnityEngine;

/// Theme NeonCarnival's ring: a fan arch - a half circle over the road built from separate
/// segments, like the ribs of an open hand fan, with a feather tip standing out from each rib.
/// Two concentric half circles (inner 6.6 m, outer 7.6 m) are joined by radial ribs; the tips
/// beyond the outer rim open out on the strong beats and fold back (an OutBack spring), and the
/// segments light up in a chase round the half circle, one rib per beat-step. All of it is
/// SongTweens, positioned from ClubBeat, so it is on the music in Play and in a recording.
///
/// The skin (CarnivalSkin) changes how the light behaves: RioNight blinks the bulbs along the rim
/// on alternate beats, Carnaval opens the tips the widest and cycles its three colours rib by rib,
/// Copacabana sends a wave of light from one foot of the arch to the other every bar.
public class RingArchCarnival : MonoBehaviour, IThemeAccent
{
    [SerializeField] private CarnivalSkin skin;
    [SerializeField] private float innerRadius = 6.6f;
    [SerializeField] private float outerRadius = 7.6f;
    [SerializeField] private int segments = 9;
    [SerializeField] private float tipLength = 1.1f;
    [SerializeField] private float beamThickness = 0.13f;
    [SerializeField] private Color color = new Color(0.1f, 1f, 0.4f);
    [SerializeField] private Color secondColor = new Color(1f, 0.85f, 0.1f);
    [SerializeField] private Color thirdColor = new Color(1f, 1f, 1f);
    [SerializeField, Range(0f, RemixGeo.MaxEmission)] private float emissionIntensity = 1.3f;

    private Material mat;
    private MeshRenderer[] ribs, arcsIn, arcsOut, bulbs;
    private Transform[] tips;
    private MeshRenderer[] tipRenderers;
    private MaterialPropertyBlock mpb;
    private readonly SongTweens tweens = new SongTweens();
    private float kick, open, sway;
    private Transform pivot;

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        mat = RemixGeo.Neon(color, emissionIntensity);
        Mesh cube = RemixGeo.Primitive(PrimitiveType.Cube), sphere = RemixGeo.Primitive(PrimitiveType.Sphere);

        // Everything hangs on a pivot of its own, so the sway never touches the root the tunnel places.
        pivot = new GameObject("Fan").transform;
        pivot.SetParent(transform, false);
        Transform root = pivot;
        int n = Mathf.Max(3, segments);
        ribs = new MeshRenderer[n + 1];
        tips = new Transform[n + 1];
        tipRenderers = new MeshRenderer[n + 1];
        arcsIn = new MeshRenderer[n];
        arcsOut = new MeshRenderer[n];
        bulbs = new MeshRenderer[n];
        // The half circle stands on the road: angle 0 is the right foot, 180 the left; its feet go
        // a little under the floor so they do not end in mid air.
        Vector3 At(float deg, float r) => new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad) * r, Mathf.Sin(deg * Mathf.Deg2Rad) * r - 0.3f, 0f);
        for (int i = 0; i <= n; i++)
        {
            float a = 180f * i / n;
            ribs[i] = RemixGeo.Beam(root, "Rib", cube, mat, At(a, innerRadius), At(a, outerRadius), beamThickness);
            // The tip: a pivot on the outer rim, pointing out along the rib; its own scale opens it.
            var tip = new GameObject("Tip").transform;
            tip.SetParent(root, false);
            tip.localPosition = At(a, outerRadius);
            tip.localRotation = Quaternion.Euler(0f, 0f, a - 90f);
            tips[i] = tip;
            tipRenderers[i] = RemixGeo.Beam(tip, "Feather", sphere, mat, Vector3.zero, Vector3.up * tipLength, 0.32f);
            if (i == n) break;
            float b = 180f * (i + 1) / n;
            arcsIn[i] = RemixGeo.Beam(root, "ArcIn", cube, mat, At(a, innerRadius), At(b, innerRadius), beamThickness);
            arcsOut[i] = RemixGeo.Beam(root, "ArcOut", cube, mat, At(a, outerRadius), At(b, outerRadius), beamThickness);
            bulbs[i] = RemixGeo.Beam(root, "Bulb", sphere, mat, At((a + b) * 0.5f, (innerRadius + outerRadius) * 0.5f),
                                     At((a + b) * 0.5f, (innerRadius + outerRadius) * 0.5f), 0.3f);
        }

        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutExpo), 1f);
        // The tips spring open on beat one and three and fold back over the next beat and a half.
        tweens.Loop(DOTween.Sequence()
            .Append(DOTween.To(x => open = x, 0f, 1f, 0.35f).SetEase(Ease.OutBack))
            .Append(DOTween.To(x => open = x, 1f, 0f, 1.65f).SetEase(Ease.InOutSine)), 2f);
        tweens.Loop(DOTween.To(x => sway = x, -1.5f, 1.5f, 4f).SetEase(Ease.InOutSine).SetLoops(2, LoopType.Yoyo), 8f);
    }

    private void OnDestroy()
    {
        tweens.KillAll();
        if (mat != null) Destroy(mat);
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        // The theme alternates its two accents ring by ring; the skin's own three stay the palette,
        // and the accent only says which of them leads on this ring.
        if (newColor != color) { var t = color; color = secondColor; secondColor = t; }
        emissionIntensity = Mathf.Min(newIntensity, 1.4f);
    }

    private void Update()
    {
        if (ribs == null) return;
        float beats = CarnivalLook.Beats;
        tweens.SeekLoops(beats);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        int n = arcsIn.Length;

        // The chase: one rib lit per half beat, round the arch and back.
        float chasePos = Mathf.PingPong(beats * 2f, n);
        float baseLevel = emissionIntensity * (0.4f + 0.35f * kick * energy);
        float wave = 0f;
        if (skin == CarnivalSkin.Copacabana) wave = CarnivalLook.BarPosition * (n + 2) - 1f;

        for (int i = 0; i <= n; i++)
        {
            float chase = Mathf.Exp(-Mathf.Pow((i - chasePos) / 1.2f, 2f));
            float level = baseLevel + emissionIntensity * 0.9f * chase * Mathf.Min(energy, 1.2f);
            if (skin == CarnivalSkin.Copacabana) level += emissionIntensity * 1.1f * Mathf.Exp(-Mathf.Pow(i - wave, 2f));
            Color c = skin == CarnivalSkin.Carnaval ? CarnivalLook.Of(i + Mathf.FloorToInt(beats), color, secondColor, thirdColor)
                    : (i % 2 == 0 ? color : secondColor);
            mpb.SetColor(RemixGeo.EmissionId, c * Mathf.Min(level, RemixGeo.MaxEmission));
            ribs[i].SetPropertyBlock(mpb);
            if (i < n) { arcsIn[i].SetPropertyBlock(mpb); arcsOut[i].SetPropertyBlock(mpb); }

            Color tipColor = skin == CarnivalSkin.RioNight ? secondColor : CarnivalLook.Of(i, thirdColor, color, secondColor);
            mpb.SetColor(RemixGeo.EmissionId, tipColor * Mathf.Min(level * 1.2f + emissionIntensity * 0.5f * open, RemixGeo.MaxEmission));
            tipRenderers[i].SetPropertyBlock(mpb);

            float spread = skin == CarnivalSkin.Carnaval ? 1f : 0.55f;
            float s = 0.35f + spread * open * (0.6f + 0.4f * Mathf.Min(energy, 1.3f));
            tips[i].localScale = new Vector3(1f, s, 1f);
        }

        // Bulbs on the rim: RioNight blinks them odd/even on alternate beats, the others glow softly.
        bool odd = (ClubBeat.BeatIndex & 1) == 1;
        for (int i = 0; i < n; i++)
        {
            float on = skin == CarnivalSkin.RioNight ? (((i & 1) == 1) == odd ? 1f : 0.15f) : 0.5f;
            Color c = skin == CarnivalSkin.RioNight ? (i % 2 == 0 ? secondColor : color) : thirdColor;
            mpb.SetColor(RemixGeo.EmissionId, c * Mathf.Min(emissionIntensity * (0.3f + 1.3f * on * (0.5f + 0.5f * kick)), RemixGeo.MaxEmission));
            bulbs[i].SetPropertyBlock(mpb);
        }

        pivot.localRotation = Quaternion.Euler(0f, 0f, sway);
    }
}
