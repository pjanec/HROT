using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;

namespace Fdp.Core.Tests.Serialization.Migrations;

// ---------------------------------------------------------------------------
// Normal migrators used to build up the standard three-version schema.
// "Test.Doc" v1 -> v2: add "kind":"default" to every item.
// "Test.Doc" v2 -> v3: add "metadata":{} to every item.
// Reverse migrators peel back those additions.
// ---------------------------------------------------------------------------

internal sealed class TestDocV1ToV2 : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Apply(JsonObject root, MigrationContext ctx)
    {
        if (root["items"] is not JsonArray items) return;
        foreach (var item in items)
        {
            if (item is JsonObject obj)
                obj["kind"] = "default";
        }
    }
}

internal sealed class TestDocV2ToV1 : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 2;
    public int ToVersion => 1;

    public void Apply(JsonObject root, MigrationContext ctx)
    {
        if (root["items"] is not JsonArray items) return;
        foreach (var item in items)
        {
            if (item is JsonObject obj)
                obj.Remove("kind");
        }
    }
}

internal sealed class TestDocV2ToV3 : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 2;
    public int ToVersion => 3;

    public void Apply(JsonObject root, MigrationContext ctx)
    {
        if (root["items"] is not JsonArray items) return;
        foreach (var item in items)
        {
            if (item is JsonObject obj)
                obj["metadata"] = new JsonObject();
        }
    }
}

internal sealed class TestDocV3ToV2 : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 3;
    public int ToVersion => 2;

    public void Apply(JsonObject root, MigrationContext ctx)
    {
        if (root["items"] is not JsonArray items) return;
        foreach (var item in items)
        {
            if (item is JsonObject obj)
                obj.Remove("metadata");
        }
    }
}

// ---------------------------------------------------------------------------
// Invariant-violating migrators used in negative pipeline tests.
// ---------------------------------------------------------------------------

// Violates invariant 2: changes $meta.docType.
internal sealed class TestDocV1ToV2_ChangesDocType : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Apply(JsonObject root, MigrationContext ctx)
        => root["$meta"]!.AsObject()["docType"] = "Test.OtherDoc";
}

// Violates invariant 1: replaces $meta object entirely.
internal sealed class TestDocV1ToV2_ReplacesMetaObject : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Apply(JsonObject root, MigrationContext ctx)
        => root["$meta"] = new JsonObject();
}

// Violates invariant 3: changes $meta.schemaVersion.
internal sealed class TestDocV1ToV2_ChangesSchemaVersion : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Apply(JsonObject root, MigrationContext ctx)
        => root["$meta"]!.AsObject()["schemaVersion"] = 99;
}

// Throws a non-MigrationException to test wrapping behavior.
internal sealed class TestDocV1ToV2_Throws : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Apply(JsonObject root, MigrationContext ctx)
        => throw new ArgumentException("Something went wrong in migrator");
}

// Adds a warning via ctx.AddWarning.
internal sealed class TestDocV1ToV2_WithWarning : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Apply(JsonObject root, MigrationContext ctx)
        => ctx.AddWarning("test warning");
}

// Adds a note via ctx.AddNote.
internal sealed class TestDocV1ToV2_WithNote : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Apply(JsonObject root, MigrationContext ctx)
        => ctx.AddNote("test note");
}

// Adds a warning inside a WithItem scope to test path capture.
internal sealed class TestDocV1ToV2_WarningWithPath : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Apply(JsonObject root, MigrationContext ctx)
    {
        using (ctx.WithItem("items"))
        {
            ctx.AddWarning("test");
        }
    }
}

// Throws MigrationException at step v2->v3 to test pipeline halt behavior.
internal sealed class ThrowingMigratorV2ToV3 : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 2;
    public int ToVersion => 3;

    public void Apply(JsonObject root, MigrationContext ctx)
        => throw new MigrationException("Simulated failure at step v2->v3");
}

// Violates invariant 4: changes $meta.engineVersion.
internal sealed class TestDocV1ToV2_ChangesEngineVersion : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Apply(JsonObject root, MigrationContext ctx)
        => root["$meta"]!.AsObject()["engineVersion"] = "tampered";
}

// Violates invariant 4: changes $meta.createdBy.
internal sealed class TestDocV1ToV2_ChangesCreatedBy : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Apply(JsonObject root, MigrationContext ctx)
        => root["$meta"]!.AsObject()["createdBy"] = "tampered";
}
