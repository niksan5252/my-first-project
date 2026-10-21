using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Video 25 (after the competitor's "LEVEL 1 - JUMP / DUCK / LEFT-RIGHT / FREEZE" cards): before
/// every level, black with speed lines in the world's colour, LEVEL N and the world's name on top
/// and up to four of the level's moves as white silhouettes with a neon rim, named under them.
/// The moves are read off the level's own notes (the most telling first). Level 1's card covers
/// the first seconds of its music, the others fill the break before their level (RunPlaylist.
/// levelCards turns RetentionOverlay's own break cards off). Nothing here touches the gameplay.
public class LevelCards : MonoBehaviour
{
    private sealed class Card { public float start, end; public string title, world; public Color color; public List<(Texture2D icon, string name)> moves; }

    private CueSpawner spawner;
    private readonly List<Card> cards = new List<Card>();
    private Canvas canvas;
    private Image black;
    private readonly List<Image> rays = new List<Image>();
    private TMP_Text title, world;
    private readonly List<RawImage> icons = new List<RawImage>();
    private readonly List<Outline> rims = new List<Outline>();
    private readonly List<TMP_Text> names = new List<TMP_Text>();
    private Card shown;

    public const float FirstCardSeconds = 3.2f;

    // What a level's notes say about its moves, the most telling first: (does the chart have it, icon, name).
    private static readonly (System.Func<BeatmapCue, bool> has, string icon, string name)[] Moves =
    {
        (c => c.type == CueType.PoseWall, "pose_star", "POSE WALLS"),
        (c => c.type == CueType.ArmGate, "armgate", "ARMS OUT"),
        (c => c.type == CueType.Knee, "knee", "HIGH KNEES"),
        (c => c.type == CueType.Crunch, "crunch", "ELBOW TO KNEE"),
        (c => c.type == CueType.ArcHold && c.shape != 0, "armflow", "ARM WAVES"),
        (c => c.type == CueType.CurbGap || c.type == CueType.CurbSplit, "pose_flamingo", "BALANCE"),
        (c => c.type == CueType.SquatHold, "pose_squat", "SQUAT"),
        (c => c.type == CueType.Dodge, "dodge", "DODGE"),
        (c => c.type == CueType.Duck, "duck", "DUCK"),
        (c => c.type == CueType.Curb, "jump", "JUMP"),
        (c => c.type == CueType.Punch || c.type == CueType.PunchHigh || c.type == CueType.PunchHold, "pose_horse", "PUNCH"),
        (c => c.type == CueType.Kick || c.type == CueType.KickHold, "step", "STEP"),
    };

    public void Configure(PlaylistDirector owner, CueSpawner cueSpawner)
    {
        spawner = cueSpawner;
        var tl = owner.Timeline;
        int level = 0;
        foreach (var s in tl.spans)
        {
            if (s.segment.role != PlaylistSegmentRole.Level) continue;
            level++;
            var picked = new List<(Texture2D, string)>();
            foreach (var m in Moves)
            {
                if (picked.Count >= 4) break;
                bool has = false;
                foreach (var c in tl.Cues) if (c.time >= s.audioStart && c.time < s.audioEnd && m.has(c)) { has = true; break; }
                if (has) picked.Add((Resources.Load<Texture2D>("CardIcons/" + m.icon), m.name));
            }
            string label = s.segment.label ?? "";
            int sp = label.IndexOf(' ', label.StartsWith("Level ") ? 6 : 0);
            var theme = s.segment.theme;
            cards.Add(new Card
            {
                start = s.IsInterlude ? s.inStart : s.audioStart,
                end = s.IsInterlude ? s.audioStart : s.audioStart + FirstCardSeconds,
                title = "LEVEL " + level, world = (sp >= 0 ? label.Substring(sp + 1) : label).ToUpperInvariant(),
                color = theme != null ? theme.primaryColor : new Color(0f, 0.9f, 1f), moves = picked,
            });
        }
        Build();
    }

    private void Build()
    {
        var go = new GameObject("LevelCards");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 320;     // over everything, the retention cards included
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        black = new GameObject("Black", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        black.rectTransform.SetParent(go.transform, false);
        black.rectTransform.anchorMin = Vector2.zero; black.rectTransform.anchorMax = Vector2.one;
        black.rectTransform.offsetMin = black.rectTransform.offsetMax = Vector2.zero;
        black.color = new Color(0.01f, 0.01f, 0.02f, 1f);
        black.raycastTarget = false;

        // Speed lines from the middle out, in the world's colour.
        var rng = new System.Random(25);
        for (int i = 0; i < 56; i++)
        {
            var r = new GameObject("Ray", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            r.rectTransform.SetParent(go.transform, false);
            r.rectTransform.anchorMin = r.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            r.rectTransform.pivot = new Vector2(0f, 0.5f);
            r.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 360f / 56f + (float)rng.NextDouble() * 5f);
            r.rectTransform.sizeDelta = new Vector2(500f + (float)rng.NextDouble() * 500f, 4f + (float)rng.NextDouble() * 10f);
            r.raycastTarget = false;
            rays.Add(r);
        }

        title = Text(go.transform, "Title", 170f, new Vector2(0f, 380f));
        DisplayFonts.Use(title, DisplayFonts.Bungee, 0.18f);
        world = Text(go.transform, "World", 60f, new Vector2(0f, 255f));
        DisplayFonts.Use(world, DisplayFonts.Monoton, 0.1f);
        world.wordSpacing = 60f;
        for (int i = 0; i < 4; i++)
        {
            var ic = new GameObject("Move" + i, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            ic.rectTransform.SetParent(go.transform, false);
            ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            ic.rectTransform.sizeDelta = new Vector2(330f, 330f);
            ic.raycastTarget = false;
            var rim = ic.gameObject.AddComponent<Outline>();
            rim.effectDistance = new Vector2(5f, -5f);
            icons.Add(ic); rims.Add(rim);
            var n = Text(go.transform, "Name" + i, 54f, Vector2.zero);
            DisplayFonts.Use(n, DisplayFonts.Russo, 0.25f);
            names.Add(n);
        }
        canvas.gameObject.SetActive(false);
    }

    private static TMP_Text Text(Transform parent, string name, float size, Vector2 pos)
    {
        var t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        t.rectTransform.SetParent(parent, false);
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        t.rectTransform.sizeDelta = new Vector2(1800f, size * 1.4f);
        t.rectTransform.anchoredPosition = pos;
        t.alignment = TextAlignmentOptions.Center;
        t.fontSize = size;
        t.enableAutoSizing = true; t.fontSizeMin = size * 0.4f; t.fontSizeMax = size;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.fontStyle = FontStyles.Italic;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    private void LateUpdate()
    {
        if (canvas == null || spawner == null || !spawner.Clock.IsRunning) return;
        float now = spawner.Clock.SongTime;
        Card card = null;
        foreach (var c in cards) if (now >= c.start && now < c.end) { card = c; break; }
        if (card == null) { if (canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false); shown = null; return; }
        if (!canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
        if (card != shown) Show(card);

        float t = now - card.start, left = card.end - now;
        float fade = Mathf.Clamp01(left / 0.25f);                      // out in a quarter second
        black.color = new Color(0.01f, 0.01f, 0.02f, fade);
        for (int i = 0; i < rays.Count; i++)
        {
            float u = Mathf.Repeat(t * 1.1f + i * 0.618f, 1f);
            rays[i].rectTransform.anchoredPosition = Vector2.zero;
            rays[i].rectTransform.localScale = new Vector3(0.4f + 1.4f * u, 1f, 1f);
            var c = card.color; c.a = (1f - u) * 0.75f * fade;
            rays[i].color = c;
        }
        // The title slams in, the moves pop in one after another, a beat-ish pulse on them.
        title.rectTransform.localScale = Vector3.one * (1f + 0.6f * Mathf.Exp(-t * 9f));
        SetAlpha(title, fade); SetAlpha(world, Mathf.Clamp01((t - 0.15f) / 0.2f) * fade);
        for (int i = 0; i < card.moves.Count; i++)
        {
            float k = Mathf.Clamp01((t - 0.3f - i * 0.15f) / 0.2f);
            float pulse = 1f + 0.05f * Mathf.Sin(t * 8f + i);
            icons[i].rectTransform.localScale = Vector3.one * (k * pulse);
            icons[i].color = new Color(1f, 1f, 1f, fade);
            SetAlpha(names[i], k * fade);
        }
    }

    private static void SetAlpha(TMP_Text t, float a) { var c = t.color; c.a = a; t.color = c; }

    private void Show(Card card)
    {
        shown = card;
        title.text = card.title;
        world.text = card.world;
        world.color = Color.Lerp(card.color, Color.white, 0.3f);
        int n = card.moves.Count;
        float gap = n <= 1 ? 0f : Mathf.Min(440f, 1700f / n);
        for (int i = 0; i < 4; i++)
        {
            bool on = i < n;
            icons[i].gameObject.SetActive(on);
            names[i].gameObject.SetActive(on);
            if (!on) continue;
            float x = (i - (n - 1) * 0.5f) * gap;
            icons[i].texture = card.moves[i].icon;
            icons[i].rectTransform.anchoredPosition = new Vector2(x, -60f);
            rims[i].effectColor = card.color;
            names[i].text = card.moves[i].name;
            names[i].rectTransform.anchoredPosition = new Vector2(x, -275f);
            names[i].rectTransform.sizeDelta = new Vector2(gap > 0f ? gap : 600f, 80f);
        }
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }
}
