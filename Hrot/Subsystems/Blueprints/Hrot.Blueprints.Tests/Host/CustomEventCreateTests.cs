using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Action;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-12c — declaring a custom event from the My Blueprint panel.
///
/// <para>
/// <c>BlueprintMyBlueprintModel</c> has always declared <c>editor.create-custom-event</c> for its
/// "Custom Events" section, and <c>BuildCustomEventItems</c> has always projected
/// <c>asset.CustomEvents</c> — but nothing ever registered the command, so the "+" was inert and the
/// list could only ever be empty. That is also why <b>BP-07</b> shipped unreachable: the
/// <c>CallCustomEvent</c> picker resolves against <c>asset.CustomEvents</c>, and no asset could
/// declare one.
/// </para>
/// </summary>
public sealed class CustomEventCreateTests
{
    private static BlueprintAsset MakeAsset()
        => BlueprintAssetBuilder.Instance("EvtAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { })
            .Build();

    private static IReadOnlyList<(string Name, string TypeId)> Params(
        params (string, string)[] items) => items.ToList();

    /// <summary>Applies edits straight through; this file is about the create path, not undo.</summary>
    private sealed class DirectEditService : IEditService
    {
        public void MarkDirty(BlueprintAsset asset) { }
        public void RecordPropertyEdit(BlueprintAsset asset, string description, Action apply, Action undo)
            => apply();
        public void NotifyStructureChanged(BlueprintAsset asset) { }
    }

    // ── Registration (the bug itself) ─────────────────────────────────────────

    /// <summary>
    /// The bug in one assertion: the command the panel's "+" invokes must exist. BP-12e made an
    /// unregistered command render as a <i>disabled</i> button with a "Not implemented" tooltip, so
    /// before this the Custom Events "+" was visibly dead.
    /// </summary>
    [Fact]
    public void CreateCustomEventCommand_IsRegistered()
    {
        var commands = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterCreateCustomEventCommand(commands, MakeAsset());

        Assert.True(commands.Invoke(NodeEditor.Core.CommandCatalog.CreateCustomEvent).Success);
    }

    [Fact]
    public void CreateCustomEventCommand_Unregistered_ReportsFailure()
    {
        var commands = new EditorCommandsImpl();

        var result = commands.Invoke(NodeEditor.Core.CommandCatalog.CreateCustomEvent);

        Assert.False(result.Success);
    }

    /// <summary>The modal overload defers to the caller instead of creating anything itself.</summary>
    [Fact]
    public void ModalOverload_OpensTheModal_AndDeclaresNothingItself()
    {
        var asset    = MakeAsset();
        var commands = new EditorCommandsImpl();
        int opened   = 0;

        BlueprintDocumentFactory.RegisterCreateCustomEventCommand(commands, () => opened++);
        commands.Invoke(NodeEditor.Core.CommandCatalog.CreateCustomEvent);

        Assert.Equal(1, opened);
        Assert.Empty(asset.CustomEvents);
    }

    /// <summary>
    /// <b>The production construction site, not a stand-in.</b> Trap #1 of this programme: a
    /// registration that only ever runs in a test proves nothing about the shipped editor. This
    /// drives <see cref="BlueprintMyBlueprintWindow.Retarget"/> — the one place the real panel's
    /// commands are wired — and asserts the section "+" is live afterwards.
    /// </summary>
    [Fact]
    public void MyBlueprintWindow_Retarget_WiresBothSectionCreateCommands()
    {
        var window   = new BlueprintMyBlueprintWindow();
        var commands = new EditorCommandsImpl();

        Assert.False(commands.Invoke(NodeEditor.Core.CommandCatalog.CreateCustomEvent).Success);

        window.Retarget(editableAsset: null, MakeAsset(), hostServices: null, commands);

        Assert.True(commands.Invoke(NodeEditor.Core.CommandCatalog.CreateCustomEvent).Success);
        Assert.True(commands.Invoke(NodeEditor.Core.CommandCatalog.CreateVariable).Success);
    }

    // ── Quick-add ─────────────────────────────────────────────────────────────

    [Fact]
    public void QuickAdd_AppendsDeclaration_AndMarksDirty()
    {
        var asset = MakeAsset();
        int dirty = 0;
        var commands = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterCreateCustomEventCommand(commands, asset, () => dirty++);

        commands.Invoke(NodeEditor.Core.CommandCatalog.CreateCustomEvent);

        var decl = Assert.Single(asset.CustomEvents);
        Assert.Equal("NewEvent", decl.Name);
        Assert.NotEqual(Guid.Empty, decl.Id);
        Assert.Empty(decl.Parameters);
        Assert.Equal(1, dirty);
    }

    /// <summary>Repeated clicks must not collide — the same rule the variable quick-add follows.</summary>
    [Fact]
    public void QuickAdd_Repeated_PicksFreeNames()
    {
        var asset = MakeAsset();

        BlueprintDocumentFactory.AddCustomEvent(asset);
        BlueprintDocumentFactory.AddCustomEvent(asset);
        BlueprintDocumentFactory.AddCustomEvent(asset);

        Assert.Equal(new[] { "NewEvent", "NewEvent1", "NewEvent2" },
            asset.CustomEvents.Select(e => e.Name).ToArray());
    }

    // ── Create with parameters ────────────────────────────────────────────────

    [Fact]
    public void Create_WithParameters_DeclaresThemInOrder()
    {
        var asset = MakeAsset();

        var decl = BlueprintDocumentFactory.CreateCustomEvent(
            asset, "OnHit", Params(("Damage", "System.Single"), ("Attacker", "Fdp.Core.Entity")));

        Assert.NotNull(decl);
        Assert.Equal("OnHit", decl!.Name);
        Assert.Equal(new[] { "Damage", "Attacker" }, decl.Parameters.Select(p => p.Name).ToArray());
        Assert.Equal("System.Single",   decl.Parameters[0].Type.TypeId);
        Assert.Equal("Fdp.Core.Entity", decl.Parameters[1].Type.TypeId);
        Assert.All(decl.Parameters, p => Assert.NotEqual(Guid.Empty, p.Id));
    }

    /// <summary>
    /// The whole point of the item: what BP-07's picker can now see. Also proves the round-trip the
    /// drawer needs — it writes and resolves the declaration's GUID, not its name.
    /// </summary>
    [Fact]
    public void Create_MakesTheEventSelectableInTheCallCustomEventPicker()
    {
        var asset = MakeAsset();
        var decl  = BlueprintDocumentFactory.CreateCustomEvent(
            asset, "OnHit", Params(("Damage", "System.Single")))!;

        var node    = new CallCustomEventNode();
        var session = new CallCustomEventNodeSession(node, asset, new DirectEditService());

        Assert.Contains(session.GetAvailableEventsForTest(), e => e.Id == decl.Id);
        Assert.Equal("OnHit (Damage)", CallCustomEventNodeSession.LabelForTest(decl));

        session.SetEventIdForTest(decl.Id.ToString("D"));
        Assert.Equal(decl.Id.ToString("D"), node.EventId);
        Assert.False(session.IsCurrentEventUnresolvedForTest());
    }

    // ── Rejection ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("On Hit")]       // space
    [InlineData("1stEvent")]     // leading digit
    [InlineData("On-Hit")]       // punctuation
    [InlineData("class")]        // C# keyword
    public void Create_RejectsNamesThatCannotBeEmitted(string name)
    {
        var asset = MakeAsset();

        Assert.Null(BlueprintDocumentFactory.CreateCustomEvent(asset, name));
        Assert.Empty(asset.CustomEvents);
    }

    /// <summary>
    /// Not cosmetic: the compiler emits <c>Event_{Name}</c> verbatim, so a name that is not an
    /// identifier is a Roslyn error rather than a validation message.
    /// </summary>
    [Fact]
    public void ValidNames_AreIdentifiers()
    {
        Assert.True(BlueprintDocumentFactory.IsValidDeclarationName("OnHit"));
        Assert.True(BlueprintDocumentFactory.IsValidDeclarationName("_private"));
        Assert.True(BlueprintDocumentFactory.IsValidDeclarationName("Event2"));
        Assert.False(BlueprintDocumentFactory.IsValidDeclarationName("2Event"));
        Assert.False(BlueprintDocumentFactory.IsValidDeclarationName("void"));
    }

    [Fact]
    public void Create_RejectsDuplicateName_CaseInsensitively_AndAddsNothing()
    {
        var asset = MakeAsset();
        BlueprintDocumentFactory.CreateCustomEvent(asset, "OnHit");

        Assert.Null(BlueprintDocumentFactory.CreateCustomEvent(asset, "onhit"));
        Assert.Single(asset.CustomEvents);
        Assert.True(BlueprintDocumentFactory.IsDuplicateCustomEventName(asset, "ONHIT"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("int")]
    public void Create_RejectsBadParameterName_AndAddsNothing(string paramName)
    {
        var asset = MakeAsset();

        Assert.Null(BlueprintDocumentFactory.CreateCustomEvent(
            asset, "OnHit", Params((paramName, "System.Single"))));
        Assert.Empty(asset.CustomEvents);
    }

    /// <summary>
    /// Two same-named parameters would emit a duplicate C# parameter, and the call node's pin
    /// projection would produce two pins with the same name.
    /// </summary>
    [Fact]
    public void Create_RejectsDuplicateParameterNames()
    {
        var asset = MakeAsset();

        Assert.Null(BlueprintDocumentFactory.CreateCustomEvent(
            asset, "OnHit", Params(("Damage", "System.Single"), ("damage", "System.Int32"))));
        Assert.Empty(asset.CustomEvents);
    }

    [Fact]
    public void Create_RejectionDoesNotMarkDirty()
    {
        var asset = MakeAsset();
        int dirty = 0;

        BlueprintDocumentFactory.CreateCustomEvent(asset, "no good", markDirty: () => dirty++);

        Assert.Equal(0, dirty);
    }

    // ── Modal validation (headless) ───────────────────────────────────────────

    [Fact]
    public void Modal_ValidDeclaration_HasNoValidationMessage()
    {
        Assert.Null(CustomEventCreateModal.ValidationMessage(
            MakeAsset(), "OnHit", Params(("Damage", "System.Single"))));
    }

    /// <summary>
    /// The modal must refuse everything <c>CreateCustomEvent</c> refuses — otherwise Confirm is
    /// enabled, the click silently adds nothing, and BP-12e's whole point is undone.
    /// </summary>
    [Theory]
    [InlineData("", "Damage")]
    [InlineData("On Hit", "Damage")]
    [InlineData("OnHit", "")]
    [InlineData("OnHit", "1st")]
    public void Modal_RefusesExactlyWhatTheCreatePathRefuses(string name, string paramName)
    {
        var asset = MakeAsset();
        var parameters = Params((paramName, "System.Single"));

        Assert.NotNull(CustomEventCreateModal.ValidationMessage(asset, name, parameters));
        Assert.Null(BlueprintDocumentFactory.CreateCustomEvent(asset, name, parameters));
    }

    [Fact]
    public void Modal_FlagsDuplicateEventName()
    {
        var asset = MakeAsset();
        BlueprintDocumentFactory.CreateCustomEvent(asset, "OnHit");

        var message = CustomEventCreateModal.ValidationMessage(
            asset, "onhit", Array.Empty<(string, string)>());

        Assert.NotNull(message);
        Assert.Contains("already exists", message);
    }

    [Fact]
    public void Modal_FlagsDuplicateParameterNames()
    {
        var message = CustomEventCreateModal.ValidationMessage(
            MakeAsset(), "OnHit", Params(("Damage", "System.Single"), ("Damage", "System.Int32")));

        Assert.NotNull(message);
        Assert.Contains("Damage", message);
    }
}
