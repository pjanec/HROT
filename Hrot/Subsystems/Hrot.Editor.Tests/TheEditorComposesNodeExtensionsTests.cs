using System.Linq;
using Fdp.Toolkit.Runner;
using Hrot.Common.Infrastructure;
using Hrot.Editor;
using Hrot.Tests.ExtensionStaging;
using Xunit;

namespace Hrot.Editor.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NodeExtensionsStaticsCollection { public const string Name = "NodeExtensionsStatics"; }

/// <summary>
/// ⭐ CE-4293 (R-4158; design §5 rails 4 and 5) — the headless editor, composed by the shared bootstrap, takes an installed extension's capability LAST,
/// runs its system every frame, and switching its feature off (F-2) stops that system. Without the extensions folder its capability list is the base list.
/// NodeExtensions is process-wide: this class runs alone.
/// </summary>
[Collection(NodeExtensionsStaticsCollection.Name)]
public sealed class TheEditorComposesNodeExtensionsTests
{
    private static string[] ComposedKeys(EditorSubsystem editor) => editor.CapabilitiesForTest.Select(c => c.Key).ToArray();

    [Fact(Timeout = 120_000)]
    public void CE4293_TheEditorTakesAnExtensionsCapabilityLast_RunsItsSystem_AndItsFeatureSwitchesItOff()
    {
        NodeExtensions.ResetForTests();
        string[] baseline;
        var plain = new EditorSubsystem();
        plain.Initialize(new SubsystemConfig { Headless = true });
        try { baseline = ComposedKeys(plain); }
        finally { plain.Shutdown(); }
        Assert.NotEmpty(baseline);
        Assert.DoesNotContain("sample.extension", baseline);

        NodeExtensions.LoadAll(SampleExtensionStaging.SharedBaseDirectory);
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try
        {
            Assert.Equal(baseline.Append("sample.extension"), ComposedKeys(editor));   // the base list, then the extension - nothing else moved

            var executions = NodeExtensions.Discovered[0].GetType().GetField("Executions")!;
            var gated = NodeExtensions.Discovered[0].GetType().GetField("GatedExecutions")!;
            int before = (int)executions.GetValue(null)!;
            int gatedBefore = (int)gated.GetValue(null)!;
            editor.TimeControllerForTest!.SwitchToContinuous();
            for (int i = 0; i < 5; i++) { editor.Update(0.016f); System.Threading.Thread.Sleep(20); }
            int running = (int)executions.GetValue(null)!;
            Assert.True(running > before, $"the extension's system must run in the editor's frames ({before} -> {running})");
            Assert.True((int)gated.GetValue(null)! > gatedBefore, "the extension's module system must run in the editor's frames too");

            // F-2: the capability's declared feature is on the node and is the switch.
            var features = editor.NodeForTest!.Features!;
            Assert.True(features.Registry.Available("sample.extension.feature"));
            features.SetNodeOverride("sample.extension.feature", "{\"Enabled\":false}");
            editor.Update(0.016f);   // the change lands at a frame boundary
            int off = (int)gated.GetValue(null)!;
            int groupOff = (int)executions.GetValue(null)!;
            for (int i = 0; i < 5; i++) { editor.Update(0.016f); System.Threading.Thread.Sleep(20); }
            Assert.Equal(off, (int)gated.GetValue(null)!);                        // the featured module system is gone ...
            Assert.True((int)executions.GetValue(null)! > groupOff);             // ... the rest of the node and the extension's other system run on
        }
        finally
        {
            editor.Shutdown();
            NodeExtensions.ResetForTests();
        }
    }
}
