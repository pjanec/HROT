using System;
using System.Linq;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Export
{
    public class AssemblyReferenceTests
    {
        [Fact]
        public void RecordingExportService_Assembly_HasNoFdpPresentationOrRaylibReference()
        {
            var assembly = typeof(RecordingExportService).Assembly;
            var referencedNames = assembly.GetReferencedAssemblies()
                .Select(n => n.Name ?? string.Empty)
                .ToList();

            Assert.DoesNotContain(referencedNames,
                n => n.StartsWith("Fdp.Presentation", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(referencedNames,
                n => n.StartsWith("Raylib", StringComparison.OrdinalIgnoreCase));
        }
    }
}
