using Hrot.Map.Definitions.Behavior;

namespace Hrot.Map.Common.Tests;

/// <summary>
/// Unit tests for <see cref="BehaviorCategory"/> additions (TASK-TI005).
/// </summary>
public class BehaviorCategoryTests
{
    // SC-1: Commander value is 16
    [Fact]
    public void Commander_Value_Is16()
    {
        Assert.Equal(16, (int)BehaviorCategory.Commander);
    }

    // SC-2: Commander is NOT part of AllMilitary
    [Fact]
    public void AllMilitary_DoesNotContain_Commander()
    {
        Assert.False(BehaviorCategory.AllMilitary.HasFlag(BehaviorCategory.Commander));
    }
}
