using System.Collections.Generic;
using UnityEngine;

/// Plays a RunPlaylist through the one tunnel: the charts merged onto one song clock (see
/// PlaylistTimeline), every track scheduled on the audio clock against that same song clock, the
/// crossfades and the thunder dips as volume curves on song time, and the thunderclap itself.
/// The change of look is PlaylistThemeBlender's job; this starts it.
///
/// Only the current track and the next one hold decoded audio: a track's data is loaded a while
/// before it is due and released once it has finished.
///
/// Set up by RunProfileSelector before anything is built (Configure), after which it runs on its
/// own from the song clock - so a playlist plays the same live and in a recording.
[DefaultExecutionOrder(-500)]
public class PlaylistDirector : MonoBehaviour
{
    // A track is decoded this long before it is due, and scheduled this long before.
    private const float LoadAhead = 25f;
    private const float ScheduleAhead = 4f;

    private RunPlaylist playlist;
    private PlaylistTimeline timeline;
    private CueSpawner spawner;
    private TunnelThemeApplier applier;
    private HudController hud;
    private PlaylistThemeBlender blender;

    private AudioSource[] sources;      // two, alternating: segment k plays on sources[k % 2]
    private RetentionOverlay overlay;
    // The quiet loop under the cards: two sources taking the repetitions in turn.
    private AudioSource[] loopSources;
    private readonly List<Loop> loops = new List<Loop>();
    private readonly HashSet<int> loopScheduled = new HashSet<int>();
    private struct Loop { public AudioClip clip; public float clipFrom, length, start, end; }
    private AudioSource thunder;
    private AudioClip thunderClip;
    private readonly HashSet<int> scheduled = new HashSet<int>();
    private readonly HashSet<int> thunderScheduled = new HashSet<int>();
    private double startDsp;
    private float startAt;
    private bool clockStarted;
    private int segment = -1;

    public PlaylistTimeline Timeline => timeline;

    /// A global volume with object motion blur for the whole run: the rings and notes rushing past
    /// the lens are smeared over the frame instead of jumping (a strobe at high scroll speeds).
    private void AddMotionBlur(float intensity)
    {
        var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        var mb = profile.Add<UnityEngine.Rendering.Universal.MotionBlur>(true);
        mb.mode.Override(UnityEngine.Rendering.Universal.MotionBlurMode.CameraAndObjects);
        mb.quality.Override(UnityEngine.Rendering.Universal.MotionBlurQuality.High);
        mb.intensity.Override(intensity);
        mb.clamp.Override(0.12f);
        var go = new GameObject("MotionBlurVolume");
        go.transform.SetParent(transform, false);
        var v = go.AddComponent<UnityEngine.Rendering.Volume>();
        v.isGlobal = true;
        v.priority = 60f;
        v.sharedProfile = profile;
    }
    public RunPlaylist Playlist => playlist;
    public int Segment => segment;
    public static PlaylistDirector Active { get; private set; }

