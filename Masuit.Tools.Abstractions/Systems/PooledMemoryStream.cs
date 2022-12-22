﻿using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;

namespace Masuit.Tools.Systems;

/// <summary>
/// 池化内存流
/// </summary>
public sealed class PooledMemoryStream : Stream
{
    /// <summary>
    /// 终结器
    /// </summary>
    ~PooledMemoryStream()
    {
        Dispose(true);
    }

    private const float OverExpansionFactor = 2;

    private byte[] _data = Array.Empty<byte>();
    private int _length;
    private readonly ArrayPool<byte> _pool;
    private bool _isDisposed;

    public PooledMemoryStream() : this(ArrayPool<byte>.Shared)
    {
    }

    public PooledMemoryStream(byte[] buffer) : this(ArrayPool<byte>.Shared, buffer.Length)
    {
        Array.Copy(buffer, 0, _data, 0, buffer.Length);
    }

    public PooledMemoryStream(ArrayPool<byte> arrayPool, int capacity = 0)
    {
        _pool = arrayPool ?? throw new ArgumentNullException(nameof(arrayPool));
        if (capacity > 0)
        {
            _data = _pool.Rent(capacity);
        }
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => true;

    public override long Length => _length;

    public override long Position { get; set; }

    public long Capacity => _data?.Length ?? 0;

#if NETCOREAPP || NET452
    public Span<byte> GetSpan()
    {
        return _data.AsSpan(0, _length);
    }

    public Memory<byte> GetMemory()
    {
        return _data.AsMemory(0, _length);
    }
    public ArraySegment<byte> ToArraySegment()
    {
        return new ArraySegment<byte>(_data, 0, (int)Length);
    }
#endif

    public override void Flush()
    {
        AssertNotDisposed();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        AssertNotDisposed();

        if (count == 0)
        {
            return 0;
        }

        var available = Math.Min(count, Length - Position);
        Array.Copy(_data, Position, buffer, offset, available);
        Position += available;
        return (int)available;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override long Seek(long offset, SeekOrigin origin)
    {
        AssertNotDisposed();

        switch (origin)
        {
            case SeekOrigin.Current:
                if (Position + offset < 0 || Position + offset > Capacity)
                {
                    throw new ArgumentOutOfRangeException(nameof(offset));
                }

                Position += offset;
                _length = (int)Math.Max(Position, _length);
                return Position;

            case SeekOrigin.Begin:
                if (offset < 0 || offset > Capacity)
                {
                    throw new ArgumentOutOfRangeException(nameof(offset));
                }

                Position = offset;
                _length = (int)Math.Max(Position, _length);
                return Position;

            case SeekOrigin.End:
                if (Length + offset < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(offset));
                }

                if (Length + offset > Capacity)
                {
                    SetCapacity((int)(Length + offset));
                }

                Position = Length + offset;
                _length = (int)Math.Max(Position, _length);
                return Position;

            default:
                throw new ArgumentOutOfRangeException(nameof(origin));
        }
    }

    public override void SetLength(long value)
    {
        AssertNotDisposed();

        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (value > Capacity)
        {
            SetCapacity((int)value);
        }

        _length = (int)value;
        if (Position > Length)
        {
            Position = Length;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(byte[] buffer, int offset, int count)
    {
        AssertNotDisposed();

        if (count == 0)
        {
            return;
        }

        if (Capacity - Position < count)
        {
            SetCapacity((int)(OverExpansionFactor * (Position + count)));
        }

        Buffer.BlockCopy(buffer, offset, _data, (int)Position, count);
        Position += count;
        _length = (int)Math.Max(Position, _length);
    }

    public void WriteTo(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        AssertNotDisposed();
        stream.Write(_data, 0, (int)Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _isDisposed = true;
            Position = 0;
            _length = 0;

            if (_data != null)
            {
                _pool.Return(_data);
                _data = null;
            }
        }

        base.Dispose(disposing);
    }

    private void SetCapacity(int newCapacity)
    {
        var newData = _pool.Rent(newCapacity);

        if (_data != null)
        {
            Buffer.BlockCopy(_data, 0, newData, 0, (int)Position);
            _pool.Return(_data);
        }

        _data = newData;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AssertNotDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(PooledMemoryStream));
        }
    }
}