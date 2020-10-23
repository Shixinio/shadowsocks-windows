
using NLog;

using Shadowsocks.Controller.Service;
using Shadowsocks.Model;
using Shadowsocks.Util;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Shadowsocks.PAC
{
    public class GeositeSource : IPACSource
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private static readonly string _databasePath = PathUtil.GetTempPath("dlc.dat");

        private const string GEOSITE_URL = "https://github.com/v2fly/domain-list-community/raw/release/dlc.dat";
        private const string GEOSITE_SHA256SUM_URL = "https://github.com/v2fly/domain-list-community/raw/release/dlc.dat.sha256sum";

        private static byte[] GEOSITE_DB;

        public static readonly Dictionary<string, IList<DomainObject>> Geosites = new Dictionary<string, IList<DomainObject>>();

        private readonly GeositeConfig config;

        public List<string> directGroups => config.geositeDirectGroups;

        public List<string> proxiedGroups => config.geositeProxiedGroups;

        public bool preferDirect => config.geositePreferDirect;

        public GeositeSource(Configuration config)
        {
            this.config = config.geosite;

            if (File.Exists(_databasePath) && new FileInfo(_databasePath).Length > 0)
            {
                GEOSITE_DB = File.ReadAllBytes(_databasePath);
            }
            else
            {
                File.WriteAllBytes(_databasePath, Resource.DLC_DAT);
                GEOSITE_DB = Resource.DLC_DAT;
            }

            LoadGeositeList();
        }

        /// <summary>
        /// Separates the attribute (e.g. @cn) from a group name.
        /// No checks are performed.
        /// </summary>
        /// <param name="group">A group name potentially with a trailing attribute.</param>
        /// <param name="groupName">The group name with the attribute stripped.</param>
        /// <param name="attribute">The attribute.</param>
        /// <returns>True for success. False for more than one '@'.</returns>
        private static bool SeparateAttributeFromGroupName(string group, out string groupName, out string attribute)
        {
            var splitGroupAttributeList = group.Split('@');
            if (splitGroupAttributeList.Length == 1) // no attribute
            {
                groupName = splitGroupAttributeList[0];
                attribute = "";
            }
            else if (splitGroupAttributeList.Length == 2) // has attribute
            {
                groupName = splitGroupAttributeList[0];
                attribute = splitGroupAttributeList[1];
            }
            else
            {
                groupName = "";
                attribute = "";
                return false;
            }
            return true;
        }

        /// <summary>
        /// load new GeoSite data from geosite DB
        /// </summary>
        private void LoadGeositeList()
        {
            var list = GeositeList.Parser.ParseFrom(GEOSITE_DB);
            foreach (var item in list.Entries)
                Geosites[item.GroupName.ToLowerInvariant()] = item.Domains;
        }

        #region Generate Rules

        /// <summary>
        /// Generates rule lines based on user preference.
        /// </summary>
        /// <param name="directGroups">A list of geosite groups configured for direct connection.</param>
        /// <param name="proxiedGroups">A list of geosite groups configured for proxied connection.</param>
        /// <param name="blacklist">Whether to use blacklist mode. False for whitelist.</param>
        /// <returns>A list of rule lines.</returns>
        public List<string> GenerateRules(List<string> directGroups, List<string> proxiedGroups, bool blacklist)
        {
            List<string> ruleLines;
            if (blacklist) // blocking + exception rules
            {
                ruleLines = GenerateBlockingRules(proxiedGroups);
                ruleLines.AddRange(GenerateExceptionRules(directGroups));
            }
            else // proxy all + exception rules
            {
                ruleLines = new List<string>()
                {
                    "/.*/" // block/proxy all unmatched domains
                };
                ruleLines.AddRange(GenerateExceptionRules(directGroups));
            }
            return ruleLines;
        }

        /// <summary>
        /// Generates rules that match domains that should be proxied.
        /// </summary>
        /// <param name="groups">A list of source groups.</param>
        /// <returns>A list of rule lines.</returns>
        private List<string> GenerateBlockingRules(List<string> groups)
        {
            List<string> ruleLines = new List<string>();
            foreach (var group in groups)
            {
                // separate group name and attribute
                SeparateAttributeFromGroupName(group, out string groupName, out string attribute);
                var domainObjects = Geosites[groupName];
                if (!string.IsNullOrEmpty(attribute)) // has attribute
                {
                    var attributeObject = new DomainObject.Types.Attribute
                    {
                        Key = attribute,
                        BoolValue = true
                    };
                    foreach (var domainObject in domainObjects)
                    {
                        if (domainObject.Attribute.Contains(attributeObject))
                            switch (domainObject.Type)
                            {
                                case DomainObject.Types.Type.Plain:
                                    ruleLines.Add(domainObject.Value);
                                    break;
                                case DomainObject.Types.Type.Regex:
                                    ruleLines.Add($"/{domainObject.Value}/");
                                    break;
                                case DomainObject.Types.Type.Domain:
                                    ruleLines.Add($"||{domainObject.Value}");
                                    break;
                                case DomainObject.Types.Type.Full:
                                    ruleLines.Add($"|http://{domainObject.Value}");
                                    ruleLines.Add($"|https://{domainObject.Value}");
                                    break;
                            }
                    }
                }
                else // no attribute
                    foreach (var domainObject in domainObjects)
                    {
                        switch (domainObject.Type)
                        {
                            case DomainObject.Types.Type.Plain:
                                ruleLines.Add(domainObject.Value);
                                break;
                            case DomainObject.Types.Type.Regex:
                                ruleLines.Add($"/{domainObject.Value}/");
                                break;
                            case DomainObject.Types.Type.Domain:
                                ruleLines.Add($"||{domainObject.Value}");
                                break;
                            case DomainObject.Types.Type.Full:
                                ruleLines.Add($"|http://{domainObject.Value}");
                                ruleLines.Add($"|https://{domainObject.Value}");
                                break;
                        }
                    }
            }
            return ruleLines;
        }

        /// <summary>
        /// Generates rules that match domains that should be connected directly without a proxy.
        /// </summary>
        /// <param name="groups">A list of source groups.</param>
        /// <returns>A list of rule lines.</returns>
        private List<string> GenerateExceptionRules(List<string> groups) =>
            GenerateBlockingRules(groups)
                .Select(r => $"@@{r}") // convert blocking rules to exception rules
                .ToList();

        #endregion

        /// <summary>
        /// Checks if the specified group exists in GeoSite database.
        /// </summary>
        /// <param name="group">The group name to check for.</param>
        /// <returns>True if the group exists. False if the group doesn't exist.</returns>
        public static bool CheckGeositeGroup(string group) => SeparateAttributeFromGroupName(group, out string groupName, out _) && Geosites.ContainsKey(groupName);

        public async void UpdateSource(ErrorEventHandler error)
        {
            var geositeUrl = string.IsNullOrWhiteSpace(config.geositeUrl) ? GEOSITE_URL : config.geositeUrl;

            _logger.Info($"Checking Geosite from {geositeUrl}");

            var hash = SHA256.Create();
            try
            {
                #region Verify that the current data is the latest data in the cloud

                // download checksum first
                var remoteSHA256Sum = (await Utils.HttpClient.GetStringAsync(GEOSITE_SHA256SUM_URL)).Substring(0, 64).ToUpper();

                _logger.Info($"Remote SHA256 sum: {remoteSHA256Sum}");

                // compare downloaded checksum with local geositeDB
                byte[] localDBHashBytes = hash.ComputeHash(GEOSITE_DB);

                string localDBHash = BitConverter.ToString(localDBHashBytes).Replace("-", string.Empty);

                _logger.Info($"Local SHA256 sum: {localDBHash}");

                // if already latest
                if (remoteSHA256Sum.Equals(localDBHash))
                {
                    _logger.Info("Local GeoSite DB is up to date.");
                    return;
                }

                #endregion

                // not latest. download new DB
                var downloadedBytes = await Utils.HttpClient.GetByteArrayAsync(geositeUrl);

                // verify sha256sum
                byte[] downloadedDBHashBytes = hash.ComputeHash(downloadedBytes);
                string downloadedDBHash = BitConverter.ToString(downloadedDBHashBytes).Replace("-", string.Empty);

                _logger.Info($"Actual SHA256 sum: {downloadedDBHash}");
                if (remoteSHA256Sum.Equals(downloadedDBHash))
                {
                    _logger.Info("Sha256sum Verification: FAILED. Downloaded GeoSite DB is corrupted. Aborting the update.");
                    throw new Exception("SHA256 sum mismatch");
                }
                else
                {
                    _logger.Info("Sha256sum Verification: PASSED. Applying to local GeoSite DB.");
                }

                // write to geosite file
                using (FileStream fileStream = File.Create(_databasePath))
                    fileStream.Write(downloadedBytes, 0, downloadedBytes.Length);

                // update stuff
                GEOSITE_DB = downloadedBytes;
                LoadGeositeList();
            }
            catch (Exception ex)
            {
                error.Invoke(null, new ErrorEventArgs(ex));
            }
        }
    }
}
