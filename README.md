# SteamP2PInfo · ELDEN RING NIGHTREIGN Edition

《艾尔登法环 黑夜君临》专用的 Steam P2P 联机信息查看工具 / A Steam P2P
connection viewer dedicated to *ELDEN RING NIGHTREIGN*.

实时显示联机玩家的昵称、SteamID、**游玩时长**、Ping 与连接质量，带可自定义
悬浮窗。 / Shows each online player's name, SteamID, **playtime**, ping and
connection quality, with a customizable in-game overlay.

> 基于 [SteamP2PInfo](https://github.com/tremwil/SteamP2PInfo)（MIT License，
> Copyright (c) 2022 William Tremblay）修改。 / A fork of
> [SteamP2PInfo](https://github.com/tremwil/SteamP2PInfo) (MIT).
>
> 修改版 / Modifications: Copyright (c) 2026 HiKi

---

## 功能 / Features

- 自动识别并附加游戏窗口（Steam AppID 2622380），无需选窗口 / 填 AppID
  Auto-detect & attach to the game window (AppID 2622380)
- 自动静默开启 Steam IPC 日志（`steam.exe +log_ipc`），Steam 重启后自动重新开启
  Silently enables Steam IPC logging; re-enabled automatically after restarts
- 附加时回溯日志，找回已连上的玩家 / Backfills players connected before attach
- 游玩时长列（会话信息 + 悬浮窗）：公开资料显示小时数，私密显示“未公开”
  Playtime column: shows hours when public, "Private" / “未公开” otherwise
- 固定列宽悬浮窗，信息始终对齐 / Fixed-width overlay columns, always aligned
- 全中文 / 全英文双版本 / Fully Chinese & English builds
- 可选 Steam Web API Key，更稳定获取游玩时长 / Optional Web API key support
- 好友关系列：同局两人互为好友时显示“好友”（大概率双排），好友列表被隐藏时显示
  “私密”。不需要 API Key：未填写时自动读取对方公开的社区好友页
  Friends column: "好友" (friends, likely a premade pair) or "私密" (list hidden);
  works without an API key by reading the public community friends page

## 截图 / Screenshots

（待补充 / TODO）

## 使用方法 / Usage

1. 以管理员身份运行 `SteamP2PInfo.exe`（用于抓包计算 Ping）
   Run `SteamP2PInfo.exe` as Administrator (required for ping capture)
2. 启动《艾尔登法环 黑夜君临》并进入游戏画面
   Launch *ELDEN RING NIGHTREIGN* and enter gameplay
3. 点击右上角“附加游戏” / Click "ATTACH GAME"
4. 进入新的联机局后查看玩家信息 / Players appear once you enter a new session

## 常见问题 / FAQ

- 没有玩家？玩家只会在日志开启后的新联机局中出现（Steam 机制）。
  No players? Only sessions started after IPC logging is enabled are detected.
- 时长显示“—”/“-”？网络无法访问 Steam 社区，请开代理或填 API Key。
  Playtime "-"? Network can't reach Steam; use a proxy or an API key.
- 时长“未公开”/“Private”？对方游戏详情私密，公开方式无法获取。
  "Private"? The player's game details are private (Steam privacy rule).
- “关系”列显示“私密”？双方好友列表都设为私密，Steam 不允许第三方读取，
  无法判断两人是否为好友（不会误报成“好友”）。
  Relation "私密"? Both players hide their friends list, so Steam will not tell,
  and the tool refuses to guess. A public list on either side is enough.
- 全屏独占不显示悬浮窗：悬浮窗仅支持窗口化 / 无边框。
  Overlay works only in windowed / borderless mode.

## 许可 / License

[MIT](LICENSE) · Copyright (c) 2022 William Tremblay, 2026 HiKi

## 免责声明 / Disclaimer

本工具为被动监控工具：不注入进程、不修改游戏、不读取内存，仅读取 Steam 日志与
网络包。使用第三方工具进行联机游戏存在一定风险，请自行评估。
/ A passive monitor: no injection, no memory reading. Use with online
anti-cheat games at your own risk.
