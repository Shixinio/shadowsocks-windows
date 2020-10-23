using Newtonsoft.Json;

using NLog;

using Shadowsocks.Model;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Shadowsocks.Util
{
    public static class ConfigurationManager
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private const string CONFIG_FILE = "gui-config.json";

        /// <summary>
        /// Loads the configuration from file.
        /// </summary>
        /// <returns>An Configuration object.</returns>
        internal static Configuration Load()
        {
            if (File.Exists(CONFIG_FILE))
            {
                try
                {
                    var localConfigContent = File.ReadAllText(CONFIG_FILE);
                    return JsonConvert.DeserializeObject<Configuration>(localConfigContent, new JsonSerializerSettings()
                    {
                        ObjectCreationHandling = ObjectCreationHandling.Replace
                    });
                }
                catch (FileNotFoundException)
                { }
                catch (Exception e)
                {
                    _logger.LogUsefulException(e);
                }
            }

            return new Configuration(Client.Version);
        }

        public static Server GetCurrentServer(this Configuration conf)
        {
            var index = conf.index;

            if (index >= 0 && index < conf.servers.Count)
                return conf.servers[index];
            else
                return ServerEx.GetDefaultServer();
        }

        public static List<Server> SortByOnlineConfig(IEnumerable<Server> servers)
        {
            var groups = servers.GroupBy(s => s.group);
            List<Server> ret = new List<Server>();
            ret.AddRange(groups.Where(g => string.IsNullOrEmpty(g.Key)).SelectMany(g => g));
            ret.AddRange(groups.Where(g => !string.IsNullOrEmpty(g.Key)).SelectMany(g => g));
            return ret;
        }

        /// <summary>
        /// Saves the Configuration object to file.
        /// </summary>
        /// <param name="conf">A Configuration object.</param>
        public static void Save(this Configuration conf)
        {
            conf.servers = SortByOnlineConfig(conf.servers);

            try
            {
                using var fileStream = File.Open(CONFIG_FILE, FileMode.Create);
                using var streamWriter = new StreamWriter(fileStream);

                streamWriter.Write(JsonConvert.SerializeObject(conf, Formatting.Indented));
                streamWriter.Flush();

                // TODO: NLog
                // conf.nLogConfig.SetLogLevel(conf.isVerboseLogging ? verboseLogLevel : NLogConfig.LogLevel.Info);
                // NLogConfig.SaveXML(conf.nLogConfig);
            }
            catch (Exception e)
            {
                _logger.LogUsefulException(e);
            }
        }

        public static void ResetUserAgent(this Configuration conf)
        {
            conf.userAgent = "ShadowsocksWindows/$version";
            conf.userAgentString = conf.userAgent.Replace("$version", conf.version);
        }
    }
}
