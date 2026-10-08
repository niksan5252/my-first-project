using System.Collections.Generic;
using UnityEngine;

/// Lays a chart out as the video trainer's timeline, once: every note (a chord counts once) is its
/// own move - a step to the pad's side, the punch of that hand, a squat - played so its strike frame
/// (VideoSilhouetteSet.Move.peak) lands exactly on the note, its wind-up started that long before.
/// The same move again soon after plays on through the move's repeat in time (no stance between);
/// otherwise one move hands over to the next with a short blend, the next one's wind-up sped up
/// (to speedMax) or cut where there is no room. Holds freeze on the strike or loop the repeat. A
/// stretch longer than pauseBeats with no notes is danced through (the groove), a shorter one is
/// the stance bouncing on the beat. VideoSilhouette only evaluates the segments at the clock.
public static class VideoSilhouettePlanner
{
    /// Clip time at a song time, linear up to the next key; `wrap` folds the stretch after it
    /// into the segment's loop.
    public struct Key
    {
        public float t, u;
        public bool wrap;
    }

    /// A stretch of the timeline drawn from one clip.
    public class Segment
    {
        public VideoSilhouetteSet.Clip clip;
        public bool mirror;
        public bool bounce;           // the stance: a light bounce on the beat
        public float from;            // song time it takes over; the blend from the one before starts here
        public float fade = 0.2f;     // the blend's length: done by the strike, however close the notes
        public readonly List<Key> keys = new List<Key>();
        public float loopStart, loopEnd;
        public bool groove;           // the loop phase-locked to the beat grid instead of keys
        public float grooveBeats;
        public float spb = 0.5f, anchor;
        public string label;

        public float ClipTime(float t)
        {
            if (groove)
            {
                float phase = Mathf.Repeat((t - anchor) / (grooveBeats * spb), 1f);
                return loopStart + phase * (loopEnd - loopStart);
            }
            if (keys.Count == 0) return 0f;
            if (t <= keys[0].t) return keys[0].u;
            for (int j = 0; j + 1 < keys.Count; j++)
            {
                var a = keys[j];
                var b = keys[j + 1];
                if (t >= b.t) continue;
                float u = Mathf.Lerp(a.u, b.u, (t - a.t) / Mathf.Max(1e-5f, b.t - a.t));
                return a.wrap && loopEnd > loopStart ? loopStart + Mathf.Repeat(u - loopStart, loopEnd - loopStart) : u;
            }
            return keys[keys.Count - 1].u;
        }

