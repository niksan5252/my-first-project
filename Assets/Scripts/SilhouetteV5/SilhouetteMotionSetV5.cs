using System;
using System.Collections.Generic;
using UnityEngine;

/// One clip the trainer can play, with the moment inside it that has to land on the note.
[Serializable]
public class SilhouetteMotionV5
{
    public AnimationClip clip;
    // Seconds into the clip of the frame that must coincide with the note: the top of a jump,
    // the bottom of a crouch. Found by Tunnel/Silhouette/Build Motion Set (SilhouetteClipAnalysis);
    // 0 for a loop.
    public float impact;
    public string side;
    // For a take that ends at its impact (Jumping Up stops at the top of the jump): after the
    // impact the clip plays BACKWARDS, so the landing is the take-off in reverse instead of the
    // figure floating down through a crossfade.
    public bool reverseAfterImpact;
    // 0 or more: the clip is not played but held as a still pose at this time (the relaxed stance
    // is the LAST frame of Fight Idle To Standing Idle). Negative: played normally.
    public float holdFrame = -1f;
    // Degrees to turn the body about the vertical while this take plays: some Mixamo takes are
    // filmed facing away from the camera (Standing Jump ~55 deg); this faces them to the viewer.
    public float yawFix;

    public bool Valid => clip != null;
    public float Length => clip != null ? clip.length : 0f;
}

/// Everything that makes the trainer silhouette WHO it is and HOW it moves. SilhouetteTrainerV5 reads
/// nothing else about the character, so a different figure is a different asset of this type.
///
/// The trainer is a warm-up instructor, not a fighter: it stands relaxed and bounces on the beat,
/// and shows each note as a calm gesture - a foot set down on the tile, a hand reaching to the
/// cube, a step and a lean away from a wall. Those gestures are posed procedurally (IK, see
/// SilhouettePoseJobV5) from the distances below, measured from the figure's own shoulders and feet,
/// so they fit any humanoid. Clips are used only where a take reads better than a pose: the
/// relaxed stance itself, the jump over a kerb and the crouch under a barrier.
[CreateAssetMenu(menuName = "Tunnel/Silhouette Motion Set", fileName = "SilhouetteMotionSetV5")]
public class SilhouetteMotionSetV5 : ScriptableObject
{
    [Header("Character")]
    // A model with a Humanoid Animator (its Avatar comes with it).
    public GameObject model;
    // Optional override; the model's own Animator avatar is used when this is empty.
    public Avatar avatar;

    [Header("Clips")]
    // The stance under everything, as motion capture: calm idles for the short rests between
    // notes (breathing, a weight shift), livelier ones for the intro, the breakdowns and the
    // finale. They alternate with crossfades, never the same one twice running, in an order fixed
    // by the track. A still pose (holdFrame) also works, the bounce then keeps it alive.
    public List<SilhouetteMotionV5> idle = new List<SilhouetteMotionV5>();
    public List<SilhouetteMotionV5> idleLively = new List<SilhouetteMotionV5>();
    // A gap between notes of at least this many bars counts as a breakdown and gets the lively
    // idles, like the intro and the finale.
    [Min(0.5f)] public float livelyGapBars = 3f;
    // A stance plays at least this long (and at least one full cycle) before the next takes over.
    [Min(1f)] public float stanceMinSeconds = 6f;
    // Crossfade from one stance to the next, in beats.
    [Range(0.1f, 4f)] public float stanceFadeBeats = 1.5f;
    public List<SilhouetteMotionV5> curb = new List<SilhouetteMotionV5>();       // jump over the kerb
    public SilhouetteMotionV5 duckDown = new SilhouetteMotionV5();   // impact = the bottom of the crouch
    public SilhouetteMotionV5 duckUp = new SilhouetteMotionV5();     // impact = standing again
    // Video 12: a Kick shown as a real step instead of the procedural foot tap - a take (our march,
    // mocap from the Gemini clip) whose impact is the foot coming DOWN on the note. Screen sides: the
    // figure faces the viewer, so its left foot is on the screen's right. Empty = the procedural tap.
    public SilhouetteMotionV5 stepScreenRight = new SilhouetteMotionV5();
    public SilhouetteMotionV5 stepScreenLeft = new SilhouetteMotionV5();
    [Min(0.1f)] public float stepWindUpMax = 0.55f;   // seconds of the take before the landing, at most
    [Min(0.05f)] public float stepStay = 0.25f;       // seconds after the landing before the stance takes over
    [Min(0.05f)] public float stepBlend = 0.4f;       // seconds of crossfade into a step and back to the stance

