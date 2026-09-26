using System;
using System.Collections.Generic;
using UnityEngine;

/// One clip the trainer can play, with the moment inside it that has to land on the note.
[Serializable]
public class SilhouetteMotion
{
    public AnimationClip clip;
    // Seconds into the clip of the frame that must coincide with the note: the fist at full reach,
    // the foot at its highest, the top of a jump, the far side of a step, the bottom of a crouch.
    // Found by Tunnel/Silhouette/Build Motion Set (SilhouetteClipAnalysis); 0 for a loop.
    public float impact;
    // Which hand/foot or direction does the work - "L hand", "R leg", "-X". Used to alternate
    // sides so both hands and both legs get work; informational otherwise.
    public string side;
    // For a take that ends at its impact (Jumping Up stops at the top of the jump): after the
    // impact the clip plays BACKWARDS, so the landing is the take-off in reverse instead of the
    // figure floating down through a crossfade.
    public bool reverseAfterImpact;

    public bool Valid => clip != null;
    public float Length => clip != null ? clip.length : 0f;
}

/// Everything that makes the trainer silhouette WHO it is and HOW it moves: the rigged model and a
/// list of clips per kind of note. SilhouetteTrainer reads nothing else about the character, so a
/// different figure is a different asset of this type - no code.
///
/// Every list can hold any number of variations; the trainer cycles through them without playing
/// the same one twice in a row, in an order fixed by the track (see SilhouettePlanner). A list left
/// empty makes the trainer fall back to the idle for that kind of note.
[CreateAssetMenu(menuName = "Tunnel/Silhouette Motion Set", fileName = "SilhouetteMotionSet")]
public class SilhouetteMotionSet : ScriptableObject
{
    [Header("Character")]
    // A model with a Humanoid Animator (its Avatar comes with it). Instantiated once, on the
    // trainer's own layer, far from the tunnel.
    public GameObject model;
    // Optional override; the model's own Animator avatar is used when this is empty.
    public Avatar avatar;

    [Header("Notes")]
    public List<SilhouetteMotion> punch = new List<SilhouetteMotion>();
    public List<SilhouetteMotion> kick = new List<SilhouetteMotion>();
    // Loops for the sustained notes, played for the whole length of the hold.
    public List<SilhouetteMotion> punchHold = new List<SilhouetteMotion>();
    public List<SilhouetteMotion> kickHold = new List<SilhouetteMotion>();

    [Header("Obstacles")]
    public List<SilhouetteMotion> curb = new List<SilhouetteMotion>();       // jump over the kerb
    public List<SilhouetteMotion> dodgeLeft = new List<SilhouetteMotion>();
    public List<SilhouetteMotion> dodgeRight = new List<SilhouetteMotion>();
    public SilhouetteMotion duckDown = new SilhouetteMotion();   // impact = the bottom of the crouch
    public SilhouetteMotion duckUp = new SilhouetteMotion();     // impact = standing again

    [Header("Between the notes")]
    // The fighting stance between notes and through short rests.
    public List<SilhouetteMotion> idle = new List<SilhouetteMotion>();
    // Intro before the first note, breakdowns with no notes, the finale after the last one.
    public List<SilhouetteMotion> dance = new List<SilhouetteMotion>();

    [Header("Timing")]
    // How much a clip may be sped up to fit its wind-up between two notes. Beyond this the start
    // of the wind-up is cut instead, so nothing ever turns into a blur.
    [Range(1f, 3f)] public float maxSpeedUp = 1.7f;
    // Crossfade between clips, in beats (capped by the space the notes leave).
    [Range(0.05f, 1f)] public float crossfadeBeats = 0.35f;
    // A gap between notes longer than this, in bars, with nothing but walls in it, is a breakdown
    // and gets a dance. Shorter gaps get the idle.
    [Min(0.5f)] public float breakdownBars = 3f;
    // How far the figure may drift from the centre of its panel, in metres, before it is eased
    // back - the long side steps travel ~0.6 m and some dances wander.
    [Min(0.1f)] public float maxDrift = 0.6f;
}
