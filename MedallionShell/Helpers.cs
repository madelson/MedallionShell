using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    internal static class Helpers
    {
        public static T As<T>(this T @this)
        {
            return @this;
        }
    }

    internal static class Throw
    {
        /// <summary>
        /// Throws an <see cref="ArgumentNullException"/> if the given value is null
        /// </summary>
        public static void IfNull<T>(T value, string parameterName)
        {
            Throw<ArgumentNullException>.If(value == null, parameterName);
        }

        /// <summary>
        /// Throws an <see cref="ArgumentException"/> if the given condition is true
        /// </summary>
        public static void If(bool condition, string parameterName)
        {
            Throw<ArgumentException>.If(condition, parameterName);
        }

        /// <summary>
        /// Throws an <see cref="ArgumentOutOfRangeException"/> if the given value is outside of the specified range
        /// </summary>
        public static void IfOutOfRange<T>(T value, string paramName, T? min = null, T? max = null)
            where T : struct, IComparable<T>
        {
            if (min.HasValue && value.CompareTo(min.Value) < 0)
            {
                throw new ArgumentOutOfRangeException(paramName, string.Format("Expected: >= {0}, but was {1}", min, value));
            }
            if (max.HasValue && value.CompareTo(max.Value) > 0)
            {
                throw new ArgumentOutOfRangeException(paramName, string.Format("Expected: <= {0}, but was {1}", max, value));
            }
        }

        public static void IfInvalidBuffer<T>(T[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException("offset");
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException("count");
            }
            if (offset + count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException("the segment described by offset and count must be within buffer");
            }
        }

        public static NotSupportedException NotSupported([CallerMemberName] string memberName = null)
        {
            throw new NotSupportedException(memberName);
        }
    }

    internal static class Throw<TException>
        where TException : Exception
    {
        /// <summary>
        /// Throws an exception of type <typeparamref name="TException"/> if the condition is true
        /// </summary>
        public static void If(bool condition, string message)
        {
            if (condition)
            {
                throw Create(message);
            }
        }

        /// <summary>
        /// As <see cref="Throw.If(bool, string)"/>, but allows the message to be specified lazily. The message function will only be evaluated
        /// if the condition is true
        /// </summary>
        public static void If(bool condition, Func<string> message)
        {
            if (condition)
            {
                throw Create(message());
            }
        }

        private static TException Create(string message)
        {
            return (TException)Activator.CreateInstance(typeof(TException), message);
        }
    }
}
