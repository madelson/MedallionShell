using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell.Tests
{
    public static class UnitTestHelpers
    {
        public static T ShouldEqual<T>(this T @this, T that, string message = null)
        {
            Assert.AreEqual(that, @this, message);
            return @this;
        }

        public static TException AssertThrows<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                var result = ex as TException;
                if (result == null)
                {
                    Assert.Fail("Expected {0}, got {1}", typeof(TException), ex);
                }
                return result;
            }

            Assert.Fail("Expected {0}, but no exception was thrown", typeof(TException));
            return null;
        }
    }
}
