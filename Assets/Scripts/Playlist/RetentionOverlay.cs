using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// The retention wrapper of a playlist (video 9, after the competitor's format), drawn over
/// everything on the song clock, so it is the same live and in a recording:
///
///   * SPOILER - a huge yellow word over the playlist's Spoiler segments (short clips of the later
///     levels, cut together at the very start);
///   * the opening cards, on the Interlude after the spoiler: WELCOME -> "THIS VIDEO HAS N LEVELS!
///     don't leave between tracks" -> HAVE FUN!, white bold on black with speed lines;
///   * between two levels (an Interlude), over the last frame of the level frozen and blurred:
///     VICTORY -> LEVEL k/N COMPLETE! -> VERY GOOD!/INSANE! (+ THAT WAS JUST A WARM-UP! after the
///     first) -> NEXT LEVEL! or, before the last, FINAL LEVEL UNLOCKED, with a small line to
///     pause anytime; the look changes underneath;
///   * after the last level (the timeline's Outro): VICTORY -> I HOPE YOU ENJOYED IT! -> the end
///     screen: SUBSCRIBE FOR MORE!, THANK YOU!, NEXT LEVEL and two empty frames for end cards.
///
/// The cards are laid out once from the timeline (Configure); the quiet music under them is the
/// director's. Nothing here touches the gameplay.
public class RetentionOverlay : MonoBehaviour
{
    private enum Back { None, Black, Frozen }

    private sealed class Card
    {
        public float start, end;
        public Back back;
        public string big, small;
        public Color color = Color.white;
        public bool endScreen;
        public float freezeAt = -1f;     // Frozen: the moment whose frame is shown
    }

    private PlaylistDirector director;
    private CueSpawner spawner;
    private readonly List<Card> cards = new List<Card>();
    private float spoilerStart = float.PositiveInfinity, spoilerEnd = float.NegativeInfinity;

    private Canvas canvas;
    private RawImage frozen;
    private Image black;
    private RectTransform lines;
    private readonly List<RectTransform> lineBars = new List<RectTransform>();
    private readonly List<Image> lineImages = new List<Image>();
    private TMP_Text bigText, smallText, spoilerText;
    private TMP_Text endSubscribe, endThanks, endNext;
    private RectTransform endFrames;
    private readonly List<float> captures = new List<float>();
    private readonly Dictionary<float, RenderTexture> frames = new Dictionary<float, RenderTexture>();
    private readonly HashSet<float> captureStarted = new HashSet<float>();
    private Card shown;

    private static readonly Color Gold = new Color(1f, 0.82f, 0.15f);
    private static readonly Color SpoilerYellow = new Color(1f, 0.9f, 0f);

    public void Configure(PlaylistDirector owner, CueSpawner cueSpawner)
    {
        director = owner;
        spawner = cueSpawner;
        var tl = owner.Timeline;
        var list = owner.Playlist;
        int levels = 0;
        foreach (var s in tl.spans) if (s.segment.role == PlaylistSegmentRole.Level) levels++;

        int level = 0;
        for (int k = 0; k < tl.spans.Count; k++)
        {
            var s = tl.spans[k];
            if (s.segment.role == PlaylistSegmentRole.Spoiler)
            {
                spoilerStart = Mathf.Min(spoilerStart, s.audioStart);
                spoilerEnd = Mathf.Max(spoilerEnd, s.audioEnd);
                continue;
            }
            if (s.IsInterlude)
            {
                float a = s.inStart, d = s.transitionLength;
                if (tl.spans[k - 1].segment.role == PlaylistSegmentRole.Spoiler)
                {
                    float c = d / 3f;
                    Add(a, a + c, Back.Black, list.welcomeText, null);
                    Add(a + c, a + 2f * c, Back.Black, "⚠ THIS VIDEO HAS " + levels + " LEVELS!", "don't leave between tracks");
                    Add(a + 2f * c, a + d, Back.Black, "HAVE FUN!", null);
                }
                else
                {
                    bool final = level + 1 == levels;
                    Add(a, a + 2.5f, Back.Frozen, "VICTORY", null, Gold).freezeAt = a;
                    Add(a + 2.5f, a + 5.5f, Back.Frozen, "LEVEL " + level + "/" + levels + " COMPLETE!", null).freezeAt = a;
                    Add(a + 5.5f, a + 9f, Back.Frozen, level == 1 ? "VERY GOOD!" : "INSANE!", level == 1 ? "THAT WAS JUST A WARM-UP!" : "YOU'RE ON FIRE!").freezeAt = a;
                    Add(a + 9f, a + d, Back.Frozen, final ? "FINAL LEVEL UNLOCKED" : "NEXT LEVEL!",
                        "Pause anytime you need — catch your breath and jump back in", final ? Gold : Color.white).freezeAt = a;
                }
            }
            level++;
        }
        if (tl.Outro > 0f)
        {
            float a = tl.MusicEnd, end = tl.Length, screen = Mathf.Min(list.endScreenSeconds, tl.Outro - 4f);
            Add(a, a + 2.5f, Back.Frozen, "VICTORY", null, Gold).freezeAt = a;
            Add(a + 2.5f, end - screen, Back.Frozen, "I HOPE YOU ENJOYED IT!", null).freezeAt = a;
            var e = Add(end - screen, end + 5f, Back.Frozen, null, null);
            e.freezeAt = a;
            e.endScreen = true;
        }
        foreach (var c in cards) if (c.freezeAt >= 0f && !captures.Contains(c.freezeAt)) captures.Add(c.freezeAt);
        Build();
    }

