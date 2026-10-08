using System.Collections.Generic;
using UnityEngine;

/// Turns a chart into the trainer's whole performance, once, before the track starts. The result
/// is two lists on the song clock, and SilhouetteTrainerV5 only looks up where the clock is in them,
/// so the pose is a pure function of song time - the same in Play and in a recording.
///
///   * Segments - clips: the relaxed stance under everything, and the two moves a take shows
///     better than a pose: the jump over a kerb (its top on the camera's hop) and the crouch
///     under a barrier (its bottom on the camera's duck). Clips crossfade into each other.
///   * Gestures - posed procedurally by SilhouettePoseJobV5: a hand reaching to a punch cube, a foot
///     set down on a kick tile, both held for a hold note, and the step-and-lean away from a wall.
///     Each has a weight envelope that reaches 1 exactly ON its note (for a wall: exactly when the
///     camera is fully committed, and it comes back over the camera's own recovery).
///
/// Sides are the SCREEN's, because the trainer faces the player like a mirror: a cube in the left
/// lanes is answered with the limb on the screen's left, a dodge to the right moves the figure to
/// the screen's right, with the camera.
public static class SilhouettePlannerV5
{
    public struct SegmentV5
    {
        public SilhouetteMotionV5 motion;
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

    public enum GestureKindV5 { Punch, PunchHold, Kick, KickHold, Dodge, OneLeg }

    public struct GestureV5
    {
        public GestureKindV5 kind;
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

