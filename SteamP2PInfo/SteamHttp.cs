using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SteamP2PInfo
{
    /// <summary>
    /// Small HTTP helper shared by the Steam lookups (playtime, friend lists).
    /// Steam services are reachable either through a local proxy/accelerator or
    /// directly, and a machine can have working internet while one of those two
    /// routes is broken - so every request tries the other route before failing.
    /// </summary>
    static class SteamHttp
    {
        private static readonly int[] PROXY_PORTS = { 7897, 7890, 7891, 7892, 10809, 1080, 2080, 8888, 33210 };
        private static readonly HashSet<int> mUnusableProxyPorts = new HashSet<int>();
        private static readonly object mLock = new object();

        private static WebProxy mCachedProxy;
        private static int mCachedProxyPort = 0;
        private static int mProxyFailureCount = 0;
        private static DateTime mLastProxyProbe = DateTime.MinValue;

        /// <summary>
        /// Downloads a URL through the detected local proxy (or through the
        /// system/default route when <paramref name="preferProxy"/> is false) and
        /// retries through the other route when the first one fails.
        /// </summary>
        public static string DownloadWithFallback(string url, bool preferProxy)
        {
            WebProxy detected = FindLocalProxy();
            WebProxy first = preferProxy ? detected : null;
            WebProxy second = preferProxy ? null : detected;

            try
            {
                string result = Download(url, first);
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
                    string result = Download(url, second);
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

        private static string Download(string url, WebProxy proxy)
        {
            // Steam only answers over TLS 1.2+, which .NET Framework does not
            // enable by default. Setting it here keeps every lookup working even
            // when no other request has enabled it yet.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            // Explicit timeouts so a blocked network (e.g. steamcommunity
            // unreachable) fails fast instead of hanging background threads.
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
    }
}
