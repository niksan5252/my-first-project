using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Hold variants, then obstacles, are appended, never inserted: the ordinals of Kick/Punch/Jump
// are what any already-serialized field holds, so a new value in the middle would silently
// re-type them.
//
// `Jump` is RETIRED but still here, and both halves of that matter. It no longer spawns - the
// hurdle bar on two posts it used to draw has been replaced by Curb, which spans the whole road
// and asks for one honest jump instead of a hop to one side - so it is absent from CueTypes and
// has no pool. It stays in the enum because deleting it would shift every ordinal after it, and
// because BeatmapReader still has to recognise the name: charts written before the kerb existed
// are full of it, and the reader turns those notes into Curb rather than refusing the file.
//
// The last three are OBSTACLES rather than notes. A note is something to strike at the hit zone
// and it ends there; an obstacle is a piece of the road that keeps coming and has to be gone
// AROUND, OVER or UNDER, and the camera itself is what performs that movement. They share the
// pool, the clock and the spawn path with the notes, and differ only in what happens when they
// reach the hit zone (see OnObstaclePassed) and in that they drive ObstacleCameraDirector.
public enum CueType { Kick, Punch, Jump, PunchHold, KickHold, Dodge, Curb, Duck }

/// Spawns pooled cue markers across the tunnel's lanes at a height matching their type
/// (Kick = floor, Punch = chest, Jump = a low hurdle bar), colors each cue instance
/// magenta or cyan (~50/50). At the hit zone the cue bursts into a small particle effect, its lane's floor pad flashes, and the
/// camera gets a brief shake.
public class CueSpawner : MonoBehaviour
{
    private const float MaxEmissionIntensity = 2.5f; // same hard ceiling as the tunnel rings

    [SerializeField] private NeonTunnelGenerator tunnel;
    [SerializeField] private Transform cameraTransform;

    // Random keeps the original behaviour — a note every spawnInterval, type and lane rolled on
    // the spot — and stays the default so the scene is playable with no track attached. Beatmap
    // reads a generated chart and places every note against the song clock instead.
    public enum CueSpawnMode { Random, Beatmap }

    [Header("Spawn Source")]
    [SerializeField] private CueSpawnMode spawnMode = CueSpawnMode.Random;
    [SerializeField] private string beatmapFileName = "test.json"; // under StreamingAssets/Beatmaps
    [SerializeField] private AudioSource musicSource;
    // Positive pushes the whole chart later against the audio. This is the knob for the latency
    // of the player's own output path, which no generated file can know about. Unity's own DSP
    // buffering is already taken off by SongClock; this is only what lies beyond it - the OS and
    // the device. Measured on the development machine (WASAPI loopback, MAG 275QF over HDMI) at
    // 30 ms; the monitor's own delay after the loopback point is not in that number.
    [SerializeField] private float beatmapUserOffset = 0f;
    // The intro: the music plays from the first frame, and for this many bars no note spawns - the
    // tunnel just runs to the track. In bars of the chart's own tempo, so the intro ends on a bar
    // line whatever the song. The first note then arrives no earlier than intro + FlightTime,
    // because a note is born one flight before it is due. See IntroCutoff for the exact rule.
    [SerializeField, Min(0f)] private float introBars = 4f;
    // When the intro ends in the middle of a workout set, skip the rest of that set too, so the
    // first block the player sees is a whole one. Off keeps every note after the cutoff.
    [SerializeField] private bool introSkipsPartialSet = true;
    // Dodge walls the generator put into the intro (--fill-gaps-dodge) are the one thing allowed
    // on the road before `introBars` ends: they start spawning this many bars after the music.
    // See IntroWallFloor.
    [SerializeField, Min(0f)] private float introWallBars = 1f;
    // How weak the feedback gets for a note the generator marked as quiet. Randomly spawned notes
    // always report full intensity, so this only ever affects chart playback.
    [SerializeField, Range(0f, 1f)] private float intensityFeedbackFloor = 0.55f;
    // Timing self-check. When greater than 0, the first N chart notes record the song time at
    // which they actually registered against the time the beatmap asked for, and the table is
    // written to the console once. Off by default: it allocates and logs, and neither belongs in
    // a normal run. This is a measurement, not a correction — see RecordTimingSample.
    [SerializeField] private int timingReportNotes = 0;

    [Header("Timing")]
    [SerializeField] private float spawnInterval = 0.9f;
    [SerializeField, Range(0.1f, 1f)] private float spawnZRatio = 0.6f; // fraction of tunnel length where cues first appear
    [SerializeField] private float hitZoneZ = 4f;
    // Where a PUNCH resolves, instead of hitZoneZ. A punch is thrown out in front of you and a
    // kick lands at your feet, so they do not belong at the same distance - and at one shared
    // distance a chest-height plate is right on top of the lens while the floor pad it shares
    // the moment with is a postage stamp. Moving only the punch out fixes both at once.
    //
    // It changes WHERE a note is when it resolves, never WHEN: the note is still solved from its
    // own time against the clock, so nothing about the timing moves with this. FlightTime stays
    // measured from the nearer hitZoneZ, which is the LONGEST flight of the two and therefore
    // still enough lead-in for everything.
    [SerializeField] private float punchHitZoneZ = 6f;
    [SerializeField] private float recycleZ = 0f;

    [Header("Cue Heights")]
    [SerializeField] private float kickHeight = 0.05f;
    // Punches sit ABOVE the camera's eye line, not level with it. At the same height as the
    // camera a punch plate lands dead centre of frame, overlapping the road and the vanishing
    // point; lifted, it reads as something thrown at out in front of you and leaves the road
    // clear underneath.
    [SerializeField] private float punchHeight = 2.05f;
    // A sustained punch rides well ABOVE the one-shot punch — head/raised-arm height rather than
    // chest height. Its own field rather than a multiple of punchHeight, so the two can be tuned
    // against each other freely: the gap between them is the read, and deriving one from the
    // other would mean every nudge to the one-shot height silently dragged the hold along too.
    //
    // 2.7 is not an aesthetic pick, it is the ceiling. The tunnel arch is an octagon, so it is
    // far lower over the OUTER lanes than over the middle: measured against the actual ring
    // geometry the opening is 3.68 high at x = +-2.25 but 5.18 at x = +-0.75, which caps a note
    // of this cross-section at 2.84 in the outer lanes versus 4.34 in the inner ones. Since every
    // lane has to read at the same height — height is how the player tells a hold from a one-shot
    // — the outer lanes govern, and anything above ~2.84 starts cutting into the arch's diagonal
    // beams. Raising this further means shrinking holdBeamPlateScale or lifting the arch itself.
    // Just above the one-shot punches, not up in the roof. It was 4.2 back when a hold had to
    // clear a chest-height plate that slid past the camera; now that a hold STOPS at the hit
    // point there is nothing to fly over, and at 4.2 it sat in the top of the arch where the
    // player is not looking.
    [SerializeField] private float punchHoldHeight = 2.45f;

    [Header("Visual Sizing (larger = more prominent)")]
    [SerializeField, Range(0.1f, 1f)] private float cueLaneWidthRatio = 0.8f;
    [SerializeField] private float kickDepth = 1.5f;
    [SerializeField] private float kickThickness = 0.34f;
    // How much of each cube's edge is cut flat by RoundedCubeMeshBuilder — a fraction of the
    // cube's own smallest dimension, not a fixed world size, so Punch (roughly cubic) and Kick
    // (a thin wide slab) both get a proportionally similar bevel instead of Kick's thin dimension
    // getting swallowed by a bevel sized for Punch.
    [SerializeField, Range(0.05f, 0.35f)] private float cueCornerChamferRatio = 0.22f;

    // Punch and Kick are both a plain square-cornered sign-plate sat inside a darker border,
    // and nothing else — no tails, no chevrons, no tapering. Punch stands upright (plate faces
    // -Z, toward the camera); Kick lies flat on the floor (plate faces +Y, so a camera looking
    // down the lane still reads its icon). What tells them apart is height and icon, not shape.
    // Length is reserved exclusively for the sustained hold variants, so that a long shape on
    // screen always means "hold this" and never decorates a one-shot note.
    [Header("Cue Plate Shape")]
    // Sized against the FRAME rather than in the abstract: at the shipping camera these put a
    // plate at about a sixth of the frame's height, which is where a punch cue stops crowding
    // the lens and starts reading as a target out ahead.
    [SerializeField] private float punchPlateWidth = 1.25f;
    [SerializeField] private float punchPlateHeight = 1.15f;
    [SerializeField] private float plateThickness = 0.26f;
    [SerializeField] private float plateBorder = 0.13f;   // width of the darker frame around the plate
    [SerializeField] private float frameThickness = 0.2f;

    // A hold is a sustained note, so its length is authored in floor grid cells rather than in
    // metres: the player reads the length off the squares rolling underneath, and tying it to
    // the tunnel's own pitch (NeonTunnelGenerator.FloorGridCellWorldSize) means the two can
    // never drift apart the way two independent constants would.
    [Header("Hold Cues")]
    // Each hold instance draws its own length from this range, so a run of holds does not read
    // as one repeated stamp. The pool is built at holdMaxCells and a spawn simply hides the
    // cells it does not need, which is why the length can vary per instance without rebuilding
    // a single mesh at runtime.
    [SerializeField, Range(1, 12)] private int holdMinCells = 2;
    [SerializeField, Range(1, 12)] private int holdMaxCells = 6;
    // Feedback while the note is live: the whole strip lifts to this multiple of its normal
    // brightness the instant it is struck, so "I am holding this" is legible along the entire
    // length rather than only at the point the hit zone happens to be.
    [SerializeField, Range(1f, 4f)] private float holdHeldBoost = 1.9f;
    // Width of the fade wipe as a fraction of the note's length. Too tight and the boundary is a
    // hard edge racing down the strip; too loose and a short note is half-faded before it starts.
    [SerializeField, Range(0.01f, 0.5f)] private float holdFadeSoftness = 0.1f;
    // How long the closing dissolve takes. The note fades out where it is instead of sliding off
    // screen, and the burst fires at the hit zone, where the player is actually looking.
    [SerializeField] private float holdFinishFade = 0.22f;
    [SerializeField, Range(1f, 4f)] private float holdFinishFlash = 2.6f;
    // PunchHold's beam cross-section, as a multiple of the plate. It must be GREATER than 1 or
    // the hold is invisible when it matters: a beam the same size as its own plate is perfectly
    // occluded by it when the cue is near the middle of the screen, which made every chest-height
    // hold read as an ordinary one-shot Punch until it was nearly past. Standing proud of the
    // plate is what lets the beam be seen from straight on as well as from an outer lane.
    [SerializeField, Range(1f, 2f)] private float holdBeamPlateScale = 1.3f;

    // --- Obstacles ---------------------------------------------------------------------
    //
    // Three pieces of road furniture rather than three more notes. They are built to one recipe,
    // which is the same one the hit pads already use for their outlines: a wireframe box of neon
    // beams, a translucent fill of the same hue inside it, and large chevrons of the CONTRASTING
    // accent on the face pointing at the camera saying which way to move. Nothing is modelled or
    // imported; every part is the shared unit cube, positioned.
    //
    // All three are anchored at their NEAR edge (local z = 0) and extend away down +Z, exactly
    // like a hold strip. That is what lets "the moment this reaches the hit zone" mean the same
    // thing for an obstacle as it does for every other cue, and it is also what the camera
    // director measures its pass window from.
    [Header("Obstacle Shape (Dodge wall / Curb / Duck barrier)")]
    [SerializeField] private float obstacleEdgeThickness = 0.22f;
    // The fill rides on the hold shader (alpha blend + ZWrite + clip), which is the project's
    // one established way of drawing a see-through surface that still sorts against the scene.
    [SerializeField, Range(0.05f, 0.9f)] private float obstacleFillAlpha = 0.55f;
    [SerializeField] private float obstacleChevronThickness = 0.26f;
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float obstacleEdgeEmission = 2.3f;
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float obstacleChevronEmission = 2.5f;
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float obstacleFillEmission = 1.5f;

    // The wall blocks this fraction of the road, measured from one edge inward, so the free half
    // is a real gap the camera can sit in rather than a token lean. Anything much below 0.5 stops
    // reading as something you have to get out of the way of.
    [SerializeField, Range(0.25f, 0.75f)] private float dodgeWallCoverRatio = 0.5f;
    [SerializeField] private float dodgeWallHeight = 2.1f;   // a person's height, so it cannot be jumped
    [SerializeField] private float dodgeWallLength = 8f;     // along the road: this is what sets how long the dodge is held

    [SerializeField, Range(0.2f, 0.8f)] private float curbHeight = 0.5f;
    [SerializeField] private float curbDepth = 1.2f;
    [SerializeField, Range(0.5f, 1f)] private float curbWidthRatio = 1f;    // of the road width

    // The gap left underneath, and the one dimension in the whole file that is tied to where
    // the CAMERA sits: the barrier only asks for anything if the camera is inside it at rest.
    // It was 1.45 when the camera rode at 1.6; at the shipping 1.35 that would have let the
    // camera sail underneath without ducking at all, turning the obstacle into decoration.
    // If the camera's height moves again, this moves with it.
    [SerializeField] private float duckBeamBottom = 1.1f;
    [SerializeField] private float duckBeamHeight = 0.95f;
    [SerializeField] private float duckBeamDepth = 0.9f;
    [SerializeField, Range(0.5f, 1f)] private float duckBeamWidthRatio = 1f;

    [Header("Chevron Cue Shading")]
    // The plate is deliberately NOT pushed to the emission ceiling: it has to stay a solid,
    // readable color so the black icon linework on it survives the bloom pass. The silhouette
    // glow comes from the rim term instead of from flooding the whole surface.
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float plateEmissionIntensity = 0.45f;
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float frameEmissionIntensity = 0.2f;
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float cueRimIntensity = 1.6f;
    [SerializeField, Range(0f, 1f)] private float frameDarkness = 0.6f; // how far the border/tail is pushed toward FrameTint

    [Header("Chevron Cue Icon")]
    // Authored sticker-style artwork (black silhouette on a transparent background) laid on the
    // plate's face. Drawn pure black and NON-emissive on purpose: the contrast against the lit
    // plate is the whole point, so the icon must not pick up any of the plate's glow or bloom.
    [SerializeField] private Texture2D fistIconTexture;
    [SerializeField] private Texture2D footIconTexture;
    [SerializeField, Range(0.3f, 1f)] private float iconPlateCoverage = 0.84f;   // of the plate's short side

    // Theme-sourced (see Configure): the cue cubes are colored ~50/50 from the theme's two
    // accent colors. Defaults here match the original sci-fi palette so this still renders
    // sensibly if used without ever calling Configure().
    private Color cueColorA = new Color(1f, 0.05f, 0.65f);
    private Color cueColorB = new Color(0f, 0.85f, 1f);
    private float cueEmissionIntensity = 1.7f;

