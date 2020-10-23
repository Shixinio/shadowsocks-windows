using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Web;
using Shadowsocks.Controller;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json;
using System.ComponentModel;
using Shadowsocks.Util;

namespace Shadowsocks.Model
{
    [Serializable]
    public class Server
    {
        public const string DefaultMethod = "chacha20-ietf-poly1305";
        public const int DefaultPort = 8388;

        private const int DEFAULT_SERVER_TIMEOUT_SEC = 5;
        public const int MaxServerTimeoutSec = 20;

        public string server;
        public int server_port;
        public string password;
        public string method;

        // optional fields
        [DefaultValue("")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string plugin;
        [DefaultValue("")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string plugin_opts;
        [DefaultValue("")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string plugin_args;
        [DefaultValue("")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string remarks;

        [DefaultValue("")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string group;

        public int timeout;

        [JsonIgnore]
        public string FormalHostName =>
                // CheckHostName() won't do a real DNS lookup
                (Uri.CheckHostName(server)) switch
                {
                    // Add square bracket when IPv6 (RFC3986)
                    UriHostNameType.IPv6 => $"[{server}]",
                    // IPv4 or domain name
                    _ => server,
                };

        internal Server()
        {
            server = "";
            server_port = DefaultPort;
            method = DefaultMethod;
            plugin = "";
            plugin_opts = "";
            plugin_args = "";
            password = "";
            remarks = "";
            timeout = DEFAULT_SERVER_TIMEOUT_SEC;
        }

        public string GetURL(bool legacyUrl = false)
        {
            if (legacyUrl && string.IsNullOrWhiteSpace(plugin))
            {
                // For backwards compatiblity, if no plugin, use old url format
                string p = $"{method}:{password}@{server}:{server_port}";
                string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(p));
                return string.IsNullOrEmpty(remarks)
                    ? $"ss://{base64}"
                    : $"ss://{base64}#{HttpUtility.UrlEncode(remarks, Encoding.UTF8)}";
            }

            UriBuilder u = new UriBuilder("ss", null);
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{method}:{password}"));
            u.UserName = b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
            u.Host = server;
            u.Port = server_port;
            u.Fragment = HttpUtility.UrlEncode(remarks, Encoding.UTF8);

            if (!string.IsNullOrWhiteSpace(plugin))
            {
                NameValueCollection param = HttpUtility.ParseQueryString("");

                string pluginPart = plugin;
                if (!string.IsNullOrWhiteSpace(plugin_opts))
                {
                    pluginPart += ";" + plugin_opts;
                }
                param["plugin"] = pluginPart;
                u.Query = param.ToString();
            }

            return u.ToString();
        }

        public static List<Server> GetServers(string ssURL) =>
            ssURL
                .Split('\r', '\n', ' ')
                .Select(u => ShadowsocksProtocol.ParseURL(u))
                .Where(s => s != null)
                .ToList();

        public string Identifier() => $"{server}:{server_port}";

        public override bool Equals(object obj) => obj is Server o2 && server == o2.server && server_port == o2.server_port;

        public override int GetHashCode() => server.GetHashCode() ^ server_port;

        public override string ToString()
        {
            if (string.IsNullOrEmpty(server))
            {
                // todo
                //return I18N.GetString("New server");
                return "New server";
            }

            var serverStr = $"{FormalHostName}:{server_port}";
            return string.IsNullOrEmpty(remarks)
                ? serverStr
                : $"{remarks} ({serverStr})";
        }
    }

    public static class ServerEx
    {
        public static Server AddDefaultServerOrServer(ref Configuration config, Server server = null)
        {
            if (config?.servers != null)
            {
                server ??= ServerEx.GetDefaultServer();

                config.servers.Add(server);
            }
            return server;
        }

        public static Server GetDefaultServer() => new Server();

        /// <summary>
        /// Used by multiple forms to validate a server.
        /// Communication is done by throwing exceptions.
        /// </summary>
        /// <param name="server"></param>
        public static void CheckServer(Server server)
        {
            CheckServer(server.server);
            CheckPort(server.server_port);
            CheckPassword(server.password);
            CheckTimeout(server.timeout, Server.MaxServerTimeoutSec);
        }

        public static void CheckPort(int port)
        {
            if (port <= 0 || port > 65535)
                throw new ArgumentException("Port out of range");
        }

        public static void CheckLocalPort(int port)
        {
            CheckPort(port);
            if (port == 8123)
                throw new ArgumentException("Port can't be 8123");
        }

        private static void CheckPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password can not be blank");
        }

        public static void CheckServer(string server)
        {
            if (string.IsNullOrEmpty(server))
                throw new ArgumentException("Server IP can not be blank");
        }

        public static void CheckTimeout(int timeout, int maxTimeout)
        {
            if (timeout <= 0 || timeout > maxTimeout)
                throw new ArgumentException($"Timeout is invalid, it should not exceed {maxTimeout}");
        }
    }
}
