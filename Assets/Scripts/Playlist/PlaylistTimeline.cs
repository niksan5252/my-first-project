using System.Collections.Generic;
using UnityEngine;

/// Where every segment of a playlist sits on the one song clock, and the single chart that clock
/// plays: every segment's notes moved onto the playlist's timeline, with the transitions kept
/// clear. Built once, before the tunnel is; everything else - the audio, the change of look, the
/// tempo the lights pulse at - only looks things up in it.
public sealed class PlaylistTimeline
{
    public sealed class Span
    {
        public int index;
        public PlaylistSegment segment;
        public float audioStart;    // song time the track's clipStart is heard
        public float audioEnd;      // song time the track stops
        // The transition INTO this segment (none for the first): [inStart, inEnd], and its middle,
        // where the look is swapped under the veil and a thunderclap lands.
        public float inStart = float.NegativeInfinity, inEnd = float.NegativeInfinity, inMiddle = float.NegativeInfinity;
        public float bpm, secondsPerBeat, gridAnchor;
        public float Offset => audioStart - segment.clipStart;   // track time -> song time
        public bool HasTransitionIn => !float.IsNegativeInfinity(inStart);
        public float transitionLength;
        public bool IsCut => HasTransitionIn && segment.transitionIn == PlaylistTransition.Cut;
        public bool IsInterlude => HasTransitionIn && segment.transitionIn == PlaylistTransition.Interlude;
    }

    public const float CutSeconds = 0.06f;

    public readonly List<Span> spans = new List<Span>();
    public Beatmap Meta { get; private set; }
    public List<BeatmapCue> Cues { get; private set; }
    // A retention wrapper's outro after the last track: the cards and the end screen.
    public float Outro { get; private set; }
    public float MusicEnd => spans.Count > 0 ? spans[spans.Count - 1].audioEnd : 0f;
    public float Length => MusicEnd + Outro;
    public string Report { get; private set; }

    /// Lays the segments out and merges their charts. `solo` >= 0 plays that segment alone.
    /// Returns null (with the reason in `error`) when a chart cannot be read.
    public static PlaylistTimeline Build(RunPlaylist playlist, int solo, int laneCount, out string error)
    {
        error = null;
        var t = new PlaylistTimeline();
        var charts = new List<(Beatmap meta, List<BeatmapCue> cues)>();
        var used = new List<PlaylistSegment>();
        for (int i = 0; i < playlist.segments.Count; i++)
        {
            if (solo >= 0 && i != solo) continue;
            var seg = playlist.segments[i];
            if (seg == null || seg.track == null) { error = "segment " + i + " has no track"; return null; }
            if (!BeatmapReader.TryLoad(seg.beatmapFileName, laneCount, out var meta, out var cues, out error))
            {
                error = "segment " + i + " (" + seg.label + "): " + error;
                return null;
            }
            used.Add(seg);
            charts.Add((meta, cues));
        }
        if (used.Count == 0) { error = "the playlist has no segments"; return null; }

        LayOut(t, used, charts, playlist);
        t.Merge(playlist, charts);
        if (playlist.retention && solo < 0) t.Outro = playlist.outroSeconds;
        return t;
    }

    /// The whole timing policy of a playlist, in one place. Crossfade: the next track starts at
    /// the transition's start and the previous one ends at its end. Thunder: the previous track
    /// ends, and the next one starts, at the transition's middle, where the clap lands.
    private static void LayOut(PlaylistTimeline t, List<PlaylistSegment> segs, List<(Beatmap meta, List<BeatmapCue> cues)> charts, RunPlaylist playlist)
    {
        float cursor = 0f;
        for (int i = 0; i < segs.Count; i++)
        {
            var seg = segs[i];
            var span = new Span { index = i, segment = seg };
            if (i > 0)
            {
                var prev = t.spans[i - 1];
                float d = seg.transitionSeconds;
                if (seg.transitionIn == PlaylistTransition.Cut)
                {
                    // A cut, crossfaded over a few hundredths of a second so it does not click.
                    d = CutSeconds;
                    span.inStart = prev.audioEnd - d * 0.5f;
                    span.audioStart = span.inStart;
                }
                else if (seg.transitionIn == PlaylistTransition.Interlude)
                {
                    // Nothing overlaps: the old track has ended, the cards play, the new one starts.
                    d = prev.segment.role == PlaylistSegmentRole.Spoiler ? playlist.introCardsSeconds : playlist.interludeSeconds;
                    span.inStart = prev.audioEnd;
                    span.audioStart = prev.audioEnd + d;
                }
                else if (seg.transitionIn == PlaylistTransition.Crossfade)
                {
                    span.inStart = prev.audioEnd - d;
                    span.audioStart = span.inStart;
                }
                else
                {
                    span.inStart = prev.audioEnd - d * 0.5f;
                    span.audioStart = prev.audioEnd;
                }
                span.inEnd = span.inStart + d;
                span.inMiddle = span.inStart + d * 0.5f;
                span.transitionLength = d;
                cursor = span.audioStart;
            }
            span.audioStart = cursor;
            span.audioEnd = cursor + seg.PlayedLength;
            var meta = charts[i].meta;
            span.bpm = meta.bpm >= 20f ? meta.bpm : 120f;
            span.secondsPerBeat = 60f / span.bpm;
            // The grid is anchored on the chart's first note, as HudBeatClock and ClubBeat do.
            float first = charts[i].cues.Count > 0 ? charts[i].cues[0].time + meta.audioOffset : 0f;
            span.gridAnchor = span.Offset + first;
            t.spans.Add(span);
        }
    }

