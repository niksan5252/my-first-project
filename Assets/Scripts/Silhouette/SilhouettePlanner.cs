using System.Collections.Generic;
using UnityEngine;

/// Turns a chart into the silhouette's whole performance, once, before the track starts: a list of
/// clip segments laid out on the song clock. SilhouetteTrainer then only has to look up where in
/// that list the clock is - the pose is a pure function of song time, which is what makes it the
/// same in Play and in a recording, and at any frame rate.
///
/// The rules, all in this file:
///   * A note's clip is started early enough that its impact frame lands ON the note. When the
///     notes leave less room than the clip's wind-up needs (a note every beat is 0.47 s at 128 BPM,
///     and a front kick winds up for a whole second) the clip is sped up, to maxSpeedUp, and past
///     that the start of the wind-up is cut. Clips always crossfade into each other.
///   * Obstacles move WITH the camera: the step, the top of the jump and the bottom of the crouch
///     land on the moment the camera is fully committed (ObstacleCameraDirector's anchor), and the
///     figure comes back over the same beat the camera does.
///   * Holds loop their clip for the whole length of the note.
///   * Between notes: the fighting idle. Intro, finale and breakdowns: a dance, one per stretch,
///     never the same one twice running. In the intro and the finale the gap-filling walls do not
///     interrupt the dance; mid-track, and for kerbs and barriers everywhere, the move is made and
///     the dance picks up where it was.
///   * Variations never repeat back to back, prefer the other hand/leg to the last one, and are
///     drawn from a generator seeded by the track, so a track always gets the same performance.
public static class SilhouettePlanner
{
    public struct Segment
    {
        public SilhouetteMotion motion;
        public float start;      // song time the segment begins to fade in
        public float fade;       // seconds of crossfade from whatever played before
        public float clipIn;     // clip time at `start`
        public float speed;
        public bool loop;
        public float impactAt;   // song time the impact frame is reached; NaN for loops
        public int note;         // index into the chart for a note's segment, -1 otherwise
        public string label;

        /// Clip time at a song time, with loops wrapped, one-shots held on their last frame and
        /// reverse-after-impact takes folded back on themselves.
        public float ClipTime(float songTime)
        {
            float len = Mathf.Max(0.0001f, motion.Length);
            float t = clipIn + (songTime - start) * speed;
            if (loop) return Mathf.Repeat(t, len);
            if (motion.reverseAfterImpact && t > motion.impact) t = Mathf.Max(0f, 2f * motion.impact - t);
            return Mathf.Clamp(t, 0f, len);
        }
    }

    public struct Stretch
    {
        public float start, end;
        public SilhouetteMotion dance;
        public bool quiet;       // intro/finale: walls do not interrupt
    }

    /// What the planner needs to know about the obstacle camera: see CueSpawner.ObstacleTiming.
    public delegate void ObstacleTimingFn(BeatmapCue cue, out float commit, out float release, out float settled, out int cameraSide);

    private struct Action
    {
        public int note;
        public BeatmapCue cue;
        public float impact;     // song time the move must peak
        public float release;    // until when it holds
        public float settled;    // obstacles: the camera is home
        public int side;         // obstacles: -1 left, +1 right
    }

    // Where the plan begins: before the music (SongClock reports negative time until the audio
    // starts). Finite on purpose - a loop's clip time is solved from it.
    private const float SongStart = -30f;

    private static bool IsObstacle(CueType t) => t == CueType.Dodge || t == CueType.Curb || t == CueType.Duck;

