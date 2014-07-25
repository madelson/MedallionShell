using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    public abstract class CommandLineSyntax
    {
        public abstract string CreateArgumentString(IEnumerable<string> arguments);
    }
}
