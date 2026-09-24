# SteamP2PInfo · ELDEN RING NIGHTREIGN Edition

《艾尔登法环 黑夜君临》专用的 Steam P2P 联机信息查看工具 / A Steam P2P
connection viewer dedicated to *ELDEN RING NIGHTREIGN*.

> **本仓库只提供中文版**：界面、配置文件与使用说明均为中文，不再附带英文构建
> （V1.0.2 时代的英文包只留在 Release 里，源码与后续版本都不再维护）。
> **This repository ships the Chinese build only** - UI, config and docs are in
> Chinese; there is no English build. The older English zip stays in the V1.0.2
> release for reference and is not maintained.

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
- 自动附加：启动后常驻后台，检测到游戏窗口即自动附加，不用手动点“附加游戏”
  （配置页可关闭）；游戏退出时工具自动关闭，下一局重新打开即可再次自动附加
  Auto attach: it watches for the game window and attaches by itself, so nothing has
  to be clicked (can be turned off in the config tab)
- 自动静默开启 Steam IPC 日志（`steam.exe +log_ipc`），Steam 重启后自动重新开启
  Silently enables Steam IPC logging; re-enabled automatically after restarts
- 附加时回溯日志，找回已连上的玩家 / Backfills players connected before attach
- 游玩时长列（会话信息 + 悬浮窗）：依次尝试 Steam Web API → 个人资料“游戏”页 →
  “最喜爱的游戏”展柜，读不到时显示“未公开”，网络异常显示“—”
  Playtime column: tries the Web API, the profile's games page and its favourite-game
  showcase, in that order
- 固定列宽悬浮窗，信息始终对齐 / Fixed-width overlay columns, always aligned
- 全中文界面（含悬浮窗、配置页与使用说明） / Chinese UI throughout
- 可选 Steam Web API Key，更稳定获取游玩时长 / Optional Web API key support
- 好友关系列：好友 / 野排 / 私密 / 查询中。不需要 API Key：未填写时自动读取对方
  公开的社区好友页
  Friends column: 好友 (premade pair) / 野排 (not friends) / 私密 (list hidden) /
  查询中 (looking up); works without an API key via the public community friends page
- 查询策略：读到结果就定住、不再重复查询；没读到时每 2 分钟重试；检测到新玩家
  （新的一局）时清空缓存重新查询
  Lookups are done once and kept; empty results retry every 2 min, and a newly
  matched player invalidates the cached results for the whole lobby
- 游戏退出时工具立即关闭，而且**退出路径完全不碰界面线程**：独立线程每 0.2 秒查一次
  游戏窗口，窗口消失后就在它自己的线程里保存配置/设置、停掉 ETW，然后结束进程——窗口
  随进程一起消失，所以不会再出现“先冻住再退”。不会自动重启，下一局重新打开即可
  The tool exits as soon as the game window is gone, and the exit path never touches the
  UI thread: a watchdog thread polls every 0.2 s, saves config/settings and stops ETW on
  its own thread, then ends the process, so the windows disappear with it instead of
  sitting there frozen. It does not restart itself; reopen it and auto attach does the rest.

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
- “关系”列的四种状态？“好友”= 互为 Steam 好友（大概率双排）；“野排”= 已确定不是
  好友；“私密”= 双方好友列表都隐藏，无法判断（不会误报）；“查询中”= 正在读取。
  Relation states? 好友 = mutual Steam friends; 野排 = proven not friends;
  私密 = both lists hidden, so the tool refuses to guess; 查询中 = lookup running.
- 关掉游戏后 Steam 还显示“游戏中”？工具也被 Steam 算作游戏进程，所以它必须一起
  退出，Steam 才会解除该状态。实测 Steam 只会在自己的清理周期里删掉工具的进程
  记录（一次实测为 4 分 39 秒），所以游戏和工具都退出后状态仍可能残留一会儿；
  对照组（不开工具）约 10 秒即恢复。工具不会自动重启，下一局重新打开即可。
  Steam still shows "in game"? The tool counts as a game process too, so it has to
  exit for Steam to clear that. Measured: Steam only drops the tool's process record
  on its own schedule (4 min 39 s in one test), so the state can linger after both
  the game and the tool are gone; without the tool it recovers in ~10 s. Automatic
  restart is off by default to keep that phase unambiguous.
- 游戏退出后工具会卡一下？根因是界面线程自己在调 Steam 接口（昵称每次重绘实时查询、
  每 6 秒一条刷新日志用的空消息、玩家连接状态查询），以及悬浮窗每秒跨进程重排游戏窗口
  Z 序（同步 SetWindowPos）。现在这些调用全部挪到后台线程 / 改成 `SWP_ASYNCWINDOWPOS`
  异步方式，退出按钮也不再依赖界面线程（实测跨线程 ShowWindow 会等界面线程 4.6 秒，
  隐藏这条路根本不通，已弃用）：监测线程存好配置后结束进程，窗口随进程消失。
  日志里每次退出有两行毫秒级记录，界面线程卡超过 1.5 秒还会写 `[UI] no timer tick ...`。
  Tool freezes on game exit? The UI thread was calling into Steam itself (persona names on
  every repaint, a 6-second dummy IPC message, peer connection state) and the overlay
  re-ordered the game window once per second with a synchronous cross-process SetWindowPos.
  All of that moved to worker threads / SWP_ASYNCWINDOWPOS. The exit path needs nothing from
  the UI thread either (a cross-thread ShowWindow waited 4.6 s in a test, so hiding windows
  is not an option): the watchdog saves the config and ends the process. Each exit logs two
  millisecond-stamped lines, plus a `[UI] no timer tick for N ms` probe past 1.5 s.
- 全屏独占不显示悬浮窗：悬浮窗仅支持窗口化 / 无边框。
  Overlay works only in windowed / borderless mode.

## 许可 / License

[MIT](LICENSE) · Copyright (c) 2022 William Tremblay, 2026 HiKi

## 免责声明 / Disclaimer

本工具为被动监控工具：不注入进程、不修改游戏、不读取内存，仅读取 Steam 日志与
网络包。使用第三方工具进行联机游戏存在一定风险，请自行评估。
/ A passive monitor: no injection, no memory reading. Use with online
anti-cheat games at your own risk.
