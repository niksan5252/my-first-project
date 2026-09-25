using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// The praise plate: one instance, re-used for every word. There is deliberately only one, so two
/// banners can never overlap - a new word arriving while the old one is still up dismisses it in
/// a fast fade and then plays its own punch-in, which is the behaviour asked for and also the
/// cheapest way to guarantee it.
///
/// Every length here is a number of BEATS resolved through HudBeatClock at the moment it is
/// needed, so the whole animation keeps its musical proportions on any track: the plate holds for
/// a bar, and its neon flicker is phase-locked to the beat rather than free-running at a fixed
/// rate that would drift against the music.
///
/// Drives its own animation from Update against plain timers rather than a coroutine or a tween
/// library: the whole thing is three phases and one curve, and a timer survives being retargeted
/// mid-flight, which a coroutine would have to be stopped and restarted to do. The timers read the
/// song clock (HudBeatClock.Now) rather than adding up frame times, so a banner lasts exactly its
/// beats in Play and in a recording alike.
[RequireComponent(typeof(CanvasGroup))]
public class HudBanner : MonoBehaviour
{
    private enum Phase { Idle, Entering, Holding, Leaving }

    [SerializeField] private RectTransform plate;   // scaled and tilted; the group's child
    [SerializeField] private Image plateImage;      // 9-sliced neon plate
    [SerializeField] private TMP_Text wordText;

    private CanvasGroup group;
    private HudPresentation style;
    private HudBeatClock beats;
    private HudSkin skin;
    private Phase phase = Phase.Idle;
    private float phaseStart;   // song time the current phase began
    // Resolved to seconds when the phase starts, not read per frame: a banner keeps the tempo it
    // began on, so a tempo change mid-animation cannot stretch a fade that is already running.
    private float enterSeconds;
    private float holdSeconds;
    private float fadeSeconds;
    private float baseGlow;

    // Set while a word is waiting for the outgoing one to finish its handover fade.
    private bool hasPending;
    private string pendingWord;
    private Color pendingColor;
    private float pendingHoldBeats;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        if (plate == null) plate = transform as RectTransform;
    }

    public void Configure(HudPresentation presentation, HudBeatClock beatClock, HudSkin look = null)
    {
        style = presentation;
        beats = beatClock;
        skin = look;
    }

    private float Elapsed => Mathf.Max(0f, beats.Now - phaseStart);

    /// Shows a word for a number of beats. If one is already up, it is dismissed first (fast) and
    /// this one follows.
    public void Show(string word, Color color, float holdBeats)
    {
        if (style == null || beats == null) return;

        if (phase != Phase.Idle)
        {
            hasPending = true;
            pendingWord = word;
            pendingColor = color;
            pendingHoldBeats = holdBeats;
            // Only shorten the exit - a banner already leaving keeps whatever is left of its fade.
            if (phase != Phase.Leaving)
            {
                phase = Phase.Leaving;
                fadeSeconds = Mathf.Max(0.01f, beats.Seconds(style.BannerReplaceFadeBeats));
                phaseStart = beats.Now;
            }
            return;
        }

        Begin(word, color, holdBeats);
    }

    private void Begin(string word, Color color, float holdBeats)
    {
        wordText.text = word;
        if (skin != null)
        {
            baseGlow = skin.StyleBanner(word, color);
        }
        else
        {
            wordText.color = color;
            plateImage.color = color;
            // The glow colour follows the word, and its resting power is remembered so the
            // flicker has something to swing around.
            baseGlow = HudGlow.Apply(wordText, color, 0.5f);
        }

        enterSeconds = Mathf.Max(0.01f, beats.Seconds(style.BannerPunchInBeats));
        holdSeconds = Mathf.Max(0.01f, beats.Seconds(holdBeats));
        phase = Phase.Entering;
        phaseStart = beats.Now;
        group.alpha = 1f;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (style == null || beats == null || phase == Phase.Idle) return;

        switch (phase)
        {
            case Phase.Entering:
            {
                float t = Mathf.Clamp01(Elapsed / enterSeconds);
                // Overshoot then settle: scale runs past 1 and comes back, the tilt eases in from
                // a steeper angle. A plain lerp reads as a slide, not as a punch.
                float s = t < 0.6f
                    ? Mathf.Lerp(style.BannerStartScale, style.BannerPunchScale, t / 0.6f)
                    : Mathf.Lerp(style.BannerPunchScale, 1f, (t - 0.6f) / 0.4f);
                plate.localScale = Vector3.one * s;
                plate.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Lerp(style.BannerEnterAngle, style.BannerRestAngle, t));
                group.alpha = Mathf.Clamp01(t * 2f);

                if (t >= 1f) { phase = Phase.Holding; phaseStart = beats.Now; }
                break;
            }

            case Phase.Holding:
            {
                Flicker();
                if (Elapsed >= holdSeconds)
                {
                    phase = Phase.Leaving;
                    fadeSeconds = Mathf.Max(0.01f, beats.Seconds(style.BannerFadeOutBeats));
                    phaseStart = beats.Now;
                }
                break;
            }

            case Phase.Leaving:
            {
                float left = fadeSeconds - Elapsed;
                float t = Mathf.Clamp01(left / fadeSeconds);
                group.alpha = t;
                plate.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, t);

                if (left > 0f) break;

                phase = Phase.Idle;
                group.alpha = 0f;
                if (hasPending)
                {
                    hasPending = false;
                    Begin(pendingWord, pendingColor, pendingHoldBeats);
                }
                break;
            }
        }
    }

    /// A shallow swing on the glow while the banner sits there - neon that is completely steady
    /// reads as a printed label. The phase comes from the beat clock rather than from Time.time,
    /// so the brightest point of every cycle lands ON a beat and the plate pulses with the track.
    /// Amplitude stays small on purpose: this is texture, not a blink, and anything stronger
    /// fights the bloom.
    private void Flicker()
    {
        if (style.BannerFlickerAmount <= 0f) return;
        float phase01 = beats.Phase(style.BannerFlickerCyclesPerBeat);
        // cos, not sin: the cycle peaks at phase 0, which is the beat itself.
        float wave = 0.5f + 0.5f * Mathf.Cos(phase01 * Mathf.PI * 2f);
        HudGlow.SetPower(wordText, baseGlow * (1f - style.BannerFlickerAmount * (1f - wave)));
    }
}
