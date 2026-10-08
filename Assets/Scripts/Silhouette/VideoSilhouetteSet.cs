using System.Collections.Generic;
using UnityEngine;

/// What the video trainer is made of: the filmed clips (black figure, neon contour, alpha), each
/// cut into moves - a step, a punch, a squat - with the moment of its strike, and which move
/// answers which note. A clip is baked to the frames its moves use (a flipbook PNG imported as a
/// Texture2DArray, Tunnel/Video Silhouette/Bake Frames). Everything here can be tuned by hand in
/// the Inspector; VideoSilhouette only reads it.
[CreateAssetMenu(menuName = "Tunnel/Video Silhouette Set", fileName = "VideoSilhouetteSet")]
public class VideoSilhouetteSet : ScriptableObject
{
    public enum Side { None, Left, Right }   // on screen; the figure faces the viewer like a mirror

    /// One move of a clip, in the clip's own seconds.
    [System.Serializable]
    public class Move
    {
        public string group;            // what notes ask for: "kick", "punch", "dodge"...
        public Side side;               // the side it moves to as filmed; the other side is mirrored
        public float start;             // the wind-up starts
        public float peak;              // the strike / landing / bottom: on the note
        public float end;               // settled again
        // A repeat around the peak (end > start: there is one): a run of these notes, or of the
        // moves sharing it, plays on through it instead of going back to the stance; a hold loops it.
        public float loopStart, loopEnd;
        public bool holdLoops;          // a hold loops the repeat (circles, pulses); else freezes on the peak

        public bool HasLoop => loopEnd > loopStart + 0.05f;
        public float LoopLength => loopEnd - loopStart;
    }

    [System.Serializable]
    public class Clip
    {
        public string name;              // the .webm's name in Assets/Silhouette/Clips
        public Texture2DArray frames;    // the baked stretch (bakeStart on), a flipbook imported as an array
        public int frameCount;           // its frames; the array may end in blank cells
        public float fps = 24f;
        public float bakeStart;          // clip seconds of the first frame
        public List<Move> moves = new List<Move>();

        public int FrameCount => frames == null ? 0 : frameCount > 0 ? Mathf.Min(frameCount, frames.depth) : frames.depth;

        /// The array slice (fractional) showing clip time `u`.
        public float FrameAt(float u) => Mathf.Clamp((u - bakeStart) * fps, 0f, Mathf.Max(0, FrameCount - 1));

        /// The clip seconds the moves use: what the bake keeps.
        public Vector2 UsedRange()
        {
            float a = float.PositiveInfinity, b = float.NegativeInfinity;
            foreach (var m in moves)
            {
                a = Mathf.Min(a, m.start);
                b = Mathf.Max(b, m.end);
                if (m.HasLoop) { a = Mathf.Min(a, m.loopStart); b = Mathf.Max(b, m.loopEnd); }
            }
            return float.IsInfinity(a) ? Vector2.zero : new Vector2(a, b);
        }
    }

    /// The notes the planner tells apart: every CueType under its own name (same order, so a
    /// CueType casts straight to it), then what a chord or a run of notes adds up to.
    public enum Note
    {
        Kick, Punch, Jump, PunchHold, KickHold, Dodge, Curb, Duck, PunchSweep, PunchHigh, CurbGap, CurbSplit, ArcHold, Rails, SquatHold,
        ChordPunch,   // two fists together
        Jack,         // a kick pair two lanes apart
        SideJump,     // a kick pair side by side
        HighLow,      // a Punch and a PunchHigh together
        Star,         // a PunchHold and a KickHold on the two outer lanes ("rays")
        March,        // single kicks less than marchBeats apart
        DoubleHigh,   // two high fists together
        Pause,        // no notes for longer than pauseBeats: the groove
    }

    [System.Serializable]
    public struct Binding
    {
        public Note note;
        public string group;
    }

    public List<Clip> clips = new List<Clip>();
    public List<Binding> bindings = new List<Binding>();

    [Header("Timing (in beats of the track)")]
    // Single kicks closer than this are a march.
    [Min(0f)] public float marchBeats = 0.75f;
    // A stretch with no notes longer than this is danced through (the Pause group); a shorter one
    // is the stance, bouncing lightly on the beat.
    [Min(0f)] public float pauseBeats = 2f;
    // The groove's loop takes one of these many beats, the one nearest its own tempo.
    public int[] grooveBeats = { 2, 4, 8 };
    // How far from its filmed speed a move may be played.
    [Min(0.1f)] public float speedMin = 0.75f, speedMax = 1.4f;
    // Two moves are blended over this long where one hands over to the next.
    [Range(0.15f, 0.3f)] public float crossfadeSeconds = 0.2f;
    // Notes closer together than this are one chord.
    [Min(0f)] public float chordSeconds = 0.04f;
    // The stance's bounce on the beat, as a share of the panel's height.
    [Range(0f, 0.05f)] public float bounce = 0.012f;

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

    public string GroupFor(Note note)
    {
        foreach (var b in bindings) if (b.note == note) return b.group;
        return null;
    }

    /// The move for a group on a side: one filmed to that side, else a sideless one, else one
    /// filmed to the other side, mirrored.
    public bool TryFind(string group, Side side, out Clip clip, out Move move, out bool mirror)
    {
        clip = null; move = null; mirror = false;
        if (string.IsNullOrEmpty(group)) return false;
        for (int pass = 0; pass < 3; pass++)
            foreach (var c in clips)
            {
                if (c == null || c.frames == null) continue;
                foreach (var m in c.moves)
                {
                    if (m.group != group) continue;
                    bool ok = pass == 0 ? m.side == side
                            : pass == 1 ? m.side == Side.None || side == Side.None
                            : true;
                    if (!ok) continue;
                    clip = c; move = m;
                    mirror = side != Side.None && m.side != Side.None && m.side != side;
                    return true;
                }
            }
        return false;
    }

    /// The default note -> move table, after the brief: each note its own move.
    public static List<Binding> DefaultBindings() => new List<Binding>
    {
        B(Note.Kick, "march"),           // the march step of the pad's side (the user's call: "марширует")
        B(Note.March, "march"),          // quick kicks L-R-L-R
        B(Note.KickHold, "marchHold"),   // the knee held up
        B(Note.Jack, "jack"),            // a kick pair wide apart
        B(Note.CurbSplit, "jack"),
        B(Note.Curb, "jack"),
        B(Note.SideJump, "jump"),        // a kick pair side by side
        B(Note.Jump, "jump"),
        B(Note.Rails, "rails"),          // narrow steps
        B(Note.Punch, "punch"),          // straight punch, the note's hand
        B(Note.PunchHold, "punch"),      // held out
        B(Note.ChordPunch, "punch2"),    // both, one after the other in a beat
        B(Note.PunchSweep, "punch"),
        B(Note.PunchHigh, "punchHigh"),
        B(Note.HighLow, "punchHigh2"),
        B(Note.DoubleHigh, "punchHigh2"),
        B(Note.Star, "punchHigh2"),      // rays
        B(Note.ArcHold, "circles"),
        B(Note.SquatHold, "sumoHold"),   // down, pulsing
        B(Note.Duck, "sumo"),            // one squat, the bottom on the note
        B(Note.Dodge, "dodge"),          // walls only
        B(Note.CurbGap, "step"),
        B(Note.Pause, "groove"),
    };

    private static Binding B(Note n, string g) => new Binding { note = n, group = g };
}
