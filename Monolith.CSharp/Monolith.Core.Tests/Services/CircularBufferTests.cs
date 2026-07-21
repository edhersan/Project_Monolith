using Xunit;
using Monolith.Voice.Services;

namespace Monolith.Core.Tests.Services;

public class CircularBufferTests
{
    [Fact]
    public void Write_Read_RoundTrip()
    {
        var buf = new CircularBuffer(100);
        var data = new byte[] { 1, 2, 3, 4, 5 };
        buf.Write(data);
        Assert.Equal(5, buf.Count);
        var result = buf.ReadAll();
        Assert.Equal(data, result);
    }

    [Fact]
    public void Write_Overflow_KeepsLatest()
    {
        var buf = new CircularBuffer(10);
        buf.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        Assert.Equal(10, buf.Count);
        Assert.True(buf.IsFull);
        buf.Write(new byte[] { 11, 12 });
        Assert.Equal(10, buf.Count);
        var result = buf.ReadAll();
        Assert.Equal(new byte[] { 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, result);
    }

    [Fact]
    public void ReadLast_ReturnsCorrectBytes()
    {
        var buf = new CircularBuffer(50);
        for (int i = 0; i < 10; i++)
            buf.Write(new byte[] { (byte)i });
        var last5 = buf.ReadLast(5);
        Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, last5);
    }

    [Fact]
    public void ReadLast_RequestMoreThanCount_ReturnsAll()
    {
        var buf = new CircularBuffer(50);
        buf.Write(new byte[] { 1, 2, 3 });
        var result = buf.ReadLast(100);
        Assert.Equal(new byte[] { 1, 2, 3 }, result);
    }

    [Fact]
    public void Clear_ResetsState()
    {
        var buf = new CircularBuffer(100);
        buf.Write(new byte[] { 1, 2, 3 });
        buf.Clear();
        Assert.Equal(0, buf.Count);
        Assert.Empty(buf.ReadAll());
    }

    [Fact]
    public void Read_WithOffset_Works()
    {
        var buf = new CircularBuffer(100);
        buf.Write(new byte[] { 10, 20, 30, 40, 50 });
        var dest = new byte[3];
        var read = buf.Read(dest, 1);
        Assert.Equal(3, read);
        Assert.Equal(new byte[] { 20, 30, 40 }, dest);
    }

    [Fact]
    public void Write_LargeData_WrapsCorrectly()
    {
        var buf = new CircularBuffer(16);
        var data = new byte[32];
        for (int i = 0; i < 32; i++) data[i] = (byte)i;
        buf.Write(data);
        Assert.Equal(16, buf.Count);
        var result = buf.ReadAll();
        Assert.Equal(new byte[] { 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31 }, result);
    }
}
