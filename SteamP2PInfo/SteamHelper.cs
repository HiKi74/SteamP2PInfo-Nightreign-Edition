using System;
using System.Diagnostics;
using System.Threading;
using Steamworks;

namespace SteamP2PInfo
{
    /// <summary>
    /// 隐藏的后台辅助进程：用游戏的 AppID 调用 SteamAPI.Init()，把自己登记成
    /// Steam 的“游戏进程”。主工具在游戏退出后仍保持打开，而 Steam 只有在登记
    /// 的进程结束时才会清除“游戏中”状态，所以主工具只需杀掉这个辅助进程即可。
    /// </summary>
    internal static class SteamHelper
    {
        public static void Run(string[] args)
        {
            int parentPid = 0;
            int appId = 0;
            if (args != null && args.Length >= 3)
            {
                int.TryParse(args[1], out parentPid);
                int.TryParse(args[2], out appId);
            }

            if (appId <= 0)
                return;

            try
            {
                Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
                if (!SteamAPI.Init())
                    return;

                while (true)
                {
                    Thread.Sleep(5000);

                    // 周期性写一条 IPC 日志，促使 Steam 刷新日志文件（与主工具
                    // 的 peer 检测机制相同）。
                    try
                    {
                        SteamFriends.SendClanChatMessage(new CSteamID(0), "");
                    }
                    catch (Exception)
                    {
                    }

                    // 主工具进程退出后，辅助进程自动结束，避免残留进程让 Steam
                    // 一直认为游戏在运行。
                    if (parentPid > 0)
                    {
                        try
                        {
                            Process.GetProcessById(parentPid);
                        }
                        catch (Exception)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                try
                {
                    SteamAPI.Shutdown();
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
