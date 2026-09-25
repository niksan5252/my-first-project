using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// A few big praise words flashed in the middle of the screen over a track - GREAT, UNSTOPPABLE,
/// BEAST MODE - each once, at the strongest moments the chart has, spread over the whole run.
///
/// The moments are planned once, when the chart is loaded (PlanMoments - the one function to
/// change for a different rule), and every one is placed on a bar line, so a word lands on the
/// downbeat. The words are drawn from a CenterWordSet without repeats.
///
/// It shares the screen politely with the corner banner: a word that falls due while the banner
/// is up waits for the next bar, and the banner holds its next word until this one is gone.
///
/// Looks like HUD look A (italic chrome gradient, neon edge) with a huge outlined "echo" of the
/// word behind it that swells and fades. All timing is in beats on the song clock's smoothed time.
public class CenterWords : MonoBehaviour
{
    [SerializeField] private CenterWordSet set;
    [SerializeField] private CueSpawner cueSpawner;
    [SerializeField] private HudController hud;
    [SerializeField] private HudBanner cornerBanner;

    public struct Moment
    {
        public float time;
        public string word;
        public string reason;
    }

    private readonly List<Moment> plan = new List<Moment>();
    private bool planned;
    private int next;

    private RectTransform root;
    private TMP_Text main, echo;
    private CanvasGroup group;

    private bool showing;
    private float start;
    private string shownWord;
    private int shownCount;

    public IReadOnlyList<Moment> Plan => plan;
    /// True from the word's first frame to its last - the corner banner waits while it is.
    public bool Showing => showing;

    private float SecondsPerBeat
    {
        get
        {
            var meta = cueSpawner != null ? cueSpawner.BeatmapMeta : null;
            float bpm = meta != null && meta.bpm >= 20f ? meta.bpm : 120f;
            return 60f / bpm;
        }
    }

    private float Now => hud != null ? hud.Beats.Now : Time.time;

    private void Awake()
    {
        if (cornerBanner != null) cornerBanner.Blocked = () => showing;
    }

    private void Update()
    {
        if (set == null || cueSpawner == null) return;
        if (!planned)
        {
            if (cueSpawner.Sequencer == null || cueSpawner.BeatmapMeta == null) return;
            Build();
            MakePlan();
        }

        float now = Now;
        if (showing) { Animate(now); return; }
        if (next >= plan.Count || now < plan[next].time) return;

        // Due. If the corner banner is up, move to the next bar line rather than talking over it.
        if (cornerBanner != null && cornerBanner.Busy)
        {
            var m = plan[next];
            m.time = NextBarLine(now + 0.01f);
            plan[next] = m;
            return;
        }
        Begin(plan[next], now);
        next++;
    }

    // --- planning ---

    private void MakePlan()
    {
        planned = true;
        plan.Clear();
        var meta = cueSpawner.BeatmapMeta;
        var seq = cueSpawner.Sequencer;
        var times = new List<float>();
        var ends = new List<float>();
        for (int i = 0; i < seq.Count; i++)
        {
            var cue = seq[i];
            // Obstacles fill the quiet stretches on purpose, so they do not count as "the drums".
            if (cue.type == CueType.Dodge || cue.type == CueType.Curb || cue.type == CueType.Duck) continue;
            times.Add(cue.time);
            ends.Add(cue.time + cue.duration);
        }
        float bar = SecondsPerBeat * 4f;
        float anchor = meta.sets != null && meta.sets.Length > 0 ? meta.sets[0].start : (times.Count > 0 ? times[0] : 0f);
        barAnchor = anchor;
        barSeconds = bar;

        var moments = PlanMoments(times, ends, meta.sets, bar, set);
        var words = DrawWords(moments.Count);
        var log = new StringBuilder("[CenterWords] ").Append(moments.Count).Append(" words planned:");
        for (int i = 0; i < moments.Count; i++)
        {
            var m = moments[i];
            m.time = NextBarLine(m.time - 0.05f);
            m.word = words[i];
            plan.Add(m);
            log.Append("\n  ").Append(m.time.ToString("F2")).Append(" s  ").Append(m.word).Append("  (").Append(m.reason).Append(')');
        }
        Debug.Log(log.ToString(), this);
    }

