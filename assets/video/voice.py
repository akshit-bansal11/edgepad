"""CI only: the narration for each script in scripts/, one edge-tts take per script.

For every script id given (all of scripts/*.json when none are) it writes out/<id>/voice.mp3, out/<id>/timing.json,
out/<id>/timing.js (the same data as `window.TIMING`, which the composition loads with a plain <script>) and
out/<id>/<id>.en.vtt. Needs `pip install edge-tts` and ffmpeg.

The take is cut apart midway through the silence after each line and laid out again with set pauses, so the
pacing is ours while every word stays as it was spoken. Silence is only ever inserted; nothing is re-synthesised.
Every word keeps its own timing, shifted with its line, which is what the captions and the headline reveals key off.
"""
import asyncio
import json
import math
import pathlib
import re
import subprocess
import sys

import edge_tts
from edge_tts.exceptions import NoAudioReceived

HERE = pathlib.Path(__file__).parent
VOICE = "en-US-AndrewMultilingualNeural"
RATE = "+4%"  # a touch brisker than the default, like the reference narration
LEAD = 0.5  # silence before the first word
GAP = 0.35  # between two lines of one chapter
CHAPTER_GAP = 0.85  # between chapters, where the picture changes the most
TAIL = 3.0  # the end card holds this long after the last word
BPM = 108  # music.py plays at this tempo; every chapter starts on one of its beats
BEAT = 60 / BPM
# Said differently from how it is written, one word for one word, so the captions keep the written form.
SPOKEN = {"Wi-Fi": "Why-Fye"}


def letters(s):
    return re.sub(r"[^a-z0-9]", "", s.lower())


async def take(text):
    # The service now and then answers with no audio at all; the same request usually succeeds a little later.
    for attempt in range(1, 5):
        mp3 = bytearray()
        words = []
        comm = edge_tts.Communicate(text, VOICE, rate=RATE, boundary="WordBoundary")
        try:
            async for chunk in comm.stream():
                if chunk["type"] == "audio":
                    mp3 += chunk["data"]
                elif chunk["type"] == "WordBoundary":
                    words.append((chunk["text"], chunk["offset"] / 1e7, (chunk["offset"] + chunk["duration"]) / 1e7))
            return bytes(mp3), words
        except NoAudioReceived:
            if attempt == 4:
                raise
            print(f"voice: no audio on attempt {attempt}, retrying")
            await asyncio.sleep(10 * attempt)


def say(text):
    for written, spoken in SPOKEN.items():
        text = text.replace(written, spoken)
    return text


def split(lines, words):
    """The spoken words of each line, matched by counting letters, so punctuation and splits cannot drift them."""
    out, w, used, total = [], 0, 0, 0
    for text in lines:
        total += len(letters(text))
        first = w
        while w < len(words) and used < total:
            used += len(letters(words[w][0]))
            w += 1
        assert w > first, "no words matched: " + text
        out.append(words[first:w])
    assert w == len(words), f"{len(words) - w} spoken words matched no line; the script and the take disagree"
    return out


def tokens(text, spoken):
    """The line's own words (with their punctuation), each timed from the spoken words whose letters it holds."""
    toks = text.split()
    ends, n = [], 0
    for tok in say(text).split():
        n += len(letters(tok))
        ends.append(n)
    out = [[tok, None, None] for tok in toks]
    c = 0
    for word, s, e in spoken:
        k = next((i for i, end in enumerate(ends) if c < end), len(toks) - 1)
        if out[k][1] is None:
            out[k][1] = s
        out[k][2] = e
        c += len(letters(word))
    # A token with no letters of its own takes the time of the one before it (or after, at the start).
    for i, t in enumerate(out):
        if t[1] is None:
            near = next((out[j] for j in [*range(i - 1, -1, -1), *range(i + 1, len(out))] if out[j][1] is not None), None)
            t[1] = t[2] = near[2] if near else spoken[0][1]
    return out


