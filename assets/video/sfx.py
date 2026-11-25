"""CI only: the sound effects of each cut, made by earshot and laid at the voice's word times.

Step 1 renders every request in SOUND.md's table once (`earshot make ... --seed 0`, cached in out/sfx/<id>.wav).
Step 2 reads out/<id>/timing.json (voice.py) and scripts/<id>.cues.json and lays each cue into one stereo 44.1 kHz buffer,
written to out/<id>/sfx.mp3 at the cut's exact length. Needs numpy, ffmpeg and the `earshot` CLI on PATH.

scripts/<id>.cues.json is a list of cues, one per line, in film order:
  {"id": "tap", "line": 7, "word": "controls", "n": 1, "off": 0.0, "gain": 0.0, "repeat": 1, "every": 0.0,
   "until": {"line": 7, "word": "next", "off": 0.0}}
  id      a cue id from CUES below (the SOUND.md table)
  line    1-based line of the script; word  the nth (n, default 1) spoken word starting with these letters (letters and
          digits only, lowercased, like film.js's L.w); no word means the line's start
  off     seconds added to that time; gain dB added to the cue; repeat/every  that many copies, `every` seconds apart
          (at most 12 per second); until  the span's end, same form but no word means the line's END: a looped bed, cross-faded and
          trimmed to the span with 150 ms fades
  The race: {"id": "race", "line": 9, "word": "two", "scale": 1.0, "last": 8.6, "tail": 10, "gain": 0.0}
          race second 0 is that word; scale stretches race time into film time (reel5 0.85); last cuts GAME.md's
          input table after that row (reel5: 8.6); tail is the line whose end the bed keeps running to, quietly, after
          the held silence (omit it and the bed ends with the hold).

Rules (SOUND.md): the finished stem never peaks above -6 dBFS (soft limit); no cue lands within 80 ms of a word's
start (it is moved later, at most 250 ms), except taps and keys; every effect ducks 6 dB while the voice speaks, again
except taps and keys. Nothing here is random: the same seed gives the same takes.
"""
import json
import pathlib
import re
import subprocess
import sys
import wave

import numpy as np

HERE = pathlib.Path(__file__).parent
SR = 44100
PIN = "7a7f7c5c70d4a763a1eca4b3666af7c77e313f97"  # the earshot commit the job installs (SOUND.md)

