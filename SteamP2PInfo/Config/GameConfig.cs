using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Windows.Controls;
using MahApps.Metro.Controls;

namespace SteamP2PInfo.Config
{
    public class GameConfig : INotifyPropertyChanged
    {
        /// <summary>
        /// Nightreign-specific constants. This build of the tool is dedicated to
        /// ELDEN RING NIGHTREIGN (Steam App ID 2622380).
        /// </summary>
        public const string NightreignProcessName = "nightreign";
        public const string NightreignLauncherProcessName = "start_protected_game";
        public const int NightreignAppId = 2622380;

        public static bool IsNightreignProcess(string processName)
        {
            return processName == NightreignProcessName || processName == NightreignLauncherProcessName;
        }

        /// <summary>
        /// Name of the game process. Used to identify which config to load when attaching to a game, and to find the window.
        /// </summary>
        [JsonProperty("process_name")]
        public string ProcessName { get; set; } = "";

        /// <summary>
        /// The Steam App ID of the game.
        /// </summary>
        [JsonProperty("steam_appid")]
        public int SteamAppId { get; set; } = 0;

        /// <summary>
        /// If true, will call SteamFriends.SetPlayedWith on each detected peer. Mainly intended to add this
        /// feature to games that don't support it, like Elden Ring. 
        /// </summary>
        [JsonProperty("set_played_with")]
        [ConfigBindingElement("标记一起游玩", typeof(ToggleSwitch), "IsOnProperty",
            Tooltip: "开启后，联机玩家会出现在 Steam 的“最近一起游玩”列表中。",
            UIElementProperties: new object[] {
                new object[] { "OnContent", "开" },
                new object[] { "OffContent", "关" }
            })]
        public bool SetPlayedWith { get; set; } = false;

        [JsonProperty("open_profile_in_overlay")]
        [ConfigBindingElement("在 Steam 悬浮窗打开个人资料", typeof(ToggleSwitch), "IsOnProperty",
            Tooltip: "开启后，双击会话信息中的玩家昵称会在 Steam 悬浮窗内打开其个人资料；\n关闭则使用默认浏览器打开。",
            UIElementProperties: new object[] {
                new object[] { "OnContent", "开" },
                new object[] { "OffContent", "关" }
            })]
        public bool OpenProfileInOverlay { get; set; } = true;

        /// <summary>
        /// If true, will dump peer information into a game-specific log file.
        /// </summary>
        [JsonProperty("log_activity")]
        [ConfigBindingElement("记录活动日志", typeof(ToggleSwitch), "IsOnProperty",
            Tooltip: "开启后，每次玩家连接/断开都会记录到该游戏专属的日志文件。",
            UIElementProperties: new object[] {
                new object[] { "OnContent", "开" },
                new object[] { "OffContent", "关" }
            })]
        public bool LogActivity { get; set; } = false;

        /// <summary>
        /// If true, the hotkey system will be enabled while attached to this game.
        /// </summary>
        [JsonProperty("hotkeys_enabled")]
        [ConfigBindingElement("启用热键", typeof(ToggleSwitch), "IsOnProperty",
            Tooltip: "开启后，附加到该游戏期间热键系统生效。",
            UIElementProperties: new object[] {
                new object[] { "OnContent", "开" },
                new object[] { "OffContent", "关" }
            })]
        public bool HotkeysEnabled { get; set; } = true;

        /// <summary>
        /// If true, a sound will be played when a new multiplayer session is detected.
        /// </summary>
        [JsonProperty("play_sound_on_new_session")]
        [ConfigBindingElement("新联机会话提示音", typeof(ToggleSwitch), "IsOnProperty",
            Tooltip: "开启后，检测到新的联机会话时会播放提示音。",
            UIElementProperties: new object[] {
                new object[] { "OnContent", "开" },
                new object[] { "OffContent", "关" }
            })]
        public bool PlaySoundOnNewSession { get; set; } = false;

        /// <summary>
        /// Overlay configuration for this game. Includes things like placement, enabled/disabled, etc.
        /// </summary>
        [JsonProperty("overlay")]
        [ConfigCategory("悬浮窗配置")]
        public OverlayConfig OverlayConfig { get; private set; } = new OverlayConfig();

        /// <summary>
        /// Configuration of the currently selected game.
        /// </summary>
        public static GameConfig Current { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Load a settings file as the current game settings, or create a new file if the game does not have associated settings yet.
        /// </summary>
        /// <param name="processName"></param>
        public static bool LoadOrCreate(string processName)
        {
            if (!Directory.Exists("config"))
                Directory.CreateDirectory("config");

            if (!File.Exists($"config\\{processName}.json"))
            {
                Current = new GameConfig() { ProcessName = processName };
                Current.Save();
            }
            else
            {
                string json = File.ReadAllText($"config\\{processName}.json");
                Current = JsonConvert.DeserializeObject<GameConfig>(json);
            }

            // This build is dedicated to Nightreign: always fill in the correct App ID
            // so the user never has to type it manually.
            if (Current.SteamAppId == 0 && IsNightreignProcess(Current.ProcessName))
            {
                Current.SteamAppId = NightreignAppId;
                Current.Save();
            }

            return false;
        }

        public void Save()
        {
            string json = JsonConvert.SerializeObject(Current, Formatting.Indented);
            File.WriteAllText($"config\\{Current.ProcessName}.json", json);
        }
    }
}
