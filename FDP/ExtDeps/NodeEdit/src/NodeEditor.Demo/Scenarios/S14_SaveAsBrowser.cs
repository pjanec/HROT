using ImGuiNET;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using NodeEditor.UI.Dialogs;
using NodeEditor.UI.Picker;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S14: Generic Save-As browser dialog with folder tree, contents, new folder creation, and overwrite confirmation.</summary>
public sealed class S14_SaveAsBrowser : Scenario
{
    public override string Name        => "14 - SaveAs Browser";
    public override string Description => "Click 'Open SaveAs Browser' to test the generic SaveAsBrowserDialog with folder browsing, content items, new folder creation, and overwrite confirmation.";

    private readonly DemoIconProvider _iconProvider = new();
    private SaveAsBrowserDialog? _dialog;
    private string _lastResult = "(none)";

    // Mutable folder set so OnCreateFolder can mutate it and GetFolderTree re-evaluates.
    private readonly HashSet<string> _createdFolders = new() { "AI", "AI/Combat", "Patrol", "Shared" };

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Math.Add",      new Vector2(200, 200));
        AddNode(graph, catalog, "Math.Multiply", new Vector2(450, 200));
    }

    public override void DrawOverlay(IEditorHostServices host)
    {
        if (ImGui.SmallButton("Open SaveAs Browser"))
        {
            var dialog = new SaveAsBrowserDialog();
            _dialog = dialog;

            var request = new SaveAsRequest
            {
                Title = "Save Blueprint As",
                InitialName = "NewBlueprint",
                InitialDestination = "AI",
                ConfirmLabel = "Save",
                GetFolderTree = BuildFolderTree,
                GetFolderContents = GetContents,
                OnCreateFolder = CreateFolder,
                NameExists = CheckNameExists,
                ValidateName = ValidateName,
            };

            dialog.Open(request, result =>
            {
                _lastResult = result.Confirmed
                    ? $"Saved: {result.DestinationPath}/{result.Name} (Overwrite: {result.Overwrite})"
                    : "Cancelled";

                if (host is FakeHostServices fakeHost)
                {
                    fakeHost.ToastQueue_.Enqueue(new EditorNotification(
                        Id: Guid.NewGuid().ToString(),
                        Severity: result.Confirmed ? NotificationSeverity.Success : NotificationSeverity.Info,
                        Title: result.Confirmed ? "Saved" : "Cancelled",
                        Body: _lastResult,
                        AutoDismiss: TimeSpan.FromSeconds(3),
                        Actions: null));
                }
            });
        }

        // Render the dialog every frame if open.
        _dialog?.DrawFrame(_iconProvider);
    }

    // ── request callbacks ──────────────────────────────────────────────────

    private CategoryNode BuildFolderTree()
    {
        // Build a tree from the current folder set.
        // Root → {AI → {Combat}, Patrol, Shared} plus any created sub-folders.
        var root = new CategoryNode("root", new List<CategoryNode>());

        var topLevel = new List<CategoryNode>();
        var existing = _createdFolders.ToHashSet();

        // Group by top-level folder
        var topNames = existing
            .Where(p => !p.Contains('/'))
            .OrderBy(n => n)
            .ToList();

        foreach (var top in topNames)
        {
            var children = existing
                .Where(p => p.StartsWith(top + "/"))
                .Select(p => p.Substring(top.Length + 1))
                .Where(n => !n.Contains('/'))
                .OrderBy(n => n)
                .Select(n => new CategoryNode(n, Array.Empty<CategoryNode>()))
                .ToList();

            topLevel.Add(new CategoryNode(top, children));
        }

        return new CategoryNode("root", topLevel);
    }

    private IReadOnlyList<SaveAsContentItem> GetContents(string folderPath)
    {
        // Return fake content items based on the folder.
        return folderPath switch
        {
            "AI" => new SaveAsContentItem[]
            {
                new("BT_Task", "asset/blueprint"),
                new("BT_Sequence", "asset/blueprint"),
            },
            "AI/Combat" => new SaveAsContentItem[]
            {
                new("BP_Enemy", "asset/blueprint"),
                new("BP_CombatController", "asset/blueprint"),
            },
            "Patrol" => new SaveAsContentItem[]
            {
                new("BT_PatrolPath", "asset/btree"),
            },
            "Shared" => new SaveAsContentItem[]
            {
                new("BP_SharedUtils", "asset/blueprint"),
                new("HSM_CommonState", "asset/hsm"),
            },
            _ => Array.Empty<SaveAsContentItem>(),
        };
    }

    private void CreateFolder(string parentPath, string name)
    {
        string fullPath = string.IsNullOrEmpty(parentPath) ? name : parentPath + "/" + name;
        _createdFolders.Add(fullPath);
    }

    private bool CheckNameExists(string name, string destPath)
    {
        // "BP_Enemy" always "exists" at "AI/Combat" to exercise overwrite confirmation.
        return name == "BP_Enemy" && destPath == "AI/Combat";
    }

    private static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name cannot be empty.";
        if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            return "Name contains invalid characters.";
        return null;
    }

    // ── icon provider ──────────────────────────────────────────────────────

    /// <summary>
    /// Demo icon provider with distinct fake IconHandles for folder keys
    /// and content-item icon keys. Follows S13's pattern.
    /// </summary>
    private sealed class DemoIconProvider : IIconProvider
    {
        private static readonly Dictionary<string, IconHandle> _icons = new()
        {
            ["folder"]           = new IconHandle(5, 16, 16, new Vector2(0.0f, 0.25f), new Vector2(0.25f, 0.5f)),
            ["folder_open"]      = new IconHandle(6, 16, 16, new Vector2(0.25f, 0.25f), new Vector2(0.5f, 0.5f)),
            ["asset/blueprint"]  = new IconHandle(1, 16, 16, new Vector2(0.0f, 0.0f), new Vector2(0.25f, 0.25f)),
            ["asset/btree"]      = new IconHandle(3, 16, 16, new Vector2(0.5f, 0.0f), new Vector2(0.75f, 0.25f)),
            ["asset/hsm"]        = new IconHandle(2, 16, 16, new Vector2(0.25f, 0.0f), new Vector2(0.5f, 0.25f)),
        };

        public bool TryGet(string key, out IconHandle handle)
        {
            return _icons.TryGetValue(key, out handle);
        }
    }
}