    // Obstacles do NOT roll their colour per instance the way notes do. A note is one of a
    // stream and the alternating hue is part of that stream's texture; an obstacle is a warning,
    // and a warning that is a different colour each time is one the player has to re-read every
    // time. Both come from the theme's two accents, and they are deliberately the two DIFFERENT
    // ones so the chevron can never blend into the body it is drawn on.
    private Color obstacleBodyColor = new Color(0f, 0.85f, 1f);
    private Color obstacleChevronColor = new Color(1f, 0.05f, 0.65f);

    [Header("Hit-Zone Floor Pads")]
    // Theme-sourced (see Configure): idle = the theme's secondary color. Flash is NOT pure
    // white — with the stronger bloom pass, a pure-white flash at high emission blows out to a
    // solid featureless block, and it'd also be a 3rd hue outside the palette — so it's derived
    // by blending the secondary color toward white instead of being its own theme field, which
    // keeps the palette honestly down to 2 author-facing colors.
    private Color padIdleColor = new Color(0f, 0.898f, 1f);
    private Color padFlashColor = new Color(0.4f, 0.95f, 1f);
    [SerializeField] private float padFlashDuration = 0.18f;
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float padIdleEmissionIntensity = 0.35f;
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float padFlashEmissionIntensity = 1.4f;
    [SerializeField, Range(0.1f, 1f)] private float padSizeRatio = 0.85f;
    [SerializeField] private float padOutlineThickness = 0.06f; // a thin border reads as a marker, not a solid filled block

    [Header("Hit Effect (debris burst)")]
    // Tumbling cube debris, not billboard puffs: the cue is a solid chamfered plate, so what
    // comes off it when it breaks should be made of the same material. Short-lived and shrinking
    // to nothing — this is the only moment any loose cubes exist around a cue.
    [SerializeField] private int hitEffectPoolSize = 6;
    [SerializeField] private int hitEffectParticleCount = 22;
    [SerializeField] private float hitEffectLifetime = 0.45f;
    [SerializeField] private float hitEffectSpeed = 5.5f;
    [SerializeField] private float hitEffectMinSize = 0.09f;
    [SerializeField] private float hitEffectMaxSize = 0.26f;
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float hitEffectEmission = 1.5f;

    [Header("Hit Flash (light burst on destruction)")]
    // A real Point Light rather than a faked emission bump, so the flash lands on whatever
    // happens to be near the break — floor, rings, arches — with correct falloff and no coupling
    // between this spawner and the tunnel's renderers. The pool is deliberately small: URP's
    // per-object additional-light limit is 4 here, so extra simultaneous lights would be dropped
    // anyway, and each one is switched off the moment it fades rather than left at zero
    // intensity, which would still consume a light slot.
    [SerializeField] private int hitLightPoolSize = 4;
    [SerializeField] private float hitLightIntensity = 60f;
    [SerializeField] private float hitLightRange = 14f;
    [SerializeField] private float hitLightDuration = 0.24f;
    // The light alone cannot brighten the tunnel itself: every surface there is deliberately
    // near-black (accent * 0.12), so it reflects almost nothing no matter how strong the light.
    // The rings are pulsed through their emission instead — see NeonTunnelGenerator.FlashNear.
    [SerializeField] private float hitRingFlashRadius = 11f;
    [SerializeField, Range(0f, 2f)] private float hitRingFlashStrength = 0.5f;

    // --- Obstacle camera reaction -------------------------------------------------------
    //
    // The camera is what performs the movement an obstacle asks for: it leans out of the way of
    // a wall, floats over a kerb and drops under a barrier. Everything below is the FEEL of
    // those three moves and nothing else - when they happen comes from the chart, and how long
    // the obstacle takes to physically go past comes from its length and the scroll speed.
    //
    // The durations are in beats, resolved against the chart's own tempo by
    // ObstacleCameraDirector, so a move keeps its musical length on a track of any speed. The
    // amplitudes are in metres, because a metre of road is a metre of road at any tempo.
    //
    // BuildCameraPolicy() below is the single place these turn into the director's table. To
    // re-tune the whole feel, that one function and these fields are all there is to touch.
    [Header("Obstacle Camera Reaction (amplitudes in metres, timings in beats)")]
    // A step out of the way, and it has to read as a STEP taken at the last moment. Three
    // things make that, and they are separate knobs because they went wrong separately:
    //
    //   * the SHAPE - the move leaves at its top speed and coasts into position
    //     (ObstacleEase.Decelerate) rather than easing out of rest at both ends. At two beats
    //     of smoothstep the same 1.5 m looked like the camera being carried sideways, and no
    //     amount of shortening fixed that on its own;
    //   * the SPEED - how long the lead-in is;
    //   * WHEN IT FINISHES - commitMargin, below. This is the one that made a fast dodge still
    //     feel early: the move used to complete when the wall reached the HIT ZONE, four metres
    //     ahead, so the camera was parked out of the way a third of a second before the wall
    //     was anywhere near it. Now it completes a tenth of a beat before the wall is level.
    //
    // Half the road's width: the wall covers one half, so this is the middle of the half that is
    // left. Less than that and the camera ends up inside the wall it is dodging.
    [SerializeField] private float dodgeSideShift = 1.5f;
    // The lean that goes with the step, in degrees about the view axis. A body that steps
    // sideways at speed does not stay upright, and without this the camera slides across the
    // road like something on rails. It is deliberately NOT given timings of its own - it rides
    // the same envelope as the sideways offset, so the two are one movement.
    [SerializeField, Range(0f, 30f)] private float dodgeRollDegrees = 11f;
    [SerializeField] private float dodgeLeadBeats = 0.8f;
    [SerializeField] private float dodgeCommitMarginBeats = 0.15f;
    [SerializeField] private float dodgeHoldExtraBeats = 0.25f;
    [SerializeField] private float dodgeRecoverBeats = 1f;

    // A jump, and shaped like one: Decelerate up, Accelerate down, which together are a
    // ballistic arc. The hang at the top is what the curve does on its own as it runs out of
    // speed, not a plateau it is parked on - at 140 BPM it comes to 0.22 s, and held much
    // longer than that a jump stops being a jump and becomes a hover.
    //
    // The apex is short here without holdExtra having to fight for it. The hold runs from full
    // commitment to the moment the kerb's far end is behind the camera, and commitMargin puts
    // commitment most of the way through that already, so what is left is the hang.
    //
    // The rise is deliberately shorter than a real 1.15 m jump's (0.48 s of free fall): the
    // camera is not a body, and the takeoff has to be late enough to read as a reaction to the
    // kerb rather than as anticipation of it. Rise and fall come out near enough the same
    // length (0.21 s up, 0.24 s down), which is what a real arc does - the asymmetry that
    // matters is inside the curves, where the speed sits at the ground on both halves and at
    // neither end of the hang.
    //
    // The late commitment buys the same thing here as on the dodge: the apex now STRADDLES the
    // moment the kerb goes under the camera instead of arriving before it. The camera used to
    // be on its way down again while the kerb was still passing beneath, which is what landing
    // on an obstacle looks like.
    [SerializeField] private float curbRise = 1.15f;
    [SerializeField] private float curbLeadBeats = 0.5f;
    [SerializeField] private float curbCommitMarginBeats = 0.15f;
    [SerializeField] private float curbHoldExtraBeats = 0.1f;
    [SerializeField] private float curbRecoverBeats = 0.6f;

    // Drop fast, come up a little slower - a crouch is a fall and the recovery is not. So the
    // drop is Decelerate (all of its speed at the start) and the rise back is Smooth.
    //
    // The duck is the one obstacle where the margin is not just a matter of feel: the camera
    // rests at y = 1.6 and the barrier's underside is at 1.45, so at rest the camera is INSIDE
    // it. The margin is therefore the real safety number here, and the clearance it leaves is
    // measured rather than assumed - see the clearance table in the session notes.
    //
    // Kept clear of the floor at the other end: sway takes another 0.05 off, so the drop must
    // stay well under 1.6 or the view dips through the road surface.
    [SerializeField] private float duckDrop = 0.8f;
    [SerializeField] private float duckLeadBeats = 0.6f;
    [SerializeField] private float duckCommitMarginBeats = 0.15f;
    [SerializeField] private float duckHoldExtraBeats = 0.15f;
    [SerializeField] private float duckRecoverBeats = 1.5f;

    // A ceiling on the summed offset. Two obstacles overlapping is rare and the sum is what
    // blends one move into the next, but it must never be able to put the camera somewhere the
    // scene has not been checked at.
    //
    // Sized just above the largest single move on each axis (1.5 across, 1.15 up) rather than
    // generously, because the ceiling is what a pile-up is allowed to reach and the sweep of the
    // ring geometry is what says whether that is safe. At (1.80, 1.60) a simultaneous full dodge
    // and full hop put the camera 0.85 m from an arch beam - no clipping at a 0.30 near plane,
    // but with far less room than anywhere else the tunnel is ever seen from.
    [SerializeField] private Vector2 obstacleOffsetLimit = new Vector2(1.6f, 1.25f);
    // The same ceiling for the lean, and sized the same way: just above the largest single
    // reaction, so a pile-up cannot roll the horizon past anything that has been looked at.
    [SerializeField, Range(0f, 45f)] private float obstacleRollLimitDegrees = 20f;
    // A hard floor under the whole camera rig, applied after everything else has had its say.
    // The per-axis offset ceiling is symmetric, so the ceiling that lets a hop reach its full
    // height also lets a pile-up of ducks reach the same distance DOWNWARD - which, from a
    // camera that now rides at 1.35, is through the road. This is cheaper and more honest than
    // making the ceiling asymmetric, and it holds whatever the amplitudes are set to.
    [SerializeField] private float cameraMinHeight = 0.4f;
    // An obstacle going past already moves the camera a long way, so the hit kick that punctuates
    // it is scaled down from a note's - at full strength the two read as one messy lurch.
    [SerializeField, Range(0f, 2f)] private float obstacleShakeScale = 0.6f;
    // Random mode only. The chart generator spaces obstacles itself; this keeps the no-chart
    // fallback from stacking them faster than the camera can come home.
    [SerializeField] private float randomObstacleMinGap = 2.5f;

    [Header("Camera Shake (optional polish)")]
    [SerializeField] private float shakeMagnitude = 0.06f;
    [SerializeField] private float shakeDecay = 8f;
    // Shake is smooth noise at this many wobbles per second, read off the game clock - not a
    // fresh random offset every frame, which rattled at whatever the frame rate happened to be
    // (220 Hz in the Editor, 60 in a recording) and read as a jerk in the middle of a dodge.
    [SerializeField, Min(1f)] private float shakeFrequency = 20f;

    [Header("Camera Sway (flight feel, continuous)")]
    [SerializeField] private float swayAmplitudeX = 0.1f;
    [SerializeField] private float swayAmplitudeY = 0.05f;
    [SerializeField] private float swayFrequencyX = 0.35f;
    [SerializeField] private float swayFrequencyY = 0.55f;

    [Header("Pooling")]
    // The starting size; a loaded chart grows it to its own peak (EnsurePoolFor).
    [SerializeField] private int poolSizePerType = 6;
    private bool warnedPoolExhausted;

    private struct CueSlot
    {
        public Transform transform;
        public CueType type;
        public MeshRenderer[] colorRenderers; // bright plate (and, for Jump, the whole hurdle)
        public MeshRenderer[] frameRenderers; // darker border + chevron tail
        public Color color;
        public int lane;
        public bool inUse;
        public bool hit;       // cue actually crossed the hit zone (burst + pad flash + shake)

        // When this note is due AT THE HIT ZONE, on whichever clock the current mode runs on.
        // It is the note's whole definition of itself: position, the animation cue and the hold
        // sweep are all derived from it, so nothing integrates frame deltas and nothing drifts.
        public float hitTime;
        public float intensity; // 0..1 from the chart; 1 for randomly spawned notes

        // Hold state. A hold is not resolved by its first contact with the hit zone the way a
        // one-shot cue is: contact only STARTS it, and it stays alive, being eaten cell by cell,
        // until its far end has passed too. holdCells is how many of the pooled cells this
        // instance actually turned on, so the same pooled object can be a 2-cell or a 6-cell note.
        public Transform holdStrip;             // the one stretched mesh; its Z scale IS the note's length
        public MeshRenderer holdStripRenderer;
        public float holdStripZStart;
        public MeshRenderer[] holdHeadRenderers; // parts sitting at the near end (plate, border) that fade as one
        public MeshRenderer holdIcon;            // separate: it is an unlit black sticker, faded through its own colour
        public int holdCells;
        public float holdLength;
        public float holdDuration; // holdLength expressed in seconds, so the sweep is pure time
        public bool holding;
        public float holdProgress;   // 0..1 of the length already swept, pushed straight to the shader
        public bool holdFinishing;   // in the closing dissolve
        public float holdFinishTimer;

        // Obstacles only. The wireframe beams reuse colorRenderers; these two are the parts that
        // need their own treatment — the fill because it is translucent, the chevrons because
        // their whole job is to be the one thing on the obstacle that is NOT its body colour.
        public MeshRenderer[] fillRenderers;
        public MeshRenderer[] chevronRenderers;
        public Transform[] chevronRoots;
        // How far this instance reaches back along +Z from its root. Holds set it from their
        // duration; obstacles from their fixed geometry; ordinary notes leave it at 0. One field
        // rather than two so the recycle test in Update stays a single line for every cue.
        public float tailLength;
    }

    // What gets a pool, and therefore what can spawn. Jump is deliberately not here; see CueType.
    private static readonly CueType[] CueTypes = { CueType.Kick, CueType.Punch, CueType.PunchHold, CueType.KickHold,
                                                   CueType.Dodge, CueType.Curb, CueType.Duck };

    // The border/tail color every cue's accent parts are blended toward — a deep ink-blue, so a
    // cyan cue gets a navy border and a magenta cue a dark plum one. Contrast, not bloom, is
    // what gives the plate its sense of depth.
    private static readonly Color FrameTint = new Color(0.03f, 0.06f, 0.36f);

