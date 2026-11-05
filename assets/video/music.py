"""CI only: an original music bed for each cut, written here, so no licence or credit applies.

Reads out/<id>/timing.json (voice.py) and the script's chapter "energy" (0 to 1), and writes out/<id>/music.mp3 at the
cut's exact length. Chapters start on the beat (voice.py snaps them), so a riser leads into each one and a low hit lands
on it, and the bed dips under every spoken line. The same seed gives the same track. Needs numpy and ffmpeg.
"""
import json
import pathlib
import subprocess
import sys
import wave

import numpy as np

HERE = pathlib.Path(__file__).parent
SR = 44100
# Two bars each, in A minor. The first number is the bass root; the rest are the open pad voicing.
CHORDS = [(33, 57, 60, 64, 71), (29, 53, 57, 60, 64), (36, 55, 60, 62, 64), (31, 55, 59, 62, 64)]
ARP = (0, 2, 1, 3, 2, 1, 3, 2)  # which pad note each eighth plays
DUCK = 0.5  # the bed's gain under speech

rng = np.random.default_rng(4)


def hz(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


def seconds(dur):
    return np.arange(int(dur * SR)) / SR


def lowpass(x, cut):
    spec = np.fft.rfft(x)
    spec /= 1 + (np.fft.rfftfreq(len(x), 1 / SR) / cut) ** 4
    return np.fft.irfft(spec, len(x))


def tone(freq, t, harmonics, decay=1.0):
    return sum(np.sin(2 * np.pi * freq * n * t) * decay**n / n for n in harmonics)


def pad(notes, dur, detune):
    t = seconds(dur)
    x = sum(tone(hz(n) * (1 + d), t, range(1, 9), 0.86) for n in notes for d in (-detune, detune))
    fade = int(0.35 * SR)
    env = np.ones(len(t))
    env[:fade] = np.linspace(0, 1, fade)
    env[-fade:] = np.linspace(1, 0, fade)
    return lowpass(x, 1500) * env / len(notes)


def pluck(note, dur=0.5):
    t = seconds(dur)
    return tone(hz(note), t, (1, 3, 5), 0.6) * np.exp(-t * 9)


def kick():
    t = seconds(0.28)
    return np.sin(2 * np.pi * (46 * t + (150 - 46) / 28 * (1 - np.exp(-28 * t)))) * np.exp(-t * 11)


def noise_hit(dur, decay, cut, high=False):
    t = seconds(dur)
    x = rng.standard_normal(len(t))
    low = lowpass(x, cut)
    return (x - low if high else low) * np.exp(-t * decay)


def riser(dur):
    t = seconds(dur)
    x = lowpass(rng.standard_normal(len(t)), 5000)
    ramp = (t / dur) ** 3
    return x * ramp + np.sin(2 * np.pi * (180 * t + 260 * t**2 / dur)) * ramp * 0.25


def impact():
    t = seconds(1.6)
    return np.sin(2 * np.pi * (34 * t + 8 / 3 * (1 - np.exp(-3 * t)))) * np.exp(-t * 3.2)


def reverb(x, seconds_long=1.8):
    n = int(seconds_long * SR)
    ir = rng.standard_normal(n) * np.exp(-np.arange(n) / SR * 4)
    size = len(x) + n
    wet = np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(ir, size), size)[: len(x)]
    return wet / (np.abs(wet).max() + 1e-9) * np.abs(x).max()


def put(buf, sig, at, gain=1.0):
    i = int(at * SR)
    if 0 <= i < len(buf):
        n = min(len(sig), len(buf) - i)
        buf[i : i + n] += sig[:n] * gain


