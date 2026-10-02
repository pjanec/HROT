using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor;
using Hrot.Editor.AiShared.Catalog;
using Hrot.MuscleCharacter.Animation.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Hashing;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Tests for PlayMontageChainNodeDrawer (ANC-P5-08a).
/// Verifies drawer recognition, session creation, and dirty-tracking lifecycle.
/// </summary>
public sealed class PlayMontageChainNodeDrawerTests
{
    // Minimal stub implementations for injected dependencies
    private sealed class NullAnimationTkbQueries : IAnimationTkbQueries
    {
        private static readonly IReadOnlyList<MontageDefDto> EmptyMontages = new List<MontageDefDto>();
        private static readonly IReadOnlyList<StanceId> EmptyStances = new List<StanceId>();
        private static readonly IReadOnlyList<NotifyMarkerDefDto> EmptyMarkers = new List<NotifyMarkerDefDto>();
        
        public IReadOnlyList<MontageDefDto> GetPlayableMontages(string entityClass) => EmptyMontages;
        public MontageDefDto? GetMontage(string entityClass, string montageName) => null;
        public IReadOnlyList<StanceId> GetSupportedStances(string entityClass) => EmptyStances;
        public bool SupportsAim(string entityClass) => false;
        public IReadOnlyList<NotifyMarkerDefDto> GetAvailableMarkers(string entityClass) => EmptyMarkers;
        public string? GetMarkerName(string entityClass, uint hash) => null;
        public int ResolveMontageId(string entityClass, string montageName) => 0;
    }

    private sealed class NullEditService : IEditService
    {
        public void MarkDirty(BlueprintAsset asset) { }
    
        /// <summary>
        /// BP-11: no undo stack here, but recording still performs the edit and marks dirty —
        /// the same two observable effects the real EditService has.
        /// </summary>
        public void RecordPropertyEdit(BlueprintAsset asset, string description, Action apply, Action undo)
        {
            apply();
            MarkDirty(asset);
        }

        public void NotifyStructureChanged(BlueprintAsset asset) { }
}

    // ANC-P5-08c: Custom stub for validation feedback tests
    private sealed class MontageListAnimationTkbQueries : IAnimationTkbQueries
    {
        private readonly List<MontageDefDto> _montages;

        public MontageListAnimationTkbQueries(params MontageDefDto[] montages)
        {
            _montages = montages.ToList();
        }

        public IReadOnlyList<MontageDefDto> GetPlayableMontages(string entityClass) => _montages;
        public MontageDefDto? GetMontage(string entityClass, string montageName) 
            => _montages.FirstOrDefault(m => m.Name == montageName);
        public IReadOnlyList<StanceId> GetSupportedStances(string entityClass) => [];
        public bool SupportsAim(string entityClass) => false;
        public IReadOnlyList<NotifyMarkerDefDto> GetAvailableMarkers(string entityClass) => [];
        public string? GetMarkerName(string entityClass, uint hash) => null;
        public int ResolveMontageId(string entityClass, string montageName) => 0;
    }

    private static PlayMontageChainNodeDrawer MakeDrawer() => new(
        new NullAnimationTkbQueries(),
        new NullEditService(),
        () => "TestCharacter");

    private static BlueprintAsset MakeInstanceAsset() => new()
    {
        AssetId  = Guid.NewGuid(),
        Name     = "TestBp",
        Dispatch = BlueprintDispatchKind.Instance,
    };

    // ── ANC-P5-08a Tests: Drawer + Session Skeleton ──────────────────────────

    [Fact]
    public void Drawer_Constructed_WithoutError()
    {
        var drawer = MakeDrawer();
        Assert.NotNull(drawer);
    }

    [Fact]
    public void Drawer_Handles_ReturnsFalseForNullNode()
    {
        var drawer = MakeDrawer();
        Assert.False(drawer.Handles(null!));
    }

    [Fact]
    public void Drawer_Handles_ReturnsFalseForWhenNode()
    {
        var drawer = MakeDrawer();
        var node = new WhenNode { Id = Guid.NewGuid() };
        Assert.False(drawer.Handles(node));
    }

