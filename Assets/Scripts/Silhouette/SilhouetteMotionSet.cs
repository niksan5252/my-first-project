using System;
using System.Collections.Generic;
using UnityEngine;

/// One clip the trainer can play, with the moment inside it that has to land on the note.
[Serializable]
public class SilhouetteMotion
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

    public bool Valid => clip != null;
    public float Length => clip != null ? clip.length : 0f;
}

/// Everything that makes the trainer silhouette WHO it is and HOW it moves. SilhouetteTrainer reads
/// nothing else about the character, so a different figure is a different asset of this type.
///
/// The trainer is a warm-up instructor, not a fighter: it stands relaxed and bounces on the beat,
/// and shows each note as a calm gesture - a foot set down on the tile, a hand reaching to the
/// cube, a step and a lean away from a wall. Those gestures are posed procedurally (IK, see
/// SilhouettePoseJob) from the distances below, measured from the figure's own shoulders and feet,
/// so they fit any humanoid. Clips are used only where a take reads better than a pose: the
/// relaxed stance itself, the jump over a kerb and the crouch under a barrier.
[CreateAssetMenu(menuName = "Tunnel/Silhouette Motion Set", fileName = "SilhouetteMotionSet")]
public class SilhouetteMotionSet : ScriptableObject
{
    [Header("Character")]
    // A model with a Humanoid Animator (its Avatar comes with it).
    public GameObject model;
    // Optional override; the model's own Animator avatar is used when this is empty.
    public Avatar avatar;

    // How much of the panel's height the character's own height takes, as the trainer's
    // frameHeights (view height = height x this). 0 keeps the trainer's. A cartoon figure with a big
    // head and short limbs needs less room above it than a realistic one.
    [Min(0f)] public float frameHeights = 0f;
    [Min(0f)] public float frameCenter = 0f;
    // Every distance below is in metres for a figure of about 1.75 m. A character of another size
    // scales them all by this - reaches, steps, the dodge's shift and the hop's height - so the
    // moves keep their proportions on it (the 3.2 m cartoon skeleton uses ~1.7).
    [Min(0.1f)] public float gestureScale = 1f;

    [Header("Clips")]
    // The stance under everything, as motion capture: calm idles for the short rests between
    // notes (breathing, a weight shift), livelier ones for the intro, the breakdowns and the
    // finale. They alternate with crossfades, never the same one twice running, in an order fixed
    // by the track. A still pose (holdFrame) also works, the bounce then keeps it alive.
    public List<SilhouetteMotion> idle = new List<SilhouetteMotion>();
    public List<SilhouetteMotion> idleLively = new List<SilhouetteMotion>();
    // A gap between notes of at least this many bars counts as a breakdown and gets the lively
    // idles, like the intro and the finale.
    [Min(0.5f)] public float livelyGapBars = 3f;
    // A stance plays at least this long (and at least one full cycle) before the next takes over.
    [Min(1f)] public float stanceMinSeconds = 6f;
    // Crossfade from one stance to the next, in beats.
    [Range(0.1f, 4f)] public float stanceFadeBeats = 1.5f;
    public List<SilhouetteMotion> curb = new List<SilhouetteMotion>();       // jump over the kerb
    public SilhouetteMotion duckDown = new SilhouetteMotion();   // impact = the bottom of the crouch
    public SilhouetteMotion duckUp = new SilhouetteMotion();     // impact = standing again

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

    [Header("Step aside: away from a wall, onto a kerb's free lane")]
    // The whole figure steps over with the camera and stays there while the wall or the kerb goes
    // past - kicks and punches in the meantime are thrown from there - then steps back. The lead
    // foot goes first and the other is brought up to it (a side step); the body leans into the
    // move only while it travels.
    public float stepAsideShift = 0.36f;           // how far the figure moves (m)
    [Range(0.3f, 1f)] public float stepLeadShare = 0.65f;   // share of the move the lead foot takes
    [Range(0f, 0.3f)] public float stepFootLift = 0.07f;    // a foot in the air clears the floor by this
    public float stepLeanPerSpeed = 9f;            // degrees of lean per m/s of travel
    public float dodgeTurnDegrees = 12f;           // shoulders turned toward the step while stepped out

    [Header("Sweep: the raised fist carried out along a PunchSweep's tail")]
    // Metres from the shoulder where the hand is at the note (high, near the middle) and where it
    // has arrived when the note ends (out to the side, a little lower).
    public Vector3 sweepFrom = new Vector3(0.12f, 0.5f, 0.12f);
    public Vector3 sweepTo = new Vector3(0.6f, 0.32f, 0.08f);

    [Header("Chord: a hop that lands on both feet on the note")]
    // The body rises and falls over chordAirBeats, touching down exactly on the note, then gives
    // at the knees by chordDip and comes back up over chordLandBeats.
    public float chordJumpHeight = 0.2f;
    [Range(0.2f, 1.5f)] public float chordAirBeats = 0.6f;
    [Range(0.2f, 1.5f)] public float chordLandBeats = 0.5f;
    public float chordDip = 0.07f;
    // How far each foot lands out from where it stands - a slightly wider stance on the landing.
    public float chordFeetOut = 0.08f;
    // Each lane between the two feet widens the landing by this (a jack lands on the outer lanes),
    // and the jump carries the body this far toward the side of the pair (side jumps, the shift).
    public float chordFeetPerLane = 0.13f;
    public float chordShift = 0.3f;
    // On the widest landing (the outer lanes) the hands meet over the head: a jumping jack's clap.
    public bool jackClap = true;

    [Header("Variety set: overhead punch, the star, balance")]
    public float punchHighRaise = 0.62f;
    // The star's arms, from the shoulder: out and up.
    public Vector3 starArm = new Vector3(0.45f, 0.42f, 0.05f);
    // The star's and the wide stance's feet, out from where they stand.
    public float straddleOut = 0.3f;
    // Standing on one leg: how high the other foot is lifted, and how far forward.
    public float oneLegLift = 0.34f;
    public float oneLegForward = 0.14f;

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
