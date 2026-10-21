using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Domain.Configuration
{
    public class VapidSettings
    {
        public string Subject { get; set; } = null!;
        public string PublicKey { get; set; } = null!;
        public string PrivateKey { get; set; } = null!;
    }
}
