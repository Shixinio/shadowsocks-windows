using Newtonsoft.Json;

using NLog;

using Shadowsocks.PAC;

using System.Collections.Generic;

namespace Shadowsocks.Model
{
    [JsonObject]
    public class GeositeConfig
    {
        [JsonIgnore]
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public string geositeUrl;// for custom geosite source (and rule group)

        public List<string> geositeDirectGroups;// groups of domains that we connect without the proxy
        public List<string> geositeProxiedGroups;// groups of domains that we connect via the proxy
        public bool geositePreferDirect; // a.k.a blacklist mode

        public GeositeConfig()
        {
            geositeUrl = "";
            geositeDirectGroups = new List<string>()
            {
                "cn",
                "geolocation-!cn@cn"
            };
            geositeProxiedGroups = new List<string>()
            {
                "geolocation-!cn"
            };
            geositePreferDirect = false;
        }

        /// <summary>
        /// Validates if the groups in the list are all valid.
        /// </summary>
        /// <param name="groups">The list of groups to validate.</param>
        /// <returns>
        /// True if all groups are valid.
        /// False if any one of them is invalid.
        /// </returns>
        public static bool ValidateGeositeGroupList(List<string> groups)
        {
            foreach (var geositeGroup in groups)
                if (!GeositeSource.CheckGeositeGroup(geositeGroup)) // found invalid group
                {
#if DEBUG
                    _logger.Debug($"Available groups:");
                    foreach (var group in GeositeSource.Geosites.Keys)
                        _logger.Debug($"{group}");
#endif
                    _logger.Warn($"The Geosite group {geositeGroup} doesn't exist. Resetting to default groups.");
                    return false;
                }
            return true;
        }

        public static void ResetGeositeDirectGroup(ref List<string> geositeDirectGroups)
        {
            geositeDirectGroups.Clear();
            geositeDirectGroups.Add("cn");
            geositeDirectGroups.Add("geolocation-!cn@cn");
        }

        public static void ResetGeositeProxiedGroup(ref List<string> geositeProxiedGroups)
        {
            geositeProxiedGroups.Clear();
            geositeProxiedGroups.Add("geolocation-!cn");
        }
    }
}
