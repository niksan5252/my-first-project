using UnityEngine;
using UnityEngine.Animations;

/// Poses the trainer on top of whatever clip is playing: moves and leans the body, sets the feet
/// where they should stand and the hands where they should reach, then lets the humanoid IK solve
/// the limbs. Runs as the last playable before the Animator, fed fresh data every frame by
/// SilhouetteTrainer - which computes it all from song time, so the pose stays a pure function of
/// the song clock.
///
/// The feet are pinned: each foot goal is taken from the clip's own pose BEFORE the body is moved
/// and moved by its own offset (a step), so shifting or sinking the body bends the knees instead
/// of sliding the feet. Offsets work where absolute targets would not: the goal is wherever the
/// avatar keeps its foot, which need not be the foot bone. Positions are world space, like the
/// stream's own goals.
public struct SilhouettePoseJob : IAnimationJob
{
    public Vector3 bodyOffset;
    public float leanDegrees;     // roll about the figure's forward axis
    public float turnDegrees;     // yaw about the vertical: shoulders and hips turned together
    public Vector3 forward;       // the figure's forward, world space

    public float lHandW, rHandW;
    public Vector3 lHand, rHand, lElbow, rElbow;
    public Vector3 lFootOffset, rFootOffset;
    public float pin;             // 0 while a clip (jump, crouch) owns the legs, 1 otherwise
    // Share of the take's own turn away from the viewer that is taken out: a boxing take stands
    // side-on, which from the front is a profile nobody can read. What the shoulders do against
    // the hips - the turn into a punch - is left as it is.
    public float faceFront;

    public void ProcessRootMotion(AnimationStream stream) { }

    public void ProcessAnimation(AnimationStream stream)
    {
        if (!stream.isHumanStream) return;
        var h = stream.AsHuman();

        Vector3 lf = h.GetGoalPosition(AvatarIKGoal.LeftFoot), rf = h.GetGoalPosition(AvatarIKGoal.RightFoot);
        Quaternion lfr = h.GetGoalRotation(AvatarIKGoal.LeftFoot), rfr = h.GetGoalRotation(AvatarIKGoal.RightFoot);
        Vector3 lh = h.GetGoalPosition(AvatarIKGoal.LeftHand), rh = h.GetGoalPosition(AvatarIKGoal.RightHand);

        if (faceFront > 0f)
        {
            Vector3 f = h.bodyRotation * Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 1e-4f)
            {
                // The goals turn with the body, about the hips.
                var q = Quaternion.AngleAxis(-Vector3.SignedAngle(forward, f, Vector3.up) * faceFront, Vector3.up);
                Vector3 c = h.bodyPosition;
                h.bodyRotation = q * h.bodyRotation;
                lf = c + q * (lf - c); rf = c + q * (rf - c);
                lfr = q * lfr; rfr = q * rfr;
                lh = c + q * (lh - c); rh = c + q * (rh - c);
            }
        }

        h.bodyPosition += bodyOffset;
        if (leanDegrees != 0f || turnDegrees != 0f)
            h.bodyRotation = Quaternion.AngleAxis(leanDegrees, forward) * Quaternion.AngleAxis(turnDegrees, Vector3.up) * h.bodyRotation;

        // A stepping foot is always driven, even while a clip owns the legs.
        Foot(ref h, AvatarIKGoal.LeftFoot, lf + lFootOffset, lfr, lFootOffset.sqrMagnitude > 1e-8f ? 1f : pin);
        Foot(ref h, AvatarIKGoal.RightFoot, rf + rFootOffset, rfr, rFootOffset.sqrMagnitude > 1e-8f ? 1f : pin);
        Hand(ref h, AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, lh + bodyOffset, lHand, lElbow, lHandW);
        Hand(ref h, AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, rh + bodyOffset, rHand, rElbow, rHandW);
        h.SolveIK();
    }

    private static void Foot(ref AnimationHumanStream h, AvatarIKGoal goal, Vector3 pos, Quaternion rot, float w)
    {
        h.SetGoalPosition(goal, pos);
        h.SetGoalRotation(goal, rot);
        h.SetGoalWeightPosition(goal, w);
        h.SetGoalWeightRotation(goal, w);
    }

    // With no gesture the arm simply rides along with the body (the clip's own arm, carried by
    // the moved body); a gesture blends the hand from there to its target.
    private static void Hand(ref AnimationHumanStream h, AvatarIKGoal goal, AvatarIKHint hint, Vector3 rest, Vector3 target, Vector3 elbow, float w)
    {
        if (w <= 0f)
        {
            h.SetGoalWeightPosition(goal, 0f);
            h.SetGoalWeightRotation(goal, 0f);
            h.SetHintWeightPosition(hint, 0f);
            return;
        }
        h.SetGoalPosition(goal, Vector3.Lerp(rest, target, w));
        h.SetGoalWeightPosition(goal, 1f);
        h.SetGoalWeightRotation(goal, 0f);
        h.SetHintPosition(hint, elbow);
        h.SetHintWeightPosition(hint, w);
    }
}
