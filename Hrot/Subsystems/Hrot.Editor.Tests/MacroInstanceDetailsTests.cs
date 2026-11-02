using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.Scenario;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>⭐ CE-4210 MA-8 (SR-39a) — the macro instance's args model: rows from the macro's header, a default versus an override, an edit that builds a NEW record carrying the merged ArgsJson, and where it may be written.</summary>
public sealed class MacroInstanceDetailsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "macro-args-" + Guid.NewGuid().ToString("N"));

    public MacroInstanceDetailsTests()
    {
        var header = new ScenarioHeader("Hrot.Scenario")
        {
            Kind = ScenarioHeader.MacroKind,
            Params = new[]
            {
                new ScenarioParamDecl("side", "ForceId", JsonValue.Create("Hostile"), "which force"),
                new ScenarioParamDecl("speed", "float", JsonValue.Create(2.5f)),
                new ScenarioParamDecl("mystery", "Nonsense"),
            },
        };
        var dir = Path.Combine(_root, "test", "fire-team");
        Directory.CreateDirectory(dir);
        var header_ = new JsonObject();
        ScenarioHeaderJson.Write(header_, header);
        File.WriteAllText(Path.Combine(dir, "scenario.json"), new JsonObject { ["Header"] = header_ }.ToJsonString());
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private ScenarioHeader Header() => MacroArgs.ReadHeader(_root, "test/fire-team").Header!;

    [Fact]
    public void TheRows_AreTheHeadersParams_InOrder_WithTheirTypesAndDefaults()
    {
        var rows = MacroArgs.Rows(Header(), "{}");
        Assert.Equal(new[] { "side", "speed", "mystery" }, rows.Select(r => r.Name));
        Assert.Equal(typeof(ForceId), rows[0].Type);
        Assert.Equal("\"Hostile\"", rows[0].DefaultJson);
        Assert.Equal("which force", rows[0].Comment);
        Assert.Equal(typeof(float), rows[1].Type);
        Assert.Null(rows[2].Type);                                    // an unknown type id: the view says so, it does not guess
        Assert.All(rows, r => Assert.False(r.IsOverridden));
        Assert.Equal("\"Hostile\"", rows[0].EffectiveJson);
    }

    [Fact]
    public void AnArg_OverridesItsRow_AndTheOthersStayDefault()
    {
        var rows = MacroArgs.Rows(Header(), "{\"side\":\"Friend\"}");
        Assert.True(rows[0].IsOverridden);
        Assert.Equal("\"Friend\"", rows[0].EffectiveJson);
        Assert.False(rows[1].IsOverridden);
        Assert.Equal("2.5", rows[1].EffectiveJson);
    }

    [Fact]
    public void AnEdit_BuildsANewRecord_CarryingTheMergedArgs_AndTheOldOneIsUntouched()
    {
        var before = new MacroInstance { Macro = "test/fire-team", ArgsJson = "{\"speed\":4}" };
        var after = MacroArgs.With(before, "side", "\"Friend\"", "\"Hostile\"");

        Assert.NotSame(before, after);
        Assert.Equal("{\"speed\":4}", before.ArgsJson);
        var args = JsonNode.Parse(after.ArgsJson)!.AsObject();
        Assert.Equal("Friend", (string?)args["side"]);
        Assert.Equal(4, (int?)args["speed"]);                         // key by key: what was set before survives
        Assert.Equal("test/fire-team", after.Macro);
    }

    [Fact]
    public void ADefaultValue_IsNoOverride_AndResetRemovesTheKey()
    {
        var withSide = new MacroInstance { Macro = "m", ArgsJson = "{\"side\":\"Friend\",\"speed\":4}" };
        Assert.Equal("{\"speed\":4}", MacroArgs.With(withSide, "side", "\"Hostile\"", "\"Hostile\"").ArgsJson);   // typed the default back
        Assert.Equal("{\"speed\":4}", MacroArgs.Reset(withSide, "side").ArgsJson);
    }

    [Fact]
    public void ChangingTheMacro_StartsWithNoArgs() =>
        Assert.Equal(("other/macro", "{}"), (MacroArgs.Retarget(new MacroInstance { Macro = "a", ArgsJson = "{\"x\":1}" }, "other/macro") is var r ? (r.Macro, r.ArgsJson) : default));

    [Fact]
    public void AMissingOrBrokenMacro_SaysWhy_NotAnEmptyForm()
    {
        Assert.Contains("not on the server", MacroArgs.ReadHeader(_root, "no/such").Problem);
        Assert.Contains("No macro", MacroArgs.ReadHeader(_root, "").Problem);
        var bad = Path.Combine(_root, "bad");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, "scenario.json"), "{ nope");
        Assert.Contains("could not be read", MacroArgs.ReadHeader(_root, "bad").Problem);
    }

    // ── where it may be written ────────────────────────────────────────────────────────────────

    private sealed class Foreign : IInspectableSession
    {
        public bool Owned; public MacroInstance? Held = new() { Macro = "m" };
        public bool IsReadOnly => false; public int EntityCount => 1;
        public IEnumerable<Entity> GetEntities() => Array.Empty<Entity>();
        public bool IsAlive(Entity e) => true;
        public bool HasComponent(Entity e, Type t) => t == typeof(MacroInstance);
        public object? GetComponent(Entity e, Type t) => Held;
        public void SetComponent(Entity e, Type t, object d) => Held = (MacroInstance)d;
        public IEnumerable<Type> GetAllComponentTypes() => Array.Empty<Type>();
        public bool HasAuthority(Entity e, Type t) => Owned;
    }

    [Fact]
    public void TheWrite_GoesOnlyWhereThisNodeOwnsTheInstance()
    {
        var s = new Foreign { Owned = false };
        var next = new MacroInstance { Macro = "m", ArgsJson = "{\"a\":1}" };
        Assert.Equal(MacroArgs.WriteResult.NotOwned, MacroArgs.Write(s, default, next));
        Assert.Equal("{}", s.Held!.ArgsJson);                          // nothing was written to a foreign instance
        s.Owned = true;
        Assert.Equal(MacroArgs.WriteResult.Written, MacroArgs.Write(s, default, next));
        Assert.Same(next, s.Held);
    }

    /// <summary>
    /// MEASURED (`CE-4210 MA-8`): a managed component of an entity is written through the inspector's session only where this node holds the component's authority bit
    /// (<c>EntityRepository.HasAuthority(e, typeId)</c>); a fresh local entity has none until the spawn path grants it, so an unowned instance is refused, not overwritten.
    /// </summary>
    [Fact]
    public void TheWrite_ToARealWorld_NeedsTheComponentsAuthorityBit_ThenReplacesTheManagedRecord()
    {
        using var repo = new EntityRepository();
        repo.RegisterManagedComponent<MacroInstance>();
        var e = repo.CreateEntity();
        var s = new RepositoryAdapter(repo);
        var first = new MacroInstance { Macro = "test/fire-team" };
        s.SetComponent(e, typeof(MacroInstance), first);
        var next = MacroArgs.With(first, "side", "\"Friend\"", "\"Hostile\"");

        Assert.Equal(MacroArgs.WriteResult.NotOwned, MacroArgs.Write(s, e, next));
        Assert.Same(first, s.GetComponent(e, typeof(MacroInstance)));             // refused: the record is untouched

        repo.SetAuthority(e, ComponentTypeRegistry.GetId(typeof(MacroInstance)), true);
        Assert.Equal(MacroArgs.WriteResult.Written, MacroArgs.Write(s, e, next));
        Assert.Same(next, s.GetComponent(e, typeof(MacroInstance)));
    }

    [Fact]
    public void TheStatusLine_NamesTheExpansionState()
    {
        using var repo = new EntityRepository();
        repo.RegisterComponent<MacroExpansionStatus>();
        var e = repo.CreateEntity();
        Assert.Equal("Not expanded", MacroInstanceDetailsView.StatusLine(repo, e));
        repo.SetComponent(e, new MacroExpansionStatus { State = MacroExpansionState.Done });
        Assert.Equal("Done", MacroInstanceDetailsView.StatusLine(repo, e));
        repo.SetComponent(e, new MacroExpansionStatus { State = MacroExpansionState.Failed, Reason = new FixedString128("no spot for 'Rifleman A'") });
        Assert.Equal("Failed: no spot for 'Rifleman A'", MacroInstanceDetailsView.StatusLine(repo, e));
    }
}
