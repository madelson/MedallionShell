using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    internal static class Constants
    {
        public const int ByteBufferSize = 1024,
            CharBufferSize = ByteBufferSize / sizeof(char);
    }
}
