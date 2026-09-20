using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace SteamP2PInfo
{
    static class VersionCheck
    {
        public static readonly string CurrentVersion = "V1.0.3";
        public static JObject LatestRelease { get; private set; }

        public static bool FetchLatest()
        {
            // Update checking is disabled in this fork: it compared against the
            // upstream repository (tremwil/SteamP2PInfo), which would mislead
            // users into downloading the original author's build. When this fork
            // has its own public repository, point the query at it instead.
            LatestRelease = null;
            return false;
        }
    }
}
