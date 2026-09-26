using System.Collections.Generic;
using UnityEngine;

/// The four reactions an obstacle can ask of the camera. Dodge is split by direction rather than
/// carrying a sign, so the policy table below can give a left dodge and a right dodge different
/// amplitudes later without anything else having to know that it did.
public enum ObstacleMove { DodgeLeft, DodgeRight, Hop, Duck }

/// How one half of a move is shaped. This is the difference between a camera that MOVES and a
/// camera that drifts, and it matters more than the durations do.
///
/// `Smooth` starts and ends at zero speed. That is right for something being carried into place
/// and wrong for anything a body does on purpose: a jump does not ease into its takeoff, it
/// leaves the ground at full speed and is slowed by gravity from there. Rendered smooth, a
/// 1.15 m hop over half a second reads as the camera being lifted on a wire.
///
/// `Decelerate` is the takeoff half of that arc - fastest at the instant it starts, coasting to
/// a stop. It is also what a sidestep looks like: you push off hard and arrive.
/// `Accelerate` is the landing half - it leaves the apex at rest and is fastest as it arrives.
///
/// Put together, Decelerate out of rest and Accelerate back into it IS a ballistic arc, and the
/// hang at the top comes out of the curve itself rather than having to be held there.
///
/// `Glide` is Smooth with the acceleration also zero at both ends (smootherstep). It is what a
/// camera move that is watched rather than felt wants: Decelerate starts at full speed and
/// Smooth at full acceleration, and on a lean the eye reads either jump as a jerk.
public enum ObstacleEase { Smooth, Decelerate, Accelerate, Glide }

/// Works out where the camera should be, relative to its resting position, because of the
/// obstacles on the road — and nothing else. Sway, hit shake and the camera's own base position
/// are somebody else's business; this returns an offset to be added to them.
///
/// Two properties are load-bearing and everything here is shaped by them.
///
/// **It is a pure function of track time.** Evaluate(t) reads the registered obstacle list and a
/// fixed policy and returns a position. Nothing is integrated, nothing is smoothed toward a
/// target, no deltaTime appears anywhere. This is the same reason cue positions are solved from
/// the clock rather than advanced per frame: a recording made at 30 fps and one made at 144 fps
/// put the camera in exactly the same place at exactly the same moment in the music, and a
/// dropped frame costs one frame of smoothness rather than a permanent drift. A velocity-based
/// or Lerp-toward-target camera cannot promise either.
///
/// **Its numbers are beats, not seconds.** The lead-in, the extra hold and the return all
/// accompany the music, so they are authored against the chart's tempo and resolved through it.
/// The one duration that is NOT in beats is how long the obstacle physically takes to go past —
/// that comes from its length and the scroll speed, and it would be wrong to stretch it when the
/// tempo changes.
///
/// Overlapping obstacles simply add, then clamp. The generator spaces obstacles so this should
/// be rare, but when it does happen — one obstacle still returning as the next is anticipated —
/// adding gives a continuous blend from the first move into the second, which is exactly what is
/// wanted, whereas picking a winner would snap between them.
public sealed class ObstacleCameraDirector
{
    /// Everything one moment of the reaction is: where the camera sits relative to rest, and how
    /// far it is leaned over. The two come back together rather than from two calls because they
    /// are one weighted sum over one list - and because Evaluate is what retires spent obstacles,
    /// so a second pass over the list would find them already gone.
    public readonly struct Pose
    {
        public readonly Vector3 offset;   // metres from the resting position
        public readonly float roll;       // degrees about the view axis; + leans the camera left

        public Pose(Vector3 offset, float roll)
        {
            this.offset = offset;
            this.roll = roll;
        }
    }

    /// The whole tunable policy of one reaction. Everything that decides how a move FEELS is
    /// here; everything that decides WHEN it happens comes from the chart and the geometry.
    public struct Move
    {
        public Vector3 offset;        // where the camera sits at full commitment
        // How far the camera is leaned over at full commitment, in degrees about the view axis.
        // Signed to match the offset: a move to the left leans left. It rides the SAME envelope
        // as the offset rather than having timings of its own, because a lean that arrives on
        // its own schedule stops looking like the body doing one thing and starts looking like
        // two effects fighting.
        public float rollDegrees;
        public float leadBeats;       // how early it starts moving, before the obstacle arrives
        // Added to the time the obstacle physically takes to clear the camera. NEGATIVE is
        // meaningful and is what a hop needs: the apex of a jump is an instant, not a plateau,
        // but the only thing that knows how long a kerb takes to go past is its geometry - so
        // the way to say "come down before it has finished passing" is to trim that number.
        // The total is floored at zero.
        public float holdExtraBeats;
        public float recoverBeats;    // how long the return to centre takes
        // How long BEFORE the obstacle is level with the camera the move has to be finished.
        // This is what decides whether a dodge reads as a last-moment swerve or as stepping
        // aside well in advance, and it is the one number here that is not about speed.
        //
        // It is measured against contact, not against the hit zone. Those are different
        // moments - the hit zone is four metres in front of the camera, a third of a second
        // earlier at the shipping scroll speed - and anchoring to the hit zone is what made
        // even a fast dodge look early: the camera finished moving while the wall was still a
        // car's length away, then sat there waiting for it.
        public float commitMarginBeats;
        public ObstacleEase outEase;  // shape of the move away from rest
        public ObstacleEase inEase;   // shape of the return to it
    }

