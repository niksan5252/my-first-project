using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// The trainer as filmed clips: VideoPlayer -> RenderTexture (ARGB32, the clips' alpha kept) ->
/// the RawImage in SilhouetteTrainer's panel. Built by SilhouetteTrainer when its video flag is
/// on, in place of the 3D figure.
///
/// The shows (VideoSilhouettePlanner) are played on two VideoPlayers in turn: while one is on
/// screen the other is prepared and parked on the first frame of the next show, so a switch is
/// only a change of texture - never a black frame. The one on screen follows SongClock: its speed
/// is nudged to stay on the planned clip time, and it seeks when it is too far off or a loop wraps.
public class VideoSilhouette : MonoBehaviour
{
    private class Slot
    {
        public VideoPlayer player;
        public RenderTexture rt;
        public int show = -1;        // the show it is prepared for
        public bool ready;           // parked on that show's first frame
        public bool seeking;
    }

    private const float SeekBeyond = 0.12f;      // seconds off before a seek instead of a nudge
    private const float NudgeGain = 2f;

    private VideoSilhouetteSet set;
    private CueSpawner spawner;
    private RawImage image;
    private RectTransform panel;
    private readonly Slot[] slots = new Slot[2];
    private List<VideoSilhouettePlanner.Show> shows;
    private int current = -1, shown = -1;   // the show the clock is in; the slot on screen
    private bool planned;

    public void Init(VideoSilhouetteSet videoSet, CueSpawner cueSpawner, RawImage target, RectTransform panelRect)
    {
        set = videoSet;
        spawner = cueSpawner;
        image = target;
        panel = panelRect;
        image.enabled = false;           // until the first frame is there
        for (int i = 0; i < 2; i++) slots[i] = MakeSlot(i);
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

    private void OnDestroy()
    {
        foreach (var s in slots)
            if (s != null && s.rt != null) { s.rt.Release(); Destroy(s.rt); }
    }

    private float Now => spawner != null && spawner.Clock.IsRunning ? spawner.Clock.SongTime : SilhouettePlanner.SongStart;

    private void Update()
    {
        if (planned || spawner == null || spawner.Sequencer == null || spawner.BeatmapMeta == null) return;
        var chart = spawner.Sequencer;
        float spb = 60f / (spawner.BeatmapMeta.bpm >= 20f ? spawner.BeatmapMeta.bpm : 120f);
        System.Func<float, float> beatAt = _ => spb;
        var timeline = PlaylistDirector.Active != null ? PlaylistDirector.Active.Timeline : null;
        if (timeline != null && timeline.spans.Count > 0)
            beatAt = t =>
            {
                var span = timeline.spans[0];
                foreach (var sp in timeline.spans) if (t >= sp.audioStart - 0.01f) span = sp;
                return span.secondsPerBeat;
            };
        var plan = VideoSilhouettePlanner.Plan(set, chart, beatAt, spawner.LaneCount);
        if (plan.Count == 0) { planned = true; return; }
        shows = plan;
        planned = true;
        current = -1;
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
        if (mine.show != k) Prepare(mine, k);
        if (mine.ready && shown != System.Array.IndexOf(slots, mine)) Show(mine);
        // The other slot, once it is off screen: the next show, parked.
        var other = slots[1 - System.Array.IndexOf(slots, mine)];
        if (shown != System.Array.IndexOf(slots, other) && k + 1 < shows.Count && other.show != k + 1) Prepare(other, k + 1);

        if (mine.ready) Drive(mine, shows[k], now);
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

    private void Show(Slot s)
    {
        shown = System.Array.IndexOf(slots, s);
        image.texture = s.rt;
        image.enabled = true;
    }

    private void Drive(Slot s, VideoSilhouettePlanner.Show show, float now)
    {
        var p = s.player;
        if (show.hold || now < show.start) { if (p.isPlaying) p.Pause(); return; }
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
        image.uvRect = new Rect(Mathf.Clamp(set.centerX - w * 0.5f, 0f, 1f - w), Mathf.Clamp(set.centerY - h * 0.5f, 0f, 1f - h), w, h);
    }
}
