using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Medallion.Shell.Tests
{
    public static class UnitTestHelpers
    {
        public static T ShouldEqual<T>(this T @this, T that, string message = null)
        {
            Assert.Equal(that, @this);
            return @this;
        }

    }
}
