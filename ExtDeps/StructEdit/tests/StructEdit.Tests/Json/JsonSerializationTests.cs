using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using StructEdit.Core;
using StructEdit.Json;
using StructEdit.Reflection;

namespace StructEdit.Tests.Json;

// ── Test fixtures ─────────────────────────────────────────────────────────────

file enum TestMode { Inactive, Active, Pending }

file class ScalarComponent
{
    public int Score { get; set; }
}

file class MixedComponent
{
    public int Score     { get; set; }
    public bool IsActive { get; set; }
    public TestMode Mode { get; set; }
}

file record RecordComponent(int X, float Y);

// InnerVector + NestedStruct are blittable value types so the builder
// uses NativeStructEditBuffer, which correctly resolves nested-field byte offsets.
file struct InnerVector { public float X; public float Y; }
file struct NestedStruct { public InnerVector Position; public int Count; }

[InlineArray(3)]
file struct Float3 { private float _e0; }

// Native (blittable) struct that contains an InlineArray — required for InlineArrayBinding
file struct InlineArrayHost { public Float3 Data; }

file class DynListComponent
{
    public List<int> Items { get; set; } = new();
}

// ── Shared helpers ────────────────────────────────────────────────────────────

file static class JsonTestHelper
{
    public static IComponentEditService Service()
        => new ComponentEditServiceBuilder().Build();

    public static IEditSession Open<T>(T component) where T : class
        => Service().Open(component, typeof(T));

    public static IEditSession Open(object component, Type type)
        => Service().Open(component, type);

    /// <summary>Finds the first child node named <paramref name="name"/> in the document root.</summary>
    public static IValueBinding Binding(IEditSession session, string name)
    {
        var node = session.Document.Root.Children.First(c => c.Name == name);
        return node.Binding ?? throw new InvalidOperationException($"No binding for '{name}'");
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// TASK-J001: Serialization tests (≥ 8)
// ══════════════════════════════════════════════════════════════════════════════

public class JsonSerializationTests
{
    // J001-T1: Scalar value appears in the JSON output
    [Fact]
    public void Serialize_ScalarValue_AppearsInJson()
    {
        using var session = JsonTestHelper.Open(new ScalarComponent { Score = 42 });
        var json = session.ToJson();

        json.Should().Contain("42");
        json.Should().Contain("$.Score");
    }

    // J001-T2: Enum value is serialized as its string name, not an integer
    [Fact]
    public void Serialize_Enum_WritesStringName()
    {
        // Score=99 so that no integer 1 appears for the Enum (Active=1)
        using var session = JsonTestHelper.Open(
            new MixedComponent { Score = 99, IsActive = true, Mode = TestMode.Active });
        var json = session.ToJson();

        // Enum must be serialized as its string name
        json.Should().Contain("\"Active\"");
        // The raw enum ordinal (1) must not appear as the value for the Mode field
        json.Should().NotContain("\"Active\": 1");
    }

    // J001-T3: Boolean true/false are written as JSON booleans
    [Fact]
    public void Serialize_Boolean_WritesJsonBoolean()
    {
        using var session = JsonTestHelper.Open(
            new MixedComponent { Score = 0, IsActive = true, Mode = TestMode.Inactive });
        var json = session.ToJson();

        json.Should().Contain("true");
    }

    // J001-T4: Nested struct fields have correct JSON paths
    [Fact]
    public void Serialize_NestedStruct_CorrectPaths()
    {
        // NestedStruct is a blittable struct — builder uses NativeStructEditBuffer
        // which correctly computes byte offsets for nested fields.
        using var session = JsonTestHelper.Open(
            new NestedStruct { Position = new InnerVector { X = 1.5f, Y = 2.5f }, Count = 7 },
            typeof(NestedStruct));
        var json = session.ToJson();

        json.Should().Contain("$.Position.X");
        json.Should().Contain("$.Position.Y");
        json.Should().Contain("$.Count");
    }

    // J001-T5: InlineArray is serialized as a values array with the correct element count
    [Fact]
    public void Serialize_InlineArray_WritesValuesArray()
    {
        var component = new InlineArrayHost();
        using var session = JsonTestHelper.Open(component, typeof(InlineArrayHost));
        var json = session.ToJson();

        json.Should().Contain("\"values\"");
        json.Should().Contain("InlineArray");
    }

    // J001-T6: DynamicArray entry contains count and children
    [Fact]
    public void Serialize_DynamicArray_WritesCountAndChildren()
    {
        using var session = JsonTestHelper.Open(
            new DynListComponent { Items = new List<int> { 10, 20, 30 } });
        var json = session.ToJson();

        json.Should().Contain("\"count\"");
        json.Should().Contain("\"children\"");
        json.Should().Contain("\"count\": 3");
    }

    // J001-T7: Record type serialized with correct field paths and values
    [Fact]
    public void Serialize_Record_CorrectPathsAndValues()
    {
        using var session = JsonTestHelper.Open(new RecordComponent(X: 5, Y: 1.5f));
        var json = session.ToJson();

        json.Should().Contain("$.X");
        json.Should().Contain("$.Y");
        json.Should().Contain("5");
    }

    // J001-T8: JSON output parses without error
    [Fact]
    public void Serialize_OutputIsValidJson()
    {
        using var session = JsonTestHelper.Open(
            new MixedComponent { Score = 99, IsActive = false, Mode = TestMode.Pending });
        var json = session.ToJson();

        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// TASK-J002: Deserialization tests (≥ 6)
// ══════════════════════════════════════════════════════════════════════════════

public class JsonDeserializationTests
{
    // J002-T1: Full round-trip: write → ToJson → LoadJson → Commit returns same values
    [Fact]
    public void RoundTrip_ScalarAndBoolAndEnum_ValuesPreserved()
    {
        var service = JsonTestHelper.Service();

        // Session 1: write values and serialize
        string json;
        using (var s1 = service.Open(
            new MixedComponent { Score = 77, IsActive = true, Mode = TestMode.Pending },
            typeof(MixedComponent)))
        {
            json = s1.ToJson();
        }

        // Session 2: load JSON and commit
        using var s2 = service.Open(new MixedComponent(), typeof(MixedComponent));
        s2.LoadJson(json);
        var result = (MixedComponent)s2.Commit();

        result.Score.Should().Be(77);
        result.IsActive.Should().BeTrue();
        result.Mode.Should().Be(TestMode.Pending);
    }

    // J002-T2: LoadJson with wrong rootTypeName throws EditJsonMismatchException
    [Fact]
    public void LoadJson_WrongTypeName_ThrowsEditJsonMismatchException()
    {
        // Serialize a ScalarComponent
        string json;
        using (var s1 = JsonTestHelper.Open(new ScalarComponent { Score = 1 }))
            json = s1.ToJson();

        // Try to load into a MixedComponent session — type mismatch
        using var s2 = JsonTestHelper.Open(new MixedComponent());
        var act = () => s2.LoadJson(json);

        act.Should().Throw<EditJsonMismatchException>()
           .Which.JsonPath.Should().Be("rootTypeName");
    }

    // J002-T3: LoadJson with wrong schema version throws EditJsonMismatchException
    [Fact]
    public void LoadJson_WrongVersion_ThrowsEditJsonMismatchException()
    {
        var typeName = typeof(ScalarComponent).AssemblyQualifiedName;
        var badJson = $$"""
            {
              "structedit_version": "9.9",
              "rootTypeName": "{{typeName}}",
              "scope": "$",
              "nodes": []
            }
            """;

        using var session = JsonTestHelper.Open(new ScalarComponent());
        var act = () => session.LoadJson(badJson);

        act.Should().Throw<EditJsonMismatchException>()
           .Which.JsonPath.Should().Be("structedit_version");
    }

    // J002-T4: Enum value is loaded correctly from its string name
    [Fact]
    public void LoadJson_Enum_ParsedFromStringName()
    {
        var service = JsonTestHelper.Service();

        string json;
        using (var s1 = service.Open(
            new MixedComponent { Score = 0, IsActive = false, Mode = TestMode.Active },
            typeof(MixedComponent)))
            json = s1.ToJson();

        using var s2 = service.Open(new MixedComponent(), typeof(MixedComponent));
        s2.LoadJson(json);
        var result = (MixedComponent)s2.Commit();

        result.Mode.Should().Be(TestMode.Active);
    }

    // J002-T5: Nested struct fields are loaded at the correct paths
    [Fact]
    public void LoadJson_NestedStruct_FieldsLoadedAtCorrectPaths()
    {
        var service = JsonTestHelper.Service();

        string json;
        using (var s1 = service.Open(
            new NestedStruct { Position = new InnerVector { X = 3.5f, Y = 7.0f }, Count = 10 },
            typeof(NestedStruct)))
            json = s1.ToJson();

        using var s2 = service.Open(new NestedStruct(), typeof(NestedStruct));
        s2.LoadJson(json);
        var result = (NestedStruct)s2.Commit();

        result.Position.X.Should().BeApproximately(3.5f, 0.001f);
        result.Position.Y.Should().BeApproximately(7.0f, 0.001f);
        result.Count.Should().Be(10);
    }

    // J002-T6: After LoadJson, Commit() returns component with all loaded values
    [Fact]
    public void LoadJson_ThenCommit_ReturnsComponentWithLoadedValues()
    {
        var service = JsonTestHelper.Service();

        // Serialize with known values
        string json;
        using (var s1 = service.Open(new ScalarComponent { Score = 55 }, typeof(ScalarComponent)))
            json = s1.ToJson();

        // Load into a fresh session starting from a different value
        using var s2 = service.Open(new ScalarComponent { Score = 0 }, typeof(ScalarComponent));
        s2.LoadJson(json);
        var result = (ScalarComponent)s2.Commit();

        result.Score.Should().Be(55);
    }

    // J002-T7: DynamicArray round-trip — count and element values preserved
    [Fact]
    public void RoundTrip_DynamicArray_CountAndElementsPreserved()
    {
        var service = JsonTestHelper.Service();

        string json;
        using (var s1 = service.Open(
            new DynListComponent { Items = new List<int> { 10, 20, 30 } },
            typeof(DynListComponent)))
            json = s1.ToJson();

        // Fresh session with empty list
        using var s2 = service.Open(new DynListComponent(), typeof(DynListComponent));
        s2.LoadJson(json);
        var result = (DynListComponent)s2.Commit();

        result.Items.Should().HaveCount(3);
        result.Items[0].Should().Be(10);
        result.Items[1].Should().Be(20);
        result.Items[2].Should().Be(30);
    }
}

// ── TASK-T005 JSON fixtures ───────────────────────────────────────────────────

file class GuidComponent
{
    public Guid Id { get; set; }
}

file class DateTimeComponent
{
    public DateTime CreatedAt { get; set; }
}

// ── TASK-T005: Guid / DateTime serialization tests ───────────────────────────

public class GuidDateTimeJsonTests
{
    // T005-1: Serialize Guid field → value is string in "D" format (xxxxxxxx-xxxx-...)
    [Fact]
    public void Serialize_GuidField_ValueIsStringInDFormat()
    {
        var id = Guid.NewGuid();
        using var session = JsonTestHelper.Open(new GuidComponent { Id = id });
        var json = session.ToJson();

        json.Should().Contain(id.ToString("D"));
    }

    // T005-2: Serialize DateTime field → value is ISO 8601 "O" string
    [Fact]
    public void Serialize_DateTimeField_ValueIsIso8601String()
    {
        var dt = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        using var session = JsonTestHelper.Open(new DateTimeComponent { CreatedAt = dt });
        var json = session.ToJson();

        json.Should().Contain(dt.ToString("O"));
    }

    // T005-3: Round-trip Guid field exactly
    [Fact]
    public void RoundTrip_GuidField_Preserved()
    {
        var service = JsonTestHelper.Service();
        var originalId = Guid.NewGuid();

        string json;
        using (var s1 = service.Open(new GuidComponent { Id = originalId }, typeof(GuidComponent)))
            json = s1.ToJson();

        using var s2 = service.Open(new GuidComponent(), typeof(GuidComponent));
        s2.LoadJson(json);
        var result = (GuidComponent)s2.Commit();

        result.Id.Should().Be(originalId);
    }

    // T005-4: Round-trip DateTime field exactly
    [Fact]
    public void RoundTrip_DateTimeField_Preserved()
    {
        var service = JsonTestHelper.Service();
        var dt = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        string json;
        using (var s1 = service.Open(new DateTimeComponent { CreatedAt = dt }, typeof(DateTimeComponent)))
            json = s1.ToJson();

        using var s2 = service.Open(new DateTimeComponent(), typeof(DateTimeComponent));
        s2.LoadJson(json);
        var result = (DateTimeComponent)s2.Commit();

        result.CreatedAt.Should().Be(dt);
    }

    // T005-5: LoadJson with an unknown path silently skips it (no exception)
    [Fact]
    public void LoadJson_UnknownPath_SilentlySkipped()
    {
        var typeName = typeof(ScalarComponent).AssemblyQualifiedName;
        var jsonWithUnknownPath = $$"""
            {
              "structedit_version": "1.0",
              "rootTypeName": "{{typeName}}",
              "scope": "$",
              "nodes": [
                { "path": "$.Score", "kind": "Scalar", "value": 42 },
                { "path": "$.NonExistentField", "kind": "Scalar", "value": 99 }
              ]
            }
            """;

        using var session = JsonTestHelper.Open(new ScalarComponent { Score = 0 });
        var act = () => session.LoadJson(jsonWithUnknownPath);

        act.Should().NotThrow();
        var result = (ScalarComponent)session.Commit();
        result.Score.Should().Be(42);
    }
}