    /// Moves every segment's notes and sets onto the song clock and drops what the transitions
    /// need clear. A note belongs to the part of its track that plays, outside both transitions
    /// that border it (and a margin), and a hold must be over before the next transition starts.
    /// At most `wallsInTransition` Dodge walls are kept inside a transition.
    private void Merge(RunPlaylist playlist, List<(Beatmap meta, List<BeatmapCue> cues)> charts)
    {
        var cues = new List<BeatmapCue>();
        var sets = new List<BeatmapSet>();
        var report = new System.Text.StringBuilder();
        int walls = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            var meta = charts[i].meta;
            float margin = playlist.noteMarginBeats * span.secondsPerBeat;
            // After a crossfade the new track's notes wait for the end of the change; after a
            // thunderclap the new track starts ON the clap, and so may its notes, a beat later.
            // A cut keeps every note up to it, on both sides; after an interlude the notes start
            // with the music (the cards were the margin).
            float from = !span.HasTransitionIn ? float.NegativeInfinity
                       : span.IsCut || span.IsInterlude ? span.audioStart
                       : span.segment.transitionIn == PlaylistTransition.Thunder ? span.inMiddle + margin
                       : span.inEnd + margin;
            var next = i + 1 < spans.Count ? spans[i + 1] : null;
            float until = next != null ? (next.IsCut ? next.inStart : next.IsInterlude ? span.audioEnd : next.inStart - margin) : span.audioEnd;
            float shift = span.Offset + meta.audioOffset;
            int kept = 0, dropped = 0;
            foreach (var c in charts[i].cues)
            {
                float time = c.time + shift;
                bool inside = time >= Mathf.Max(from, span.audioStart) && time + c.duration <= until;
                bool wallInTransition = c.type == CueType.Dodge && !inside && walls < playlist.wallsInTransition
                                        && InTransition(time) && time >= span.audioStart && time <= span.audioEnd;
                if (!inside && !wallInTransition) { dropped++; continue; }
                if (wallInTransition) walls++;
                cues.Add(new BeatmapCue(time, c.type, c.lane, c.duration, c.intensity, c.arc, c.radius));
                kept++;
            }
            if (meta.sets != null)
                foreach (var s in meta.sets)
                    sets.Add(new BeatmapSet
                    {
                        index = sets.Count, exercise = s.exercise, notes = s.notes,
                        start = s.start + shift, end = s.end + shift, restEnd = s.restEnd + shift,
                    });
            report.Append("  ").Append(i + 1).Append(". ").Append(span.segment.label).Append(": audio ")
                  .Append(span.audioStart.ToString("F2")).Append("..").Append(span.audioEnd.ToString("F2"))
                  .Append(" s, bpm ").Append(span.bpm.ToString("0.###"))
                  .Append(span.HasTransitionIn ? ", in " + span.segment.transitionIn + " " + span.inStart.ToString("F2") + ".." + span.inEnd.ToString("F2") : "")
                  .Append(", notes ").Append(kept).Append(" kept / ").Append(dropped).Append(" dropped\n");
        }

        // Stable by time, like BeatmapReader: a chord keeps the order its chart wrote it in.
        var order = new int[cues.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        System.Array.Sort(order, (a, b) => { int c = cues[a].time.CompareTo(cues[b].time); return c != 0 ? c : a.CompareTo(b); });
        Cues = new List<BeatmapCue>(cues.Count);
        foreach (int i in order) Cues.Add(cues[i]);

        // Sets that lost their notes to a transition go, and the rest count what they kept.
        var keptSets = new List<BeatmapSet>();
        foreach (var s in sets)
        {
            int n = Cues.FindAll(c => c.time >= s.start - 0.001f && c.time <= s.restEnd).Count;
            if (n == 0) continue;
            s.notes = Mathf.Min(s.notes, n);
            s.index = keptSets.Count;
            keptSets.Add(s);
        }

        Meta = new Beatmap
        {
            version = BeatmapReader.SupportedVersion,
            track = playlist.title,
            bpm = spans[0].bpm,
            audioOffset = 0f,
            generator = "PlaylistTimeline (" + spans.Count + " segments)",
            sets = keptSets.ToArray(),
        };
        Report = "[Playlist] '" + playlist.title + "': " + spans.Count + " segment(s), " + Length.ToString("F2") + " s, "
               + Cues.Count + " notes\n" + report;
    }

    private bool InTransition(float time)
    {
        foreach (var s in spans)
            if (s.HasTransitionIn && time >= s.inStart && time <= s.inEnd) return true;
        return false;
    }

    /// The segment whose music is the main one at `time`: before a transition's middle the old
    /// one, from it on the new one.
    public int SegmentAt(float time)
    {
        for (int i = spans.Count - 1; i > 0; i--)
            if (time >= spans[i].inMiddle) return i;
        return 0;
    }
}