    [Header("Two-foot jumps (video 13)")]
    // Two Kicks together on the inner lanes: a jump forward (impact = the landing).
    public SilhouetteMotionV5 jumpForward = new SilhouetteMotionV5();
    // Two Kicks together on one side (0+1 / 2+3): a hop landing on that side of the screen.
    public SilhouetteMotionV5 jumpSideScreenRight = new SilhouetteMotionV5();
    public SilhouetteMotionV5 jumpSideScreenLeft = new SilhouetteMotionV5();
    // A side hop lets the stance back this many seconds after its landing, at most.
    [Min(0.1f)] public float jumpSideTail = 0.35f;
    // Jumping jacks (outer pair 0+3 / inner pair 1+2 in turn): posed, the body hopping this high
    // between the landings, the feet wide (wideOut) or together.
    [Min(0f)] public float jackHop = 0.12f;

    [Header("Beat bounce (a light layer over the mocap)")]
    // Knees give a little on every beat, lowest ON the beat - just enough to feel the beat on top
    // of the motion capture, which is what keeps the body alive.
    [Range(0f, 0.1f)] public float bounceDepth = 0.009f;

    [Header("Punch: a hand reaches to the cube")]
    // Metres from the shoulder: out to the cube's side (outer lane / inner lane), forward, and
    // down (chest height). A sustained punch sits high, so its hand goes up instead.
    // Seen from the front a reach straight ahead is lost, so the hand goes out to the side and up
    // to shoulder height, elbow soft - the way the reference instructor shows it.
    public float punchOutOuter = 0.42f;
    public float punchOutInner = 0.3f;
    public float punchForward = 0.12f;
    public float punchDrop = -0.04f;
    public float punchHoldRaise = 0.48f;
    public float punchHoldPulse = 0.04f;

    [Header("CurbSplit / SquatHold: feet very wide")]
    public float wideOut = 0.42f;          // metres each foot goes out (CurbSplit)
    public float wideSink = 0.12f;
    public float wideOutSquat = 0.45f;     // SquatHold: a sumo squat
    public float wideSinkSquat = 0.32f;

    [Header("CurbGap: standing on one leg")]
    public float oneLegLift = 0.42f;      // metres the free foot is raised (knee up)
    public float oneLegForward = 0.18f;
    public float oneLegShift = 0.06f;     // the body over the standing leg

    [Header("Kick: a foot taps the tile")]
    // One foot taps and comes back while the other stays planted - the next note taps with the
    // other foot. Metres from where the tapping foot stands: out to the side for an outer lane,
    // a little for an inner one (almost under the body), forward, and how high it is lifted.
    public float kickOutOuter = 0.42f;
    public float kickOutInner = 0.2f;
    public float kickForward = 0.12f;
    public float kickLift = 0.12f;
    // The body barely moves: an outer tap shifts the weight a little toward it, an inner one not
    // at all, and there is no sink - anything more reads as a hop from foot to foot.
    [Range(0f, 1f)] public float kickBodyFollowOuter = 0.1f;
    // The tap's own timing, in beats: down onto the tile by the note, a moment there, and back
    // under the body before the next note (a note every beat is the common case).
    [Range(0.1f, 1f)] public float tapInBeats = 0.45f;
    [Range(0f, 0.5f)] public float tapStayBeats = 0.1f;
    [Range(0.1f, 1f)] public float tapOutBeats = 0.4f;

    [Header("Dodge: turn, lean and step away from the wall")]
    public float dodgeShift = 0.2f;        // how far the body moves (m)
    public float dodgeLeadFoot = 0.34f;    // the foot on the side of the step
    public float dodgeTrailFoot = 0.1f;
    public float dodgeLeanDegrees = 12f;
    public float dodgeTurnDegrees = 30f;   // shoulders and hips turned toward the step

    [Header("Timing")]
    // A gesture reaches its note in at most this many beats (and at most riseSeconds), and lets go
    // over fallBeats. (Kicks have their own tap timing above.)
    [Range(0.1f, 1f)] public float riseBeats = 0.6f;
    [Range(0.1f, 1f)] public float riseSeconds = 0.3f;
    [Range(0.1f, 2f)] public float fallBeats = 0.7f;
    // Clips: how much a take may be sped up to fit, and the crossfade into it, in beats.
    [Range(1f, 3f)] public float maxSpeedUp = 1.7f;
    [Range(0.05f, 1f)] public float crossfadeBeats = 0.35f;
    // How far the figure may drift from the centre of its panel before it is eased back (m).
    [Min(0.1f)] public float maxDrift = 0.6f;
}
