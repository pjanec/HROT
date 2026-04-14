using System.Collections.Generic;
using Hrot.Editor;
using Hrot.Editor.UI;
using Moq;
using Xunit;

namespace Hrot.Editor.Tests;

public class EntityPropertyInspectorTests
{
    [Fact]
    public void HandleCommitEdit_CallsCommitPropertyEdit()
    {
        var mock       = new Mock<IEditorLogic>();
        var panel      = new EntityPropertyInspector();
        var components = new List<object> { "SomeComponent" };
        panel.HandleCommitEdit(mock.Object, networkId: 42L, components: components);
        mock.Verify(l => l.CommitPropertyEdit(42L, components), Times.Once);
    }
}
