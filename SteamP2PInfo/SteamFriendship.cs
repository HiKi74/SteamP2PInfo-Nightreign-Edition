using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Steamworks;

namespace SteamP2PInfo
{
    /// <summary>
    /// Tells whether two players of the current session are Steam friends, which
    /// usually means they queued up together. Two sources are used, in order: the
    /// official ISteamUser/GetFriendList endpoint (needs a Steam Web API key) and
    /// the public "friends" tab of the community profile (needs no key at all).
    /// Both only answer when the player's friends list is public - Steam hides it
    /// otherwise, and that case is reported as "private" instead of "unknown".
    /// </summary>
    static class SteamFriendship
    {
        /// <summary>What is currently known about the relation of two players.</summary>
        internal enum RelationState
        {
            /// <summary>No answer yet: the lookup is still running or the network failed.</summary>
            Unknown,

            /// <summary>A readable friends list proves that they are not friends.</summary>
            NotFriends,

            /// <summary>One of the two lists contains the other player.</summary>
            Friends,

            /// <summary>Steam hides the friends list, so the question cannot be answered.</summary>
            Private
        }

        /// <summary>
        /// A lookup that produced a friends list is kept for the rest of the
        /// session: the answer cannot change while the tool runs, so there is no
        /// reason to ask Steam again. A lookup that came back empty (hidden list,
        /// blocked network) is retried every two minutes, so a player who makes
        /// their list public mid-session shows up on his own.
        /// </summary>
        private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(2);

        /// <summary>The community friends page tags every entry with its Steam ID.</summary>
        private static readonly Regex COMMUNITY_FRIEND_REGEX =
            new Regex("data-steamid=\"(\\d{15,20})\"", RegexOptions.Compiled);

        private class Entry
        {
            public readonly HashSet<ulong> Friends = new HashSet<ulong>();
            public DateTime LastFetch = DateTime.MinValue;
            public bool Fetching = false;
            public bool Resolved = false;      // the list was read (it may be empty)
            public bool Hidden = false;        // Steam refuses to show the list
        }

        /// <summary>Outcome of reading one player's friends list.</summary>
        private class FetchResult
        {
            public HashSet<ulong> Friends;    // null when the list could not be read
            public bool Hidden;
        }

        private static readonly Dictionary<ulong, Entry> mCache = new Dictionary<ulong, Entry>();
        private static readonly object mLock = new object();

        /// <summary>
        /// Returns what is known about two players. A missing answer is looked up
        /// in the background and picked up by the UI on a later refresh.
        /// </summary>
        public static RelationState GetRelation(CSteamID a, CSteamID b)
        {
            ulong first = a.m_SteamID;
            ulong second = b.m_SteamID;

            if (first == 0 || second == 0 || first == second)
                return RelationState.Unknown;

            lock (mLock)
            {
                Entry firstEntry = GetEntry(first);
                Entry secondEntry = GetEntry(second);

                // Friendship is mutual, so a single readable list answers it.
                if (Contains(firstEntry, second) || Contains(secondEntry, first))
                    return RelationState.Friends;

                // A readable list that does not mention the other player proves
                // that they are not friends, whatever the second list shows.
                if (firstEntry.Resolved || secondEntry.Resolved)
                    return RelationState.NotFriends;

                // Nothing readable on either side. "Private" is only claimed once
                // both sides have actually been queried: while a first lookup is
                // still running the answer may well become a readable list. A
                // re-check keeps the previous answer instead of flickering back to
                // "unknown", which is why this tests the last finished lookup and
                // not the "fetching" flag.
                if ((firstEntry.Hidden || secondEntry.Hidden) &&
                    HasBeenQueried(firstEntry) && HasBeenQueried(secondEntry))
                    return RelationState.Private;

                return RelationState.Unknown;
            }
        }

        private static bool Contains(Entry entry, ulong steamId)
        {
            return entry.Resolved && entry.Friends.Contains(steamId);
        }

        /// <summary>True once at least one lookup of this list has finished.</summary>
        private static bool HasBeenQueried(Entry entry)
        {
            return entry.LastFetch != DateTime.MinValue;
        }

