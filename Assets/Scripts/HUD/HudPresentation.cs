using UnityEngine;

/// Everything about WHEN the HUD says something and HOW LONG it takes to say it. The controller
/// holds none of these numbers: it reads this asset, so the words, the thresholds and every
/// animation length are editable without touching code or entering play mode.
///
/// All durations are in BEATS, not seconds, and are turned into seconds at runtime by
/// HudBeatClock using the bpm the chart declares. A pulse of 0.35 means "a bit over a third of a
/// beat" on any track; the same animation in seconds would drift out of the music the moment the
/// tempo changed. Four beats is one bar at 4/4, which is the length most of these are tuned to.
///
/// Colours are deliberately NOT here - those come from the active TunnelTheme
/// (hudComboTextColor / hudBannerBorderColor / the two accents), so a theme swap recolours the
/// HUD along with the tunnel.
[CreateAssetMenu(menuName = "Tunnel/HUD Presentation", fileName = "HudPresentation")]
public class HudPresentation : ScriptableObject
{
    /// A combo value that earns a banner, and the word it shows. Each fires once per run.
    [System.Serializable]
    public struct Milestone
    {
        public int combo;
        public string word;
    }

    [Header("Combo milestones (each fires once)")]
    [SerializeField]
    private Milestone[] milestones =
    {
        new Milestone { combo = 10,  word = "NICE" },
        new Milestone { combo = 25,  word = "GREAT" },
        new Milestone { combo = 50,  word = "PERFECT" },
        new Milestone { combo = 100, word = "AWESOME" },
        new Milestone { combo = 200, word = "UNSTOPPABLE" },
        new Milestone { combo = 400, word = "AWESOME" },
    };

    [Header("End of a workout set")]
    // Cycled in order, one word per set-end that actually gets shown.
    [SerializeField] private string[] setWords = { "WELL DONE!", "GREAT", "NICE" };
    // A set ends every ~8 bars on the current chart, which is far too often to celebrate. This is
    // the floor on the gap between two banners of ANY kind. In beats rather than seconds because
    // the sets themselves are laid out on the bar grid: 28 beats is "about every other set" at
    // any tempo, where a fixed 12 s would mean every third set on a fast track and every set on a
    // slow one.
    [SerializeField] private float minBeatsBetweenBanners = 28f;
    // A "set" of one or two notes is a grid artefact, not a block of work.
    [SerializeField] private int minNotesForSetBanner = 3;

    [Header("End of the chart")]
    [SerializeField] private string finalWord = "CONGRATULATIONS";
    [SerializeField] private float finalHoldBeats = 8f;   // two bars

    [Header("Banner animation (beats)")]
    [SerializeField] private float bannerPunchInBeats = 0.5f;
    [SerializeField] private float bannerHoldBeats = 4f;  // one bar
    [SerializeField] private float bannerFadeOutBeats = 0.8f;
    // How fast a banner already on screen is dismissed when a new one takes its place. Short on
    // purpose: two banners never overlap, so this is the whole handover.
    [SerializeField] private float bannerReplaceFadeBeats = 0.25f;
    [SerializeField] private float bannerPunchScale = 1.08f;
    [SerializeField] private float bannerStartScale = 0.6f;
    [SerializeField] private float bannerRestAngle = -4f;   // the plate's resting tilt
    [SerializeField] private float bannerEnterAngle = -8f;  // where the punch-in starts
    // Flicker rate as a multiple of the beat, and phase-locked to it: 1 pulses once per beat, 2
    // on every eighth. Locked rather than free-running, so the neon breathes WITH the track
    // instead of beating against it.
    //
    // The default is 2 (eighths) and that is not arbitrary. The beat grid is anchored on the
    // first scored note, and a chart's first note can just as easily be an off-beat eighth as a
    // downbeat - measured on techno_circuit_flow, the notes sit on the EIGHTH grid to within a
    // median of 34 ms but are split between on- and off-beats. At one cycle per beat a wrong
    // anchor would put every pulse consistently on the off-beat; at two, half a beat of anchor
    // error is exactly one whole cycle, so the pulse lands on the music either way.
    [SerializeField] private float bannerFlickerCyclesPerBeat = 2f;
    [SerializeField, Range(0f, 1f)] private float bannerFlickerAmount = 0.18f;

    [Header("Combo pulse (beats)")]
    [SerializeField] private float comboPulseBeats = 0.35f;
    [SerializeField] private float comboPulseScale = 1.2f;
    // How far the number's glow is pushed on a hit, on top of its resting glow.
    [SerializeField, Range(0f, 1f)] private float comboGlowPunch = 0.55f;
    [SerializeField] private float comboGlowDecayBeats = 0.6f;

    [Header("Tempo")]
    // Used only when no chart is loaded (the spawner's Random mode) or the chart's bpm is absent.
    [SerializeField] private float fallbackBpm = 120f;

    public Milestone[] Milestones => milestones;
    public string[] SetWords => setWords;
    public float MinBeatsBetweenBanners => minBeatsBetweenBanners;
    public int MinNotesForSetBanner => minNotesForSetBanner;
    public string FinalWord => finalWord;
    public float FinalHoldBeats => finalHoldBeats;

    public float BannerPunchInBeats => bannerPunchInBeats;
    public float BannerHoldBeats => bannerHoldBeats;
    public float BannerFadeOutBeats => bannerFadeOutBeats;
    public float BannerReplaceFadeBeats => bannerReplaceFadeBeats;
    public float BannerPunchScale => bannerPunchScale;
    public float BannerStartScale => bannerStartScale;
    public float BannerRestAngle => bannerRestAngle;
    public float BannerEnterAngle => bannerEnterAngle;
    public float BannerFlickerCyclesPerBeat => bannerFlickerCyclesPerBeat;
    public float BannerFlickerAmount => bannerFlickerAmount;

    public float ComboPulseBeats => comboPulseBeats;
    public float ComboPulseScale => comboPulseScale;
    public float ComboGlowPunch => comboGlowPunch;
    public float ComboGlowDecayBeats => comboGlowDecayBeats;

    public float FallbackBpm => fallbackBpm;
}
