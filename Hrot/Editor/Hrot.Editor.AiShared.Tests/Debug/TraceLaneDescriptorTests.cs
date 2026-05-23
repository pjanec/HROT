using Hrot.Editor.AiShared.Debug;

namespace Hrot.Editor.AiShared.Tests.Debug;

public class TraceLaneDescriptorTests
{
    [Fact]
    public void Record_EqualityByValues()
    {
        var a = new TraceLaneDescriptor("lane1", "Lane One", TraceLevel.Lifecycle);
        var b = new TraceLaneDescriptor("lane1", "Lane One", TraceLevel.Lifecycle);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Record_DisplayName_IsSet()
    {
        var descriptor = new TraceLaneDescriptor("id", "My Lane", TraceLevel.Decisions);
        Assert.Equal("My Lane", descriptor.DisplayName);
    }

    [Fact]
    public void Record_SupportedLevels_IsSet()
    {
        var descriptor = new TraceLaneDescriptor("id", "Lane", TraceLevel.Values | TraceLevel.Errors);
        Assert.Equal(TraceLevel.Values | TraceLevel.Errors, descriptor.SupportedLevels);
    }
}