    private Card Add(float start, float end, Back back, string big, string small, Color? color = null)
    {
        var c = new Card { start = start, end = end, back = back, big = big, small = small, color = color ?? Color.white };
        cards.Add(c);
        return c;
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
        foreach (var rt in frames.Values) if (rt != null) { rt.Release(); Destroy(rt); }
    }

    // --- building ---

    private void Build()
    {
        var go = new GameObject("RetentionOverlay");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;     // over the HUD (100) and the trainer (50)
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        frozen = Full<RawImage>("Frozen");
        frozen.color = new Color(0.5f, 0.5f, 0.55f, 1f);
        black = Full<Image>("Black");
        black.color = new Color(0.01f, 0.01f, 0.02f, 1f);

        lines = new GameObject("SpeedLines", typeof(RectTransform)).GetComponent<RectTransform>();
        lines.SetParent(canvas.transform, false);
        lines.anchorMin = lines.anchorMax = new Vector2(0.5f, 0.5f);
        var rng = new System.Random(9);
        for (int i = 0; i < 64; i++)
        {
            var img = new GameObject("Line", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            img.rectTransform.SetParent(lines, false);
            img.rectTransform.pivot = new Vector2(0f, 0.5f);
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 360f / 64f + (float)rng.NextDouble() * 4f);
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            lineBars.Add(img.rectTransform);
            lineImages.Add(img);
        }

        bigText = Text("Big", 118f, new Vector2(0f, 40f), 1700f);
        smallText = Text("Small", 46f, new Vector2(0f, -90f), 1600f);
        smallText.fontStyle = FontStyles.Normal;
        spoilerText = Text("Spoiler", 230f, new Vector2(0f, 300f), 1800f);
        spoilerText.text = "SPOILER";
        spoilerText.color = SpoilerYellow;
        spoilerText.outlineWidth = 0.22f;
        spoilerText.outlineColor = new Color32(30, 20, 0, 255);

        // The end screen: two rows of words and two empty frames for the platform's end cards.
        endSubscribe = Text("Subscribe", 104f, new Vector2(0f, 400f), 1700f);
        endSubscribe.text = "SUBSCRIBE FOR MORE!";
        endThanks = Text("Thanks", 64f, new Vector2(0f, 290f), 1600f);
        endThanks.text = "THANK YOU!";
        endNext = Text("Next", 56f, new Vector2(0f, 150f), 1600f);
        endNext.text = "NEXT LEVEL";
        endNext.color = Gold;
        endFrames = new GameObject("EndFrames", typeof(RectTransform)).GetComponent<RectTransform>();
        endFrames.SetParent(canvas.transform, false);
        endFrames.anchorMin = endFrames.anchorMax = new Vector2(0.5f, 0.5f);
        foreach (float x in new[] { -400f, 400f })
        {
            var frame = new GameObject("EndCardFrame", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            frame.rectTransform.SetParent(endFrames, false);
            frame.rectTransform.sizeDelta = new Vector2(704f, 396f);
            frame.rectTransform.anchoredPosition = new Vector2(x, -170f);
            frame.sprite = SilhouettePanelSprites.EdgeRound(18f, 4f);
            frame.type = Image.Type.Sliced;
            frame.color = new Color(1f, 1f, 1f, 0.9f);
            frame.raycastTarget = false;
        }
        SetVisible(null, 0f);
    }

    private T Full<T>(string name) where T : Graphic
    {
        var g = new GameObject(name, typeof(RectTransform), typeof(T)).GetComponent<T>();
        g.rectTransform.SetParent(canvas.transform, false);
        g.rectTransform.anchorMin = Vector2.zero;
        g.rectTransform.anchorMax = Vector2.one;
        g.rectTransform.offsetMin = g.rectTransform.offsetMax = Vector2.zero;
        g.raycastTarget = false;
        return g;
    }

    private TMP_Text Text(string name, float size, Vector2 pos, float width)
    {
        var t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        t.rectTransform.SetParent(canvas.transform, false);
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        t.rectTransform.sizeDelta = new Vector2(width, size * 1.6f);
        t.rectTransform.anchoredPosition = pos;
        t.alignment = TextAlignmentOptions.Center;
        t.fontSize = size;
        t.enableAutoSizing = true;
        t.fontSizeMin = size * 0.4f;
        t.fontSizeMax = size;
        t.fontStyle = FontStyles.Bold;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    // --- every frame ---

    private void LateUpdate()
    {
        if (canvas == null || spawner == null || !spawner.Clock.IsRunning) return;
        float now = spawner.Clock.SongTime;

        // The frozen frames: taken just before the cards that show them cover the screen.
        foreach (float at in captures)
            if (!captureStarted.Contains(at) && now >= at - 0.06f && now < at + 1f)
            {
                captureStarted.Add(at);
                StartCoroutine(Capture(at));
            }

        Card card = null;
        foreach (var c in cards) if (now >= c.start && now < c.end) { card = c; break; }
        SetVisible(card, now);

        bool spoiler = now >= spoilerStart && now < spoilerEnd;
        spoilerText.enabled = spoiler;
        if (spoiler)
        {
            float beat = Mathf.Repeat(now * 2f, 1f);
            spoilerText.rectTransform.localScale = Vector3.one * (1f + 0.05f * Mathf.Pow(1f - beat, 3f));
            spoilerText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -4f);
        }
    }

    private void SetVisible(Card card, float now)
    {
        bool any = card != null;
        black.enabled = any && card.back == Back.Black;
        bool frozenOn = any && card.back == Back.Frozen && frames.TryGetValue(card.freezeAt, out var rt) && rt != null;
        frozen.enabled = frozenOn;
        if (frozenOn) frozen.texture = frames[card.freezeAt];
        if (any && card.back == Back.Frozen && !frozenOn) { black.enabled = true; }   // a frame not captured (a start mid-way)

        bool end = any && card.endScreen;
        endSubscribe.enabled = endThanks.enabled = endNext.enabled = end;
        endFrames.gameObject.SetActive(end);
        if (end) frozen.color = new Color(0.32f, 0.32f, 0.36f, 1f);
        else frozen.color = new Color(0.5f, 0.5f, 0.55f, 1f);

        bool words = any && !end && card.big != null;
        bigText.enabled = words;
        smallText.enabled = words && !string.IsNullOrEmpty(card.small);
        lines.gameObject.SetActive(any);
        if (!any) { shown = null; return; }

        float age = now - card.start, left = card.end - now;
        // In with a punch (0.6 -> 1.08 -> 1 over a quarter second), out with a fade.
        float pop = age < 0.12f ? Mathf.Lerp(0.6f, 1.08f, age / 0.12f) : age < 0.25f ? Mathf.Lerp(1.08f, 1f, (age - 0.12f) / 0.13f) : 1f;
        float alpha = Mathf.Clamp01(left / 0.2f) * Mathf.Clamp01(age / 0.08f);
        if (words)
        {
            if (shown != card) { bigText.text = card.big; smallText.text = card.small ?? ""; }
            bigText.color = new Color(card.color.r, card.color.g, card.color.b, alpha);
            bigText.rectTransform.localScale = Vector3.one * pop;
            smallText.color = new Color(1f, 1f, 1f, alpha * 0.9f);
        }
        if (end)
        {
            endSubscribe.rectTransform.localScale = Vector3.one * (1f + 0.04f * Mathf.Pow(1f - Mathf.Repeat(age, 1f), 3f));
        }
        shown = card;
        UpdateLines(now, end ? 0.35f : 1f);
    }

    // Speed lines: thin streaks rushing out from the middle, longer and brighter as they go.
    private void UpdateLines(float now, float strength)
    {
        float reach = 1300f;
        for (int i = 0; i < lineBars.Count; i++)
        {
            float u = Mathf.Repeat(now * 0.9f + i * 0.618f, 1f);
            float r = Mathf.Lerp(260f, reach, u);
            float len = Mathf.Lerp(40f, 420f, u * u);
            var bar = lineBars[i];
            float a = bar.localEulerAngles.z * Mathf.Deg2Rad;
            bar.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            bar.sizeDelta = new Vector2(len, Mathf.Lerp(2f, 5f, u));
            lineImages[i].color = new Color(1f, 1f, 1f, 0.32f * strength * Mathf.Sin(u * Mathf.PI));
        }
    }

    // The frame as it is on screen, blurred: read back once, then halved down to a 32nd and back
    // up to an 8th, bilinear every step - a soft blur at no cost per frame.
    private IEnumerator Capture(float at)
    {
        yield return new WaitForEndOfFrame();
        var shot = ScreenCapture.CaptureScreenshotAsTexture();
        if (shot == null) yield break;
        int w = shot.width, h = shot.height;
        RenderTexture Rt(int d) { var r = new RenderTexture(Mathf.Max(8, w / d), Mathf.Max(8, h / d), 0) { filterMode = FilterMode.Bilinear }; r.Create(); return r; }
        var a = Rt(4); Graphics.Blit(shot, a);
        var b = Rt(16); Graphics.Blit(a, b);
        var c = Rt(32); Graphics.Blit(b, c);
        var d = Rt(8); Graphics.Blit(c, d);
        foreach (var r in new[] { a, b, c }) { r.Release(); Destroy(r); }
        Destroy(shot);
        frames[at] = d;
    }
}