    private float barAnchor;
    private float barSeconds = 2f;

    private float NextBarLine(float t)
    {
        float k = Mathf.Ceil((t - barAnchor) / barSeconds);
        return barAnchor + k * barSeconds;
    }

    /// Where the words go. The rule, in full: collect the strong moments of the chart - the drums
    /// coming back after a stretch with no notes (strongest), the middle of a long unbroken run of
    /// notes, the end of a workout set (weakest) - split the playable span into `wordsPerTrack`
    /// equal parts, and take the strongest moment in each part, nearest the part's middle on a
    /// tie. That spreads the words over the whole track instead of bunching them where the chart
    /// happens to be busiest. A part with no moment in it gets its own middle.
    public static List<Moment> PlanMoments(List<float> notes, List<float> noteEnds, BeatmapSet[] sets,
                                           float bar, CenterWordSet cfg)
    {
        var result = new List<Moment>();
        if (notes.Count == 0 || cfg.wordsPerTrack <= 0) return result;

        var candidates = new List<(float time, int weight, string reason)>();
        float lastEnd = float.NegativeInfinity;
        int run = 0;
        for (int i = 0; i < notes.Count; i++)
        {
            float gap = notes[i] - lastEnd;
            if (i == 0 || gap >= cfg.returnGapBars * bar) candidates.Add((notes[i], 3, i == 0 ? "first drums" : "drums back after " + (gap / bar).ToString("F0") + " bars"));
            run = gap < cfg.streakGapBars * bar ? run + 1 : 1;
            if (cfg.streakLength > 0 && run % cfg.streakLength == 0) candidates.Add((notes[i], 2, run + "-note streak"));
            lastEnd = Mathf.Max(lastEnd, noteEnds[i]);
        }
        if (sets != null)
            foreach (var s in sets)
                if (s.notes >= 3) candidates.Add((s.end, 1, "end of a " + s.exercise + " set"));

        float from = notes[0], to = notes[notes.Count - 1];
        int n = cfg.wordsPerTrack;
        float part = (to - from) / n;
        float lastChosen = float.NegativeInfinity;
        for (int k = 0; k < n; k++)
        {
            float lo = from + k * part, hi = lo + part, mid = (lo + hi) * 0.5f;
            (float time, int weight, string reason) best = (mid, 0, "middle of part " + (k + 1));
            foreach (var c in candidates)
            {
                if (c.time < lo || c.time >= hi || c.time - lastChosen < cfg.minBarsBetween * bar) continue;
                if (c.weight > best.weight || (c.weight == best.weight && Mathf.Abs(c.time - mid) < Mathf.Abs(best.time - mid)))
                    best = c;
            }
            if (best.time - lastChosen < cfg.minBarsBetween * bar) continue;
            result.Add(new Moment { time = best.time, reason = best.reason });
            lastChosen = best.time;
        }
        return result;
    }

