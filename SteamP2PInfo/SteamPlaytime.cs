using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json.Linq;
using Steamworks;

namespace SteamP2PInfo
{
    /// <summary>
    /// Fetches ELDEN RING NIGHTREIGN playtime for a peer from their public Steam
    /// profile (the "Games" tab exposes playtime_forever as JSON). No API key is
    /// required, but the profile (and its game details) must be public.
    /// </summary>
    static class SteamPlaytime
    {
        private const int NIGHTREIGN_APPID = Config.GameConfig.NightreignAppId;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

        private const string LOADING_TEXT = "\u67E5\u8BE2\u4E2D"; // 查询中
        private const string PRIVATE_TEXT = "\u672A\u516C\u5F00"; // 未公开
        private const string ERROR_TEXT = "\u2014"; // —

        private class Entry
        {
            public string Text = LOADING_TEXT;
            public DateTime LastFetch = DateTime.MinValue;
            public bool Fetching = false;
        }

        private static readonly Dictionary<ulong, Entry> mCache = new Dictionary<ulong, Entry>();
        private static readonly object mLock = new object();

        private static readonly int[] PROXY_PORTS = { 7897, 7890, 7891, 7892, 10809, 1080, 2080, 8888, 33210 };
        private static WebProxy mCachedProxy;
        private static DateTime mLastProxyProbe = DateTime.MinValue;

        /// <summary>
        /// Returns the current display text for the given Steam ID. Triggers a
        /// background refresh when the cached value is missing or stale.
        /// </summary>
        public static string GetText(CSteamID steamId)
        {
            ulong id = steamId.m_SteamID;
            lock (mLock)
            {
                if (!mCache.TryGetValue(id, out Entry entry))
                {
                    entry = new Entry();
                    mCache[id] = entry;
                    StartFetch(id, entry);
                }
                else if (!entry.Fetching && DateTime.UtcNow - entry.LastFetch > RefreshInterval)
                {
                    StartFetch(id, entry);
                }

                return entry.Text;
            }
        }

        private static void StartFetch(ulong steamId, Entry entry)
        {
            entry.Fetching = true;
            Task.Run(() =>
            {
                try
                {
                    string text = FetchPlaytimeText(steamId);
                    lock (mLock)
                    {
                        entry.Text = text;
                        entry.LastFetch = DateTime.UtcNow;
                        entry.Fetching = false;
                    }
                }
                catch (Exception)
                {
                    lock (mLock)
                    {
                        entry.Fetching = false;
                    }
                }
            });
        }

        private static string FetchPlaytimeText(ulong steamId)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                WebProxy proxy = FindLocalProxy();

                // 1) If a Steam Web API key is configured, use GetOwnedGames --
                //    the most reliable source for playtime.
                string apiKey = Settings.Default.SteamWebApiKey;
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    string apiResult = TryFetchFromWebApi(apiKey, steamId, proxy);
                    if (apiResult != null)
                        return apiResult;
                }

                // 2) Public profile "Games" page (no key required).
                string html;
                try
                {
                    html = DownloadString($"https://steamcommunity.com/profiles/{steamId}/games?tab=all", proxy);
                }
                catch (Exception)
                {
                    // Network problem (blocked / no proxy): show a placeholder
                    // instead of "not public".
                    return ERROR_TEXT;
                }