    private CueSlot[] slots;
    private Mesh cubeMesh;
    private Mesh punchPlateMesh;
    private Mesh punchFrameMesh;
    private Mesh unitCubeMesh; // stretched per instance into whatever length a hold drew
    private float kickHoldWidth;
    private Material cueHoldMat;
    // Its own instance of the hold shader, not the notes' one: the obstacle fill sits at a fixed
    // partial alpha for its whole life, and sharing cueHoldMat would mean every hold note in
    // flight had to carry an alpha override just to stay opaque.
    private Material obstacleFillMat;
    private Mesh kickPlateMesh;
    private Mesh kickFrameMesh;
    private Mesh kickHoldIconQuad;
    private Material cueBaseMat;
    private Material fistIconMat;
    private Material footIconMat;
    private Mesh punchIconQuad;
    private Mesh kickIconQuad;
    private Material padMat;
    private MaterialPropertyBlock mpb;
    private MeshRenderer[][] padRenderers; // 4 border-bar renderers per lane, not a filled quad
    private float[] padFlashTimers;
    private ParticleSystem[] hitEffects;
    private Material[] hitEffectMats;
    private int nextHitEffect;
    private Light[] hitLights;
    private float[] hitLightTimers;
    private int nextHitLight;
    private float holdCellSize;  // the floor's own grid pitch, cached from the tunnel in Build
    private float holdMaxLength; // the longest a hold can be: holdMaxCells resolved against that pitch
    private readonly ObstacleCameraDirector obstacleCamera = new ObstacleCameraDirector();
    private readonly SongClock songClock = new SongClock();
    private BeatmapSequencer sequencer;
    private Beatmap beatmapMeta;

    // Timing self-check state. x = the time the chart asked for, y = the song time the note was
    // actually seen at. Allocated regardless but only ever written to when timingReportNotes > 0.
    private readonly List<Vector2> timingSamples = new List<Vector2>();
    private bool timingReported;
    /// Raised the instant a note is scored at the hit zone - once per one-shot cue, and once per
    /// hold, at the moment the hold's far end has gone through. The HUD's combo counter listens
    /// to this instead of watching the spawner's state, so nothing outside this file has to know
    /// what a slot is or when one is in flight.
    public event System.Action<CueHitInfo> CueScored;

    public Beatmap BeatmapMeta => beatmapMeta;
    public SongClock Clock => songClock;
    public AudioClip Music => musicSource != null ? musicSource.clip : null;
    public BeatmapSequencer Sequencer => sequencer;
    private float spawnZ;
    private float spawnTimer;
    private Vector3 cameraBasePos;
    // The camera's own aim, captured once. The obstacle lean is applied ON TOP of it rather
    // than replacing it, for the same reason the offset is added to cameraBasePos: whatever the
    // scene points the camera at is not this component's business.
    private Quaternion cameraBaseRot = Quaternion.identity;
    private float shakeStrength;
    private bool built;
    // Set by StartBeatmap, consumed by the first ordinary frame. See StartSongClock.
    private int clockStartFrames = -1;

    private void OnValidate()
    {
        padIdleEmissionIntensity = Mathf.Clamp(padIdleEmissionIntensity, 0f, MaxEmissionIntensity);
        padFlashEmissionIntensity = Mathf.Clamp(padFlashEmissionIntensity, 0f, MaxEmissionIntensity);
    }

    // No self-building Awake(): a TunnelThemeApplier calls Configure(theme) then Build(), same
    // pattern (and same reasoning) as NeonTunnelGenerator. Called directly with no theme (or
    // never called), this still builds fine using the defaults above.

    // Copies the palette this spawner needs out of the given theme. Must be called before
    // Build(). Hit-detection, pooling, timings, animation sync and camera feel are untouched —
    // only the cue/pad colors come from the theme.
    public void Configure(TunnelTheme theme)
    {
        if (theme == null) return;

        cueColorA = theme.primaryColor;
        cueColorB = theme.secondaryColor;
        cueEmissionIntensity = theme.accentEmissionIntensity;

        obstacleBodyColor = theme.secondaryColor;
        obstacleChevronColor = theme.primaryColor;

        padIdleColor = theme.secondaryColor;
        padFlashColor = Color.Lerp(theme.secondaryColor, Color.white, 0.6f);
    }

    /// Seconds on the clock the current mode runs on: the song clock under a beatmap, a plain
    /// wall clock otherwise. Every note's position, animation cue and hold sweep is measured
    /// against this one value, which is what lets both modes share a single movement model.
    private float Now => spawnMode == CueSpawnMode.Beatmap ? songClock.SongTime : Time.time;

    /// How long a note takes to travel from where it appears to the hit zone. Notes are released
    /// exactly this far ahead of when they are due, so arrival lands on the beat.
    public int LaneCount => tunnel.LaneCount;

    public float FlightTime => (spawnZ - hitZoneZ) / Mathf.Max(0.01f, tunnel.ScrollSpeed);

