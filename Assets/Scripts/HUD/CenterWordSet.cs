using TMPro;
using UnityEngine;

/// The big praise words that flash in the middle of the screen a handful of times per track:
/// which words, how many, and how they move. Moments are chosen by CenterWords from the chart;
/// colours come from the theme like the rest of the HUD. Every duration is in beats.
[CreateAssetMenu(menuName = "Tunnel/Center Words", fileName = "CenterWordSet")]
public class CenterWordSet : ScriptableObject
{
    [Header("Words (each used at most once per track)")]
    public string[] words =
    {
        "GREAT", "AWESOME", "UNSTOPPABLE", "INCREDIBLE", "ON FIRE",
        "AMAZING", "BEAST MODE", "NO LIMITS", "KEEP GOING", "LEGENDARY",
    };
    public int wordsPerTrack = 5;
    // Off: the same chart always gets the same words (a seed from the track name), so two
    // recordings of one track match. On: a fresh draw every run.
    public bool newDrawEveryRun;

    [Header("Spacing (bars)")]
    public float minBarsBetween = 8f;
    // A stretch with no notes at least this long, followed by notes, is a "drums are back" moment.
    public float returnGapBars = 2f;
    // Notes closer than this belong to one run; the Nth note of a run is a "long streak" moment.
    public float streakGapBars = 1f;
    public int streakLength = 16;

    [Header("Look")]
    public TMP_FontAsset font;
    public float size = 130f;
    public float maxWidth = 1000f;          // reference pixels; longer words shrink to fit
    public float centerY = -330f;           // from the top of the 1920x1080 reference canvas
    [Range(0f, 1f)] public float glow = 0.45f;
    [Range(0f, 1f)] public float flashGlow = 1f;

    [Header("Colour (overrides the theme for these words only)")]
    // Off: the words take the theme's accents like the rest of the HUD. On: the colours below.
    public bool overrideThemeColours = true;
    public Color topColor = new Color(1f, 0.894f, 0.361f);      // #FFE45C, the light top of the chrome
    public Color bottomColor = new Color(1f, 0.604f, 0f);       // between #FFB000 and #FF8A00
    public Color outlineColor = new Color(0.62f, 0.13f, 0.02f); // dark orange edge, reads on pink/cyan
    public Color glowColor = new Color(1f, 0.72f, 0.1f);        // warm yellow, outside the glyph
    public Color echoColor = new Color(1f, 0.78f, 0.15f);       // the big outlined copy behind

    [Header("Animation (beats)")]
    public float punchInBeats = 0.375f;
    public float holdBeats = 2f;
    public float fadeOutBeats = 0.5f;
    public float flashDecayBeats = 1f;
    public float startScale = 0.6f;
    public float overshootScale = 1.1f;
    public float exitScale = 1.15f;
    public float flickerCyclesPerBeat = 1f;
    [Range(0f, 1f)] public float flickerAmount = 0.2f;

    [Header("Echo (the huge outlined copy behind)")]
    public float echoStartScale = 2.2f;
    public float echoEndScale = 3.0f;
    [Range(0f, 1f)] public float echoAlpha = 0.3f;
}
