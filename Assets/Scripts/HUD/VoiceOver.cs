using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// Says the HUD's praise words out loud (banners, the rating ladder, the centre words): a clip
/// per word in Resources/Voice, named after the word in capitals with spaces as underscores and
/// punctuation dropped ("THAT'S AMAZING" -> THATS_AMAZING). A word without a clip stays silent.
/// One 2D source; a word that comes within `minGap` of the last one is skipped so two banners
/// landing together do not talk over each other. The recorder takes it with the rest of the mix.
public static class VoiceOver
{
    public static float volume = 0.9f;
    private const float MinGap = 2.5f;

    private static AudioSource source;
    private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    private static float lastTime = -10f;
    public static int Said { get; private set; }   // words spoken this session (a check for tests)

    /// Said whatever the gap: the coach's lines on the cards and level starts (video 20).
    public static void SayNow(string key)
    {
        lastTime = -10f;
        Say(key);
    }

    public static void Say(string word)
    {
        if (string.IsNullOrEmpty(word) || !Application.isPlaying) return;
        if (Time.unscaledTime < lastTime) lastTime = -10f;   // a new Play session without a domain reload
        if (Time.unscaledTime - lastTime < MinGap) return;
        var clip = Clip(Key(word));
        if (clip == null) return;
        if (source == null)
        {
            var go = new GameObject("VoiceOver");
            Object.DontDestroyOnLoad(go);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = 0;
        }
        lastTime = Time.unscaledTime;
        source.PlayOneShot(clip, volume);
        Said++;
    }

    private static AudioClip Clip(string key)
    {
        if (!clips.TryGetValue(key, out var c))
        {
            c = Resources.Load<AudioClip>("Voice/" + key);
            clips[key] = c;
        }
        return c;
    }

    private static string Key(string word)
    {
        var sb = new StringBuilder();
        foreach (char ch in word.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch == ' ' && sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
        }
        return sb.ToString().TrimEnd('_');
    }
}
