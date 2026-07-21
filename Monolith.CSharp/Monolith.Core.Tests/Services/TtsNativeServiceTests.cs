using System;
using System.Threading;
using System.Threading.Tasks;
using Monolith.Voice.Native;
using Xunit;

namespace Monolith.Core.Tests.Services;

public class TtsNativeServiceTests
{
    [Fact]
    public void CreateAndDispose_NoCrash()
    {
        using var service = new TtsNativeService();
    }

    [Fact]
    public async Task SpeakAsync_NoDll_DoesNotThrow()
    {
        // Native DLL not available → SpeakAsync logs and returns gracefully
        using var service = new TtsNativeService();
        var ex = await Record.ExceptionAsync(() =>
            service.SpeakAsync("test"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task SpeakAsync_CancelledBeforeCall_DoesNotThrow()
    {
        using var service = new TtsNativeService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Record.ExceptionAsync(() =>
            service.SpeakAsync("test", cts.Token));

        Assert.Null(ex);
    }

    [Fact]
    public void DoubleDispose_NoCrash()
    {
        var service = new TtsNativeService();
        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public void MultipleServices_CreateAndDispose_NoCrash()
    {
        for (int i = 0; i < 3; i++)
        {
            using var service = new TtsNativeService();
        }
    }
}
