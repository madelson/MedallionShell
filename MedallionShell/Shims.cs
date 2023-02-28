using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Medallion.Shell;

internal static class Shims
{
    public static T[] EmptyArray<T>() =>
#if !NET45
        Array.Empty<T>();
#else
        Empty<T>.Array;

    private static class Empty<T>
    {
        public static readonly T[] Array = new T[0];
    }
#endif

#if !NETCOREAPP2_1_OR_GREATER && !NETSTANDARD2_1
    public static Span<T> AsSpan<T>(this T[] array, int start, int length) => new(new(array, start, length));
#endif
}

#pragma warning disable SA1306 // Field names should begin with lower-case letter

#if !NETCOREAPP2_1_OR_GREATER && !NETSTANDARD2_1
internal readonly struct Memory<T>
{
    public readonly T[] Array;
    public readonly int Offset;
    public readonly int Length;

    public Span<T> Span => new(this);

    public Memory(T[] array, int offset, int length)
    {
        Debug.Assert(offset >= 0 && length >= 0 && offset + length <= array.Length, "buffer must be valid");

        this.Array = array;
        this.Offset = offset;
        this.Length = length;
    }

    public Memory<T> Slice(int start) => this.Slice(start, this.Length - start);
    public Memory<T> Slice(int start, int length) => new(this.Array, this.Offset + start, length);

    public void CopyTo(Memory<T> destination) =>
        System.Array.Copy(sourceArray: this.Array, sourceIndex: this.Offset, destinationArray: destination.Array, destinationIndex: destination.Offset, length: this.Length);
}

internal readonly ref struct Span<T>
{
    public readonly Memory<T> Memory;

    public int Length => this.Memory.Length;

    public Span(Memory<T> memory) { this.Memory = memory; }

    public static implicit operator Span<T>(T[] array) => new(new(array, 0, array.Length));

    public void CopyTo(Span<T> destination) => this.Memory.CopyTo(destination.Memory);

    public Span<T> Slice(int start) => new(this.Memory.Slice(start));
}
#endif
