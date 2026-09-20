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
    /// Fetches ELDEN RING NIGHTREIGN playtime for a peer. The value is looked up
    /// in three places, in this order: the Steam Web API (when a key is set), the
    /// public "Games" tab of the profile, and finally the profile's "Favorite
    /// Game" showcase (which stays visible for players who hide their details).
    /// </summary>
    static class SteamPlaytime
    {
        private const int NIGHTREIGN_APPID = Config.GameConfig.NightreignAppId;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Failed lookups are retried much sooner than successful ones, so a
        /// single timeout does not keep "-" on screen for five minutes.
        /// </summary>
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

        private const string LOADING_TEXT = "\u67E5\u8BE2\u4E2D"; // 查询中
        private const string PRIVATE_TEXT = "\u672A\u516C\u5F00"; // 未公开
        private const string ERROR_TEXT = "\u2014"; // —

        private class Entry
        {
            public string Text = LOADING_TEXT;
            public DateTime LastFetch = DateTime.MinValue;
            public bool Fetching = false;
            public bool Failed = false;
        }

        private static readonly Dictionary<ulong, Entry> mCache = new Dictionary<ulong, Entry>();
        private static readonly object mLock = new object();

        private static readonly int[] PROXY_PORTS = { 7897, 7890, 7891, 7892, 10809, 1080, 2080, 8888, 33210 };
        private static WebProxy mCachedProxy;
        private static int mCachedProxyPort = 0;
        private static readonly HashSet<int> mUnusableProxyPorts = new HashSet<int>();
        private static int mProxyFailureCount = 0;
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
                else if (!entry.Fetching)
                {
                    TimeSpan interval = entry.Failed ? RetryInterval : RefreshInterval;
                    if (DateTime.UtcNow - entry.LastFetch > interval)
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
                string text;
                try
                {
                    text = FetchPlaytimeText(steamId);
                }
                catch (Exception)
                {
                    text = ERROR_TEXT;
                }

                lock (mLock)
                {
                    entry.Text = text;
                    entry.Failed = text == ERROR_TEXT;
                    entry.LastFetch = DateTime.UtcNow;
                    entry.Fetching = false;
                }
            });
        }

        private static string FetchPlaytimeText(ulong steamId)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                // 1) If a Steam Web API key is configured, use GetOwnedGames --
                //    the most reliable source for playtime. An empty result only
                //    means the game details are not public, so keep trying the
                //    other sources below instead of giving up right away.
                bool apiReportedNotPublic = false;
                string apiKey = Settings.Default.SteamWebApiKey;
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    string apiResult = TryFetchFromWebApi(apiKey, steamId);
                    if (apiResult == PRIVATE_TEXT)
                        apiReportedNotPublic = true;
                    else if (apiResult != null)
                        return apiResult;
                }

                // 2) Public profile "Games" page (no key required).
                bool gamesPageDownloaded = false;
                try
                {
                    string html = DownloadStringWithFallback(
                        $"https://steamcommunity.com/profiles/{steamId}/games?tab=all&l=english", true);
                    gamesPageDownloaded = true;

                    long? minutes = ParsePlaytimeMinutes(html);
                    if (minutes != null)
                        return FormatHours(minutes.Value);
                }
                catch (Exception)
                {
                    // Network problem (blocked / no proxy): fall through and try
                    // the profile page, which may still be reachable.
                }

                // 3) "Favorite Game" showcase on the profile page. Players who
                //    keep their game details private often still show total hours
                //    in this showcase, so it is the last chance to get a number.
                try
                {
                    string profile = DownloadStringWithFallback(
                        $"https://steamcommunity.com/profiles/{steamId}/?l=english", true);
                    long? showcaseMinutes = ParseShowcasePlaytimeMinutes(profile);
                    if (showcaseMinutes != null)
                        return FormatHours(showcaseMinutes.Value);
                }
                catch (Exception)
                {
                }

                // Nothing public to read. If we at least reached Steam, report the
                // profile as not public; otherwise this is a network problem.
                if (gamesPageDownloaded || apiReportedNotPublic)
                    return PRIVATE_TEXT;

                return ERROR_TEXT;
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
        private static string TryFetchFromWebApi(string apiKey, ulong steamId)
        {
            try
            {
                string url =
                    $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/" +
                    $"?key={Uri.EscapeDataString(apiKey)}&steamid={steamId}" +
                    $"&include_appinfo=true&appids_filter[0]={Config.GameConfig.NightreignAppId}&format=json";

                string json = DownloadStringWithFallback(url, true);
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
        /// Downloads a URL through the detected local proxy (or through the
        /// system/default route when <paramref name="preferProxy"/> is false) and
        /// retries through the other route when the first one fails. Accelerators
        /// often help one Steam service while breaking another, which is why a
        /// machine with working internet could still show "-" for playtime.
        /// </summary>
        private static string DownloadStringWithFallback(string url, bool preferProxy)
        {
            WebProxy detected = FindLocalProxy();
            WebProxy first = preferProxy ? detected : null;
            WebProxy second = preferProxy ? null : detected;

            try
            {
                string result = DownloadString(url, first);
                if (first != null)
                    ReportProxySuccess();
                return result;
            }
            catch (Exception firstError)
            {
                if (second == null || ReferenceEquals(first, second))
                    throw;

                try
                {
                    string result = DownloadString(url, second);
                    if (second != null)
                        ReportProxySuccess();
                    else
                        ReportProxyFailure();
                    return result;
                }
                catch (Exception)
                {
                    ReportProxyFailure();
                    throw firstError;
                }
            }
        }

        private static void ReportProxySuccess()
        {
            lock (mLock)
                mProxyFailureCount = 0;
        }

        /// <summary>
        /// Remembers that the detected proxy port could not carry HTTP traffic.
        /// Some accelerators open a local port that only handles game traffic;
        /// after two failures that port is skipped when probing again.
        /// </summary>
        private static void ReportProxyFailure()
        {
            lock (mLock)
            {
                if (++mProxyFailureCount < 2)
                    return;

                mProxyFailureCount = 0;

                if (mCachedProxyPort != 0)
                    mUnusableProxyPorts.Add(mCachedProxyPort);

                mCachedProxy = null;
                mCachedProxyPort = 0;
                mLastProxyProbe = DateTime.MinValue;
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
                mCachedProxyPort = 0;

                foreach (int port in PROXY_PORTS)
                {
                    if (mUnusableProxyPorts.Contains(port))
                        continue;

                    try
                    {
                        using (TcpClient client = new TcpClient())
                        {
                            // Synchronous probe: connections to closed loopback
                            // ports fail immediately, and no dangling async task
                            // is left behind to fault later on another thread.
                            client.Connect("127.0.0.1", port);
                            mCachedProxy = new WebProxy($"http://127.0.0.1:{port}");
                            mCachedProxyPort = port;
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

        /// <summary>
        /// Reads the playtime from the profile's "Favorite Game" showcase when
        /// that showcase is displaying ELDEN RING NIGHTREIGN. The showcase stays
        /// visible for players who keep their game details private, which is why
        /// it is used as the last resort. Returns minutes, or null when the
        /// showcase does not show Nightreign (or hides its hours).
        /// </summary>
        private static long? ParseShowcasePlaytimeMinutes(string html)
        {
            // A showcase stat renders as:
            //   <div class="showcase_stat">
            //     <div class="value">1,919</div>
            //     <div class="label">Hours played</div>
            //   </div>
            // and is preceded by the showcased game's link (/app/<appid>).
            MatchCollection stats = Regex.Matches(html,
                "class=\"value\"[^>]*>\\s*([\\d][\\d,\\.\\s]*)\\s*</div>\\s*<div[^>]*class=\"label\"[^>]*>\\s*([^<]+?)\\s*</div>",
                RegexOptions.IgnoreCase);

            foreach (Match stat in stats)
            {
                if (!IsHoursLabel(stat.Groups[2].Value))
                    continue;

                int windowStart = Math.Max(0, stat.Index - 2500);
                string window = html.Substring(windowStart, stat.Index - windowStart);

                // Only trust values coming from a favorite game showcase ...
                if (window.IndexOf("favoritegame_showcase", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // ... that is showing Nightreign (the showcased game is the last
                // app link in front of the stat).
                MatchCollection appLinks = Regex.Matches(window, "/app/(\\d+)", RegexOptions.IgnoreCase);
                if (appLinks.Count == 0)
                    continue;

                int showcasedAppId;
                if (!int.TryParse(appLinks[appLinks.Count - 1].Groups[1].Value, out showcasedAppId)
                    || showcasedAppId != NIGHTREIGN_APPID)
                    continue;

                // The showcase reports whole (localized, thousands-separated) hours.
                string rawValue = Regex.Replace(stat.Groups[1].Value, "[\\s,]", "");
                double hours;
                if (double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out hours))
                    return (long)Math.Round(hours * 60);
            }

            return null;
        }

        /// <summary>
        /// True when a showcase stat label means "hours played". The page is
        /// requested in English, but other locales are accepted as well in case
        /// Steam serves it regardless of the requested language.
        /// </summary>
        private static bool IsHoursLabel(string label)
        {
            if (string.IsNullOrEmpty(label))
                return false;

            return label.IndexOf("hour", StringComparison.OrdinalIgnoreCase) >= 0
                || label.IndexOf("\u5C0F\u65F6") >= 0            // 小时
                || label.IndexOf("\u5C0F\u6642") >= 0            // 小時
                || label.IndexOf("\u6642\u6578") >= 0            // 時數
                || label.IndexOf("\u30D7\u30EC\u30A4\u6642\u9593") >= 0; // プレイ時間
        }

        private static string FormatHours(long minutes)
        {
            return (minutes / 60.0).ToString("F1") + "h";
        }
    }
}
