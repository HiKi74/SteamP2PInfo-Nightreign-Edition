using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Steamworks;

namespace SteamP2PInfo
{
    /// <summary>
    /// Tells whether two players of the current session are Steam friends, which
    /// usually means they queued up together. The answer comes from
    /// ISteamUser/GetFriendList, so it needs a Steam Web API key AND a player
    /// whose friends list is public (Steam answers 401 otherwise). When the
    /// relation cannot be resolved the state simply stays unknown.
    /// </summary>
    static class SteamFriendship
    {
        /// <summary>
        /// A resolved friend list is kept for an hour; unresolved lookups (no API
        /// key, private friends list, network problem) are retried occasionally.
        /// </summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(10);

        private class Entry
        {
            public readonly HashSet<ulong> Friends = new HashSet<ulong>();
            public DateTime LastFetch = DateTime.MinValue;
            public bool Fetching = false;
            public bool Resolved = false;
        }

        private static readonly Dictionary<ulong, Entry> mCache = new Dictionary<ulong, Entry>();
        private static readonly object mLock = new object();

        /// <summary>
        /// True when the two players are known to be Steam friends. Returns false
        /// while the answer is unknown; the lookup happens in the background and
        /// the UI picks the result up on a later refresh.
        /// </summary>
        public static bool AreFriends(CSteamID a, CSteamID b)
        {
            ulong first = a.m_SteamID;
            ulong second = b.m_SteamID;

            if (first == 0 || second == 0 || first == second)
                return false;

            // Friendship is mutual, so one public friends list answers it.
            return IsInFriendList(first, second) || IsInFriendList(second, first);
        }

        private static bool IsInFriendList(ulong owner, ulong other)
        {
            lock (mLock)
            {
                Entry entry;
                if (!mCache.TryGetValue(owner, out entry))
                {
                    entry = new Entry();
                    mCache[owner] = entry;
                    StartFetch(owner, entry);
                    return false;
                }

                if (!entry.Fetching)
                {
                    TimeSpan interval = entry.Resolved ? RefreshInterval : RetryInterval;
                    if (DateTime.UtcNow - entry.LastFetch > interval)
                        StartFetch(owner, entry);
                }

                return entry.Resolved && entry.Friends.Contains(other);
            }
        }

        private static void StartFetch(ulong steamId, Entry entry)
        {
            entry.Fetching = true;
            Task.Run(() =>
            {
                HashSet<ulong> friends = null;

                try
                {
                    string apiKey = Settings.Default.SteamWebApiKey;
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        string url =
                            "https://api.steampowered.com/ISteamUser/GetFriendList/v1/" +
                            "?key=" + Uri.EscapeDataString(apiKey) +
                            "&steamid=" + steamId +
                            "&relationship=friend";

                        friends = ParseFriendList(SteamHttp.DownloadWithFallback(url, true));
                    }
                }
                catch (Exception)
                {
                    // Private friends list (HTTP 401) or a network problem: the
                    // relation stays unknown and is retried later.
                }

                lock (mLock)
                {
                    if (friends != null)
                    {
                        entry.Friends.Clear();
                        foreach (ulong friend in friends)
                            entry.Friends.Add(friend);
                        entry.Resolved = true;
                    }

                    entry.LastFetch = DateTime.UtcNow;
                    entry.Fetching = false;
                }
            });
        }

        /// <summary>
        /// Turns the GetFriendList response into a set of Steam IDs. An empty set
        /// means "friends list is public but empty".
        /// </summary>
        internal static HashSet<ulong> ParseFriendList(string json)
        {
            HashSet<ulong> result = new HashSet<ulong>();

            JArray friends = JObject.Parse(json)["friendslist"]?["friends"] as JArray;
            if (friends == null)
                return result;

            foreach (JToken token in friends)
            {
                ulong id;
                if (ulong.TryParse((string)token["steamid"], out id) && id != 0)
                    result.Add(id);
            }

            return result;
        }
    }
}