    /// Builds the merged chart and hands it, the audio and the first theme to the scene. Returns
    /// false (and logs why) when a chart is missing; the scene then plays as it is set up.
    public bool Configure(RunPlaylist list, int solo, CueSpawner cueSpawner, TunnelThemeApplier themeApplier,
                          HudController hudController, SilhouetteTrainer trainer)
    {
        playlist = list;
        spawner = cueSpawner;
        applier = themeApplier;
        hud = hudController;
        int lanes = 4;
        var tunnel = themeApplier != null ? themeApplier.Tunnel : null;
        if (tunnel != null) lanes = tunnel.LaneCount;
        if (tunnel != null && list.scrollSpeedScale > 0.01f && Mathf.Abs(list.scrollSpeedScale - 1f) > 0.001f)
            tunnel.SetScrollSpeed(tunnel.ScrollSpeed * list.scrollSpeedScale);
        if (list.cameraFov > 1f && Camera.main != null) Camera.main.fieldOfView = list.cameraFov;
        if (list.motionBlur > 0f) AddMotionBlur(list.motionBlur);
        timeline = PlaylistTimeline.Build(list, solo, lanes, out string error);
        if (timeline == null)
        {
            Debug.LogError("[Playlist] " + error, this);
            return false;
        }
        Active = this;

        spawner.SetChart(timeline.Meta, timeline.Cues);
        spawner.SetIntro(0f, false, list.preRoll);
        spawner.SetExternalAudio(AudioReady, OnClockStarted);
        string name = list.title + (solo >= 0 ? "_" + (solo + 1) + "_" + timeline.spans[0].segment.label : "");
        spawner.SetTrackInfo(name.Replace(' ', '_'), timeline.Length);
        if (themeApplier != null) themeApplier.SetTheme(timeline.spans[0].segment.theme);
        if (trainer != null && list.hideTrainer) trainer.enabled = false;     // before its Start: the panel is never built
        if (list.trainerV5 && !list.hideTrainer)
        {
            var v5 = Object.FindAnyObjectByType<SilhouetteTrainerV5>(FindObjectsInactive.Include);
            if (v5 != null)
            {
                if (trainer != null) trainer.enabled = false;
                v5.gameObject.SetActive(true);
                v5.enabled = true;
                trainer = null;                                                 // the scene trainer's options below do not apply
            }
        }
        else if (trainer != null && list.trainerMotionSet != null) trainer.UseMotionSet(list.trainerMotionSet, list.trainerTextured && !list.trainerFilmed);
        if (trainer != null && list.trainerPanelInThemeColors) trainer.UsePanelPalette();
        if (trainer != null && list.trainerFilmed && !list.hideTrainer) trainer.UseFilmedLook();
        if (trainer != null && list.trainerMocap3D && !list.hideTrainer) trainer.UseCharacterNotVideo();
        if (trainer != null && list.trainerClassic && !list.hideTrainer) trainer.UseClassicContour();
        if (list.retention)
        {
            overlay = gameObject.AddComponent<RetentionOverlay>();
            overlay.Configure(this, spawner);
            if (list.progressHud) gameObject.AddComponent<PlaylistProgressHud>().Configure(this, spawner);
            if (hud != null) hud.UseRatingLadder(list.ratingWords, list.ratingCombos);
        }

        BuildAudio();
        // The beat every environment pulses to lives here, for the whole run. Left to the
        // environments, each would find the previous one's ClubBeat when it is built, add none of
        // its own, and be left with nothing once the previous environment is destroyed at the swap.
        if (GetComponent<ClubBeat>() == null) gameObject.AddComponent<ClubBeat>();
        ClubBeat.TempoAt = TempoAt;

        blender = gameObject.AddComponent<PlaylistThemeBlender>();
        blender.Configure(this, themeApplier);

        // The first track's data now; the rest as they come up.
        var first = timeline.spans[0].segment.track;
        if (first != null) first.LoadAudioData();
        Debug.Log(timeline.Report, this);
        return true;
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        if (ClubBeat.TempoAt == (System.Func<float, Vector2>)TempoAt) ClubBeat.TempoAt = null;
        if (thunderClip != null) Destroy(thunderClip);
    }

    private void BuildAudio()
    {
        var template = spawner.MusicSource;
        sources = new AudioSource[2];
        for (int i = 0; i < 2; i++) sources[i] = NewSource("Playlist Track " + (char)('A' + i), template);
        thunder = NewSource("Playlist Thunder", template);
        thunderClip = ThunderSynth.Create(AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000);
        thunder.clip = thunderClip;
        thunder.volume = playlist.thunderVolume;
        if (playlist.retention) PlanLoops(template);
        // The scene's own music source is not used by a playlist.
        if (template != null) { template.Stop(); template.clip = null; }
    }