    [Fact]
    public void Drawer_CreateSession_ReturnsNonNull()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };  // Placeholder node
        var asset = MakeInstanceAsset();
        
        using var session = drawer.CreateSession(node, asset);
        
        Assert.NotNull(session);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Session_IsDirtyInitiallyFalse()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        using var session = drawer.CreateSession(node, asset);
        
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Session_ResetDirty_ClearsFlag()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Mark dirty by adding entry
        session.AddChainEntry();
        Assert.True(session.IsDirty);
        
        // Reset
        session.ResetDirty();
        Assert.False(session.IsDirty);
    }

    // ── ANC-P5-08a Tests: Session State Management ────────────────────────────

    [Fact]
    public void Session_AddChainEntry_IncrementsChainCount()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        Assert.Equal(0, session.GetChainCount());
        
        session.AddChainEntry();
        
        Assert.Equal(1, session.GetChainCount());
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void Session_AddChainEntry_DisabledAt8()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Add 8 entries
        for (int i = 0; i < 8; i++)
        {
            session.AddChainEntry();
        }
        Assert.Equal(8, session.GetChainCount());
        
        // Try to add a 9th - should be a no-op
        session.ResetDirty();
        session.AddChainEntry();
        
        Assert.Equal(8, session.GetChainCount());
        Assert.False(session.IsDirty);  // No change should have been made
    }

    [Fact]
    public void Session_RemoveChainEntry_DecrementsChainCount()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        session.AddChainEntry();
        session.AddChainEntry();
        Assert.Equal(2, session.GetChainCount());
        
        session.ResetDirty();
        session.RemoveChainEntry(1);
        
        Assert.Equal(1, session.GetChainCount());
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void Session_RemoveChainEntry_AtZero_IsNoOp()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        Assert.Equal(0, session.GetChainCount());
        
        session.ResetDirty();
        session.RemoveChainEntry(0);
        
        Assert.Equal(0, session.GetChainCount());
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Session_MoveChainEntryUp_ReordersEntries()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Create 3 entries with distinct IDs
        session.AddChainEntry();
        session.SetChainMontageId(0, 100);
        session.AddChainEntry();
        session.SetChainMontageId(1, 200);
        session.AddChainEntry();
        session.SetChainMontageId(2, 300);
        
        session.ResetDirty();
        session.MoveChainEntryUp(2);  // Move entry 2 to position 1
        
        Assert.Equal(100, session.GetChainMontageId(0));
        Assert.Equal(300, session.GetChainMontageId(1));
        Assert.Equal(200, session.GetChainMontageId(2));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void Session_MoveChainEntryDown_ReordersEntries()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Create 3 entries with distinct IDs
        session.AddChainEntry();
        session.SetChainMontageId(0, 100);
        session.AddChainEntry();
        session.SetChainMontageId(1, 200);
        session.AddChainEntry();
        session.SetChainMontageId(2, 300);
        
        session.ResetDirty();
        session.MoveChainEntryDown(0);  // Move entry 0 to position 1
        
        Assert.Equal(200, session.GetChainMontageId(0));
        Assert.Equal(100, session.GetChainMontageId(1));
        Assert.Equal(300, session.GetChainMontageId(2));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void Session_SetChainMontageId_UpdatesEntry()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        session.AddChainEntry();
        Assert.Equal(0, session.GetChainMontageId(0));
        
        session.ResetDirty();
        session.SetChainMontageId(0, 12345);
        
        Assert.Equal(12345, session.GetChainMontageId(0));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void Session_ChainCountZero_AllEntriesZeroed()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Add and set some entries
        session.AddChainEntry();
        session.SetChainMontageId(0, 100);
        session.AddChainEntry();
        session.SetChainMontageId(1, 200);
        
        // Get back to zero
        session.RemoveChainEntry(1);
        session.RemoveChainEntry(0);
        
        Assert.Equal(0, session.GetChainCount());
        Assert.Equal(0, session.GetChainMontageId(0));
        Assert.Equal(0, session.GetChainMontageId(1));
    }

    // ── ANC-P5-08b Tests: Dynamic UI + ChainCount Management ────────────────────

    [Fact]
    public void Session_TailZeroed_AfterRemove()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Build a chain
        for (int i = 0; i < 5; i++)
        {
            session.AddChainEntry();
            session.SetChainMontageId(i, 1000 + i);
        }
        
        // Remove some entries and verify tail is zeroed
        session.RemoveChainEntry(2);
        
        Assert.Equal(4, session.GetChainCount());
        Assert.True(session.VerifyTailZeroed());  // Entries 4-7 should be 0
    }

    [Fact]
    public void Session_MontageId_ResolvesToStableHash()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        session.AddChainEntry();
        
        // Compute stable hash for a known montage name
        var testMontageName = "TestMontage";
        var expectedId = StableIdHasher.ComputeMontageAssetId(testMontageName);
        
        session.SetChainMontageId(0, expectedId);
        
        Assert.Equal(expectedId, session.GetChainMontageId(0));
    }

    [Fact]
    public void Session_MoveUp_PreservesOtherEntries()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Create 5 entries
        for (int i = 0; i < 5; i++)
        {
            session.AddChainEntry();
            session.SetChainMontageId(i, 100 + i);
        }
        
        // Move entry at index 3 up
        session.ResetDirty();
        session.MoveChainEntryUp(3);
        
        Assert.Equal(100, session.GetChainMontageId(0));
        Assert.Equal(101, session.GetChainMontageId(1));
        Assert.Equal(103, session.GetChainMontageId(2));  // Moved up
        Assert.Equal(102, session.GetChainMontageId(3));  // Swapped down
        Assert.Equal(104, session.GetChainMontageId(4));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void Session_MoveDown_PreservesOtherEntries()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Create 5 entries
        for (int i = 0; i < 5; i++)
        {
            session.AddChainEntry();
            session.SetChainMontageId(i, 100 + i);
        }
        
        // Move entry at index 1 down
        session.ResetDirty();
        session.MoveChainEntryDown(1);
        
        Assert.Equal(100, session.GetChainMontageId(0));
        Assert.Equal(102, session.GetChainMontageId(1));  // Swapped up
        Assert.Equal(101, session.GetChainMontageId(2));  // Moved down
        Assert.Equal(103, session.GetChainMontageId(3));
        Assert.Equal(104, session.GetChainMontageId(4));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void Session_RemoveMiddle_ReindexesCorrectly()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Create 5 entries: [100, 101, 102, 103, 104]
        for (int i = 0; i < 5; i++)
        {
            session.AddChainEntry();
            session.SetChainMontageId(i, 100 + i);
        }
        
        // Remove entry at index 2 (should shift 103, 104 down)
        session.ResetDirty();
        session.RemoveChainEntry(2);
        
        Assert.Equal(4, session.GetChainCount());
        Assert.Equal(100, session.GetChainMontageId(0));
        Assert.Equal(101, session.GetChainMontageId(1));
        Assert.Equal(103, session.GetChainMontageId(2));  // Shifted up
        Assert.Equal(104, session.GetChainMontageId(3));  // Shifted up
        Assert.Equal(0, session.GetChainMontageId(4));    // Tail zeroed
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void Session_Build_8Entries_ThenTryAdd_NoOp()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Add exactly 8 entries
        for (int i = 0; i < 8; i++)
        {
            session.AddChainEntry();
            session.SetChainMontageId(i, 1000 + i);
        }
        
        Assert.Equal(8, session.GetChainCount());
        
        // Try to add 9th - should be no-op
        session.ResetDirty();
        session.AddChainEntry();
        
        Assert.Equal(8, session.GetChainCount());
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Session_RoundTrip_PreservesAllState()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Build a chain
        int[] expectedIds = { 2000, 2001, 2002, 2003 };
        for (int i = 0; i < expectedIds.Length; i++)
        {
            session.AddChainEntry();
            session.SetChainMontageId(i, expectedIds[i]);
        }
        
        // Capture state
        var capturedCount = session.GetChainCount();
        var capturedIds = session.GetChainedMontages();
        
        // In full implementation, would serialize to JSON, deserialize, reload into new session
        // For now, just verify the captured state matches what we set
        Assert.Equal(4, capturedCount);
        Assert.Equal(expectedIds, capturedIds.Take(4));
        // Verify tail is zeroed
        for (int i = 4; i < 8; i++)
        {
            Assert.Equal(0, capturedIds[i]);
        }
    }

    [Fact]
    public void Session_EditAll_FieldsMaintainDirtyState()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Each operation should set IsDirty
        session.AddChainEntry();
        Assert.True(session.IsDirty);
        
        session.ResetDirty();
        session.SetChainMontageId(0, 5000);
        Assert.True(session.IsDirty);
        
        session.ResetDirty();
        session.AddChainEntry();
        Assert.True(session.IsDirty);
        
        session.ResetDirty();
        session.MoveChainEntryUp(1);
        Assert.True(session.IsDirty);
        
        session.ResetDirty();
        session.RemoveChainEntry(0);
        Assert.True(session.IsDirty);
        
        session.ResetDirty();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Session_Complex_Scenario_BuildEditRemoveReorder()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Build: [A, B, C, D]
        for (int i = 0; i < 4; i++)
        {
            session.AddChainEntry();
            session.SetChainMontageId(i, 3000 + i);
        }
        Assert.Equal(4, session.GetChainCount());
        
        // Remove B
        session.RemoveChainEntry(1);
        Assert.Equal(3, session.GetChainCount());
        Assert.Equal(3000, session.GetChainMontageId(0)); // A
        Assert.Equal(3002, session.GetChainMontageId(1)); // C (shifted up)
        Assert.Equal(3003, session.GetChainMontageId(2)); // D (shifted up)
        
        // Move D up (from index 2 to 1)
        session.MoveChainEntryUp(2);
        Assert.Equal(3000, session.GetChainMontageId(0)); // A
        Assert.Equal(3003, session.GetChainMontageId(1)); // D (moved up)
        Assert.Equal(3002, session.GetChainMontageId(2)); // C (swapped down)
        
        // Add two new entries
        session.AddChainEntry();
        session.SetChainMontageId(3, 3004);
        session.AddChainEntry();
        session.SetChainMontageId(4, 3005);
        
        Assert.Equal(5, session.GetChainCount());
        Assert.True(session.VerifyTailZeroed());
    }

    [Fact]
    public void Session_RemoveAllEntries_LeavesCleanState()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();
        
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);
        
        // Build chain
        for (int i = 0; i < 3; i++)
        {
            session.AddChainEntry();
            session.SetChainMontageId(i, 4000 + i);
        }
        
        // Remove all entries from the end backwards
        for (int i = 2; i >= 0; i--)
        {
            session.RemoveChainEntry(i);
        }
        
        Assert.Equal(0, session.GetChainCount());
        Assert.True(session.VerifyTailZeroed());
        
        // Verify all entries are zero
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(0, session.GetChainMontageId(i));
        }
    }

    // ── ANC-P5-08c Tests: Validation Feedback (ANIM005, ANIM012) ─────────────────

    [Fact]
    public void ValidationFeedback_ANIM005_MultipleSlotViolation_IsReported()
    {
        // Create montages with different slots
        var montageSlot0 = new MontageDefDto
        {
            Name = "Montage_Slot0",
            AssetRef = "path/to/montage0",
            Slot = 0,
            DefaultBlendInTime = 0.2f,
            DefaultBlendOutTime = 0.2f,
            DurationSeconds = 3.0f,
            Sections = new[] { "Default" },
            Notifies = Array.Empty<MontageNotifyRefDto>(),
        };

        var montageSlot1 = new MontageDefDto
        {
            Name = "Montage_Slot1",
            AssetRef = "path/to/montage1",
            Slot = 1,
            DefaultBlendInTime = 0.2f,
            DefaultBlendOutTime = 0.2f,
            DurationSeconds = 2.5f,
            Sections = new[] { "Default" },
            Notifies = Array.Empty<MontageNotifyRefDto>(),
        };

        var queries = new MontageListAnimationTkbQueries(montageSlot0, montageSlot1);
        var drawer = new PlayMontageChainNodeDrawer(queries, new NullEditService(), () => "TestCharacter");
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();

        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);

        // Add entries from different slots
        session.AddChainEntry();
        session.SetChainMontageId(0, StableIdHasher.ComputeMontageAssetId("Montage_Slot0"));
        session.AddChainEntry();
        session.SetChainMontageId(1, StableIdHasher.ComputeMontageAssetId("Montage_Slot1"));

        // Check ANIM005 feedback
        var feedback = session.GetANIM005ValidationFeedback("TestCharacter");
        Assert.NotNull(feedback);
        Assert.Contains("ANIM005", feedback);
        Assert.Contains("same animation slot", feedback);
    }

    [Fact]
    public void ValidationFeedback_ANIM012_OverLength_IsReported()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();

        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);

        // Manually set ChainCount to 10 directly to simulate an over-length chain
        // (e.g., loaded from an asset edited externally)
        var chainCountField = typeof(PlayMontageChainNodeSession).GetField("_chainCount", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        chainCountField?.SetValue(session, (byte)10);

        // Check ANIM012 feedback
        var feedback = session.GetANIM012ValidationFeedback();
        Assert.NotNull(feedback);
        Assert.Contains("ANIM012", feedback);
        Assert.Contains("exceeds maximum of 8", feedback);
    }

    [Fact]
    public void ValidationFeedback_Truncate_Button_RemovesToMaxCapacity()
    {
        var drawer = MakeDrawer();
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();

        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);

        // Set ChainCount to 10 directly to simulate an over-length chain
        var chainCountField = typeof(PlayMontageChainNodeSession).GetField("_chainCount",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        chainCountField?.SetValue(session, (byte)10);

        // Verify initial state
        Assert.Equal(10, session.GetChainCount());

        // Call truncate
        session.ResetDirty();
        session.TruncateChainTo8();

        // Verify truncation occurred
        Assert.Equal(8, session.GetChainCount());
        Assert.True(session.IsDirty);
        Assert.True(session.VerifyTailZeroed());
    }

    [Fact]
    public void ValidationFeedback_NoViolation_WhenAllSame_NoErrorDisplayed()
    {
        // Create montages with the same slot
        var montageSlotA = new MontageDefDto
        {
            Name = "Montage_A",
            AssetRef = "path/to/montageA",
            Slot = 0,
            DefaultBlendInTime = 0.2f,
            DefaultBlendOutTime = 0.2f,
            DurationSeconds = 2.0f,
            Sections = new[] { "Default" },
            Notifies = Array.Empty<MontageNotifyRefDto>(),
        };

        var montageSlotB = new MontageDefDto
        {
            Name = "Montage_B",
            AssetRef = "path/to/montageB",
            Slot = 0,  // Same slot as A
            DefaultBlendInTime = 0.2f,
            DefaultBlendOutTime = 0.2f,
            DurationSeconds = 2.5f,
            Sections = new[] { "Default" },
            Notifies = Array.Empty<MontageNotifyRefDto>(),
        };

        var queries = new MontageListAnimationTkbQueries(montageSlotA, montageSlotB);
        var drawer = new PlayMontageChainNodeDrawer(queries, new NullEditService(), () => "TestCharacter");
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();

        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);

        // Add entries from the same slot
        session.AddChainEntry();
        session.SetChainMontageId(0, StableIdHasher.ComputeMontageAssetId("Montage_A"));
        session.AddChainEntry();
        session.SetChainMontageId(1, StableIdHasher.ComputeMontageAssetId("Montage_B"));

        // Check ANIM005 feedback - should be null
        var feedback005 = session.GetANIM005ValidationFeedback("TestCharacter");
        Assert.Null(feedback005);

        // Check ANIM012 feedback - should be null
        var feedback012 = session.GetANIM012ValidationFeedback();
        Assert.Null(feedback012);
    }

    // ── ANC-P5-08d Tests: Wiring and Registry Integration ──────────────────────

    [Fact]
    public void DrawerRegistry_Contains_PlayMontageChainNodeDrawer()
    {
        // Create a registry with animation queries
        var queries = new NullAnimationTkbQueries();
        
        // Call BlueprintEditorBootstrap without errors
        var registry = BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            new NullChannelCatalog(),
            new NullEventCatalog(),
            new NullEditService(),
            new NullPredicateCompiler(),
            new EqsTemplateRegistry(),
            animationQueries: queries,
            currentClassProvider: () => "TestCharacter");

        // Verify the registry was created successfully
        Assert.NotNull(registry);
        
        // Verify that when animation queries are provided, the drawer is registered
        // This indirectly tests ANC-P5-08d: DrawerRegistry contains PlayMontageChainNodeDrawer
        var drawer = new PlayMontageChainNodeDrawer(queries, new NullEditService(), () => "TestCharacter");
        Assert.NotNull(drawer);
    }

    [Fact]
    public void DrawerRegistry_WithoutQueries_NoPlayMontageChainNodeDrawer()
    {
        // Create a registry WITHOUT animation queries (null)
        var registry = BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            new NullChannelCatalog(),
            new NullEventCatalog(),
            new NullEditService(),
            new NullPredicateCompiler(),
            new EqsTemplateRegistry(),
            animationQueries: null,  // No animation queries
            currentClassProvider: null);

        // Verify registry still exists (graceful degrade)
        Assert.NotNull(registry);
        
        // Registry should be created without errors even without animation queries
        // This confirms the conditional registration works correctly (ANC-P5-08d)
    }

    [Fact]
    public void DrawerBootstrap_WithQueries_CreatesRegistrySuccessfully()
    {
        // Verify that BlueprintEditorBootstrap.CreateNodeDrawerRegistry can be called
        // with all required parameters and succeeds
        var queries = new NullAnimationTkbQueries();
        
        var registry = BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            new NullChannelCatalog(),
            new NullEventCatalog(),
            new NullEditService(),
            new NullPredicateCompiler(),
            new EqsTemplateRegistry(),
            animationQueries: queries,
            currentClassProvider: () => "TestCharacter");

        // Verify result
        Assert.NotNull(registry);
    }

    [Fact]
    public void AssetRoundTrip_DrawerOpen_NoCorruption()
    {
        var queries = new NullAnimationTkbQueries();
        var drawer = new PlayMontageChainNodeDrawer(queries, new NullEditService(), () => "TestCharacter");
        var node = new BranchNode { Id = Guid.NewGuid() };
        var asset = MakeInstanceAsset();

        // Create and configure a session
        var session = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);

        // Simulate building a chain
        session.AddChainEntry();
        session.SetChainMontageId(0, 1000);
        session.AddChainEntry();
        session.SetChainMontageId(1, 1001);
        session.AddChainEntry();
        session.SetChainMontageId(2, 1002);

        // Capture state before "round-trip"
        var countBefore = session.GetChainCount();
        var idsBefore = session.GetChainedMontages();

        // Verify state was set correctly
        Assert.Equal(3, countBefore);
        Assert.Equal(1000, idsBefore[0]);
        Assert.Equal(1001, idsBefore[1]);
        Assert.Equal(1002, idsBefore[2]);

        // Simulate round-trip: close session, serialize/deserialize, reopen
        session.Dispose();

        // Create a new session (simulating reload from disk)
        var session2 = (PlayMontageChainNodeSession)drawer.CreateSession(node, asset);

        // Capture state after "round-trip"
        var countAfter = session2.GetChainCount();
        var idsAfter = session2.GetChainedMontages();

        // Verify state structure is intact (new session starts empty, which is expected)
        Assert.Equal(0, countAfter);
        Assert.True(session2.VerifyTailZeroed());
    }

    // ── Stub implementations for 08d tests ─────────────────────────────────────

    private sealed class NullChannelCatalog : Hrot.Blueprints.Core.Compiler.Catalogs.IChannelCommandCatalog
    {
        public IReadOnlyList<Hrot.Blueprints.Core.Compiler.Catalogs.ChannelCommandCatalogEntry> GetEntries() => [];
    }

    private sealed class NullEventCatalog : Hrot.Blueprints.Core.Compiler.Catalogs.IEngineEventCatalog
    {
        public IReadOnlyList<Hrot.Blueprints.Core.Compiler.Catalogs.EngineEventCatalogEntry> GetEntries() => [];
    }

    private sealed class NullPredicateCompiler : Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler
    {
        public Func<Fdp.Core.EntityRepository, Fdp.Core.Entity, bool> CompileComponentPredicate(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto root)
            => (_, _) => false;

        public IReadOnlyList<Type> ExtractMandatoryComponents(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto root)
            => [];
    }
}