    public static List<Segment> Plan(SilhouetteMotionSet set, BeatmapSequencer chart, float bpm, float songEnd,
                                     string seedText, ObstacleTimingFn obstacleTiming, out List<Stretch> stretches)
    {
        var segments = new List<Segment>();
        stretches = new List<Stretch>();
        float beat = 60f / (bpm >= 20f ? bpm : 120f);
        float bar = beat * 4f;
        var rng = new System.Random(StableHash(seedText));
        var picker = new Picker(rng);

        // --- where the notes are, and the stretches between them ---
        var actions = new List<Action>();
        float firstNote = float.PositiveInfinity, lastNoteEnd = float.NegativeInfinity;
        var noteSpans = new List<Vector2>();
        for (int i = 0; i < chart.Count; i++)
        {
            var c = chart[i];
            var a = new Action { note = i, cue = c, impact = c.time, release = c.time + c.duration, settled = c.time + c.duration };
            if (IsObstacle(c.type))
            {
                obstacleTiming(c, out a.impact, out a.release, out a.settled, out a.side);
            }
            else
            {
                firstNote = Mathf.Min(firstNote, c.time);
                lastNoteEnd = Mathf.Max(lastNoteEnd, c.time + c.duration);
                noteSpans.Add(new Vector2(c.time, c.time + c.duration));
            }
            actions.Add(a);
        }
        actions.Sort((x, y) => x.impact.CompareTo(y.impact));

        // Dance stretches: intro, breakdowns (a gap between notes of breakdownBars or more), finale.
        // A breakdown's dance starts a beat after the last note and gives way to the stance a bar
        // before the next, so a set always opens from the fighting stance.
        if (float.IsPositiveInfinity(firstNote)) { firstNote = songEnd; lastNoteEnd = songEnd; }
        stretches.Add(new Stretch { start = SongStart, end = firstNote - bar, quiet = true });
        for (int i = 1; i < noteSpans.Count; i++)
        {
            float prevEnd = 0f;
            for (int k = 0; k < i; k++) prevEnd = Mathf.Max(prevEnd, noteSpans[k].y);
            float gap = noteSpans[i].x - prevEnd;
            if (gap >= set.breakdownBars * bar)
                stretches.Add(new Stretch { start = prevEnd + beat, end = noteSpans[i].x - bar, quiet = false });
        }
        stretches.Add(new Stretch { start = lastNoteEnd + beat, end = float.PositiveInfinity, quiet = true });
        for (int i = 0; i < stretches.Count; i++)
        {
            var s = stretches[i];
            s.dance = picker.Next(set.dance, "dance");
            stretches[i] = s;
        }

        // Walls inside the intro/finale belong to the dance.
        var dance = stretches;
        actions.RemoveAll(a => a.cue.type == CueType.Dodge && QuietAt(dance, a.impact));

        // --- lay the segments out ---
        var idle = picker.Next(set.idle, "idle");
        float xfade = set.crossfadeBeats * beat;
        float freeFrom = SongStart;                // the previous move is done with the body from here
        float returnFade = xfade;                  // how the background fades back in after it
        float busyUntil = float.NegativeInfinity;  // no wind-up may start before this

        for (int ai = 0; ai < actions.Count; ai++)
        {
            var a = actions[ai];
            var type = a.cue.type;
            bool hold = type == CueType.PunchHold || type == CueType.KickHold;

            SilhouetteMotion motion =
                type == CueType.Punch ? picker.Next(set.punch, "punch", true)
              : type == CueType.Kick ? picker.Next(set.kick, "kick", true)
              : type == CueType.PunchHold ? picker.Next(set.punchHold, "punchHold")
              : type == CueType.KickHold ? picker.Next(set.kickHold, "kickHold")
              : type == CueType.Dodge ? picker.Next(a.side < 0 ? set.dodgeLeft : set.dodgeRight, a.side < 0 ? "dodgeL" : "dodgeR")
              : type == CueType.Duck ? (set.duckDown.Valid ? set.duckDown : null)
              : picker.Next(set.curb, "curb");
            if (motion == null || !motion.Valid) continue;

            // Room for the wind-up: after a one-shot, from its impact (the follow-through is
            // what the next wind-up crossfades out of); after a hold or an obstacle, from the
            // moment it lets go.
            float room = float.IsNegativeInfinity(busyUntil) ? 10f : Mathf.Max(0f, a.impact - busyUntil);

            Segment seg;
            if (hold)
            {
                float fade = Mathf.Min(xfade, Mathf.Max(0.05f, room * 0.8f));
                seg = new Segment { motion = motion, start = a.impact - fade, fade = fade, clipIn = 0f, speed = 1f, loop = true, impactAt = float.NaN };
            }
            else seg = WindUp(motion, a.impact, room, set.maxSpeedUp, xfade);
            seg.note = IsObstacle(type) ? -1 : a.note;
            seg.label = type + " " + motion.clip.name;

            // Background from where the last move let go until this one begins.
            AddBackground(segments, stretches, idle, freeFrom, seg.start, returnFade, xfade);
            segments.Add(seg);
            busyUntil = a.impact;

            if (hold)
            {
                freeFrom = a.release;
                busyUntil = a.release;
                returnFade = xfade;
            }
            else if (type == CueType.Duck)
            {
                // Down to the bottom with the camera, stay there while the barrier passes, and
                // come up as the camera does.
                if (set.duckUp.Valid)
                {
                    float rise = Mathf.Max(0.1f, a.settled - a.release);
                    float speed = Mathf.Clamp(set.duckUp.impact / rise, 0.5f, set.maxSpeedUp);
                    var up = new Segment { motion = set.duckUp, start = a.release, fade = Mathf.Min(0.12f, rise * 0.3f), clipIn = 0f, speed = speed, loop = false, note = -1, label = "Duck " + set.duckUp.clip.name };
                    up.impactAt = up.start + set.duckUp.impact / speed;
                    segments.Add(up);
                    freeFrom = up.start + set.duckUp.Length / speed - xfade;
                    busyUntil = up.impactAt;
                }
                else freeFrom = busyUntil = a.release;
                returnFade = xfade;
            }
            else if (IsObstacle(type))
            {
                // Held at the far side of the step / the landing until the camera starts back,
                // then eased home over exactly the camera's own recovery.
                float clipDone = seg.start + (motion.Length - seg.clipIn) / seg.speed;
                freeFrom = Mathf.Max(a.release, type == CueType.Curb ? clipDone - xfade : a.release);
                if (type == CueType.Dodge) busyUntil = a.release;
                returnFade = type == CueType.Curb ? xfade : Mathf.Max(xfade, a.settled - a.release);
            }
            else
            {
                // A one-shot keeps playing its follow-through until the next wind-up takes over,
                // or until the clip is over, whichever is first.
                float clipDone = seg.impactAt + (motion.reverseAfterImpact ? motion.impact : motion.Length - motion.impact) / seg.speed;
                freeFrom = clipDone - xfade;
                returnFade = xfade;
            }
        }
        AddBackground(segments, stretches, idle, freeFrom, float.PositiveInfinity, returnFade, xfade);
        if (segments.Count == 0 && idle != null)
            segments.Add(new Segment { motion = idle, start = SongStart, fade = 0f, speed = 1f, loop = true, impactAt = float.NaN, note = -1, label = "Idle" });
        return segments;
    }

