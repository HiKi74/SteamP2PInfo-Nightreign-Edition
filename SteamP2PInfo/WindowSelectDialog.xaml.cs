using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Diagnostics;
using SteamP2PInfo.WinAPI;
using MahApps.Metro.Controls;

namespace SteamP2PInfo
{
    /// <summary>
    /// Interaction logic for WindowSelectDialog.xaml
    /// </summary>
    public partial class WindowSelectDialog : MetroWindow
    {
        public class WindowInfo
        {
            public IntPtr Handle;
            public string Title;
            public string ProcessName;
            public uint ProcessId;
            public uint ThreadId;
        }

        /// <summary>
        /// Finds the visible ELDEN RING NIGHTREIGN game window (process
        /// "nightreign"). Returns null when the game is not running.
        /// </summary>
        public static WindowInfo FindNightreignWindow()
        {
            WindowInfo best = null;
            long bestArea = 0;
            IntPtr shellWindow = User32.GetShellWindow();

            User32.EnumWindows((hWnd, lParam) =>
            {
                if (hWnd == shellWindow || !User32.IsWindowVisible(hWnd))
                    return true;

                int length = User32.GetWindowTextLength(hWnd);
                if (length == 0)
                    return true;

                StringBuilder builder = new StringBuilder(length);
                User32.GetWindowText(hWnd, builder, length + 1);

                uint processId;
                uint threadId = User32.GetWindowThreadProcessId(hWnd, out processId);
                string processName;
                try
                {
                    processName = Process.GetProcessById((int)processId).ProcessName;
                }
                catch (Exception)
                {
                    return true;
                }

                if (processName != Config.GameConfig.NightreignProcessName)
                    return true;

                WindowInfo wInfo = new WindowInfo()
                {
                    Handle = hWnd,
                    Title = builder.ToString(),
                    ProcessName = processName,
                    ProcessId = processId,
                    ThreadId = threadId
                };

                // Prefer the largest window (the actual game, not any small helper UI).
                if (User32.GetWindowRect(hWnd, out RECT rect))
                {
                    long area = (long)(rect.x2 - rect.x1) * (rect.y2 - rect.y1);
                    if (best == null || area > bestArea)
                    {
                        bestArea = area;
                        best = wInfo;
                    }
                }
                else if (best == null)
                {
                    best = wInfo;
                }

                return true;
            }, 0);

            return best;
        }

        private List<WindowInfo> windows = new List<WindowInfo>();
        public WindowInfo SelectedWindow = null;

        public WindowSelectDialog()
        {
            InitializeComponent();

            IntPtr shellWindow = User32.GetShellWindow();

            User32.EnumWindows((hWnd, lParam) =>
            {
                if (hWnd == shellWindow) return true;
                if (!User32.IsWindowVisible(hWnd)) return true;

                int length = User32.GetWindowTextLength(hWnd);
                if (length == 0) return true;

                StringBuilder builder = new StringBuilder(length);
                User32.GetWindowText(hWnd, builder, length + 1);

                WindowInfo wInfo = new WindowInfo() { Handle = hWnd, Title = builder.ToString() };
                wInfo.ThreadId = User32.GetWindowThreadProcessId(hWnd, out wInfo.ProcessId);
                wInfo.ProcessName = Process.GetProcessById((int)wInfo.ProcessId).ProcessName;
                windows.Add(wInfo);

                return true;
            }, 0);

            foreach (WindowInfo wInfo in windows)
            {
                WindowListBox.Items.Add($"{wInfo.Title} ({wInfo.ProcessName})");
            }
        }

        private void WindowListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectedWindow = windows[WindowListBox.SelectedIndex];
            DialogResult = true;
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedWindow = windows[WindowListBox.SelectedIndex];
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void WindowListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            btnOpen.IsEnabled = true;
        }
    }
}
