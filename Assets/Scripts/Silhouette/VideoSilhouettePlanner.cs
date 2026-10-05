using System.Collections.Generic;
using UnityEngine;

/// Lays a chart out as the video trainer's shows, once. The chart is cut into phrases of whole bars
/// and each phrase is ONE clip looped at a constant speed - one cycle on 1 or 2 beats, the strike
/// frame (VideoSilhouetteSet.Clip.hitTime) on the beat - the way a class instructor keeps one
/// exercise going for a phrase instead of acting out each note. The clip changes only on a phrase
/// boundary, cross-faded from half a beat before it. Bars with no notes are step touch in time.
/// VideoSilhouette only looks up where the clock is in this list.
public static class VideoSilhouettePlanner
{
    public struct Show
    {
        public VideoSilhouetteSet.Clip clip;
        public float start, end;     // song time; end overlaps the next show by the cross-fade
        public float hit;            // song time of a strike (a downbeat); the loop is phased on it
        public float speed;
        public bool loop;            // the fragment repeats to fill the show
        public bool hold;            // paused on `holdAt` clip-wise: the neutral stance
        public float holdAt;         // clip time shown while holding
        public string label;

        /// Clip time at a song time.
        public float ClipTime(float songTime)
        {
            if (hold) return holdAt;
            float u = clip.hitTime - clip.start + (songTime - hit) * speed;
            return clip.start + (loop ? Mathf.Repeat(u, clip.Length) : Mathf.Clamp(u, 0f, clip.Length));
        }
    }

    /// One stretch of the song on one beat grid (a playlist track, or the whole chart).
    public struct Grid
    {
        public float from, to;       // song time
        public float anchor;         // a downbeat
        public float secondsPerBeat;
    }

    private const int BeatsPerBar = 4;

    /// The policy: what a chord of notes struck together asks the figure to do. Replace this to
    /// change which notes count as which move; the clip for each is VideoSilhouetteSet.bindings.
    public static VideoSilhouetteSet.Note Classify(List<BeatmapCue> chord, int laneCount)
    {
        // An obstacle owns the whole body, whatever is struck with it.
        foreach (var c in chord)
            switch (c.type)
            {
                case CueType.Dodge: case CueType.Curb: case CueType.Jump: case CueType.Duck:
                case CueType.CurbGap: case CueType.CurbSplit: case CueType.Rails: case CueType.SquatHold:
                    return (VideoSilhouetteSet.Note)c.type;
            }

        int punches = 0, highs = 0, pholds = 0, kicks = 0, kholds = 0;
        var kickLanes = new List<int>();
        bool outerPHold = false, outerKHold = false;
        foreach (var c in chord)
        {
            bool outer = c.lane == 0 || c.lane == laneCount - 1;
            switch (c.type)
            {
                case CueType.Punch: punches++; break;
                case CueType.PunchHigh: highs++; break;
                case CueType.PunchHold: pholds++; outerPHold |= outer; break;
                case CueType.Kick: kicks++; kickLanes.Add(c.lane); break;
                case CueType.KickHold: kholds++; kickLanes.Add(c.lane); outerKHold |= outer; break;
            }
        }
        if (pholds > 0 && kholds > 0 && outerPHold && outerKHold) return VideoSilhouetteSet.Note.Star;
        if (punches > 0 && highs > 0) return VideoSilhouetteSet.Note.HighLow;
        if (punches + highs + pholds >= 2) return highs >= 2 ? VideoSilhouetteSet.Note.PunchHigh : VideoSilhouetteSet.Note.ChordPunch;
        if (kickLanes.Count >= 2)
        {
            // Two pads apart (a lane between them): legs out - a jack. Side by side: both feet at
            // once in place - a bounce.
            kickLanes.Sort();
            bool apart = kickLanes[kickLanes.Count - 1] - kickLanes[0] >= 2;
            return apart ? VideoSilhouetteSet.Note.Jack : VideoSilhouetteSet.Note.SideJump;
        }
        // One note (or a punch over a kick: the hand is what reads).
        foreach (var t in new[] { CueType.PunchHigh, CueType.ArcHold, CueType.PunchSweep, CueType.PunchHold, CueType.Punch, CueType.KickHold, CueType.Kick })
            foreach (var c in chord) if (c.type == t) return (VideoSilhouetteSet.Note)t;
        return (VideoSilhouetteSet.Note)chord[0].type;
    }

    /// The policy: how much a chord counts towards its bar's exercise. An obstacle is a must for
    /// the body, so it outweighs the strikes around it.
    private static float Weight(VideoSilhouetteSet.Note note)
    {
        switch (note)
        {
            case VideoSilhouetteSet.Note.Dodge: case VideoSilhouetteSet.Note.Curb: case VideoSilhouetteSet.Note.Jump:
            case VideoSilhouetteSet.Note.Duck: case VideoSilhouetteSet.Note.CurbGap: case VideoSilhouetteSet.Note.CurbSplit:
            case VideoSilhouetteSet.Note.Rails: case VideoSilhouetteSet.Note.SquatHold:
                return 2f;
            default: return 1f;
        }
    }

    private class Bar
    {
        public float start, downbeat, spb;   // start: the downbeat, or where its grid begins
        public int grid;
        public readonly Dictionary<VideoSilhouetteSet.Clip, float> votes = new Dictionary<VideoSilhouetteSet.Clip, float>();
        public VideoSilhouetteSet.Clip clip;   // the winner; null: no notes
    }

    private class Run
    {
        public VideoSilhouetteSet.Clip clip;
        public int first, count;               // bars
    }

