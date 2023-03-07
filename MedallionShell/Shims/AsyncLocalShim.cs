// note: we use the shim implementation for our NETFRAMEWORK tests in DEBUG just to get coverage
#if NET45 || (DEBUG && NETFRAMEWORK)
#pragma warning disable SA1649 // File name should match first type name
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell;

internal sealed class AsyncLocal<T> where T : class? // needed for ConditionalWeakTable impl; ok for our purposes
{
    // CallContext values must be serializable; this indirection should eliminate any chance of that burning us
    private static readonly ConditionalWeakTable<object, T> Storage = new();

    private readonly string key = $"AsyncLocal<{typeof(T)}>_{Guid.NewGuid()}";

    public T? Value
    {
        get => CallContext.LogicalGetData(this.key) is { } storageKey 
            ? Storage.TryGetValue(storageKey, out var value) 
                ? value 
                : throw new KeyNotFoundException() 
            : default;
        set
        {
            object? storageKey;
            if (value is null) { storageKey = null; }
            else { Storage.Add(storageKey = new(), value); }
            CallContext.LogicalSetData(this.key, storageKey);
        }
    }
}
#endif
