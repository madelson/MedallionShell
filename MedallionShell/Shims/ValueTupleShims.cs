#if !NETCOREAPP && !NET47_OR_GREATER && !NETSTANDARD2_0_OR_GREATER
#pragma warning disable SA1649 // File name should match first type name

namespace System;

internal struct ValueTuple<T1, T2, T3>(T1 item1, T2 item2, T3 item3)
{
    public T1 Item1 = item1;
    public T2 Item2 = item2;
    public T3 Item3 = item3;
}
#endif