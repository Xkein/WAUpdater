using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WAUpdater
{
    public class UpdateMirror
    {
        public UpdateMirror(string name, string uriBase, string location, long fileSizeLimit = long.MaxValue)
        {
            Name = name;
            UriBase = uriBase;
            Location = location;
            FileSizeLimit = fileSizeLimit;
        }

        public string Name { get; }
        public string UriBase { get; }
        public string Location { get; }
        public long FileSizeLimit { get; }

        public override string ToString()
        {
            return $"{Name}-{Location}, {UriBase}";
        }
    }
}
