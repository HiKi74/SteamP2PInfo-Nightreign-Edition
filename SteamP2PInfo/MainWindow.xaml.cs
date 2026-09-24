using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using System.IO;
using Steamworks;
using System.Security.Permissions;
using System.Media;
using SteamP2PInfo.Config;

namespace SteamP2PInfo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private ObservableCollection<SteamPeerBase> peers;
        private OverlayWindow overlay;
        private Timer timer;
        private int timerTicks = 0;
        private int overlayHotkey = 0;
        private int previousPeersAmount = 0;

        private const string STEAM_IPC_FILTER = "BeginAuthSession,EndAuthSession,LeaveLobby,SendClanChatMessage";
        private const string STEAM_COMMAND = "log_ipc \"" + STEAM_IPC_FILTER + "\"";
        private const string IPC_LOGGING_MARKER = "Started IPC logging for BeginAuthSession,EndAuthSession,LeaveLobby,SendClanChatMessage.";

        /// <summary>Set while an attach attempt is running, so only one runs at a time.</summary>
        private bool mAttachInProgress;

        /// <summary>Set when the game window disappeared and the tool is going down.</summary>
        private volatile bool mExiting;

        private DispatcherTimer autoAttachTimer;
        private DateTime autoAttachRetryAfter = DateTime.MinValue;

        private WindowSelectDialog.WindowInfo wInfo;

        public MainWindow()
        {
            if (Process.GetProcessesByName("SteamP2PInfo").Length > 1)
            {
                MessageBox.Show("Steam P2P Info 已有一个实例在运行，不能同时打开两个。", "程序已在运行", MessageBoxButton.OK, MessageBoxImage.Stop);
                Close();
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowUnhandledException((Exception)e.ExceptionObject, "CurrentDomain", e.IsTerminating);
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                e.SetObserved();
                // Background task failures are usually not actionable for the
                // user; record them so we can diagnose, but don't spam dialogs.
                LogCrash(e.Exception, "TaskScheduler");
            };
            Dispatcher.UnhandledException += (s, e) => { if (!Debugger.IsAttached) ShowUnhandledException(e.Exception, "Dispatcher", true); };

            InitializeComponent();
            SteamPaths.EnsureConfigured();
            Closing += MainWindow_Closed;

            // The tool starts unattached and waits for the game, so the Nightreign
            // config is loaded up front - the automatic attach reads its setting
            // from there.
            try
            {
                GameConfig.LoadOrCreate(GameConfig.NightreignProcessName);
            }
            catch (Exception ex)
            {
                LogCrash(ex, "Config");
            }

            peers = new ObservableCollection<SteamPeerBase>();
            dataGridSession.DataContext = peers;
            Title = "Steam P2P Info " + VersionCheck.CurrentVersion + "（黑夜君临）";

            timer = new Timer(Timer_Tick, null, Timeout.Infinite, Timeout.Infinite);
            Settings.Default.PropertyChanged += (s, e) => Settings.Default.Save();

            Task.Run(() =>
            {
                if (VersionCheck.FetchLatest())
                {
                    string v = VersionCheck.LatestRelease["tag_name"].ToString();
                    if (string.Compare(VersionCheck.CurrentVersion, v) < 0)
                    {
                        this.Invoke(() =>
                        {
                            linkUpdate.NavigateUri = new Uri("https://github.com/tremwil/SteamP2PInfo/releases/tag/" + v);
                            textUpdate.Text = string.Format("发现新版本（{0}），点击下载", v);
                            this.ShowMessageAsync("发现新版本", string.Format("新版本 {0} 已发布！点击标题栏链接下载。", v));
                        });
                    }
                }
            });

            // Silently enable Steam IPC logging as early as possible so peers
            // connecting before "ATTACH GAME" is clicked are still detected.
            Task.Run(() =>
            {
                try
                {
                    if (Process.GetProcessesByName("steam").Length > 0)
                        AutoRunSteamCommand();
                }
                catch (Exception)
                {
                }
            });

            // Look for the game window while unattached and attach on its own.
            StartAutoAttachWatch();
        }

        private void Timer_Tick(object o)
        {
            this.Invoke(() =>
            {
                // Necessary to close the program after the game exits, as SteamAPI_Shutdown isn't
                // sufficient to have steam recognize the game is no longer running
                if (!WinAPI.User32.IsWindow(wInfo.Handle))
                {
                    // Shutting down takes a moment while this timer keeps ticking once
                    // per second, so stop it first.
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                    Close();
                    return;
                }

                if (HotkeyManager.Enabled && !GameConfig.Current.HotkeysEnabled)
                    HotkeyManager.Disable();

                if (!HotkeyManager.Enabled && GameConfig.Current.HotkeysEnabled)
                    HotkeyManager.Enable();

                if ((timerTicks = (timerTicks + 1) % 6) == 0)
                {
                    // Rather not have the settings update on a loop, but 
                    // Fody generated OnChange seems to break PropertyChanged 
                    // for GameConfig. So do this for now.
                    GameConfig.Current?.Save();
                    SteamPeerManager.UpdatePeerList();
                }

                peers.Clear();
                foreach (SteamPeerBase p in SteamPeerManager.GetPeers())
                    peers.Add(p);

                if (GameConfig.Current.PlaySoundOnNewSession)
                {
                    if (peers.Count > 0 && previousPeersAmount == 0)
                    {
                        SystemSounds.Beep.Play();
                    }
                    previousPeersAmount = peers.Count;
                }

                // Update session info column sizes
                foreach (var col in dataGridSession.Columns)
                {
                    col.Width = new DataGridLength(1, DataGridLengthUnitType.Pixel);
                    col.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
                }
                dataGridSession.UpdateLayout();

                // Overlay columns use fixed widths (defined in XAML) so the
                // columns stay at stable positions regardless of name length;
                // only the Steam ID width is derived from the configured font.
                double idWidth = MeasureSteamIdColumnWidth();
                var idColumn = overlay.dataGrid.Columns.FirstOrDefault(c => c.Header != null && c.Header.ToString() == "Steam ID");
                if (idColumn != null)
                    idColumn.Width = new DataGridLength(idWidth);
                overlay.dataGrid.UpdateLayout();

                // Queue position update after the overlay has re-rendered
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(overlay.UpdatePosition));
                overlay.UpdateVisibility();
            });
        }

        /// <summary>
        /// Estimates the pixel width needed to display a full 17-digit Steam ID
        /// using the configured overlay font.
        /// </summary>
        private double MeasureSteamIdColumnWidth()
        {
            try
            {
                string fontString = GameConfig.Current.OverlayConfig.Font;
                var converter = System.ComponentModel.TypeDescriptor.GetConverter(typeof(System.Drawing.Font));
                var font = (System.Drawing.Font)converter.ConvertFromInvariantString(fontString);

                var typeface = new Typeface(
                    new FontFamily(font.Name),
                    FontStyles.Normal,
                    font.Bold ? FontWeights.Bold : FontWeights.Regular,
                    FontStretches.Normal);

                double sizeDips = font.SizeInPoints * 96.0 / 72.0;
                var formatted = new FormattedText("00000000000000000",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, sizeDips, Brushes.Black, 1.0);

                return Math.Ceiling(formatted.Width) + 14;
            }
            catch (Exception)
            {
                return 190;
            }
        }

        private void ShowUnhandledException(Exception err, string type, bool fatal)
        {
            LogCrash(err, type);

            // WPF windows may only be created on the UI thread. This handler can
            // be invoked from background threads (task failures, ETW thread), so
            // marshal to the dispatcher before showing any dialog.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowUnhandledException(err, type, fatal)));
                return;
            }

            MetroDialogSettings diagSettings = new MetroDialogSettings()
            {
                ColorScheme = MetroDialogColorScheme.Accented,
                AffirmativeButtonText = "复制",
                NegativeButtonText = "关闭"
            };

            SystemSounds.Exclamation.Play();
            var result = this.ShowModalMessageExternal($"未处理异常：{err.GetType().Name}", $"{err.Message}\n{err.StackTrace}", MessageDialogStyle.AffirmativeAndNegative, diagSettings);
            if (result == MessageDialogResult.Affirmative)
                Clipboard.SetText($"{err.GetType().Name}: {err.Message}\n{err.StackTrace}");

            Close();
        }

        /// <summary>
        /// Always records an exception to crash.log so crashes can be diagnosed
        /// even if no dialog is shown.
        /// </summary>
        private void LogCrash(Exception err, string type)
        {
            try
            {
                File.AppendAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {type}: {err}\n\n");
            }
            catch (Exception)
            {
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            if (GameConfig.Current != null) GameConfig.Current.Save();
            Settings.Default.Save();
            if (overlay != null) overlay.Close();
            HotkeyManager.Disable();
            ETWPingMonitor.Stop();
        }

        /// <summary>
        /// While the tool is unattached it keeps looking for the game window and
        /// attaches on its own as soon as Nightreign shows up. Together with the
        /// shutdown when the game ends this makes the tool hands free: start it once
        /// and every session is covered.
        /// </summary>
        private void StartAutoAttachWatch()
        {
            autoAttachTimer = new DispatcherTimer();
            autoAttachTimer.Interval = TimeSpan.FromSeconds(5);
            autoAttachTimer.Tick += async (sender, args) =>
            {
                if (wInfo != null || mAttachInProgress || mExiting)
                    return;

                if (GameConfig.Current == null || !GameConfig.Current.AutoAttach)
                    return;

                if (DateTime.UtcNow < autoAttachRetryAfter)
                    return;

                if (WindowSelectDialog.FindNightreignWindow() == null)
                    return;

                mAttachInProgress = true;
                try
                {
                    Logger.WriteLine("[ATTACH] game window found, attaching automatically");

                    if (!await AttachToGame(true))
                        autoAttachRetryAfter = DateTime.UtcNow.AddSeconds(30);
                }
                catch (Exception ex)
                {
                    LogCrash(ex, "AutoAttach");
                    autoAttachRetryAfter = DateTime.UtcNow.AddSeconds(30);
                }
                finally
                {
                    mAttachInProgress = false;
                }
            };
            autoAttachTimer.Start();
        }

        /// <summary>
        /// Once attached, a dedicated thread watches the game window. The UI thread
        /// cannot do that job reliably: Steam calls block for seconds while the game
        /// shuts down, which froze the window and delayed the shutdown - and the
        /// shutdown is what clears Steam's "in game" state.
        /// </summary>
        private void StartGameExitWatch()
        {
            IntPtr gameWindow = wInfo.Handle;

            Thread watch = new Thread(() =>
            {
                while (!mExiting)
                {
                    Thread.Sleep(500);
                    if (WinAPI.User32.IsWindow(gameWindow))
                        continue;

                    mExiting = true;
                    Logger.WriteLine("[LAUNCH] game window is gone, shutting down");

                    // Do the slow work on this thread: the UI thread may well be
                    // stuck inside a Steam call at this very moment.
                    try { ETWPingMonitor.Stop(); } catch (Exception) { }
                    try { GameConfig.Current?.Save(); } catch (Exception) { }

                    try { Dispatcher.BeginInvoke(new Action(Close)); } catch (Exception) { }

                    // Never hang on the way out: Steam only clears "in game" once
                    // this process is really gone.
                    Thread.Sleep(3000);
                    Environment.Exit(0);
                }
            });
            watch.IsBackground = true;
            watch.Name = "GameExitWatch";
            watch.Start();
        }


        private void headerFmt_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Process.Start("https://docs.microsoft.com/en-us/dotnet/standard/base-types/custom-date-and-time-format-strings");
        }

        private void webLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private async void labelGameState_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            if (wInfo != null || mAttachInProgress)
                return;

            mAttachInProgress = true;
            try
            {
                await AttachToGame(false);
            }
            finally
            {
                mAttachInProgress = false;
            }
        }

        /// <summary>
        /// Attaches to the Nightreign window. With <paramref name="quiet"/> set it
        /// never shows a dialog: the automatic attach uses that mode and simply
        /// tries again later instead of interrupting whatever the user is doing.
        /// </summary>
        private async Task<bool> AttachToGame(bool quiet)
        {
            SteamPaths.EnsureConfigured();

            // This build is dedicated to ELDEN RING NIGHTREIGN: look for the game
            // window directly instead of asking the user to pick one.
            WindowSelectDialog.WindowInfo selected = WindowSelectDialog.FindNightreignWindow();
            if (selected == null)
            {
                if (!quiet)
                    MessageBox.Show(
                        "未检测到《艾尔登法环 黑夜君临》的游戏窗口。\n请先启动游戏并进入游戏画面，再点击“附加游戏”。",
                        "未找到游戏", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            GameConfig.LoadOrCreate(selected.ProcessName);

            if (!Directory.Exists(System.IO.Path.GetDirectoryName(Settings.Default.SteamLogPath)))
            {
                if (!quiet)
                    MessageBox.Show("找不到 Steam 日志目录，请检查 Steam 是否安装正确。", "目录不存在", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // Nightreign's App ID is filled in automatically; keep the prompt as a
            // safety net in case the config is missing it.
            if (GameConfig.Current.SteamAppId == 0)
            {
                // The automatic attach must not ask questions: a config that lost
                // its App ID is filled in again on the next load.
                if (quiet)
                    return false;

                string input = Microsoft.VisualBasic.Interaction.InputBox("请输入该游戏的 Steam App ID：", "Steam App ID Required");
                if (!uint.TryParse(input, out uint result))
                {
                    MessageBox.Show("请输入有效的数字", "输入错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                GameConfig.Current.SteamAppId = (int)result;
            }

            textGameState.Text = "正在附加...";
            textGameState.Foreground = Brushes.Orange;

            // SteamAPI.Init() performs a handshake with the Steam client and can
            // take a second or two. Run it off the UI thread so the window stays
            // responsive while attaching.
            bool initOk;
            try
            {
                initOk = await Task.Run(() =>
                {
                    Environment.SetEnvironmentVariable("SteamAppId", GameConfig.Current.SteamAppId.ToString());
                    return SteamAPI.Init();
                });
            }
            catch (Exception)
            {
                initOk = false;
            }

            if (!initOk)
            {
                if (!quiet)
                    MessageBox.Show("Steam API 初始化失败，请确认 Steam 已登录且游戏正在运行。", "Steam API Error", MessageBoxButton.OK, MessageBoxImage.Error);

                GameConfig.Current.SteamAppId = 0;
                GameConfig.Current.Save();
                textGameState.Text = "附加游戏";
                textGameState.Foreground = Brushes.Orange;
                return false;
            }

            wInfo = selected;
            SteamPeerManager.Init();

            // Enables IPC logging implicitly (no console UI involved); the
            // command was already sent at startup when possible.
            AutoRunSteamCommand();

            HotkeyManager.RemoveHotkey(overlayHotkey);
            overlayHotkey = HotkeyManager.AddHotkey(wInfo.Handle, () => GameConfig.Current.OverlayConfig.Hotkey, () => GameConfig.Current.OverlayConfig.Enabled ^= true);

            overlay = new OverlayWindow(wInfo.Handle, wInfo.ProcessId, wInfo.ThreadId);
            overlay.dataGrid.DataContext = peers;
            if (!overlay.InstallMsgHook())
            {
                overlay.Close();
                MessageBox.Show("悬浮窗消息钩子设置失败", "WINAPI Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return false;
            }

            textGameState.Text = wInfo.Title;
            textGameState.Foreground = Brushes.LawnGreen;

            Grid configEditor = ConfigUIBuilder.CreateConfigEditor(GameConfig.Current);
            ConfigTab.Children.Add(configEditor);

            timer.Change(0, 1000);

            Logger.WriteLine("[ATTACH] attached to \"" + wInfo.Title + "\" (pid " + wInfo.ProcessId +
                "), watching the window for the exit");

            StartGameExitWatch();
            return true;
        }

        private bool MustEnterSteamCommand()
        {
            DateTime ipcLogDate;
            String startupDateString = null;

            // Check if the program was recently updated -- We'll want to enter the command again if so
            if (Settings.Default.LastRunVersion != VersionCheck.CurrentVersion)
            {
                Settings.Default.LastRunVersion = VersionCheck.CurrentVersion;
                Settings.Default.Save();
                return true;
            }

            if (!File.Exists(Settings.Default.SteamLogPath))
                return true;

            try
            {
                ipcLogDate = File.GetLastWriteTime(Settings.Default.SteamLogPath);

            } catch (Exception)
            {
                return true;
            }

            try
            {
                using (FileStream stream = new FileStream(Settings.Default.SteamBootstrapLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var reader = new ReverseTextReader(stream, Encoding.UTF8);
                    var today = DateTime.Today;
                    int dateCheckCountdown = 20;
                    while (!reader.EndOfStream)
                    {
                        String line = reader.ReadLine();
                        if (line.Trim().Length == 0)
                            continue;
                        else if (line.Contains("Startup - updater built"))
                        {
                            int substringStartIndex = line.IndexOf("[") + 1;
                            startupDateString = line.Substring(substringStartIndex, line.IndexOf("]") - substringStartIndex);
                            break;
                        }
                        else
                        {
                            if (--dateCheckCountdown == 0)
                            {
                                int substringStartIndex = line.IndexOf("[") + 1;
                                DateTime lineDate = DateTime.Parse(line.Substring(substringStartIndex, line.IndexOf("]") - substringStartIndex));
                                if (today.Subtract(lineDate).TotalHours > 24)
                                    return true; // let's assume Steam hasn't been running for 24+ hours
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                return true;
            }

            return startupDateString == null || DateTime.Parse(startupDateString) > ipcLogDate;
        }

        private void SteamConsoleHelper()
        {
            Process.Start(new ProcessStartInfo("steam://open/console"));

            MetroDialogSettings diagSettings = new MetroDialogSettings()
            {
                ColorScheme = MetroDialogColorScheme.Accented,
                AffirmativeButtonText = "复制命令",
                NegativeButtonText = "关闭"
            };

            var result = this.ShowModalMessageExternal(
                "必要步骤", $"已打开 Steam 控制台。请在其中输入以下命令以开启联机调用日志：'{STEAM_COMMAND}'",
                MessageDialogStyle.AffirmativeAndNegative, diagSettings
            );
            if (result == MessageDialogResult.Affirmative)
            {
                try
                {
                    Clipboard.SetText(STEAM_COMMAND);
                }
                catch (Exception e)
                {
                    MessageBox.Show($"复制命令到剪贴板失败，请手动输入 '{STEAM_COMMAND}'。\n\n {e}", "剪贴板写入失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Ensures Steam IPC logging is enabled. This is done implicitly by
        /// launching "steam.exe +log_ipc ..." -- Steam executes the console
        /// command without needing the console UI to be open or focused. Falls
        /// back to the manual copy-paste dialog only if that fails.
        /// </summary>
        private void AutoRunSteamCommand()
        {
            Task.Run(() =>
            {
                if (TryRunSteamCommandImplicitly())
                    return;

                try
                {
                    this.Invoke(() => SteamConsoleHelper());
                }
                catch (Exception)
                {
                }
            });
        }

        /// <summary>
        /// Tries to enable IPC logging silently. Returns true when the
        /// confirmation line appears in console_log.txt.
        /// </summary>
        private bool TryRunSteamCommandImplicitly()
        {
            try
            {
                if (IsIpcLoggingActive())
                    return true;

                string steamExe = SteamPaths.GetSteamExePath();
                if (string.IsNullOrEmpty(steamExe) || !File.Exists(steamExe))
                    return false;

                ProcessStartInfo psi = new ProcessStartInfo(steamExe)
                {
                    Arguments = "+log_ipc \"" + STEAM_IPC_FILTER + "\"",
                    UseShellExecute = true
                };
                Process.Start(psi);

                // Wait for the confirmation line ("Started IPC logging...").
                for (int i = 0; i < 10; i++)
                {
                    if (IsIpcLoggingActive())
                        return true;
                    Thread.Sleep(500);
                }

                return IsIpcLoggingActive();
            }
            catch (Exception e)
            {
                Logger.WriteLine($"[AUTO COMMAND ERROR] {e}");
                return false;
            }
        }

        /// <summary>
        /// True if Steam recently logged the IPC calls we need. Steam writes a
        /// "Started IPC logging..." confirmation to console_log.txt when the
        /// log_ipc command takes effect; we look at the tail of that file.
        /// Re-running the command when logging is already active is harmless, so
        /// we only use a small window of recent lines to avoid stale markers.
        /// </summary>
        private bool IsIpcLoggingActive()
        {
            try
            {
                string consoleLogPath = SteamPaths.GetConsoleLogPath();
                if (string.IsNullOrEmpty(consoleLogPath) || !File.Exists(consoleLogPath))
                    return false;

                using (FileStream stream = new FileStream(consoleLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var reader = new ReverseTextReader(stream, Encoding.UTF8);
                    int lines = 0;
                    while (!reader.EndOfStream && lines < 30)
                    {
                        string line = reader.ReadLine();
                        lines++;
                        if (line == null)
                            continue;
                        // The most recent state wins: reading from the tail, a
                        // "stopped" line means logging is off even if an older
                        // "Started..." line is still present in the file.
                        if (line.Contains("IPC logging has been stopped"))
                            return false;
                        if (line.Contains(IPC_LOGGING_MARKER))
                            return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        private void dataGridSession_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            DependencyObject dep = (DependencyObject)e.OriginalSource;
            while ((dep != null) && !(dep is DataGridRow))
            {
                dep = VisualTreeHelper.GetParent(dep);
            }
            if (dep == null) return;

            if (dep is DataGridRow)
            {
                DataGridRow row = dep as DataGridRow;
                if (GameConfig.Current.OpenProfileInOverlay)
                    SteamFriends.ActivateGameOverlayToUser("steamid", peers[row.GetIndex()].SteamID);
                else
                    Process.Start($"https://steamcommunity.com/profiles/{peers[row.GetIndex()].SteamID}");
            }
        }
    }
}
