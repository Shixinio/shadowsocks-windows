using Newtonsoft.Json.Linq;

using NLog;

using Shadowsocks.Common.Model;
using Shadowsocks.Model;
using Shadowsocks.Util;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Shadowsocks.Controller
{
    public class UpdateChecker : IService
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // https://developer.github.com/v3/repos/releases/
        private const string UpdateURL = "https://api.github.com/repos/shadowsocks/shadowsocks-windows/releases";

        private readonly Configuration configuration;
        private JToken _releaseObject;

        public string NewReleaseVersion { get; private set; }
        public string NewReleaseZipFilename { get; private set; }

        public event EventHandler CheckUpdateCompleted;
        
        private readonly Version _version;

        public UpdateChecker(Configuration configuration)
        {
            this.configuration = configuration;
            _version = new Version(Client.Version);
        }

        /// <summary>
        /// Checks for updates and asks the user if updates are found.
        /// </summary>
        /// <param name="millisecondsDelay">A delay in milliseconds before checking.</param>
        /// <returns></returns>
        public async Task CheckForVersionUpdate(int millisecondsDelay = 0)
        {
            // delay
            _logger.Info($"Waiting for {millisecondsDelay}ms before checking for version update.");
            await Task.Delay(millisecondsDelay);
            // start
            _logger.Info($"Checking for version update.");
            try
            {
                // list releases via API
                var releasesListJsonString = await Utils.HttpClient.GetStringAsync(UpdateURL);
                // parse
                var releasesJArray = JArray.Parse(releasesListJsonString);
                foreach (var releaseObject in releasesJArray)
                {
                    var releaseTagName = (string)releaseObject["tag_name"];
                    var releaseVersion = new Version(releaseTagName);
                    if (releaseTagName == configuration.skippedUpdateVersion) // finished checking
                        break;
                    if (releaseVersion.CompareTo(_version) > 0 &&
                        (!(bool)releaseObject["prerelease"] || configuration.checkPreRelease && (bool)releaseObject["prerelease"])) // selected
                    {
                        _logger.Info($"Found new version {releaseTagName}.");
                        _releaseObject = releaseObject;
                        NewReleaseVersion = releaseTagName;
                        // todo
                        // AskToUpdate(releaseObject);
                        return;
                    }
                }
                _logger.Info($"No new versions found.");
                CheckUpdateCompleted?.Invoke(this, new EventArgs());
            }
            catch (Exception e)
            {
                _logger.LogUsefulException(e);
            }
        }

        /// <summary>
        /// Opens a window to show the update's information.
        /// </summary>
        /// <param name="releaseObject">The update release object.</param>
        //private void AskToUpdate(JToken releaseObject)
        //{
        //    if (versionUpdatePromptWindow == null)
        //    {
        //        versionUpdatePromptWindow = new Window()
        //        {
        //            Title = LocalizationProvider.GetLocalizedValue<string>("VersionUpdate"),
        //            Height = 480,
        //            Width = 640,
        //            MinHeight = 480,
        //            MinWidth = 640,
        //            Content = new VersionUpdatePromptView(releaseObject)
        //        };
        //        versionUpdatePromptWindow.Closed += VersionUpdatePromptWindow_Closed;
        //        versionUpdatePromptWindow.Show();
        //    }
        //    versionUpdatePromptWindow.Activate();
        //}

        //private void VersionUpdatePromptWindow_Closed(object sender, EventArgs e)
        //{
        //    versionUpdatePromptWindow = null;
        //}

        /// <summary>
        /// Downloads the selected update and notifies the user.
        /// </summary>
        /// <returns></returns>
        public async Task DoUpdate()
        {
            try
            {
                var assets = (JArray)_releaseObject["assets"];
                // download all assets
                foreach (JObject asset in assets)
                {
                    var filename = (string)asset["name"];
                    var browser_download_url = (string)asset["browser_download_url"];
                    var response = await Utils.HttpClient.GetAsync(browser_download_url);
                    using (var downloadedFileStream = File.Create(PathUtil.GetTempPath(filename)))
                        await response.Content.CopyToAsync(downloadedFileStream);
                    _logger.Info($"Downloaded {filename}.");
                    // store .zip filename
                    if (filename.EndsWith(".zip"))
                        NewReleaseZipFilename = filename;
                }
                _logger.Info("Finished downloading.");
                // notify user
                // todo
                //CloseVersionUpdatePromptWindow();
                Process.Start("explorer.exe", $"/select, \"{PathUtil.GetTempPath(NewReleaseZipFilename)}\"");
            }
            catch (Exception e)
            {
                _logger.LogUsefulException(e);
            }
        }

        /// <summary>
        /// Saves the skipped update version.
        /// </summary>
        public void SkipUpdate()
        {
            var version = (string)_releaseObject["tag_name"] ?? "";
            configuration.skippedUpdateVersion = version;
            // todo
            // Program.MainController.SaveSkippedUpdateVerion(version);
            _logger.Info($"The update {version} has been skipped and will be ignored next time.");
            // todo
         //   CloseVersionUpdatePromptWindow();
        }

        /// <summary>
        /// Closes the update prompt window.
        /// </summary>
        //public void CloseVersionUpdatePromptWindow()
        //{
        //    if (versionUpdatePromptWindow != null)
        //    {
        //        versionUpdatePromptWindow.Close();
        //        versionUpdatePromptWindow = null;
        //    }
        //}
    }
}
