using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// Loads a beatmap from StreamingAssets and turns it into a validated, time-sorted note list.
///
/// Everything that can be wrong with a generated file is caught HERE, once, at load, and reported
/// as a single message naming the offending event index. The alternative — letting a bad type
/// string or an out-of-range lane through — surfaces as odd behaviour minutes into a track, which
/// is far more expensive to trace back to the generator than a refusal at startup.
public static class BeatmapReader
{
    public const int SupportedVersion = 1;

    /// StreamingAssets is read with plain file IO, which covers the editor and desktop players.
    /// On Android and WebGL that path lives inside a compressed archive and needs UnityWebRequest
    /// instead; this is the one line that has to change when those platforms matter.
    public static string ResolvePath(string fileName)
    {
        return Path.Combine(Application.streamingAssetsPath, "Beatmaps", fileName);
    }

    public static bool TryLoad(string fileName, int laneCount, out Beatmap meta, out List<BeatmapCue> cues, out string error)
    {
        meta = null;
        cues = null;
        error = null;

        string path = ResolvePath(fileName);
        if (!File.Exists(path))
        {
            error = "beatmap not found: " + path;
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception e)
        {
            error = "could not read " + path + ": " + e.Message;
            return false;
        }

        return TryParse(json, laneCount, out meta, out cues, out error);
    }

    public static bool TryParse(string json, int laneCount, out Beatmap meta, out List<BeatmapCue> cues, out string error)
    {
        meta = null;
        cues = null;
        error = null;

        try
        {
            meta = JsonUtility.FromJson<Beatmap>(json);
        }
        catch (Exception e)
        {
            error = "malformed beatmap JSON: " + e.Message;
            return false;
        }

        if (meta == null)
        {
            error = "beatmap JSON parsed to nothing — is the file empty, or a bare array instead of an object?";
            return false;
        }

        if (meta.version != SupportedVersion)
        {
            error = "beatmap version " + meta.version + " is not supported (this build reads version " + SupportedVersion + ")";
            return false;
        }

        if (meta.events == null || meta.events.Length == 0)
        {
            error = "beatmap contains no events";
            return false;
        }

        var parsed = new List<BeatmapCue>(meta.events.Length);
        int migratedJumps = 0;
        for (int i = 0; i < meta.events.Length; i++)
        {
            var e = meta.events[i];
            if (e == null)
            {
                error = Where(i) + "event is null";
                return false;
            }

            if (!Enum.TryParse(e.type, false, out CueType type) || !Enum.IsDefined(typeof(CueType), type))
            {
                // Case-sensitive on purpose: "kick" and "Kick" differing by case is a generator
                // bug worth surfacing now rather than quietly accepting two spellings forever.
                error = Where(i) + "unknown type '" + e.type + "' (expected one of: " + string.Join(", ", Enum.GetNames(typeof(CueType))) + ")";
                return false;
            }

            if (e.lane < 0 || e.lane >= laneCount)
            {
                error = Where(i) + "lane " + e.lane + " is outside 0.." + (laneCount - 1);
                return false;
            }

            if (float.IsNaN(e.time) || float.IsInfinity(e.time) || e.time < 0f)
            {
                error = Where(i) + "time " + e.time.ToString(CultureInfo.InvariantCulture) + " is not a valid position in the track";
                return false;
            }

            // Jump is retired: the hurdle bar it drew has been replaced by Curb, which spans the
            // whole road. Charts written before the kerb existed are full of Jump notes, so the
            // name is still accepted and migrated here rather than rejected - refusing them
            // would mean every old chart in StreamingAssets stops loading for a cosmetic change.
            // This is the ONE place it happens, so nothing downstream has to know Jump ever was.
            if (type == CueType.Jump) { type = CueType.Curb; migratedJumps++; }

            // Obstacles need no rule of their own here. They are one-shot types, so the
            // duration check below already refuses one that carries a sustain, and their lane -
            // a side for Dodge, ignored by Curb and Duck - is validated by exactly the same
            // range test as a note's, which is the point of carrying the direction in it.
            bool wantsHold = type == CueType.KickHold || type == CueType.PunchHold;
            if (wantsHold && !(e.duration > 0f))
            {
                error = Where(i) + type + " needs a duration greater than 0";
                return false;
            }
            if (!wantsHold && e.duration > 0f)
            {
                error = Where(i) + type + " is a one-shot note but carries duration " + e.duration.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            parsed.Add(new BeatmapCue(e.time, type, e.lane, wantsHold ? e.duration : 0f, Mathf.Clamp01(e.intensity)));
        }

        // Sorted here rather than trusted from the file: the sequencer walks a cursor forward and
        // would silently skip anything out of order.
        //
        // List.Sort is NOT stable, so ties are broken by the original index to keep same-timestamp
        // notes — a chord struck across several lanes — in the order the generator wrote them.
        // Without that, two runs over the same file could order a chord differently, which is the
        // kind of difference that makes a timing bug impossible to reproduce.
        var order = new int[parsed.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        var byTime = parsed;
        Array.Sort(order, (a, b) =>
        {
            int cmp = byTime[a].time.CompareTo(byTime[b].time);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });

        cues = new List<BeatmapCue>(parsed.Count);
        for (int i = 0; i < order.Length; i++) cues.Add(parsed[order[i]]);

        // Said out loud rather than done quietly. A chart made before the kerb existed was
        // spaced for a hurdle in a single lane, and Curb spans the whole road and moves the
        // camera - so a dense run of them will hop far more often than a chart generated for
        // them would. Regenerate such a chart rather than living with the migration.
        if (migratedJumps > 0)
            Debug.LogWarning("[BeatmapReader] " + migratedJumps + " retired 'Jump' note(s) were loaded as 'Curb'. "
                           + "This chart predates the kerb; regenerate it for spacing meant for a full-road obstacle.");

        return true;
    }

    private static string Where(int index) => "beatmap event #" + index + ": ";
}
