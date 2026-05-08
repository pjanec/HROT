using Hrot.Common.Events;
using Hrot.Network.NED.IG;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Compile-time verification that <see cref="ContextActionsUpdateTranslator"/>
/// references <see cref="ContextActionsUpdate"/> from <c>Hrot.Common.Events</c>.
///
/// The old ParseActions tests (BUG2-E002) have been removed because ParseActions
/// was deleted: the translator now forwards <c>MenuDefinitionJson</c> as-is via
/// the protected TryDecode override. End-to-end coverage is provided by
/// <see cref="ContextMenuSystemTests"/>.
/// </summary>
public class ContextActionsUpdateTranslatorTests
{
    /// <summary>
    /// Verifies that <see cref="ContextActionsUpdate"/> (now in Hrot.Common.Events)
    /// is accessible from the test assembly and has the expected <c>MenuJson</c>
    /// property (not the old <c>Actions</c> list).
    /// </summary>
    [Fact]
    public void ContextActionsUpdate_HasMenuJsonProperty()
    {
        var evt = new ContextActionsUpdate
        {
            EntityNetworkId = 42,
            MenuJson        = """[{"id":1,"label":"Test"}]""",
        };

        Assert.Equal(42, evt.EntityNetworkId);
        Assert.Contains("Test", evt.MenuJson);
    }
}