# SOUND.md's table, word for word: id -> (request, earshot options).
CUES = {
    "room-tone": ("a quiet room at night, no loop", ["--dur", "12", "--lufs", "-34"]),
    "whoosh-soft": ("a soft short whoosh", ["--lufs", "-24"]),
    "logo-pop": ("a logo bloom", ["--sound", "logo/bloom", "--lufs", "-20"]),
    "title-thump": ("a heavy bag dropped under a title", ["--sound", "title/bag", "--lufs", "-22"]),
    "tick-key": ("the top of a mechanical key for a tick", ["--sound", "tick/key", "--lufs", "-26"]),
    "tap": ("a soft UI tap", ["--lufs", "-24"]),
    "ruler-tick": ("a tiny dry click", ["--lufs", "-30", "--events", "1"]),
    "switch-on": ("a toggle switching on", ["--sound", "toggle/key_down", "--lufs", "-24"]),
    "pop-open": ("a drawer opening softly", ["--lufs", "-24"]),
    "key-type": ("typing on a mechanical keyboard, single keystroke", ["--n", "6", "--lufs", "-28"]),
    "morph-slide": ("a page sliding", ["--sound", "slide/paper", "--lufs", "-26"]),
    "engine": ("a car engine revving, steady, no loop", ["--dur", "12"]),
    "tyre-squeal": ("tyres squealing on asphalt", ["--lufs", "-22"]),
    "boost": ("a fast rising whoosh", ["--sound", "whoosh/fast", "--lufs", "-20"]),
    "whoosh-pass": ("a car passing by, short", ["--lufs", "-28"]),
    "engine-far": ("a distant car engine, no loop", ["--dur", "6", "--lufs", "-36"]),
    "pick-up": ("a small object picked up", ["--lufs", "-26"]),
    "drop": ("a soft thump when a card settles", ["--sound", "thump/door", "--lufs", "-26"]),
    "next-page": ("turning a page", ["--sound", "next/page", "--lufs", "-24"]),
    "laser-hum": ("a very soft electric hum, no loop", ["--dur", "3", "--lufs", "-36"]),
    "shutter": ("a camera shutter", ["--sound", "camera/shutter", "--lufs", "-22"]),
    "rec-blip": ("a short soft beep", ["--lufs", "-26"]),
    "whoosh-arc": ("a light whoosh through the air", ["--sound", "whoosh/sweep", "--lufs", "-24"]),
    "land": ("a soft thump when a card settles, small", ["--lufs", "-28"]),
    "copy-tick": ("a keyboard shortcut click", ["--lufs", "-28"]),
    "whoosh-stream": ("a soft airy rush, no loop", ["--dur", "4", "--lufs", "-32"]),
    "pause-click": ("a light switch click", ["--sound", "switch/lamp", "--lufs", "-26"]),
    "success": ("a soft success sound", ["--sound", "success/shutter", "--lufs", "-22"]),
    "page-slide": ("a page sliding", ["--sound", "slide/paper", "--lufs", "-28"]),
    "draw-stroke": ("a marker drawn on a whiteboard, short", ["--lufs", "-30"]),
    "ring-fill": ("a soft rising tick", ["--lufs", "-30"]),
    "swipe": ("a fingertip swipe on glass", ["--lufs", "-28"]),
    "air-hum": ("a gentle breath of air, no loop", ["--sound", "rise/breath", "--lufs", "-34"]),
    "soft-chime": ("a soft notification", ["--lufs", "-26"]),
    "line-draw": ("a soft rising shimmer", ["--lufs", "-30"]),
    "riser": ("a breath of air rising", ["--sound", "rise/breath", "--lufs", "-24"]),
    "shine": ("a sword drawn for a glint", ["--sound", "shine/blade", "--lufs", "-26"]),
}
EXEMPT = {"tap", "key-type", "tick-key"}  # not ducked under the voice, not moved off a word
PITCHES = (0.8, 1.0, 1.25)  # the engine's low, mid and high takes: one render resampled
DUCK_DB, CLEAR, MOVE_MAX = -6.0, 0.08, 0.25
CEILING = 10 ** (-6 / 20)  # -6 dBFS
ENGINE_TRIM = 10 ** (-8 / 20)  # the engine renders at earshot's default loudness; sit it under the other effects
# GAME.md's input track, mirrored from ui/game.js EP.game.ROWS: (race second, LX, RT, LT). A is held 4.4-5.6 s and adds
# BOOST to the speed for those 1.2 s. Rows ease in-out over EASE seconds.
ROWS = [(0.0, 0, 0, 0), (0.3, 0, 1.0, 0), (2.0, 0.45, 0.85, 0), (3.6, 0.2, 1.0, 0), (4.4, 0, 1.0, 0), (5.8, 0, 0, 0.9),
        (6.4, -0.95, 0.4, 0.2), (7.6, -0.3, 1.0, 0), (8.6, 0, 1.0, 0), (10.0, 0, 0.7, 0)]
A_PRESS, A_END, BOOST, EASE, HZ = 4.4, 5.6, 0.6, 0.2, 240


def say(msg):
    print(msg, flush=True)


def letters(s):
    return re.sub(r"[^a-z0-9]", "", s.lower())


def load(path):
    """A wav as float stereo at SR (ffmpeg converts rate and channels)."""
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-f", "f32le", "-ac", "2", "-ar", str(SR), "-"],
                         capture_output=True, check=True).stdout
    x = np.frombuffer(raw, "<f4").reshape(-1, 2).astype(float)
    return x if len(x) > 1 else silence()


def silence(dur=0.1):
    return np.zeros((int(dur * SR), 2))


def render(cid, sfx):
    """The takes of one cue, from cache or earshot. A request earshot cannot answer becomes silence, with a warning."""
    request, opts = CUES[cid]
    found = lambda: [sfx / f"{cid}.wav"] if (sfx / f"{cid}.wav").exists() else sorted(sfx.glob(f"{cid}_*.wav"))
    if found():
        return [load(p) for p in found()]

    def run(extra):
        for p in found():
            p.unlink()
        try:
            res = subprocess.run(["earshot", "make", request, *extra, "--seed", "0", "-o", str(sfx / f"{cid}.wav")],
                                 capture_output=True, text=True)
        except OSError as err:
            return f"earshot did not start: {err}"
        return res.stdout + res.stderr

    out = run(opts)
    say(f"sfx {cid}: {out.strip().splitlines()[0] if out.strip() else '(no output)'}")
    if "makes no" in out or "not followed" in out or not found():
        also = next((ln for ln in out.splitlines() if ln.startswith("also")), "")
        rest = also.split(":", 1)[1] if also.startswith("also:") else also.split("): ", 1)[-1]
        alt = re.search(r"[a-z0-9]+/[a-z0-9_]+", rest)
        out = ""
        if alt:
            dropped = [o for i, o in enumerate(opts) if o != "--sound" and (i == 0 or opts[i - 1] != "--sound")]
            out = run([*dropped, "--sound", alt.group(0)])
            say(f"sfx {cid} (also {alt.group(0)}): {out.strip().splitlines()[0] if out.strip() else '(no output)'}")
        if not found() or "makes no" in out or "not followed" in out:
            say(f"::warning::sfx {cid}: earshot has no fitting sound, silence used")
            for p in found():
                p.unlink()
            return [silence()]
    return [load(p) for p in found()]


