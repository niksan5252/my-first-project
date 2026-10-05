using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// The trainer as filmed clips: VideoPlayer -> RenderTexture (ARGB32, the clips' alpha kept) ->
/// the RawImage in SilhouetteTrainer's panel. Built by SilhouetteTrainer when its video flag is
/// on, in place of the 3D figure.
///
/// The shows (VideoSilhouettePlanner, one looped clip per phrase) are played on two VideoPlayers in
/// turn, each into its own RawImage: while one is on screen the other is prepared and parked on the
/// first frame of the next show; at the change both play and the new one is alpha-blended in over
/// VideoSilhouetteSet.crossfadeSeconds - never a black frame. Each follows SongClock: its speed is
/// nudged to stay on the planned clip time, and it seeks when it is too far off.
public class VideoSilhouette : MonoBehaviour
{
    private class Slot
    {
        public VideoPlayer player;
        public RenderTexture rt;
        public int show = -1;        // the show it is prepared for
        public bool ready;           // parked on that show's first frame
        public bool seeking;
        public RawImage image;
    }

    private const float SeekBeyond = 0.12f;      // seconds off before a seek instead of a nudge
    private const float NudgeGain = 2f;

    private VideoSilhouetteSet set;
    private CueSpawner spawner;
    private RawImage image;
    private Color tint;
    private RectTransform panel;
    private readonly Slot[] slots = new Slot[2];
    private List<VideoSilhouettePlanner.Show> shows;
    private int current = -1;                          // the show the clock is in
    private float fadeFrom = float.NegativeInfinity;   // when it began to blend in
    private int fadeShow = -1;
    private bool planned;

    public void Init(VideoSilhouetteSet videoSet, CueSpawner cueSpawner, RawImage target, RectTransform panelRect)
    {
        set = videoSet;
        spawner = cueSpawner;
        image = target;
        panel = panelRect;
        image.enabled = false;           // until the first frame is there
        tint = image.color;
        for (int i = 0; i < 2; i++) slots[i] = MakeSlot(i);
        slots[0].image = image;
        slots[1].image = TwinImage(image);
        // Before the chart is known: the neutral stance.
        var neutral = set.Find(set.neutralClip);
        shows = new List<VideoSilhouettePlanner.Show>();
        if (neutral != null) shows.Add(VideoSilhouettePlanner.Neutral(set, neutral, float.NegativeInfinity, float.PositiveInfinity));
        Frame();
    }

