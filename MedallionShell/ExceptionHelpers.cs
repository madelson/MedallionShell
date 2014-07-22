using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    internal static class ExceptionHelpers
    {
        public static bool IsExpectedPipeException(this Exception @this)
        {
            var aggregateException = @this as AggregateException;
            if (aggregateException != null)
            {
                return aggregateException.InnerExceptions.All(IsExpectedPipeException);
            }
            var ioException = @this as IOException;
            if (ioException != null)
            {
                // this occurs when a head-like process stops reading from the input before we're done writing to it
                // see http://stackoverflow.com/questions/24876580/how-to-distinguish-programmatically-between-different-ioexceptions/24877149#24877149
                // see http://msdn.microsoft.com/en-us/library/cc231199.aspx
                return unchecked((uint)ioException.HResult) == 0x8007006D;
            }

            return @this.InnerException != null 
                ? IsExpectedPipeException(@this.InnerException) 
                : false;
        }
    }
}
