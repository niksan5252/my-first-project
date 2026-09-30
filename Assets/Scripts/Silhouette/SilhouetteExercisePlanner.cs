using System.Collections.Generic;
using UnityEngine;

/// The trainer's performance built from motion capture: every note is shown by a Mixamo take whose
/// impact frame - the jab's full reach, the tap of a foot, the landing of a jump - is put exactly on
/// the note by time-warping the take, and the takes crossfade into each other on the beat. What
/// the takes cannot say is added on top, lightly: a touch of IK toward the cube or the tile at the
/// note (the gestures), the stand on one leg beside a kerb, and where the figure stands (the home
/// track and the side steps' own travel).
///
/// Which take shows what:
///   Punch - a jab, cross or hook with the hand of the note's side; PunchHigh - an uppercut;
///   PunchHold - the uppercut held at its top; PunchSweep - the hook drawn out over the note;
///   runs of Kick/KickHold - one dance loop (Step Touch, Kick Step, Running Man), each note on the
///   next tap of the right foot; jack runs - Jumping Jacks, open and closed landings on the notes;
///   Cross (holds on both outer lanes) - the jack's open landing held, the star; side jumps, pair
///   shifts and chords - a jump in place per note, carried sideways to the pair; a punch in a chord
///   is thrown by that arm alone over the jump; Duck - the air squat's bottom held under the bar;
///   Curb - the standing jump's apex on the camera's hop; walls and CurbGap - a side step out with
///   the camera and back, its own travel carrying the figure over.
/// Hands over a leg exercise play on their arm only (an avatar-masked layer); alone they move the
/// whole body - weight, hips and shoulders turning into the punch.
public static class SilhouetteExercisePlanner
{
    /// One take on the song clock. Clip time is piecewise linear between keys (song time -> clip
    /// time), extrapolated at the outer keys' speed; a loop's clip time wraps.
    public sealed class Move
    {
        public SilhouetteMotion motion;
        public int layer;              // 0 the body, 1 the figure's left arm, 2 its right arm
        public float start, end, fadeIn, fadeOut;
        public float amp = 1f;         // < 1: blended with the stance - a smaller version of the take
        public readonly List<float> keySong = new List<float>(), keyClip = new List<float>();
        public bool carries;           // a side step: its own sideways travel moves the figure
        public int note = -1;
        public string label;
        public int instance;           // which copy of the clip plays it - set by the trainer
        // The notes this take lands on: song time, the clip time it is at then, the limb that
        // should deliver it (0 left, 1 right, -1 both) and the chart note - for the sync report.
        public readonly List<Hit> hits = new List<Hit>();
        public void AddHit(float song, float clip, int limb, int note) => hits.Add(new Hit { song = song, clip = clip, limb = limb, note = note });

        public void Key(float song, float clip)
        {
            // Keys only ever move forward in song time; a key at the same moment replaces the last.
            if (keySong.Count > 0 && song <= keySong[keySong.Count - 1] + 1e-4f)
            {
                keyClip[keyClip.Count - 1] = Mathf.Max(keyClip[keyClip.Count - 1], clip);
                return;
            }
            keySong.Add(song);
            keyClip.Add(clip);
        }

        private float Speed(int i) => (keyClip[i + 1] - keyClip[i]) / Mathf.Max(1e-5f, keySong[i + 1] - keySong[i]);

        /// Unwrapped clip time at a song time.
        public float RawTime(float t)
        {
            int n = keySong.Count;
            if (n == 0) return t - start;
            if (n == 1) return keyClip[0] + (t - keySong[0]);
            if (t <= keySong[0]) return keyClip[0] + (t - keySong[0]) * Speed(0);
            if (t >= keySong[n - 1]) return keyClip[n - 1] + (t - keySong[n - 1]) * Speed(n - 2);
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (keySong[mid] <= t) lo = mid; else hi = mid; }
            return Mathf.Lerp(keyClip[lo], keyClip[hi], (t - keySong[lo]) / Mathf.Max(1e-5f, keySong[hi] - keySong[lo]));
        }

        public float ClipTime(float t)
        {
            float c = RawTime(t), len = Mathf.Max(1e-4f, motion.Length);
            return motion.Loops ? Mathf.Repeat(c, len) : Mathf.Clamp(c, 0f, len);
        }

