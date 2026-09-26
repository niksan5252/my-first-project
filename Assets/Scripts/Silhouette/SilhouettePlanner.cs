using System.Collections.Generic;
using UnityEngine;

/// Turns a chart into the trainer's whole performance, once, before the track starts. The result
/// is two lists on the song clock, and SilhouetteTrainer only looks up where the clock is in them,
/// so the pose is a pure function of song time - the same in Play and in a recording.
///
///   * Segments - clips: the relaxed stance under everything, and the two moves a take shows
///     better than a pose: the jump over a kerb (its top on the camera's hop) and the crouch
///     under a barrier (its bottom on the camera's duck). Clips crossfade into each other.
///   * Gestures - posed procedurally by SilhouettePoseJob: a hand reaching to a punch cube, a foot
///     set down on a kick tile, both held for a hold note, and the step-and-lean away from a wall.
///     Each has a weight envelope that reaches 1 exactly ON its note (for a wall: exactly when the
///     camera is fully committed, and it comes back over the camera's own recovery).
///
/// Sides are the SCREEN's, because the trainer faces the player like a mirror: a cube in the left
/// lanes is answered with the limb on the screen's left, a dodge to the right moves the figure to
/// the screen's right, with the camera.
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
        public int note;         // chart index for an obstacle's segment, -1 for the stance
        public string label;

        /// Clip time at a song time, with loops wrapped, one-shots held on their last frame and
        /// reverse-after-impact takes folded back on themselves.
        public float ClipTime(float songTime)
        {
            float len = Mathf.Max(0.0001f, motion.Length);
            if (motion.holdFrame >= 0f) return Mathf.Min(motion.holdFrame, len);
            float t = clipIn + (songTime - start) * speed;
            if (loop) return Mathf.Repeat(t, len);
            if (motion.reverseAfterImpact && t > motion.impact) t = Mathf.Max(0f, 2f * motion.impact - t);
            return Mathf.Clamp(t, 0f, len);
        }
    }

    public enum GestureKind { Punch, PunchHold, Kick, KickHold, Dodge }

    public struct Gesture
    {
        public GestureKind kind;
        public int side;         // -1 screen left, +1 screen right
        public bool outer;       // an outer lane: the wider reach / the wide stance
        public float start;      // weight starts rising
        public float impact;     // weight is 1: the touch, the reach, the camera fully committed
        public float holdEnd;    // weight leaves 1
        public float end;        // weight is back to 0
        public bool glide;       // ease both ways (a dodge, to match the camera's Glide)
        public int note;

        public float Weight(float t)
        {
            if (t <= start || t >= end) return 0f;
            if (t < impact)
            {
                float x = (t - start) / Mathf.Max(1e-4f, impact - start);
                // Punches and steps decelerate INTO the touch - the hand arrives, it is not thrown.
                return glide ? Smooth(x) : 1f - (1f - x) * (1f - x);
            }
            if (t <= holdEnd) return 1f;
            return 1f - Smooth((t - holdEnd) / Mathf.Max(1e-4f, end - holdEnd));
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }

    /// See CueSpawner.ObstacleTiming.
    public delegate void ObstacleTimingFn(BeatmapCue cue, out float begin, out float commit, out float release, out float settled, out int cameraSide);

    // Where the plan begins: before the music (SongClock reports negative time until the audio
    // starts). Finite on purpose - a loop's clip time is solved from it.
    private const float SongStart = -30f;

    public static void Plan(SilhouetteMotionSet set, BeatmapSequencer chart, float bpm, int laneCount, string seedText,
                            ObstacleTimingFn obstacleTiming, out List<Segment> segments, out List<Gesture> gestures)
    {
        segments = new List<Segment>();
        gestures = new List<Gesture>();
        float beat = 60f / (bpm >= 20f ? bpm : 120f);
        var rng = new System.Random(StableHash(seedText));
        SilhouetteMotion lastCurb = null;

        var idle = FirstValid(set.idle);
        float xfade = set.crossfadeBeats * beat;
        float rise = Mathf.Min(set.riseSeconds, set.riseBeats * beat);
        float fall = set.fallBeats * beat;
        float freeFrom = SongStart;     // the last clip move has let go of the body from here
        float returnFade = xfade;
        float busyUntil = float.NegativeInfinity;

        var order = new List<int>();
        for (int i = 0; i < chart.Count; i++) order.Add(i);
        order.Sort((a, b) => chart[a].time.CompareTo(chart[b].time));

        foreach (int i in order)
        {
            var c = chart[i];
            int side = c.lane < laneCount * 0.5f ? -1 : 1;
            bool outer = c.lane == 0 || c.lane == laneCount - 1;
            switch (c.type)
            {
                case CueType.Punch:
                case CueType.Kick:
                case CueType.PunchHold:
                case CueType.KickHold:
                {
                    bool kick = c.type == CueType.Kick || c.type == CueType.KickHold;
                    bool hold = c.type == CueType.PunchHold || c.type == CueType.KickHold;
                    float holdEnd = hold ? c.time + c.duration : kick ? c.time + set.plantBeats * beat : c.time;
                    gestures.Add(new Gesture
                    {
                        kind = c.type == CueType.Punch ? GestureKind.Punch : c.type == CueType.Kick ? GestureKind.Kick
                             : c.type == CueType.PunchHold ? GestureKind.PunchHold : GestureKind.KickHold,
                        side = side, outer = outer,
                        start = c.time - rise, impact = c.time, holdEnd = holdEnd, end = holdEnd + fall, note = i,
                    });
                    break;
                }
                case CueType.Dodge:
                {
                    obstacleTiming(c, out float begin, out float commit, out float release, out float settled, out int camSide);
                    gestures.Add(new Gesture
                    {
                        kind = GestureKind.Dodge, side = camSide, outer = true, glide = true,
                        start = begin, impact = commit, holdEnd = release, end = settled, note = i,
                    });
                    break;
                }
                default:   // Curb (and the retired Jump, read as Curb) and Duck: clips
                {
                    obstacleTiming(c, out _, out float commit, out float release, out float settled, out _);
                    bool duck = c.type == CueType.Duck;
                    var motion = duck ? (set.duckDown.Valid ? set.duckDown : null) : Pick(set.curb, ref lastCurb, rng);
                    if (motion == null) break;
                    float room = float.IsNegativeInfinity(busyUntil) ? 10f : Mathf.Max(0f, commit - busyUntil);
                    var seg = WindUp(motion, commit, room, set.maxSpeedUp, xfade);
                    seg.note = i;
                    seg.label = c.type + " " + motion.clip.name;
                    AddIdle(segments, idle, freeFrom, seg.start, returnFade);
                    segments.Add(seg);
                    busyUntil = commit;
                    returnFade = xfade;
                    if (duck && set.duckUp.Valid)
                    {
                        // Down with the camera, stay while the barrier passes, up as the camera rises.
                        float up = Mathf.Max(0.1f, settled - release);
                        float speed = Mathf.Clamp(set.duckUp.impact / up, 0.5f, set.maxSpeedUp);
                        var s = new Segment { motion = set.duckUp, start = release, fade = Mathf.Min(0.12f, up * 0.3f), speed = speed, note = i, label = "Duck " + set.duckUp.clip.name };
                        s.impactAt = s.start + set.duckUp.impact / speed;
                        segments.Add(s);
                        freeFrom = s.start + set.duckUp.Length / speed - xfade;
                        busyUntil = s.impactAt;
                    }
                    else
                    {
                        float clipDone = seg.start + (motion.Length - seg.clipIn) / seg.speed;
                        freeFrom = Mathf.Max(release, clipDone - xfade);
                    }
                    break;
                }
            }
        }
        AddIdle(segments, idle, freeFrom, float.PositiveInfinity, returnFade);
    }

    /// The first beat of the grid every note sits on, for the stance's bounce.
    public static float BeatAnchor(BeatmapSequencer chart)
    {
        float a = float.PositiveInfinity;
        for (int i = 0; i < chart.Count; i++) a = Mathf.Min(a, chart[i].time);
        return float.IsPositiveInfinity(a) ? 0f : a;
    }

    /// The wind-up rule: sped up to fit the room before the impact, and past maxSpeed cut.
    private static Segment WindUp(SilhouetteMotion m, float impactTime, float room, float maxSpeed, float xfade)
    {
        float w = m.impact;
        float real = Mathf.Min(w, Mathf.Max(0.12f, room * 0.85f));
        float speed = 1f, clipIn = 0f;
        if (real < w)
        {
            speed = Mathf.Min(maxSpeed, w / real);
            clipIn = w - real * speed;
        }
        return new Segment
        {
            motion = m, start = impactTime - real, fade = Mathf.Min(xfade, real * 0.6f), clipIn = clipIn, speed = speed,
            loop = false, impactAt = impactTime,
        };
    }

    private static void AddIdle(List<Segment> segs, SilhouetteMotion idle, float from, float to, float fade)
    {
        if (idle == null || to - from <= 0.05f) return;
        segs.Add(new Segment
        {
            motion = idle, start = from, fade = from <= SongStart ? 0f : fade, clipIn = Mathf.Repeat(from, idle.Length),
            speed = 1f, loop = true, impactAt = float.NaN, note = -1, label = "Stance " + idle.clip.name,
        });
    }

    private static SilhouetteMotion FirstValid(List<SilhouetteMotion> list)
    {
        if (list != null) foreach (var m in list) if (m != null && m.Valid) return m;
        return null;
    }

    /// Never the same take twice running, otherwise at random - seeded by the track.
    private static SilhouetteMotion Pick(List<SilhouetteMotion> list, ref SilhouetteMotion last, System.Random rng)
    {
        var pool = new List<SilhouetteMotion>();
        if (list != null) foreach (var m in list) if (m != null && m.Valid && m != last) pool.Add(m);
        if (pool.Count == 0 && last != null && last.Valid) pool.Add(last);
        if (pool.Count == 0) return null;
        last = pool[rng.Next(pool.Count)];
        return last;
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
