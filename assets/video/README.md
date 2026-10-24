The 4.0.0 demo video, rendered by the `video` job in `.github/workflows/ci.yml` (nothing here runs locally).

1. `voice.py` speaks `script.json` in one edge-tts take, cuts it at the pauses, widens them and writes `out/voice.mp3`, `out/timing.json` and `out/edgepad-demo.en.vtt`.
2. `index.html` and `ui/*.js` draw each frame on a canvas as a pure function of time, ported from the Android drawing code. Open `index.html?aspect=16x9&t=20` for a still (`9x16` and `4x5` too); without `out/timing.json` it uses a stand-in timeline.
3. `render.mjs` serves the repo, drives headless Chrome over CDP and pipes 30 fps frames and the voice into ffmpeg.
4. It runs only when something under `assets/video/` changed. The `edgepad-video` artifact holds `out/`: `edgepad-demo-16x9.mp4`, `-9x16.mp4`, `-4x5.mp4`, `edgepad-demo-poster.jpg`, `demo.gif` and the `.vtt`.
5. `assets/demo.gif` is not replaced by the job; copy `out/demo.gif` over it by hand if it should be.
