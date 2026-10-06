using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// The trainer as filmed clips, drawn from frames baked ahead of time (VideoSilhouetteSet.Clip.frames,
/// one Texture2DArray per loop) into the RawImage of SilhouetteTrainer's panel through the
/// Tunnel/SilhouetteFrames shader. Built by SilhouetteTrainer when its video flag is on, in place of
/// the 3D figure.
///
/// Nothing plays: every frame the shows (VideoSilhouettePlanner, one looped clip per phrase) are
/// looked up at the song clock and the shader is told which two frames to blend - so the picture
/// can never stall, seek or drift from the music. At a phrase change the new clip is blended in
/// over VideoSilhouetteSet.crossfadeSeconds in the same shader.
public class VideoSilhouette : MonoBehaviour
{
    private static readonly int FramesA = Shader.PropertyToID("_FramesA");
    private static readonly int FramesB = Shader.PropertyToID("_FramesB");
    private static readonly int ParamA = Shader.PropertyToID("_A");
    private static readonly int ParamB = Shader.PropertyToID("_B");
    private static readonly int Mix = Shader.PropertyToID("_Mix");

    private VideoSilhouetteSet set;
    private CueSpawner spawner;
    private RawImage image;
    private Material mat;
    private List<VideoSilhouettePlanner.Show> shows = new List<VideoSilhouettePlanner.Show>();
    private int current;
    private bool planned;

    public void Init(VideoSilhouetteSet videoSet, CueSpawner cueSpawner, RawImage target, RectTransform panelRect)
    {
        set = videoSet;
        spawner = cueSpawner;
        image = target;
        var shader = set.frameShader != null ? set.frameShader : Shader.Find("Tunnel/SilhouetteFrames");
        mat = new Material(shader) { name = "VideoSilhouetteFrames" };
        image.material = mat;
        image.texture = Texture2D.whiteTexture;
        image.uvRect = new Rect(0f, 0f, 1f, 1f);   // the frames are baked to the panel's crop
        // Before the chart is known: standing easy.
        var idle = set.Find(set.restClip) ?? set.Find(set.fallbackClip);
        if (idle != null) shows.Add(VideoSilhouettePlanner.Idle(idle));
        image.enabled = shows.Count > 0;
    }

    private void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }

    private bool Running => spawner != null && spawner.Clock.IsRunning;
    private float Now => Running ? spawner.Clock.SongTime : Time.time;

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
        planned = true;
        var plan = VideoSilhouettePlanner.Plan(set, chart, grids, spawner.LaneCount);
        if (plan.Count == 0) return;
        shows = plan;
        current = 0;
        image.enabled = true;
        Debug.Log("[VideoSilhouette] " + shows.Count + " shows planned", this);
    }

    private void LateUpdate()
    {
        if (shows.Count == 0) return;
        // Before the song starts the idle show runs on its own time; the plan only once it runs.
        float now = Now;
        if (planned && !Running) now = float.NegativeInfinity;

        int k = Mathf.Clamp(current, 0, shows.Count - 1);
        while (k + 1 < shows.Count && shows[k + 1].start <= now) k++;
        while (k > 0 && shows[k].start > now) k--;
        current = k;

        var cur = shows[k];
        float t = float.IsInfinity(now) ? Time.time : now;
        Bind(FramesA, ParamA, cur, t);
        float fade = Mathf.Max(0.01f, set.crossfadeSeconds);
        float a = k > 0 ? Mathf.Clamp01((now - cur.start) / fade) : 1f;
        if (a < 1f) Bind(FramesB, ParamB, shows[k - 1], t);
        else Bind(FramesB, ParamB, cur, t);
        mat.SetFloat(Mix, a);
    }

    private void Bind(int tex, int param, VideoSilhouettePlanner.Show show, float songTime)
    {
        int n = show.clip.FrameCount;
        float f = show.Frame(songTime);
        int f0 = Mathf.Min(n - 1, Mathf.FloorToInt(f));
        int f1 = f0 + 1 >= n ? 0 : f0 + 1;
        mat.SetTexture(tex, show.clip.frames);
        mat.SetVector(param, new Vector4(f0, f1, f - f0, 0f));
    }
}