    private Slot MakeSlot(int i)
    {
        var go = new GameObject("VideoSilhouettePlayer" + i);
        go.transform.SetParent(transform, false);
        var p = go.AddComponent<VideoPlayer>();
        p.playOnAwake = false;
        p.isLooping = false;
        p.skipOnDrop = true;
        p.waitForFirstFrame = true;
        p.audioOutputMode = VideoAudioOutputMode.None;
        p.renderMode = VideoRenderMode.RenderTexture;
        // Game time: in a recording (fixed capture step) the clip advances with the frames.
        p.timeUpdateMode = VideoTimeUpdateMode.GameTime;
        var rt = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32) { name = "VideoSilhouetteRT" + i };
        rt.Create();
        p.targetTexture = rt;
        var slot = new Slot { player = p, rt = rt };
        p.prepareCompleted += _ => Park(slot);
        p.seekCompleted += _ => { slot.seeking = false; if (!slot.ready && slot.show >= 0) slot.ready = true; };
        return slot;
    }

    /// A second RawImage over the first, same place and look, for the cross-fade.
    private static RawImage TwinImage(RawImage a)
    {
        var src = a.rectTransform;
        var go = new GameObject(a.gameObject.name + "B", typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform)go.transform;
        rt.SetParent(src.parent, false);
        rt.SetSiblingIndex(src.GetSiblingIndex() + 1);
        rt.anchorMin = src.anchorMin; rt.anchorMax = src.anchorMax; rt.pivot = src.pivot;
        rt.anchoredPosition = src.anchoredPosition; rt.sizeDelta = src.sizeDelta;
        rt.localScale = src.localScale; rt.localRotation = src.localRotation;
        var b = go.GetComponent<RawImage>();
        b.material = a.material;
        b.color = a.color;
        b.raycastTarget = false;
        b.enabled = false;
        return b;
    }

    private void OnDestroy()
    {
        foreach (var s in slots)
            if (s != null && s.rt != null) { s.rt.Release(); Destroy(s.rt); }
    }

    private bool Running => spawner != null && spawner.Clock.IsRunning;
    private float Now => Running ? spawner.Clock.SongTime : SilhouettePlanner.SongStart;

    private void Update()
    {
        if (planned || spawner == null || spawner.Sequencer == null || spawner.BeatmapMeta == null) return;
        var chart = spawner.Sequencer;
        // The beat grids: one per playlist track (each up to the next), or the chart's own,
        // anchored on its first note as the HUD's beat clock is.
        var grids = new List<VideoSilhouettePlanner.Grid>();
        var timeline = PlaylistDirector.Active != null ? PlaylistDirector.Active.Timeline : null;
        if (timeline != null && timeline.spans.Count > 0)
        {
            for (int i = 0; i < timeline.spans.Count; i++)
            {
                var sp = timeline.spans[i];
                grids.Add(new VideoSilhouettePlanner.Grid
                {
                    from = i == 0 ? float.NegativeInfinity : sp.audioStart,
                    to = i + 1 < timeline.spans.Count ? timeline.spans[i + 1].audioStart : float.PositiveInfinity,
                    anchor = sp.gridAnchor, secondsPerBeat = sp.secondsPerBeat,
                });
            }
        }
        else if (chart.Count > 0)
        {
            float first = float.PositiveInfinity;
            for (int i = 0; i < chart.Count; i++) first = Mathf.Min(first, chart[i].time);
            grids.Add(new VideoSilhouettePlanner.Grid
            {
                from = float.NegativeInfinity, to = float.PositiveInfinity, anchor = first,
                secondsPerBeat = 60f / (spawner.BeatmapMeta.bpm >= 20f ? spawner.BeatmapMeta.bpm : 120f),
            });
        }
        var plan = VideoSilhouettePlanner.Plan(set, chart, grids, spawner.LaneCount);
        if (plan.Count == 0) { planned = true; return; }
        shows = plan;
        planned = true;
        current = -1;
        fadeShow = -1;
        // Whatever the slots were preparing belongs to the old list.
        foreach (var s in slots) { s.show = -1; s.ready = false; }
        Debug.Log("[VideoSilhouette] " + shows.Count + " shows planned", this);
    }

    private void LateUpdate()
    {
        if (shows == null || shows.Count == 0) return;
        Frame();
        float now = Now;

        int k = Mathf.Clamp(current, 0, shows.Count - 1);
        while (k + 1 < shows.Count && shows[k + 1].start <= now) k++;
        while (k > 0 && shows[k].start > now) k--;
        current = k;

        var mine = SlotFor(k);
        var other = SlotFor(k + 1);
        if (mine.show != k) Prepare(mine, k);
        // The show before, still blending out on the other player.
        bool outgoing = k > 0 && other.show == k - 1 && other.ready && other.image.enabled;

        float a = 1f;
        if (mine.ready)
        {
            if (fadeShow != k)
            {
                fadeShow = k;
                fadeFrom = outgoing ? Mathf.Max(shows[k].start, now) : float.NegativeInfinity;
            }
            a = Mathf.Clamp01((now - fadeFrom) / Mathf.Max(0.01f, set.crossfadeSeconds));
            if (outgoing && a >= 1f) outgoing = false;
            // The incoming one over the outgoing one.
            if (mine.image.transform.GetSiblingIndex() < other.image.transform.GetSiblingIndex())
                mine.image.transform.SetSiblingIndex(other.image.transform.GetSiblingIndex());
            Paint(mine, a);
            Drive(mine, shows[k], now);
        }
        if (outgoing)
        {
            Paint(other, mine.ready ? 1f - a : 1f);
            Drive(other, shows[k - 1], now);
        }
        else
        {
            other.image.enabled = false;
            // Free: the next show, parked.
            if (k + 1 < shows.Count && other.show != k + 1) Prepare(other, k + 1);
        }
    }

    private void Paint(Slot s, float alpha)
    {
        s.image.texture = s.rt;
        s.image.color = new Color(tint.r, tint.g, tint.b, tint.a * alpha);
        s.image.enabled = alpha > 0.001f;
    }

    /// Shows alternate between the two players, so the next one is always on the free player.
    private Slot SlotFor(int show) => slots[show & 1];

    private void Prepare(Slot s, int show)
    {
        s.show = show;
        s.ready = false;
        s.seeking = false;
        var clip = shows[show].clip.clip;
        s.player.Pause();
        if (s.player.clip != clip || !s.player.isPrepared)
        {
            s.player.clip = clip;
            s.player.Prepare();     // -> Park
        }
        else Park(s);
    }

    /// Parks a prepared player on its show's first frame, paused.
    private void Park(Slot s)
    {
        if (s.show < 0 || s.show >= shows.Count) return;
        var show = shows[s.show];
        s.player.Pause();
        s.seeking = true;
        s.player.time = show.ClipTime(show.start);   // -> seekCompleted: ready
    }

    private void Drive(Slot s, VideoSilhouettePlanner.Show show, float now)
    {
        var p = s.player;
        if (show.hold || !Running || now < show.start) { if (p.isPlaying) p.Pause(); return; }
        float want = show.ClipTime(now);
        float atEnd = show.clip.end - 0.02f;
        if (!show.loop && want >= atEnd)
        {
            if (p.isPlaying) p.Pause();
            return;
        }
        if (s.seeking) return;
        float diff = (float)p.time - want;
        if (Mathf.Abs(diff) > SeekBeyond)
        {
            s.seeking = true;
            p.time = want;
        }
        p.playbackSpeed = Mathf.Max(0.05f, show.speed * Mathf.Clamp(1f - diff * NudgeGain, 0.85f, 1.15f));
        if (!p.isPlaying) p.Play();
    }

    /// The figure cut out of the landscape video to the panel's portrait shape.
    private void Frame()
    {
        if (panel == null || image == null) return;
        float panelAspect = panel.rect.width / Mathf.Max(1f, panel.rect.height);
        float h = 1f / Mathf.Max(0.5f, set.zoom);
        float w = Mathf.Min(1f, h * panelAspect * 720f / 1280f);
        var uv = new Rect(Mathf.Clamp(set.centerX - w * 0.5f, 0f, 1f - w), Mathf.Clamp(set.centerY - h * 0.5f, 0f, 1f - h), w, h);
        foreach (var s in slots) if (s != null && s.image != null) s.image.uvRect = uv;
    }
}
