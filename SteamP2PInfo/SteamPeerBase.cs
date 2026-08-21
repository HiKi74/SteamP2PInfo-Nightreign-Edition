using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        /// </summary>
        public virtual string Name { get { return SteamFriends.GetFriendPersonaName(SteamID); } }

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