    public static void Plan(SilhouetteMotionSetV5 set, BeatmapSequencer chart, float bpm, int laneCount, string seedText,
                            ObstacleTimingFn obstacleTiming, out List<SegmentV5> segments, out List<GestureV5> gestures)
    {
        segments = new List<SegmentV5>();
        gestures = new List<GestureV5>();
        float beat = 60f / (bpm >= 20f ? bpm : 120f);
        var rng = new System.Random(StableHash(seedText));
        SilhouetteMotionV5 lastCurb = null;

        var stance = new StanceTrackV5(set, chart, beat, rng);
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
                case CueType.Kick when (side > 0 ? set.stepScreenRight : set.stepScreenLeft).Valid:
                {
                    // A real step from the take: the foot of the pad's side comes down on the note.
                    var motion = side > 0 ? set.stepScreenRight : set.stepScreenLeft;
                    float room = float.IsNegativeInfinity(busyUntil) ? 10f : Mathf.Max(0f, c.time - busyUntil);
                    // At its own speed: the last stepWindUpMax seconds of the take before the landing
                    // (less if the note before is closer), never sped up.
                    float real = Mathf.Clamp(Mathf.Min(set.stepWindUpMax, room * 0.85f), 0.12f, motion.impact);
                    var seg = new SegmentV5
                    {
                        motion = motion, start = c.time - real, fade = Mathf.Min(set.stepBlend, real * 0.8f),
                        clipIn = motion.impact - real, speed = 1f, loop = false, impactAt = c.time,
                    };
                    seg.note = i;
                    seg.label = "Step " + motion.clip.name;
                    stance.Fill(segments, freeFrom, seg.start, returnFade);
                    segments.Add(seg);
                    busyUntil = c.time;
                    returnFade = set.stepBlend;      // a slow settle back into the stance, not a snap
                    freeFrom = c.time + set.stepStay;
                    break;
                }
                case CueType.Punch:
                case CueType.Kick:
                case CueType.PunchHold:
                case CueType.KickHold:
                {
                    bool kick = c.type == CueType.Kick || c.type == CueType.KickHold;
                    bool hold = c.type == CueType.PunchHold || c.type == CueType.KickHold;
                    // A tap is quicker than a reach: the foot has to be back under the body
                    // before the next note, which is usually the other foot one beat later.
                    float holdEnd = hold ? c.time + c.duration : kick ? c.time + set.tapStayBeats * beat : c.time;
                    float inT = kick ? set.tapInBeats * beat : rise;
                    float outT = kick ? set.tapOutBeats * beat : fall;
                    gestures.Add(new GestureV5
                    {
                        kind = c.type == CueType.Punch ? GestureKindV5.Punch : c.type == CueType.Kick ? GestureKindV5.Kick
                             : c.type == CueType.PunchHold ? GestureKindV5.PunchHold : GestureKindV5.KickHold,
                        side = side, outer = outer,
                        start = c.time - inT, impact = c.time, holdEnd = holdEnd, end = holdEnd + outT, note = i,
                    });
                    break;
                }
                case CueType.Dodge:
                {
                    obstacleTiming(c, out float begin, out float commit, out float release, out float settled, out int camSide);
                    gestures.Add(new GestureV5
                    {
                        kind = GestureKindV5.Dodge, side = camSide, outer = true, glide = true,
                        start = begin, impact = commit, holdEnd = release, end = settled, note = i,
                    });
                    break;
                }
                case CueType.CurbGap:
                {
                    // A kerb with one lane free: stand on the leg of the free lane, the other knee
                    // up and held while the kerb passes, then down (video 12: the user's call).
                    obstacleTiming(c, out float begin, out float commit, out float release, out float settled, out _);
                    gestures.Add(new GestureV5
                    {
                        kind = GestureKindV5.OneLeg, side = -side, outer = false,
                        start = Mathf.Min(begin, commit - 0.6f * beat), impact = commit, holdEnd = release, end = settled + 0.3f * beat, note = i,
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
                    stance.Fill(segments, freeFrom, seg.start, returnFade);
                    segments.Add(seg);
                    busyUntil = commit;
                    returnFade = xfade;
                    if (duck && set.duckUp.Valid)
                    {
                        // Down with the camera, stay while the barrier passes, up as the camera rises.
                        float up = Mathf.Max(0.1f, settled - release);
                        float speed = Mathf.Clamp(set.duckUp.impact / up, 0.5f, set.maxSpeedUp);
                        var s = new SegmentV5 { motion = set.duckUp, start = release, fade = Mathf.Min(0.12f, up * 0.3f), speed = speed, note = i, label = "Duck " + set.duckUp.clip.name };
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
        stance.Fill(segments, freeFrom, float.PositiveInfinity, returnFade);
    }

    /// The first beat of the grid every note sits on, for the stance's bounce.
    public static float BeatAnchor(BeatmapSequencer chart)
    {
        float a = float.PositiveInfinity;
        for (int i = 0; i < chart.Count; i++) a = Mathf.Min(a, chart[i].time);
        return float.IsPositiveInfinity(a) ? 0f : a;
    }

    /// The wind-up rule: sped up to fit the room before the impact, and past maxSpeed cut.
    private static SegmentV5 WindUp(SilhouetteMotionV5 m, float impactTime, float room, float maxSpeed, float xfade)
    {
        float w = m.impact;
        float real = Mathf.Min(w, Mathf.Max(0.12f, room * 0.85f));
        float speed = 1f, clipIn = 0f;
        if (real < w)
        {
            speed = Mathf.Min(maxSpeed, w / real);
            clipIn = w - real * speed;
        }
        return new SegmentV5
        {
            motion = m, start = impactTime - real, fade = Mathf.Min(xfade, real * 0.6f), clipIn = clipIn, speed = speed,
            loop = false, impactAt = impactTime,
        };
    }

    /// The stance under the gestures: mocap idles laid end to end with crossfades. Lively ones
    /// where there are no notes for a while (intro, breakdowns, finale), calm ones in between,
    /// each held for at least stanceMinSeconds and one full cycle, never the same twice running.
    /// The choice at any moment is fixed once for the whole track, so a jump or a crouch that
    /// interrupts the stance comes back to the same idle, at the point it would have reached.
    private sealed class StanceTrackV5
    {
        private struct PieceV5 { public float start, end; public SilhouetteMotionV5 motion; }
        private readonly List<PieceV5> pieces = new List<PieceV5>();
        private readonly float fade;

        public StanceTrackV5(SilhouetteMotionSetV5 set, BeatmapSequencer chart, float beat, System.Random rng)
        {
            fade = set.stanceFadeBeats * beat;
            float bar = beat * 4f;

            // Where the notes are (obstacles do not count: walls fill the quiet stretches).
            var spans = new List<Vector2>();
            for (int i = 0; i < chart.Count; i++)
            {
                var c = chart[i];
                if (c.type == CueType.Dodge || c.type == CueType.Curb || c.type == CueType.Duck || c.type == CueType.Jump) continue;
                spans.Add(new Vector2(c.time, c.time + c.duration));
            }
            spans.Sort((a, b) => a.x.CompareTo(b.x));
            var lively = new List<Vector2>();
            if (spans.Count == 0) lively.Add(new Vector2(SongStart, float.PositiveInfinity));
            else
            {
                lively.Add(new Vector2(SongStart, spans[0].x - bar));
                float end = spans[0].y;
                for (int i = 1; i < spans.Count; i++)
                {
                    if (spans[i].x - end >= set.livelyGapBars * bar) lively.Add(new Vector2(end + beat, spans[i].x - bar));
                    end = Mathf.Max(end, spans[i].y);
                }
                lively.Add(new Vector2(end + beat, float.PositiveInfinity));
            }

            var calm = Valid(set.idle);
            var bright = Valid(set.idleLively);
            if (bright.Count == 0) bright = calm;
            if (calm.Count == 0) calm = bright;
            if (calm.Count == 0) return;

            // Walk the track: alternate calm and lively regions, pieces inside each.
            SilhouetteMotionV5 last = null;
            float t = SongStart;
            const float Horizon = 3600f;
            int li = 0, guard = 0;
            while (t < Horizon && guard++ < 10000)
            {
                while (li < lively.Count && lively[li].y <= t) li++;
                bool inLively = li < lively.Count && t >= lively[li].x;
                float regionEnd = inLively ? lively[li].y : (li < lively.Count ? lively[li].x : float.PositiveInfinity);
                var pool = inLively ? bright : calm;
                var m = Pick(pool, ref last, rng);
                float len = Mathf.Max(set.stanceMinSeconds, m.Length);
                float pieceEnd = Mathf.Min(t + len, regionEnd);
                // A leftover shorter than a crossfade is merged rather than played as a flicker.
                if (regionEnd - pieceEnd < fade * 2f) pieceEnd = regionEnd;
                if (pieceEnd - t < fade) pieceEnd = Mathf.Min(regionEnd, t + fade);
                pieces.Add(new PieceV5 { start = t, end = pieceEnd, motion = m });
                t = pieceEnd;
            }
        }

        /// Adds the stance for [from, to): the pieces it overlaps, the first fading in over
        /// `firstFade`, the rest over the stance crossfade.
        public void Fill(List<SegmentV5> segs, float from, float to, float firstFade)
        {
            if (to - from <= 0.05f) return;
            bool first = true;
            foreach (var p in pieces)
            {
                if (p.end <= from || p.start >= to) continue;
                float s = Mathf.Max(from, p.start);
                // The tail of a piece that a jump or crouch cut into, too short to fade in and
                // out again: skip it and let the next piece take over.
                if (p.end - s < fade && p.end < to) continue;
                segs.Add(new SegmentV5
                {
                    motion = p.motion, start = s,
                    fade = s <= SongStart ? 0f : (first ? firstFade : fade),
                    // Anchored on the piece's own start, so an interrupted idle resumes in phase.
                    clipIn = Mathf.Repeat(s - p.start, p.motion.Length),
                    speed = 1f, loop = true, impactAt = float.NaN, note = -1, label = "Stance " + p.motion.clip.name,
                });
                first = false;
            }
        }

        private static List<SilhouetteMotionV5> Valid(List<SilhouetteMotionV5> list)
        {
            var r = new List<SilhouetteMotionV5>();
            if (list != null) foreach (var m in list) if (m != null && m.Valid) r.Add(m);
            return r;
        }
    }

    /// Never the same take twice running, otherwise at random - seeded by the track.
    private static SilhouetteMotionV5 Pick(List<SilhouetteMotionV5> list, ref SilhouetteMotionV5 last, System.Random rng)
    {
        var pool = new List<SilhouetteMotionV5>();
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
