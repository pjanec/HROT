using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario.Migrations.Helpers;

namespace Hrot.Common.Tests.Scenario.Migrations;

/// <summary>
/// Unit tests for <see cref="EntityPatch"/> helper methods (D-024).
/// Covers OnEachEntity, AddField, RemoveField, RenameField, RenameComponent, OnComponent.
/// </summary>
public sealed class EntityPatchTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal scenario root. When entityInfo is null the entity
    /// receives only a SimTransform component.
    /// </summary>
    private static JsonObject MakeScenarioRoot(string entityId, JsonObject? entityInfo = null)
    {
        var entity = new JsonObject();
        if (entityInfo != null)
            entity["EntityInfo"] = entityInfo;
        else
            entity["SimTransform"] = new JsonObject { ["X"] = 0, ["Y"] = 0 };

        return new JsonObject
        {
            ["$meta"] = new JsonObject { ["docType"] = "Hrot.Scenario", ["schemaVersion"] = 1 },
            ["entities"] = new JsonObject { [entityId] = entity }
        };
    }

    private static JsonObject MakeEntityInfo(string name = "Alpha", string forceId = "Friend") =>
        new JsonObject { ["Name"] = name, ["ForceId"] = forceId };

    // ── Group 1: OnEachEntity ─────────────────────────────────────────────

    // T_EP_01
    [Fact]
    public void OnEachEntity_EntitiesHaveEntityInfo_CallbackCalledForEach()
    {
        var entitiesObj = new JsonObject
        {
            ["e1"] = new JsonObject { ["EntityInfo"] = MakeEntityInfo("A") },
            ["e2"] = new JsonObject { ["EntityInfo"] = MakeEntityInfo("B") }
        };
        var root = new JsonObject
        {
            ["$meta"] = new JsonObject { ["docType"] = "Hrot.Scenario", ["schemaVersion"] = 1 },
            ["entities"] = entitiesObj
        };

        int counter = 0;
        EntityPatch.OnEachEntity(root, (id, entity) => counter++);

        Assert.Equal(2, counter);
    }

    // T_EP_02
    [Fact]
    public void OnEachEntity_EntityMissingEntityInfo_CallbackNotCalled()
    {
        // Entity has only SimTransform; callback only counts entities WITH EntityInfo.
        var root = MakeScenarioRoot("entity-1"); // no entityInfo -> SimTransform

        int counter = 0;
        EntityPatch.OnEachEntity(root, (id, entity) =>
        {
            if (entity["EntityInfo"] is not JsonObject)
                return;
            counter++;
        });

        Assert.Equal(0, counter);
    }

    // ── Group 2: AddField ─────────────────────────────────────────────────

    // T_EP_03
    [Fact]
    public void AddField_FieldAbsent_AddsWithClonedDefault()
    {
        var root = MakeScenarioRoot("e1", MakeEntityInfo());

        EntityPatch.AddField(root, "EntityInfo", "Tags", new JsonArray(), CasingPolicy.ForcePascal);

        var tags = root["entities"]!["e1"]!["EntityInfo"]!["Tags"];
        Assert.NotNull(tags);
        Assert.IsType<JsonArray>(tags);
        Assert.Equal(0, tags.AsArray().Count);
    }

    // T_EP_04
    [Fact]
    public void AddField_FieldAlreadyPresent_IsIdempotent()
    {
        var info = MakeEntityInfo();
        info["Tags"] = new JsonArray(JsonValue.Create(1)!);
        var root = MakeScenarioRoot("e1", info);

        EntityPatch.AddField(root, "EntityInfo", "Tags", new JsonArray(), CasingPolicy.ForcePascal);

        var tags = root["entities"]!["e1"]!["EntityInfo"]!["Tags"]!.AsArray();
        Assert.Equal(1, tags.Count);
        Assert.Equal(1, tags[0]!.GetValue<int>());
    }

    // T_EP_05
    [Fact]
    public void AddField_TwoEntitiesWithSharedDefault_DeepClonesDefault()
    {
        var sharedDefault = new JsonArray();
        var entitiesObj = new JsonObject
        {
            ["e1"] = new JsonObject { ["EntityInfo"] = MakeEntityInfo("A") },
            ["e2"] = new JsonObject { ["EntityInfo"] = MakeEntityInfo("B") }
        };
        var root = new JsonObject
        {
            ["$meta"] = new JsonObject { ["docType"] = "Hrot.Scenario", ["schemaVersion"] = 1 },
            ["entities"] = entitiesObj
        };

        EntityPatch.AddField(root, "EntityInfo", "Tags", sharedDefault, CasingPolicy.ForcePascal);

        // Modify entity 1's Tags array
        root["entities"]!["e1"]!["EntityInfo"]!["Tags"]!.AsArray().Add(JsonValue.Create("injected")!);

        // Entity 2's Tags array must be independent (deep clone)
        var tags2 = root["entities"]!["e2"]!["EntityInfo"]!["Tags"]!.AsArray();
        Assert.Equal(0, tags2.Count);
    }

    // ── Group 3: RemoveField ──────────────────────────────────────────────

    // T_EP_06
    [Fact]
    public void RemoveField_FieldPresent_RemovesIt()
    {
        var info = MakeEntityInfo();
        info["Tags"] = new JsonArray();
        var root = MakeScenarioRoot("e1", info);

        EntityPatch.RemoveField(root, "EntityInfo", "Tags");

        var entityInfo = root["entities"]!["e1"]!["EntityInfo"]!.AsObject();
        Assert.False(entityInfo.ContainsKey("Tags"));
    }

    // T_EP_07
    [Fact]
    public void RemoveField_FieldAbsent_IsIdempotent()
    {
        var info = MakeEntityInfo(); // has only Name and ForceId
        var root = MakeScenarioRoot("e1", info);
        int originalCount = root["entities"]!["e1"]!["EntityInfo"]!.AsObject().Count;

        EntityPatch.RemoveField(root, "EntityInfo", "Tags");

        var entityInfo = root["entities"]!["e1"]!["EntityInfo"]!.AsObject();
        Assert.Equal(originalCount, entityInfo.Count);
    }

    // ── Group 4: RenameField ──────────────────────────────────────────────

    // T_EP_08
    [Fact]
    public void RenameField_FieldPresent_RenamesIt()
    {
        var info = MakeEntityInfo("Alpha");
        var root = MakeScenarioRoot("e1", info);

        EntityPatch.RenameField(root, "EntityInfo", "Name", "DisplayName", CasingPolicy.ForcePascal);

        var entityInfo = root["entities"]!["e1"]!["EntityInfo"]!.AsObject();
        Assert.True(entityInfo.ContainsKey("DisplayName"));
        Assert.Equal("Alpha", entityInfo["DisplayName"]!.GetValue<string>());
        Assert.False(entityInfo.ContainsKey("Name"));
    }

    // T_EP_09
    [Fact]
    public void RenameField_FieldAbsent_IsNoOp()
    {
        var info = new JsonObject { ["ForceId"] = "Friend" };
        var root = MakeScenarioRoot("e1", info);
        int originalCount = root["entities"]!["e1"]!["EntityInfo"]!.AsObject().Count;

        EntityPatch.RenameField(root, "EntityInfo", "OldName", "NewName", CasingPolicy.ForcePascal);

        var entityInfo = root["entities"]!["e1"]!["EntityInfo"]!.AsObject();
        Assert.Equal(originalCount, entityInfo.Count);
    }

    // ── Group 5: RenameComponent ──────────────────────────────────────────

    // T_EP_10
    [Fact]
    public void RenameComponent_ComponentPresent_RenamesIt()
    {
        var info = new JsonObject { ["Name"] = "A" };
        var root = MakeScenarioRoot("e1", info);

        EntityPatch.RenameComponent(root, "EntityInfo", "Info");

        var entity = root["entities"]!["e1"]!.AsObject();
        Assert.True(entity.ContainsKey("Info"));
        Assert.False(entity.ContainsKey("EntityInfo"));
        Assert.Equal("A", entity["Info"]!["Name"]!.GetValue<string>());
    }

    // T_EP_11
    [Fact]
    public void RenameComponent_BothNamesPresent_ThrowsMigrationException()
    {
        var entity = new JsonObject
        {
            ["EntityInfo"] = new JsonObject { ["Name"] = "A" },
            ["Info"] = new JsonObject { ["Name"] = "B" }
        };
        var root = new JsonObject
        {
            ["$meta"] = new JsonObject { ["docType"] = "Hrot.Scenario", ["schemaVersion"] = 1 },
            ["entities"] = new JsonObject { ["e1"] = entity }
        };

        Assert.Throws<MigrationException>(() =>
            EntityPatch.RenameComponent(root, "EntityInfo", "Info"));
    }

    // ── Group 6: OnComponent ─────────────────────────────────────────────

    // T_EP_12
    [Fact]
    public void OnComponent_EntityHasComponent_CallbackCalled()
    {
        var entitiesObj = new JsonObject
        {
            ["e-with-info"]    = new JsonObject { ["EntityInfo"] = MakeEntityInfo("X") },
            ["e-without-info"] = new JsonObject { ["SimTransform"] = new JsonObject { ["X"] = 0, ["Y"] = 0 } }
        };
        var root = new JsonObject
        {
            ["$meta"] = new JsonObject { ["docType"] = "Hrot.Scenario", ["schemaVersion"] = 1 },
            ["entities"] = entitiesObj
        };

        int counter = 0;
        EntityPatch.OnComponent(root, "EntityInfo", (id, component) => counter++);

        Assert.Equal(1, counter);
    }

    // ── Group 7: AddField null guard (D-026) ─────────────────────────────

    // T_EP_13
    [Fact]
    public void AddField_NullDefaultValue_ThrowsArgumentNullException()
    {
        var root = MakeScenarioRoot("e1", MakeEntityInfo());
        Assert.Throws<ArgumentNullException>(() =>
            EntityPatch.AddField(root, "EntityInfo", "NewField", (JsonNode)null!, CasingPolicy.ForcePascal));
    }

    // ── Group 8: TransformComponent (D-027) ──────────────────────────────

    // T_EP_14
    [Fact]
    public void TransformComponent_EntityHasComponent_TransformApplied()
    {
        var info = MakeEntityInfo("Alpha", "Friend");
        var root = MakeScenarioRoot("e1", info);

        EntityPatch.TransformComponent(root, "EntityInfo", (entity, component) =>
        {
            component["Rank"] = "Colonel";
        });

        var rank = root["entities"]!["e1"]!["EntityInfo"]!["Rank"]!.GetValue<string>();
        Assert.Equal("Colonel", rank);
    }

    // T_EP_15
    [Fact]
    public void TransformComponent_EntityLacksComponent_NothingHappens()
    {
        // Entity has SimTransform, not EntityInfo.
        var root = MakeScenarioRoot("e1"); // no entityInfo param -> SimTransform only

        int callCount = 0;
        EntityPatch.TransformComponent(root, "EntityInfo", (entity, component) =>
        {
            callCount++;
        });

        Assert.Equal(0, callCount);
    }

    // T_EP_16
    [Fact]
    public void TransformComponent_TransformAddsComponentToEntity_SiblingVisible()
    {
        var info = MakeEntityInfo();
        var root = MakeScenarioRoot("e1", info);

        EntityPatch.TransformComponent(root, "EntityInfo", (entity, component) =>
        {
            // The transform may add sibling components.
            entity["NewComp"] = new JsonObject { ["Value"] = 42 };
        });

        var newComp = root["entities"]!["e1"]!["NewComp"]!["Value"]!.GetValue<int>();
        Assert.Equal(42, newComp);
    }

    // ── Group 9: InferCasing via AddField/MatchExisting (D-028) ──────────

    // T_EP_17
    [Fact]
    public void AddField_MatchExisting_AllPascalFields_NewFieldIsPascal()
    {
        // EntityInfo has Name (Pascal) and ForceId (Pascal) -> majority Pascal.
        var root = MakeScenarioRoot("e1", MakeEntityInfo());

        EntityPatch.AddField(root, "EntityInfo", "tags", new JsonArray(), CasingPolicy.MatchExisting);

        var component = root["entities"]!["e1"]!["EntityInfo"]!.AsObject();
        Assert.True(component.ContainsKey("Tags"), "Expected 'Tags' (Pascal), got camel or absent.");
        Assert.False(component.ContainsKey("tags"), "Should not have lowercase 'tags'.");
    }

    // T_EP_18
    [Fact]
    public void AddField_MatchExisting_AllCamelFields_NewFieldIsCamel()
    {
        // Build a component with only camelCase fields.
        var camelComp = new JsonObject { ["name"] = "Alpha", ["forceId"] = "Friend" };
        var root = new JsonObject
        {
            ["$meta"] = new JsonObject { ["docType"] = "Hrot.Scenario", ["schemaVersion"] = 1 },
            ["entities"] = new JsonObject
            {
                ["e1"] = new JsonObject { ["CamelComp"] = camelComp }
            }
        };

        EntityPatch.AddField(root, "CamelComp", "Tags", new JsonArray(), CasingPolicy.MatchExisting);

        var component = root["entities"]!["e1"]!["CamelComp"]!.AsObject();
        Assert.True(component.ContainsKey("tags"), "Expected 'tags' (camel), got pascal or absent.");
        Assert.False(component.ContainsKey("Tags"), "Should not have Pascal 'Tags'.");
    }

    // T_EP_19
    [Fact]
    public void AddField_MatchExisting_EmptyComponent_DefaultsToPascal()
    {
        // Empty component -> tie (0 Pascal vs 0 Camel) -> PascalCase wins.
        var emptyComp = new JsonObject();
        var root = new JsonObject
        {
            ["$meta"] = new JsonObject { ["docType"] = "Hrot.Scenario", ["schemaVersion"] = 1 },
            ["entities"] = new JsonObject
            {
                ["e1"] = new JsonObject { ["EmptyComp"] = emptyComp }
            }
        };

        EntityPatch.AddField(root, "EmptyComp", "field", JsonValue.Create(1)!, CasingPolicy.MatchExisting);

        var component = root["entities"]!["e1"]!["EmptyComp"]!.AsObject();
        Assert.True(component.ContainsKey("Field"), "Expected 'Field' (Pascal default for tie), got camel or absent.");
    }

    // T_EP_20
    [Fact]
    public void AddField_MatchExisting_EqualPascalAndCamel_PascalWinsTie()
    {
        // 2 Pascal, 2 Camel -> tie -> Pascal wins.
        var mixedComp = new JsonObject
        {
            ["Name"] = "A",
            ["ForceId"] = "B",
            ["color"] = "red",
            ["weight"] = JsonValue.Create(10)
        };
        var root = new JsonObject
        {
            ["$meta"] = new JsonObject { ["docType"] = "Hrot.Scenario", ["schemaVersion"] = 1 },
            ["entities"] = new JsonObject
            {
                ["e1"] = new JsonObject { ["MixedComp"] = mixedComp }
            }
        };

        EntityPatch.AddField(root, "MixedComp", "newField", JsonValue.Create("v")!, CasingPolicy.MatchExisting);

        var component = root["entities"]!["e1"]!["MixedComp"]!.AsObject();
        Assert.True(component.ContainsKey("NewField"), "Expected 'NewField' (Pascal wins tie), got camel or absent.");
    }
}