    private void Update()
    {
        // `slots` is checked as well as `built`, which looks redundant and is not: a script edit
        // made while the Editor is in play mode reloads the domain, and the reload brings `built`
        // back as true while the pool it refers to comes back empty. The component then throws
        // once per frame for as long as play mode lasts. Nothing built this way is playable, so
        // the right answer is to go quiet rather than to rebuild half a tunnel underneath a
        // running track.
        if (!built || slots == null) return;

        if (clockStartFrames > 0) clockStartFrames--;
        else if (clockStartFrames == 0 && MusicLoaded())
        {
            clockStartFrames = -1;
            StartSongClock();
        }

        float now = Now;

        if (spawnMode == CueSpawnMode.Beatmap) PumpBeatmap(now);
        else
        {
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                spawnTimer = spawnInterval;
                TrySpawnCue();
            }
        }

        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].inUse) continue;

            // Position is SOLVED from the clock, not advanced by a frame delta. A dropped frame
            // therefore costs one frame of smoothness and nothing else: the note is wherever the
            // track says it should be, so a ten-minute song cannot accumulate error the way
            // integrating deltaTime did.
            Vector3 pos = slots[i].transform.localPosition;
            pos.z = HitZoneZFor(slots[i].type) + (slots[i].hitTime - now) * tunnel.ScrollSpeed;

            // A hold STOPS when its near edge arrives and is played out where it stands. Its far
            // end keeps closing at the scroll speed - see UpdateHold - so nothing about the
            // note's tail changes; what changes is that the head no longer slides past the lens
            // and off the back of the frame, leaving the player watching an empty road.
            if (IsHold(slots[i].type) && now >= slots[i].hitTime) pos.z = HitZoneZFor(slots[i].type);

            slots[i].transform.localPosition = pos;

            if (IsHold(slots[i].type))
                UpdateHold(i, now, pos);
            else if (!slots[i].hit && now >= slots[i].hitTime)
            {
                slots[i].hit = true;
                RecordTimingSample(slots[i].hitTime, now);
                if (IsObstacle(slots[i].type)) OnObstaclePassed(i, pos);
                else OnCueHit(i, pos);
            }

            // A hold is only spent once its FAR end has gone by, so it must survive its own root
            // dropping past recycleZ — for a 6-cell note the root is 12 units behind the hit zone
            // by the time the last cell is played. An obstacle is the same story for the same
            // reason: an eight-metre wall is still beside the camera long after its near edge
            // crossed the hit zone. Measuring the far end keeps one rule for all three.
            float tailZ = pos.z + slots[i].tailLength;
            // A note in its closing dissolve is exempt: it is being faded out deliberately, and
            // snapping it off here would be the very "disappears while sliding away" the fade
            // replaces. UpdateHoldFinish retires it the moment the fade completes.
            if (slots[i].inUse && !slots[i].holdFinishing && tailZ < recycleZ)
            {
                slots[i].inUse = false;
                slots[i].transform.gameObject.SetActive(false);
            }
        }

        UpdateHitPads();
        UpdateHitLights();
        UpdateCameraShake();
    }

    public void Build()
    {
        if (built) return;
        built = true;

        cubeMesh = GetSharedCubeMesh();
        cueBaseMat = CreateVolumetricMaterial(Color.white, cueEmissionIntensity);
        cueHoldMat = CreateHoldMaterial(cueEmissionIntensity);
        obstacleFillMat = CreateHoldMaterial(obstacleFillEmission);
        fistIconMat = CreateIconMaterial(fistIconTexture);
        footIconMat = CreateIconMaterial(footIconTexture);
        mpb = new MaterialPropertyBlock();

        // Baked once at the actual target size (not a generic unit cube stretched via
        // transform.localScale) so the chamfer stays a consistent bevel regardless of how
        // differently Punch (an upright sign) and Kick (a floor plate) are proportioned —
        // stretching a shared unit-cube bevel non-uniformly would smear it into an ellipse.
        float laneWidth = tunnel.FloorWidth / tunnel.LaneCount;
        float kickWidth = laneWidth * cueLaneWidthRatio;
        holdCellSize = tunnel.FloorGridCellWorldSize;
        holdMaxCells = Mathf.Max(holdMaxCells, holdMinCells); // a min above max would make Random.Range pick nothing sane
        holdMaxLength = holdMaxCells * holdCellSize;
        BuildChevronMeshes(kickWidth);

        spawnZ = tunnel.TunnelLength * spawnZRatio;

        int total = poolSizePerType * CueTypes.Length;
        slots = new CueSlot[total];
        int idx = 0;
        for (int t = 0; t < CueTypes.Length; t++)
            for (int i = 0; i < poolSizePerType; i++)
                slots[idx++] = NewSlot(CueTypes[t]);

        BuildHitPads();
        BuildHitEffects();
        BuildHitLights();
        BuildCameraPolicy();

        if (cameraTransform != null)
        {
            cameraBasePos = cameraTransform.localPosition;
            cameraBaseRot = cameraTransform.localRotation;
        }

        if (spawnMode == CueSpawnMode.Beatmap) StartBeatmap();
    }

    /// Turns the serialized feel above into the director's table. This is the ONE place the
    /// mapping from "which obstacle" to "what the camera does" lives: a Dodge becomes a lean into
    /// the free half of the road, a Curb a hop, a Duck a drop. Swapping the whole character of
    /// the reactions - say, adding a roll, or making a left dodge shallower than a right one -
    /// is this function and the fields above, and nothing else in the project needs to know.
    private void BuildCameraPolicy()
    {
        float side = Mathf.Abs(dodgeSideShift);

        // A wall on the RIGHT sends the camera left, and vice versa. The naming is the camera's
        // movement, not the wall's side, because that is what this table describes.
        obstacleCamera.SetPolicy(ObstacleMove.DodgeLeft, new ObstacleCameraDirector.Move
        {
            offset = new Vector3(-side, 0f, 0f),
            // Positive Z rotates the camera's up vector toward -X, i.e. leans it LEFT, which is
            // the way this move is going. The mirror image gets the opposite sign below.
            rollDegrees = Mathf.Abs(dodgeRollDegrees),
            leadBeats = dodgeLeadBeats,
            commitMarginBeats = dodgeCommitMarginBeats,
            holdExtraBeats = dodgeHoldExtraBeats,
            recoverBeats = dodgeRecoverBeats,
            // Glide both ways: out of rest and back into it with no jump in speed or
            // acceleration. Decelerate here left rest - and left the far side - at full speed,
            // which read as a jerk at the start of every lean and every return.
            outEase = ObstacleEase.Glide,
            inEase = ObstacleEase.Glide,
        });
        obstacleCamera.SetPolicy(ObstacleMove.DodgeRight, new ObstacleCameraDirector.Move
        {
            offset = new Vector3(side, 0f, 0f),
            rollDegrees = -Mathf.Abs(dodgeRollDegrees),
            leadBeats = dodgeLeadBeats,
            commitMarginBeats = dodgeCommitMarginBeats,
            holdExtraBeats = dodgeHoldExtraBeats,
            recoverBeats = dodgeRecoverBeats,
            outEase = ObstacleEase.Glide,
            inEase = ObstacleEase.Glide,
        });
        obstacleCamera.SetPolicy(ObstacleMove.Hop, new ObstacleCameraDirector.Move
        {
            offset = new Vector3(0f, Mathf.Abs(curbRise), 0f),
            leadBeats = curbLeadBeats,
            commitMarginBeats = curbCommitMarginBeats,
            holdExtraBeats = curbHoldExtraBeats,
            recoverBeats = curbRecoverBeats,
            outEase = ObstacleEase.Decelerate,   // takeoff: all the speed is at the ground
            inEase = ObstacleEase.Accelerate,    // fall: fastest as it lands
        });
        obstacleCamera.SetPolicy(ObstacleMove.Duck, new ObstacleCameraDirector.Move
        {
            offset = new Vector3(0f, -Mathf.Abs(duckDrop), 0f),
            leadBeats = duckLeadBeats,
            commitMarginBeats = duckCommitMarginBeats,
            holdExtraBeats = duckHoldExtraBeats,
            recoverBeats = duckRecoverBeats,
            outEase = ObstacleEase.Decelerate,   // a crouch is a drop
            inEase = ObstacleEase.Smooth,        // standing back up is not
        });

        obstacleCamera.SetLimit(new Vector3(Mathf.Abs(obstacleOffsetLimit.x), Mathf.Abs(obstacleOffsetLimit.y), 0f));
        obstacleCamera.SetRollLimit(obstacleRollLimitDegrees);
        obstacleCamera.SetTempo(beatmapMeta != null ? beatmapMeta.bpm : 0f);
    }

    /// Books the camera's reaction to one obstacle. Everything the director needs is known here:
    /// WHEN from the chart, and how long the thing takes to go past from its own length and the
    /// scroll speed - measured from the hit zone, four metres ahead, to behind the camera.
    private void RegisterObstaclePass(CueType type, int side, float dueTime)
    {
        ObstacleMove move =
            type == CueType.Dodge ? (side > 0 ? ObstacleMove.DodgeLeft : ObstacleMove.DodgeRight)
          : type == CueType.Curb ? ObstacleMove.Hop
          : ObstacleMove.Duck;

        float speed = Mathf.Max(0.01f, tunnel.ScrollSpeed);
        // Two moments, both measured from the chart's arrival time. The first is when the
        // obstacle's near face is level with the camera - which is NOT the arrival, because the
        // hit zone sits hitZoneZ metres in front of it - and the second is when its far end is
        // behind the camera. The director anchors the move to the first and ends the hold at
        // the second, so both follow hitZoneZ and the scroll speed instead of being re-tuned
        // by hand whenever either changes.
        float reachSeconds = hitZoneZ / speed;
        float passSeconds = (hitZoneZ + ObstacleZExtent(type)) / speed;
        obstacleCamera.Register(move, dueTime, reachSeconds, passSeconds);
    }

    /// When the camera reacts to one obstacle from the chart: starting to move, fully committed,
    /// starting back, and home again - the numbers RegisterObstaclePass books, worked out without
    /// booking them.
    /// `cameraSide` is -1 when the camera moves left, +1 right, 0 for a hop or a duck.
    public void ObstacleTiming(BeatmapCue cue, out float begin, out float commit, out float release, out float settled, out int cameraSide)
    {
        int side = DodgeSideFromLane(cue.lane);
        ObstacleMove move =
            cue.type == CueType.Dodge ? (side > 0 ? ObstacleMove.DodgeLeft : ObstacleMove.DodgeRight)
          : cue.type == CueType.Curb ? ObstacleMove.Hop
          : ObstacleMove.Duck;
        cameraSide = move == ObstacleMove.DodgeLeft ? -1 : move == ObstacleMove.DodgeRight ? 1 : 0;
        float speed = Mathf.Max(0.01f, tunnel.ScrollSpeed);
        obstacleCamera.Window(move, cue.time, hitZoneZ / speed, (hitZoneZ + ObstacleZExtent(cue.type)) / speed,
                              out begin, out commit, out release, out settled);
    }

    /// Loads the chart; the music and the clock start a couple of frames later, see StartSongClock.
    /// There is no lead-in beyond that: the intro is made by dropping every note that could not be
    /// born after it ends, see TrimIntro.
    ///
    /// The trim happens here, not in the generator, because the rule depends on FlightTime, and
    /// FlightTime is a property of this scene (scroll speed, spawn distance), not of the audio: a
    /// chart trimmed at generation time would silently go wrong the day the tunnel speed changes.
    /// It also keeps the intro a knob that can be turned without regenerating the chart, which
    /// would rewrite its whole set plan.
    ///
    /// A failure here falls back to Random rather than leaving a silent, empty tunnel — a broken
    /// path or a bad file should be loud in the console and still leave something on screen.
    public void StartBeatmap()
    {
        if (!BeatmapReader.TryLoad(beatmapFileName, tunnel.LaneCount, out beatmapMeta, out var cues, out string error))
        {
            Debug.LogError("[CueSpawner] beatmap load failed, falling back to random spawning: " + error, this);
            spawnMode = CueSpawnMode.Random;
            return;
        }

        int total = cues.Count;
        float cutoff = IntroCutoff(introBars, beatmapMeta.bpm, FlightTime, beatmapMeta.sets, introSkipsPartialSet);
        float wallFloor = IntroWallFloor(introWallBars, beatmapMeta.bpm, FlightTime);
        cues = TrimIntro(cues, beatmapMeta, cutoff, wallFloor);
        EnsurePoolFor(cues);

        sequencer = new BeatmapSequencer(cues);
        RetireAllCues();
        obstacleCamera.SetTempo(beatmapMeta.bpm);
        songClock.Stop();
        if (musicSource != null && musicSource.clip != null) musicSource.clip.LoadAudioData();
        clockStartFrames = ClockStartDelayFrames;
        Debug.Log("[CueSpawner] beatmap '" + beatmapFileName + "' loaded: " + cues.Count + " of " + total + " notes, bpm " + beatmapMeta.bpm
                + ", intro " + introBars.ToString("0.##") + " bars, first note at " + (cues.Count > 0 ? cues[0].time : 0f).ToString("F2")
                + "s (cutoff " + cutoff.ToString("F2") + "s, flight " + FlightTime.ToString("F2") + "s), offset "
                + (beatmapMeta.audioOffset + beatmapUserOffset).ToString("F3") + "s", this);
    }

    /// Frames to wait after the chart loads before the clock starts, and how far ahead of the
    /// mixer the music is scheduled, in DSP buffers.
    private const int ClockStartDelayFrames = 2;
    private const int ClockStartLeadBuffers = 3;

    /// Starts the music and the song clock together, once the clip is decoded, on an ordinary frame.
    ///
    /// Scheduling the clip before its audio data is loaded is what made the chart run early: the
    /// tracks import with Preload Audio Data off, so PlayScheduled first had to decompress the
    /// whole Vorbis file, and the music began that long after the moment the clock counted from -
    /// measured at 115 to 470 ms, different on every run, which no constant offset can fix.
    /// StartBeatmap asks for the data up front and this waits for it, so the import setting no
    /// longer matters. The lead of a few DSP buffers covers the rest: PlayScheduled only reaches
    /// the audio thread at the end of the frame that issued it, and a start time of "now" would
    /// already be in the past by then.
    private bool MusicLoaded()
    {
        var clip = musicSource != null ? musicSource.clip : null;
        return clip == null || clip.loadState == AudioDataLoadState.Loaded || clip.loadState == AudioDataLoadState.Failed;
    }

    private void StartSongClock()
    {
        AudioSettings.GetDSPBufferSize(out int bufferLength, out _);
        float lead = ClockStartLeadBuffers * bufferLength / (float)Mathf.Max(1, AudioSettings.outputSampleRate);
        songClock.Begin(musicSource, lead, beatmapMeta.audioOffset, beatmapUserOffset);
        Debug.Log("[CueSpawner] song clock started on frame " + Time.frameCount + ", "
                + Time.timeSinceLevelLoad.ToString("F2") + "s after load, lead " + (lead * 1000f).ToString("F1") + " ms, DSP buffer latency "
                + (songClock.BufferLatency * 1000f).ToString("F1") + " ms, offset " + ((beatmapMeta.audioOffset + beatmapUserOffset) * 1000f).ToString("F1") + " ms", this);
    }

    /// The chart doesn't carry a time signature. The generator defaults to 4/4 and every track in
    /// this project is in it.
    private const float IntroBeatsPerBar = 4f;

    /// The earliest arrival time a chart note may have. This is the whole intro policy, so a
    /// different rule means changing this function and nothing else.
    ///
    /// The floor is intro + flight: a note spawns one flight before it is due, so anything earlier
    /// would have to be born during the intro. With `wholeSets`, a cutoff that lands inside a
    /// workout set moves on to the start of the next set, so the routine opens on a whole block
    /// rather than on the tail of one.
    private static float IntroCutoff(float bars, float bpm, float flight, BeatmapSet[] sets, bool wholeSets)
    {
        float beat = 60f / (bpm > 0f ? bpm : 120f);
        float cutoff = Mathf.Max(0f, bars) * IntroBeatsPerBar * beat + flight;
        if (!wholeSets || sets == null) return cutoff;

        for (int i = 0; i < sets.Length; i++)
        {
            if (sets[i].end < cutoff) continue;                 // over before the intro ends
            return sets[i].start >= cutoff ? cutoff : NextSetStart(sets, i, cutoff);
        }
        return cutoff;
    }

    private static float NextSetStart(BeatmapSet[] sets, int straddling, float fallback)
    {
        return straddling + 1 < sets.Length ? Mathf.Max(fallback, sets[straddling + 1].start) : float.PositiveInfinity;
    }

    /// The earliest arrival an intro wall may have: born `bars` after the music starts.
    private static float IntroWallFloor(float bars, float bpm, float flight)
    {
        float beat = 60f / (bpm > 0f ? bpm : 120f);
        return Mathf.Max(0f, bars) * IntroBeatsPerBar * beat + flight;
    }

    /// Whether a note before the intro cutoff is a gap-filling wall and may stay. Such a wall is
    /// a Dodge that no workout set counts among its notes - the generator adds them after the
    /// sets are planned, so the sets it writes say 0 notes wherever only walls stand.
    private static bool IsIntroWall(BeatmapCue cue, BeatmapSet[] sets, float floor)
    {
        if (cue.type != CueType.Dodge || cue.time < floor) return false;
        if (sets == null) return true;
        foreach (var set in sets)
            if (set.notes > 0 && cue.time >= set.start - 0.001f && cue.time <= set.restEnd) return false;
        return true;
    }

    /// Drops every note before `cutoff` except the intro walls (see IsIntroWall) and, with them,
    /// every set that no longer has notes. The
    /// sets matter as much as the notes: the HUD walks them in order and banners each one as the
    /// notes pass its end, so a set left over from the intro would fire its banner on the first
    /// real note, and the "all notes scored" final banner compares against the sequencer's count.
    private static List<BeatmapCue> TrimIntro(List<BeatmapCue> cues, Beatmap meta, float cutoff, float wallFloor)
    {
        var kept = cues.FindAll(c => c.time >= cutoff || IsIntroWall(c, meta.sets, wallFloor));
        if (meta.sets == null) return kept;

        var sets = new List<BeatmapSet>();
        foreach (var set in meta.sets)
        {
            int notes = kept.FindAll(c => c.time >= set.start - 0.001f && c.time <= set.restEnd).Count;
            if (set.end < cutoff || notes == 0) continue;
            set.notes = Mathf.Min(set.notes, notes);
            sets.Add(set);
        }
        meta.sets = sets.ToArray();
        return kept;
    }

    /// Runs the chart without audio, on a clock the caller advances by hand. Used by the timing
    /// self-check, and usable for a silent rehearsal of a chart.
    public void StartBeatmapOffline(float startTime)
    {
        if (!BeatmapReader.TryLoad(beatmapFileName, tunnel.LaneCount, out beatmapMeta, out var cues, out string error))
        {
            Debug.LogError("[CueSpawner] beatmap load failed: " + error, this);
            return;
        }

        spawnMode = CueSpawnMode.Beatmap;
        sequencer = new BeatmapSequencer(cues);
        RetireAllCues();
        obstacleCamera.SetTempo(beatmapMeta.bpm);
        clockStartFrames = -1;
        songClock.BeginOffline(startTime, beatmapMeta.audioOffset, beatmapUserOffset);
    }

    /// Empties the tunnel: every cue still in flight is put back in the pool and the camera's
    /// booked reactions are dropped with them.
    ///
    /// The two have to happen TOGETHER, which is why this exists rather than a bare Clear() on
    /// the director. A cue's position is solved from the clock, so restarting the clock teleports
    /// whatever is in flight to wherever the new time says it belongs - a note due at t=58 with
    /// the clock restarted at t=15 lands five hundred metres down a tunnel a hundred and forty
    /// long. Dropping the camera's pass list on its own is worse still: the obstacle object
    /// survives the restart and slides past with the camera sitting perfectly still.
    private void RetireAllCues()
    {
        obstacleCamera.Clear();
        if (slots == null) return;

        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].inUse) continue;
            slots[i].inUse = false;
            slots[i].holdFinishing = false;
            if (slots[i].transform != null) slots[i].transform.gameObject.SetActive(false);
        }
    }

    /// Records one (chart time, observed time) pair, and writes the table once enough have landed.
    ///
    /// What this measures is frame quantisation, and it can only ever be LATE, never early: a note
    /// registers on the first frame whose song time has reached it, so the error is distributed
    /// over [0, frame interval) and its mean is about half a frame. It is reported rather than
    /// corrected because it cannot be corrected from here — a note can only be shown on a frame
    /// that exists, and the position it is drawn at is already solved from the clock, so what the
    /// player SEES is right even on the frame the hit flag is set late.
    ///
    /// A delta far larger than one frame is the interesting outcome: that is the song clock and
    /// the chart disagreeing, which is a real timing bug rather than a sampling artefact.
    private void RecordTimingSample(float due, float observed)
    {
        if (timingReportNotes <= 0 || timingReported || spawnMode != CueSpawnMode.Beatmap) return;

        timingSamples.Add(new Vector2(due, observed));
        if (timingSamples.Count < timingReportNotes) return;

        timingReported = true;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[CueSpawner] timing report: " + timingSamples.Count + " notes from '" + beatmapFileName + "'");
        sb.AppendLine("   #   event.time     songTime     delta ms");

        float sum = 0f, worst = 0f;
        for (int i = 0; i < timingSamples.Count; i++)
        {
            float delta = (timingSamples[i].y - timingSamples[i].x) * 1000f;
            sum += delta;
            if (Mathf.Abs(delta) > Mathf.Abs(worst)) worst = delta;
            sb.AppendLine(i.ToString().PadLeft(4)
                        + timingSamples[i].x.ToString("F3").PadLeft(13)
                        + timingSamples[i].y.ToString("F3").PadLeft(13)
                        + delta.ToString("F2").PadLeft(13));
        }

        sb.AppendLine("mean " + (sum / timingSamples.Count).ToString("F2") + " ms, worst "
                    + worst.ToString("F2") + " ms, one frame at 60 fps = 16.67 ms");
        Debug.Log(sb.ToString(), this);
    }

    /// Random mode: roll a note and hand it to the shared spawn path. Hold length is rolled in
    /// cells here, which is the only thing this mode still decides that the chart otherwise would.
    private void TrySpawnCue()
    {
        CueType type = CueTypes[Random.Range(0, CueTypes.Length)];
        int lane = Random.Range(0, tunnel.LaneCount);

        // Obstacles are a whole-body movement and the camera needs time to come back to centre
        // between them, so an obstacle rolled too soon after the last one degrades to an ordinary
        // note rather than being dropped - the tunnel should never go quiet because of pacing.
        if (IsObstacle(type) && Now + FlightTime < obstacleCamera.BusyUntil + randomObstacleMinGap)
            type = (Random.value < 0.5f) ? CueType.Kick : CueType.Punch;

        float duration = 0f;
        if (IsHold(type))
        {
            int cells = Mathf.Max(1, Random.Range(holdMinCells, holdMaxCells + 1));
            duration = cells * holdCellSize / Mathf.Max(0.01f, tunnel.ScrollSpeed);
        }

        SpawnCue(type, lane, duration, 1f, Now + FlightTime);
    }

    /// Releases every chart note whose spawn moment has arrived. Looped rather than taken one per
    /// frame because a chord lands several notes on the same timestamp, and a frame that ran long
    /// has to catch up rather than smear the notes it slept through across later frames.
    private void PumpBeatmap(float now)
    {
        if (sequencer == null) return;

        float flight = FlightTime;
        while (sequencer.TryDequeue(now, flight, out BeatmapCue cue))
            SpawnCue(cue.type, cue.lane, cue.duration, cue.intensity, cue.time);
    }

    /// The single place a note is brought to life, shared by both modes so they cannot drift
    /// apart. `dueTime` is when this note must be AT the hit zone, on the active clock; the
    /// caller decides it — Random from now plus a flight, Beatmap from the chart.
    private void SpawnCue(CueType type, int lane, float duration, float intensity, float dueTime)
    {
        int slotIndex = FindFreeSlot(type);
        if (slotIndex < 0)
        {
            // Should not happen once EnsurePoolFor has sized the pool; said out loud if it does,
            // because a note that silently never appears looks like a chart bug.
            if (!warnedPoolExhausted)
                Debug.LogWarning("[CueSpawner] no free " + type + " cue at " + dueTime.ToString("F2") + "s - the note is skipped", this);
            warnedPoolExhausted = true;
            return;
        }

        lane = Mathf.Clamp(lane, 0, tunnel.LaneCount - 1);
        bool obstacle = IsObstacle(type);
        int side = DodgeSideFromLane(lane);

        // An obstacle is a piece of the road rather than something standing in a lane: the kerb
        // and the barrier span the whole width, and the wall takes a SIDE, which the lane names.
        float x = !obstacle ? tunnel.GetLaneCenterX(lane)
                : type == CueType.Dodge ? DodgeWallCenterX(side)
                : 0f;
        float y = HeightForType(type);
        Color color = obstacle ? obstacleBodyColor
                              : (Random.value < 0.5f) ? cueColorA : cueColorB;

        Transform t = slots[slotIndex].transform;
        t.gameObject.SetActive(true);
        // Placed where the clock says it already is, rather than at spawnZ: a note released a
        // fraction late must appear slightly further down the tunnel, not snap back to the start.
        t.localPosition = new Vector3(x, y, HitZoneZFor(type) + (dueTime - Now) * tunnel.ScrollSpeed);

        slots[slotIndex].hitTime = dueTime;
        slots[slotIndex].intensity = Mathf.Clamp01(intensity);

        // Length is resolved before the colour is applied, because ApplyCueColor is also what
        // wipes the spent tint left on this pooled object by its previous use.
        SetupHoldInstance(slotIndex, type, duration);
        if (type == CueType.Dodge) AimDodgeChevrons(slotIndex, side);
        ApplyCueColor(slots[slotIndex], color);

        // How far this instance reaches back along the road, which is the one thing the recycle
        // test needs and the only thing a hold and an obstacle measure differently.
        slots[slotIndex].tailLength = IsHold(type) ? slots[slotIndex].holdLength
                                    : obstacle ? ObstacleZExtent(type)
                                    : 0f;

        if (obstacle) RegisterObstaclePass(type, side, dueTime);

        slots[slotIndex].inUse = true;
        slots[slotIndex].hit = false;
        slots[slotIndex].lane = lane;
        slots[slotIndex].color = color;
    }

    private static bool IsHold(CueType type) => type == CueType.KickHold || type == CueType.PunchHold;

    /// The distance at which this cue resolves. One number for almost everything, and a second
    /// for the punches; see punchHitZoneZ.
    private float HitZoneZFor(CueType type) =>
        (type == CueType.Punch || type == CueType.PunchHold) ? punchHitZoneZ : hitZoneZ;

    private static bool IsObstacle(CueType type) =>
        type == CueType.Dodge || type == CueType.Curb || type == CueType.Duck;

    /// Which side of the road a Dodge wall stands on, read off the note's lane: the left half of
    /// the lanes puts it on the left, the right half on the right. The lane carries the direction
    /// because the chart format already has a lane on every event and it is already validated
    /// against the tunnel - adding a `direction` field would mean a second thing to keep in step
    /// between the generator, the DTO, the reader and the spawner for no extra expressiveness.
    private int DodgeSideFromLane(int lane) => lane < tunnel.LaneCount * 0.5f ? -1 : 1;

    /// Picks this instance's length and turns on exactly that many of the pooled cells. A
    /// non-hold slot is left completely alone, which is what keeps Kick/Punch/Jump on precisely
    /// the code path they had before holds existed.
    private void SetupHoldInstance(int slotIndex, CueType type, float durationSeconds)
    {
        if (!IsHold(type) || slots[slotIndex].holdStrip == null) return;

        // Duration is the authority and length follows from it, not the other way round: the note
        // has to last as long as the sustained sound does, whatever the scroll speed happens to be.
        float duration = Mathf.Max(0.01f, durationSeconds);
        float length = duration * tunnel.ScrollSpeed;

        // The length lives entirely in the transform: one mesh, stretched. Nothing is hidden or
        // rebuilt, so there is no join anywhere along it for the player to see.
        SetHoldStripLength(slotIndex, length);

        slots[slotIndex].holdCells = Mathf.Max(1, Mathf.RoundToInt(length / Mathf.Max(0.01f, holdCellSize)));
        slots[slotIndex].holdLength = length;
        slots[slotIndex].holdDuration = duration;
        slots[slotIndex].holding = false;
        slots[slotIndex].holdProgress = 0f;
        slots[slotIndex].holdFinishing = false;
        slots[slotIndex].holdFinishTimer = 0f;
        PushHoldShader(slotIndex, 0f, 1f, 1f);
        SetHoldIconAlpha(slotIndex, 1f);
    }

    /// Turns a pooled wall into a left-hand or a right-hand one. Only the chevrons rotate; the
    /// box itself is symmetric about its own centre line, and the root's x has already put it
    /// against the correct side of the road.
    private void AimDodgeChevrons(int slotIndex, int side)
    {
        var roots = slots[slotIndex].chevronRoots;
        if (roots == null) return;

        var rotation = Quaternion.Euler(0f, 0f, DodgeChevronAngle(side));
        for (int i = 0; i < roots.Length; i++)
            if (roots[i] != null) roots[i].localRotation = rotation;
    }

    /// Stretches a hold's single mesh to the given length, keeping its NEAR edge at the note's
    /// root. Used both to size a note on spawn and to shorten it as it is played out, so the two
    /// cannot disagree about which end of the strip is anchored.
    private void SetHoldStripLength(int slotIndex, float length)
    {
        var strip = slots[slotIndex].holdStrip;
        if (strip == null) return;

        length = Mathf.Max(0.0001f, length);
        var scale = strip.localScale;
        strip.localScale = new Vector3(scale.x, scale.y, length);
        var lp = strip.localPosition;
        strip.localPosition = new Vector3(lp.x, lp.y, slots[slotIndex].holdStripZStart + length * 0.5f);
    }

    private CueSlot NewSlot(CueType type)
    {
        var visual = CreateCueVisual(type);
        visual.root.gameObject.SetActive(false);
        return new CueSlot
        {
            transform = visual.root,
            type = type,
            colorRenderers = visual.bright,
            frameRenderers = visual.frame,
            holdStrip = visual.strip,
            holdStripRenderer = visual.stripRenderer,
            holdStripZStart = visual.stripZStart,
            holdHeadRenderers = visual.head,
            holdIcon = visual.icon,
            fillRenderers = visual.fill,
            chevronRenderers = visual.chevron,
            chevronRoots = visual.chevronRoots,
            inUse = false,
        };
    }

    /// Grows the pool to what the loaded chart actually needs. A cue is alive from its spawn, one
    /// flight before it is due, until its far end is behind the camera, so the number of one type
    /// on the road at once is a property of the chart - one Punch a beat at 128 BPM keeps ~16 in
    /// flight - and a fixed pool silently drops every note past its size. That is what made long
    /// Punch/Kick sets look half empty, and the odd note in a run "not count": it never spawned.
    private void EnsurePoolFor(List<BeatmapCue> cues)
    {
        float speed = Mathf.Max(0.01f, tunnel.ScrollSpeed);
        float flight = FlightTime;
        var added = new System.Text.StringBuilder();
        var grown = new List<CueSlot>(slots);
        foreach (var type in CueTypes)
        {
            // Alive from spawn to well behind the camera: flight, the hold, the trip from the hit
            // zone past the camera, the object's own length, and a second of slack for the tail.
            float extent = IsObstacle(type) ? ObstacleZExtent(type) : 1f;
            float after = (hitZoneZ + extent) / speed + 1f;
            var edges = new List<(float t, int d)>();
            foreach (var c in cues)
            {
                if (c.type != type) continue;
                edges.Add((c.time - flight, 1));
                edges.Add((c.time + c.duration + after, -1));
            }
            edges.Sort((a, b) => a.t != b.t ? a.t.CompareTo(b.t) : a.d.CompareTo(b.d));
            int alive = 0, peak = 0;
            foreach (var e in edges) { alive += e.d; peak = Mathf.Max(peak, alive); }

            int have = 0;
            foreach (var slot in grown) if (slot.type == type) have++;
            int need = peak + 2;
            if (need <= have) continue;
            for (int i = have; i < need; i++) grown.Add(NewSlot(type));
            added.Append(type).Append(' ').Append(have).Append("->").Append(need).Append("  ");
        }
        if (added.Length == 0) return;
        slots = grown.ToArray();
        Debug.Log("[CueSpawner] pool grown for this chart: " + added, this);
    }

    private int FindFreeSlot(CueType type)
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].type == type && !slots[i].inUse) return i;
        return -1;
    }

    private float HeightForType(CueType type)
    {
        switch (type)
        {
            case CueType.Kick:
            case CueType.KickHold: return kickHeight; // a hold lies on the floor exactly like the kick it sustains
            case CueType.Punch: return punchHeight;
            case CueType.PunchHold: return punchHoldHeight; // sustained punches fly high, out of the one-shot lane
            // Jump's posts and every obstacle's box are authored from their own floor-level root.
            default: return 0f;
        }
    }

    /// A hold plays out instead of resolving on contact. Its near edge reaching the hit zone
    /// only STARTS it: from then on the hit zone sweeps down the strip, darkening each cell as
    /// it passes, and the note is not finished — and does not burst — until the far end has gone
    /// by too. A miss is not modelled yet; this is the sustain visual and its timing only.
    private void UpdateHold(int slotIndex, float now, Vector3 pos)
    {
        // The closing dissolve owns the note once it starts, and must keep running even though
        // the note has already been scored, so it is checked before anything else.
        if (slots[slotIndex].holdFinishing)
        {
            UpdateHoldFinish(slotIndex);
            return;
        }

        if (now < slots[slotIndex].hitTime) return; // the near edge has not arrived yet

        if (!slots[slotIndex].holding)
        {
            slots[slotIndex].holding = true;
            RecordTimingSample(slots[slotIndex].hitTime, now);
            OnHoldStart(slotIndex, pos);
        }

        // Progress is a continuous fraction of the note's DURATION, read straight off the clock.
        // Measuring it in seconds rather than in metres travelled means the note plays out with
        // the music even if the scroll speed is changed mid-track.
        float span = Mathf.Max(0.0001f, slots[slotIndex].holdDuration);
        float progress = Mathf.Clamp01((now - slots[slotIndex].hitTime) / span);
        slots[slotIndex].holdProgress = progress;

        // The note is being CONSUMED, not swept. Its head is pinned at the hit point and its
        // body shortens from the far end until there is none left - which is the same motion the
        // far end had while the note was flying, because length = duration x scroll speed. So
        // the tail keeps closing at exactly the speed it always did, and the note visibly gets
        // shorter in the player's hands instead of sliding away behind them.
        SetHoldStripLength(slotIndex, slots[slotIndex].holdLength * (1f - progress));

        // No length wipe any more: the geometry itself is what goes. The whole of what is left
        // stays lit at the held boost, which is what "still holding this" looks like.
        PushHoldShader(slotIndex, 0f, holdHeldBoost, 1f);
        SetHoldIconAlpha(slotIndex, 1f);

        if (progress >= 1f && !slots[slotIndex].hit)
        {
            slots[slotIndex].hit = true;
            BeginHoldFinish(slotIndex);
        }
    }

    /// Start-of-hold feedback: the lane's pad lights and the hit light pops, but deliberately no
    /// debris burst and no camera shake — those punctuate the END of the note, and firing them
    /// at both ends would make a hold read as two separate hits.
    private void OnHoldStart(int slotIndex, Vector3 localPos)
    {
        Vector3 worldPos = transform.TransformPoint(localPos);
        FlashHitLight(worldPos, slots[slotIndex].color);
        FlashPad(slots[slotIndex].lane);
    }

    /// The far end has gone through. The burst is fired AT THE HIT ZONE rather than at the note's
    /// own position: by now the note stretches back behind the camera, so its transform is not
    /// where the player is looking, and putting the punctuation there would mean firing it off
    /// screen. What remains of the strip then fades out where it is instead of sliding away.
    private void BeginHoldFinish(int slotIndex)
    {
        slots[slotIndex].holdFinishing = true;
        slots[slotIndex].holdFinishTimer = Mathf.Max(0.01f, holdFinishFade);

        Vector3 worldPos = transform.TransformPoint(
            new Vector3(tunnel.GetLaneCenterX(slots[slotIndex].lane), HeightForType(slots[slotIndex].type),
                        HitZoneZFor(slots[slotIndex].type)));

        float punch = IntensityScale(slots[slotIndex].intensity);
        PlayHitEffect(worldPos, slots[slotIndex].color);
        FlashHitLight(worldPos, slots[slotIndex].color, punch);
        FlashPad(slots[slotIndex].lane);
        shakeStrength = shakeMagnitude * punch;

        // Scored here, at the END of the sustain, not when it started: one hold is one note.
        CueScored?.Invoke(new CueHitInfo(slots[slotIndex].type, slots[slotIndex].lane,
            slots[slotIndex].intensity, Now, true));
    }

    private void UpdateHoldFinish(int slotIndex)
    {
        slots[slotIndex].holdFinishTimer -= Time.deltaTime;
        float t = Mathf.Clamp01(slots[slotIndex].holdFinishTimer / Mathf.Max(0.01f, holdFinishFade));

        // A last flare that decays as it disappears, so the note goes out on a flash rather than
        // simply ceasing to exist. Progress is 0, not 1: the body has already been consumed down
        // to nothing by the time this runs, and what is left to fade is the HEAD - which a
        // progress of 1 would snap off instantly instead of taking out with the flare.
        PushHoldShader(slotIndex, 0f, Mathf.Lerp(1f, holdFinishFlash, t), t);
        SetHoldIconAlpha(slotIndex, t);

        if (slots[slotIndex].holdFinishTimer > 0f) return;

        slots[slotIndex].holdFinishing = false;
        slots[slotIndex].inUse = false;
        slots[slotIndex].transform.gameObject.SetActive(false);
    }

    /// One property-block write per hold renderer. Going through a block rather than the shared
    /// material is what lets every pooled note be at a different point in its own fade without
    /// any of them needing a material instance of its own.
    private void PushHoldShader(int slotIndex, float progress, float boost, float alpha)
    {
        var slot = slots[slotIndex];
        if (slot.holdStripRenderer != null)
        {
            slot.holdStripRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat("_HoldProgress", progress);
            mpb.SetFloat("_HoldSoftness", holdFadeSoftness);
            mpb.SetFloat("_HoldBoost", boost);
            mpb.SetFloat("_HoldAlpha", alpha);
            mpb.SetFloat("_HoldGradient", 1f);
            slot.holdStripRenderer.SetPropertyBlock(mpb);
        }

        if (slot.holdHeadRenderers == null) return;

        // The head is compact — it occupies one point along the note, not a span — so it takes no
        // gradient and fades as a whole, on the same schedule the sweep passes over it.
        float headAlpha = alpha * (1f - Mathf.Clamp01(progress / Mathf.Max(0.0001f, holdFadeSoftness * 2f)));
        for (int i = 0; i < slot.holdHeadRenderers.Length; i++)
        {
            var mr = slot.holdHeadRenderers[i];
            if (mr == null) continue;
            mr.GetPropertyBlock(mpb);
            mpb.SetFloat("_HoldProgress", 0f);
            mpb.SetFloat("_HoldBoost", boost);
            mpb.SetFloat("_HoldAlpha", headAlpha);
            mpb.SetFloat("_HoldGradient", 0f);
            mr.SetPropertyBlock(mpb);
        }
    }

    /// The icon is an unlit black sticker on its own material, so it fades through its colour's
    /// alpha rather than the hold shader's.
    private void SetHoldIconAlpha(int slotIndex, float alpha)
    {
        var icon = slots[slotIndex].holdIcon;
        if (icon == null) return;

        icon.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", new Color(0f, 0f, 0f, Mathf.Clamp01(alpha)));
        icon.SetPropertyBlock(mpb);
    }

    /// An obstacle reaching the hit zone is not the end of it. Unlike a note, which is spent on
    /// contact, the thing keeps coming and the player is still inside the movement it asked for
    /// - so this fires the punctuation and leaves the object alive to finish going past. Update's
    /// tail test retires it once its far end is behind the camera.
    ///
    /// No pad flash, deliberately: the floor pads mark where NOTES are struck, and lighting one
    /// for a wall that spans two lanes would say a note landed there.
    private void OnObstaclePassed(int slotIndex, Vector3 localPos)
    {
        // At the hit zone rather than at the object's own centre: a wall's centre is four metres
        // further down the road, so a burst there fires behind where the player is looking.
        Vector3 worldPos = transform.TransformPoint(new Vector3(localPos.x, ObstacleBurstHeight(slots[slotIndex].type), hitZoneZ));

        float punch = IntensityScale(slots[slotIndex].intensity);
        PlayHitEffect(worldPos, slots[slotIndex].color);
        FlashHitLight(worldPos, slots[slotIndex].color, punch);
        shakeStrength = shakeMagnitude * punch * obstacleShakeScale;

        CueScored?.Invoke(new CueHitInfo(slots[slotIndex].type, slots[slotIndex].lane,
            slots[slotIndex].intensity, Now, false));
    }

    /// Roughly the middle of the obstacle's own face, so the debris comes off the thing rather
    /// than out of the floor under it.
    private float ObstacleBurstHeight(CueType type)
    {
        switch (type)
        {
            case CueType.Dodge: return dodgeWallHeight * 0.5f;
            case CueType.Curb: return curbHeight * 0.5f;
            default: return duckBeamBottom + duckBeamHeight * 0.5f;
        }
    }

    private void OnCueHit(int slotIndex, Vector3 localPos)
    {
        Vector3 worldPos = transform.TransformPoint(localPos);
        float punch = IntensityScale(slots[slotIndex].intensity);
        PlayHitEffect(worldPos, slots[slotIndex].color);
        FlashHitLight(worldPos, slots[slotIndex].color, punch);
        FlashPad(slots[slotIndex].lane);
        shakeStrength = shakeMagnitude * punch;

        slots[slotIndex].inUse = false;
        slots[slotIndex].transform.gameObject.SetActive(false);

        CueScored?.Invoke(new CueHitInfo(slots[slotIndex].type, slots[slotIndex].lane,
            slots[slotIndex].intensity, Now, false));
    }

    // --- Hit-zone floor pads ---

    private void BuildHitPads()
    {
        padMat = CreateNeonMaterial(padIdleColor, padIdleEmissionIntensity);
        int n = Mathf.Max(1, tunnel.LaneCount);
        padRenderers = new MeshRenderer[n][];
        padFlashTimers = new float[n];
        float laneWidth = tunnel.FloorWidth / n;
        float padSize = laneWidth * padSizeRatio;

        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("HitPad_" + i);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(tunnel.GetLaneCenterX(i), 0.003f, hitZoneZ);

            padRenderers[i] = BuildOutlineRect(go.transform, padSize, padSize, padOutlineThickness, padMat);
        }
    }

    // A thin rectangular border (4 flat bars), not a filled quad — reads as a floor marker
    // outline instead of a solid block of color.
    private MeshRenderer[] BuildOutlineRect(Transform parent, float width, float depth, float lineThickness, Material mat)
    {
        float hw = width * 0.5f;
        float hd = depth * 0.5f;
        var renderers = new MeshRenderer[4];

        renderers[0] = CreateFlatBar(parent, new Vector3(0f, 0f, hd), new Vector3(width, 0.02f, lineThickness), mat);
        renderers[1] = CreateFlatBar(parent, new Vector3(0f, 0f, -hd), new Vector3(width, 0.02f, lineThickness), mat);
        renderers[2] = CreateFlatBar(parent, new Vector3(hw, 0f, 0f), new Vector3(lineThickness, 0.02f, depth), mat);
        renderers[3] = CreateFlatBar(parent, new Vector3(-hw, 0f, 0f), new Vector3(lineThickness, 0.02f, depth), mat);
        return renderers;
    }

    private MeshRenderer CreateFlatBar(Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
    {
        var go = new GameObject("Bar");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        return AttachCube(go, mat);
    }

    private void FlashPad(int lane)
    {
        if (padFlashTimers == null || lane < 0 || lane >= padFlashTimers.Length) return;
        padFlashTimers[lane] = padFlashDuration;
    }

    private void UpdateHitPads()
    {
        for (int i = 0; i < padFlashTimers.Length; i++)
        {
            if (padFlashTimers[i] <= 0f) continue;

            padFlashTimers[i] -= Time.deltaTime;
            float t = Mathf.Clamp01(padFlashTimers[i] / padFlashDuration);
            Color c = Color.Lerp(padIdleColor, padFlashColor, t);
            float emission = Mathf.Lerp(padIdleEmissionIntensity, padFlashEmissionIntensity, t);

            mpb.SetColor("_BaseColor", c * 0.3f);
            mpb.SetColor("_EmissionColor", c * Mathf.Clamp(emission, 0f, MaxEmissionIntensity));
            for (int b = 0; b < padRenderers[i].Length; b++)
                padRenderers[i][b].SetPropertyBlock(mpb);
        }
    }

    // --- Hit effect (pooled particle bursts) ---

    private void BuildHitEffects()
    {
        // One chamfered unit cube shared by every particle; each particle scales it by its own
        // start size, so the debris matches the cue's faceted low-poly look instead of being
        // round sprites.
        Mesh debrisMesh = RoundedCubeMeshBuilder.Build(Vector3.one, 0.18f);

        hitEffects = new ParticleSystem[hitEffectPoolSize];
        // A material per pooled system rather than one shared: the burst's color is written into
        // _BaseColor as an HDR value so the debris blooms like the cue it came from, and a shared
        // material would repaint every burst still in flight whenever a new one started.
        hitEffectMats = new Material[hitEffectPoolSize];

        for (int i = 0; i < hitEffectPoolSize; i++)
        {
            Material burstMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            burstMat.SetColor("_BaseColor", Color.white);
            hitEffectMats[i] = burstMat;

            var go = new GameObject("HitBurst_" + i);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // a freshly added system starts playing; main.duration can't be set until it's stopped

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = hitEffectLifetime;
            main.startLifetime = hitEffectLifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(hitEffectSpeed * 0.45f, hitEffectSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(hitEffectMinSize, hitEffectMaxSize);
            main.gravityModifier = 1.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = Color.white;
            // Radians, not degrees — ParticleSystem angles are radians everywhere in script.
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)hitEffectParticleCount) });

            // Spread over roughly the plate's own footprint, so the debris looks like the plate
            // came apart rather than like something exploded from a single point behind it.
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.45f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            var sizeCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.55f, 0.75f), new Keyframe(1f, 0f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // Tumbling: without this the cubes fly as a rigid, obviously-identical set.
            var rotationOverLifetime = ps.rotationOverLifetime;
            rotationOverLifetime.enabled = true;
            rotationOverLifetime.separateAxes = true;
            rotationOverLifetime.x = new ParticleSystem.MinMaxCurve(-7f, 7f);
            rotationOverLifetime.y = new ParticleSystem.MinMaxCurve(-7f, 7f);
            rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-7f, 7f);

            var psRenderer = ps.GetComponent<ParticleSystemRenderer>();
            psRenderer.material = burstMat;
            psRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            psRenderer.mesh = debrisMesh;
            psRenderer.alignment = ParticleSystemRenderSpace.World;
            psRenderer.shadowCastingMode = ShadowCastingMode.Off;
            psRenderer.receiveShadows = false;

            hitEffects[i] = ps;
        }
    }

    // --- Hit flash (pooled point lights) ---

    private void BuildHitLights()
    {
        int count = Mathf.Max(0, hitLightPoolSize);
        hitLights = new Light[count];
        hitLightTimers = new float[count];

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("HitFlash_" + i);
            go.transform.SetParent(transform, false);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = hitLightRange;
            light.intensity = 0f;
            // No shadows: this is a sub-quarter-second pop on emissive scenery, and shadow maps
            // for four flickering point lights would cost far more than the effect is worth.
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            go.SetActive(false);

            hitLights[i] = light;
        }
    }

    /// Maps a chart's 0..1 onset strength onto a multiplier for the hit feedback. It never
    /// reaches zero: a quiet note still has to register as a hit, it just should not punch as
    /// hard as the loudest one in the track.
    private float IntensityScale(float intensity)
    {
        return Mathf.Lerp(intensityFeedbackFloor, 1f, Mathf.Clamp01(intensity));
    }

    private void FlashHitLight(Vector3 worldPos, Color color, float scale = 1f)
    {
        if (hitLights == null || hitLights.Length == 0) return;

        int index = nextHitLight;
        nextHitLight = (nextHitLight + 1) % hitLights.Length;

        Light light = hitLights[index];
        light.transform.position = worldPos;
        light.color = color;
        light.intensity = hitLightIntensity * scale;
        light.range = hitLightRange;
        light.gameObject.SetActive(true);
        hitLightTimers[index] = hitLightDuration;

        if (tunnel != null)
            tunnel.FlashNear(worldPos, color, hitRingFlashRadius, hitRingFlashStrength, hitLightDuration);
    }

    private void UpdateHitLights()
    {
        if (hitLights == null) return;

        for (int i = 0; i < hitLights.Length; i++)
        {
            if (hitLightTimers[i] <= 0f) continue;

            hitLightTimers[i] -= Time.deltaTime;
            float t = Mathf.Clamp01(hitLightTimers[i] / hitLightDuration);
            // Squared falloff: a linear fade reads as a lingering glow rather than a strike.
            hitLights[i].intensity = hitLightIntensity * t * t;

            if (hitLightTimers[i] <= 0f)
            {
                hitLightTimers[i] = 0f;
                hitLights[i].intensity = 0f;
                hitLights[i].gameObject.SetActive(false);
            }
        }
    }

    private void PlayHitEffect(Vector3 worldPos, Color color)
    {
        if (hitEffects.Length == 0) return;

        int index = nextHitEffect;
        nextHitEffect = (nextHitEffect + 1) % hitEffects.Length;
        ParticleSystem ps = hitEffects[index];

        // The color goes on the material as an HDR value rather than into startColor: particle
        // vertex colors are 8-bit and clamp at 1, which would leave the debris dimmer than the
        // plate it broke off and skip the bloom entirely.
        hitEffectMats[index].SetColor("_BaseColor", color * Mathf.Clamp(hitEffectEmission, 0f, MaxEmissionIntensity));

        ps.transform.position = worldPos;
        ps.Play();
    }

    // --- Camera shake + sway ---

    // All THREE write the SAME transform, so they're combined into one offset here rather than
    // each independently setting localPosition (which would just have one silently overwrite the
    // others every frame). Sway runs continuously for a "flight" feel; shake is the brief,
    // decaying kick added on top of it when a cue is hit; the obstacle pose is the deliberate
    // lean/hop/duck around whatever is on the road.
    //
    // The obstacle offset goes on top of the other two rather than replacing them: a dodge that
    // switched off the flight sway would read as the camera being locked in place, which is the
    // opposite of what a body moving out of the way looks like.
    //
    // The obstacle pose is also the only thing here that touches ROTATION, and it is the only
    // reason this method writes localRotation at all - hence the cached base, so a scene that
    // aims the camera somewhere other than straight down the road keeps its aim.
    private void UpdateCameraShake()
    {
        if (cameraTransform == null) return;

        ObstacleCameraDirector.Pose pose = obstacleCamera.Evaluate(Now);
        Vector3 obstacleOffset = pose.offset;

        Vector3 sway = new Vector3(
            Mathf.Sin(Time.time * swayFrequencyX) * swayAmplitudeX,
            Mathf.Sin(Time.time * swayFrequencyY + 1.3f) * swayAmplitudeY,
            0f);

        Vector3 shakeOffset = Vector3.zero;
        if (shakeStrength > 0.0005f)
        {
            float n = Time.time * shakeFrequency;
            shakeOffset = new Vector3(Mathf.PerlinNoise(n, 0.37f) * 2f - 1f,
                                      Mathf.PerlinNoise(0.71f, n) * 2f - 1f, 0f) * shakeStrength;
            // Exponential, so how fast it dies does not depend on the frame rate either.
            shakeStrength *= Mathf.Exp(-shakeDecay * Time.deltaTime);
        }

        Vector3 placed = cameraBasePos + obstacleOffset + sway + shakeOffset;
        placed.y = Mathf.Max(placed.y, cameraMinHeight);
        cameraTransform.localPosition = placed;
        cameraTransform.localRotation = cameraBaseRot * Quaternion.Euler(0f, 0f, pose.roll);
    }

    // --- Cue visuals ---

    // Bakes the three meshes each chevron cue is assembled from, for both orientations. Punch
    // keeps the plate upright (thickness along Z, the axis it travels); Kick rotates the same
    // construction onto the floor, which is why its plate/frame swap the Y and Z extents while
    // the tail — which tapers along Z either way — keeps its own axis order.
    private void BuildChevronMeshes(float kickWidth)
    {
        float pw = punchPlateWidth;
        float ph = punchPlateHeight;
        float chamfer = Mathf.Min(pw, ph) * cueCornerChamferRatio;
        float frameChamfer = chamfer * 1.05f;

        punchPlateMesh = RoundedCubeMeshBuilder.Build(new Vector3(pw, ph, plateThickness), chamfer);
        punchFrameMesh = RoundedCubeMeshBuilder.Build(new Vector3(pw + plateBorder * 2f, ph + plateBorder * 2f, frameThickness), frameChamfer);
        // The one mesh every hold note is drawn with. A unit cube, stretched along Z per instance
        // by BuildHoldStrip/SetupHoldInstance — safe to stretch precisely because hold geometry
        // carries no chamfer, and the reason a note of any length is a single seamless surface.
        unitCubeMesh = RoundedCubeMeshBuilder.Build(Vector3.one, 0f);

        // Kick lies flat: its "height" is the plate's floor-hugging thickness and its "depth"
        // along the lane is what the punch plate used for height. Unlike Punch it is a plain
        // rectangular slab — square corners, no chamfer, no tapering tail. The chevron/arrow
        // silhouette is Punch's alone: Punch flies at chest height where the taper reads as a
        // direction-of-travel pointer, while on the floor the same taper just made the Kick pad
        // look like a triangular wedge aimed at the player instead of a pad to stomp.
        float kw = kickWidth;
        float kd = kickDepth;

        kickPlateMesh = RoundedCubeMeshBuilder.Build(new Vector3(kw, kickThickness, kd), 0f);
        kickFrameMesh = RoundedCubeMeshBuilder.Build(new Vector3(kw + plateBorder * 2f, kickThickness * 0.8f, kd + plateBorder * 2f), 0f);

        // KickHold is the same floor slab laid ALONG the lane instead of across it: one strip,
        // the lane's width, stretched to the note's length. Its footprint is sized off the SHORT
        // side — the lane width, or one cell if that is tighter — so the drawing never stretches.
        kickHoldWidth = kw;
        float holdIcon = Mathf.Min(kw, holdCellSize) * iconPlateCoverage;
        kickHoldIconQuad = BuildIconQuad(holdIcon, faceUp: true);

        // The icons are square, sized off each plate's shorter side so the drawing never has to
        // stretch: Punch's is upright on the plate's front face, Kick's lies flat on its top
        // face with the toe pointing away down the lane.
        float punchIcon = Mathf.Min(pw, ph) * iconPlateCoverage;
        float kickIcon = Mathf.Min(kw, kd) * iconPlateCoverage;
        punchIconQuad = BuildIconQuad(punchIcon, faceUp: false);
        kickIconQuad = BuildIconQuad(kickIcon, faceUp: true);
    }

    /// A single square facing the camera (-Z) or the sky (+Y), UV-mapped so the icon texture
    /// reads upright: for the floor version "up" in the texture points down the lane (+Z).
    private static Mesh BuildIconQuad(float size, bool faceUp)
    {
        float h = size * 0.5f;
        var mesh = new Mesh { name = faceUp ? "CueIconQuad_Floor" : "CueIconQuad_Upright" };

        Vector3[] verts = faceUp
            ? new[] { new Vector3(-h, 0f, h), new Vector3(h, 0f, h), new Vector3(h, 0f, -h), new Vector3(-h, 0f, -h) }
            : new[] { new Vector3(-h, h, 0f), new Vector3(h, h, 0f), new Vector3(h, -h, 0f), new Vector3(-h, -h, 0f) };

        Vector3 normal = faceUp ? Vector3.up : Vector3.back;
        mesh.SetVertices(verts);
        mesh.SetNormals(new[] { normal, normal, normal, normal });
        mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f) });
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// What one pooled cue object is made of. A struct rather than a tuple because holds need to
    /// hand back four extra pieces and a six-slot tuple stops being readable at the call site.
    private struct CueVisual
    {
        public Transform root;
        public MeshRenderer[] bright;  // carries the lane colour
        public MeshRenderer[] frame;   // carries the darker border tint
        public Transform strip;        // hold only: the stretched single mesh
        public MeshRenderer stripRenderer;
        public float stripZStart;   // where the strip begins along the note, before its length is applied
        public MeshRenderer[] head;    // hold only: parts at the near end, faded as one
        public MeshRenderer icon;      // hold only: the sticker, faded through its own colour
        public MeshRenderer[] fill;    // obstacle only: the translucent body
        public MeshRenderer[] chevron; // obstacle only: the contrasting direction marks
        public Transform[] chevronRoots; // obstacle only: re-aimed per instance (a Dodge wall)
    }

    private CueVisual CreateCueVisual(CueType type)
    {
        switch (type)
        {
            case CueType.Kick:
            {
                var root = new GameObject("Cue_Kick");
                root.transform.SetParent(transform, false);

                // Frame sits a hair below and around the plate; the plate rides on top of it so
                // the darker border is visible as an outline from an elevated camera. No tail:
                // see BuildChevronMeshes — Kick is a plain slab, only Punch keeps the chevron.
                var frame = CreateChevronPart(root.transform, "Frame", kickFrameMesh, new Vector3(0f, -kickThickness * 0.22f, 0f));
                var plate = CreateChevronPart(root.transform, "Plate", kickPlateMesh, Vector3.zero);
                AttachIcon(root.transform, kickIconQuad, footIconMat, new Vector3(0f, kickThickness * 0.5f + IconLift, 0f));

                return new CueVisual { root = root.transform, bright = new[] { plate }, frame = new[] { frame } };
            }
            case CueType.Punch:
            {
                var root = new GameObject("Cue_Punch");
                root.transform.SetParent(transform, false);

                // Local -Z is toward the camera, so the plate is pushed forward out of the frame.
                // No tail: a one-shot Punch is just the square sign-plate. The tapering chevron
                // it used to trail read as an arrow rather than as part of the sign, and the
                // sustained note now carries the only long shape in the cue vocabulary, so
                // keeping a short stub here would just blur what "long" means to the player.
                float plateZ = -(frameThickness * 0.5f + plateThickness * 0.5f - plateThickness * 0.55f);
                var frame = CreateChevronPart(root.transform, "Frame", punchFrameMesh, Vector3.zero);
                var plate = CreateChevronPart(root.transform, "Plate", punchPlateMesh, new Vector3(0f, 0f, plateZ));
                AttachIcon(root.transform, punchIconQuad, fistIconMat, new Vector3(0f, 0f, plateZ - plateThickness * 0.5f - IconLift));

                return new CueVisual { root = root.transform, bright = new[] { plate }, frame = new[] { frame } };
            }
            case CueType.PunchHold:
            {
                // The airborne twin of KickHold: the Punch plate, border and fist icon at the near
                // end as the note's head, then one unbroken beam trailing away behind it — just at
                // chest height instead of on the floor.
                //
                // The beam does NOT taper. A tapering wedge was the first attempt, but it is the
                // same arrow silhouette that was taken off the one-shot Punch, and on a sustained
                // note it also made the far half read as "smaller" rather than "further".
                var root = new GameObject("Cue_PunchHold");
                root.transform.SetParent(transform, false);

                // Every part is on the hold material, head included: when the sweep reaches the
                // head it has to be able to fade away like everything else, and the opaque cue
                // shader has no alpha to fade.
                float plateZ = -(frameThickness * 0.5f + plateThickness * 0.5f - plateThickness * 0.55f);
                var frame = CreateChevronPart(root.transform, "Frame", punchFrameMesh, Vector3.zero, cueHoldMat);
                var plate = CreateChevronPart(root.transform, "Plate", punchPlateMesh, new Vector3(0f, 0f, plateZ), cueHoldMat);

                // The beam starts behind the border's rear face so the two never interpenetrate.
                float beamScale = Mathf.Max(1f, holdBeamPlateScale);
                var (strip, stripRenderer, stripZ) = BuildHoldStrip(
                    root.transform, punchPlateWidth * beamScale, punchPlateHeight * beamScale, frameThickness * 0.5f);

                var icon = AttachIcon(root.transform, punchIconQuad, fistIconMat, new Vector3(0f, 0f, plateZ - plateThickness * 0.5f - IconLift));

                // Plate and beam both wear the full lane colour: on a hold the sustained beam is
                // what the player tracks, so it must not be dimmed to a border tint.
                return new CueVisual
                {
                    root = root.transform,
                    bright = new[] { plate, stripRenderer },
                    frame = new[] { frame },
                    strip = strip,
                    stripRenderer = stripRenderer,
                    stripZStart = stripZ,
                    head = new[] { plate, frame },
                    icon = icon,
                };
            }
            case CueType.KickHold:
            {
                // A single unbroken strip lying ALONG the lane rather than across it. The root
                // sits at its NEAR edge, not its centre, so the moment the root reaches the hit
                // zone is the moment the hold starts — the same "arrival" instant the one-shot
                // cues use, which keeps the timing in Update honest without a special case.
                //
                // No separate border object here: a second, almost coplanar surface under a strip
                // that fades would blend against it along its whole length, and the darker tint
                // is exactly the thing that must never linger once a stretch has been played.
                var root = new GameObject("Cue_KickHold");
                root.transform.SetParent(transform, false);

                var (strip, stripRenderer, stripZ) = BuildHoldStrip(root.transform, kickHoldWidth, kickThickness, 0f);

                // A single footprint, near the start: it marks where the hold begins. Repeating
                // it down the length made every stretch look like its own separate cue.
                var icon = AttachIcon(root.transform, kickHoldIconQuad, footIconMat,
                    new Vector3(0f, kickThickness * 0.5f + IconLift, holdCellSize * 0.5f));

                return new CueVisual
                {
                    root = root.transform,
                    bright = new[] { stripRenderer },
                    frame = System.Array.Empty<MeshRenderer>(),
                    strip = strip,
                    stripRenderer = stripRenderer,
                    stripZStart = stripZ,
                    head = System.Array.Empty<MeshRenderer>(),
                    icon = icon,
                };
            }
            case CueType.Dodge:
            {
                // A wall down one side of the road, taller than a person so it cannot be jumped,
                // and long enough that getting out of its way is a move you hold rather than a
                // flinch. Two large chevrons on its front face say which way the free half is;
                // they are re-aimed at spawn, so this one object serves a left or a right wall.
                DodgeWallBox(out Vector3 min, out Vector3 max);
                float wallWidth = max.x - min.x;
                // Side by side, not stacked. Two sideways chevrons one above the other meet at
                // their open ends and the pair reads as a single zigzag; side by side they are
                // the ordinary double-arrow everyone already knows means "go this way", and it
                // survives being shrunk to a few dozen pixels at the far end of the tunnel.
                float chevronSpan = dodgeWallHeight * 0.5f;
                float chevronDepth = wallWidth * 0.22f;
                var centers = new[]
                {
                    new Vector2(-wallWidth * 0.18f, dodgeWallHeight * 0.5f),
                    new Vector2(wallWidth * 0.18f, dodgeWallHeight * 0.5f),
                };
                return BuildObstacleVisual("Cue_DodgeWall", min, max, centers,
                                           chevronSpan, chevronDepth, DodgeChevronAngle(1));
            }
            case CueType.Curb:
            {
                // A low kerb right across the road. Three chevrons pointing up, spread over the
                // full width, because the movement it asks for is the same wherever you stand.
                CurbBox(out Vector3 min, out Vector3 max);
                var centers = ChevronRow(3, max.x - min.x, curbHeight * 0.5f);
                return BuildObstacleVisual("Cue_Curb", min, max, centers,
                                           (max.x - min.x) / 6f, curbHeight * 0.66f, 180f);
            }
            case CueType.Duck:
            {
                // A beam across the road at head height, with the passage underneath left clear.
                // Its chevrons point down: the instruction is "get below this", not "avoid it".
                DuckBox(out Vector3 min, out Vector3 max);
                var centers = ChevronRow(3, max.x - min.x, duckBeamBottom + duckBeamHeight * 0.5f);
                return BuildObstacleVisual("Cue_DuckBarrier", min, max, centers,
                                           (max.x - min.x) / 6f, duckBeamHeight * 0.62f, 0f);
            }
            default:
            {
                // Only reachable if a type is added to CueTypes without a case here. Loud, and
                // still returns something the pool can hold, rather than throwing halfway
                // through Build and leaving the tunnel half-made.
                Debug.LogError("[CueSpawner] no visual for cue type " + type + "; it will spawn as nothing.", this);
                var root = new GameObject("Cue_" + type);
                root.transform.SetParent(transform, false);
                return new CueVisual
                {
                    root = root.transform,
                    bright = System.Array.Empty<MeshRenderer>(),
                    frame = System.Array.Empty<MeshRenderer>(),
                };
            }
        }
    }

    // --- Obstacle geometry --------------------------------------------------------------
    //
    // One recipe, three sets of numbers. Every obstacle is a box given as its local min/max
    // corner, and it is drawn three times over: as twelve neon beams along its edges, as one
    // translucent slab filling it, and as chevrons standing a hair proud of the face that meets
    // the camera. The chevrons are the instruction and everything else is the object, which is
    // why they are the only part that carries the contrasting accent.
    //
    // Box coordinates are all relative to the NEAR edge at z = 0 and to the obstacle's own
    // centre line in x. See the CueType comment for why the near edge is the anchor.

    /// The wall, built CENTRED on x = 0 rather than on the side it will stand on. Which side it
    /// takes is then one number at spawn - the root's x - and one angle on its chevrons, so a
    /// single pooled object serves both directions. Mirroring the object instead (scale.x = -1)
    /// would flip its triangle winding and cull away every front face.
    private void DodgeWallBox(out Vector3 min, out Vector3 max)
    {
        float cover = tunnel.FloorWidth * dodgeWallCoverRatio;
        min = new Vector3(-cover * 0.5f, 0f, 0f);
        max = new Vector3(cover * 0.5f, dodgeWallHeight, dodgeWallLength);
    }

    /// Which way a wall's chevrons point, given the side it stands on: away from the wall, into
    /// the half of the road that is still free. The canonical chevron points -Y, so +90 aims it
    /// at +X and -90 at -X.
    private static float DodgeChevronAngle(int side) => side > 0 ? -90f : 90f;

    /// Evenly spaced chevron centres across a face of the given width, all at one height.
    private static Vector2[] ChevronRow(int count, float width, float y)
    {
        var centers = new Vector2[count];
        float step = width / (count + 1);
        for (int i = 0; i < count; i++) centers[i] = new Vector2(-width * 0.5f + step * (i + 1), y);
        return centers;
    }

    /// Centre x of a wall standing against the given side of the road: -1 left, +1 right.
    private float DodgeWallCenterX(int side)
    {
        float half = tunnel.FloorWidth * 0.5f;
        float cover = tunnel.FloorWidth * dodgeWallCoverRatio;
        return side * (half - cover * 0.5f);
    }

    private void CurbBox(out Vector3 min, out Vector3 max)
    {
        float half = tunnel.FloorWidth * 0.5f * curbWidthRatio;
        min = new Vector3(-half, 0f, 0f);
        max = new Vector3(half, curbHeight, curbDepth);
    }

    private void DuckBox(out Vector3 min, out Vector3 max)
    {
        float half = tunnel.FloorWidth * 0.5f * duckBeamWidthRatio;
        min = new Vector3(-half, duckBeamBottom, 0f);
        max = new Vector3(half, duckBeamBottom + duckBeamHeight, duckBeamDepth);
    }

    /// How far back along +Z an obstacle of this type reaches. The camera director needs it to
    /// know how long the thing takes to go past, and Update needs it to know when it is spent.
    private float ObstacleZExtent(CueType type)
    {
        switch (type)
        {
            case CueType.Dodge: return dodgeWallLength;
            case CueType.Curb: return curbDepth;
            default: return duckBeamDepth;
        }
    }

    /// The twelve edges of a box, as beams. Built from the corner pairs rather than written out
    /// by hand so there is no chance of a missing or duplicated edge when a size changes.
    private MeshRenderer[] CreateBoxEdges(Transform parent, Vector3 min, Vector3 max, float thickness, Material mat)
    {
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
            corners[i] = new Vector3((i & 1) == 0 ? min.x : max.x,
                                     (i & 2) == 0 ? min.y : max.y,
                                     (i & 4) == 0 ? min.z : max.z);

        // Each pair differs in exactly one bit, i.e. runs along exactly one axis.
        var pairs = new (int, int)[]
        {
            (0,1),(2,3),(4,5),(6,7),   // along X
            (0,2),(1,3),(4,6),(5,7),   // along Y
            (0,4),(1,5),(2,6),(3,7),   // along Z
        };

        var beams = new MeshRenderer[pairs.Length];
        for (int i = 0; i < pairs.Length; i++)
            beams[i] = CreateBeam(parent, corners[pairs[i].Item1], corners[pairs[i].Item2], thickness, mat);
        return beams;
    }

    /// The translucent body. Inset by half an edge so the beams stand proud of it instead of
    /// z-fighting along every edge of the box.
    private MeshRenderer CreateBoxFill(Transform parent, Vector3 min, Vector3 max, Material mat)
    {
        Vector3 inset = Vector3.one * (obstacleEdgeThickness * 0.5f);
        Vector3 lo = min + inset;
        Vector3 hi = max - inset;
        Vector3 size = Vector3.Max(hi - lo, Vector3.one * 0.01f);

        var go = new GameObject("Fill");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = (lo + hi) * 0.5f;
        go.transform.localScale = size;
        return AttachMesh(go, unitCubeMesh, mat);
    }

    // How far a chevron floats off the face it is drawn on. Same reasoning as IconLift: enough
    // to win the depth test against the translucent fill behind it, little enough that it does
    // not visibly detach from the face at a glancing angle.
    private const float ChevronLift = 0.05f;

    /// One chevron: two beams meeting at an apex, lying in the plane of the face it is drawn on.
    ///
    /// Authored once pointing -Y (a "v") and turned into the other three by a rotation about Z,
    /// so the up, left and right forms are the same object at 180, -90 and +90 degrees and
    /// cannot drift apart in shape. `span` is the distance between the two open ends, `depth`
    /// how far the apex reaches from them. The returned transform is kept so a Dodge wall can
    /// re-aim its chevrons at spawn, which is what lets one pooled wall serve both sides.
    private (Transform root, MeshRenderer[] beams) CreateChevron(Transform parent, Vector2 center, float zFace,
                                                                 float span, float depth, float angleDeg, Material mat)
    {
        var go = new GameObject("Chevron");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(center.x, center.y, zFace);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg);

        Vector3 apex = new Vector3(0f, -depth * 0.5f, 0f);
        Vector3 left = new Vector3(-span * 0.5f, depth * 0.5f, 0f);
        Vector3 right = new Vector3(span * 0.5f, depth * 0.5f, 0f);

        var beams = new[]
        {
            CreateBeam(go.transform, apex, left, obstacleChevronThickness, mat),
            CreateBeam(go.transform, apex, right, obstacleChevronThickness, mat),
        };
        return (go.transform, beams);
    }

    /// Assembles one obstacle from its box and its chevrons. The chevrons always sit on the face
    /// the camera meets first (local z = 0, lifted toward it), because that is the only face a
    /// player has time to read: by the time a side face is square-on, the obstacle is level with
    /// them and the instruction has come too late to act on.
    private CueVisual BuildObstacleVisual(string name, Vector3 min, Vector3 max,
                                          Vector2[] chevronCenters, float span, float depth, float angleDeg)
    {
        var root = new GameObject(name);
        root.transform.SetParent(transform, false);

        var edges = CreateBoxEdges(root.transform, min, max, obstacleEdgeThickness, cueBaseMat);
        var fill = CreateBoxFill(root.transform, min, max, obstacleFillMat);

        var chevronRoots = new Transform[chevronCenters.Length];
        var chevrons = new List<MeshRenderer>();
        for (int i = 0; i < chevronCenters.Length; i++)
        {
            var built = CreateChevron(root.transform, chevronCenters[i], -ChevronLift,
                                      span, depth, angleDeg, cueBaseMat);
            chevronRoots[i] = built.root;
            chevrons.AddRange(built.beams);
        }

        return new CueVisual
        {
            root = root.transform,
            bright = edges,
            frame = System.Array.Empty<MeshRenderer>(),
            head = System.Array.Empty<MeshRenderer>(),
            fill = new[] { fill },
            chevron = chevrons.ToArray(),
            chevronRoots = chevronRoots,
        };
    }

    // How far the icon quad floats off the plate face. Just enough to clear z-fighting; any more
    // and the drawing visibly detaches from the plate at a glancing angle.
    private const float IconLift = 0.006f;

    // Deliberately NOT returned as a color renderer: the icon is flat black at every cue color,
    // so ApplyCueColor must never touch it.
    private MeshRenderer AttachIcon(Transform parent, Mesh quad, Material mat, Vector3 localPos)
    {
        var go = new GameObject("Icon");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        return AttachMesh(go, quad, mat);
    }

    /// Lays holdMaxCells cell objects end to end along +Z from the cue's root, each carrying one
    /// plate and one frame. Returns them front-first, which is the order the hit zone consumes
    /// them in, so every later loop can just walk the array forwards.
    /// Builds the one unbroken mesh a hold note is drawn with: a unit cube on its own child
    /// object, which SetupHoldInstance then stretches along Z to whatever length this instance
    /// drew. A unit cube can be stretched freely because hold geometry carries no chamfer — the
    /// bevel is what made stretching unsafe for the plates — so one mesh covers every length
    /// without a seam, and the played part is a shader parameter rather than a cut in the model.
    private (Transform strip, MeshRenderer renderer, float zStart) BuildHoldStrip(
        Transform root, float width, float height, float zStart)
    {
        var go = new GameObject("Strip");
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0f, 0f, zStart);
        go.transform.localScale = new Vector3(width, height, 1f); // Z is set per instance on spawn

        var mr = AttachMesh(go, unitCubeMesh, cueHoldMat);
        return (go.transform, mr, zStart);
    }

    private MeshRenderer CreateChevronPart(Transform parent, string name, Mesh mesh, Vector3 localPos, Material mat = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        return AttachMesh(go, mesh, mat != null ? mat : cueBaseMat);
    }

    // Two-tone: the bright plate carries the cue's own accent color at a moderate emission, and
    // the border/tail carry a much darker blend of the same hue. The depth cue is that contrast,
    // not a bloom halo — so the rim term (a thin edge outline) is the only part pushed bright.
    // Jump is left on the original single-tone, higher-emission treatment: it's a neon hurdle
    // bar, not a sign-plate, and has no border or tail to contrast against.
    private void ApplyCueColor(CueSlot slot, Color color)
    {
        if (IsObstacle(slot.type)) { ApplyObstacleColor(slot); return; }

        // Every remaining note is a sign-plate. The single-tone, higher-emission treatment that
        // used to sit here was the hurdle bar's alone, and the hurdle is gone.
        float fill = Mathf.Clamp(plateEmissionIntensity, 0f, MaxEmissionIntensity);
        float rim = Mathf.Clamp(cueRimIntensity, 0f, MaxEmissionIntensity);

        mpb.SetColor("_BaseColor", color * 0.8f);
        mpb.SetColor("_EmissionColor", color * fill);
        mpb.SetColor("_RimColor", Color.Lerp(color, Color.white, 0.35f) * rim * 0.5f);
        for (int i = 0; i < slot.colorRenderers.Length; i++)
            slot.colorRenderers[i].SetPropertyBlock(mpb);

        if (slot.frameRenderers == null || slot.frameRenderers.Length == 0) return;

        Color frame = Color.Lerp(color, FrameTint, frameDarkness);
        mpb.SetColor("_BaseColor", frame * 0.7f);
        mpb.SetColor("_EmissionColor", frame * Mathf.Clamp(frameEmissionIntensity, 0f, MaxEmissionIntensity));
        mpb.SetColor("_RimColor", Color.Lerp(color, Color.white, 0.3f) * rim);
        for (int i = 0; i < slot.frameRenderers.Length; i++)
            slot.frameRenderers[i].SetPropertyBlock(mpb);
    }

    /// Three surfaces, three jobs. The edges draw the object, the fill says how much road it
    /// takes up without hiding what is behind it, and the chevrons carry the instruction in the
    /// other accent so they read at a distance where the box is still a smudge.
    private void ApplyObstacleColor(CueSlot slot)
    {
        float rim = Mathf.Clamp(cueRimIntensity, 0f, MaxEmissionIntensity);

        mpb.SetColor("_BaseColor", obstacleBodyColor * 0.15f);
        mpb.SetColor("_EmissionColor", obstacleBodyColor * Mathf.Clamp(obstacleEdgeEmission, 0f, MaxEmissionIntensity));
        mpb.SetColor("_RimColor", Color.Lerp(obstacleBodyColor, Color.white, 0.35f) * rim * 0.5f);
        for (int i = 0; i < slot.colorRenderers.Length; i++)
            slot.colorRenderers[i].SetPropertyBlock(mpb);

        if (slot.chevronRenderers != null)
        {
            mpb.SetColor("_BaseColor", obstacleChevronColor * 0.2f);
            mpb.SetColor("_EmissionColor", obstacleChevronColor * Mathf.Clamp(obstacleChevronEmission, 0f, MaxEmissionIntensity));
            mpb.SetColor("_RimColor", Color.Lerp(obstacleChevronColor, Color.white, 0.4f) * rim);
            for (int i = 0; i < slot.chevronRenderers.Length; i++)
                slot.chevronRenderers[i].SetPropertyBlock(mpb);
        }

        if (slot.fillRenderers == null) return;

        // The fill rides the hold shader with its length gradient switched off, so it is a flat
        // partial alpha rather than a wipe: the same depth-write-plus-clip transparency the hold
        // notes use, which is what keeps it sorting sanely against the rest of the scene.
        float fillEmission = Mathf.Clamp(obstacleFillEmission, 0f, MaxEmissionIntensity);
        for (int i = 0; i < slot.fillRenderers.Length; i++)
        {
            var mr = slot.fillRenderers[i];
            if (mr == null) continue;
            mr.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", obstacleBodyColor * 0.2f);
            mpb.SetColor("_EmissionColor", obstacleBodyColor * fillEmission);
            mpb.SetColor("_RimColor", Color.Lerp(obstacleBodyColor, Color.white, 0.25f) * fillEmission);
            mpb.SetFloat("_HoldProgress", 0f);
            mpb.SetFloat("_HoldGradient", 0f);
            mpb.SetFloat("_HoldBoost", 1f);
            mpb.SetFloat("_HoldAlpha", Mathf.Clamp01(obstacleFillAlpha));
            mr.SetPropertyBlock(mpb);
        }
    }

    private MeshRenderer AttachCube(GameObject go, Material mat)
    {
        return AttachMesh(go, cubeMesh, mat);
    }

    private MeshRenderer AttachMesh(GameObject go, Mesh mesh, Material mat)
    {
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    private MeshRenderer CreateBeam(Transform parent, Vector3 a, Vector3 b, float thickness, Material mat)
    {
        var go = new GameObject("Beam");
        go.transform.SetParent(parent, false);

        Vector3 edge = b - a;
        float length = edge.magnitude;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, edge.normalized);
        go.transform.localScale = new Vector3(thickness, length, thickness);

        return AttachCube(go, mat);
    }

    private Mesh GetSharedCubeMesh()
    {
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        return mesh;
    }

    private Material CreateNeonMaterial(Color color, float intensity)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color * 0.12f);
        mat.SetFloat("_Smoothness", 0.15f);
        mat.SetFloat("_Metallic", 0f);
        float clamped = Mathf.Clamp(intensity, 0f, MaxEmissionIntensity);
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mat.SetColor("_EmissionColor", color * clamped);
        return mat;
    }

    // The cue body's material: real Lit specular/diffuse never shows in this scene (zero Light
    // components everywhere except the desert theme's dim directional light), so instead of a
    // flat emissive block, this uses the custom volumetric shader — a fixed fake-light term to
    // shade the beveled faces plus a Fresnel rim glow along the silhouette — so the cube reads
    // as lit and dimensional purely from its own geometry/view angle. Property names match the
    // old CreateNeonMaterial exactly (_BaseColor/_EmissionColor), so ApplyCueColor's existing
    // per-instance MaterialPropertyBlock coloring keeps working unchanged.
    private Material CreateVolumetricMaterial(Color color, float intensity)
    {
        var mat = new Material(Shader.Find("Custom/CueVolumetric"));
        mat.SetColor("_BaseColor", color * 0.12f);
        float clamped = Mathf.Clamp(intensity, 0f, MaxEmissionIntensity);
        mat.SetColor("_EmissionColor", color * clamped);
        mat.SetColor("_RimColor", color * clamped);
        mat.SetVector("_FakeLightDir", new Vector4(0.4f, 0.8f, -0.4f, 0f));
        mat.SetFloat("_FakeLightIntensity", 0.6f);
        mat.SetFloat("_RimPower", 2.2f);
        mat.SetFloat("_RimIntensity", 1.3f);
        return mat;
    }

    // Plain unlit alpha-blended black: no emission and no rim, unlike every other cue surface.
    // The linework's only job is to be the darkest thing on the plate, and anything that made it
    // glow (or let it pick up the plate's glow) would soften exactly the contrast it exists for.
    /// Same look as the opaque cue material, but on the shader that can fade along its length.
    /// The two are kept as separate shaders on purpose: one-shot Kick/Punch/Jump stay on the
    /// opaque path they have always used, so nothing about them can change here.
    private Material CreateHoldMaterial(float intensity)
    {
        var mat = new Material(Shader.Find("Custom/CueHoldStrip"));
        float clamped = Mathf.Clamp(intensity, 0f, MaxEmissionIntensity);
        mat.SetColor("_BaseColor", Color.white * 0.12f);
        mat.SetColor("_EmissionColor", Color.white * clamped);
        mat.SetColor("_RimColor", Color.white * clamped);
        mat.SetVector("_FakeLightDir", new Vector4(0.4f, 0.8f, -0.4f, 0f));
        mat.SetFloat("_FakeLightIntensity", 0.6f);
        mat.SetFloat("_RimPower", 2.2f);
        mat.SetFloat("_RimIntensity", 1.3f);
        mat.SetFloat("_HoldSoftness", holdFadeSoftness);
        mat.SetFloat("_HoldProgress", 0f);
        mat.SetFloat("_HoldBoost", 1f);
        mat.SetFloat("_HoldAlpha", 1f);
        return mat;
    }

    private Material CreateIconMaterial(Texture2D icon)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetTexture("_BaseMap", icon);
        // Forced to black rather than trusting the artwork's own RGB: PNGs exported with a white
        // matte keep white in the fully transparent pixels, and bilinear filtering drags that
        // white into the silhouette's edge as a pale halo. Taking only the alpha avoids it.
        mat.SetColor("_BaseColor", Color.black);

        mat.SetFloat("_Surface", 1f); // transparent
        mat.SetFloat("_Blend", 0f);   // alpha blend
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }
}
