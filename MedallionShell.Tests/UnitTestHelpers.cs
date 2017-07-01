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

        public static T ShouldNotEqual<T>(this T @this, T that, string message = null)
        {
            Assert.AreNotEqual(that, @this, message);
            return @this;
        }

        public static TException AssertThrows<TException>(Action action, string message = null)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException ex) { return ex; }
            catch (Exception other)
            {
                Assert.Fail($"{(message != null ? message + ": " : string.Empty)}Expected {typeof(TException)}. Found {other}");
            }

            Assert.Fail($"{(message != null ? message + ": " : string.Empty)}Expected {typeof(TException)}, but no exception was thrown");

            throw new InvalidOperationException("Should never get here");
        }

        public static void AssertDoesNotThrow(Action action, string message = null)
        {
            try { action(); }
            catch (Exception ex)
            {
                Assert.Fail($"Expected: no failure; was: '{ex}'{(message != null ? message + ": " : string.Empty)}");
            }
        }

        public static void AssertIsInstanceOf<T>(object value, string message = null)
        {
            Assert.IsInstanceOfType(value, typeof(T), message);
        }

        public static string ShouldContain(this string haystack, string needle, string message = null)
        {
            Assert.IsNotNull(haystack, $"Expected: contains '{needle}'. Was: NULL{(message != null ? $" ({message})" : string.Empty)}");
            if (!haystack.Contains(needle))
            {
                Assert.Fail($"Expected: contains '{needle}'. Was: '{haystack}'{(message != null ? $" ({message})" : string.Empty)}");
            }

            return haystack;
        }
    }
}
