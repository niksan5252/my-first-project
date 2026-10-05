using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

/// What the video trainer is made of: the filmed clips (black figure, neon contour, alpha), each
/// with the moment of its strike, and which clip answers which note. Everything here is meant to
/// be tuned by hand in the Inspector; VideoSilhouette only reads it.
[CreateAssetMenu(menuName = "Tunnel/Video Silhouette Set", fileName = "VideoSilhouetteSet")]
public class VideoSilhouetteSet : ScriptableObject
{
    [System.Serializable]
    public class Clip
    {
        public string name;
        public VideoClip clip;
        // Seconds into the clip: the strike (the fist out, the squat's bottom, the feet landing).
        // The phrase loop puts it on the beat.
        public float hitTime;
        // The fragment that is played around it - for a clip of several repeats, one repeat.
        public float start;
        public float end;
        // Not read by the phrase planner (every clip loops at a constant phrase speed); kept for
        // the setup tool.
        public bool fitSpeed = true;

        public float Length => Mathf.Max(0.04f, end - start);
    }

    /// The notes a clip can answer: every CueType under its own name (same order, so a CueType
    /// casts straight to it), then the combinations read off a chord of notes struck together.
    public enum Note
    {
        Kick, Punch, Jump, PunchHold, KickHold, Dodge, Curb, Duck, PunchSweep, PunchHigh, CurbGap, CurbSplit, ArcHold, Rails, SquatHold,
        ChordPunch,   // two fists together
        Jack,         // a kick pair symmetric about the middle (0+3, 1+2)
        SideJump,     // any other kick pair (0+1, 2+3, a Shift walking across)
        HighLow,      // a Punch and a PunchHigh together
        Star,         // a PunchHold and a KickHold on the two outer lanes (the Cross set)
    }

    [System.Serializable]
    public struct Binding
    {
        public Note note;
        public string clip;
    }

    public List<Clip> clips = new List<Clip>();
    public List<Binding> bindings = new List<Binding>();
    // Any note with no binding.
    public string fallbackClip = "01_step_touch";
    // Bars with no notes: this clip looped in time (a pause is never a statue).
    public string restClip = "01_step_touch";
    // Before the chart is laid out: this clip, paused on `neutralTime`.
    public string neutralClip = "01_step_touch";
    public float neutralTime = 0f;

    [Header("Phrases (in bars and beats of the track)")]
    // The shortest stretch one clip is kept for; a shorter run of notes joins its neighbour.
    [Min(1)] public int minPhraseBars = 2;
    // The beats one loop of a clip may take; the planner picks the one nearest the clip's own tempo.
    public int[] cycleBeats = { 1, 2 };
    // Two clips are alpha-blended over this long at a phrase change, from half a beat before it.
    [Range(0.4f, 0.6f)] public float crossfadeSeconds = 0.5f;
    // Notes closer together than this are one chord.
    [Min(0f)] public float chordSeconds = 0.04f;

    [Header("Framing")]
    // The video is landscape, the panel portrait: the figure is cut out of the middle. 1 = the
    // video's full height fills the panel; larger zooms in.
    [Min(0.5f)] public float zoom = 1f;
    [Range(0f, 1f)] public float centerX = 0.5f;
    [Range(0f, 1f)] public float centerY = 0.5f;

    public Clip Find(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return null;
        foreach (var c in clips) if (c != null && c.clip != null && c.name == clipName) return c;
        return null;
    }

    public Clip For(Note note)
    {
        foreach (var b in bindings) if (b.note == note) { var c = Find(b.clip); if (c != null) return c; }
        return Find(fallbackClip);
    }

    /// The default note -> clip table: the exercise a phrase of these notes is shown as. The ones
    /// with no clip of their own are paired with the nearest one (commented).
    public static List<Binding> DefaultBindings() => new List<Binding>
    {
        B(Note.Kick, "01_step_touch"),          // steps one after another
        B(Note.KickHold, "01_step_touch"),
        B(Note.SideJump, "07_sumo_b"),          // two pads side by side: wide stance with a bounce (no soft-jump clip)
        B(Note.Curb, "07_sumo_b"),              // a hop over the curb: the same bounce (no soft-jump clip)
        B(Note.Jump, "07_sumo_b"),              // retired, read as Curb
        B(Note.CurbSplit, "07_sumo_b"),         // a wide stance
        B(Note.Punch, "02_jab_cross"),          // punches L/R
        B(Note.PunchHigh, "04_punch_up"),
        B(Note.HighLow, "04_punch_up"),
        B(Note.ChordPunch, "03_double_punch"),  // the double cube: both hands
        B(Note.PunchHold, "13_reach_up"),
        B(Note.Jack, "05_jumping_jack"),        // two lanes apart
        B(Note.Star, "12_star"),
        B(Note.Rails, "fm_arms_wide"),          // stance, arms out
        B(Note.SquatHold, "06_squat_b"),        // squat with a pulse
        B(Note.Duck, "06_squat_b"),
        B(Note.Dodge, "11_side_bend"),          // walls: lean aside
        B(Note.CurbGap, "08_side_lunge_b"),     // step over onto the free lane
        B(Note.PunchSweep, "fm_arms_wide"),     // the fist carried out to the side
        B(Note.ArcHold, "01_step_touch"),       // no arm-circle clip: step touch
    };

    private static Binding B(Note n, string c) => new Binding { note = n, clip = c };
}