def resample(x, f):
    """Play x at f times its speed (lower pitch below 1), by linear interpolation."""
    idx = np.arange(0, len(x) - 1, f)
    return np.stack([np.interp(idx, np.arange(len(x)), x[:, c]) for c in (0, 1)], 1)


def loop_to(x, n, xf_s=0.25):
    """x repeated, cross-faded at each seam, to exactly n samples."""
    if len(x) >= n:
        return x[:n].copy()
    xf = min(int(xf_s * SR), len(x) // 4)
    step = len(x) - xf
    ramp = np.linspace(0, 1, xf)[:, None]
    out = np.zeros((n, 2))
    k = 0
    while k * step < n:
        c = x.copy()
        if k:
            c[:xf] *= ramp
        c[-xf:] *= ramp[::-1]
        seg = c[: n - k * step]
        out[k * step : k * step + len(seg)] += seg
        k += 1
    return out


def fade(x, ms=150):
    m = min(int(ms / 1000 * SR), len(x) // 2)
    if m:
        x = x.copy()
        x[:m] *= np.linspace(0, 1, m)[:, None]
        x[-m:] *= np.linspace(1, 0, m)[:, None]
    return x


def put(buf, x, t, db=0.0):
    a = int(round(t * SR))
    lo, hi = max(a, 0), min(a + len(x), len(buf))
    if hi > lo:
        buf[lo:hi] += x[lo - a : hi - a] * 10 ** (db / 20)


def race_curves(last, tr_end):
    """GAME.md's physics at 240 Hz: (time, lx, rt, lt, boost 0..1, speed 0..1 of its maximum)."""
    rows = np.array([r for r in ROWS if last is None or r[0] <= last], float)
    tt = np.arange(0, tr_end + 0.1, 1 / HZ)
    i = np.searchsorted(rows[:, 0], tt, "right") - 1
    prev = rows[np.maximum(i - 1, 0)]
    k = np.clip((tt - rows[i, 0]) / EASE, 0, 1)
    k = k * k * (3 - 2 * k)
    lx, rt, lt = (prev[:, c] + (rows[i, c] - prev[:, c]) * k for c in (1, 2, 3))
    boost = ((tt >= A_PRESS) & (tt < A_END)).astype(float)
    speed, s = np.zeros(len(tt)), 0.0
    for j in range(len(tt)):
        s = max(s + (0.9 * rt[j] - 1.6 * lt[j] - 0.02 * s + BOOST * boost[j]) / HZ, 0.0)
        speed[j] = s
    return tt, lx, rt, lt, boost, speed / max(speed.max(), 1e-9)


def lay_race(main, cue, lines, takes, t0, scale):
    """The race's sound bed: engine by speed and throttle, tyre squeal, boost and lamp whooshes."""
    line = lines[cue["line"] - 1]
    held = line["end"] + line["hold"]
    tail = lines[cue["tail"] - 1]["end"] if cue.get("tail") else held
    t_end = max(tail, held)
    n = int((t_end - t0) * SR)
    if n <= 0:
        return
    tt, lx, rt, lt, boost, speed = race_curves(cue.get("last"), (t_end - t0) / scale)
    tr = np.arange(n) / SR / scale
    at = lambda a: np.interp(tr, tt, a)
    s, r, b, x = at(speed), at(rt), at(boost) / BOOST, at(lx)
    pos = 2 * s
    weights = [np.clip(1 - pos, 0, 1), np.clip(1 - np.abs(pos - 1), 0, 1), np.clip(pos - 1, 0, 1)]
    layers = [loop_to(resample(takes["engine"][0], f), n) for f in PITCHES]
    bed = sum(np.sqrt(w)[:, None] * ly for w, ly in zip(weights, layers))
    bed *= ((0.3 + 0.7 * r) * (1 + 0.4 * b) * ENGINE_TRIM)[:, None]
    squeal = np.clip((np.abs(x) - 0.7) / 0.2, 0, 1) * np.clip((s - 0.6) / 0.1, 0, 1)
    bed += loop_to(takes["tyre-squeal"][0], n) * squeal[:, None]
    # Through the held silence at full level, then 1.5 s down to -18 dB and quiet to the tail's end.
    t = np.arange(n) / SR + t0
    env = np.where(t < held, 1.0, 10 ** (-18 / 20 * np.clip((t - held) / 1.5, 0, 1)))
    env = env * np.clip((t_end - t) / 0.3, 0, 1) * np.clip((t - t0) / 0.1, 0, 1)
    put(main, bed * env[:, None], t0, cue.get("gain", 0.0))
    put(main, takes["boost"][0], t0 + A_PRESS * scale, cue.get("gain", 0.0))
    for rs in np.linspace(4.5, 5.5, 4):
        put(main, takes["whoosh-pass"][0], t0 + rs * scale, cue.get("gain", 0.0))


def build(sid, sfx, takes):
    timing = json.loads((HERE / "out" / sid / "timing.json").read_text(encoding="utf-8"))
    cues = json.loads((HERE / "scripts" / f"{sid}.cues.json").read_text(encoding="utf-8"))
    lines, dur = timing["lines"], timing["duration"]
    n = int(round(dur * SR))
    main, free = np.zeros((n, 2)), np.zeros((n, 2))
    starts = np.array([w[1] for ln in lines for w in ln["words"]])

    def when(line, word=None, k=1, off=0.0, end=False):
        if not 1 <= line <= len(lines):
            say(f"::warning::sfx {sid}: no line {line}")
            return None
        ln = lines[line - 1]
        if word:
            hits = [w for w in ln["words"] if letters(w[0]).startswith(letters(word))]
            if len(hits) >= k:
                return hits[k - 1][1] + off
            say(f"::warning::sfx {sid}: line {line} has no word {k} of '{word}'")
        return ln["end" if end else "start"] + off

    def clear(t):
        moved = t
        while moved - t < MOVE_MAX and np.any(np.abs(starts - moved) < CLEAR):
            moved = starts[np.abs(starts - moved) < CLEAR].max() + CLEAR + 1e-6
        return moved

    used, count = {}, 0
    for cue in cues:
        cid = cue["id"]
        t = when(cue["line"], cue.get("word"), cue.get("n", 1), cue.get("off", 0.0))
        if t is None:
            continue
        if cid == "race":
            lay_race(main, cue, lines, takes, t, cue.get("scale", 1.0))
            count += 1
            continue
        if cid not in takes:
            say(f"::warning::sfx {sid}: unknown cue {cid}")
            continue
        target = free if cid in EXEMPT else main
        if "until" in cue:
            u = cue["until"]
            end = when(u["line"], u.get("word"), u.get("n", 1), u.get("off", 0.0), end=True)
            if end is not None and end > t:
                put(target, fade(loop_to(takes[cid][0], int((end - t) * SR))), t, cue.get("gain", 0.0))
                count += 1
            continue
        for r in range(int(cue.get("repeat", 1))):
            at = t + r * max(cue.get("every", 0.0), 1 / 12 if cue.get("repeat", 1) > 1 else 0.0)
            at = at if cid in EXEMPT else clear(at)
            take = takes[cid][used.get(cid, 0) % len(takes[cid])]
            used[cid] = used.get(cid, 0) + 1
            put(target, take, at, cue.get("gain", 0.0))
        count += 1

    # Every effect but taps and keys sits 6 dB down while the voice speaks, with 80 ms ramps.
    ctl = np.zeros(int(dur * 1000) + 2)
    for ln in lines:
        ctl[int(max(ln["start"] - 0.05, 0) * 1000) : int((ln["end"] + 0.1) * 1000)] = 1
    ctl = np.convolve(ctl, np.ones(80) / 80, "same")
    duck = np.interp(np.arange(n) / SR, np.arange(len(ctl)) / 1000, 10 ** (DUCK_DB * ctl / 20))
    mix = main * duck[:, None] + free
    mix = CEILING * np.tanh(mix / CEILING)  # a soft ceiling at -6 dBFS
    out = HERE / "out" / sid
    with wave.open(str(out / "sfx.wav"), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((mix * 32767).astype("<i2").tobytes())
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", str(out / "sfx.wav"), "-b:a", "192k", str(out / "sfx.mp3")],
                   check=True)
    (out / "sfx.wav").unlink()
    say(f"sfx {sid}: {dur:.1f}s, {count} cues, peak {20 * np.log10(max(np.abs(mix).max(), 1e-9)):.1f} dBFS")


def main():
    ids = sys.argv[1:] or sorted(p.name[: -len(".cues.json")] for p in (HERE / "scripts").glob("*.cues.json"))
    sfx = HERE / "out" / "sfx"
    sfx.mkdir(parents=True, exist_ok=True)
    say(f"earshot pinned at {PIN}")
    takes = {cid: render(cid, sfx) for cid in CUES}
    for sid in ids:
        build(sid, sfx, takes)


main()
