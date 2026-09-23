using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.ComponentModel;
using MahApps.Metro.Controls;
using System.Windows.Controls;
using System.Globalization;

namespace SteamP2PInfo.Config
{
    public class OverlayConfig : INotifyPropertyChanged
    {
        public class PingColorRange
        {
            [JsonProperty("threshold")]
            public double Threshold { get; set; }
            [JsonProperty("color")]
            public string Color { get; set; }
        }

        [JsonProperty("enabled")]
        [ConfigBindingElement("启用悬浮窗", typeof(ToggleSwitch), "IsOnProperty",
            Tooltip: "启用/禁用悬浮窗。",
            UIElementProperties: new object[] {
                new object[] { "OnContent", "开" },
                new object[] { "OffContent", "关" }
            })]
        public bool Enabled { get; set; } = true;

        [JsonProperty("show_steam_id")]
        [ConfigBindingElement("显示 Steam ID", typeof(ToggleSwitch), "IsOnProperty",
            Tooltip: "是否在悬浮窗中显示 64 位 Steam ID？",
            UIElementProperties: new object[] {
                new object[] { "OnContent", "开" },
                new object[] { "OffContent", "关" }
            })]
        public bool ShowSteamID { get; set; } = false;

        [JsonProperty("show_connection_quality")]
        [ConfigBindingElement("显示连接质量", typeof(ToggleSwitch), "IsOnProperty",
            Tooltip: "显示连接质量评分（0=最差，1=最佳）。",
            UIElementProperties: new object[] {
                new object[] { "OnContent", "开" },
                new object[] { "OffContent", "关" }
            })]
        public bool ShowConnectionQuality { get; set; } = true;

        [JsonProperty("show_playtime")]
        [ConfigBindingElement("显示黑夜君临游玩时长", typeof(ToggleSwitch), "IsOnProperty",
            Tooltip: "在悬浮窗中显示每位玩家的《黑夜君临》游玩时长。\n仅当对方 Steam 个人资料（游戏详情）公开时可见。",
            UIElementProperties: new object[] {
                new object[] { "OnContent", "开" },
                new object[] { "OffContent", "关" }
            })]
        public bool ShowPlaytime { get; set; } = true;

        [JsonProperty("show_relation")]
        [ConfigBindingElement("显示好友关系", typeof(ToggleSwitch), "IsOnProperty",
            Tooltip: "同一局里两名玩家互为 Steam 好友时，在对应行显示“好友”（大概率是双排）。\n需要在“配置”页填写 Steam Web API Key，且对方好友列表公开时才有效，否则该列留空。",
            UIElementProperties: new object[] {
                new object[] { "OnContent", "开" },
                new object[] { "OffContent", "关" }
            })]
        public bool ShowRelation { get; set; } = true;

        [JsonProperty("hotkey")]
        [ConfigBindingElement("悬浮窗热键", typeof(HotKeyBox), "HotKeyProperty",
            Tooltip: "用于显示/隐藏悬浮窗的热键。",
            ValueConverter: typeof(HotkeyConverter))]
        public int Hotkey { get; set; } = 0;

        [JsonProperty("banner_format")]
        [ConfigBindingElement("横幅格式", typeof(TextBox), "TextProperty",
            Tooltip: "悬浮窗顶部横幅文字的格式字符串。")]
        public string BannerFormat { get; set; } = "[{time:HH:mm:ss}] SteamP2PInfo_NightReign - by tremwil & HiKi";

        [JsonProperty("font")]
        [ConfigFontSelector("字体", "悬浮窗文字的字体。")]
        public string Font { get; set; } = "Segoe UI, 20.25pt";

        [JsonProperty("x_offset")]
        [ConfigBindingElement("水平偏移", typeof(NumericUpDown), "ValueProperty",
            Tooltip: "锚点起的水平偏移（占窗口宽度的百分比）。",
            UIElementProperties: new object[] {
                new object[] { "Minimum", 0d },
                new object[] { "Maximum", 1d },
                new object[] { "Interval", 0.005d },
                new object[] { "ParsingNumberStyle", NumberStyles.Float },
                new object[] { "StringFormat", "F3" }
            })]
        public double XOffset { get; set; } = 0.025;

        [JsonProperty("y_offset")]
        [ConfigBindingElement("垂直偏移", typeof(NumericUpDown), "ValueProperty",
            Tooltip: "锚点起的垂直偏移（占窗口高度的百分比）。",
            UIElementProperties: new object[] {
                new object[] { "Minimum", 0d },
                new object[] { "Maximum", 1d },
                new object[] { "Interval", 0.005d },
                new object[] { "ParsingNumberStyle", NumberStyles.Float },
                new object[] { "StringFormat", "F3" }
            })]
        public double YOffset { get; set; } = 0.025;

        [JsonProperty("anchor")]
        [ConfigEnumComboBox("锚点", typeof(OverlayAnchor), 
            Tooltip: "悬浮窗锚定在游戏窗口的哪个角。")]
        public OverlayAnchor Anchor { get; set; } = OverlayAnchor.TopRight;

        [JsonProperty("text_color")]
        [ConfigBindingElement("文字颜色", typeof(ColorPicker), "SelectedColorProperty",
            Tooltip: "悬浮窗文字颜色。")]
        public string TextColor { get; set; } = "#FFFFFFFF";

        [JsonProperty("stroke_color")]
        [ConfigBindingElement("描边颜色", typeof(ColorPicker), "SelectedColorProperty",
            Tooltip: "悬浮窗文字描边（轮廓）颜色。")]
        public string StrokeColor { get; set; } = "#FF000000";

        [JsonProperty("stroke_width")]
        [ConfigBindingElement("描边宽度", typeof(NumericUpDown), "ValueProperty",
            Tooltip: "悬浮窗文字描边（轮廓）宽度。",
            UIElementProperties: new object[] {
                new object[] { "Minimum", 0d },
                new object[] { "Interval", 0.5d },
                new object[] { "ParsingNumberStyle", NumberStyles.Float },
                new object[] { "StringFormat", "F1" }
            })]
        public double StrokeWidth { get; set; } = 2.0;

        [JsonProperty("ping_colors", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<PingColorRange> PingColors { get; set; } = new List<PingColorRange>()
        {
            new PingColorRange { Threshold = 0, Color = "#FF00BFFF" },
            new PingColorRange { Threshold = 50, Color = "#FF7CFC00" },
            new PingColorRange { Threshold = 100, Color = "#FFFFFF00" },
            new PingColorRange { Threshold = 200, Color = "#FFCD5C5C" }
        };

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
