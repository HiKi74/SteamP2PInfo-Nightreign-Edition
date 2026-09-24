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
- 自动附加：启动后常驻后台，检测到游戏窗口即自动附加；游戏退出后自动退出并稍后
  重启待命，多局无需手动操作（配置页可关闭）
  Auto attach: it watches for the game window and attaches by itself, so nothing has
  to be clicked between matches (can be turned off in the config tab)
- 自动静默开启 Steam IPC 日志（`steam.exe +log_ipc`），Steam 重启后自动重新开启
  Silently enables Steam IPC logging; re-enabled automatically after restarts
- 附加时回溯日志，找回已连上的玩家 / Backfills players connected before attach
- 游玩时长列（会话信息 + 悬浮窗）：公开资料显示小时数，私密显示“未公开”
  Playtime column: shows hours when public, "Private" / “未公开” otherwise
- 固定列宽悬浮窗，信息始终对齐 / Fixed-width overlay columns, always aligned
- 全中文 / 全英文双版本 / Fully Chinese & English builds
- 可选 Steam Web API Key，更稳定获取游玩时长 / Optional Web API key support
- 好友关系列：好友 / 野排 / 私密 / 查询中。不需要 API Key：未填写时自动读取对方
  公开的社区好友页
  Friends column: 好友 (premade pair) / 野排 (not friends) / 私密 (list hidden) /
  查询中 (looking up); works without an API key via the public community friends page
- 查询策略：读到结果就定住、不再重复查询；没读到时每 2 分钟重试；检测到新玩家
  （新的一局）时清空缓存重新查询
  Lookups are done once and kept; empty results retry every 2 min, and a newly
  matched player invalidates the cached results for the whole lobby
- 游戏退出后工具自动退出并重启一个新实例：退出才能让 Steam 解除“游戏中”，
- 游戏退出时工具立即关闭（独立线程 0.5 秒检测、3 秒硬上限强制退出）；默认不自动
  重启，下一局重新打开工具即可，配合“自动附加游戏”依旧无需手动点附加
  The tool exits as soon as the game window is gone (own thread, 0.5 s polling, hard
  3 s limit); automatic restart is off by default

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
  退出，Steam 才会解除该状态；退出前工具会自动启动一个新实例（标题栏显示
  “已自动重启，等待附加游戏”），下一局点“附加游戏”即可。手动关掉窗口不会再重启。
  Steam still shows "in game"? The tool counts as a game process too, so it has to
  exit for Steam to clear that. Measured: Steam only drops the tool's process record
  on its own schedule (4 min 39 s in one test), so the state can linger after both
  the game and the tool are gone; without the tool it recovers in ~10 s. Automatic
  restart is off by default to keep that phase unambiguous.
- 游戏退出后工具会卡一下？Steam 接口在游戏关闭瞬间会阻塞界面线程，V1.0.3 改为独立
  线程监测游戏窗口并负责退出（0.5 秒一次，最多等 3 秒后强制结束进程）。
  Tool freezes on game exit? Steam calls block the UI thread right when the game
  shuts down; V1.0.3 watches the window on its own thread instead.
- 全屏独占不显示悬浮窗：悬浮窗仅支持窗口化 / 无边框。
  Overlay works only in windowed / borderless mode.

## 许可 / License

[MIT](LICENSE) · Copyright (c) 2022 William Tremblay, 2026 HiKi

## 免责声明 / Disclaimer

本工具为被动监控工具：不注入进程、不修改游戏、不读取内存，仅读取 Steam 日志与
网络包。使用第三方工具进行联机游戏存在一定风险，请自行评估。
/ A passive monitor: no injection, no memory reading. Use with online
anti-cheat games at your own risk.