        public float Envelope(float t)
        {
            if (t < start || t >= end) return 0f;
            float a = fadeIn <= 0f ? 1f : Smooth((t - start) / fadeIn);
            float b = fadeOut <= 0f ? 1f : Smooth((end - t) / fadeOut);
            return Mathf.Min(a, b);
        }
    }

    public struct Hit { public float song, clip; public int limb, note; }

    /// Where the figure stands sideways, apart from walls and kerbs: carried over to the pair of a
    /// side jump during the jump, back to the middle after the run. x in figure metres.
    public struct HomeStep { public float t0, t1, x0, x1; }

    public static float Home(List<HomeStep> steps, float t)
    {
        float x = 0f;
        foreach (var s in steps)
        {
            if (t < s.t0) break;
            x = t >= s.t1 ? s.x1 : Mathf.Lerp(s.x0, s.x1, Smooth((t - s.t0) / Mathf.Max(1e-4f, s.t1 - s.t0)));
        }
        return x;
    }

    private enum Leg { None, Step, JackOpen, JackClosed, JackHold, Hop, ChordHop }

    private sealed class Moment
    {
        public float t;
        public readonly List<int> notes = new List<int>();
        public Leg leg;
        public int legNote = -1;          // the note that times the legs (a kick, a hold)
        public int stepLimb;              // Step: the figure's foot (0 left, 1 right)
        public float hold;                // a held exercise: its length
        public float pairCentre;          // a pair: -1 the two left lanes .. 1 the two right lanes
        public bool starArms;             // two PunchHolds on the outer lanes: both arms up and out
        public readonly List<int> hands = new List<int>();
    }

    private const float SameMoment = 0.002f;

    public static void Plan(SilhouetteMotionSet set, BeatmapSequencer chart, System.Func<float, float> beatAt, int laneCount,
                            string seedText, SilhouettePlanner.ObstacleTimingFn obstacleTiming, float travelScale,
                            out List<Move> moves, out List<SilhouettePlanner.Gesture> gestures, out List<HomeStep> home)
    {
        moves = new List<Move>();
        gestures = new List<SilhouettePlanner.Gesture>();
        home = new List<HomeStep>();
        var rng = new System.Random(SilhouettePlanner.StableHash(seedText) ^ 0x5eed);
        var moments = Moments(chart, laneCount);
        ClassifyPairs(moments, beatAt);

        // --- the legs: runs of one kind of exercise, each one take ---
        var legBusy = new List<Vector2>();   // spans the legs are doing an exercise: hands go on the arms
        int k = 0;
        while (k < moments.Count)
        {
            var m = moments[k];
            if (m.leg == Leg.None) { k++; continue; }
            var run = new List<Moment> { m };
            int j = k + 1;
            for (; j < moments.Count; j++)
            {
                var o = moments[j];
                if (o.leg == Leg.None) continue;
                if (Family(o.leg) != Family(m.leg) || o.t - run[run.Count - 1].t - run[run.Count - 1].hold > 2.6f * beatAt(o.t)) break;
                run.Add(o);
            }
            var last = run[run.Count - 1];
            switch (Family(m.leg))
            {
                case 0: StepRun(set, run, beatAt, rng, moves, gestures, chart, laneCount); break;
                case 1: JackRun(set, run, beatAt, moves); break;
                default: HopRun(set, run, beatAt, moves, home, travelScale); break;
            }
            legBusy.Add(new Vector2(m.t - beatAt(m.t), last.t + last.hold + 0.5f * beatAt(last.t)));
            k = j;
        }

        // --- the hands ---
        float lastBaseImpact = float.NegativeInfinity;
        SilhouetteMotion lastPunch = null, lastUpper = null;
        foreach (var m in moments)
        {
            float beat = beatAt(m.t);
            float fade = set.takeFadeBeats * beat;
            bool legsBusy = m.leg != Leg.None;
            foreach (var span in legBusy) if (m.t >= span.x && m.t <= span.y) legsBusy = true;

            if (m.starArms)
            {
                // Both arms up and out: the jack's open landing, on the arms only, held.
                var jack = First(set.jacks, false);
                float open = FirstMark(jack, MarkKind.LandOpen);
                int n = m.hands[0];
                float dur = chart[n].duration;
                for (int arm = 1; arm <= 2; arm++)
                {
                    var mv = new Move { motion = jack, layer = arm, note = n, label = "Star arms" };
                    mv.Key(m.t - 0.4f, open - 0.4f);
                    mv.Key(m.t, open);
                    mv.Key(m.t + dur, open + 0.03f);
                    mv.start = m.t - 0.4f; mv.fadeIn = 0.3f;
                    mv.end = m.t + dur + fade; mv.fadeOut = fade;
                    moves.Add(mv);
                }
                continue;
            }

            bool baseTaken = legsBusy;
            foreach (int n in m.hands)
            {
                var c = chart[n];
                int limb = LimbOf(c.lane, laneCount);
                bool up = c.type == CueType.PunchHigh || c.type == CueType.PunchHold;
                var family = up ? set.uppercuts : set.punches;
                SilhouetteMotion take;
                if (up) take = PickLimb(family, limb, rng, ref lastUpper);
                else take = PickLimb(family, limb, rng, ref lastPunch);
                if (take == null) continue;
                int layer = baseTaken ? (limb == 0 ? 1 : 2) : 0;
                float room = layer == 0 ? (float.IsNegativeInfinity(lastBaseImpact) ? 10f : m.t - lastBaseImpact) : 1.2f * beat;
                var mv = OneShot(take, take.impact, m.t, room, set.takeMaxSpeed, fade, layer);
                mv.note = n;
                mv.label = c.type + " " + take.clip.name + (layer > 0 ? " (arm)" : "");
                float len = take.Length;
                if (c.type == CueType.PunchHold && c.duration > 0.05f)
                {
                    // Held at the top for the note, then down.
                    Rekey(mv, m.t, take.impact, m.t + c.duration, take.impact + 0.08f, len);
                }
                else if (c.type == CueType.PunchSweep && c.duration > 0.05f)
                {
                    // The hook's swing drawn out over the note's tail.
                    Rekey(mv, m.t, take.impact - 0.12f, m.t + c.duration, take.impact + 0.15f, len);
                }
                if (layer > 0 && mv.end > m.t + beat + (c.duration > 0.05f ? c.duration : 0f))
                {
                    // Over a leg exercise the arm is back with it a beat after the punch: its take's
                    // recovery would otherwise hold the arm out into the next note.
                    mv.end = m.t + beat + (c.duration > 0.05f ? c.duration : 0f);
                    mv.fadeOut = Mathf.Min(mv.fadeOut, 0.6f * beat);
                }
                mv.AddHit(m.t, take.impact, limb, n);
                moves.Add(mv);
                if (layer == 0) { lastBaseImpact = m.t; baseTaken = true; }

                var kind = c.type == CueType.PunchHigh ? SilhouettePlanner.GestureKind.PunchHigh
                         : c.type == CueType.Punch ? SilhouettePlanner.GestureKind.Punch : SilhouettePlanner.GestureKind.PunchHold;
                gestures.Add(Assist(kind, c, n, laneCount, set.assistBeats * beat));
            }
        }

        // --- obstacles ---
        for (int i = 0; i < chart.Count; i++)
        {
            var c = chart[i];
            float beat = beatAt(c.time);
            float fade = set.takeFadeBeats * beat;
            int side = c.lane < laneCount * 0.5f ? -1 : 1;
            switch (c.type)
            {
                case CueType.Dodge:
                case CueType.CurbGap:
                {
                    obstacleTiming(c, out float begin, out float commit, out float release, out float settled, out int camSide);
                    if (camSide == 0) camSide = side;
                    gestures.Add(new SilhouettePlanner.Gesture
                    {
                        kind = c.type == CueType.Dodge ? SilhouettePlanner.GestureKind.Dodge : SilhouettePlanner.GestureKind.OneLeg,
                        side = camSide, outer = true, glide = true, start = begin, impact = commit, holdEnd = release, end = settled, note = i,
                    });
                    // Out with the camera and back: a short side step each way. Toward the
                    // screen's side camSide is the figure's -camSide X.
                    float dir = -camSide;
                    var outStep = SideStep(set, dir);
                    var backStep = SideStep(set, -dir);
                    if (outStep != null) moves.Add(StepMove(outStep, begin, commit, fade, i, "Step out " + outStep.clip.name));
                    if (backStep != null) moves.Add(StepMove(backStep, release, settled, fade, i, "Step back " + backStep.clip.name));
                    break;
                }
                case CueType.CurbSplit:
                    gestures.Add(new SilhouettePlanner.Gesture
                    {
                        kind = SilhouettePlanner.GestureKind.Wide, side = side, outer = true, start = c.time - beat, impact = c.time,
                        holdEnd = c.time + c.duration, end = c.time + c.duration + beat, note = i,
                    });
                    break;
                case CueType.Duck:
                {
                    obstacleTiming(c, out float begin, out float commit, out float release, out float settled, out _);
                    var sq = set.squat;
                    if (sq == null || !sq.Valid) break;
                    // Down onto the camera's duck, the bottom held under the bar, up with the camera.
                    float down = Mathf.Min(sq.impact, Mathf.Max(0.2f, commit - begin + beat));
                    float sp = sq.impact / down;
                    var mv = new Move { motion = sq, layer = 0, note = i, label = "Duck " + sq.clip.name };
                    mv.Key(commit - down, 0f);
                    mv.Key(commit, sq.impact);
                    mv.Key(Mathf.Max(commit + 0.05f, release), sq.impact + 0.06f);
                    float rise = sq.rise > sq.impact ? sq.rise : sq.Length;
                    mv.Key(Mathf.Max(release + 0.1f, settled), rise);
                    float tail = sq.Length - rise;
                    mv.Key(Mathf.Max(release + 0.1f, settled) + tail, sq.Length);
                    mv.start = commit - down; mv.fadeIn = Mathf.Min(fade, down * 0.5f / Mathf.Max(1f, sp));
                    mv.end = Mathf.Max(release + 0.1f, settled) + tail; mv.fadeOut = Mathf.Min(fade, tail + 0.1f);
                    moves.Add(mv);
                    break;
                }
                case CueType.Curb:
                case CueType.Jump:
                {
                    obstacleTiming(c, out _, out float commit, out _, out _, out _);
                    SilhouetteMotion last = null;
                    var jump = SilhouettePlanner.Pick(set.curb, ref last, rng);
                    if (jump == null) break;
                    var mv = OneShot(jump, jump.impact, commit, 10f, set.takeMaxSpeed, fade, 0);
                    mv.note = i; mv.label = "Curb " + jump.clip.name;
                    moves.Add(mv);
                    break;
                }
            }
        }

        moves.Sort((a, b) => a.start.CompareTo(b.start));
        gestures.Sort((a, b) => a.start.CompareTo(b.start));
        home.Sort((a, b) => a.t0.CompareTo(b.t0));
    }

    // --- building the moments ---

    private static List<Moment> Moments(BeatmapSequencer chart, int laneCount)
    {
        var order = new List<int>();
        for (int i = 0; i < chart.Count; i++) order.Add(i);
        order.Sort((a, b) => chart[a].time.CompareTo(chart[b].time));
        var list = new List<Moment>();
        foreach (int i in order)
        {
            var c = chart[i];
            if (c.type == CueType.Dodge || c.type == CueType.Curb || c.type == CueType.Duck || c.type == CueType.Jump
                || c.type == CueType.CurbGap || c.type == CueType.CurbSplit) continue;
            if (list.Count == 0 || c.time - list[list.Count - 1].t > SameMoment) list.Add(new Moment { t = c.time });
            list[list.Count - 1].notes.Add(i);
        }
        int outerR = laneCount - 1;
        float mid = (laneCount - 1) * 0.5f;
        foreach (var m in list)
        {
            var kicks = new List<int>();
            var kholds = new List<int>();
            var pholds = new List<int>();
            foreach (int n in m.notes)
            {
                var t = chart[n].type;
                if (t == CueType.Kick) kicks.Add(n);
                else if (t == CueType.KickHold) kholds.Add(n);
                else if (t == CueType.PunchHold) { pholds.Add(n); m.hands.Add(n); }
                else m.hands.Add(n);
            }
            bool OuterPair(List<int> l) => l.Count == 2 && IsOuter(chart[l[0]].lane, outerR) && IsOuter(chart[l[1]].lane, outerR) && chart[l[0]].lane != chart[l[1]].lane;
            if (OuterPair(kholds))
            {
                // The star (Cross): the jack's open landing held; its arms are the take's own.
                m.leg = Leg.JackHold; m.legNote = kholds[0]; m.hold = chart[kholds[0]].duration;
                if (OuterPair(pholds)) foreach (int n in pholds) m.hands.Remove(n);
            }
            else if (OuterPair(pholds) && kicks.Count == 0 && kholds.Count == 0)
            {
                m.starArms = true;
                m.hands.Clear();
                m.hands.Add(pholds[0]);
            }
            if (m.leg == Leg.None && kicks.Count == 2)
            {
                int a = Mathf.Min(chart[kicks[0]].lane, chart[kicks[1]].lane), b = Mathf.Max(chart[kicks[0]].lane, chart[kicks[1]].lane);
                m.leg = a == 0 && b == outerR ? Leg.JackOpen : Leg.Hop;   // refined by ClassifyPairs
                m.legNote = kicks[0];
                m.pairCentre = mid > 0f ? ((a + b) * 0.5f - mid) / mid : 0f;
            }
            else if (m.leg == Leg.None && kicks.Count + kholds.Count >= 1)
            {
                int n = kicks.Count > 0 ? kicks[0] : kholds[0];
                m.leg = Leg.Step; m.legNote = n;
                m.stepLimb = LimbOf(chart[n].lane, laneCount);
                if (chart[n].type == CueType.KickHold) m.hold = chart[n].duration;
                // A second foot note at the same moment that is not a pair: shown by the take anyway.
            }
        }
        return list;
    }

    /// A pair of kicks in the middle (lanes 1+2) is the closed half of a jumping jack inside a
    /// jack run, a jump in place in a run of chords, and one of the jumps of a pair shift; pairs
    /// off the middle are side jumps. Decided per run of pairs.
    private static void ClassifyPairs(List<Moment> moments, System.Func<float, float> beatAt)
    {
        int k = 0;
        while (k < moments.Count)
        {
            if (moments[k].leg != Leg.Hop && moments[k].leg != Leg.JackOpen) { k++; continue; }
            var run = new List<Moment> { moments[k] };
            int j = k + 1;
            for (; j < moments.Count; j++)
            {
                var o = moments[j];
                if (o.leg == Leg.None) continue;
                if ((o.leg != Leg.Hop && o.leg != Leg.JackOpen) || o.t - run[run.Count - 1].t > 2.6f * beatAt(o.t)) break;
                run.Add(o);
            }
            bool open = run.Exists(o => o.leg == Leg.JackOpen);
            bool sideways = run.Exists(o => o.leg == Leg.Hop && Mathf.Abs(o.pairCentre) > 0.01f);
            foreach (var o in run)
            {
                if (o.leg == Leg.JackOpen) continue;
                o.leg = open && !sideways ? Leg.JackClosed : sideways ? Leg.Hop : Leg.ChordHop;
            }
            k = j;
        }
    }

    private static int Family(Leg l) => l == Leg.Step ? 0 : (l == Leg.JackOpen || l == Leg.JackClosed || l == Leg.JackHold) ? 1 : 2;

    private static bool IsOuter(int lane, int outerR) => lane == 0 || lane == outerR;

    /// A note on the screen's left is answered, as in a mirror, by the figure's right hand or foot.
    private static int LimbOf(int lane, int laneCount) => lane < laneCount * 0.5f ? 1 : 0;

    // --- runs ---

    private static void StepRun(SilhouetteMotionSet set, List<Moment> run, System.Func<float, float> beatAt, System.Random rng,
                                List<Move> moves, List<SilhouettePlanner.Gesture> gestures, BeatmapSequencer chart, int laneCount)
    {
        var loops = set.steps.FindAll(s => s != null && s.Valid && s.marks.Length > 0);
        if (loops.Count == 0) return;
        var take = loops[rng.Next(loops.Count)];
        float len = take.Length;
        var first = run[0];
        float beat = beatAt(first.t);
        // Start on a random tap of the right foot, so every run shows another part of the dance.
        var starts = new List<int>();
        for (int i = 0; i < take.marks.Length; i++) if (take.markLimbs[i] == first.stepLimb) starts.Add(i);
        float clip = starts.Count > 0 ? take.marks[starts[rng.Next(starts.Count)]] : take.marks[0];
        var mv = new Move { motion = take, layer = 0, note = first.legNote, label = "Steps " + take.clip.name + " x" + run.Count };
        mv.Key(first.t - beat, clip - beat);
        mv.Key(first.t, clip);
        float prevT = first.t;
        int hits = 0;
        for (int r = 0; r < run.Count; r++)
        {
            var m = run[r];
            if (r > 0)
            {
                float dt = m.t - prevT;
                float found = FindMark(take, clip, dt * set.takeMinSpeed, dt * set.takeMaxSpeed, dt, i => take.markLimbs[i] == m.stepLimb);
                if (float.IsNaN(found)) found = FindMark(take, clip, dt * set.takeMinSpeed, dt * set.takeMaxSpeed, dt, i => true);
                else hits++;
                if (float.IsNaN(found)) found = clip + dt;
                Dwell(mv, prevT, clip, m.t, found);
                clip = found;
            }
            else hits++;
            mv.AddHit(m.t, clip, m.stepLimb, m.legNote);
            float held = 0f;
            if (m.hold > 0.05f)
            {
                // The foot stays on the tile, the take barely moving on - but lets go 0.4 beat
                // before the next note, so the step there is a step and not a jump cut.
                float release = r + 1 < run.Count ? Mathf.Min(m.hold, run[r + 1].t - m.t - 0.4f * beatAt(m.t)) : m.hold;
                held = Mathf.Max(0.05f, release);
                mv.Key(m.t + held, clip + 0.1f);
                clip += 0.1f;
            }
            prevT = m.t + held;
            var c = chart[m.legNote];
            gestures.Add(Assist(m.hold > 0.05f ? SilhouettePlanner.GestureKind.KickHold : SilhouettePlanner.GestureKind.Kick, c, m.legNote, laneCount, set.assistBeats * beatAt(m.t)));
        }
        var last = run[run.Count - 1];
        float tail = beatAt(last.t);
        mv.Key(prevT + tail, clip + tail);
        mv.start = first.t - beat; mv.fadeIn = set.takeFadeBeats * beat;
        mv.end = prevT + tail + set.takeFadeBeats * tail; mv.fadeOut = set.takeFadeBeats * tail;
        mv.label += " (" + hits + " on the right foot)";
        moves.Add(mv);
    }

    private static void JackRun(SilhouetteMotionSet set, List<Moment> run, System.Func<float, float> beatAt, List<Move> moves)
    {
        var take = First(set.jacks, false);
        if (take == null || take.marks.Length == 0) return;
        var first = run[0];
        float beat = beatAt(first.t);
        bool OpenOf(Moment m) => m.leg != Leg.JackClosed;
        float clip = FirstMark(take, OpenOf(first) ? MarkKind.LandOpen : MarkKind.LandClosed);
        var mv = new Move { motion = take, layer = 0, note = first.legNote, label = "Jack " + take.clip.name + " x" + run.Count };
        mv.Key(first.t - beat, clip - beat);
        mv.Key(first.t, clip);
        float prevT = first.t;
        for (int r = 0; r < run.Count; r++)
        {
            var m = run[r];
            if (r > 0)
            {
                float dt = m.t - prevT;
                var want = OpenOf(m) ? MarkKind.LandOpen : MarkKind.LandClosed;
                float found = FindMark(take, clip, dt * set.takeMinSpeed * 0.8f, dt * set.hopMaxSpeed, dt, i => take.markKinds[i] == want);
                if (float.IsNaN(found)) found = FindMark(take, clip, 0.05f, 10f, dt, i => take.markKinds[i] == want);
                Dwell(mv, prevT, clip, m.t, found);
                clip = found;
            }
            mv.AddHit(m.t, clip, -1, m.legNote);
            if (m.hold > 0.05f)
            {
                mv.Key(m.t + m.hold, clip + 0.03f);
                clip += 0.03f;
            }
            prevT = m.t + m.hold;
        }
        // Out of the last landing over half a beat, back into the stance.
        float tail = 0.5f * beatAt(prevT);
        mv.Key(prevT + tail, clip + tail);
        mv.start = first.t - beat; mv.fadeIn = set.takeFadeBeats * beat;
        mv.end = prevT + tail + set.takeFadeBeats * beat; mv.fadeOut = set.takeFadeBeats * beat;
        moves.Add(mv);
    }

    private static void HopRun(SilhouetteMotionSet set, List<Moment> run, System.Func<float, float> beatAt, List<Move> moves,
                               List<HomeStep> home, float travelScale)
    {
        float x = 0f, prevT = float.NegativeInfinity;
        for (int r = 0; r < run.Count; r++)
        {
            var m = run[r];
            float beat = beatAt(m.t);
            // Side jumps and shifts (a jump every beat): the smaller take; chords: the two in turn
            // (B lands deeper, into a squat).
            var take = HopTake(set, m.leg == Leg.ChordHop && r % 2 == 1 ? "B" : "A");
            if (take == null) return;
            float gap = float.IsNegativeInfinity(prevT) ? 2f * beat : m.t - prevT;
            float prep = Mathf.Max(0f, take.takeoff - 0.3f);
            float span = take.impact - prep;
            float window = Mathf.Min(span, gap * 0.95f);
            float speed = Mathf.Clamp(span / window, 1f, set.hopMaxSpeed);
            float clipIn = take.impact - window * speed;
            var mv = new Move { motion = take, layer = 0, note = m.legNote, label = m.leg + " " + take.clip.name };
            mv.Key(m.t - window, clipIn);
            mv.Key(m.t, take.impact);
            mv.AddHit(m.t, take.impact, -1, m.legNote);
            float tail = Mathf.Min(take.Length - take.impact, 0.5f);
            mv.Key(m.t + tail, take.impact + tail);
            mv.start = m.t - window; mv.fadeIn = Mathf.Min(set.takeFadeBeats * beat, window * 0.4f);
            mv.end = m.t + tail; mv.fadeOut = Mathf.Min(0.3f, tail);
            // A jump every beat is a bounce: the take blended down with the stance.
            if (gap < 0.65f) mv.amp = set.quickHopAmount;
            moves.Add(mv);
            if (m.leg == Leg.Hop)
            {
                // Carried over to the pair while in the air: screen side s -> the figure's -s X.
                float nx = -m.pairCentre * set.chordShift * travelScale;
                float air = (take.impact - take.takeoff) / speed;
                home.Add(new HomeStep { t0 = m.t - air, t1 = m.t, x0 = x, x1 = nx });
                x = nx;
            }
            if (m.hold > 0.05f) mv.Key(m.t + m.hold, take.impact + 0.05f);
            prevT = m.t + m.hold;
        }
        if (Mathf.Abs(x) > 1e-4f)
        {
            float b = beatAt(prevT);
            home.Add(new HomeStep { t0 = prevT + 0.25f * b, t1 = prevT + 1.25f * b, x0 = x, x1 = 0f });
        }
    }

    // --- takes ---

    /// A one-shot with its impact on `t`: wound up at its own speed if there is room, sped up (up to
    /// maxSpeed) and then cut into if there is not; after the impact at its own speed.
    private static Move OneShot(SilhouetteMotion m, float impact, float t, float room, float maxSpeed, float fade, int layer)
    {
        float real = Mathf.Min(impact, Mathf.Max(0.15f, room * 0.85f));
        float speed = 1f, clipIn = 0f;
        if (real < impact)
        {
            speed = Mathf.Min(maxSpeed, impact / real);
            clipIn = impact - real * speed;
        }
        var mv = new Move { motion = m, layer = layer };
        mv.Key(t - real, clipIn);
        mv.Key(t, impact);
        float after = m.Length - impact;
        mv.Key(t + after, m.Length);
        mv.start = t - real;
        mv.fadeIn = Mathf.Min(fade, real * 0.7f);
        mv.end = t + after;
        mv.fadeOut = Mathf.Min(fade, after);
        return mv;
    }

    /// Replaces a one-shot's keys after `t0`: clip c0 at t0, c1 at t1, then on at its own speed.
    private static void Rekey(Move mv, float t0, float c0, float t1, float c1, float len)
    {
        int keep = 0;
        while (keep < mv.keySong.Count && mv.keySong[keep] < t0 - 1e-4f) keep++;
        mv.keySong.RemoveRange(keep, mv.keySong.Count - keep);
        mv.keyClip.RemoveRange(keep, mv.keyClip.Count - keep);
        mv.Key(t0, c0);
        mv.Key(t1, c1);
        float after = Mathf.Max(0f, len - c1);
        mv.Key(t1 + after, len);
        mv.end = t1 + after;
        mv.fadeOut = Mathf.Min(mv.fadeOut, Mathf.Max(0.1f, after));
    }

    /// A side step keyed onto the camera's move: starting with it, its 95% travel on the commit.
    private static Move StepMove(SilhouetteMotion m, float from, float to, float fade, int note, string label)
    {
        var mv = new Move { motion = m, layer = 0, carries = true, note = note, label = label };
        float span = Mathf.Max(0.1f, to - from);
        float speed = Mathf.Max(0.5f, m.impact / span);
        mv.Key(from, 0f);
        mv.Key(to, m.impact);
        float after = (m.Length - m.impact) / speed;
        mv.Key(to + after, m.Length);
        mv.start = from - 0.05f;
        mv.fadeIn = Mathf.Min(fade, span * 0.35f);
        mv.end = to + after + fade;
        mv.fadeOut = fade;
        return mv;
    }

    /// Clip time for song time `tNext`, from (tPrev, cPrev): when the take has to be slowed down, the
    /// slowing is put right after the landing or the tap - a body absorbing it - and the rest of the
    /// way plays at the take's own speed, rather than the whole move going in slow motion.
    private static void Dwell(Move mv, float tPrev, float cPrev, float tNext, float cNext)
    {
        float dt = tNext - tPrev, dc = cNext - cPrev;
        if (dt > 1e-4f && dc / dt < 0.9f && dc > 0f)
        {
            float flight = 0.65f * dc;               // at speed 1
            float midT = tNext - flight;
            if (midT > tPrev + 0.02f) mv.Key(midT, cNext - flight);
        }
        mv.Key(tNext, cNext);
    }

    /// The next mark after clip time `after` (unwrapped over the loop) that `ok` accepts, advanced by
    /// between minAdv and maxAdv, closest to `ideal`; NaN when there is none.
    private static float FindMark(SilhouetteMotion m, float after, float minAdv, float maxAdv, float ideal, System.Func<int, bool> ok)
    {
        float len = Mathf.Max(1e-3f, m.Length);
        float best = float.NaN, bestScore = float.PositiveInfinity;
        int k0 = Mathf.FloorToInt(after / len) - 1;
        for (int k = k0; k <= k0 + 3 + Mathf.CeilToInt(maxAdv / len); k++)
            for (int i = 0; i < m.marks.Length; i++)
            {
                float c = m.marks[i] + k * len;
                float adv = c - after;
                if (adv < minAdv || adv > maxAdv || !ok(i)) continue;
                float score = Mathf.Abs(adv - ideal);
                if (score < bestScore) { bestScore = score; best = c; }
            }
        return best;
    }

    private static float FirstMark(SilhouetteMotion m, MarkKind kind)
    {
        for (int i = 0; i < m.marks.Length; i++) if (m.markKinds[i] == kind) return m.marks[i];
        return m.marks.Length > 0 ? m.marks[0] : 0f;
    }

    private static SilhouetteMotion First(List<SilhouetteMotion> l, bool mirror)
    {
        foreach (var m in l) if (m != null && m.Valid && m.clip.name.EndsWith("(Mirror)") == mirror) return m;
        return l.Count > 0 ? l[0] : null;
    }

    private static SilhouetteMotion HopTake(SilhouetteMotionSet set, string variant)
    {
        foreach (var m in set.hops) if (m != null && m.Valid && !m.clip.name.EndsWith("(Mirror)") && m.clip.name.EndsWith(" " + variant)) return m;
        return First(set.hops, false);
    }

    /// The side step whose travel goes toward `dir` (the figure's X): the short right one and its
    /// mirror, the same step both ways.
    private static SilhouetteMotion SideStep(SilhouetteMotionSet set, float dir)
    {
        SilhouetteMotion any = null;
        foreach (var m in set.sideSteps)
        {
            if (m == null || !m.Valid || Mathf.Sign(m.travel) != Mathf.Sign(dir)) continue;
            if (m.clip.name.StartsWith("Side Step Short Right")) return m;
            any = any ?? m;
        }
        return any;
    }

    /// A take of the family with the given limb, never the same one twice running.
    private static SilhouetteMotion PickLimb(List<SilhouetteMotion> family, int limb, System.Random rng, ref SilhouetteMotion last)
    {
        var pool = new List<SilhouetteMotion>();
        foreach (var m in family) if (m != null && m.Valid && m.limb == limb && m != last) pool.Add(m);
        if (pool.Count == 0) foreach (var m in family) if (m != null && m.Valid && m.limb == limb) pool.Add(m);
        if (pool.Count == 0) return null;
        last = pool[rng.Next(pool.Count)];
        return last;
    }

    /// The IK touch-up on a note: the trainer's procedural reach or tap, at a fraction of its
    /// weight, over a short window around the note.
    private static SilhouettePlanner.Gesture Assist(SilhouettePlanner.GestureKind kind, BeatmapCue c, int n, int laneCount, float window)
    {
        float holdEnd = c.duration > 0.05f && (kind == SilhouettePlanner.GestureKind.PunchHold || kind == SilhouettePlanner.GestureKind.KickHold)
            ? c.time + c.duration : c.time;
        return new SilhouettePlanner.Gesture
        {
            kind = kind, side = c.lane < laneCount * 0.5f ? -1 : 1, outer = c.lane == 0 || c.lane == laneCount - 1, glide = true,
            start = c.time - window, impact = c.time, holdEnd = holdEnd, end = holdEnd + window, note = n,
        };
    }

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }
}
