using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SteamP2PInfo
{
    /// <summary>
    /// Resolves the Steam installation folder from the registry and keeps the
    /// configured log paths in sync (Steam may be installed outside of the
    /// default C:\Program Files (x86)\Steam location).
    /// </summary>
    static class SteamPaths
    {
        /// <summary>
        /// If the currently configured IPC log path is missing, try to find the
        /// real Steam folder via the registry and update the settings.
        /// </summary>
        public static void EnsureConfigured()
        {
            try
            {
                string logDir = Path.GetDirectoryName(Settings.Default.SteamLogPath);
                if (!string.IsNullOrEmpty(logDir) && Directory.Exists(logDir))
                    return;

                string steamPath = GetSteamInstallPath();
                if (string.IsNullOrEmpty(steamPath))
                    return;

                string logsDir = Path.Combine(steamPath, "logs");
                if (!Directory.Exists(logsDir))
                    return;

                Settings.Default.SteamLogPath = Path.Combine(logsDir, "ipc_SteamClient.log");
                Settings.Default.SteamBootstrapLogPath = Path.Combine(logsDir, "bootstrap_log.txt");
                Settings.Default.Save();
            }
            catch (Exception)
            {
                // Never block the UI over settings detection.
            }
        }

        public static string GetSteamInstallPath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string path = key?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }
            }
            catch (Exception)
            {
                // Fall through.
            }

            // Fallback: derive the path from the running Steam client.
            try
            {
                foreach (Process process in Process.GetProcessesByName("steam"))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(process.MainModule.FileName);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            return dir;
                    }
                    catch (Exception)
                    {
                        // Try the next steam process.
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        public static string GetConsoleLogPath()
        {
            return Path.Combine(Path.GetDirectoryName(Settings.Default.SteamLogPath) ?? "", "console_log.txt");
        }

        public static string GetSteamExePath()
        {
            string installPath = GetSteamInstallPath();
            if (string.IsNullOrEmpty(installPath))
                return null;
            string exe = Path.Combine(installPath, "steam.exe");
            return File.Exists(exe) ? exe : null;
        }
    }
}
