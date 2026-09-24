using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace SteamP2PInfo
{
    public static class Logger
    {
        private static StreamWriter fs;
        private static DateTime lastLogCreated;
        private static string lastLoggedGame = "";

        /// <summary>
        /// The log is written from the UI thread, the peer worker thread and the exit
        /// watchdog, so every write is serialised here. Failures are swallowed: logging
        /// must never be the reason the tool dies or hangs.
        /// </summary>
        private static readonly object mLogLock = new object();

        private static void CreateOrOpenLogFile()
        {
            DateTime dateTime = DateTime.Now;

            if (fs == null || lastLogCreated.Day != dateTime.Day || lastLoggedGame != Config.GameConfig.Current.ProcessName)
            {
                if (fs != null)
                {
                    fs.Close();
                    fs.Dispose();
                }

                string logDir = $"logs\\{Config.GameConfig.Current.ProcessName}\\";
                Directory.CreateDirectory(logDir);

                fs = File.AppendText(Path.Combine(logDir, $"{Config.GameConfig.Current.ProcessName}-{dateTime:yyyy-MM-dd}.log"));
                fs.AutoFlush = true;
                lastLogCreated = dateTime;
                lastLoggedGame = Config.GameConfig.Current.ProcessName;
            }
        }

        public static void Write(string message)
        {
            try
            {
                if (Config.GameConfig.Current == null || !Config.GameConfig.Current.LogActivity) return;
                lock (mLogLock)
                {
                    CreateOrOpenLogFile();
                    if (fs != null) fs.Write($"[{DateTime.Now:HH:mm:ss.ff}] {message}");
                }
            }
            catch (Exception)
            {
            }
        }

        public static void WriteLine(string message)
        {
            try
            {
                if (Config.GameConfig.Current == null || !Config.GameConfig.Current.LogActivity) return;
                lock (mLogLock)
                {
                    CreateOrOpenLogFile();
                    if (fs != null) fs.WriteLine($"[{DateTime.Now:HH:mm:ss.ff}] {message}");
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
