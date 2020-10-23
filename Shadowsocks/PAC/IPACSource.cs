using System.Collections.Generic;
using System.IO;

namespace Shadowsocks.PAC
{
    public interface IPACSource
    {
        public List<string> directGroups { get; }

        public List<string> proxiedGroups { get; }

        public bool preferDirect { get; }

        List<string> GenerateRules(List<string> directGroups, List<string> proxiedGroups, bool blacklist);

        void UpdateSource(ErrorEventHandler error);
    }
}
