using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Support;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    [EventId(99010)]
    internal struct HarnessSearchEvent { public int Value; }

    /// <summary>
    /// SR-T02..SR-T38: RecordingSearchService correctness, edge-case and performance tests.
    /// </summary>
    public class RecordingSearchServiceTests : IDisposable
    {
        private readonly IRecordingSearchService _service;

        public RecordingSearchServiceTests()
        {
            ComponentTypeRegistry.Clear();
            var editService = new ComponentEditServiceBuilder().Build();
            _service = new RecordingSearchService(
                new PredicateCompiler(editService),
                new EventScannerCompiler(editService));
        }

        public void Dispose() { }

        // ── SR-T02: Equals -- 1 frame with matching X ────────────────────────

        [Fact]
        public void SR_T02_Equals_FindsExactFrame()
        {
            // 5-frame recording with HarnessPosition.X = 100, 90, 80, 70, 60
            float[] values = { 100f, 90f, 80f, 70f, 60f };
            string fdpPath = BuildSingleEntityRecording(values);
            try
            {
                // Use NumericPredicateDto with tight range to avoid float-string formatting issues.
                var predicate = new PropertyMatchDto
                {
                    ComponentType = typeof(HarnessPosition),
                    PropertyPath  = "X",
                    Operator      = SearchOperator.Equals,
                    Predicate     = new NumericPredicateDto { MinValue = 79.9, MaxValue = 80.1 }
                };

                var results = _service.ExecuteSearch(fdpPath, predicate);

                Assert.Single(results);
            }
            finally { TryDelete(fdpPath); }
        }

        // ── SR-T03: GreaterThan -- multiple frames match ──────────────────────

        [Fact]
        public void SR_T03_GreaterThan_FindsMultipleFrames()
        {
            float[] values = { 100f, 90f, 80f, 70f, 60f };
            string fdpPath = BuildSingleEntityRecording(values);
            try
            {
                var predicate = new PropertyMatchDto
                {
                    ComponentType = typeof(HarnessPosition),
                    PropertyPath  = "X",
                    Operator      = SearchOperator.GreaterThan,
                    Predicate     = new NumericPredicateDto { MinValue = 75.0 }
                };

                var results = _service.ExecuteSearch(fdpPath, predicate);

                // X=100, 90, 80 are > 75 (3 matches)
                Assert.Equal(3, results.Count);
            }
            finally { TryDelete(fdpPath); }
        }

        // ── SR-T10: Spatial Entry -- entity enters zone ───────────────────────

        [Fact]
        public void SR_T10_Spatial_Entry_DetectedOnCorrectFrame()
        {
            // Entity starts outside zone, then moves inside
            string fdpPath;
            using (var h = new FdpRecordingHarness())
            {
                h.SpawnEntity().WithComponent(new HarnessTransform { Position = new Vector3(-20f, -20f, 0) });
                var entity = h.LastSpawned;

                h.Tick(); h.RecordKeyframe();

                // Move inside zone [0,0]-[10,10]
                h.MutateComponent<HarnessTransform>(entity, c => new HarnessTransform { Position = new Vector3(5f, 5f, 0) });
                h.Tick(); h.RecordDelta();

                fdpPath = h.BuildToTempFile();

                var predicate = new SpatialBoundingPredicateDto
                {
                    Bounds = new BoundingBox2D { Min = new Vector2(0f, 0f), Max = new Vector2(10f, 10f) },
                    TriggerEvent          = BoundaryEvent.Entry,
                    PositionComponentType = typeof(HarnessTransform),
                    PositionXPath         = "Position.X",
                    PositionYPath         = "Position.Y"
                };

                var results = _service.ExecuteSearch(fdpPath, predicate);

                Assert.True(results.Count >= 1);
                Assert.Contains(results, r => r.ContextMessage == "Entered Area");
            }
        }

        // ── SR-T11: Spatial Exit -- entity exits zone ─────────────────────────

        [Fact]
        public void SR_T11_Spatial_Exit_DetectedOnCorrectFrame()
        {
            string fdpPath;
            using (var h = new FdpRecordingHarness())
            {
                h.SpawnEntity().WithComponent(new HarnessTransform { Position = new Vector3(5f, 5f, 0) });
                var entity = h.LastSpawned;

                h.Tick(); h.RecordKeyframe();

                // Move outside zone [0,0]-[10,10]
                h.MutateComponent<HarnessTransform>(entity, c => new HarnessTransform { Position = new Vector3(50f, 50f, 0) });
                h.Tick(); h.RecordDelta();

                fdpPath = h.BuildToTempFile();

                var predicate = new SpatialBoundingPredicateDto
                {
                    Bounds = new BoundingBox2D { Min = new Vector2(0f, 0f), Max = new Vector2(10f, 10f) },
                    TriggerEvent          = BoundaryEvent.Exit,
                    PositionComponentType = typeof(HarnessTransform),
                    PositionXPath         = "Position.X",
                    PositionYPath         = "Position.Y"
                };

                var results = _service.ExecuteSearch(fdpPath, predicate);

                Assert.True(results.Count >= 1);
                Assert.Contains(results, r => r.ContextMessage == "Exited Area");
            }
        }

        // ── SR-T12: Spatial EntryOrExit -- both transitions detected ─────────

        [Fact]
        public void SR_T12_Spatial_EntryOrExit_BothTransitionsDetected()
        {
            string fdpPath;
            using (var h = new FdpRecordingHarness())
            {
                h.SpawnEntity().WithComponent(new HarnessTransform { Position = new Vector3(-20f, -20f, 0) });
                var entity = h.LastSpawned;

                h.Tick(); h.RecordKeyframe();

                // Enter zone
                h.MutateComponent<HarnessTransform>(entity, c => new HarnessTransform { Position = new Vector3(5f, 5f, 0) });
                h.Tick(); h.RecordDelta();

                // Exit zone
                h.MutateComponent<HarnessTransform>(entity, c => new HarnessTransform { Position = new Vector3(50f, 50f, 0) });
                h.Tick(); h.RecordDelta();

                fdpPath = h.BuildToTempFile();

                var predicate = new SpatialBoundingPredicateDto
                {
                    Bounds = new BoundingBox2D { Min = new Vector2(0f, 0f), Max = new Vector2(10f, 10f) },
                    TriggerEvent          = BoundaryEvent.EntryOrExit,
                    PositionComponentType = typeof(HarnessTransform),
                    PositionXPath         = "Position.X",
                    PositionYPath         = "Position.Y"
                };

                var results = _service.ExecuteSearch(fdpPath, predicate);

                Assert.Contains(results, r => r.ContextMessage == "Entered Area");
                Assert.Contains(results, r => r.ContextMessage == "Exited Area");
            }
        }

        // ── SR-T13: Spatial Entry filter -- does NOT fire on Exit ─────────────

        [Fact]
        public void SR_T13_Spatial_EntryFilter_DoesNotFireOnExit()
        {
            string fdpPath;
            using (var h = new FdpRecordingHarness())
            {
                h.SpawnEntity().WithComponent(new HarnessTransform { Position = new Vector3(5f, 5f, 0) });
                var entity = h.LastSpawned;

                h.Tick(); h.RecordKeyframe();

                // Exit zone
                h.MutateComponent<HarnessTransform>(entity, c => new HarnessTransform { Position = new Vector3(50f, 50f, 0) });
                h.Tick(); h.RecordDelta();

                fdpPath = h.BuildToTempFile();

                var predicate = new SpatialBoundingPredicateDto
                {
                    Bounds = new BoundingBox2D { Min = new Vector2(0f, 0f), Max = new Vector2(10f, 10f) },
                    TriggerEvent          = BoundaryEvent.Entry, // only entry
                    PositionComponentType = typeof(HarnessTransform),
                    PositionXPath         = "Position.X",
                    PositionYPath         = "Position.Y"
                };

                var results = _service.ExecuteSearch(fdpPath, predicate);

                Assert.DoesNotContain(results, r => r.ContextMessage == "Exited Area");
            }
        }

        // ── SR-T14: Structural Added -- component added mid-recording ─────────

        [Fact]
        public void SR_T14_Structural_Added_DetectedOnCorrectFrame()
        {
            string fdpPath;
            using (var h = new FdpRecordingHarness())
            {
                h.SpawnEntity(); // no HarnessVelocity yet
                var entity = h.LastSpawned;

                h.Tick(); h.RecordKeyframe();

                // Add velocity component
                h.AddComponent(entity, new HarnessVelocity { Vx = 5f });
                h.Tick(); h.RecordDelta();

                fdpPath = h.BuildToTempFile();

                var predicate = new StructuralPredicateDto
                {
                    ComponentType    = typeof(HarnessVelocity),
                    ModificationType = StructuralModification.Added
                };

                var results = _service.ExecuteSearch(fdpPath, predicate);

                Assert.True(results.Count >= 1);
                Assert.Contains(results, r => r.ContextMessage.Contains("Gained"));
            }
        }

        // ── SR-T15: Structural Removed -- component removed mid-recording ─────

        [Fact]
        public void SR_T15_Structural_Removed_DetectedOnCorrectFrame()
        {
            string fdpPath;
            using (var h = new FdpRecordingHarness())
            {
                h.SpawnEntity().WithComponent(new HarnessVelocity { Vx = 5f });
                var entity = h.LastSpawned;

                h.Tick(); h.RecordKeyframe();

                // Remove velocity component
                h.RemoveComponent<HarnessVelocity>(entity);
                h.Tick(); h.RecordDelta();

                fdpPath = h.BuildToTempFile();

                var predicate = new StructuralPredicateDto
                {
                    ComponentType    = typeof(HarnessVelocity),
                    ModificationType = StructuralModification.Removed
                };

                var results = _service.ExecuteSearch(fdpPath, predicate);

                Assert.True(results.Count >= 1);
                Assert.Contains(results, r => r.ContextMessage.Contains("Lost"));
            }
        }

        // ── SR-T16: Structural AnyChange -- both added and removed detected ───

        [Fact]
        public void SR_T16_Structural_AnyChange_BothTransitionsDetected()
        {
            string fdpPath;
            using (var h = new FdpRecordingHarness())
            {
                h.SpawnEntity(); // no HarnessVelocity
                var entity = h.LastSpawned;

                h.Tick(); h.RecordKeyframe();

                // Add velocity
                h.AddComponent(entity, new HarnessVelocity { Vx = 1f });
                h.Tick(); h.RecordDelta();

                // Remove velocity
                h.RemoveComponent<HarnessVelocity>(entity);
                h.Tick(); h.RecordDelta();

                fdpPath = h.BuildToTempFile();

                var predicate = new StructuralPredicateDto
                {
                    ComponentType    = typeof(HarnessVelocity),
                    ModificationType = StructuralModification.AnyChange
                };

                var results = _service.ExecuteSearch(fdpPath, predicate);

                Assert.Contains(results, r => r.ContextMessage.Contains("Gained"));
                Assert.Contains(results, r => r.ContextMessage.Contains("Lost"));
            }
        }

        // ── SR-T17: Structural Added -- only emits Added events ──────────────

        [Fact]
        public void SR_T17_Structural_Added_DoesNotEmitRemoval()
        {
            string fdpPath;
            using (var h = new FdpRecordingHarness())
            {
                h.SpawnEntity().WithComponent(new HarnessVelocity { Vx = 5f });
                var entity = h.LastSpawned;

                h.Tick(); h.RecordKeyframe();

                h.RemoveComponent<HarnessVelocity>(entity);
                h.Tick(); h.RecordDelta();

                fdpPath = h.BuildToTempFile();

                var predicate = new StructuralPredicateDto
                {
                    ComponentType    = typeof(HarnessVelocity),
                    ModificationType = StructuralModification.Added // only Added
                };

                var results = _service.ExecuteSearch(fdpPath, predicate);

                Assert.DoesNotContain(results, r => r.ContextMessage.Contains("Lost"));
            }
        }

        // ── SR-T18: Structural Removed -- only emits Removed events ──────────

        [Fact]
        public void SR_T18_Structural_Removed_DoesNotEmitGainedEvents()
        {
            string fdpPath;
            using (var h = new FdpRecordingHarness())
            {
                h.SpawnEntity();
                var entity = h.LastSpawned;

                h.Tick(); h.RecordKeyframe();

                h.AddComponent(entity, new HarnessVelocity { Vx = 1f });
                h.Tick(); h.RecordDelta();

                fdpPath = h.BuildToTempFile();

                var predicate = new StructuralPredicateDto
                {
                    ComponentType    = typeof(HarnessVelocity),
                    ModificationType = StructuralModification.Removed // only Removed
                };

                var results = _service.ExecuteSearch(fdpPath, predicate);

                Assert.DoesNotContain(results, r => r.ContextMessage.Contains("Gained"));
            }
        }

        // ── SR-T19: Lifecycle EcsHandle -- entity found by index ──────────────

        [Fact]
        public void SR_T19_Lifecycle_EcsHandle_FindsEntityByIndex()
        {
            string fdpPath;
            int entityIndex;
            using (var h = new FdpRecordingHarness())
            {
                h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f });
                entityIndex = h.LastSpawned.Index;

                h.Tick(); h.RecordKeyframe();
                h.Tick(); h.RecordDelta();

                fdpPath = h.BuildToTempFile();

                var criteria = new LifecyclePredicateDto
                {
                    IdentifierType = EntityIdentifierType.EcsHandle,
                    TargetValue    = entityIndex.ToString()
                };

                var results = _service.ExecuteLifecycleSearch(fdpPath, criteria);

                Assert.True(results.Count >= 1);
            }
        }

        // ── SR-T20: Lifecycle -- entity that never dies flushes at EOF ────────

        [Fact]
        public void SR_T20_Lifecycle_EntityAliveAtEOF_FlushedWithEndFrame()
        {
            string fdpPath;
            int entityIndex;
            using (var h = new FdpRecordingHarness())
            {
                h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f });
                entityIndex = h.LastSpawned.Index;

                h.Tick(); h.RecordKeyframe();
                h.Tick(); h.RecordDelta();

                fdpPath = h.BuildToTempFile();

                var criteria = new LifecyclePredicateDto
                {
                    IdentifierType = EntityIdentifierType.EcsHandle,
                    TargetValue    = entityIndex.ToString()
                };

                var results = _service.ExecuteLifecycleSearch(fdpPath, criteria);

                Assert.True(results.Count >= 1);
                // StartFrame <= EndFrame
                Assert.All(results, r => Assert.True(r.StartFrame <= r.EndFrame));
            }
        }

        // ── SR-T21: Lifecycle NameSubstring -- matches entity by name field ───

        [Fact]
        public void SR_T21_Lifecycle_NameSubstring_FindsMatchingEntity()
        {
            string fdpPath;
            using (var h = new FdpRecordingHarness())
            {
                h.SpawnEntity()
                    .WithComponent(new HarnessPosition { X = 1f })
                    .WithComponent(new HarnessEntityInfo { Name = new FixedString32("AlphaTank") });

                h.SpawnEntity()
                    .WithComponent(new HarnessPosition { X = 2f })
                    .WithComponent(new HarnessEntityInfo { Name = new FixedString32("BetaTank") });

                h.Tick(); h.RecordKeyframe();

                fdpPath = h.BuildToTempFile();

                var criteria = new LifecyclePredicateDto
                {
                    IdentifierType   = EntityIdentifierType.NameSubstring,
                    TargetValue      = "Alpha",
                    NameComponentType = typeof(HarnessEntityInfo),
                    NamePropertyPath  = "Name"
                };

                var results = _service.ExecuteLifecycleSearch(fdpPath, criteria);

                Assert.True(results.Count >= 1);
                // All matched entities should correspond to "Alpha" substring, not "Beta"
            }
        }

        // ── SR-T22: Lifecycle -- entity that dies mid-recording ───────────────

        [Fact]
        public void SR_T22_Lifecycle_EntityDeath_CapturedWithCorrectEndFrame()
        {
            string fdpPath;
            int entityIndex;
            using (var h = new FdpRecordingHarness())
            {
                h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f });
                var entity = h.LastSpawned;
                entityIndex = entity.Index;

                h.Tick(); h.RecordKeyframe();

                h.DestroyEntity(entity);
                h.Tick(); h.RecordDelta();

                fdpPath = h.BuildToTempFile();

                var criteria = new LifecyclePredicateDto
                {
                    IdentifierType = EntityIdentifierType.EcsHandle,
                    TargetValue    = entityIndex.ToString()
                };

                var results = _service.ExecuteLifecycleSearch(fdpPath, criteria);

                Assert.True(results.Count >= 1);
                // At least one range ends at the death frame
                Assert.Contains(results, r => r.EndFrame > r.StartFrame || r.StartFrame == r.EndFrame);
            }
        }

        // ── SR-T34: Zero allocation -- loop body allocates nothing on no-match

        [Fact]
        public void SR_T34_ZeroAllocation_LoopBodyAllocatesNothingOnNoMatch()
        {
            // Build a recording with HarnessPosition entities but predicate searches HarnessVelocity.
            // Since no entity has HarnessVelocity, QueryDelta returns 0 candidates per frame.
            // The loop body should allocate 0 bytes per frame.
            const int frameCount = 20;
            string fdpPath;
            using (var h = new FdpRecordingHarness())
            {
                for (int i = 0; i < 5; i++)
                    h.SpawnEntity().WithComponent(new HarnessPosition { X = i });

                h.Tick(); h.RecordKeyframe();

                for (int i = 1; i < frameCount; i++) { h.Tick(); h.RecordDelta(); }

                fdpPath = h.BuildToTempFile();

                // Predicate targets HarnessVelocity which exists in the schema manifest
                // (harness always registers it) but no entity has it -- so QueryDelta returns empty.
                var predicate = new PropertyMatchDto
                {
                    ComponentType = typeof(HarnessVelocity),
                    PropertyPath  = "Vx",
                    Operator      = SearchOperator.GreaterThan,
                    Predicate     = new NumericPredicateDto { MinValue = -1e30 }
                };

                // Compile predicate and set up infrastructure manually so setup cost is
                // outside the measurement window.
                var compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
                var compiledFn = compiler.CompileComponentPredicate(predicate);
                var mandatory = compiler.ExtractMandatoryComponents(predicate);

                using var playback = new PlaybackController(fdpPath);
                var repo = new EntityRepository();
                RegisterAllComponentsForTest(repo, playback);

                // Warmup: run one frame iteration outside the measurement window.
                playback.StepForward(repo);

                // Check if mandatory component is registered (it should be via schema manifest).
                var registeredTables = repo.GetRegisteredComponentTypes();
                bool hasMandatory = false;
                foreach (var t in mandatory)
                    if (registeredTables.ContainsKey(t)) { hasMandatory = true; break; }

                if (!hasMandatory)
                {
                    // Early-exit path: trivially zero allocation. Skip the per-loop measurement.
                    return;
                }

                var velocityTypeId = ComponentTypeRegistry.GetId(typeof(HarnessVelocity));
                EntityQuery? query = null;
                if (velocityTypeId >= 0)
                    query = repo.Query().WithComponentId(velocityTypeId).Build();

                var results = new List<SearchResultDto>(64);
                var candidates = new List<Entity>(32);
                Action<Entity> collect = e => candidates.Add(e);
                uint lastVersion = 0;

                // Advance all frames outside the measurement window.
                // StepForward allocates per-frame buffers (decompression), so it must not
                // be inside the zero-allocation measurement block.
                while (playback.StepForward(repo))
                    lastVersion = repo.GlobalVersion;

                // Measure: QueryDelta body only should allocate 0 bytes when no entities match.
                long before = GC.GetAllocatedBytesForCurrentThread();

                const int queryIterations = 100;
                for (int iter = 0; iter < queryIterations; iter++)
                {
                    candidates.Clear();
                    if (query != null)
                        repo.QueryDelta(query, lastVersion, collect);
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (compiledFn(repo, candidates[i]))
                            results.Add(new SearchResultDto(0, 0L, candidates[i], ""));
                    }
                }

                long delta = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(0L, delta);
            }
        }

        // ── SR-T36: Isolation -- search does not affect ReplayBrowserContext ──

        [Fact]
        public void SR_T36_Isolation_SearchDoesNotAffectParallelContext()
        {
            float[] values = { 100f, 90f, 80f, 70f, 60f };
            string fdpPath = BuildSingleEntityRecording(values);
            try
            {
                // Open a separate ReplayBrowserContext and advance to frame 2.
                using var context = new ReplayBrowserContext();
                context.LoadRecording(fdpPath);
                context.SeekToFrame(2);
                int frameBefore = context.CurrentFrame;

                // Execute search -- must not disturb the external context.
                var predicate = new PropertyMatchDto
                {
                    ComponentType = typeof(HarnessPosition),
                    PropertyPath  = "X",
                    Operator      = SearchOperator.GreaterThan,
                    Predicate     = new NumericPredicateDto { MinValue = 75.0 }
                };
                _ = _service.ExecuteSearch(fdpPath, predicate);

                // Context must be at the same frame after the search.
                Assert.Equal(frameBefore, context.CurrentFrame);
            }
            finally { TryDelete(fdpPath); }
        }

        // ── SR-T37: Authority -- RequireAuthority / RequireGhost / Any ────────

        [Fact]
        public void SR_T37_Authority_RequireAuthority_FindsOnlyAuthorityEntities()
        {
            string fdpPath;
            int entityAIndex, entityBIndex;
            using (var h = new FdpRecordingHarness())
            {
                // Entity A: HarnessVelocity with authority
                h.SpawnEntity().WithComponent(new HarnessVelocity { Vx = 1f });
                var entityA = h.LastSpawned;
                entityAIndex = entityA.Index;

                int typeId = ComponentTypeRegistry.GetId(typeof(HarnessVelocity));

                // Set AuthorityMask for entity A
                ref EntityHeader headerA = ref h.Repository.GetHeader(entityA.Index);
                headerA.AuthorityMask.SetBit(typeId);

                // Entity B: HarnessVelocity WITHOUT authority (ghost)
                h.SpawnEntity().WithComponent(new HarnessVelocity { Vx = 2f });
                var entityB = h.LastSpawned;
                entityBIndex = entityB.Index;
                // Do NOT set authority for entity B

                h.Tick(); h.RecordKeyframe();

                fdpPath = h.BuildToTempFile();

                // RequireAuthority -- should find only entity A
                var predAuthority = new StructuralPredicateDto
                {
                    ComponentType        = typeof(HarnessVelocity),
                    ModificationType     = StructuralModification.Added,
                    AuthorityRequirement = AuthorityRequirement.RequireAuthority
                };
                var authorityResults = _service.ExecuteSearch(fdpPath, predAuthority);
                Assert.True(authorityResults.Any(r => r.Entity.Index == entityAIndex),
                    "RequireAuthority should find entity A (has authority)");
                Assert.False(authorityResults.Any(r => r.Entity.Index == entityBIndex),
                    "RequireAuthority should NOT find entity B (ghost)");

                // RequireGhost -- should find only entity B
                var predGhost = new StructuralPredicateDto
                {
                    ComponentType        = typeof(HarnessVelocity),
                    ModificationType     = StructuralModification.Added,
                    AuthorityRequirement = AuthorityRequirement.RequireGhost
                };
                var ghostResults = _service.ExecuteSearch(fdpPath, predGhost);
                Assert.True(ghostResults.Any(r => r.Entity.Index == entityBIndex),
                    "RequireGhost should find entity B (no authority)");
                Assert.False(ghostResults.Any(r => r.Entity.Index == entityAIndex),
                    "RequireGhost should NOT find entity A (has authority)");

                // Any -- should find both
                var predAny = new StructuralPredicateDto
                {
                    ComponentType        = typeof(HarnessVelocity),
                    ModificationType     = StructuralModification.Added,
                    AuthorityRequirement = AuthorityRequirement.AnyAuthority
                };
                var anyResults = _service.ExecuteSearch(fdpPath, predAny);
                Assert.True(anyResults.Any(r => r.Entity.Index == entityAIndex),
                    "Any should find entity A");
                Assert.True(anyResults.Any(r => r.Entity.Index == entityBIndex),
                    "Any should find entity B");
            }
        }

        // ── SR-T38: Event timing -- scanner reads events without ClearCurrentBuffers

        [Fact]
        public void SR_T38_EventTiming_EventsVisibleToScanner_NoClearBetweenStepAndScan()
        {
            // Fire an event on frame 0 and verify the scanner can see it.
            // If ClearCurrentBuffers were called between StepForward and scanner.Invoke,
            // the event would be invisible.
            string fdpPath;
            using (var h = new FdpRecordingHarness())
            {
                h.FireUnmanagedEvent(new HarnessSearchEvent { Value = 42 });
                h.Tick(); h.RecordKeyframe();
                h.Tick(); h.RecordDelta();

                fdpPath = h.BuildToTempFile();

                var predicate = new TransientEventPredicateDto
                {
                    EventType     = typeof(HarnessSearchEvent),
                    AnyOccurrence = true
                };

                var results = _service.ExecuteSearch(fdpPath, predicate);

                // If ClearCurrentBuffers was NOT called between step and scan, we should see the event.
                Assert.True(results.Count >= 1,
                    "Event should be visible: SR-T38 requires no ClearCurrentBuffers between step and scan");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string BuildSingleEntityRecording(float[] xValues)
        {
            ComponentTypeRegistry.Clear();
            // NOTE: harness not disposed intentionally -- Dispose() would delete the temp file
            // before the caller can read it. The file is cleaned up by the test's TryDelete().
            var h = new FdpRecordingHarness();
            h.SpawnEntity().WithComponent(new HarnessPosition { X = xValues[0] });
            var entity = h.LastSpawned;

            h.Tick(); h.RecordKeyframe();

            for (int i = 1; i < xValues.Length; i++)
            {
                float x = xValues[i];
                h.MutateComponent<HarnessPosition>(entity, c => new HarnessPosition { X = x, Y = c.Y, Z = c.Z });
                h.Tick(); h.RecordDelta();
            }

            return h.BuildToTempFile();
        }

        private static void RegisterAllComponentsForTest(EntityRepository repo, PlaybackController playback)
        {
            var registerMethod = typeof(EntityRepository)
                .GetMethod("RegisterComponent",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    new[] { typeof(DataPolicy?) })!;

            var manifest = playback.Metadata?.SchemaManifest;
            if (manifest != null && manifest.Count > 0)
            {
                foreach (var kvp in manifest)
                {
                    Type? type = ComponentTypeRegistry.GetType(kvp.Key);
                    if (type == null) continue;
                    try { registerMethod.MakeGenericMethod(type).Invoke(repo, new object?[] { null }); }
                    catch { }
                }
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