        /// The stance's bounce at a song time: 0..1, down on each beat.
        public float Bounce(float t) => bounce ? Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Repeat((t - anchor) / spb, 1f)), 2f) : 0f;

        public void Add(float t, float u, bool wrap = false) => keys.Add(new Key { t = t, u = u, wrap = wrap });

        public float Wrap(float u) => loopEnd > loopStart ? loopStart + Mathf.Repeat(u - loopStart, loopEnd - loopStart) : u;
    }

    /// One stretch of the song on one beat grid (a playlist track, or the whole chart).
    public struct Grid
    {
        public float from, to;       // song time
        public float anchor;         // a downbeat
        public float secondsPerBeat;
    }

    /// A note (or chord) and the move it was given - kept for the report.
    public class Action
    {
        public float t, dur;
        public VideoSilhouetteSet.Note note;
        public VideoSilhouetteSet.Side side;
        public VideoSilhouetteSet.Clip clip;
        public VideoSilhouetteSet.Move move;
        public bool mirror;
        public bool missing;         // no move for its group: the fallback was used
        public bool connected;       // played on from the move before, no blend
        public float take = float.NegativeInfinity;   // when it took over from the move before
        public float speed = 1f;     // its wind-up's speed
    }

    private const string Fallback = "step";
    private const float MinFade = 0.06f;    // notes closer than the blend: it is shortened to this at most

    /// The policy: what a chord of notes struck together asks the figure to do, and to which side
    /// (on screen). Hands outrank feet - a punch reads better than a step - and an obstacle owns
    /// the whole body. Replace this to change which notes count as which move; the move for each is
    /// VideoSilhouetteSet.bindings.
    public static VideoSilhouetteSet.Note Classify(List<BeatmapCue> chord, int laneCount, out VideoSilhouetteSet.Side side)
    {
        side = VideoSilhouetteSet.Side.None;
        foreach (var c in chord)
            switch (c.type)
            {
                case CueType.Dodge:
                    // A wall on a side: the body goes the other way.
                    side = SideOf(c.lane, laneCount) == VideoSilhouetteSet.Side.Left ? VideoSilhouetteSet.Side.Right : VideoSilhouetteSet.Side.Left;
                    return VideoSilhouetteSet.Note.Dodge;
                case CueType.CurbGap:
                    side = SideOf(c.lane, laneCount);
                    return VideoSilhouetteSet.Note.CurbGap;
                case CueType.Curb: case CueType.Jump: case CueType.Duck: case CueType.CurbSplit:
                case CueType.Rails: case CueType.SquatHold:
                    return (VideoSilhouetteSet.Note)c.type;
            }

        int punches = 0, highs = 0, pholds = 0, kicks = 0;
        var kickLanes = new List<int>();
        bool outerPHold = false, outerKHold = false;
        BeatmapCue? hand = null, foot = null, arc = null;
        foreach (var c in chord)
        {
            bool outer = c.lane == 0 || c.lane == laneCount - 1;
            switch (c.type)
            {
                case CueType.Punch: case CueType.PunchSweep: punches++; hand = hand ?? c; break;
                case CueType.PunchHigh: highs++; hand = c; break;
                case CueType.PunchHold: pholds++; outerPHold |= outer; hand = hand ?? c; break;
                case CueType.Kick: kicks++; kickLanes.Add(c.lane); foot = foot ?? c; break;
                case CueType.KickHold: kickLanes.Add(c.lane); outerKHold |= outer; foot = foot ?? c; break;
                case CueType.ArcHold: arc = c; break;
            }
        }
        if (pholds > 0 && outerPHold && outerKHold) return VideoSilhouetteSet.Note.Star;
        if (highs >= 2) return VideoSilhouetteSet.Note.DoubleHigh;
        if (highs > 0 && punches + pholds > 0) return VideoSilhouetteSet.Note.HighLow;
        if (punches + pholds >= 2) return VideoSilhouetteSet.Note.ChordPunch;
        if (hand.HasValue)
        {
            side = SideOf(hand.Value.lane, laneCount);
            return (VideoSilhouetteSet.Note)hand.Value.type;
        }
        if (arc.HasValue) return VideoSilhouetteSet.Note.ArcHold;
        if (kickLanes.Count >= 2)
        {
            kickLanes.Sort();
            return kickLanes[kickLanes.Count - 1] - kickLanes[0] >= 2 ? VideoSilhouetteSet.Note.Jack : VideoSilhouetteSet.Note.SideJump;
        }
        if (foot.HasValue)
        {
            side = SideOf(foot.Value.lane, laneCount);
            return (VideoSilhouetteSet.Note)foot.Value.type;
        }
        return (VideoSilhouetteSet.Note)chord[0].type;
    }

    private static VideoSilhouetteSet.Side SideOf(int lane, int laneCount) =>
        lane < laneCount * 0.5f ? VideoSilhouetteSet.Side.Left : VideoSilhouetteSet.Side.Right;

    public static List<Segment> Plan(VideoSilhouetteSet set, BeatmapSequencer chart, List<Grid> grids, int laneCount) =>
        Plan(set, chart, grids, laneCount, out _);

    public static List<Segment> Plan(VideoSilhouetteSet set, BeatmapSequencer chart, List<Grid> grids, int laneCount, out List<Action> actions)
    {
        var segs = new List<Segment>();
        actions = new List<Action>();
        if (grids.Count == 0 || chart.Count == 0) return segs;

        var order = new List<BeatmapCue>();
        for (int i = 0; i < chart.Count; i++) order.Add(chart[i]);
        order.Sort((a, b) => a.time.CompareTo(b.time));

        // Notes -> actions, a chord counting once.
        var chord = new List<BeatmapCue>();
        for (int i = 0; i < order.Count; i++)
        {
            chord.Clear();
            float t = order[i].time;
            while (i < order.Count && order[i].time - t <= set.chordSeconds) chord.Add(order[i++]);
            i--;
            var note = Classify(chord, laneCount, out var side);
            float dur = 0f;
            foreach (var c in chord) dur = Mathf.Max(dur, c.duration);
            actions.Add(new Action { t = t, dur = dur, note = note, side = side });
        }
        // Single kicks in quick succession: a march, the whole run.
        for (int i = 0; i < actions.Count; i++)
        {
            if (actions[i].note != VideoSilhouetteSet.Note.Kick) continue;
            int j = i;
            while (j + 1 < actions.Count && actions[j + 1].note == VideoSilhouetteSet.Note.Kick
                   && actions[j + 1].t - actions[j].t <= set.marchBeats * BeatAt(grids, actions[j].t).secondsPerBeat + 0.01f) j++;
            if (j > i) for (int k = i; k <= j; k++) actions[k].note = VideoSilhouetteSet.Note.March;
            i = j;
        }
        // Their moves.
        foreach (var a in actions)
        {
            if (!set.TryFind(set.GroupFor(a.note), a.side, out a.clip, out a.move, out a.mirror))
            {
                a.missing = true;
                set.TryFind(Fallback, a.side, out a.clip, out a.move, out a.mirror);
            }
        }
        actions.RemoveAll(a => a.move == null);
        if (actions.Count == 0) return segs;

        float fade = set.crossfadeSeconds;
        Segment cur = null;
        Action prev = null;
        int settleKeys = 0;                             // where the move before began to settle
        float prevDone = SilhouettePlanner.SongStart;   // the move before has settled by then
        foreach (var a in actions)
        {
            var grid = BeatAt(grids, a.t);
            float spb = grid.secondsPerBeat;
            var m = a.move;

            if (prev != null && cur != null && Continues(set, prev, a, spb, out float d))
            {
                // On through the repeat: from the strike before to this one, no stance between.
                float from = prev.t + prev.dur;
                cur.keys.RemoveRange(settleKeys, cur.keys.Count - settleKeys);
                float u0 = cur.ClipTime(from);
                cur.Add(from, u0, true);
                cur.Add(a.t, u0 + d);
                cur.Add(a.t, cur.Wrap(u0 + d));
                a.connected = true;
            }
            else
            {
                float pre = m.peak - m.start;
                float windUp = a.t - pre;                       // at its own speed
                float freeFrom = prev != null ? prev.t + prev.dur : SilhouettePlanner.SongStart;
                float take;                                     // when this move takes over
                if (windUp - prevDone >= fade)
                {
                    // Room before it: the stance, or the groove for a long pause.
                    float pause = a.t - freeFrom;
                    // No groove (the user's call): every pause is the plain standing stance.
                    var fill = Stance(set, prev, a, prevDone, grid);
                    fill.fade = fade;
                    if (prev == null) fill.from = float.NegativeInfinity;
                    segs.Add(fill);
                    take = windUp;
                }
                else
                {
                    // No room: the wind-up sped up, and cut if even that is too slow.
                    take = Mathf.Max(a.t - pre / set.speedMax, freeFrom + 0.05f);
                    take = Mathf.Max(freeFrom, Mathf.Min(take, a.t - fade));
                    if (prev == null) take = float.NegativeInfinity;
                }
                float speed = float.IsInfinity(take) ? 1f : Mathf.Clamp(pre / Mathf.Max(1e-3f, a.t - take), 1f, set.speedMax);
                a.take = take;
                a.speed = speed;
                float blend = float.IsInfinity(take) ? fade : Mathf.Clamp(a.t - take, MinFade, fade);
                cur = new Segment
                {
                    clip = a.clip, mirror = a.mirror, from = take, fade = blend, loopStart = m.loopStart, loopEnd = m.loopEnd,
                    spb = spb, anchor = grid.anchor, label = m.group,
                };
                cur.Add(a.t - pre / speed, m.start);
                cur.Add(a.t, m.peak);
                segs.Add(cur);
            }

            // The hold, then settling.
            float end = a.t;
            if (a.dur > 0.05f)
            {
                end = a.t + a.dur;
                int n = m.HasLoop && m.holdLoops ? Mathf.RoundToInt(a.dur / m.LoopLength) : 0;
                if (n >= 1 && n * m.LoopLength / a.dur <= set.speedMax && n * m.LoopLength / a.dur >= set.speedMin)
                {
                    float u0 = cur.ClipTime(a.t);
                    cur.Add(a.t, u0, true);
                    cur.Add(end, u0 + n * m.LoopLength);
                    cur.Add(end, cur.Wrap(u0 + n * m.LoopLength));
                }
                else cur.Add(end, cur.ClipTime(a.t));
            }
            float settle = cur.ClipTime(end);
            settleKeys = cur.keys.Count;
            cur.Add(end, settle);
            cur.Add(end + Mathf.Max(0f, m.end - m.peak), m.end);
            prevDone = end + Mathf.Max(0f, m.end - m.peak);
            prev = a;
        }
        // After the last note: the stance.
        var last = BeatAt(grids, prev.t);
        segs.Add(Stance(set, prev, null, prevDone, last));
        segs.Sort((x, y) => x.from.CompareTo(y.from));
        return segs;
    }

    /// The same clip and side, both moves on one repeat, close enough to play on through it at a
    /// speed in range: `d`, the clip time from one strike to the next.
    private static bool Continues(VideoSilhouetteSet set, Action prev, Action a, float spb, out float d)
    {
        d = 0f;
        var p = prev.move;
        var m = a.move;
        if (prev.clip != a.clip || prev.mirror != a.mirror || !p.HasLoop || !m.HasLoop) return false;
        if (Mathf.Abs(p.loopStart - m.loopStart) > 0.01f || Mathf.Abs(p.loopEnd - m.loopEnd) > 0.01f) return false;
        if (prev.dur > 0.05f && !p.holdLoops) return false;
        float gap = a.t - (prev.t + prev.dur);
        if (gap <= 0.01f || gap > set.pauseBeats * spb) return false;
        float len = m.LoopLength;
        float d0 = Mathf.Repeat(m.peak - p.peak, len);
        if (d0 < 0.05f * len) d0 += len;
        float best = float.PositiveInfinity;
        for (int k = 0; k < 8; k++)
        {
            float dk = d0 + k * len;
            float s = dk / gap;
            if (s > set.speedMax) break;
            if (s < set.speedMin) continue;
            if (Mathf.Abs(Mathf.Log(s)) < Mathf.Abs(Mathf.Log(best))) { best = s; d = dk; }
        }
        return !float.IsInfinity(best);
    }

    /// Standing between moves, bouncing lightly on the beat: the plain neutral stance - the
    /// standing frame of the march take (its first step's start) - whatever move came before.
    /// Without that take: the pose the move before settled in (or the next one's first).
    private static Segment Stance(VideoSilhouetteSet set, Action prev, Action next, float from, Grid grid)
    {
        var src = prev ?? next;
        var s = new Segment
        {
            clip = src.clip, mirror = src.mirror, from = from, bounce = true,
            spb = grid.secondsPerBeat, anchor = grid.anchor, label = "stance",
        };
        float u = prev != null ? prev.move.end : next.move.start;
        if (set.TryFind("march", VideoSilhouetteSet.Side.Right, out var neutral, out var nm, out _))
        {
            s.clip = neutral;
            s.mirror = false;
            u = nm.start;
        }
        s.Add(from, u);
        return s;
    }

    /// The groove for a long pause: its repeat looped on the beat grid, one loop on the beats
    /// nearest its own tempo.
    private static Segment Groove(VideoSilhouetteSet set, Grid grid, float from)
    {
        if (!set.TryFind(set.GroupFor(VideoSilhouetteSet.Note.Pause), VideoSilhouetteSet.Side.None, out var clip, out var move, out _) || !move.HasLoop)
            return null;
        float best = 4f, bestOff = float.PositiveInfinity;
        foreach (int n in set.grooveBeats)
        {
            if (n < 1) continue;
            float off = Mathf.Abs(Mathf.Log(move.LoopLength / (n * grid.secondsPerBeat)));
            if (off < bestOff) { bestOff = off; best = n; }
        }
        return new Segment
        {
            clip = clip, from = from, groove = true, grooveBeats = best, loopStart = move.loopStart, loopEnd = move.loopEnd,
            spb = grid.secondsPerBeat, anchor = grid.anchor, label = "groove",
        };
    }

    private static Grid BeatAt(List<Grid> grids, float t)
    {
        foreach (var g in grids) if (t >= g.from && t < g.to) return g;
        return grids[grids.Count - 1];
    }
}
