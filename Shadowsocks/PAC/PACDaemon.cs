using Newtonsoft.Json;

using NLog;

using Shadowsocks.Common.Model;
using Shadowsocks.Model;
using Shadowsocks.PAC;
using Shadowsocks.Util;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Shadowsocks.Controller.Service
{

    public class PACUpdatedEventArgs : EventArgs
    {
        public bool Success;

        public PACUpdatedEventArgs(bool success)
        {
            Success = success;
        }
    }

    /// <summary>
    /// Processing the PAC file content
    /// </summary>
    public class PACDaemon : IService
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public const string PAC_FILE = "pac.txt";
        public const string USER_RULE_FILE = "user-rule.txt";
        public const string USER_ABP_FILE = "abp.txt";

        public static event EventHandler<PACUpdatedEventArgs> UpdateCompleted;
        public static event ErrorEventHandler Error;

        public event EventHandler PACFileChanged;
        public event EventHandler UserRuleFileChanged;

        private readonly IPACSource source;

        private FileSystemWatcher PACFileWatcher;
        private FileSystemWatcher UserRuleFileWatcher;

        public PACDaemon(IPACSource source)
        {
            this.source = source;

            TouchPACFile();
            TouchUserRuleFile();

            WatchPacFile();
            WatchUserRuleFile();
        }

        public string TouchPACFile()
        {
            if (!File.Exists(PAC_FILE))
                MergeAndWritePACFile();

            return PAC_FILE;
        }

        internal string TouchUserRuleFile()
        {
            if (!File.Exists(USER_RULE_FILE))
            {
                File.WriteAllText(USER_RULE_FILE, Resource.USER_RULE);
            }

            return USER_RULE_FILE;
        }

        internal string GetPACContent()
        {
            if (!File.Exists(PAC_FILE))
                MergeAndWritePACFile();

            return File.ReadAllText(PAC_FILE, Encoding.UTF8);
        }

        private static List<string> ProcessUserRules(string content)
        {
            var valid_lines = new List<string>();
            using (var stringReader = new StringReader(content))
            {
                for (var line = stringReader.ReadLine(); line != null; line = stringReader.ReadLine())
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("!") || line.StartsWith("["))
                        continue;

                    valid_lines.Add(line);
                }
            }
            return valid_lines;
        }

        public bool MergeAndWritePACFile() => MergeAndWritePACFile(source.directGroups, source.proxiedGroups, source.preferDirect);

        /// <summary>
        /// Merge and write pac.txt from geosite.
        /// Used at multiple places.
        /// </summary>
        /// <param name="directGroups">A list of geosite groups configured for direct connection.</param>
        /// <param name="proxiedGroups">A list of geosite groups configured for proxied connection.</param>
        /// <param name="blacklist">Whether to use blacklist mode. False for whitelist.</param>
        /// <returns></returns>
        public bool MergeAndWritePACFile(List<string> directGroups, List<string> proxiedGroups, bool blacklist)
        {
            var abpContent = MergePACFile(directGroups, proxiedGroups, blacklist);
            if (File.Exists(PAC_FILE))
            {
                var original = FileManager.NonExclusiveReadAllText(PAC_FILE, Encoding.UTF8);
                if (original == abpContent)
                    return false;
            }

            File.WriteAllText(PAC_FILE, abpContent, Encoding.UTF8);
            return true;
        }

        private string MergePACFile(List<string> directGroups, List<string> proxiedGroups, bool blacklist)
        {
            string abpContent;
            if (File.Exists(USER_ABP_FILE))
            {
                abpContent = FileManager.NonExclusiveReadAllText(USER_ABP_FILE, Encoding.UTF8);
            }
            else
            {
                abpContent = Resource.ABP_JS;
            }

            List<string> userruleLines = new List<string>();
            if (File.Exists(USER_RULE_FILE))
            {
                string userrulesString = FileManager.NonExclusiveReadAllText(USER_RULE_FILE, Encoding.UTF8);
                userruleLines = ProcessUserRules(userrulesString);
            }

            List<string> ruleLines = source.GenerateRules(directGroups, proxiedGroups, blacklist);
            abpContent =
$@"var __USERRULES__ = {JsonConvert.SerializeObject(userruleLines, Formatting.Indented)};
var __RULES__ = {JsonConvert.SerializeObject(ruleLines, Formatting.Indented)};
{abpContent}";
            return abpContent;
        }

        public void UpdatePACFromGeosite()
        {
            source.UpdateSource(Error);
            UpdateCompleted.Invoke(null, new PACUpdatedEventArgs(MergeAndWritePACFile()));
        }

        private void WatchPacFile()
        {
            PACFileWatcher?.Dispose();
            PACFileWatcher = new FileSystemWatcher(PathUtil.WorkingDirectory)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                Filter = PAC_FILE
            };
            PACFileWatcher.Changed += PACFileWatcher_Changed;
            PACFileWatcher.Created += PACFileWatcher_Changed;
            PACFileWatcher.Deleted += PACFileWatcher_Changed;
            PACFileWatcher.Renamed += PACFileWatcher_Changed;
            PACFileWatcher.EnableRaisingEvents = true;
        }

        private void WatchUserRuleFile()
        {
            UserRuleFileWatcher?.Dispose();
            UserRuleFileWatcher = new FileSystemWatcher(PathUtil.WorkingDirectory)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                Filter = USER_RULE_FILE
            };
            UserRuleFileWatcher.Changed += UserRuleFileWatcher_Changed;
            UserRuleFileWatcher.Created += UserRuleFileWatcher_Changed;
            UserRuleFileWatcher.Deleted += UserRuleFileWatcher_Changed;
            UserRuleFileWatcher.Renamed += UserRuleFileWatcher_Changed;
            UserRuleFileWatcher.EnableRaisingEvents = true;
        }

        #region FileSystemWatcher.OnChanged()

        // FileSystemWatcher Changed event is raised twice
        // http://stackoverflow.com/questions/1764809/filesystemwatcher-changed-event-is-raised-twice
        // Add a short delay to avoid raise event twice in a short period
        private void PACFileWatcher_Changed(object obj, FileSystemEventArgs e)
        {
            if (PACFileChanged != null)
            {
                _logger.Info($"Detected: PAC file '{e.Name}' was {e.ChangeType.ToString().ToLower()}.");
                Task.Factory.StartNew(() =>
                {
                    var sender = obj as FileSystemWatcher;

                    sender.EnableRaisingEvents = false;
                    System.Threading.Thread.Sleep(10);
                    PACFileChanged(this, new EventArgs());
                    sender.EnableRaisingEvents = true;
                });
            }
        }

        private void UserRuleFileWatcher_Changed(object obj, FileSystemEventArgs e)
        {
            if (UserRuleFileChanged != null)
            {
                _logger.Info($"Detected: User Rule file '{e.Name}' was {e.ChangeType.ToString().ToLower()}.");
                Task.Factory.StartNew(() =>
                {
                    var sender = obj as FileSystemWatcher;

                    sender.EnableRaisingEvents = false;
                    System.Threading.Thread.Sleep(10);
                    UserRuleFileChanged(this, new EventArgs());
                    sender.EnableRaisingEvents = true;
                });
            }
        }

        #endregion
    }
}
