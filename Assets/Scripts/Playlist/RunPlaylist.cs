using System;
using System.Collections.Generic;
using UnityEngine;

/// How the music gets from one segment of a playlist to the next.
public enum PlaylistTransition
{
    // The outgoing track fades out while the incoming one fades in over the whole transition.
    Crossfade,
    // The outgoing track dips away, a thunderclap and a flash land at the transition's middle,
    // and the incoming track starts on the clap.
    Thunder,
}

/// What a transition LOOKS like, independent of what the music does.
public enum PlaylistTransitionLook
{
    // The fog closes in and a veil covers the distance while the look is swapped under it.
    Veil,
    // A gust of wind: a vortex of leaves blows at the camera and covers the frame for a moment.
    Gust,
    // Rain comes in: the sun sinks, the sky clouds over, a downpour starts and a soft lightning
    // flash (and a distant rumble) lands on the transition's middle.
    Rain,
}

/// One track of a playlist: its music, its chart, its look, and how it is entered.
[Serializable]
public class PlaylistSegment
{
    public string label;
    public AudioClip track;
    // File name under StreamingAssets/Beatmaps, as for a RunProfile.
    public string beatmapFileName;
    public TunnelTheme theme;

    // The part of the track that plays, in seconds of the track. clipEnd 0 = to the end.
    [Min(0f)] public float clipStart;
    [Min(0f)] public float clipEnd;

    // How this segment is entered from the one before it (ignored for the first segment), and
    // how long the change takes - music and picture both.
    public PlaylistTransition transitionIn = PlaylistTransition.Crossfade;
    [Range(2f, 6f)] public float transitionSeconds = 3.5f;
    public PlaylistTransitionLook transitionLook = PlaylistTransitionLook.Veil;

    public float PlayedLength
    {
        get
        {
            float len = track != null ? track.length : 0f;
            float end = clipEnd > 0f ? Mathf.Min(clipEnd, len) : len;
            return Mathf.Max(0f, end - clipStart);
        }
    }
}

/// A video made of several tracks played back to back through ONE tunnel: one song clock, one
/// combo, one recording. Each segment brings its own chart and its own TunnelTheme; between two
/// segments the music crossfades (or a thunderclap cuts it) and the whole look - sky, fog, palette,
/// arches, scenery, post-processing, HUD - changes over the few seconds it takes to fly through a
/// gate. RunProfileSelector plays one of these instead of a RunProfile when it has one.
[CreateAssetMenu(menuName = "Tunnel/Run Playlist", fileName = "RunPlaylist")]
public class RunPlaylist : ScriptableObject
{
    // Names the recording.
    public string title = "Playlist";
    public List<PlaylistSegment> segments = new List<PlaylistSegment>();

    [Header("Start")]
    // As for a RunProfile: the tunnel starts before the music so the first note can fly in, and
    // the notes come from the very first bar.
    public bool preRoll = true;

    [Header("Transitions")]
    // No notes this many beats either side of a transition, on top of the transition itself.
    [Min(0f)] public float noteMarginBeats = 1f;
    // At most this many Dodge walls may stand inside a transition (the rest of the notes never do).
    [Min(0)] public int wallsInTransition = 1;
    // How far the fog closes in at the middle of a transition (metres to full fog), which is what
    // hides the scenery being swapped.
    [Min(5f)] public float veilFogEnd = 22f;
    // The thunder's flash, 0..1 of the screen.
    [Range(0f, 1f)] public float thunderFlash = 0.8f;
    [Range(0f, 1f)] public float thunderVolume = 1f;
    // Where the incoming track starts on a thunder transition, as a volume, rising to full over
    // thunderRiseSeconds - it comes in under the clap.
    [Range(0f, 1f)] public float thunderTrackStartVolume = 0.55f;
    [Min(0f)] public float thunderRiseSeconds = 0.8f;

    [Header("Finale")]
    // The last track fades out over its last seconds, so the video ends on a calm tail rather than
    // a cut. Set the last segment's clipEnd a few seconds after its last note.
    [Min(0f)] public float finalFadeSeconds = 2.5f;

    [Header("Trainer")]
    // The character that shows the moves in this playlist, instead of the scene's own. Null keeps
    // the scene's (Jennifer).
    public SilhouetteMotionSet trainerMotionSet;
    // Draw it as a textured character with a soft rim light in the segment's colours, rather than
    // as a neon contour.
    public bool trainerTextured = true;
    // Keep the scene's own trainer as it is (Jennifer's cyan contour) but light its panel - the
    // backdrop and the frame's glow - in each segment's colours.
    public bool trainerPanelInThemeColors;
}