    private const int MoveCount = 4;

    private struct Pass
    {
        public ObstacleMove move;
        public float arrival;      // song time at which the obstacle's near edge reaches the hit zone
        public float reachSeconds; // from that moment until its near edge is level with the camera
        public float passSeconds;  // from that moment until its far end is behind the camera
    }

    private readonly Move[] policy = new Move[MoveCount];
    private readonly List<Pass> passes = new List<Pass>();

    private float secondsPerBeat = 0.5f;
    private Vector3 limit = new Vector3(2f, 2f, 0f);
    private float rollLimit = 30f;

    /// Beats come from the chart. A missing or nonsense bpm leaves the fallback in place rather
    /// than producing a beat of infinite length, the same rule HudBeatClock follows.
    public void SetTempo(float bpm, float fallbackBpm = 120f)
    {
        float resolved = (bpm >= 20f && bpm <= 400f) ? bpm : fallbackBpm;
        secondsPerBeat = 60f / Mathf.Clamp(resolved, 20f, 400f);
    }

    public void SetPolicy(ObstacleMove move, Move shape) => policy[(int)move] = shape;

    /// A ceiling on the summed offset, per axis, so no pile-up of overlapping obstacles can put
    /// the camera somewhere the scene was never checked at.
    public void SetLimit(Vector3 maxOffset) => limit = maxOffset;

    /// The same ceiling, for the lean. Overlapping dodges in the same direction would otherwise
    /// add into a roll the scene has never been looked at from.
    public void SetRollLimit(float maxDegrees) => rollLimit = Mathf.Abs(maxDegrees);

    public void Clear() => passes.Clear();

    public int Pending => passes.Count;

    /// Announces an obstacle. Called at SPAWN, which in beatmap mode is a full flight time before
    /// the obstacle is due — far more than any lead-in — so the reaction is always registered
    /// long before the envelope that needs it becomes non-zero.
    public void Register(ObstacleMove move, float arrivalTime, float reachSeconds, float passSeconds)
    {
        passes.Add(new Pass
        {
            move = move,
            arrival = arrivalTime,
            reachSeconds = Mathf.Max(0f, reachSeconds),
            passSeconds = Mathf.Max(0f, passSeconds),
        });
    }

    /// Song time at which the last registered obstacle has finished being reacted to. Random mode
    /// uses it to refuse an obstacle that would land before the camera has come home.
    public float BusyUntil
    {
        get
        {
            float end = float.NegativeInfinity;
            for (int i = 0; i < passes.Count; i++) end = Mathf.Max(end, WindowEnd(passes[i]));
            return end;
        }
    }

    /// Where the camera should be, relative to rest, at this point in the track.
    ///
    /// Spent passes are dropped here rather than in a separate tick, because this is the one
    /// method that is guaranteed to be called and it already knows the time. That does mean the
    /// caller must walk time forwards — which everything driven by a song clock does.
    public Pose Evaluate(float time)
    {
        Vector3 sum = Vector3.zero;
        float roll = 0f;

        for (int i = passes.Count - 1; i >= 0; i--)
        {
            Pass pass = passes[i];
            if (WindowEnd(pass) < time)
            {
                passes.RemoveAt(i);
                continue;
            }

            Move shape = policy[(int)pass.move];
            float anchor = AnchorOf(shape, pass);
            float weight = Envelope(time - anchor,
                                    Seconds(shape.leadBeats),
                                    HoldOf(shape, pass, anchor),
                                    Seconds(shape.recoverBeats),
                                    shape.outEase, shape.inEase);
            if (weight <= 0f) continue;

            sum += shape.offset * weight;
            roll += shape.rollDegrees * weight;
        }

        return new Pose(
            new Vector3(
                Mathf.Clamp(sum.x, -limit.x, limit.x),
                Mathf.Clamp(sum.y, -limit.y, limit.y),
                Mathf.Clamp(sum.z, -limit.z, limit.z)),
            Mathf.Clamp(roll, -rollLimit, rollLimit));
    }

