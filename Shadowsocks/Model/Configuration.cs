using Newtonsoft.Json;

using NLog;

using Shadowsocks.Common.Model;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace Shadowsocks.Model
{
    [JsonObject]
    public class Configuration
    {
        [JsonIgnore]
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        [JsonIgnore]
        public bool firstRunOnNewVersion;

        [JsonIgnore]
        public string userAgentString; // $version substituted with numeral version in it

        [JsonIgnore]
        NLogConfig nLogConfig;

#if DEBUG
        private static readonly NLogConfig.LogLevel verboseLogLevel = NLogConfig.LogLevel.Trace;

#else
        private static readonly NLogConfig.LogLevel verboseLogLevel =  NLogConfig.LogLevel.Debug;
#endif

        public string version;

        public List<Server> servers;

        public List<string> onlineConfigSource;

        // when strategy is set, index is ignored
        public int index;
        public bool global;
        public bool enabled;
        public bool shareOverLan;
        public bool firstRun;
        public int localPort;
        public bool portableMode;
        public bool showPluginOutput;
        public string pacUrl;

        public bool useOnlinePac;
        public bool secureLocalPac; // enable secret for PAC server
        public bool regeneratePacOnUpdate; // regenerate pac.txt on version update
        public bool autoCheckUpdate;
        public bool checkPreRelease;
        public string skippedUpdateVersion; // skip the update with this version number
        public bool isVerboseLogging;

        // hidden options
        public bool isIPv6Enabled; // for experimental ipv6 support
        public bool generateLegacyUrl; // for pre-sip002 url compatibility

        public string userAgent;

        public GeositeConfig geosite;

        //public NLogConfig.LogLevel logLevel;
        public LogViewerConfig logViewer;
        public ForwardProxyConfig proxy;
        public HotkeyConfig hotkey;

        public WebProxy WebProxy => enabled
            ? new WebProxy(
                isIPv6Enabled 
                ? $"[{IPAddress.IPv6Loopback}]" 
                : IPAddress.Loopback.ToString(), localPort) 
            : null;

        [JsonIgnore]
        public string LocalHost => isIPv6Enabled ? "[::1]" : "127.0.0.1";

        internal Configuration(string version)
        {
            servers = new List<Server>();

            onlineConfigSource = new List<string>();

            this.version = version;
            index = 0;
            global = false;
            enabled = false;
            shareOverLan = false;
            firstRun = true;
            localPort = 1080;
            portableMode = true;
            showPluginOutput = false;
            pacUrl = "";
            useOnlinePac = false;
            secureLocalPac = true;
            regeneratePacOnUpdate = true;
            autoCheckUpdate = false;
            checkPreRelease = false;
            skippedUpdateVersion = "";
            isVerboseLogging = false;

            // hidden options
            isIPv6Enabled = false;
            generateLegacyUrl = false;
            userAgent = "ShadowsocksWindows/$version";

            geosite = new GeositeConfig();

            logViewer = new LogViewerConfig();
            proxy = new ForwardProxyConfig();
            hotkey = new HotkeyConfig();

            firstRunOnNewVersion = false;
        }

        /// <summary>
        /// Process the loaded configurations and set up things.
        /// </summary>
        /// <param name="config">A reference of Configuration object.</param>
        public static void Process(ref Configuration config)
        {
            // Verify if the configured geosite groups exist.
            // Reset to default if ANY one of the configured group doesn't exist.
            if (!GeositeConfig.ValidateGeositeGroupList(config.geosite.geositeDirectGroups))
                GeositeConfig.ResetGeositeDirectGroup(ref config.geosite.geositeDirectGroups);
            if (!GeositeConfig.ValidateGeositeGroupList(config.geosite.geositeProxiedGroups))
                GeositeConfig.ResetGeositeProxiedGroup(ref config.geosite.geositeProxiedGroups);

            // Mark the first run of a new version.
            var appVersion = new Version("");
            var configVersion = new Version(config.version);
            if (appVersion.CompareTo(configVersion) > 0)
            {
                config.firstRunOnNewVersion = true;
            }
            // Add an empty server configuration
            if (config.servers.Count == 0)
                config.servers.Add(ServerEx.GetDefaultServer());
            // Selected server
            // if (config.index == -1 && string.IsNullOrEmpty(config.strategy))
            // config.index = 0;
            if (config.index >= config.servers.Count)
                config.index = config.servers.Count - 1;
            // Check OS IPv6 support
            if (!System.Net.Sockets.Socket.OSSupportsIPv6)
                config.isIPv6Enabled = false;
            config.proxy.CheckConfig();
            // Replace $version with the version number.
            config.userAgentString = config.userAgent.Replace("$version", config.version);

            // NLog log level
            try
            {
                config.nLogConfig = NLogConfig.LoadXML();
                switch (config.nLogConfig.GetLogLevel())
                {
                    case NLogConfig.LogLevel.Fatal:
                    case NLogConfig.LogLevel.Error:
                    case NLogConfig.LogLevel.Warn:
                    case NLogConfig.LogLevel.Info:
                        config.isVerboseLogging = false;
                        break;
                    case NLogConfig.LogLevel.Debug:
                    case NLogConfig.LogLevel.Trace:
                        config.isVerboseLogging = true;
                        break;
                }
            }
            catch (Exception e)
            {
                // GuiTool.ShowMessageBox($"Cannot get the log level from NLog config file. Please check if the nlog config file exists with corresponding XML nodes.\n{e.Message}");
            }
        }
    }
}
