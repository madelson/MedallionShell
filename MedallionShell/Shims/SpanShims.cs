#if !NETCOREAPP2_1_OR_GREATER && !NETSTANDARD2_1
#pragma warning disable SA1649 // File name should match first type name

using System.Diagnostics;

namespace System;

internal static class MemoryExtensions
{
    public static Span<T> AsSpan<T>(this T[] array, int start, int length) => new(new(array, start, length));
}

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

internal readonly ref struct Span<T>(Memory<T> memory)
{
    public readonly Memory<T> Memory = memory;

    public int Length => this.Memory.Length;

    public static implicit operator Span<T>(T[] array) => new(new(array, 0, array.Length));

    public void CopyTo(Span<T> destination) => this.Memory.CopyTo(destination.Memory);

    public Span<T> Slice(int start) => new(this.Memory.Slice(start));
}
#endif