                long? minutes = ParsePlaytimeMinutes(html);
                return minutes == null ? PRIVATE_TEXT : FormatHours(minutes.Value);
            }
            catch (Exception)
            {
                return ERROR_TEXT;
            }
        }

        /// <summary>
        /// Queries IPlayerService/GetOwnedGames for Nightreign playtime.
        /// Returns null on network failure (caller should try another source),
        /// otherwise the display text (hours or "not public").
        /// </summary>
        private static string TryFetchFromWebApi(string apiKey, ulong steamId, WebProxy proxy)
        {
            try
            {
                string url =
                    $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/" +
                    $"?key={Uri.EscapeDataString(apiKey)}&steamid={steamId}" +
                    $"&include_appinfo=true&appids_filter[0]={Config.GameConfig.NightreignAppId}&format=json";

                string json = DownloadString(url, proxy);
                JObject root = JObject.Parse(json);
                JToken response = root["response"];
                JArray games = response?["games"] as JArray;
                if (games == null || games.Count == 0)
                    return PRIVATE_TEXT;

                long minutes = (long?)games[0]["playtime_forever"] ?? 0;
                return FormatHours(minutes);
            }
            catch (WebException)
            {
                // Network failure: fall back to the community page.
                return null;
            }
            catch (Exception)
            {
                // Malformed response: treat as not visible.
                return PRIVATE_TEXT;
            }
        }

        private static string DownloadString(string url, WebProxy proxy)
        {
            // Use HttpWebRequest with explicit timeouts so a blocked network
            // (e.g. steamcommunity unreachable) fails fast instead of hanging
            // background threads for minutes.
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;
            request.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
            request.Proxy = proxy ?? WebRequest.DefaultWebProxy;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Detects a local HTTP proxy (e.g. Clash Verge) by probing common ports.
        /// The result is cached for a few minutes so we don't slow down every fetch.
        /// </summary>
        private static WebProxy FindLocalProxy()
        {
            lock (mLock)
            {
                if (DateTime.UtcNow - mLastProxyProbe < TimeSpan.FromMinutes(5))
                    return mCachedProxy;

                mLastProxyProbe = DateTime.UtcNow;
                mCachedProxy = null;

                foreach (int port in PROXY_PORTS)
                {
                    try
                    {
                        using (TcpClient client = new TcpClient())
                        {
                            // Synchronous probe: connections to closed loopback
                            // ports fail immediately, and no dangling async task
                            // is left behind to fault later on another thread.
                            client.Connect("127.0.0.1", port);
                            mCachedProxy = new WebProxy($"http://127.0.0.1:{port}");
                            break;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                return mCachedProxy;
            }
        }

        /// <summary>
        /// Extracts playtime (in minutes) for Nightreign from the profile "Games" page.
        /// Returns null when the data is not visible (private profile / private game
        /// details / game not listed).
        /// </summary>
        private static long? ParsePlaytimeMinutes(string html)
        {
            // Primary: parse the g_rgGames array embedded in the games tab.
            Match match = Regex.Match(html,
                @"(?:var\s+)?g_rgGames\s*=\s*(\[.*?\])\s*;",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (match.Success)
            {
                try
                {
                    JArray games = JArray.Parse(match.Groups[1].Value);
                    foreach (JToken token in games)
                    {
                        if ((int?)token["appid"] == NIGHTREIGN_APPID)
                        {
                            long minutes = (long?)token["playtime_forever"] ?? 0;
                            return minutes;
                        }
                    }
                    return null;
                }
                catch (Exception)
                {
                    // Fall through to the direct-scan fallback below.
                }
            }

            // Fallback: locate the game entry directly and read playtime nearby,
            // which is robust against changes in the page's surrounding markup.
            Match appMatch = Regex.Match(html,
                "\"appid\"\\s*:\\s*" + NIGHTREIGN_APPID + "(?![0-9])",
                RegexOptions.IgnoreCase);
            if (appMatch.Success)
            {
                string tail = html.Substring(appMatch.Index, Math.Min(800, html.Length - appMatch.Index));
                Match pt = Regex.Match(tail, "\"playtime_forever\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);
                if (pt.Success)
                    return long.Parse(pt.Groups[1].Value);

                Match hours = Regex.Match(tail, "\"hours_forever\"\\s*:\\s*\"([\\d.]+)\"", RegexOptions.IgnoreCase);
                if (hours.Success)
                    return (long)(double.Parse(hours.Groups[1].Value, CultureInfo.InvariantCulture) * 60);

                return null;
            }

            return null;
        }

        private static string FormatHours(long minutes)
        {
            return (minutes / 60.0).ToString("F1") + "h";
        }
    }
}
