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
        // It is put exactly on the note's time.
        public float hitTime;
        // The fragment that is played around it - for a clip of several repeats, one repeat.
        public float start;
        public float end;
        // true: the playback speed is fitted to the time the note leaves (minSpeed..maxSpeed);
        // false: plays at 1x, cut by the next note.
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
    // Between notes: this clip, paused on `neutralTime`.
    public string neutralClip = "01_step_touch";
    public float neutralTime = 0f;

    [Header("Timing (in beats of the track)")]
    [Range(0.3f, 1f)] public float minSpeed = 0.6f;
    [Range(1f, 3f)] public float maxSpeed = 1.6f;
    // The longest a one-shot note may keep its clip when the next note is far away; after that the
    // figure goes back to the neutral stance.
    [Min(0.5f)] public float maxShowBeats = 4f;
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

    /// The default note -> clip table, by meaning. The notes with no clip of their own are paired
    /// with the nearest one (commented).
    public static List<Binding> DefaultBindings() => new List<Binding>
    {
        B(Note.Punch, "02_jab_cross"),
        B(Note.ChordPunch, "03_double_punch"),
        B(Note.PunchHigh, "04_punch_up"),
        B(Note.Jack, "05_jumping_jack"),
        B(Note.Duck, "06_squat_b"),
        B(Note.SquatHold, "07_sumo_b"),
        B(Note.SideJump, "08_side_lunge_b"),
        B(Note.HighLow, "09_march_b"),
        B(Note.Dodge, "11_side_bend"),
        B(Note.Star, "12_star"),
        B(Note.Kick, "01_step_touch"),
        // nearest by meaning
        B(Note.PunchHold, "13_reach_up"),       // a fist held up
        B(Note.KickHold, "01_step_touch"),
        B(Note.Curb, "05_jumping_jack"),        // one honest jump
        B(Note.Jump, "05_jumping_jack"),        // retired, read as Curb
        B(Note.CurbGap, "08_side_lunge_b"),     // over onto the free lane
        B(Note.CurbSplit, "07_sumo_b"),         // a wide stance held
        B(Note.Rails, "09_march_b"),            // narrow, feet together, in place
        B(Note.PunchSweep, "fm_arms_wide"),     // the fist carried out to the side
        B(Note.ArcHold, "fm_arms_wide"),
    };

    private static Binding B(Note n, string c) => new Binding { note = n, clip = c };
}
