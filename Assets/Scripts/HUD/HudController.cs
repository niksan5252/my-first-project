using TMPro;
using UnityEngine;

/// The whole HUD: a combo counter that ticks on every scored note, and the praise banner under
/// it. It owns no gameplay state - the count comes from CueSpawner's CueScored event and the set
/// boundaries from the loaded chart, so nothing here polls, and nothing in the spawn path knows
/// the HUD exists.
///
/// Colours arrive from the active TunnelTheme through TunnelThemeApplier (ApplyTheme below);
/// words, thresholds and timings come from a HudPresentation asset. Neither is hard-coded here.
///
/// Those timings are in beats. HudBeatClock turns them into seconds using the bpm the loaded
/// chart declares, so the counter's pulse, the banner's hold and the gap between banners all keep
/// their musical length when the track changes tempo - and the banner's flicker is locked to the
/// beat grid rather than free-running.
public class HudController : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private CueSpawner cueSpawner;
    [SerializeField] private HudPresentation style;
    // How it all looks. Optional: without one the HUD keeps the scene's own styling.
    [SerializeField] private HudSkin skin;

    [Header("Combo")]
    [SerializeField] private RectTransform comboGroup;  // pulsed as one, so number and label agree
    [SerializeField] private TMP_Text comboNumber;
    [SerializeField] private TMP_Text comboLabel;

    [Header("Banner")]
    [SerializeField] private HudBanner banner;

    // Theme colours, with the defaults from TunnelTheme so the HUD is still legible if it is ever
    // run without an applier.
    private Color comboColor = new Color(1f, 0.05f, 0.45f);
    private Color bannerBorderColor = new Color(0f, 0.85f, 1f);
    private Color primaryColor = new Color(1f, 0.05f, 0.45f);
    private Color secondaryColor = new Color(0f, 0.85f, 1f);

    // Beats, not seconds: every HUD duration is resolved through this against the chart's own
    // bpm, so the counter's pulse and the banner keep their musical length on any track.
    private readonly HudBeatClock beats = new HudBeatClock();

    private int combo;
    private int scoredNotes;
    // Both run on the song clock (HudBeatClock.Now), not on frame time, so the pulse is the same
    // length in Play and in a recording and cannot drift off the beat it started on.
    private float pulseStart = float.NegativeInfinity;
    private float pulseSeconds;
    private bool pulsing;
    private float glowStart = float.NegativeInfinity;
    private float glowSeconds;
    private bool glowing;
    private float comboBaseGlow;

    private int nextMilestone;
    private int nextSetIndex;
    private int setWordCursor;
    private int bannerCount;
    // In song time, and compared against a gap measured in beats - the banner cadence belongs to
    // the music like everything else here.
    private float lastBannerSongTime = float.NegativeInfinity;
    private bool finalShown;
    private bool subscribed;

    private void Awake()
    {
        if (cueSpawner != null) beats.Bind(cueSpawner.Clock);
        if (banner != null) banner.Configure(style, beats, skin);
        ResetCounter();
    }

    private void OnEnable() => Subscribe();
    private void OnDisable() => Unsubscribe();

    /// Subscribing in OnEnable alone is not enough: the spawner is built by TunnelThemeApplier in
    /// Awake, and script order decides whether that has happened yet. The event itself exists
    /// from the moment the component does, so a late subscribe would still work - this just makes
    /// the order irrelevant.
    private void Start() => Subscribe();

    private void Subscribe()
    {
        if (subscribed || cueSpawner == null) return;
        cueSpawner.CueScored += OnCueScored;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || cueSpawner == null) return;
        cueSpawner.CueScored -= OnCueScored;
        subscribed = false;
    }

    /// Called by TunnelThemeApplier when a theme loads or is swapped live.
    public void ApplyTheme(TunnelTheme theme)
    {
        if (theme == null) return;

        comboColor = theme.hudComboTextColor;
        bannerBorderColor = theme.hudBannerBorderColor;
        primaryColor = theme.primaryColor;
        secondaryColor = theme.secondaryColor;

        Restyle();
    }

    public HudBeatClock Beats => beats;
    public Color PrimaryColor => primaryColor;
    public Color SecondaryColor => secondaryColor;

    /// Re-dresses the counter in the current colours - after a theme change, or when HudSkin
    /// switches looks.
    public void Restyle()
    {
        if (skin != null)
        {
            comboBaseGlow = skin.StyleCombo(comboColor, primaryColor, secondaryColor);
            if (comboNumber != null) comboNumber.text = skin.FormatCombo(combo);
            skin.SetNextMilestone(NextMilestoneCombo());
            return;
        }

        if (comboNumber != null)
        {
            // The glyph core stays near white and the theme colour goes into the glow. Filling
            // the digits with a saturated colour and then adding enough glow to bloom is what
            // turns a counter into a white blob: bloom threshold in this scene is 0.8.
            comboNumber.color = Color.Lerp(comboColor, Color.white, 0.75f);
            comboBaseGlow = HudGlow.Apply(comboNumber, comboColor, 0.45f);
        }

        if (comboLabel != null)
        {
            comboLabel.color = new Color(1f, 1f, 1f, 0.72f);
            HudGlow.Apply(comboLabel, comboColor, 0.25f);
        }
    }

    public void ResetCounter()
    {
        combo = 0;
        scoredNotes = 0;
        nextMilestone = 0;
        nextSetIndex = 0;
        setWordCursor = 0;
        bannerCount = 0;
        finalShown = false;
        lastBannerSongTime = float.NegativeInfinity;
        beats.Reset();
        if (comboNumber != null) comboNumber.text = skin != null ? skin.FormatCombo(0) : "0";
        if (skin != null) skin.SetNextMilestone(NextMilestoneCombo());
    }

    /// The combo value of the next milestone banner, or 0 when none is left.
    private int NextMilestoneCombo()
    {
        var table = style != null ? style.Milestones : null;
        return table != null && nextMilestone < table.Length ? table[nextMilestone].combo : 0;
    }

    /// How far the count is from the previous milestone to the next, 0..1.
    private float MilestoneProgress()
    {
        var table = style != null ? style.Milestones : null;
        if (table == null || nextMilestone >= table.Length) return 1f;
        int from = nextMilestone > 0 ? table[nextMilestone - 1].combo : 0;
        int to = table[nextMilestone].combo;
        return to > from ? Mathf.Clamp01((combo - from) / (float)(to - from)) : 1f;
    }

    // --- the one entry point ---

    private void OnCueScored(CueHitInfo info)
    {
        // The chart's tempo is only known once a beatmap has loaded, and the first scored note
        // is both the earliest moment anything here animates and a note that sits ON the beat
        // grid - so it is where the HUD takes its tempo and anchors its beat phase.
        var meta = cueSpawner != null ? cueSpawner.BeatmapMeta : null;
        beats.SetTempo(meta != null ? meta.bpm : 0f, style != null ? style.FallbackBpm : 120f);
        beats.AnchorTo(info.songTime);

        combo++;
        scoredNotes++;
        if (comboNumber != null) comboNumber.text = skin != null ? skin.FormatCombo(combo) : combo.ToString();

        pulseSeconds = Mathf.Max(0.01f, beats.Seconds(style != null ? style.ComboPulseBeats : 0.35f));
        glowSeconds = Mathf.Max(0.01f, beats.Seconds(style != null ? style.ComboGlowDecayBeats : 0.6f));
        pulseStart = glowStart = beats.Now;
        pulsing = glowing = true;
        if (skin != null) skin.OnCombo(combo, MilestoneProgress());

        if (style == null || banner == null) return;

        // Priority: a combo milestone beats an end-of-set that lands in the same moment. Both
        // update lastBannerSongTime, so whichever wins also suppresses the other through the
        // minimum-gap rule rather than through a special case.
        if (TryMilestoneBanner(info.songTime)) { AdvanceSetCursor(info.songTime); return; }
        TrySetBanner(info.songTime);
        TryFinalBanner(info.songTime);
    }

    private bool TryMilestoneBanner(float songTime)
    {
        var table = style.Milestones;
        if (table == null || nextMilestone >= table.Length) return false;
        if (combo < table[nextMilestone].combo) return false;

        string word = table[nextMilestone].word;
        nextMilestone++;
        if (skin != null) skin.SetNextMilestone(NextMilestoneCombo());
        ShowBanner(word, NextBannerColor(), style.BannerHoldBeats, songTime);
        return true;
    }

    /// A set's `end` is the time of its LAST note, so the note that satisfies this test is that
    /// note - the banner lands on the beat that finishes the block, not a second later when the
    /// next block's first note arrives.
    private void TrySetBanner(float songTime)
    {
        var sets = Sets();
        if (sets == null || nextSetIndex >= sets.Length) return;
        if (songTime < sets[nextSetIndex].end - 0.02f) return;

        BeatmapSet finished = sets[nextSetIndex];
        nextSetIndex++;

        if (finished.notes < style.MinNotesForSetBanner) return;
        if (songTime - lastBannerSongTime < beats.Seconds(style.MinBeatsBetweenBanners)) return;

        var words = style.SetWords;
        if (words == null || words.Length == 0) return;

        string word = words[setWordCursor % words.Length];
        setWordCursor++;
        ShowBanner(word, NextBannerColor(), style.BannerHoldBeats, songTime);
    }

    /// Keeps the set cursor honest when a milestone took the frame: the block still ended, it
    /// simply did not get a banner of its own.
    private void AdvanceSetCursor(float songTime)
    {
        var sets = Sets();
        while (sets != null && nextSetIndex < sets.Length && songTime >= sets[nextSetIndex].end - 0.02f)
            nextSetIndex++;
    }

    private void TryFinalBanner(float songTime)
    {
        if (finalShown || cueSpawner == null) return;
        var sequencer = cueSpawner.Sequencer;
        if (sequencer == null || sequencer.Count == 0 || scoredNotes < sequencer.Count) return;

        finalShown = true;
        // The one banner that ignores the gap rule and takes the theme's own border colour: it is
        // the end of the workout, not another milestone in it.
        ShowBanner(style.FinalWord, bannerBorderColor, style.FinalHoldBeats, songTime);
    }

    private BeatmapSet[] Sets()
    {
        var meta = cueSpawner != null ? cueSpawner.BeatmapMeta : null;
        return meta != null ? meta.sets : null;
    }

    /// Alternates the two accents, so consecutive banners never read as the same stamp twice.
    private Color NextBannerColor()
    {
        return (bannerCount % 2 == 0) ? primaryColor : secondaryColor;
    }

    private void ShowBanner(string word, Color color, float holdBeats, float songTime)
    {
        bannerCount++;
        lastBannerSongTime = songTime;
        banner.Show(word, color, holdBeats);
    }

    // --- combo pulse ---

    private void Update()
    {
        if (style == null) return;

        float now = beats.Now;

        if (pulsing && comboGroup != null)
        {
            float left = pulseSeconds - Mathf.Max(0f, now - pulseStart);
            float t = Mathf.Clamp01(left / pulseSeconds);
            // One rise and fall over the pulse, peaking immediately and easing back, so a stream
            // of notes reads as a series of taps rather than as a vibration.
            float s = 1f + (style.ComboPulseScale - 1f) * Mathf.Sin(t * Mathf.PI);
            comboGroup.localScale = Vector3.one * s;
            if (left <= 0f) { comboGroup.localScale = Vector3.one; pulsing = false; }
        }

        if (glowing && comboNumber != null)
        {
            float left = glowSeconds - Mathf.Max(0f, now - glowStart);
            float t = Mathf.Clamp01(left / glowSeconds);
            HudGlow.SetPower(comboNumber, comboBaseGlow + style.ComboGlowPunch * t);
            if (left <= 0f) { HudGlow.SetPower(comboNumber, comboBaseGlow); glowing = false; }
        }
    }
}
