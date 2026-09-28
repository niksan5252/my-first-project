using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// DOTween tweens that run on the music's clock instead of Unity's.
///
/// A tween left to DOTween's own update advances with Time.deltaTime, which is not the song: it
/// starts whenever the tween was created, drifts from the beat over a few minutes, and in a
/// recording follows the captured frame step rather than the audio written to the file. So every
/// tween added here is built PAUSED, with its duration in BEATS, and is positioned each frame with
/// Goto(beats) from ClubBeat - SongClock's smoothed song time on the chart's own grid. The tween
/// supplies the shape (eases, sequences, yoyos); the song supplies the time.
///
///   * Loop(tween, periodBeats, offsetBeats) - repeats every periodBeats; offsetBeats shifts where
///     in the bar it lands (0 = on the beat grid's anchor).
///   * Once(tween) - plays from 0 to its end as `beats` runs from 0 up; positioned by the caller
///     with Seek(beatsSinceSomething), e.g. ClubBeat.BeatsSinceDrop.
public sealed class SongTweens
{
    private struct Entry { public Tween tween; public float period, offset; }

    private readonly List<Entry> loops = new List<Entry>();
    private readonly List<Tween> all = new List<Tween>();

    /// Registers a looping tween. Its own duration should equal periodBeats (1 unit = 1 beat).
    public T Loop<T>(T tween, float periodBeats, float offsetBeats = 0f) where T : Tween
    {
        Prepare(tween);
        loops.Add(new Entry { tween = tween, period = Mathf.Max(0.001f, periodBeats), offset = offsetBeats });
        return tween;
    }

    /// Registers a one-shot tween, positioned by the caller through Seek(tween, beats).
    public T Once<T>(T tween) where T : Tween
    {
        Prepare(tween);
        return tween;
    }

    /// Moves every looping tween to song position `beats` (beats since the grid's anchor).
    public void SeekLoops(float beats)
    {
        foreach (var e in loops)
            if (e.tween.IsActive()) e.tween.Goto(Mathf.Repeat(beats - e.offset, e.period));
    }

    /// Positions a one-shot tween `beats` after its start; before 0 or past its end it rests at
    /// the start or the end.
    public static void Seek(Tween tween, float beats)
    {
        if (!tween.IsActive()) return;
        float d = tween.Duration(false);
        tween.Goto(float.IsInfinity(beats) || beats < 0f ? (beats > 0f ? d : 0f) : Mathf.Min(beats, d));
    }

    public void KillAll()
    {
        foreach (var t in all) if (t.IsActive()) t.Kill();
        all.Clear();
        loops.Clear();
    }

    private void Prepare(Tween tween)
    {
        // Never on DOTween's clock: paused, kept alive at its end, not recycled under us.
        tween.SetAutoKill(false).SetRecyclable(false).SetUpdate(UpdateType.Manual);
        tween.Pause();
        all.Add(tween);
    }
}
