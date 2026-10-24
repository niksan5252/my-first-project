"""The coach's missing lines for the cards (Assets/Resources/Voice, one clip per key; VoiceOver plays
them, a key without a clip stays silent). Only clips that do not exist yet are made (--force redoes them).
Female coach voice like the existing clips: edge-tts en-US-AvaNeural, a bit faster and higher,
-15 LUFS, 48 kHz stereo wav.
usage (from the project folder; pip install edge-tts, ffmpeg on PATH):
  python -I Tools/voice/coach_voice.py
"""
import asyncio, os, subprocess, sys, tempfile

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "Voice")
VOICE, RATE, PITCH = "en-US-AvaNeural", "+12%", "+4Hz"

LINES = {
    # video 29 (Best Of): ten levels, the secret last one
    "BEST_OF": "Welcome to the best of Beat Tunnel!",
    "LEVELS_10": "This video has ten levels! Don't leave between tracks!",
    "SECRET_HOOK": "Level ten is a secret! Can you make it to the end?",
    "SECRET_LEVEL": "Secret level! You unlocked it!",
    "LEVEL_9": "Level nine!",
}


async def say(text, mp3):
    import edge_tts
    await edge_tts.Communicate(text, VOICE, rate=RATE, pitch=PITCH).save(mp3)


def main():
    force = "--force" in sys.argv
    os.makedirs(OUT, exist_ok=True)
    for key, text in LINES.items():
        wav = os.path.join(OUT, key + ".wav")
        if os.path.exists(wav) and not force: print("have", key); continue
        with tempfile.TemporaryDirectory() as d:
            mp3 = os.path.join(d, "a.mp3")
            asyncio.run(say(text, mp3))
            subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", mp3, "-af", "loudnorm=I=-15:TP=-1.5",
                            "-ar", "48000", "-ac", "2", wav], check=True)
        print("made", key, "-", text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
