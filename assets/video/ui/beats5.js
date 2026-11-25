// The 5.x film's beats: one entry per line of scripts/demo5.json and scripts/reel5.json, in STORYBOARD.md's order.
// The entries themselves (headline, sub-lines, act) are in beats5a.js (the first half of each cut) and beats5b.js (the rest).
(function () {
  const list = (acts, n) => Array.from({ length: n }, (_, i) => acts[i + 1]);
  window.BEATS.demo5 = list(EP.acts5.demo, 24);
  window.BEATS.reel5 = list(EP.acts5.reel, 14);
})();