    /// The wind-up rule. `room` is how much time the previous move leaves before the impact.
    private static Segment WindUp(SilhouetteMotion m, float impactTime, float room, float maxSpeed, float xfade)
    {
        float w = m.impact;                                  // wind-up at normal speed
        float real = Mathf.Min(w, Mathf.Max(0.12f, room * 0.85f));
        float speed = 1f, clipIn = 0f;
        if (real < w)
        {
            speed = Mathf.Min(maxSpeed, w / real);
            clipIn = w - real * speed;                       // what speeding up could not absorb is cut
        }
        float fade = Mathf.Min(xfade, real * 0.6f);
        return new Segment
        {
            motion = m, start = impactTime - real, fade = fade, clipIn = clipIn, speed = speed,
            loop = false, impactAt = impactTime,
        };
    }

    private static bool QuietAt(List<Stretch> stretches, float t)
    {
        foreach (var s in stretches) if (s.quiet && t >= s.start && t < s.end) return true;
        return false;
    }

    private static int StretchAt(List<Stretch> stretches, float t)
    {
        for (int i = 0; i < stretches.Count; i++) if (t >= stretches[i].start && t < stretches[i].end) return i;
        return -1;
    }

    /// Fills [from, to) with the background: the stretch's dance where there is one, the idle
    /// elsewhere, switching at each stretch boundary. A dance resumes at the point it would have
    /// reached had it never been interrupted, so it keeps its own flow through a jump or a step.
    private static void AddBackground(List<Segment> segs, List<Stretch> stretches, SilhouetteMotion idle,
                                      float from, float to, float firstFade, float xfade)
    {
        float t = from;
        float fade = firstFade;
        bool first = true;
        int guard = 0;
        while (t < to && guard++ < 64)
        {
            int si = StretchAt(stretches, t);
            float next = to;
            SilhouetteMotion m;
            float clipIn = 0f;
            if (si >= 0 && stretches[si].dance != null && stretches[si].dance.Valid)
            {
                m = stretches[si].dance;
                next = Mathf.Min(to, stretches[si].end);
                // The intro's dance is anchored on the music's first sample, the others on their own start.
                float anchor = stretches[si].start <= SongStart ? 0f : stretches[si].start;
                clipIn = Mathf.Repeat(t - anchor, m.Length);
            }
            else
            {
                m = idle;
                // Up to the next stretch that starts after t.
                foreach (var s in stretches) if (s.start > t && s.start < next) next = s.start;
            }
            if (m != null && m.Valid && next - t > 0.05f)
            {
                segs.Add(new Segment
                {
                    motion = m, start = t, fade = t <= SongStart ? 0f : (first ? fade : xfade * 2f),
                    clipIn = clipIn, speed = 1f, loop = true, impactAt = float.NaN, note = -1,
                    label = (m == idle ? "Idle " : "Dance ") + m.clip.name,
                });
            }
            first = false;
            if (next <= t) break;
            t = next;
        }
    }

    /// Picks variations: never the same clip twice running within a kind, the other side to the
    /// last one when `alternateSides` and there is a choice, otherwise at random from the rest.
    private struct Picker
    {
        private readonly System.Random rng;
        private readonly Dictionary<string, SilhouetteMotion> last;

        public Picker(System.Random rng)
        {
            this.rng = rng;
            last = new Dictionary<string, SilhouetteMotion>();
        }

        public SilhouetteMotion Next(List<SilhouetteMotion> list, string kind, bool alternateSides = false)
        {
            if (list == null) return null;
            var pool = new List<SilhouetteMotion>();
            foreach (var m in list) if (m != null && m.Valid) pool.Add(m);
            if (pool.Count == 0) return null;
            last.TryGetValue(kind, out var prev);

            var fresh = pool.FindAll(m => m != prev);
            if (fresh.Count == 0) fresh = pool;
            if (alternateSides && prev != null && !string.IsNullOrEmpty(prev.side))
            {
                string prevLimb = Limb(prev.side);
                var other = fresh.FindAll(m => Limb(m.side) != prevLimb);
                if (other.Count > 0) fresh = other;
            }
            var pick = fresh[rng.Next(fresh.Count)];
            last[kind] = pick;
            return pick;
        }

        private static string Limb(string side) => string.IsNullOrEmpty(side) ? "" : side.Substring(0, 1);
    }

    /// FNV-1a: string.GetHashCode is not guaranteed to be the same across runs or runtimes.
    public static int StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s ?? "") { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }
}
