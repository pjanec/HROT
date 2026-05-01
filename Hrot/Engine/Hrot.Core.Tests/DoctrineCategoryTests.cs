using Hrot.Map.Definitions.Doctrine;

namespace Hrot.Map.Common.Tests;

/// <summary>
/// Unit tests for <see cref="DoctrineCategory"/> additions (TASK-TI005).
/// </summary>
public class DoctrineCategoryTests
{
    // SC-1: Commander value is 16
    [Fact]
    public void Commander_Value_Is16()
    {
        Assert.Equal(16, (int)DoctrineCategory.Commander);
    }

    // SC-2: Commander is NOT part of AllMilitary
    [Fact]
    public void AllMilitary_DoesNotContain_Commander()
    {
        Assert.False(DoctrineCategory.AllMilitary.HasFlag(DoctrineCategory.Commander));
    }
}
