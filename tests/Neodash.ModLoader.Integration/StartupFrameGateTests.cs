using Neodash_Mod_Loader.Ui;
using Xunit;

namespace Neodash.ModLoader.Integration;

public sealed class StartupFrameGateTests
{
    private static readonly StartupFrameSnapshot Ready = new(true, 1200, 800, 1, 1200, 800);

    [Fact]
    public void LoadedOuterWindowDoesNotRevealBeforeItsInitialPageIsReady()
    {
        var gate = new StartupFrameGate();
        var outerOnly = Ready with { ContentReady = false };
        Assert.False(gate.Observe(outerOnly));
        Assert.False(gate.Observe(outerOnly));
        Assert.False(gate.HasCandidate);
        Assert.False(gate.Observe(Ready));
        Assert.True(gate.HasCandidate);
        Assert.True(gate.Observe(Ready));
    }

    [Fact]
    public void StaleRenderSurfaceCannotRevealAResizedNativeWindow()
    {
        var gate = new StartupFrameGate();
        var stale = Ready with { ClientWidth = 1720, ClientHeight = 900 };
        Assert.False(gate.Observe(stale));
        Assert.False(gate.Observe(stale));
        Assert.False(gate.HasCandidate);
        var resized = stale with { Width = 1720, Height = 900 };
        Assert.False(gate.Observe(resized));
        Assert.True(gate.Observe(resized));
    }

    [Fact]
    public void ResizeAndIncompleteContentInvalidateThePreviousFrame()
    {
        var gate = new StartupFrameGate();
        Assert.False(gate.Observe(Ready));
        var resized = Ready with { Width = 1400, ClientWidth = 1400 };
        Assert.False(gate.Observe(resized));
        Assert.True(gate.Observe(resized));
        Assert.False(gate.Observe(resized with { ContentReady = false }));
        Assert.False(gate.Observe(resized));
        Assert.True(gate.Observe(resized));
    }

    [Fact]
    public void DpiChangeRequiresAConfirmingFrameAtTheNewScale()
    {
        var gate = new StartupFrameGate();
        Assert.False(gate.Observe(Ready));
        Assert.True(gate.Observe(Ready));
        var scaled = Ready with { Width = 800, Height = 800 / 1.5, Scale = 1.5 };
        Assert.False(gate.Observe(scaled));
        Assert.True(gate.Observe(scaled));
    }

    [Fact]
    public void InvalidOrNonFiniteDimensionsNeverQualify()
    {
        StartupFrameSnapshot[] invalid = [
            Ready with { Width = 0 }, Ready with { Height = 0 }, Ready with { Scale = 0 },
            Ready with { ClientWidth = 0 }, Ready with { ClientHeight = 0 },
            Ready with { Width = double.NaN }, Ready with { Height = double.PositiveInfinity },
            Ready with { Scale = double.NaN }, Ready with { Scale = -1 }
        ];
        var gate = new StartupFrameGate();
        foreach (var frame in invalid)
        {
            Assert.False(gate.Observe(Ready));
            Assert.False(gate.Observe(frame));
            Assert.False(gate.HasCandidate);
        }
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(1, true)]
    [InlineData(1.01, false)]
    public void PhysicalPixelRoundingIsLimitedToOnePixel(double difference, bool expected)
    {
        var frame = Ready with { Width = Ready.Width + difference, Height = Ready.Height - difference };
        var gate = new StartupFrameGate();
        Assert.False(gate.Observe(frame));
        Assert.Equal(expected, gate.Observe(frame));
    }
}
