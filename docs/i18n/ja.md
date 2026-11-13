# Japanese (ja) glossary

Android folder `values-ja`, Windows culture `ja`. Written to match the wording of Android and Windows in Japanese.

## Register and conventions

- Register: です・ます in sentences; buttons, tabs and titles are short noun or verb-stem labels (`接続`, `切断`, `保存`). No honorific overload, no 〜してください piled up: instructions use 〜してください once per sentence.
- Address: none. The reader is never named.
- Punctuation: full-width `、` `。` `：` `？` `（）`. Quotes for button, menu and screen names quoted in text are `「…」`. Ellipsis `…`. Dot-separated labels keep ` · ` (so the guide kickers keep "1 · ...").
- Spacing: none between Japanese and Latin or digits (`Edgepadを開く`, `3本指`, `1MB`, `60Hz`, `100dp`). A space only before a Latin word standing alone in a label (`Edgepad %1$s · プロトコル %2$d`) or around ` · ` and ` / `.
- Joiners: `list_separator` `"、"` (quoted); `sentences_joined` has no space (`%1$s%2$s`); `battery_spoken` keeps a space; `corner_slot` `左上 · 左端`.
- Plurals: `other` only. Counters are written into the sentence (`%1$d件`, `%1$d台`, `%1$d本指`, `%1$d個`).
- Bluetooth, Wi-Fi, USB, Windows, Android, Mac stay Latin (Android and Windows in Japanese show Bluetooth in Latin too).
- Titles are not case-folded; no trailing `。` on labels, `。` on every full sentence.
- Key caps keep their Latin print (Ctrl, Alt, Shift, Fn, F1 to F12); ranges use `～` (`F1～F12`). Arrow keys in Windows text are `↑` `↓` (`Alt+↑`).
- Fonts: the system font is used (see README section 2), so full-width forms are safe.

## Terms

| English | ja | Note |
|---|---|---|
| laptop | ノートPC | One word on both apps; "computer" (`show computers only`) is パソコン |
| phone | スマホ | Casual, as everyday Japanese says it; `スマートフォン` is too long for labels |
| surface (control surface) | 操作画面 | Settings: 操作画面の設定 |
| dial / ruler | ダイヤル | One word for both; the ruler drawing is 目盛り in the guide |
| corner(s) / split corners | コーナー / コーナーを分割 | |
| edge | 端 (左端, 上端, 右端, 下端) | Physical edges |
| volume | 音量 (short 音量) | |
| brightness | 明るさ (short 明) | |
| zoom | ズーム (short 拡大) | |
| media scrub | メディアシーク (short シーク) | |
| app switcher | アプリ切り替え (short アプリ) | |
| refresh rate | リフレッシュレート (short Hz) | |
| vertical / horizontal scroll | 縦スクロール / 横スクロール (short 縦 / 横) | |
| virtual desktops | 仮想デスクトップ (short 仮想) | |
| browser tabs | ブラウザのタブ (short タブ) | |
| switch laptop | ノートPCの切り替え (short PC) | |
| trackpad | トラックパッド | |
| pointer | ポインター | |
| gesture | ジェスチャー | tap タップ, swipe スワイプ, pinch ピンチ, hold 長押し |
| natural scrolling | ナチュラルスクロール | |
| media / now playing | メディア / 再生中 | |
| Controls (the grid) | コントロール | Quoted the same way in text: 「コントロール」 |
| control colour | 操作部の色 | Not コントロール, to keep it apart from the Controls grid |
| keyboard | キーボード | |
| With Fn / Direct | Fnと併用 / 直接 | |
| gamepad | ゲームパッド | |
| layout / preset | レイアウト / プリセット | |
| binding | 割り当て | |
| D-pad / stick / button / shoulder / trigger | 十字キー / スティック / ボタン / ショルダー / トリガー | |
| keyboard mode | キーボードモード | |
| macro | マクロ | Windows menu item `マクロ…(&M)`, quoted on the phone as 「マクロ…」 |
| annotate | 書き込み | Tools: ペン, 蛍光ペン, 直線, 矢印, 四角形, 楕円 |
| sign | 署名 | |
| capture | キャプチャ | Region 範囲, Full screen 全画面, Record 録画 |
| shapes | 図形 | |
| lock the trackpad / the laptop | トラックパッドをロック / ノートPCをロック | |
| focus (hide dials and media) | ダイヤルとメディアを隠す | |
| share clipboard | クリップボードを共有 | Same text on phone, Windows menu and dialogs; the Android Share sheet is 共有 |
| pairing / pairing code | ペアリング / ペアリングコード | |
| PIN | PIN | |
| trust / don't trust | 信頼する / 信頼しない | Buttons; the phone text quotes 「信頼する」 |
| security level | レベル | `レベル{0}：{1}` |
| forget | 登録解除 | Phone `forget` and Windows `Common_Forget` match; `すべて登録解除` quoted in errors |
| remembered | 登録済み | |
| Devices | デバイス | |
| active (laptop) | 操作中 / 操作先にする (make active) | Distinct from 使用中 (battery in use) |
| connect / disconnect / reconnect | 接続 / 切断 / 再接続 | |
| handshake | ハンドシェイク | |
| Nearby devices permission | 「付近のデバイス」の許可 | Android's label |
| USB tethering | USBテザリング | Android's label |
| haptic feedback | 触覚フィードバック | |
| appearance / Look | 外観 | Same word for both |
| theme System / Dark / Light | システム / ダーク / ライト | |
| upright / sideways | 縦向き / 横向き | |
| tray app / notification area | Edgepad / タスクバーの通知領域 | System tray icon: タスクトレイ |
| tray menu | マクロ…, セキュリティ…, Wi-FiおよびUSB接続, クリップボードを共有, アップデートを毎日確認, ログを開く, Edgepadを終了 | Access key written `(&X)` after the label, with the English letter |
| firewall | ファイアウォール | |
| start with Windows | Windowsの起動時に開始 | |
| update / release notes / release page / major release | アップデート / リリースノート / リリースページ / メジャーリリース | |
| protocol | プロトコル | |
| round trip | 往復時間 | |