    public static List<Show> Plan(VideoSilhouetteSet set, BeatmapSequencer chart, List<Grid> grids, int laneCount)
    {
        var shows = new List<Show>();
        var rest = set.Find(set.restClip) ?? set.Find(set.fallbackClip);
        if (rest == null || grids.Count == 0) return shows;

        var order = new List<BeatmapCue>();
        for (int i = 0; i < chart.Count; i++) order.Add(chart[i]);
        if (order.Count == 0) return shows;
        order.Sort((a, b) => a.time.CompareTo(b.time));

        // The bars, grid by grid, from the first note's bar to the last note's (holds included).
        float firstNote = order[0].time, lastNote = float.NegativeInfinity;
        foreach (var c in order) lastNote = Mathf.Max(lastNote, c.time + c.duration);
        var bars = new List<Bar>();
        for (int g = 0; g < grids.Count; g++)
        {
            var gr = grids[g];
            float barLen = BeatsPerBar * gr.secondsPerBeat;
            float from = Mathf.Max(gr.from, firstNote - barLen), to = Mathf.Min(gr.to, lastNote + barLen);
            if (to <= from) continue;
            float b0 = gr.anchor + Mathf.Floor((from - gr.anchor) / barLen) * barLen;
            for (float b = b0; b < to; b += barLen)
                bars.Add(new Bar { start = Mathf.Max(b, gr.from), downbeat = b, spb = gr.secondsPerBeat, grid = g });
        }
        if (bars.Count == 0) return shows;

        // Each chord votes for its clip in its bar, and a hold in every bar it lasts into.
        var chord = new List<BeatmapCue>();
        int bar = 0;
        for (int i = 0; i < order.Count; i++)
        {
            chord.Clear();
            float t = order[i].time;
            while (i < order.Count && order[i].time - t <= set.chordSeconds) chord.Add(order[i++]);
            i--;
            var note = Classify(chord, laneCount);
            var clip = set.For(note);
            if (clip == null) continue;
            float hold = 0f;
            foreach (var c in chord) hold = Mathf.Max(hold, c.duration);
            while (bar + 1 < bars.Count && bars[bar + 1].start <= t) bar++;
            for (int b = bar; b < bars.Count && (b == bar || bars[b].start < t + hold); b++)
            {
                bars[b].votes.TryGetValue(clip, out float v);
                bars[b].votes[clip] = v + Weight(note);
            }
        }
        foreach (var b in bars)
        {
            float best = 0f;
            foreach (var kv in b.votes) if (kv.Value > best) { best = kv.Value; b.clip = kv.Key; }
            if (b.clip == null) b.clip = rest;    // a pause: step touch in time, not a statue
        }

        // Runs of one clip, never across a change of grid (the loop is phased on one tempo).
        var runs = new List<Run>();
        for (int b = 0; b < bars.Count; b++)
        {
            var last = runs.Count > 0 ? runs[runs.Count - 1] : null;
            if (last != null && last.clip == bars[b].clip && bars[last.first].grid == bars[b].grid) last.count++;
            else runs.Add(new Run { clip = bars[b].clip, first = b, count = 1 });
        }
        // A run shorter than a phrase goes to its neighbour on the same grid (the longer one), so
        // the clip never changes more often than every minPhraseBars.
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int r = 0; r < runs.Count; r++)
            {
                if (runs[r].count >= set.minPhraseBars) continue;
                int g = bars[runs[r].first].grid;
                var prev = r > 0 && bars[runs[r - 1].first].grid == g ? runs[r - 1] : null;
                var next = r + 1 < runs.Count && bars[runs[r + 1].first].grid == g ? runs[r + 1] : null;
                if (prev == null && next == null) continue;    // the grid's only run
                if (prev != null && (next == null || prev.count >= next.count))
                {
                    prev.count += runs[r].count;
                    runs.RemoveAt(r);
                    if (r < runs.Count && runs[r].clip == prev.clip && bars[runs[r].first].grid == g) { prev.count += runs[r].count; runs.RemoveAt(r); }
                }
                else
                {
                    next.first = runs[r].first;
                    next.count += runs[r].count;
                    runs.RemoveAt(r);
                    if (r > 0 && runs[r - 1].clip == next.clip && bars[runs[r - 1].first].grid == g) { runs[r - 1].count += next.count; runs.RemoveAt(r); }
                }
                merged = true;
                break;
            }
        }

        // One show per run, from half a beat before its first downbeat.
        float fade = set.crossfadeSeconds;
        for (int r = 0; r < runs.Count; r++)
        {
            var first = bars[runs[r].first];
            float spb = first.spb;
            var clip = runs[r].clip;
            float start = r == 0 ? SilhouettePlanner.SongStart : first.start - 0.5f * spb;
            shows.Add(new Show
            {
                clip = clip, start = start, end = float.PositiveInfinity, hit = first.downbeat,
                speed = clip.Length / (CycleBeats(set, clip, spb) * spb), loop = true,
                label = clip.name,
            });
            if (r > 0) { var p = shows[r - 1]; p.end = start + fade; shows[r - 1] = p; }
        }
        return shows;
    }

    /// The beats one cycle of the clip takes: of the allowed counts, the one that keeps it nearest
    /// its own filmed tempo.
    public static int CycleBeats(VideoSilhouetteSet set, VideoSilhouetteSet.Clip clip, float spb)
    {
        int best = 1;
        float bestOff = float.PositiveInfinity;
        foreach (int n in set.cycleBeats)
        {
            if (n < 1) continue;
            float off = Mathf.Abs(Mathf.Log(clip.Length / (n * spb)));
            if (off < bestOff) { bestOff = off; best = n; }
        }
        return best;
    }

    public static Show Neutral(VideoSilhouetteSet set, VideoSilhouetteSet.Clip clip, float start, float end) =>
        new Show { clip = clip, start = start, end = end, hit = start, speed = 0f, hold = true, holdAt = set.neutralTime, label = "neutral" };
}
