using CommandLine;

using NLog;
using NLog.Config;

using Shadowsocks.Common.Model;
using Shadowsocks.Model;
using Shadowsocks.Util;

using System;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shadowsocks.Controller
{
    class RequestAddUrlEventArgs : EventArgs
    {
        public readonly string Url;

        public RequestAddUrlEventArgs(string url)
        {
            Url = url;
        }
    }

    public class CommandLineOption
    {
        [Option("open-url", Required = false, HelpText = "Add an ss:// URL")]
        public string OpenUrl { get; set; }
    }

    internal class IPCService : IService
    {
        private const int INT32_LEN = 4;
        private const int OP_OPEN_URL = 1;

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public static CommandLineOption Options { get; private set; }

        private static readonly string PIPE_PATH = $"Shadowsocks\\{PathUtil.ExecutablePath.GetHashCode()}";
        private static readonly Mutex mutex = new Mutex(true, $"Shadowsocks_{PathUtil.ExecutablePath.GetHashCode()}");

        public event EventHandler<RequestAddUrlEventArgs> OpenUrlRequested;

        private readonly Configuration config;
        private readonly IDialog dialog;

        public IPCService(IDialog dialog, Configuration config)
        {
            this.config = config;
            this.dialog = dialog;

            var hasAnotherInstance = !mutex.WaitOne(TimeSpan.Zero, true);

            // store args for further use
            Parser.Default.ParseArguments<CommandLineOption>(Environment.GetCommandLineArgs())
                .WithParsed(opt => Options = opt)
                .WithNotParsed(e => e.Output());

            if (hasAnotherInstance)
            {
                if (!string.IsNullOrWhiteSpace(Options.OpenUrl))
                {
                    RequestOpenUrl(Options.OpenUrl);
                }
                else
                {
                    var message = $"Find Shadowsocks icon in your notify tray.{Environment.NewLine}If you want to start multiple Shadowsocks, make a copy in another directory.";

                    dialog.Show(message);
                }
                return;
            }
        }

        public void Startup()
        {
            Task.Run(() => RunServer());

            OpenUrlRequested += (_, e) => AskAddServerBySSURL(e.Url);

            if (!string.IsNullOrWhiteSpace(Options.OpenUrl))
            {
                AskAddServerBySSURL(Options.OpenUrl);
            }
        }

        public bool AskAddServerBySSURL(string ssURL)
        {
            if (dialog.Ask($"Import from URL: {ssURL} ?") == DialogResult.Yes)
            {
                if (AddServerBySSURL(ssURL))
                {
                    dialog.Show($"Successfully imported from {ssURL}");
                    return true;
                }
                else
                    dialog.Show("Failed to import. Please check if the link is valid.");
            }

            return false;
        }

        public bool AddServerBySSURL(string ssURL)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ssURL))
                    return false;

                var servers = Server.GetServers(ssURL);
                if (servers == null || servers.Count == 0)
                    return false;

                config.servers.AddRange(servers);

                config.index = config.servers.Count - 1;
                // TODO:
                //SaveConfig(config);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogUsefulException(e);
            }

            return false;
        }

        public async void RunServer()
        {
            byte[] buf = new byte[4096];
            while (true)
            {
                using (NamedPipeServerStream stream = new NamedPipeServerStream(PIPE_PATH))
                {
                    await stream.WaitForConnectionAsync();
                    await stream.ReadAsync(buf, 0, INT32_LEN);
                    int opcode = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buf, 0));
                    if (opcode == OP_OPEN_URL)
                    {
                        await stream.ReadAsync(buf, 0, INT32_LEN);
                        int strlen = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buf, 0));

                        await stream.ReadAsync(buf, 0, strlen);
                        string url = Encoding.UTF8.GetString(buf, 0, strlen);

                        OpenUrlRequested?.Invoke(this, new RequestAddUrlEventArgs(url));
                    }
                    stream.Close();
                }
            }
        }

        private static (NamedPipeClientStream, bool) TryConnect()
        {
            NamedPipeClientStream pipe = new NamedPipeClientStream(PIPE_PATH);
            bool exist;
            try
            {
                pipe.Connect(10);
                exist = true;
            }
            catch (TimeoutException)
            {
                exist = false;
            }
            return (pipe, exist);
        }

        public static bool AnotherInstanceRunning()
        {
            (var pipe, var exist) = TryConnect();
            pipe.Dispose();
            return exist;
        }

        public static void RequestOpenUrl(string url)
        {
            (var pipe, var exist) = TryConnect();

            if (!exist) return;

            byte[] opAddUrl = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(OP_OPEN_URL));
            pipe.Write(opAddUrl, 0, INT32_LEN); // opcode addurl
            byte[] b = Encoding.UTF8.GetBytes(url);
            byte[] blen = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(b.Length));
            pipe.Write(blen, 0, INT32_LEN);
            pipe.Write(b, 0, b.Length);
            pipe.Close();
            pipe.Dispose();
        }
    }
}
