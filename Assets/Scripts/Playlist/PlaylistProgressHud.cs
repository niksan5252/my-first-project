using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Video 20 ("make them watch to the end"): a strip at the top centre over the whole run -
/// LEVEL k/N, a bar filling over the workout (the levels' music only, not the cards), and a kcal
/// counter that climbs while a level plays. A chart's SPEED ROUND (PlaylistTimeline.Span.speedStart)
/// gets a pulsing banner with a countdown, a wider lens and the coach's call; each level start is
/// called out too ("Level three!"). Everything runs on the song clock, so it is in the recording.
public class PlaylistProgressHud : MonoBehaviour
{
    private PlaylistDirector director;
    private CueSpawner spawner;
    private Canvas canvas;
    private TMP_Text levelText, kcalText, speedText;
    private Image barBack, barFill;
    private float total, baseFov;
    private int lastLevel = -1;
    private bool speedCalled, speedDoneCalled;

    private static readonly Color Gold = new Color(1f, 0.82f, 0.15f);
    private static readonly Color Hot = new Color(1f, 0.25f, 0.35f);

    /// The seconds of music that belong to levels (the bar's and the kcal counter's whole).
    public static float LevelSeconds(PlaylistTimeline tl)
    {
        float s = 0f;
        foreach (var sp in tl.spans) if (sp.segment.role == PlaylistSegmentRole.Level) s += sp.audioEnd - sp.audioStart;
        return s;
    }

    public void Configure(PlaylistDirector owner, CueSpawner cueSpawner)
    {
        director = owner;
        spawner = cueSpawner;
        total = Mathf.Max(1f, LevelSeconds(owner.Timeline));
        baseFov = Camera.main != null ? Camera.main.fieldOfView : 60f;
        Build();
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private void Build()
    {
        var go = new GameObject("ProgressHud");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;     // over the HUD (100), under the cards (300)
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        levelText = Text("Level", 48f, new Vector2(0f, -38f), 600f);
        barBack = Bar("BarBack", new Color(0.05f, 0.05f, 0.1f, 0.7f), 620f);
        barFill = Bar("BarFill", Gold, 620f);
        barFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        barFill.rectTransform.anchoredPosition = new Vector2(-310f, -82f);
        kcalText = Text("Kcal", 34f, new Vector2(0f, -122f), 600f);
        kcalText.color = new Color(1f, 0.6f, 0.25f);
        speedText = Text("Speed", 92f, new Vector2(0f, -230f), 1400f);
        speedText.color = Hot;
        speedText.outlineWidth = 0.22f;
        speedText.outlineColor = new Color32(40, 0, 10, 255);
        SetVisible(false);
    }

    private TMP_Text Text(string name, float size, Vector2 pos, float width)
    {
        var t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        t.rectTransform.SetParent(canvas.transform, false);
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        t.rectTransform.sizeDelta = new Vector2(width, size * 1.5f);
        t.rectTransform.anchoredPosition = pos;
        t.alignment = TextAlignmentOptions.Center;
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.color = Color.white;
        t.outlineWidth = 0.18f;
        t.outlineColor = new Color32(0, 0, 0, 200);
        t.raycastTarget = false;
        return t;
    }

    private Image Bar(string name, Color c, float width)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        img.rectTransform.SetParent(canvas.transform, false);
        img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        img.rectTransform.sizeDelta = new Vector2(width, 18f);
        img.rectTransform.anchoredPosition = new Vector2(0f, -82f);
        img.sprite = null;          // a solid strip
        img.type = Image.Type.Simple;
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    private void SetVisible(bool on)
    {
        levelText.enabled = kcalText.enabled = barBack.enabled = barFill.enabled = on;
        if (!on) speedText.enabled = false;
    }

    private void LateUpdate()
    {
        if (canvas == null || spawner == null || !spawner.Clock.IsRunning || director == null || director.Timeline == null) return;
        float now = spawner.Clock.SongTime;
        var tl = director.Timeline;

        // Where we are: the level playing (or the last one finished), and the level music so far.
        int levels = 0, current = -1;
        float done = 0f;
        PlaylistTimeline.Span playing = null;
        foreach (var sp in tl.spans)
        {
            if (sp.segment.role != PlaylistSegmentRole.Level) continue;
            levels++;
            if (now >= sp.audioStart) { current = levels; done += Mathf.Clamp(now, sp.audioStart, sp.audioEnd) - sp.audioStart; }
            if (now >= sp.audioStart && now < sp.audioEnd) playing = sp;
        }
        bool show = playing != null;
        SetVisible(show);

        if (current != lastLevel && playing != null && now - playing.audioStart < 1.5f)
        {
            lastLevel = current;
            if (director.Playlist.coachVoice) VoiceOver.SayNow(current == levels ? "LEVEL_FINAL" : "LEVEL_" + current);
        }
        if (!show) { ResetFov(); return; }

        levelText.text = "LEVEL " + current + "/" + levels;
        float p = Mathf.Clamp01(done / total);
        barFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(18f, 620f * p), 18f);
        int kcal = Mathf.FloorToInt(done / 60f * director.Playlist.kcalPerMinute);
        kcalText.text = kcal + " KCAL BURNED";

        // The SPEED ROUND: a banner pulsing on the beat with the seconds left, the lens a little wider.
        bool fast = !float.IsNaN(playing.speedStart) && now >= playing.speedStart && now < playing.speedEnd;
        speedText.enabled = fast;
        var cam = Camera.main;
        if (fast)
        {
            if (!speedCalled) { speedCalled = true; if (director.Playlist.coachVoice) VoiceOver.SayNow("SPEED_ROUND"); }
            float age = now - playing.speedStart, left = playing.speedEnd - now;
            float beat = Mathf.Repeat(age / Mathf.Max(0.2f, playing.secondsPerBeat), 1f);
            speedText.text = age < 2.5f ? "SPEED ROUND!" : "SPEED ROUND  " + Mathf.CeilToInt(left);
            speedText.fontSize = age < 2.5f ? 110f : 64f;
            speedText.rectTransform.localScale = Vector3.one * (1f + 0.08f * Mathf.Pow(1f - beat, 3f));
            if (cam != null) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov + 9f, Time.deltaTime * 3f);
        }
        else
        {
            if (speedCalled && !speedDoneCalled && !float.IsNaN(playing.speedEnd) && now >= playing.speedEnd)
            {
                speedDoneCalled = true;
                if (director.Playlist.coachVoice) VoiceOver.SayNow("SPEED_DONE");
            }
            ResetFov();
        }
    }

    private void ResetFov()
    {
        var cam = Camera.main;
        if (cam != null && Mathf.Abs(cam.fieldOfView - baseFov) > 0.01f)
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov, Time.deltaTime * 3f);
    }
}
