namespace Monolith.Voice.Services;

public class CircularBuffer
{
    private readonly byte[] _buffer;
    private int _writePos;
    private int _count;

    public CircularBuffer(int capacityBytes)
    {
        _buffer = new byte[capacityBytes];
    }

    public int Capacity => _buffer.Length;
    public int Count => _count;
    public bool IsFull => _count >= _buffer.Length;

    public void Write(ReadOnlySpan<byte> data)
    {
        var remaining = data.Length;
        var offset = 0;

        while (remaining > 0)
        {
            var chunkSize = Math.Min(remaining, _buffer.Length - _writePos);
            data.Slice(offset, chunkSize).CopyTo(new Span<byte>(_buffer, _writePos, chunkSize));
            _writePos = (_writePos + chunkSize) % _buffer.Length;
            _count = Math.Min(_count + chunkSize, _buffer.Length);
            offset += chunkSize;
            remaining -= chunkSize;
        }
    }

    public int Read(Span<byte> destination, int startOffset)
    {
        if (startOffset < 0 || startOffset >= _count)
            return 0;

        var readPos = (_writePos - _count + startOffset + _buffer.Length) % _buffer.Length;
        var bytesToRead = Math.Min(destination.Length, _count - startOffset);
        var bytesRead = 0;

        while (bytesRead < bytesToRead)
        {
            var chunkSize = Math.Min(bytesToRead - bytesRead, _buffer.Length - readPos);
            new Span<byte>(_buffer, readPos, chunkSize).CopyTo(destination.Slice(bytesRead, chunkSize));
            readPos = (readPos + chunkSize) % _buffer.Length;
            bytesRead += chunkSize;
        }

        return bytesRead;
    }

    public byte[] ReadAll()
    {
        var result = new byte[_count];
        Read(result, 0);
        return result;
    }

    public byte[] ReadLast(int byteCount)
    {
        var actual = Math.Min(byteCount, _count);
        var result = new byte[actual];
        Read(result, _count - actual);
        return result;
    }

    public void Clear()
    {
        _writePos = 0;
        _count = 0;
    }
}