    /// The same window Evaluate would give an obstacle, without booking it: when the camera is
    /// fully committed, when it starts back, and when it is home. For presentation that has to move
    /// WITH the camera (the trainer silhouette) and plans the whole chart in advance.
    public void Window(ObstacleMove move, float arrivalTime, float reachSeconds, float passSeconds,
                       out float commit, out float release, out float settled)
    {
        var pass = new Pass { move = move, arrival = arrivalTime, reachSeconds = Mathf.Max(0f, reachSeconds), passSeconds = Mathf.Max(0f, passSeconds) };
        Move shape = policy[(int)move];
        commit = AnchorOf(shape, pass);
        release = commit + HoldOf(shape, pass, commit);
        settled = release + Mathf.Max(0f, Seconds(shape.recoverBeats));
    }

    // Not clamped at zero: holdExtraBeats is allowed to be negative. lead and recover handle
    // their own non-positive cases inside Envelope.
    private float Seconds(float beats) => beats * secondsPerBeat;

    /// The moment the camera is fully committed - the top of the arc, the far side of the step.
    /// Placed a margin before the obstacle is level with the camera rather than at the chart's
    /// arrival time, so shortening the margin makes the whole reaction later without touching
    /// how fast it is.
    private float AnchorOf(Move shape, Pass pass) =>
        pass.arrival + pass.reachSeconds - Seconds(shape.commitMarginBeats);

    /// How long the camera stays committed. Measured to the moment the obstacle's far end is
    /// behind the camera, so a later anchor automatically shortens the hold instead of pushing
    /// the whole reaction past the thing it is reacting to.
    private float HoldOf(Move shape, Pass pass, float anchor) =>
        Mathf.Max(0f, pass.arrival + pass.passSeconds - anchor + Seconds(shape.holdExtraBeats));

    private float WindowEnd(Pass pass)
    {
        Move shape = policy[(int)pass.move];
        float anchor = AnchorOf(shape, pass);
        return anchor + HoldOf(shape, pass, anchor) + Mathf.Max(0f, Seconds(shape.recoverBeats));
    }

    /// The shape of one reaction, as a weight from 0 to 1, against time measured from the moment
    /// the obstacle reaches the hit zone.
    ///
    ///        1 |       .-------------.
    ///          |      /               \
    ///        0 |____ /                 \______
    ///            -lead  0          hold   +recover
    ///
    /// t is measured from the ANCHOR - the moment of full commitment, which AnchorOf places a
    /// short margin before the obstacle is level with the camera. Everything to the left of 0 is
    /// the camera getting out of the way; everything to the right is it holding and coming back.
    ///
    /// The two flanks take their own easing (see ObstacleEase), which is what lets one function
    /// serve a sidestep and a jump: with Decelerate out and Accelerate back the plot above is a
    /// ballistic arc, and the flat top is only as long as the caller asked for - for a hop, next
    /// to nothing, because the hang belongs to the curve rather than to the hold.
    private static float Envelope(float t, float lead, float hold, float recover,
                                  ObstacleEase outEase, ObstacleEase inEase)
    {
        if (t < 0f)
        {
            if (lead <= 0f || t <= -lead) return 0f;
            return Ease(outEase, (t + lead) / lead);
        }

        if (t <= hold) return 1f;
        if (recover <= 0f) return 0f;

        float u = (t - hold) / recover;
        return u >= 1f ? 0f : 1f - Ease(inEase, u);
    }

    /// Maps 0..1 to 0..1. All three end where they start and finish; they differ only in where
    /// the speed is, which is the whole point.
    private static float Ease(ObstacleEase ease, float u)
    {
        u = Mathf.Clamp01(u);
        switch (ease)
        {
            // Peak speed 2x the average, at the very start, falling to zero at the end.
            case ObstacleEase.Decelerate: return 1f - (1f - u) * (1f - u);
            // The mirror: zero at the start, peak 2x the average as it arrives.
            case ObstacleEase.Accelerate: return u * u;
            // Peak speed 1.875x the average, in the middle; speed AND acceleration zero at both ends.
            case ObstacleEase.Glide: return u * u * u * (u * (u * 6f - 15f) + 10f);
            // Peak speed 1.5x the average, in the middle, zero at both ends.
            default: return u * u * (3f - 2f * u);
        }
    }
}
