using Shadowsocks.Model;

using System;
using System.Collections.Specialized;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Shadowsocks.Util
{
    public static class ShadowsocksProtocol
    {
        #region ParseLegacyURL

        private static readonly Regex UrlFinder = new Regex(@"ss://(?<base64>[A-Za-z0-9+-/=_]+)(?:#(?<tag>\S+))?", RegexOptions.IgnoreCase);
        private static readonly Regex DetailsParser = new Regex(@"^((?<method>.+?):(?<password>.*)@(?<hostname>.+?):(?<port>\d+?))$", RegexOptions.IgnoreCase);

        #endregion ParseLegacyURL

        private static Server ParseLegacyURL(string ssURL)
        {
            var match = UrlFinder.Match(ssURL);
            if (!match.Success)
                return null;

            var base64 = match.Groups["base64"].Value.TrimEnd('/');
            Match details;
            try
            {
                details = DetailsParser.Match(
                    Encoding.UTF8.GetString(
                        Convert.FromBase64String(
                            base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=')
                            )));
            }
            catch (FormatException)
            {
                return null;
            }

            if (!details.Success)
                return null;


            var tag = match.Groups["tag"].Value;
            return new Server()
            {
                method = details.Groups["method"].Value,
                password = details.Groups["password"].Value,
                server = details.Groups["hostname"].Value,
                server_port = int.Parse(details.Groups["port"].Value),
                remarks = tag == null ? HttpUtility.UrlDecode(tag, Encoding.UTF8) : ""
            };
        }

        public static Server ParseURL(string serverUrl)
        {
            string _serverUrl = serverUrl.Trim();
            if (!_serverUrl.StartsWith("ss://", StringComparison.InvariantCultureIgnoreCase))
            {
                return null;
            }

            Server legacyServer = ParseLegacyURL(serverUrl);
            if (legacyServer != null)   //legacy
            {
                return legacyServer;
            }
            else   //SIP002
            {
                Uri parsedUrl;
                try
                {
                    parsedUrl = new Uri(serverUrl);
                }
                catch (UriFormatException)
                {
                    return null;
                }
                Server server = new Server
                {
                    remarks = HttpUtility.UrlDecode(parsedUrl.GetComponents(UriComponents.Fragment, UriFormat.Unescaped), Encoding.UTF8),
                    server = parsedUrl.IdnHost,
                    server_port = parsedUrl.Port,
                };

                // parse base64 UserInfo
                string rawUserInfo = parsedUrl.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped);
                string base64 = rawUserInfo.Replace('-', '+').Replace('_', '/');    // Web-safe base64 to normal base64
                string userInfo;
                try
                {
                    userInfo = Encoding.UTF8.GetString(Convert.FromBase64String(
                    base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=')));
                }
                catch (FormatException)
                {
                    return null;
                }
                string[] userInfoParts = userInfo.Split(new char[] { ':' }, 2);
                if (userInfoParts.Length != 2)
                {
                    return null;
                }
                server.method = userInfoParts[0];
                server.password = userInfoParts[1];

                NameValueCollection queryParameters = HttpUtility.ParseQueryString(parsedUrl.Query);
                string[] pluginParts = (queryParameters["plugin"] ?? "").Split(new[] { ';' }, 2);
                if (pluginParts.Length > 0)
                {
                    server.plugin = pluginParts[0] ?? "";
                }

                if (pluginParts.Length > 1)
                {
                    server.plugin_opts = pluginParts[1] ?? "";
                }

                return server;
            }
        }
    }
}