def compose(sid):
    timing = json.loads((HERE / "out" / sid / "timing.json").read_text(encoding="utf-8"))
    script = json.loads((HERE / "scripts" / f"{sid}.json").read_text(encoding="utf-8"))
    energies = [c["energy"] for c in script["chapters"]]
    starts = [c["start"] for c in timing["chapters"]]
    dur, beat = timing["duration"], 60.0 / timing["bpm"]
    bar = beat * 4
    size = int(dur * SR) + 1
    # Stems: left and right pads, the pumped middle, the steady top, and the hits that go through the reverb.
    pad_l, pad_r, arp_l, arp_r, low, top, fx = (np.zeros(size) for _ in range(7))
    tail = timing["lines"][-1]["end"]  # after the last word the kit stops and the chord rings out

    def energy(at):
        i = max(0, int(np.searchsorted(starts, at, side="right")) - 1)
        return 0.0 if at >= tail else energies[i]

    chord_dur = bar * 2
    rendered = [(pad(c[1:], chord_dur + 0.35, 0.0016), pad(c[1:], chord_dur + 0.35, 0.0023)) for c in CHORDS]
    kick_s, hat_s, clap_s = kick(), noise_hit(0.07, 90, 7000, True), noise_hit(0.16, 28, 3200)
    plucks = {n: pluck(n + 12) for c in CHORDS for n in c[1:]}
    for c in range(int(dur / chord_dur) + 1):
        at = c * chord_dur
        put(pad_l, rendered[c % 4][0], at, 0.4)
        put(pad_r, rendered[c % 4][1], at, 0.4)
    for b in range(int(dur / beat) + 1):
        at = b * beat
        e = energy(at)
        chord = CHORDS[b // 8 % 4]
        for eighth in (0, 1):
            t8 = at + eighth * beat / 2
            if e >= 0.3:
                side = arp_l if (b * 2 + eighth) % 2 == 0 else arp_r
                put(side, plucks[chord[1 + ARP[(b * 2 + eighth) % 8]]], t8, 0.16)
            if e >= 0.5:
                put(low, np.sin(2 * np.pi * hz(chord[0] + 12) * seconds(beat / 2)) * np.hanning(int(beat / 2 * SR)), t8, 0.5)
        if e >= 0.6:
            put(low, kick_s, at, 0.9)
        if e >= 0.5:
            put(top, hat_s, at + beat / 2, 0.16)
        if e >= 0.85 and b % 2 == 1:
            put(top, clap_s, at, 0.26)
    for i, start in enumerate(starts):
        if i:
            put(fx, riser(beat * 2), start - beat * 2, 0.05)
            put(fx, impact(), start, 0.55)

    # The kit's pump on what sits under it, only where the kick plays.
    t = np.arange(size) / SR
    pump = 1 - 0.5 * np.exp(-((t % beat) / beat) * 6)
    gate = np.array([energy(x) >= 0.6 for x in t[:: SR // 20]], float)
    pump = 1 + (pump - 1) * np.repeat(gate, SR // 20)[:size]
    wet_l = reverb(pad_l + arp_l + fx) * 0.22
    wet_r = reverb(pad_r + arp_r + fx) * 0.22
    left = (pad_l + arp_l) * pump + low + top + fx + wet_l
    right = (pad_r + arp_r) * pump + low + top + fx + wet_r

    # Down under each line, back up in the gaps.
    speech = np.zeros(size)
    for line in timing["lines"]:
        speech[int((line["start"] - 0.1) * SR) : int((line["end"] + 0.15) * SR)] = 1
    win = int(0.15 * SR)
    speech = np.convolve(speech, np.ones(win) / win, "same")
    duck = 1 - (1 - DUCK) * speech
    fade = np.clip((dur - t) / 2.5, 0, 1) ** 2
    mix = np.stack([left, right], 1) * (duck * fade)[:, None]
    mix = np.tanh(mix / np.abs(mix).max() * 1.2) * 0.7

    out = HERE / "out" / sid
    with wave.open(str(out / "music.wav"), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((mix * 32767).astype("<i2").tobytes())
    subprocess.run(
        ["ffmpeg", "-y", "-loglevel", "error", "-i", str(out / "music.wav"), "-b:a", "192k", str(out / "music.mp3")],
        check=True,
    )
    (out / "music.wav").unlink()
    print(f"music {sid}: {dur:.1f}s at {timing['bpm']} bpm, {len(starts)} sections")


for sid in sys.argv[1:] or sorted(p.stem for p in (HERE / "scripts").glob("*.json")):
    compose(sid)
