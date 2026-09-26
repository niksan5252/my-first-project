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

    [Header("Clips")]
    // The relaxed stance under everything: intro, rests, finale and every gesture. The first is
    // used; a list so a different character can bring its own. A still pose (holdFrame) is fine:
    // the beat bounce and the sway are what keep it alive.
    public List<SilhouetteMotion> idle = new List<SilhouetteMotion>();
    public List<SilhouetteMotion> curb = new List<SilhouetteMotion>();       // jump over the kerb
    public SilhouetteMotion duckDown = new SilhouetteMotion();   // impact = the bottom of the crouch
    public SilhouetteMotion duckUp = new SilhouetteMotion();     // impact = standing again

    [Header("Beat bounce (the stance's own movement)")]
    // Knees give on every beat, lowest ON the beat; the body sways side to side over two beats.
    [Range(0f, 0.1f)] public float bounceDepth = 0.035f;
    [Range(0f, 0.1f)] public float swayWidth = 0.025f;

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

    [Header("Kick: a foot is set down on the tile")]
    // Metres from where the foot stands: out to the side for an outer lane (a wide stance), a
    // little for an inner one (almost under the body), forward, and how high it is lifted between.
    public float kickOutOuter = 0.42f;
    public float kickOutInner = 0.2f;
    public float kickForward = 0.12f;
    public float kickLift = 0.12f;
    // How much of the step the body follows, and how far it sinks into a wide stance.
    [Range(0f, 1f)] public float kickBodyFollow = 0.3f;
    public float kickWideSink = 0.05f;

    [Header("Dodge: step and lean away from the wall, arm out")]
    public float dodgeShift = 0.2f;        // how far the body moves (m)
    public float dodgeLeadFoot = 0.34f;    // the foot on the side of the step
    public float dodgeTrailFoot = 0.1f;
    public float dodgeLeanDegrees = 12f;
    public float dodgeArmReach = 0.46f;    // hand out from the shoulder, at shoulder height

    [Header("Timing")]
    // A gesture reaches its note in at most this many beats (and at most riseSeconds), and lets go
    // over fallBeats. Kicks stay planted for plantBeats before the foot comes back.
    [Range(0.1f, 1f)] public float riseBeats = 0.6f;
    [Range(0.1f, 1f)] public float riseSeconds = 0.3f;
    [Range(0.1f, 2f)] public float fallBeats = 0.7f;
    [Range(0f, 1f)] public float plantBeats = 0.25f;
    // Clips: how much a take may be sped up to fit, and the crossfade into it, in beats.
    [Range(1f, 3f)] public float maxSpeedUp = 1.7f;
    [Range(0.05f, 1f)] public float crossfadeBeats = 0.35f;
    // How far the figure may drift from the centre of its panel before it is eased back (m).
    [Min(0.1f)] public float maxDrift = 0.6f;
}