    private string[] DrawWords(int count)
    {
        var pool = new List<string>(set.words ?? new string[0]);
        int seed = set.newDrawEveryRun ? System.Environment.TickCount : StableHash(cueSpawner.BeatmapMeta.track ?? "");
        var rng = new System.Random(seed);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        var result = new string[count];
        for (int i = 0; i < count; i++) result[i] = pool.Count > 0 ? pool[i % pool.Count] : "GREAT";
        return result;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return (int)h;
        }
    }

    // --- showing ---

    private void Begin(Moment m, float now)
    {
        showing = true;
        start = now;
        shownWord = m.word;
        main.text = m.word;
        echo.text = m.word;
        // The echo is the same word at the size the main one actually ended up (long words shrink
        // to fit), so the two keep their proportions.
        main.ForceMeshUpdate();
        echo.fontSize = main.fontSize;

        // Alternate which accent leads, so consecutive words are not the same stamp twice.
        Color a = hud.PrimaryColor, b = hud.SecondaryColor;
        if (shownCount % 2 == 1) (a, b) = (b, a);
        shownCount++;

        main.enableVertexGradient = true;
        main.color = Color.white;
        main.colorGradient = new VertexGradient(Color.Lerp(b, Color.white, 0.45f), Color.Lerp(b, Color.white, 0.45f), a, a);
        HudGlow.Apply(main, a, set.glow);
        var mm = main.fontMaterial;
        mm.SetColor(ShaderUtilities.ID_OutlineColor, Color.Lerp(b, Color.white, 0.25f));
        mm.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.08f);

        // The echo is hollow: a faint tinted face and a bright edge, so it reads as a giant
        // outline of the word rather than as a second, blurrier copy.
        var em = echo.fontMaterial;
        em.DisableKeyword(ShaderUtilities.Keyword_Glow);
        em.EnableKeyword(ShaderUtilities.Keyword_Outline);
        em.SetColor(ShaderUtilities.ID_FaceColor, new Color(b.r, b.g, b.b, 0.12f));
        em.SetColor(ShaderUtilities.ID_OutlineColor, new Color(b.r, b.g, b.b, 1f));
        em.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.18f);
        echo.color = Color.white;
        root.gameObject.SetActive(true);
        Animate(now);
    }

    private void Animate(float now)
    {
        float beat = SecondsPerBeat;
        float e = Mathf.Max(0f, now - start);
        float tin = set.punchInBeats * beat, thold = set.holdBeats * beat, tout = set.fadeOutBeats * beat;
        float total = tin + thold + tout;

        float scale, alpha;
        if (e < tin)
        {
            float t = e / tin;
            // Out past full size and back: 0.6 -> 1.1 by 60%, then settle on 1.0.
            scale = t < 0.6f ? Mathf.Lerp(set.startScale, set.overshootScale, EaseOut(t / 0.6f))
                             : Mathf.Lerp(set.overshootScale, 1f, EaseInOut((t - 0.6f) / 0.4f));
            alpha = Mathf.Clamp01(t / 0.3f);
        }
        else if (e < tin + thold)
        {
            scale = 1f;
            alpha = 1f;
        }
        else
        {
            float t = Mathf.Clamp01((e - tin - thold) / tout);
            scale = Mathf.Lerp(1f, set.exitScale, EaseOut(t));
            alpha = 1f - t * t;
        }

        // Glow: a flash on arrival that decays, then a shallow flicker locked to the beat.
        float flash = Mathf.Clamp01(1f - e / Mathf.Max(0.01f, set.flashDecayBeats * beat));
        float phase = hud.Beats.Phase(set.flickerCyclesPerBeat);
        float wave = 0.5f + 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);
        float glow = set.glow * (1f - set.flickerAmount * (1f - wave)) + (set.flashGlow - set.glow) * flash * flash;
        HudGlow.SetPower(main, glow);

        main.rectTransform.localScale = Vector3.one * scale;
        group.alpha = alpha;

        // The echo swells the whole time and is gone by the end.
        float u = Mathf.Clamp01(e / total);
        echo.rectTransform.localScale = Vector3.one * Mathf.Lerp(set.echoStartScale, set.echoEndScale, EaseOut(u));
        var c = echo.color; c.a = set.echoAlpha * (1f - u) * Mathf.Clamp01(e / Mathf.Max(0.01f, tin)); echo.color = c;

        if (e >= total)
        {
            showing = false;
            root.gameObject.SetActive(false);
        }
    }

    private static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }
    private static float EaseInOut(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // --- building (runtime only, never saved into the scene) ---

    private void Build()
    {
        if (root != null) return;
        var go = new GameObject("CenterWord", typeof(RectTransform), typeof(CanvasGroup));
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(transform, false);
        // Behind the corner HUD, which is later in the hierarchy.
        go.transform.SetAsFirstSibling();
        root = (RectTransform)go.transform;
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = new Vector2(0f, set.centerY);
        root.sizeDelta = new Vector2(set.maxWidth, set.size * 1.3f);
        group = go.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        echo = NewText("Echo", set.size);
        main = NewText("Word", set.size);
        main.enableAutoSizing = true;
        main.fontSizeMax = set.size;
        main.fontSizeMin = set.size * 0.5f;
        go.SetActive(false);
    }

    private TMP_Text NewText(string name, float size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(root, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        var text = go.AddComponent<TextMeshProUGUI>();
        if (set.font != null) text.font = set.font;
        text.fontSize = size;
        text.fontStyle = FontStyles.Italic;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
