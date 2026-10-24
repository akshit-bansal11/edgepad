"""CI only: one edge-tts take of script.json, cut apart at its pauses so the gaps between scenes can be widened.

Writes out/voice.mp3, out/timing.json and out/edgepad-demo.en.vtt. Needs `pip install edge-tts` and ffmpeg.
Silence is only ever inserted between lines; nothing is re-synthesised."""
import asyncio
import json
import pathlib
import re
import subprocess

import edge_tts

HERE = pathlib.Path(__file__).parent
OUT = HERE / "out"
VOICE = "en-US-AndrewMultilingualNeural"
LEAD = 0.6  # silence before the first word
PAD = 0.25  # a scene starts this long before its line is spoken
GAP = 0.7  # least silence after a line before the next scene
TAIL = 1.5  # least time the end card holds after the last word


def letters(s):
    return re.sub(r"[^a-z]", "", s.lower())


async def take(text):
    mp3 = bytearray()
    words = []
    comm = edge_tts.Communicate(text, VOICE, boundary="WordBoundary")
    async for chunk in comm.stream():
        if chunk["type"] == "audio":
            mp3 += chunk["data"]
        elif chunk["type"] == "WordBoundary":
            words.append((chunk["text"], chunk["offset"] / 1e7, (chunk["offset"] + chunk["duration"]) / 1e7))
    return bytes(mp3), words


def align(lines, words):
    """Per line: first word start, last word end, next line's first word start (None for the last)."""
    spans, w, used, total = [], 0, 0, 0
    for line in lines:
        total += len(letters(line["say"]))
        first = w
        while w < len(words) and used < total:
            used += len(letters(words[w][0]))
            w += 1
        assert w > first, "no words matched " + line["id"]
        spans.append((words[first][1], words[w - 1][2], words[w][1] if w < len(words) else None))
    assert w == len(words), f"{len(words) - w} spoken words matched no line; the script and the take disagree"
    return spans


def stamp(sec):
    ms = round(sec * 1000)
    return "%02d:%02d:%02d.%03d" % (ms // 3600000, ms // 60000 % 60, ms // 1000 % 60, ms % 1000)


def main():
    OUT.mkdir(exist_ok=True)
    lines = json.loads((HERE / "script.json").read_text(encoding="utf-8"))
    audio, words = asyncio.run(take(" ".join(line["say"] for line in lines)))
    (OUT / "take.mp3").write_bytes(audio)
    spans = align(lines, words)

    # Cut each line out of the take midway through the silence after it, then lay the pieces out again.
    cuts = [0.0] + [(end + nxt) / 2 for _, end, nxt in spans[:-1]] + [spans[-1][1] + 0.3]
    filters, order, at, timing = [], [], 0.0, []
    for i, (line, (start, end, _)) in enumerate(zip(lines, spans)):
        lead = LEAD if i == 0 else PAD
        speech = end - start
        slot = max(lead + speech + GAP, line.get("hold", 0))
        if i == len(lines) - 1:
            slot = max(slot, lead + speech + TAIL)
        a = max(cuts[i], start - 0.15)
        b = min(cuts[i + 1], end + 0.3)
        before = max(lead - (start - a), 0.001)  # silence ahead of the cut, so the first word lands on `lead`
        filters.append(f"[0:a]atrim={a:.3f}:{b:.3f},asetpts=PTS-STARTPTS,apad=whole_dur={slot - before:.3f}[s{i}]")
        filters.append(f"anullsrc=r=24000:cl=mono,atrim=0:{before:.3f}[p{i}]")
        order.append(f"[p{i}][s{i}]")
        timing.append(
            {
                "id": line["id"],
                "scene": line["scene"],
                "start": round(at + lead, 3),
                "end": round(at + lead + speech, 3),
                "caption": line["caption"],
            }
        )
        at += slot
    graph = ";".join(filters) + ";" + "".join(order) + f"concat=n={2 * len(lines)}:v=0:a=1[out]"
    subprocess.run(
        ["ffmpeg", "-y", "-loglevel", "error", "-i", str(OUT / "take.mp3"), "-filter_complex", graph,
         "-map", "[out]", "-ar", "24000", "-ac", "1", "-b:a", "96k", str(OUT / "voice.mp3")],
        check=True,
    )
    (OUT / "take.mp3").unlink()
    (OUT / "timing.json").write_text(json.dumps({"duration": round(at, 3), "lines": timing}, indent=1), encoding="utf-8")
    cues = "\n".join(f"{stamp(t['start'])} --> {stamp(t['end'])}\n{t['caption']}\n" for t in timing)
    (OUT / "edgepad-demo.en.vtt").write_text("WEBVTT\n\n" + cues, encoding="utf-8")
    print(f"voice: {at:.1f}s, {len(words)} words, {len(lines)} lines")


main()
