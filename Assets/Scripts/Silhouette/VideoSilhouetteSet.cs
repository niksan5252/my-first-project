using System.Collections.Generic;
using UnityEngine;

/// What the video trainer is made of: the filmed clips (black figure, neon contour, alpha), each
/// baked to one loop of its movement (a flipbook PNG imported as a Texture2DArray, Tunnel/Video
/// Silhouette/Bake Frames), and
/// which clip answers which note. A new clip is a .webm in Assets/Silhouette/Video plus a row in
/// `bindings`; the bake adds its row in `clips` and finds its loop and strike. Everything here can
/// be tuned by hand in the Inspector; VideoSilhouette only reads it.
[CreateAssetMenu(menuName = "Tunnel/Video Silhouette Set", fileName = "VideoSilhouetteSet")]
public class VideoSilhouetteSet : ScriptableObject
{
    [System.Serializable]
    public class Clip
    {
        public string name;              // the .webm's name in Assets/Silhouette/Video
        public Texture2DArray frames;    // one loop, baked (a flipbook imported as an array)
        public int frameCount;           // the loop's frames; the array may end in blank cells
        public float fps = 24f;
        // The strike in the loop (the fist out, the squat's bottom, the feet landing): on the beat.
        public int peakFrame;
        // Strikes in one loop (a march's left AND right: 2) - the loop then takes beats in pairs.
        [Min(1)] public int strikes = 1;
        // The loop is the move and its mirror image: one side, then the other (a one-sided take).
        public bool mirror;
        // Off: the bake finds the loop and strike and writes them here. On: it keeps these.
        public bool manualCycle;
        public float cycleStart, cycleEnd, peakTime;   // source seconds
        [Min(0.05f)] public float speedMin = 0.8f, speedMax = 1.25f;

        public int FrameCount => frames == null ? 0 : frameCount > 0 ? Mathf.Min(frameCount, frames.depth) : frames.depth;
        public float Length => Mathf.Max(0.04f, FrameCount / Mathf.Max(1f, fps));
        public float PeakTime => peakFrame / Mathf.Max(1f, fps);
    }

    /// The notes a clip can answer: every CueType under its own name (same order, so a CueType
    /// casts straight to it), then the combinations read off a chord of notes struck together.
    public enum Note
    {
        Kick, Punch, Jump, PunchHold, KickHold, Dodge, Curb, Duck, PunchSweep, PunchHigh, CurbGap, CurbSplit, ArcHold, Rails, SquatHold,
        ChordPunch,   // two fists together
        Jack,         // a kick pair two lanes apart
        SideJump,     // a kick pair side by side
        HighLow,      // a Punch and a PunchHigh together
        Star,         // a PunchHold and a KickHold on the two outer lanes (the Cross set)
        March,        // single kicks a beat or less apart: quick steps
    }

    [System.Serializable]
    public struct Binding
    {
        public Note note;
        public string clip;
    }

    public List<Clip> clips = new List<Clip>();
    public List<Binding> bindings = new List<Binding>();
    // Files in the Video folder the bake leaves out (weak takes).
    public List<string> excluded = new List<string>();
    // Any note with no binding.
    public string fallbackClip = "28_step_touch";
    // Bars with no notes, and before the chart is laid out: standing, a light sway.
    public string restClip = "30_idle";

    [Header("Phrases (in bars and beats of the track)")]
    // The shortest stretch one clip is kept for; a shorter run of notes joins its neighbour.
    [Min(1)] public int minPhraseBars = 2;
    // The beats one strike of a clip may take; the planner picks the one nearest the clip's own
    // tempo (within its speedMin..speedMax if it can).
    public int[] cycleBeats = { 1, 2, 4, 8 };
    // Two clips are blended over this long at a phrase change, from half a beat before it.
    [Range(0.4f, 0.6f)] public float crossfadeSeconds = 0.5f;
    // Notes closer together than this are one chord.
    [Min(0f)] public float chordSeconds = 0.04f;
    // Single kicks this many beats apart or closer are a march rather than step touch.
    [Min(0f)] public float marchBeats = 0.75f;

    // Tunnel/SilhouetteFrames, referenced here so a build keeps it.
    public Shader frameShader;

    [Header("Bake (the panel's crop of the video)")]
    // 1 = the video's full height fills the panel; larger zooms in.
    [Min(1f)] public float zoom = 1f;
    [Range(0f, 1f)] public float centerX = 0.5f;
    [Range(0f, 1f)] public float centerY = 0.5f;
    [Min(64)] public int frameHeight = 512;
    // The panel's width / height (SilhouetteTrainer's panelSize).
    [Min(0.1f)] public float panelAspect = 0.625f;

    public Clip Find(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return null;
        foreach (var c in clips) if (c != null && c.frames != null && c.name == clipName) return c;
        return null;
    }

    public Clip For(Note note)
    {
        foreach (var b in bindings) if (b.note == note) { var c = Find(b.clip); if (c != null) return c; }
        return Find(fallbackClip);
    }

    /// The default note -> clip table, after Логика_силуэта_конкурента.md: the exercise a phrase
    /// of these notes is shown as.
    public static List<Binding> DefaultBindings() => new List<Binding>
    {
        B(Note.Kick, "28_step_touch"),          // pads one by one L/R: step touch
        B(Note.KickHold, "28_step_touch"),
        B(Note.March, "22_march"),              // quick steps
        B(Note.SideJump, "26_bounce"),          // two pads side by side: a small bounce, arms swinging
        B(Note.Jack, "05_jumping_jack"),        // two pads wide apart: jumping jacks
        B(Note.CurbSplit, "05_jumping_jack"),   // two long strips apart: jacks
        B(Note.Jump, "21_jump"),                // a plate across the road: a jump on the spot
        B(Note.Curb, "21_jump"),
        B(Note.Rails, "fm_arms_wide"),          // rails both sides: stand, arms out
        B(Note.SquatHold, "25_sumo_pulse"),     // lasers + beams: wide sumo squat, pulsing
        B(Note.Duck, "06_squat_b"),             // one beam: down and up
        B(Note.Dodge, "24_side_stretch"),       // glass walls: step aside, arm reaching out
        B(Note.CurbGap, "08_side_lunge_b"),     // step over onto the free lane
        B(Note.PunchHigh, "23_punch_overhead"), // high cubes L/R: punches up in turn
        B(Note.HighLow, "23_punch_overhead"),
        B(Note.Punch, "23_punch_overhead"),     // no strong forward-punch take
        B(Note.ChordPunch, "13_reach_up"),      // the double cube: both hands up
        B(Note.PunchHold, "13_reach_up"),
        B(Note.Star, "12_star"),                // rays: a wide star
        B(Note.PunchSweep, "fm_arms_wide"),     // the fist carried out to the side
        B(Note.ArcHold, "20_arm_circles"),
    };

    public static List<string> DefaultExcluded() => new List<string>
    {
        "01_step_touch", "02_jab_cross", "03_double_punch", "09_march_b", "fm_punch", "fm_march", "fm_misc_77",
    };

    private static Binding B(Note n, string c) => new Binding { note = n, clip = c };
}
