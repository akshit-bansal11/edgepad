# Simplified Chinese (zh-CN) glossary

Android folder `values-zh-rCN`, Windows culture `zh-Hans`. Written for mainland usage, following the wording of Android and Windows in Chinese.

## Register and conventions

- Address: 你 (the phone's and Windows' own UI use 你, not 您). Labels are bare nouns or verb phrases; sentences are plain and short.
- Punctuation: full-width (，。：；？！), quotes “…” for button and menu names quoted in text, ellipsis `…`. Items in a dot-separated label keep ` · `.
- Spacing: one space between Chinese and Latin words or numbers inside prose (`Edgepad 打开时`, `第 3 页`). No space next to a placeholder (`已连接到%1$s`), a percent sign, or a unit symbol joined to a number (`%1$d%%`); units written as words keep a space (`%1$d dp`, `47 MB`).
- Joiners: `list_separator` `、`; `sentences_joined` has no space; `battery_spoken` keeps a space (device name then percentage); `corner_slot` `左上 · 左边缘`.
- Plurals: `other` only.
- Bluetooth is written 蓝牙 (what Android and Windows show in Chinese), not Latin; this deviates from the brief's "never translate Bluetooth" on purpose. Wi-Fi and USB stay Latin.
- Key caps keep their Latin print (Ctrl, Alt, Shift, Fn, Esc, Tab, Enter, Delete, Home, End, Page Up, Page Down, Insert), as Chinese keyboards do.
- Titles are not capitalised (no case in Chinese); no trailing full stop on labels.

## Terms

| English | zh-CN | Note |
|---|---|---|
| laptop | 电脑 | One word everywhere, phone/laptop alike; `show computers only` is also 电脑 |
| phone | 手机 | |
| surface (control surface) | 操控区 | Settings: 操控区设置 |
| dial / ruler | 标尺 | Same word for both; corner dials 边角标尺 |
| corner(s) / split corners | 边角 / 拆分边角 | |
| volume | 音量 (short 音量) | |
| brightness | 亮度 (short 亮度) | |
| zoom | 缩放 (short 缩放) | |
| media scrub | 进度调节 (short 进度) | |
| app switcher | 应用切换 (short 应用) | |
| refresh rate | 刷新率 (short 刷新) | |
| vertical scroll / horizontal scroll | 垂直滚动 / 水平滚动 (short 竖滚 / 横滚) | |
| virtual desktops | 虚拟桌面 (short 桌面) | |
| browser tabs | 浏览器标签页 (short 标签) | |
| switch laptop | 切换电脑 (short 电脑) | |
| trackpad | 触控板 | |
| pointer | 指针 | |
| gesture | 手势 | tap 轻点, swipe 滑动, pinch 捏合, hold 长按 |
| natural scrolling | 自然滚动 | |
| media / now playing | 媒体 / 正在播放 | |
| Controls (the grid) | 控制 | |
| control (a gamepad control) | 控件 | |
| keyboard | 键盘 | |
| With Fn / Direct | 配合 Fn / 直接 | |
| gamepad | 手柄 | |
| layout / preset | 布局 / 预设 | Preset names stay English (Xbox, Platformer, Racing, Shooter): the app draws them from code, so the guide must show the same words |
| binding | 绑定 | |
| D-pad / stick / button / shoulder / trigger | 方向键 / 摇杆 / 按钮 / 肩键 / 扳机 | |
| keyboard mode | 键盘模式 | |
| macro / macro deck | 宏 | Menu item: 宏… (Windows: 宏(&M)…) |
| annotate | 标注 | |
| sign | 签名 | |
| capture | 截取 | Region 区域, Full screen 全屏, Record 录制 |
| shapes | 图形 | |
| lock the trackpad | 锁定触控板 | Lock the laptop: 锁定电脑 |
| focus (hide dials and media) | 隐藏标尺和媒体 | |
| share clipboard | 共享剪贴板 | Share (Android sheet): 分享 |
| pairing / pairing code | 配对 / 配对码 | |
| PIN | PIN | |
| trust / don't trust | 信任 / 不信任 | |
| security level | 安全级别 | |
| forget | 忘记 | |
| remembered | 已记住 | |
| Devices | 设备 | |
| active | 当前 | 设为当前 = make active |
| connect / disconnect / reconnect | 连接 / 断开连接 / 重新连接 | |
| handshake | 握手 | |
| Nearby devices permission | “附近的设备”权限 | Android's label |
| USB tethering | USB 网络共享 | Android's label |
| haptic feedback | 触感反馈 | |
| appearance / Look | 外观 | Same word for both |
| theme System / Dark / Light | 跟随系统 / 深色 / 浅色 | |
| upright / sideways | 竖屏 / 横屏 | |
| control colour | 控件颜色 | |
| tray app / tray | Edgepad 托盘 / 系统托盘 (notification area: 通知区域) | |
| tray menu items | 宏… / 安全… / Wi-Fi 和 USB 连接 / 共享剪贴板 / 每天检查更新 / 打开日志 / 退出 Edgepad | Quoted by the Android guide as “Wi-Fi 和 USB 连接”, “宏…”, “信任” |
| firewall | 防火墙 | |
| start with Windows | 开机时启动 | Windows 11 wording |
| update / release notes / release page / major release | 更新 / 版本说明 / 发布页面 / 重大版本 | |
| protocol | 协议 | |
| round trip | 往返时延 | |

## Windows and shared core

- Culture file names: `Strings.zh-Hans.resx`, `CoreStrings.zh-Hans.resx`.
- Access keys are appended in parentheses, Windows style, before any ellipsis: `宏(&M)…`. Quoted by Android as “宏…”. Tray menu keys: T start, M macros, E security, W Wi-Fi and USB, F firewall, B Bluetooth, S share clipboard, C check updates, L log, U update, D documentation, Q quit.
- Security dialog keys: P set PIN, F forget all, R reset, S save. Macro dialog: A add, B browse, C choose icon, U own icon, R remove.
- Quoted names match their source strings: “信任” / “不信任”, “忘记”, “全部忘记”, “重置”, “取消”, “允许通过 Windows 防火墙”, “是，已配对” (Android `pair_confirm_yes`).
- Units: seconds are `{0} 秒`; levels `级别 {0}，{1}`; counts `{0} / {1}`.
- Terms: trusted phone 受信任的手机; security levels 仅信任 / 每部手机输入一次 PIN / 每次连接都输入 PIN; Macros columns 名称 / 打开内容 / 参数; badges 已连接 / 出现问题 / 等待中; start with Windows 开机时启动; Windows Firewall Windows 防火墙; administrator 管理员.
