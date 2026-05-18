using FluentAssertions;
using StructEdit.Core;
using StructEdit.Core.Plugins;
using StructEdit.Core.UnionSupport;
using StructEdit.Json;
using StructEdit.Reflection;

namespace StructEdit.Tests.Integration;

// ── Fixtures ──────────────────────────────────────────────────────────────────

file struct IntegStruct { public int X; public float Y; }
file record IntegRecord(int X, float Y);

file class IntegClass
{
    public int Score { get; set; }
    public bool IsActive { get; set; }
}

// Struct with fixed buffer for BufferView provider integration test
file unsafe struct FixedBufComponent { public int Mode; public fixed byte Data[8]; }

file struct DataOverlay { public float A; public float B; }

file sealed class DataOverlayProvider : IBufferViewProvider
{
    public bool CanCreateView(BufferViewRequest request)
        => request.ComponentType == typeof(FixedBufComponent)
           && request.BufferPath.Value == "$.Data";

    public BufferViewResult CreateView(BufferViewRequest request)
        => request.ProjectBufferAs(typeof(DataOverlay), "DataOverlay");
}

// Validator that fails when X <= 0
file sealed class PositiveXValidator : IComponentValidator
{
    public ValidationResult Validate(EditValidationContext ctx)
    {
        var boxed = ctx.Buffer.Box();
        var score = (IntegStruct)boxed;
        if (score.X <= 0)
            return ValidationResult.Fail(new[] { new ValidationError("$.X", "X must be positive") });
        return ValidationResult.Ok();
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class IntegHelper
{
    public static IComponentEditService DefaultService()
        => new ComponentEditServiceBuilder().Build();

    public static IValueBinding FindBinding(IEditSession session, string name)
    {
        var node = session.Document.Root.Children.First(c => c.Name == name);
        return node.Binding ?? throw new InvalidOperationException($"No binding for '{name}'");
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// TASK-T006: End-to-end integration tests
// ══════════════════════════════════════════════════════════════════════════════

public class IntegrationTests
{
    // T006-1: Open → write → commit returns written value (struct)
    [Fact]
    public void Struct_WriteAndCommit_ReturnsWrittenValue()
    {
        var service = IntegHelper.DefaultService();
        using var session = service.Open(new IntegStruct { X = 1, Y = 0f }, typeof(IntegStruct));

        IntegHelper.FindBinding(session, "X").SetBoxed(42);

        var result = (IntegStruct)session.Commit();
        result.X.Should().Be(42);
    }

    // T006-2: Open → write → commit returns written value (record)
    [Fact]
    public void Record_WriteAndCommit_ReturnsWrittenValue()
    {
        var service = IntegHelper.DefaultService();
        using var session = service.Open(new IntegRecord(X: 0, Y: 0f), typeof(IntegRecord));

        IntegHelper.FindBinding(session, "X").SetBoxed(99);

        var result = (IntegRecord)session.Commit();
        result.X.Should().Be(99);
    }

    // T006-3: Open → write → ToJson → LoadJson → commit returns same value
    [Fact]
    public void WriteSerializeDeserializeCommit_RoundTrip()
    {
        var service = IntegHelper.DefaultService();

        using var s1 = service.Open(new IntegClass { Score = 5, IsActive = false }, typeof(IntegClass));
        IntegHelper.FindBinding(s1, "Score").SetBoxed(77);
        IntegHelper.FindBinding(s1, "IsActive").SetBoxed(true);
        var json = s1.ToJson();

        using var s2 = service.Open(new IntegClass(), typeof(IntegClass));
        s2.LoadJson(json);
        var result = (IntegClass)s2.Commit();

        result.Score.Should().Be(77);
        result.IsActive.Should().BeTrue();
    }

    // T006-4: Validator blocks invalid commit; valid value commits successfully
    [Fact]
    public void Validator_BlocksInvalid_AllowsValid()
    {
        var service = new ComponentEditServiceBuilder()
            .RegisterValidator<IntegStruct>(new PositiveXValidator())
            .Build();

        using var session = service.Open(new IntegStruct { X = 5, Y = 0f }, typeof(IntegStruct));

        // Write invalid value (X ≤ 0)
        IntegHelper.FindBinding(session, "X").SetBoxed(-1);
        session.Invoking(s => s.Commit())
            .Should().Throw<EditValidationException>();

        // Write valid value (X > 0)
        IntegHelper.FindBinding(session, "X").SetBoxed(10);
        var result = (IntegStruct)session.Commit();
        result.X.Should().Be(10);
    }

    // T006-5: Register buffer view provider → document has BufferView node
    [Fact]
    public void BufferViewProvider_DocumentHasBufferViewNode()
    {
        var service = new ComponentEditServiceBuilder()
            .RegisterBufferViewProvider(new DataOverlayProvider())
            .Build();

        unsafe
        {
            using var session = service.Open(new FixedBufComponent { Mode = 0 }, typeof(FixedBufComponent));
            var root = session.Document.Root;

            var bufViewNode = root.Children.FirstOrDefault(c => c.Kind == EditNodeKind.BufferView);
            bufViewNode.Should().NotBeNull("DataOverlayProvider should replace the FixedBuffer with a BufferView");
            bufViewNode!.Name.Should().Be("DataOverlay");
        }
    }

    // T006-6: Two independent sessions commit independent values
    [Fact]
    public void TwoSessions_CommitIndependently()
    {
        var service = IntegHelper.DefaultService();

        using var s1 = service.Open(new IntegStruct { X = 1, Y = 0f }, typeof(IntegStruct));
        using var s2 = service.Open(new IntegStruct { X = 2, Y = 0f }, typeof(IntegStruct));

        IntegHelper.FindBinding(s1, "X").SetBoxed(100);
        IntegHelper.FindBinding(s2, "X").SetBoxed(200);

        var r1 = (IntegStruct)s1.Commit();
        var r2 = (IntegStruct)s2.Commit();

        r1.X.Should().Be(100);
        r2.X.Should().Be(200);
    }
}