    private AudioSource NewSource(string name, AudioSource template)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f;
        if (template != null)
        {
            src.outputAudioMixerGroup = template.outputAudioMixerGroup;
            src.priority = template.priority;
            src.volume = template.volume;
        }
        return src;
    }

    /// The song clock waits for this: the track that plays first must be decoded.
    private bool AudioReady()
    {
        var span = timeline.spans[timeline.SegmentAt(spawner.StartAt)];
        var clip = span.segment.track;
        if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        return clip == null || clip.loadState == AudioDataLoadState.Loaded || clip.loadState == AudioDataLoadState.Failed;
    }

    /// The song clock has started: song time t is produced at dsp startDsp + t. Everything already
    /// scheduled is dropped and laid out again, so a restart part-way in (SetStartTime) works too.
    private void OnClockStarted(double dsp, float at)
    {
        startDsp = dsp;
        startAt = at;
        clockStarted = true;
        foreach (var s in sources) s.Stop();
        thunder.Stop();
        scheduled.Clear();
        thunderScheduled.Clear();
        if (loopSources != null) foreach (var s in loopSources) s.Stop();
        loopScheduled.Clear();
        Pump(at);
    }

    private void Update()
    {
        if (!clockStarted || spawner == null || !spawner.Clock.IsRunning) return;
        float now = spawner.Clock.SongTime;
        Pump(now);
        PumpLoops(now);
        ApplyVolumes(now);
        TrackSegment(now);
    }

    /// The quiet loops: under every interlude, the last `loopBars` bars of the track before it
    /// (up to its cut), repeated end to end; under the outro, the last track's. Each repetition is
    /// one scheduled play, sample-exact, on two sources in turn.
    private void PlanLoops(AudioSource template)
    {
        loopSources = new AudioSource[2];
        for (int i = 0; i < 2; i++) loopSources[i] = NewSource("Playlist Loop " + (char)('A' + i), template);
        var spans = timeline.spans;
        for (int k = 0; k < spans.Count; k++)
        {
            bool before = k + 1 < spans.Count ? spans[k + 1].IsInterlude : timeline.Outro > 0f;
            if (!before) continue;
            var s = spans[k];
            var clip = s.segment.track;
            if (clip == null) continue;
            float cut = s.segment.clipStart + s.segment.PlayedLength;
            float len = Mathf.Min(playlist.loopBars * 4f * s.secondsPerBeat, cut);
            float from = s.audioEnd, until = k + 1 < spans.Count ? spans[k + 1].audioStart : timeline.Length;
            for (float t = from; t < until - 0.05f; t += len)
                loops.Add(new Loop { clip = clip, clipFrom = cut - len, length = Mathf.Min(len, until - t), start = t, end = until });
        }
    }

    private void PumpLoops(float now)
    {
        if (loopSources == null) return;
        float master = spawner.MusicSource != null ? spawner.MusicSource.volume : 1f;
        for (int i = 0; i < loops.Count; i++)
        {
            var l = loops[i];
            if (!loopScheduled.Contains(i) && now >= l.start - ScheduleAhead && now < l.start + l.length && startAt <= l.start + l.length)
            {
                if (l.clip.loadState == AudioDataLoadState.Unloaded) l.clip.LoadAudioData();
                if (l.clip.loadState != AudioDataLoadState.Loaded) continue;
                loopScheduled.Add(i);
                var src = loopSources[i % 2];
                src.Stop();
                src.clip = l.clip;
                float from = Mathf.Max(startAt, l.start);
                src.time = Mathf.Clamp(l.clipFrom + (from - l.start), 0f, l.clip.length - 0.01f);
                src.PlayScheduled(startDsp + from);
                src.SetScheduledEndTime(startDsp + l.start + l.length);
            }
        }
        // A short fade in at each break's start and out at its end, quiet in between.
        foreach (var src in loopSources)
        {
            float v = 0f;
            foreach (var l in loops)
                if (now >= l.start - 0.1f && now <= l.end + 0.1f)
                {
                    float first = loops.Find(x => x.end == l.end).start;
                    v = Mathf.Clamp01((now - first) / 0.6f) * Mathf.Clamp01((l.end - now) / 1.2f);
                    break;
                }
            src.volume = playlist.loopVolume * v * master;
        }
    }

    /// Loads, schedules and releases tracks around `now`.
    private void Pump(float now)
    {
        for (int k = 0; k < timeline.spans.Count; k++)
        {
            var span = timeline.spans[k];
            var clip = span.segment.track;
            if (clip == null) continue;

            if (now > span.audioEnd + 1f)
            {
                if (clip.loadState == AudioDataLoadState.Loaded && !UsedLater(clip, k)) clip.UnloadAudioData();
                continue;
            }
            if (now >= span.audioStart - LoadAhead && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();

            if (!scheduled.Contains(k) && now >= span.audioStart - ScheduleAhead && clip.loadState == AudioDataLoadState.Loaded)
            {
                scheduled.Add(k);
                var src = sources[k % 2];
                src.Stop();
                src.clip = clip;
                float from = Mathf.Max(startAt, span.audioStart);
                src.time = Mathf.Min(span.segment.clipStart + (from - span.audioStart), Mathf.Max(0f, clip.length - 0.01f));
                src.volume = VolumeFor(k, from);
                src.PlayScheduled(startDsp + from);
                src.SetScheduledEndTime(startDsp + span.audioEnd);
            }

            bool rumble = span.HasTransitionIn && (span.segment.transitionIn == PlaylistTransition.Thunder
                                                   || span.segment.transitionLook == PlaylistTransitionLook.Rain);
            if (rumble
                && !thunderScheduled.Contains(k) && now >= span.inMiddle - ScheduleAhead && startAt <= span.inMiddle)
            {
                thunderScheduled.Add(k);
                thunder.Stop();
                // Rain's thunder is distant: half as loud as a thunder transition's clap.
                thunder.volume = playlist.thunderVolume * (span.segment.transitionIn == PlaylistTransition.Thunder ? 1f : 0.45f);
                thunder.PlayScheduled(startDsp + span.inMiddle - ThunderSynth.CrackAt);
            }
        }
    }

    private bool UsedLater(AudioClip clip, int k)
    {
        for (int j = k + 1; j < timeline.spans.Count; j++) if (timeline.spans[j].segment.track == clip) return true;
        return false;
    }

    private void ApplyVolumes(float now)
    {
        float master = spawner.MusicSource != null ? spawner.MusicSource.volume : 1f;
        for (int k = 0; k < timeline.spans.Count; k++)
        {
            var src = sources[k % 2];
            if (src.clip != timeline.spans[k].segment.track) continue;
            if (now < timeline.spans[k].audioStart - 0.5f || now > timeline.spans[k].audioEnd + 0.5f) continue;
            src.volume = VolumeFor(k, now) * master;
        }
    }

    /// The volume policy of the transitions: a crossfade is equal-power over the whole transition;
    /// a thunder transition dips the outgoing track to nothing by the clap and brings the incoming
    /// one in under it from thunderTrackStartVolume.
    private float VolumeFor(int k, float t)
    {
        var span = timeline.spans[k];
        float v = 1f;
        if (span.IsInterlude)
            v *= Mathf.Clamp01((t - span.audioStart) / 0.05f);
        else if (span.HasTransitionIn)
        {
            if (span.segment.transitionIn == PlaylistTransition.Crossfade || span.IsCut)
                v *= Mathf.Sin(Mathf.Clamp01((t - span.inStart) / (span.inEnd - span.inStart)) * Mathf.PI * 0.5f);
            else
                v *= Mathf.Lerp(playlist.thunderTrackStartVolume, 1f,
                                Mathf.Clamp01((t - span.audioStart) / Mathf.Max(0.01f, playlist.thunderRiseSeconds)));
        }
        if (k + 1 == timeline.spans.Count && playlist.finalFadeSeconds > 0f)
            v *= Mathf.Clamp01((span.audioEnd - t) / playlist.finalFadeSeconds);
        if (k + 1 < timeline.spans.Count)
        {
            var next = timeline.spans[k + 1];
            if (next.IsInterlude)
                v *= Mathf.Clamp01((span.audioEnd - t) / 0.05f);
            else if (next.segment.transitionIn == PlaylistTransition.Crossfade || next.IsCut)
                v *= Mathf.Cos(Mathf.Clamp01((t - next.inStart) / (next.inEnd - next.inStart)) * Mathf.PI * 0.5f);
            else
            {
                float x = Mathf.Clamp01((t - next.inStart) / Mathf.Max(0.01f, next.inMiddle - next.inStart));
                v *= 1f - x * x;
            }
        }
        return Mathf.Clamp01(v);
    }

    /// The musical side of a segment change, on the transition's middle: the tempo the HUD, the
    /// obstacles and the chart's metadata use becomes the new track's.
    private void TrackSegment(float now)
    {
        int k = timeline.SegmentAt(now);
        if (k == segment) return;
        segment = k;
        var span = timeline.spans[k];
        if (spawner.BeatmapMeta != null) spawner.BeatmapMeta.bpm = span.bpm;
        spawner.SetTempo(span.bpm);
        if (hud != null) hud.Beats.Reset();
        // Every level starts its combo from 0 (the spoiler's clips do not count).
        if (playlist.retention && hud != null && span.segment.role == PlaylistSegmentRole.Level) hud.ResetCounter();
        Debug.Log("[Playlist] segment " + (k + 1) + " '" + span.segment.label + "' from song " + now.ToString("F2") + " s, bpm " + span.bpm.ToString("0.###"), this);
    }

    /// For ClubBeat: the grid of the track that is playing at `t`.
    private Vector2 TempoAt(float t)
    {
        var span = timeline.spans[timeline.SegmentAt(t)];
        return new Vector2(span.gridAnchor, span.secondsPerBeat);
    }
}