def stamp(sec):
    ms = round(sec * 1000)
    return "%02d:%02d:%02d.%03d" % (ms // 3600000, ms // 60000 % 60, ms // 1000 % 60, ms % 1000)


def cues(lines):
    """Subtitle cues of a few words each, broken after punctuation, so a cue never outstays its words."""
    out = []
    for line in lines:
        group = []
        for i, w in enumerate(line["words"]):
            group.append(w)
            last = i == len(line["words"]) - 1
            if last or len(group) >= 7 or (w[0][-1] in ".,?!:" and len(group) >= 3):
                out.append((group[0][1], group[-1][2], " ".join(x[0] for x in group)))
                group = []
    return "WEBVTT\n\n" + "\n".join(f"{stamp(a)} --> {stamp(b)}\n{t}\n" for a, b, t in out)


def build(sid):
    script = json.loads((HERE / "scripts" / f"{sid}.json").read_text(encoding="utf-8"))
    out = HERE / "out" / sid
    out.mkdir(parents=True, exist_ok=True)
    flat = [(ci, text) for ci, ch in enumerate(script["chapters"]) for text in ch["lines"]]
    texts = [t for _, t in flat]
    spoken_texts = [say(t) for t in texts]
    audio, words = asyncio.run(take(" ".join(spoken_texts)))
    (out / "take.mp3").write_bytes(audio)
    spoken = split(spoken_texts, words)
    starts = [s[0][1] for s in spoken]
    ends = [s[-1][2] for s in spoken]

    # Cut each line out of the take midway through the silence after it, then lay the pieces out again.
    cuts = [0.0] + [(ends[i] + starts[i + 1]) / 2 for i in range(len(flat) - 1)] + [ends[-1] + 0.3]
    filters, order, at, lines, chapters = [], [], 0.0, [], []
    for i, (ci, text) in enumerate(flat):
        last = i == len(flat) - 1
        lead = LEAD if i == 0 else 0.0
        a = max(cuts[i], starts[i] - 0.12)
        b = min(cuts[i + 1], ends[i] + 0.25)
        speech = ends[i] - starts[i]
        gap = TAIL if last else CHAPTER_GAP if flat[i + 1][0] != ci else script.get("gap", GAP)
        if not last and flat[i + 1][0] != ci:  # the next chapter lands on a beat, so the music can hit it
            end = at + lead + speech + gap + (0.12 if i else 0)
            gap += math.ceil(end / BEAT) * BEAT - end
        slot = lead + speech + gap
        before = max(lead + (0.12 if i else 0) - (starts[i] - a), 0.001)  # silence ahead of the cut
        first = at + before + (starts[i] - a)  # where this line's first word lands
        shift = first - starts[i]
        if not chapters or chapters[-1]["index"] != ci:
            chapters.append({"index": ci, "title": script["chapters"][ci]["title"], "start": round(at, 3)})
        filters.append(f"[0:a]atrim={a:.3f}:{b:.3f},asetpts=PTS-STARTPTS,apad=whole_dur={slot + (0.12 if i else 0) - before:.3f},atrim=0:{slot + (0.12 if i else 0) - before:.3f}[s{i}]")
        filters.append(f"anullsrc=r=24000:cl=mono,atrim=0:{before:.3f}[p{i}]")
        order.append(f"[p{i}][s{i}]")
        toks = tokens(text, spoken[i])
        lines.append(
            {
                "chapter": ci,
                "text": text,
                "start": round(first, 3),
                "end": round(ends[i] + shift, 3),
                "words": [[t, round(s + shift, 3), round(e + shift, 3)] for t, s, e in toks],
            }
        )
        at += slot + (0.12 if i else 0)
    graph = ";".join(filters) + ";" + "".join(order) + f"concat=n={2 * len(flat)}:v=0:a=1,loudnorm=I=-16:TP=-1.5:LRA=11[out]"
    subprocess.run(
        ["ffmpeg", "-y", "-loglevel", "error", "-i", str(out / "take.mp3"), "-filter_complex", graph,
         "-map", "[out]", "-ar", "24000", "-ac", "1", "-b:a", "128k", str(out / "voice.mp3")],
        check=True,
    )
    (out / "take.mp3").unlink()
    # The poster is a frame from the corner-dial beat, the picture that says the most about the app.
    dial = next((ln for ln in lines if "dial" in ln["text"].lower()), lines[len(lines) // 2])
    timing = {
        "id": sid,
        "title": script["title"],
        "bpm": BPM,
        "duration": round(at, 3),
        "poster": round(min(dial["end"], dial["start"] + 3.0), 3),
        "chapters": [{"title": c["title"], "start": c["start"]} for c in chapters],
        "lines": lines,
    }
    data = json.dumps(timing, indent=1)
    (out / "timing.json").write_text(data, encoding="utf-8")
    (out / "timing.js").write_text(f"window.TIMING = {data};\n", encoding="utf-8")
    (out / f"{sid}.en.vtt").write_text(cues(lines), encoding="utf-8")
    print(f"voice {sid}: {at:.1f}s, {len(words)} words, {len(lines)} lines, {len(chapters)} chapters")


def main():
    ids = sys.argv[1:] or sorted(p.stem for p in (HERE / "scripts").glob("*.json"))
    for sid in ids:
        build(sid)


main()
