using System.Collections.Generic;
using UnityEngine;

/// The club theme's conductor: where the music is in its beat and bar, and how hard the room
/// should be going. Everything that flashes in time - LED walls, lasers, strobes, speaker cones -
/// reads it, either from C# (the statics) or from shaders (the _Club* globals), so the whole room
/// moves as one.
///
/// Time is SongClock's smoothed song time and the grid is the chart's own (its bpm, anchored on its
/// first note), so the lights land on the same beats as the notes, in Play and in a recording.
///
/// Energy comes from the chart, planned once: the room idles (lowEnergy) wherever there are no
/// notes for a while - the intro, breakdowns, the outro - runs at 1 while the notes play, and a
/// drop (notes coming back after a breakdown) kicks it to 1 + dropBoost, easing back over dropBars.
public class ClubBeat : MonoBehaviour
{
    public static float SongTime { get; private set; }
    public static float BeatPhase { get; private set; }      // 0..1, 0 exactly on a beat
    public static int BeatIndex { get; private set; }
    public static int Bar { get; private set; }
    public static float Energy { get; private set; } = 0.4f;
    public static float SecondsPerBeat { get; private set; } = 0.5f;
    /// 1 on the beat, falling off quickly - the shape of a kick.
    public static float Pulse => Mathf.Pow(1f - BeatPhase, 3f);

    [SerializeField] private float lowEnergy = 0.4f;
    [SerializeField] private float dropBoost = 0.6f;
    [SerializeField] private float dropBars = 4f;
    // A gap without notes at least this long, in bars, makes the room idle and the next note a drop.
    // 4.5: the chart's kerb and hold sets leave ~3-bar gaps between note sets, which are not
    // breakdowns; the intro and the real breakdowns are far longer.
    [SerializeField] private float quietBars = 4.5f;

    private static readonly int IdSong = Shader.PropertyToID("_ClubSongTime");
    private static readonly int IdPhase = Shader.PropertyToID("_ClubBeatPhase");
    private static readonly int IdBeat = Shader.PropertyToID("_ClubBeatIndex");
    private static readonly int IdBar = Shader.PropertyToID("_ClubBar");
    private static readonly int IdEnergy = Shader.PropertyToID("_ClubEnergy");

    private CueSpawner spawner;
    private bool planned;
    private float anchor, beat = 0.5f;
    private readonly List<Vector2> loud = new List<Vector2>();   // note stretches: start, end
    private readonly List<float> drops = new List<float>();

    private void Update()
    {
        if (spawner == null) spawner = FindAnyObjectByType<CueSpawner>();
        if (!planned && spawner != null && spawner.Sequencer != null && spawner.BeatmapMeta != null) Plan();

        bool running = spawner != null && spawner.Clock.IsRunning;
        float t = running ? spawner.Clock.SongTime : Time.time;
        float beats = (t - anchor) / beat;
        SongTime = t;
        SecondsPerBeat = beat;
        BeatIndex = Mathf.FloorToInt(beats);
        BeatPhase = beats - BeatIndex;
        Bar = Mathf.FloorToInt(beats / 4f);
        Energy = planned ? EnergyAt(t) : lowEnergy;

        Shader.SetGlobalFloat(IdSong, t);
        Shader.SetGlobalFloat(IdPhase, BeatPhase);
        Shader.SetGlobalFloat(IdBeat, BeatIndex);
        Shader.SetGlobalFloat(IdBar, Bar);
        Shader.SetGlobalFloat(IdEnergy, Energy);
    }

    private void Plan()
    {
        planned = true;
        var chart = spawner.Sequencer;
        float bpm = spawner.BeatmapMeta.bpm;
        beat = 60f / (bpm >= 20f ? bpm : 120f);
        anchor = float.PositiveInfinity;
        var notes = new List<Vector2>();
        for (int i = 0; i < chart.Count; i++)
        {
            var c = chart[i];
            anchor = Mathf.Min(anchor, c.time);
            if (c.type == CueType.Dodge || c.type == CueType.Curb || c.type == CueType.Duck || c.type == CueType.Jump) continue;
            notes.Add(new Vector2(c.time, c.time + c.duration));
        }
        if (float.IsPositiveInfinity(anchor)) anchor = 0f;
        notes.Sort((a, b) => a.x.CompareTo(b.x));

        float quiet = quietBars * 4f * beat;
        for (int i = 0; i < notes.Count; i++)
        {
            if (loud.Count > 0 && notes[i].x - loud[loud.Count - 1].y < quiet)
                loud[loud.Count - 1] = new Vector2(loud[loud.Count - 1].x, Mathf.Max(loud[loud.Count - 1].y, notes[i].y));
            else
            {
                loud.Add(notes[i]);
                drops.Add(notes[i].x);
            }
        }
        Debug.Log("[ClubBeat] bpm " + bpm.ToString("0.###") + ", grid from " + anchor.ToString("F3") + ", " + loud.Count + " loud stretches, drops at "
                + string.Join(", ", drops.ConvertAll(d => d.ToString("F1"))), this);
    }

    /// The room's energy at a song time: low between stretches, 1 inside, a boost after each drop.
    /// Edges are eased over a beat so nothing snaps.
    private float EnergyAt(float t)
    {
        float e = lowEnergy;
        foreach (var s in loud)
        {
            float inW = Mathf.Clamp01((t - (s.x - beat)) / beat) * Mathf.Clamp01(((s.y + beat) - t) / beat);
            e = Mathf.Max(e, Mathf.Lerp(lowEnergy, 1f, inW));
        }
        float dropSpan = dropBars * 4f * beat;
        foreach (float d in drops)
        {
            float x = (t - d) / dropSpan;
            if (x >= 0f && x < 1f) e += dropBoost * (1f - x) * (1f - x);
        }
        return e;
    }
}
