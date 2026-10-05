using System.Collections.Generic;
using UnityEngine;

/// Lays a chart out as the video trainer's shows, once: which clip, from when to when on the song
/// clock, at which speed, so that the clip's strike frame (VideoSilhouetteSet.Clip.hitTime) lands
/// exactly on its note. Between shows the figure stands in the neutral pose. VideoSilhouette only
/// looks up where the clock is in this list.
public static class VideoSilhouettePlanner
{
    public struct Show
    {
        public VideoSilhouetteSet.Clip clip;
        public float start, end;     // song time
        public float hit;            // song time of the strike; for a hold, the time it is held at
        public float speed;
        public bool loop;            // the fragment repeats to fill the show
        public bool hold;            // paused on `hit` clip-wise: the neutral stance
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
            kickLanes.Sort();
            bool symmetric = kickLanes[0] + kickLanes[kickLanes.Count - 1] == laneCount - 1;
            return symmetric ? VideoSilhouetteSet.Note.Jack : VideoSilhouetteSet.Note.SideJump;
        }
        // One note (or a punch over a kick: the hand is what reads).
        foreach (var t in new[] { CueType.PunchHigh, CueType.ArcHold, CueType.PunchSweep, CueType.PunchHold, CueType.Punch, CueType.KickHold, CueType.Kick })
            foreach (var c in chord) if (c.type == t) return (VideoSilhouetteSet.Note)t;
        return (VideoSilhouetteSet.Note)chord[0].type;
    }

    public static List<Show> Plan(VideoSilhouetteSet set, BeatmapSequencer chart, System.Func<float, float> beatAt, int laneCount)
    {
        var shows = new List<Show>();
        var neutral = set.Find(set.neutralClip);

        var order = new List<BeatmapCue>();
        for (int i = 0; i < chart.Count; i++) order.Add(chart[i]);
        order.Sort((a, b) => a.time.CompareTo(b.time));
        var chords = new List<List<BeatmapCue>>();
        foreach (var c in order)
        {
            if (chords.Count > 0 && c.time - chords[chords.Count - 1][0].time <= set.chordSeconds) chords[chords.Count - 1].Add(c);
            else chords.Add(new List<BeatmapCue> { c });
        }

        float prevEnd = float.NegativeInfinity;
        for (int k = 0; k < chords.Count; k++)
        {
            var chord = chords[k];
            float t = chord[0].time;
            var clip = set.For(Classify(chord, laneCount));
            if (clip == null) continue;

            float spb = beatAt(t);
            float maxHalf = set.maxShowBeats * spb * 0.5f;
            float hold = 0f;
            foreach (var c in chord) hold = Mathf.Max(hold, c.duration);
            float next = k + 1 < chords.Count ? chords[k + 1][0].time : float.PositiveInfinity;
            float prev = k > 0 ? chords[k - 1][0].time : float.NegativeInfinity;
            // The room the show has: up to halfway to each neighbour (the next one's run-up), and
            // to the end of a hold.
            float lo = Mathf.Min(t, Mathf.Max(t - maxHalf, (prev + t) * 0.5f, prevEnd));
            float hi = Mathf.Min(t + maxHalf, (t + next) * 0.5f);
            if (hold > 0f) hi = Mathf.Max(hi, Mathf.Min(t + hold, next));
            float before = Mathf.Max(0.01f, t - lo), after = Mathf.Max(0.01f, hi - t);

            float pre = Mathf.Max(0f, clip.hitTime - clip.start), post = Mathf.Max(0f, clip.end - clip.hitTime);
            float speed = 1f;
            bool loop = false;
            if (clip.fitSpeed)
            {
                // The slowest speed at which the fragment fits on both sides of the strike.
                speed = Mathf.Max(pre / before, post / after, 0.01f);
                if (speed < set.minSpeed)
                {
                    // Too much room for one pass: repeat the fragment, as many times as brings the
                    // speed back nearest 1.
                    int n = Mathf.Max(1, Mathf.RoundToInt((after - post) / clip.Length) + 1);
                    speed = Mathf.Clamp((post + (n - 1) * clip.Length) / after, set.minSpeed, set.maxSpeed);
                    loop = n > 1;
                }
                speed = Mathf.Min(speed, set.maxSpeed);
            }
            float start = Mathf.Max(lo, t - pre / speed);
            float end = loop ? hi : Mathf.Min(hi, t + post / speed);

            if (neutral != null && start - prevEnd > 1e-3f)
                shows.Add(Neutral(set, neutral, float.IsNegativeInfinity(prevEnd) ? SilhouettePlanner.SongStart : prevEnd, start));
            shows.Add(new Show { clip = clip, start = start, end = end, hit = t, speed = speed, loop = loop, label = clip.name });
            prevEnd = end;
        }
        if (neutral != null)
            shows.Add(Neutral(set, neutral, float.IsNegativeInfinity(prevEnd) ? SilhouettePlanner.SongStart : prevEnd, float.PositiveInfinity));
        return shows;
    }

    public static Show Neutral(VideoSilhouetteSet set, VideoSilhouetteSet.Clip clip, float start, float end) =>
        new Show { clip = clip, start = start, end = end, hit = start, speed = 0f, hold = true, holdAt = set.neutralTime, label = "neutral" };
}
