using FluentAssertions;
using StructEdit.Core;
using StructEdit.Core.Plugins;
using StructEdit.Core.Memory;
using StructEdit.Reflection;

namespace StructEdit.Tests.Session;

// ── Shared test fixtures ──────────────────────────────────────────────────────

file record SimpleRecord(int X, float Y);

file struct SimpleStruct { public int X; public float Y; }

file class SimpleClass { public int X { get; set; } public float Y { get; set; } }

// ── Validators ────────────────────────────────────────────────────────────────

file sealed class AlwaysFailValidator : IComponentValidator
{
    public ValidationResult Validate(EditValidationContext ctx)
        => ValidationResult.Fail(new[] { new ValidationError("$", "Always fails") });
}

file sealed class CapturingValidator : IComponentValidator
{
    public EditValidationContext? Received { get; private set; }

    public ValidationResult Validate(EditValidationContext ctx)
    {
        Received = ctx;
        return ValidationResult.Ok();
    }
}

// ── Custom component editor ───────────────────────────────────────────────────

file sealed class CustomRecordEditor : ICustomComponentEditor
{
    public Type ComponentType => typeof(SimpleRecord);

    public EditDocument BuildDocument(IEditBuffer buffer, EditScope scope, EditContext? context)
    {
        var root = new EditNode(
            new EditNodeId(1), "CustomRoot", "$",
            EditNodeKind.Class, typeof(SimpleRecord));
        return new EditDocument(root, typeof(SimpleRecord), scope);
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class ServiceHelper
{
    public static IComponentEditService DefaultService()
        => new ComponentEditServiceBuilder().Build();

    public static IEditSession OpenRecord(int x = 5, float y = 0f)
    {
        var service = DefaultService();
        return service.Open(new SimpleRecord(x, y), typeof(SimpleRecord));
    }

    public static IEditSession OpenStruct(int x = 5, float y = 0f)
    {
        var service = DefaultService();
        return service.Open(new SimpleStruct { X = x, Y = y }, typeof(SimpleStruct));
    }

    /// <summary>
    /// Finds the binding for a property/field named <paramref name="name"/>
    /// by walking the document root's children (depth=1).
    /// </summary>
    public static IValueBinding FindBinding(IEditSession session, string name)
    {
        var node = session.Document.Root.Children.First(c => c.Name == name);
        return node.Binding ?? throw new InvalidOperationException($"Node '{name}' has no binding");
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// TASK-S001: ComponentEditServiceBuilder & IComponentEditService
// ══════════════════════════════════════════════════════════════════════════════

public class ServiceBuilderTests
{
    // S001-T1: Open() returns a non-null IEditSession
    [Fact]
    public void Open_ReturnsSession()
    {
        var service = ServiceHelper.DefaultService();
        using var session = service.Open(new SimpleRecord(1, 2f), typeof(SimpleRecord));
        session.Should().NotBeNull();
    }

    // S001-T2: Returned session has a non-null Document with a non-null Root
    [Fact]
    public void Open_SessionHasNonNullDocument()
    {
        using var session = ServiceHelper.OpenRecord();
        session.Document.Should().NotBeNull();
        session.Document.Root.Should().NotBeNull();
    }

    // S001-T3: When a custom ICustomComponentEditor is registered, it is used
    [Fact]
    public void Open_UsesCustomComponentEditor_WhenRegistered()
    {
        var service = new ComponentEditServiceBuilder()
            .RegisterComponentEditor(new CustomRecordEditor())
            .Build();
        using var session = service.Open(new SimpleRecord(1, 0f), typeof(SimpleRecord));

        session.Document.Root.Name.Should().Be("CustomRoot");
        session.Document.Root.Kind.Should().Be(EditNodeKind.Class);
    }

    // S001-T4: Registered validator is invoked by Validate()
    [Fact]
    public void Open_RegisteredValidatorIsUsed()
    {
        var service = new ComponentEditServiceBuilder()
            .RegisterValidator<SimpleRecord>(new AlwaysFailValidator())
            .Build();
        using var session = service.Open(new SimpleRecord(1, 0f), typeof(SimpleRecord));

        var result = session.Validate();
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// TASK-S002: EditSession lifecycle
// ══════════════════════════════════════════════════════════════════════════════

public class EditSessionTests
{
    // S002-T1: IsDirty is false immediately after Open
    [Fact]
    public void IsDirty_FalseOnOpen()
    {
        using var session = ServiceHelper.OpenStruct();
        session.IsDirty.Should().BeFalse();
    }

    // S002-T2: IsDirty becomes true after a field write
    [Fact]
    public void IsDirty_TrueAfterWrite()
    {
        using var session = ServiceHelper.OpenStruct(x: 5);
        ServiceHelper.FindBinding(session, "X").SetBoxed(99);
        session.IsDirty.Should().BeTrue();
    }

    // S002-T3: Commit() returns the replacement with the written value
    [Fact]
    public void Commit_ReturnsReplacementWithWrittenValue()
    {
        using var session = ServiceHelper.OpenRecord(x: 5);
        ServiceHelper.FindBinding(session, "X").SetBoxed(9);
        var result = (SimpleRecord)session.Commit();
        result.X.Should().Be(9);
    }

    // S002-T4: Commit() can be called twice and returns consistent values
    [Fact]
    public void Commit_CalledTwice_ReturnsSameValues()
    {
        using var session = ServiceHelper.OpenRecord(x: 5);
        ServiceHelper.FindBinding(session, "X").SetBoxed(9);

        var first = (SimpleRecord)session.Commit();
        var second = (SimpleRecord)session.Commit();

        first.X.Should().Be(9);
        second.X.Should().Be(9);
    }

    // S002-T5: Commit() throws EditValidationException when validator fails
    [Fact]
    public void Commit_ThrowsEditValidationException_WhenValidatorFails()
    {
        var service = new ComponentEditServiceBuilder()
            .RegisterValidator<SimpleRecord>(new AlwaysFailValidator())
            .Build();
        using var session = service.Open(new SimpleRecord(1, 0f), typeof(SimpleRecord));

        session.Invoking(s => s.Commit())
            .Should().Throw<EditValidationException>()
            .Which.Result.IsValid.Should().BeFalse();
    }

    // S002-T6: Cancel() leaves the original object unchanged
    [Fact]
    public void Cancel_LeavesOriginalUnchanged()
    {
        var original = new SimpleRecord(5, 0f);
        using var session = ServiceHelper.DefaultService().Open(original, typeof(SimpleRecord));
        ServiceHelper.FindBinding(session, "X").SetBoxed(99);
        session.Cancel();
        // The original reference must still hold its original value
        original.X.Should().Be(5);
    }

    // S002-T7: Dispose() is idempotent (calling twice does not throw)
    [Fact]
    public void Dispose_IsIdempotent()
    {
        var session = ServiceHelper.OpenRecord();
        session.Invoking(s =>
        {
            s.Dispose();
            s.Dispose();
        }).Should().NotThrow();
    }

    // S002-T8: Accessing Document after Dispose throws ObjectDisposedException
    [Fact]
    public void Document_ThrowsObjectDisposedException_AfterDispose()
    {
        var session = ServiceHelper.OpenRecord();
        session.Dispose();
        session.Invoking(s => _ = s.Document)
            .Should().Throw<ObjectDisposedException>();
    }

    // S002-T9: RebuildDocument() resets RebuildState to Stable
    [Fact]
    public void RebuildDocument_ResetsRebuildStateToStable()
    {
        using var session = ServiceHelper.OpenRecord();
        session.MarkStructuralChange();
        session.RebuildState.Should().Be(EditRebuildState.RebuildRequired);

        session.RebuildDocument();
        session.RebuildState.Should().Be(EditRebuildState.Stable);
    }

    // S002-T10: Validate() receives the full buffer (not just the scoped subset)
    [Fact]
    public void Validate_ReceivesFullBuffer_RegardlessOfScope()
    {
        var validator = new CapturingValidator();
        var service = new ComponentEditServiceBuilder()
            .RegisterValidator<SimpleRecord>(validator)
            .Build();
        var scope = EditScope.ForField(EditPath.Parse("$.X"));
        using var session = service.Open(new SimpleRecord(1, 2f), typeof(SimpleRecord), scope: scope);

        session.Validate();

        validator.Received.Should().NotBeNull();
        validator.Received!.ComponentType.Should().Be(typeof(SimpleRecord));
        // Buffer exposes the full component type, not just the scoped field
        validator.Received.Buffer.ComponentType.Should().Be(typeof(SimpleRecord));
    }

    // S002-T11: Two concurrent sessions on the same type have independent buffers
    [Fact]
    public void TwoConcurrentSessions_HaveIndependentBuffers()
    {
        var service = ServiceHelper.DefaultService();

        using var session1 = service.Open(new SimpleStruct { X = 1 }, typeof(SimpleStruct));
        using var session2 = service.Open(new SimpleStruct { X = 2 }, typeof(SimpleStruct));

        ServiceHelper.FindBinding(session1, "X").SetBoxed(10);
        ServiceHelper.FindBinding(session2, "X").SetBoxed(20);

        var result1 = (SimpleStruct)session1.Commit();
        var result2 = (SimpleStruct)session2.Commit();

        result1.X.Should().Be(10);
        result2.X.Should().Be(20);
    }

    // S002-T12: Commit() on a record session returns the record with the edited field
    [Fact]
    public void Commit_Record_ReturnsRecordWithEditedField()
    {
        using var session = ServiceHelper.OpenRecord(x: 5, y: 1.5f);
        ServiceHelper.FindBinding(session, "X").SetBoxed(42);

        var result = (SimpleRecord)session.Commit();
        result.X.Should().Be(42);
        result.Y.Should().BeApproximately(1.5f, 0.001f);
    }

    // ── TASK-T003: Additional session edge-case tests ─────────────────────

    // T003-1: Validate() with no registered validator returns Ok
    [Fact]
    public void Validate_NoValidator_ReturnsOk()
    {
        using var session = ServiceHelper.OpenRecord();
        var result = session.Validate();
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // T003-2: RebuildDocument() produces a new Document object reference
    [Fact]
    public void RebuildDocument_ChangesDocumentReference()
    {
        using var session = ServiceHelper.OpenRecord();
        var before = session.Document;
        session.RebuildDocument();
        session.Document.Should().NotBeSameAs(before);
    }

    // T003-3: Multiple MarkStructuralChange() calls still leave RebuildRequired
    [Fact]
    public void MarkStructuralChange_CalledMultipleTimes_StillRebuildRequired()
    {
        using var session = ServiceHelper.OpenRecord();
        session.MarkStructuralChange();
        session.MarkStructuralChange();
        session.MarkStructuralChange();
        session.RebuildState.Should().Be(EditRebuildState.RebuildRequired);
    }

    // T003-4: Opening a session with a scoped multi-field selection → root is SelectionRoot
    [Fact]
    public void Open_MultiFieldScope_RootIsSelectionRoot()
    {
        var service = ServiceHelper.DefaultService();
        var scope = EditScope.ForFields("$.X", "$.Y");
        using var session = service.Open(new SimpleStruct { X = 1, Y = 2f }, typeof(SimpleStruct), scope: scope);
        session.Document.Root.Kind.Should().Be(EditNodeKind.SelectionRoot);
    }

    // T003-5: Commit() after RebuildDocument() returns the correct committed value
    [Fact]
    public void Commit_AfterRebuildDocument_ReturnsCorrectValue()
    {
        using var session = ServiceHelper.OpenStruct(x: 5);
        ServiceHelper.FindBinding(session, "X").SetBoxed(99);
        session.MarkStructuralChange();
        session.RebuildDocument();
        var result = (SimpleStruct)session.Commit();
        result.X.Should().Be(99);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// DEBT-05: Managed binding IsDirty tests
// ══════════════════════════════════════════════════════════════════════════════

public class ManagedBindingDirtyTests
{
    // DEBT05-T1: Writing via property binding marks session dirty (managed reference type)
    [Fact]
    public void Write_ViaPropertyBinding_ManagedClass_IsDirtyTrue()
    {
        var service = ServiceHelper.DefaultService();
        using var session = service.Open(new SimpleClass { X = 1 }, typeof(SimpleClass));
        session.IsDirty.Should().BeFalse();

        ServiceHelper.FindBinding(session, "X").SetBoxed(42);
        session.IsDirty.Should().BeTrue();
    }

    // DEBT05-T2: Writing via property binding on a record marks session dirty
    [Fact]
    public void Write_ViaPropertyBinding_Record_IsDirtyTrue()
    {
        var service = ServiceHelper.DefaultService();
        using var session = service.Open(new SimpleRecord(0, 0f), typeof(SimpleRecord));
        session.IsDirty.Should().BeFalse();

        ServiceHelper.FindBinding(session, "X").SetBoxed(7);
        session.IsDirty.Should().BeTrue();
    }

    // DEBT05-T3: Cancel() after write still returns unmodified committed value (managed class)
    [Fact]
    public void Write_ThenCancel_ManagedClass_OriginalUnchanged()
    {
        var original = new SimpleClass { X = 5 };
        var service = ServiceHelper.DefaultService();
        using var session = service.Open(original, typeof(SimpleClass));

        ServiceHelper.FindBinding(session, "X").SetBoxed(999);
        session.IsDirty.Should().BeTrue();
        session.Cancel();

        original.X.Should().Be(5, "Cancel() must not affect the original object");
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// TASK-P001: Custom Field Editor tests
// ══════════════════════════════════════════════════════════════════════════════

file class GuidComponent { public Guid Id { get; set; } }
file class DateTimeComponent { public DateTime CreatedAt { get; set; } }
file class ColorValue { public int R { get; set; } public int G { get; set; } }
file class ColorHolder { public ColorValue? Color { get; set; } }

file sealed class ColorFieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(ColorValue);

    public EditNode CreateNode(EditNodeId id, string name, string jsonPath,
        IValueBinding binding, EditNodeMetadata metadata)
        => new EditNode(id, name, jsonPath, EditNodeKind.Custom, typeof(ColorValue), binding, null, metadata);
}

public class CustomFieldEditorTests
{
    // P001-T1: Register GuidFieldEditor → node kind is Guid
    [Fact]
    public void RegisterGuidEditor_NodeKindIsGuid()
    {
        var service = new ComponentEditServiceBuilder()
            .RegisterFieldEditor<Guid>(new StructEdit.Reflection.Editors.GuidFieldEditor())
            .Build();
        using var session = service.Open(new GuidComponent(), typeof(GuidComponent));

        var node = session.Document.Root.Children.First(c => c.Name == "Id");
        node.Kind.Should().Be(EditNodeKind.Guid);
    }

    // P001-T2: Register DateTimeFieldEditor → node kind is DateTime
    [Fact]
    public void RegisterDateTimeEditor_NodeKindIsDateTime()
    {
        var service = new ComponentEditServiceBuilder()
            .RegisterFieldEditor<DateTime>(new StructEdit.Reflection.Editors.DateTimeFieldEditor())
            .Build();
        using var session = service.Open(new DateTimeComponent(), typeof(DateTimeComponent));

        var node = session.Document.Root.Children.First(c => c.Name == "CreatedAt");
        node.Kind.Should().Be(EditNodeKind.DateTime);
    }

    // P001-T3: No custom editor registered → Guid kind still detected by default path
    [Fact]
    public void NoCustomEditor_GuidDetectedByDefault()
    {
        var service = new ComponentEditServiceBuilder().Build();
        using var session = service.Open(new GuidComponent(), typeof(GuidComponent));

        var node = session.Document.Root.Children.First(c => c.Name == "Id");
        node.Kind.Should().Be(EditNodeKind.Guid);
    }

    // P001-T4: Custom editor for a custom type → node kind matches
    [Fact]
    public void CustomEditorForCustomType_NodeKindIsCustom()
    {
        var service = new ComponentEditServiceBuilder()
            .RegisterFieldEditor<ColorValue>(new ColorFieldEditor())
            .Build();
        using var session = service.Open(new ColorHolder { Color = new ColorValue { R = 255, G = 0 } }, typeof(ColorHolder));

        var colorNode = session.Document.Root.Children.FirstOrDefault(c => c.Name == "Color");
        colorNode.Should().NotBeNull();
        colorNode!.Kind.Should().Be(EditNodeKind.Custom);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// TASK-P002: Custom Component Editor tests
// ══════════════════════════════════════════════════════════════════════════════

file class AnotherClass { public int Value { get; set; } }

file sealed class AnotherClassComponentEditor : ICustomComponentEditor
{
    public Type ComponentType => typeof(AnotherClass);

    public EditDocument BuildDocument(IEditBuffer buffer, EditScope scope, EditContext? context)
    {
        var root = new EditNode(new EditNodeId(1), "CustomAnotherRoot", "$",
            EditNodeKind.Custom, typeof(AnotherClass));
        return new EditDocument(root, typeof(AnotherClass), scope);
    }
}

public class CustomComponentEditorTests
{
    // P002-T1: Registered custom component editor → Open() uses it → custom document structure
    [Fact]
    public void RegisteredComponentEditor_UsedForMatchingType()
    {
        var service = new ComponentEditServiceBuilder()
            .RegisterComponentEditor(new AnotherClassComponentEditor())
            .Build();

        using var session = service.Open(new AnotherClass { Value = 1 }, typeof(AnotherClass));
        session.Document.Root.Name.Should().Be("CustomAnotherRoot");
        session.Document.Root.Kind.Should().Be(EditNodeKind.Custom);
    }

    // P002-T2: No custom editor → falls through to reflection builder → standard document
    [Fact]
    public void NoComponentEditor_UsesReflectionBuilder()
    {
        var service = new ComponentEditServiceBuilder().Build();
        using var session = service.Open(new AnotherClass { Value = 5 }, typeof(AnotherClass));

        // Reflection builder produces a Class root with a Value child
        session.Document.Root.Kind.Should().Be(EditNodeKind.Class);
        session.Document.Root.Children.Should().Contain(c => c.Name == "Value");
    }

    // P002-T3: Custom editor for AnotherClass doesn't affect SimpleClass sessions
    [Fact]
    public void ComponentEditorForTypeA_DoesNotAffectTypeB()
    {
        var service = new ComponentEditServiceBuilder()
            .RegisterComponentEditor(new AnotherClassComponentEditor())
            .Build();

        using var sessionB = service.Open(new SimpleClass { X = 7 }, typeof(SimpleClass));
        // SimpleClass is unaffected — reflection builder used
        sessionB.Document.Root.Kind.Should().Be(EditNodeKind.Class);
        sessionB.Document.Root.Children.Should().Contain(c => c.Name == "X");
    }
}
