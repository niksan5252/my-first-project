using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Video 24 (the user: "show before the start which moves are new"): over the playlist's Intro
/// segment, NEW MOVES! on top, the move's name, its cue button as it flies in the tunnel, and under
/// it the V5 trainer moved to the middle of the screen and enlarged, doing the move on the intro's
/// own notes. The moves and their times come from RunPlaylist.introMoves ("seconds|NAME|icon",
/// seconds from the intro's start, icon a Resources/CueIcons name). Nothing here touches the gameplay.
public class NewMovesIntro : MonoBehaviour
{
    private struct Move { public float at; public string name; public Texture2D icon; }

    private CueSpawner spawner;
    private float start, end;
    private readonly List<Move> moves = new List<Move>();
    private Canvas canvas;
    private Image dim, plate;
    private RawImage icon;
    private TMP_Text title, moveName;
    private SilhouetteTrainerV5 trainer;
    private RectTransform panel;
    private Vector2 homeMin, homeMax, homePivot, homePos;
    private Vector3 homeScale;
    private bool featured;
    private int shownMove = -1;

    private static readonly Color Gold = new Color(1f, 0.82f, 0.15f);
    // The button and the trainer stand left of the road, so the notes flying down its middle stay in view.
    private const float BlockX = -560f;

    public void Configure(PlaylistDirector owner, CueSpawner cueSpawner)
    {
        spawner = cueSpawner;
        PlaylistTimeline.Span intro = null;
        foreach (var s in owner.Timeline.spans) if (s.segment.role == PlaylistSegmentRole.Intro) { intro = s; break; }
        if (intro == null) { enabled = false; return; }
        start = intro.audioStart;
        end = intro.audioEnd;
        foreach (var line in owner.Playlist.introMoves)
        {
            var p = (line ?? "").Split('|');
            if (p.Length < 3 || !float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float at)) continue;
            moves.Add(new Move { at = start + at, name = p[1], icon = Resources.Load<Texture2D>("CueIcons/" + p[2]) });
        }
        Build();
    }

    private void Build()
    {
        var go = new GameObject("NewMovesIntro");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 45;      // under the trainer (50): its enlarged panel stays on top of the dim
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        dim = new GameObject("Dim", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        dim.rectTransform.SetParent(go.transform, false);
        dim.rectTransform.anchorMin = Vector2.zero; dim.rectTransform.anchorMax = Vector2.one;
        dim.rectTransform.offsetMin = dim.rectTransform.offsetMax = Vector2.zero;
        dim.color = new Color(0f, 0f, 0.02f, 0.45f);
        dim.raycastTarget = false;

        title = Text(go.transform, "Title", 96f, new Vector2(0f, 450f), Gold);
        title.text = "NEW MOVES!";
        moveName = Text(go.transform, "Move", 64f, new Vector2(0f, 352f), Color.white);
        DisplayFonts.Use(title, DisplayFonts.Bungee);
        DisplayFonts.Use(moveName, DisplayFonts.Russo);

        // The cue button: a glowing square like the signs in the tunnel, the pictogram on it.
        plate = new GameObject("Plate", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        plate.rectTransform.SetParent(go.transform, false);
        plate.rectTransform.anchorMin = plate.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        plate.rectTransform.sizeDelta = new Vector2(190f, 190f);
        plate.rectTransform.anchoredPosition = new Vector2(BlockX, 225f);
        plate.color = new Color(0f, 0.9f, 1f, 1f);
        plate.raycastTarget = false;
        var outline = plate.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.9f);
        outline.effectDistance = new Vector2(6f, -6f);
        icon = new GameObject("Icon", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        icon.rectTransform.SetParent(plate.transform, false);
        icon.rectTransform.anchorMin = new Vector2(0.1f, 0.1f); icon.rectTransform.anchorMax = new Vector2(0.9f, 0.9f);
        icon.rectTransform.offsetMin = icon.rectTransform.offsetMax = Vector2.zero;
        icon.color = Color.black;          // the pictograms are black on transparent: only the alpha counts
        icon.raycastTarget = false;
        canvas.gameObject.SetActive(false);
    }

    private TMP_Text Text(Transform parent, string name, float size, Vector2 pos, Color color)
    {
        var t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        t.rectTransform.SetParent(parent, false);
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        t.rectTransform.sizeDelta = new Vector2(1800f, size * 1.4f);
        t.rectTransform.anchoredPosition = pos;
        t.alignment = TextAlignmentOptions.Center;
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold | FontStyles.Italic;
        t.color = color;
        t.outlineWidth = 0.2f;
        t.outlineColor = new Color32(0, 0, 0, 255);
        t.raycastTarget = false;
        return t;
    }

    private void LateUpdate()
    {
        if (canvas == null || spawner == null || !spawner.Clock.IsRunning) return;
        float now = spawner.Clock.SongTime;
        bool on = now >= start - 0.5f && now < end;
        if (canvas.gameObject.activeSelf != on) canvas.gameObject.SetActive(on);
        Feature(on);
        if (!on) return;

        int m = 0;
        for (int i = 0; i < moves.Count; i++) if (now >= moves[i].at) m = i;
        if (m != shownMove && moves.Count > 0)
        {
            shownMove = m;
            moveName.text = moves[m].name;
            icon.texture = moves[m].icon;
            icon.enabled = moves[m].icon != null;
            plate.color = m % 2 == 0 ? new Color(0f, 0.9f, 1f) : new Color(1f, 0.1f, 0.72f);
        }
        // A pop as each move comes up, and a soft beat on the button.
        float since = moves.Count > 0 ? now - moves[shownMove].at : 1f;
        float pop = 1f + 0.25f * Mathf.Exp(-since * 6f) + 0.04f * ClubBeat.Pulse;
        plate.rectTransform.localScale = Vector3.one * pop;
        moveName.rectTransform.localScale = Vector3.one * (1f + 0.15f * Mathf.Exp(-since * 6f));
    }

    // The trainer's panel: under the button, enlarged - and back home after.
    private void Feature(bool on)
    {
        if (on == featured) return;
        if (panel == null)
        {
            trainer = FindObjectOfType<SilhouetteTrainerV5>();
            panel = trainer != null ? trainer.Panel : null;
            if (panel == null) return;
            homeMin = panel.anchorMin; homeMax = panel.anchorMax; homePivot = panel.pivot; homePos = panel.anchoredPosition; homeScale = panel.localScale;
        }
        featured = on;
        if (on)
        {
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 1f);
            // The trainer's canvas is 2560x1440: the panel's top just under the button (y ~ 70 of 1080).
            float scale = Mathf.Min(1.15f, 820f / Mathf.Max(1f, panel.sizeDelta.y));
            panel.localScale = homeScale * scale;
            panel.anchoredPosition = new Vector2(BlockX * 1440f / 1080f, 115f * 1440f / 1080f);
        }
        else
        {
            panel.anchorMin = homeMin; panel.anchorMax = homeMax; panel.pivot = homePivot;
            panel.anchoredPosition = homePos; panel.localScale = homeScale;
        }
    }

    private void OnDestroy()
    {
        if (featured) Feature(false);
        if (canvas != null) Destroy(canvas.gameObject);
    }
}
