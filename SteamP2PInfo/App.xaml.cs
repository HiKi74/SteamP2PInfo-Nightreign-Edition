using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SteamP2PInfo
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 隐藏的辅助进程模式：由它向 Steam 登记为游戏进程，主工具在游戏
            // 退出后可以保持打开。游戏关闭时主工具杀掉这个辅助进程，Steam
            // 就会清除“游戏中”状态。
            if (e.Args != null && e.Args.Length > 0 && e.Args[0] == "--helper")
            {
                SteamHelper.Run(e.Args);
                Shutdown();
                return;
            }

            new MainWindow().Show();
        }
    }
}
