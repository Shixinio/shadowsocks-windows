using Shadowsocks.Common.Model;
using Shadowsocks.PAC;
using Shadowsocks.Util;

using SimpleInjector;

using System.Linq;
using System.Reflection;

namespace Shadowsocks
{
    public static class Client
    {
        public static readonly string Version = Assembly.GetEntryAssembly().GetName().Version.ToString();

        static Client()
        {
            var container = IoCManager.Container;

            // Must register first.
            container.Register(() => ConfigurationManager.Load(), Lifestyle.Singleton);

            container.Register<IPACSource, GeositeSource>(Lifestyle.Singleton);

            var services = (
                from type in container.GetTypesToRegister<IService>(new[] { typeof(Client).Assembly })
                select Lifestyle.Singleton.CreateRegistration(type, container)
            ).ToArray();

            container.Collection.Register<IService>(services);

            container.Verify();
        }

        public static void Bootstrap()
        {
            foreach (var service in IoCManager.Container.GetAllInstances<IService>())
                service.Startup();
        }
    }
}
