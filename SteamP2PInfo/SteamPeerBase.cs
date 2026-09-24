using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;

using SteamP2PInfo.Config;

namespace SteamP2PInfo
{
    /// <summary>
    /// Repr
    /// </summary>
    abstract class SteamPeerBase : IDisposable
    {
        /// <summary>
        /// Steam ID of the peer.
        /// </summary>
        public CSteamID SteamID { get; protected set; }

        // The following field is probably useless now that Steam appears to have disabled the IsPlayingSharedGame request

        /// <summary>
        /// Main Steam ID of the peer, if playing on an alternate account. 
        /// </summary>
        public CSteamID MainSteamID { get; protected set; }

        /// <summary>
        /// Steam persona name of the peer.
        ///
        /// Cached on purpose: this used to call SteamFriends.GetFriendPersonaName every
        /// time the UI rendered a cell, and Steam answers those calls over IPC to the
        /// Steam client - which stalls for seconds while the game is shutting down.
        /// The first reader (a worker thread now, see SteamPeerManager) fills the cache,
        /// everything after that is a plain string read.
        /// </summary>
        public virtual string Name
        {
            get
            {
                string cached = mPersonaName;
                if (cached != null)
                    return cached;

                string name;
                try
                {
                    name = SteamFriends.GetFriendPersonaName(SteamID);
                }
                catch (Exception)
                {
                    name = null;
                }
                if (string.IsNullOrEmpty(name))
                    name = SteamID.m_SteamID.ToString();

                Interlocked.CompareExchange(ref mPersonaName, name, null);
                return mPersonaName;
            }
        }

        private string mPersonaName;

        /// <summary>
        /// True if the peer is connected via the deprected api, ISteamNetworking.
        /// </summary>
        public abstract bool IsOldAPI { get; }

        /// <summary>
        /// Name representing the type of connection this peer is using (e.g. SteamNetworking, SteamNetworkingSockets)
        /// </summary>
        public abstract string ConnectionTypeName { get; }

        /// <summary>
        /// Ping to peer in milliseconds.
        /// </summary>
        public abstract double Ping { get; }

        /// <summary>
        /// Subjective measure of connection quality to the remote peer where 0 = horrible and 1 = perfect.
        /// May not be very accurate if using the old API. When using the new API, should be directly related to packet loss.
        /// </summary>
        public abstract double ConnectionQuality { get; }

        /// <summary>
        /// Playtime of the peer in ELDEN RING NIGHTREIGN, as a display string.
        /// Only available when the peer's Steam profile (game details) is public.
        /// </summary>
        public string PlaytimeText { get { return SteamPlaytime.GetText(SteamID); } }

        /// <summary>
        /// Relation of this peer to the other players of the session: "好友" when
        /// they are Steam friends with one of them (a likely pre-made pair),
        /// "野排" when a readable friends list proves that they are not, "私密"
        /// when Steam hides the lists needed to answer, and "查询中" while the
        /// lookups are still running. See SteamFriendship for details.
        /// </summary>
        public string RelationText
        {
            get
            {
                bool hidden = false;
                bool notFriends = false;

                foreach (SteamPeerBase other in SteamPeerManager.GetPeers())
                {
                    if (other == null || other.SteamID.m_SteamID == SteamID.m_SteamID)
                        continue;

                    switch (SteamFriendship.GetRelation(SteamID, other.SteamID))
                    {
                        case SteamFriendship.RelationState.Friends:
                            return "\u597D\u53CB"; // 好友

                        case SteamFriendship.RelationState.NotFriends:
                            notFriends = true;
                            break;

                        case SteamFriendship.RelationState.Private:
                            hidden = true;
                            break;
                    }
                }

                if (hidden)
                    return "\u79C1\u5BC6"; // 私密

                if (notFriends)
                    return "\u91CE\u6392"; // 野排

                return "\u67E5\u8BE2\u4E2D"; // 查询中
            }
        }

        /// <summary>
        /// Ping as a display string ("--" while not measured, so the column keeps
        /// a stable width and position).
        /// </summary>
        public string PingText { get { return Ping < 0 ? "--" : Ping.ToString("N0"); } }

        /// <summary>
        /// Connection quality as a display string ("--" while not measured).
        /// </summary>
        public string QualityText { get { return ConnectionQuality < 0 ? "--" : ConnectionQuality.ToString("N2"); } }

        /// <summary>
        /// ARGB hexadecimal color code used to fill the ping text.
        /// </summary>
        public string PingColor
        {
            get
            {
                OverlayConfig.PingColorRange range = new OverlayConfig.PingColorRange()
                {
                    Threshold = double.NegativeInfinity,
                    Color = GameConfig.Current.OverlayConfig.TextColor
                };

                foreach (OverlayConfig.PingColorRange r in GameConfig.Current.OverlayConfig.PingColors)
                {
                    if (r.Threshold <= Ping && r.Threshold > range.Threshold)
                        range = r;
                }

                return range.Color;
            }
        }

        protected SteamPeerBase(CSteamID steamID)
        {
            SteamID = steamID;
        }

        /// <summary>
        /// Update peer info that may not be known at instance creation time.
        /// Should return true if the peer is still connected and false otherwise.
        /// </summary>
        public abstract bool UpdatePeerInfo();

        public virtual void Dispose() { }
    }
}
