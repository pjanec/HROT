using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.UiFrameRail;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard.Frame;

/// <summary>
/// ⭐ <c>CE-1061</c> (U4, design §13.10b) — the BTree / HSM variables panel in a REAL frame for a DTO-named asset: the params DTO chooser
/// above the table, the reserved <c>Params</c> row and its field rows under it. ⛔ Not a screenshot diff (R-124): the rail proves the panel
/// draws without an ImGui error, and the screenshot is EVIDENCE for a human (saved to <c>HROT_FRAME_SCREENSHOTS</c>, else the temp folder).
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheParamsPanelRendersTests
{
    public struct RenderedParams
    {
        public float Radius;
        public int Rounds;
        public bool UseCover;
    }

    private sealed class ParamsAsset : FakeBlackboardAsset, IParamsDtoAsset
    {
        public string? ParamsDtoTypeId { get; set; }
        public void SetParamsDto(string typeId) => ParamsDtoTypeId = typeId;
    }

    private sealed class NoRefactor : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string targetKey) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid hostAssetId) => Array.Empty<AssetReferenceInfo>();
        public RefactorPreview PreviewRename(string fromKey, string toKey, RefactorOptions options) =>
            new(fromKey, toKey, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyRename(RefactorPreview preview) => new(true, Array.Empty<string>(), null);
        public DeletePreview PreviewDelete(Guid assetId, DeleteOptions options) =>
            new(assetId, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview preview) => new(true, Array.Empty<string>(), null);
        public Task<RefactorPreview> PreviewRenameAsync(string fromKey, string toKey, RefactorOptions options, CancellationToken ct = default) =>
            Task.FromResult(PreviewRename(fromKey, toKey, options));
        public Task<RefactorResult> ApplyRenameAsync(RefactorPreview preview, CancellationToken ct = default) => Task.FromResult(ApplyRename(preview));
    }

    [SkippableFact]
    public void CE1061_TheVariablesPanel_DrawsTheChooser_AndTheParamsFields()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        var asset = new ParamsAsset { ParamsDtoTypeId = typeof(RenderedParams).FullName };
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("Params", typeof(RenderedParams), null, DefaultValueJson: "{\"Rounds\":3}", IsReservedParams: true),
            new BlackboardVariableEntry("Counter", typeof(int), "a plain variable"),
        });
        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);
        var control = new VariablesPanelControl(new NoRefactor(), asset, vm.KnownTypeNames);
        var section = new VariablesPanelSection("Variables", "ce1061", new BTreeHsmSchemaSource(asset, vm, isReadOnly: false),
            vm.TotalInlineBytes, vm.InlineBudget, vm.Warning, AliasingEnabled: false);

        using var frame = UiFrameHarness.Begin();
        frame.StepN(4, () =>
        {
            ImGuiNET.ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.SetNextWindowSize(new System.Numerics.Vector2(620, 330), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.Begin("Variables (CE-1061)");
            control.DrawSingle(section);
            ImGuiNET.ImGui.End();
        });
        var dir = Environment.GetEnvironmentVariable("HROT_FRAME_SCREENSHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");
        frame.Screenshot(Path.Combine(dir, "ce1061-variables-panel.png"));
        Assert.True(frame.FramesRendered >= 4);
    }
}