        /// <summary>
        /// Returns the cache entry of one player, starting the lookup or a refresh
        /// when it is missing or stale. Must be called while holding <see cref="mLock"/>.
        /// </summary>
        private static Entry GetEntry(ulong steamId)
        {
            Entry entry;
            if (!mCache.TryGetValue(steamId, out entry))
            {
                entry = new Entry();
                mCache[steamId] = entry;
                StartFetch(steamId, entry);
                return entry;
            }

            // Settled answers stay settled; only an empty result is retried.
            if (!entry.Fetching && !entry.Resolved &&
                DateTime.UtcNow - entry.LastFetch > RetryInterval)
                StartFetch(steamId, entry);

            return entry;
        }

        private static void StartFetch(ulong steamId, Entry entry)
        {
            entry.Fetching = true;

            Task.Run(() =>
            {
                FetchResult result;
                try
                {
                    result = Fetch(steamId);
                }
                catch (Exception)
                {
                    result = new FetchResult();
                }

                lock (mLock)
                {
                    if (result.Friends != null)
                    {
                        entry.Friends.Clear();
                        foreach (ulong friend in result.Friends)
                            entry.Friends.Add(friend);

                        entry.Resolved = true;
                        entry.Hidden = false;
                    }
                    else if (!entry.Resolved)
                    {
                        entry.Hidden = result.Hidden;
                    }

                    entry.LastFetch = DateTime.UtcNow;
                    entry.Fetching = false;
                }
            });
        }

        /// <summary>
        /// Reads a player's friends list from the Web API when a key is configured
        /// and falls back to the community profile, which needs no key.
        /// </summary>
        private static FetchResult Fetch(ulong steamId)
        {
            FetchResult result = new FetchResult();

            string apiKey = Settings.Default.SteamWebApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                result.Friends = FetchFromWebApi(apiKey, steamId, result);
                if (result.Friends != null)
                    return result;
            }

            result.Friends = FetchFromCommunityPage(steamId, result);
            return result;
        }

        /// <summary>
        /// ISteamUser/GetFriendList. Steam answers 401 when the player's friends
        /// list is not public and 403 when the key itself is not accepted, so only
        /// 401 counts as "hidden"; everything else falls back to the web page.
        /// </summary>
        private static HashSet<ulong> FetchFromWebApi(string apiKey, ulong steamId, FetchResult result)
        {
            string url =
                "https://api.steampowered.com/ISteamUser/GetFriendList/v1/" +
                "?key=" + Uri.EscapeDataString(apiKey) +
                "&steamid=" + steamId +
                "&relationship=friend";

            string json;
            try
            {
                json = SteamHttp.DownloadWithFallback(url, true);
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null && response.StatusCode == HttpStatusCode.Unauthorized)
                    result.Hidden = true; // the player hid the friends list

                return null;
            }
            catch (Exception)
            {
                return null;
            }

            try
            {
                return ParseFriendList(json);
            }
            catch (Exception)
            {
                // Unexpected payload: let the community page decide.
                return null;
            }
        }

        /// <summary>
        /// Public "friends" tab of the community profile: no key required, one
        /// `data-steamid` entry per friend. A player who hides the list (or has no
        /// friends at all) gets an empty container, which is reported as hidden.
        /// </summary>
        private static HashSet<ulong> FetchFromCommunityPage(ulong steamId, FetchResult result)
        {
            string html;
            try
            {
                html = SteamHttp.DownloadWithFallback(
                    "https://steamcommunity.com/profiles/" + steamId + "/friends/?l=english", true);
            }
            catch (Exception)
            {
                return null;
            }

            HashSet<ulong> friends = ParseCommunityFriends(html);
            if (friends.Count == 0)
            {
                result.Hidden = true;
                return null;
            }

            return friends;
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

        /// <summary>
        /// Extracts the Steam IDs embedded in the community friends page.
        /// </summary>
        internal static HashSet<ulong> ParseCommunityFriends(string html)
        {
            HashSet<ulong> result = new HashSet<ulong>();
            if (string.IsNullOrEmpty(html))
                return result;

            foreach (Match match in COMMUNITY_FRIEND_REGEX.Matches(html))
            {
                ulong id;
                if (ulong.TryParse(match.Groups[1].Value, out id) && id != 0)
                    result.Add(id);
            }

            return result;
        }
    }
}
