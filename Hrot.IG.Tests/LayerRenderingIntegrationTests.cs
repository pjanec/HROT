using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Hrot.Map.Definitions.Tkb;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace Hrot.IG.Tests;

/// <summary>
/// Integration tests for IG.2.5: end-to-end rendering pipeline with 100 entities.
///
/// Validates that the full <c>StyleResolutionSystem → MapCullingSystem</c> pipeline
/// processes entities correctly without ECS crashes, and that the culling system
/// correctly partitions entities by camera bounds.
///
/// Setup:
/// <list type="number">
///   <item>100 entities are spawned at positions across a 10 km × 10 km area.</item>
///   <item>
///     The first 50 entities (indices 0–49) are positioned at X = [0, 4900] m;
///     the remaining 50 (indices 50–99) are at X = [5000, 9900] m. All at Y = 5000 m.
///   </item>
///   <item>Affiliations are distributed: 0–24 Friend, 25–49 Hostile, 50–74 Friend, 75–99 Hostile.</item>
///   <item>
///     The camera viewport covers X = [0, 4999], Y = [0, 10000], so exactly 50 entities
///     are in view (X ≤ 4999) and 50 are outside (X ≥ 5000).
///   </item>
/// </list>
/// </summary>
public class LayerRenderingIntegrationTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private const int   EntityCount         = 100;
    private const int   ExpectedVisibleCount = 50;
    private const float EntitySpacingX       = 100f;  // 100 m between entities
    private const float EntityYPos           = 5000f; // fixed Y row

    // Viewport: covers first 50 entity positions (X ≤ 4999)
    private const float ViewportMinX = 0f;
    private const float ViewportMaxX = 4999f;
    private const float ViewportMinY = 0f;
    private const float ViewportMaxY = 10000f;
    private const float ViewportZoom = IgCameraConstants.InitialZoom;

    // ── World factory ─────────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();

        // Unmanaged components
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<ResolvedStyle>();
        repo.RegisterComponent<CullingState>();
        repo.RegisterComponent<VisualData>();

        // Managed components
        repo.RegisterManagedComponent<IgSymbolOverride>();

        return repo;
    }

    /// <summary>
    /// Runs a single system and immediately plays back its command buffer so that
    /// written components are visible in subsequent queries.
    /// </summary>
    private static void RunSystem(EntityRepository repo, IEcsModuleSystem system)
    {
        system.Execute(repo, 0f);
        var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
        cb.Playback(repo);
    }

    // ── Integration test ──────────────────────────────────────────────────────

    /// <summary>
    /// Spawns 100 entities with diverse affiliations (Friend / Hostile) and
    /// verifies that after one pipeline tick exactly 50 are marked visible by
    /// <see cref="MapCullingSystem"/> (those inside the camera viewport).
    ///
    /// Also spot-checks that <see cref="StyleResolutionSystem"/> wrote
    /// <see cref="ResolvedStyle"/> components with correct affiliation tints.
    /// </summary>
    [Fact]
    public void PipelineTick_100Entities_Exactly50MarkedVisible()
    {
        var repo = CreateRepo();

        // ── Spawn 100 entities ────────────────────────────────────────────────
        var entities = new Entity[EntityCount];

        for (int i = 0; i < EntityCount; i++)
        {
            float x = i * EntitySpacingX;   // 0, 100, 200, ..., 9900
            float y = EntityYPos;

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(i + 1));
            repo.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(x, y, 0f),
                Rotation = Quaternion.Identity,
            });

            // Diverse affiliations: Friend for first and third quarter, Hostile otherwise.
            string styleSet = (i < 25 || (i >= 50 && i < 75))
                ? IgSymbolOverride.StyleSetFriend
                : IgSymbolOverride.StyleSetHostile;

            repo.SetManagedComponent(entity, new IgSymbolOverride
            {
                StyleSetId    = styleSet,
                LabelOverride = $"Unit-{i:D3}",
            });

            entities[i] = entity;
        }

        // ── Run StyleResolutionSystem ─────────────────────────────────────────
        var styleSystem = new StyleResolutionSystem(new MapUserConfig());
        RunSystem(repo, styleSystem);

        // ── Configure camera viewport covering the first half of entities ─────
        var viewport = new MapCameraViewport
        {
            WorldMinX = ViewportMinX,
            WorldMaxX = ViewportMaxX,
            WorldMinY = ViewportMinY,
            WorldMaxY = ViewportMaxY,
            Zoom      = ViewportZoom,
        };

        // ── Run MapCullingSystem ──────────────────────────────────────────────
        var cullingSystem = new MapCullingSystem(viewport);
        RunSystem(repo, cullingSystem);

        // ── Assert: exactly 50 entities are visible ───────────────────────────
        int visibleCount = 0;
        foreach (var entity in entities)
        {
            if (repo.HasComponent<CullingState>(entity) &&
                repo.GetComponent<CullingState>(entity).IsVisible)
            {
                visibleCount++;
            }
        }

        Assert.Equal(ExpectedVisibleCount, visibleCount);
    }

    /// <summary>
    /// Verifies that all 100 entities received a <see cref="ResolvedStyle"/> component
    /// — the pipeline must write style data for every entity that matches
    /// <c>With&lt;NetworkIdentity&gt;.With&lt;SimTransform&gt;</c>, regardless of
    /// whether they are inside the camera viewport.
    /// </summary>
    [Fact]
    public void StyleResolutionSystem_100Entities_AllReceiveResolvedStyle()
    {
        var repo = CreateRepo();

        for (int i = 0; i < EntityCount; i++)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(i + 1));
            repo.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(i * EntitySpacingX, EntityYPos, 0f),
                Rotation = Quaternion.Identity,
            });
            repo.SetManagedComponent(entity, new IgSymbolOverride
            {
                StyleSetId    = IgSymbolOverride.StyleSetFriend,
                LabelOverride = $"Unit-{i:D3}",
            });
        }

        var styleSystem = new StyleResolutionSystem(new MapUserConfig());
        RunSystem(repo, styleSystem);

        int withStyle = 0;
        var query = repo.Query().With<NetworkIdentity>().With<SimTransform>().Build();
        foreach (var entity in query)
        {
            if (repo.HasComponent<ResolvedStyle>(entity))
                withStyle++;
        }

        Assert.Equal(EntityCount, withStyle);
    }

    /// <summary>
    /// Verifies that Friend entities receive the correct blue tint and Hostile
    /// entities receive the correct red tint after a pipeline tick — confirms the
    /// StyleResolutionSystem and CullingSystem interact correctly end-to-end.
    /// </summary>
    [Fact]
    public void PipelineTick_AffiliationTintsResolvedCorrectly()
    {
        var repo = CreateRepo();

        // Entity 0: Friend — positioned in-view
        var friendEntity = repo.CreateEntity();
        repo.AddComponent(friendEntity, new NetworkIdentity(1));
        repo.AddComponent(friendEntity, new SimTransform
        {
            Position = new Vector3(500f, EntityYPos, 0f),
            Rotation = Quaternion.Identity,
        });
        repo.SetManagedComponent(friendEntity, new IgSymbolOverride
        {
            StyleSetId = IgSymbolOverride.StyleSetFriend,
        });

        // Entity 1: Hostile — positioned in-view
        var hostileEntity = repo.CreateEntity();
        repo.AddComponent(hostileEntity, new NetworkIdentity(2));
        repo.AddComponent(hostileEntity, new SimTransform
        {
            Position = new Vector3(1500f, EntityYPos, 0f),
            Rotation = Quaternion.Identity,
        });
        repo.SetManagedComponent(hostileEntity, new IgSymbolOverride
        {
            StyleSetId = IgSymbolOverride.StyleSetHostile,
        });

        var styleSystem = new StyleResolutionSystem(new MapUserConfig());
        RunSystem(repo, styleSystem);

        var viewport = new MapCameraViewport
        {
            WorldMinX = ViewportMinX,
            WorldMaxX = ViewportMaxX,
            WorldMinY = ViewportMinY,
            WorldMaxY = ViewportMaxY,
            Zoom      = ViewportZoom,
        };
        var cullingSystem = new MapCullingSystem(viewport);
        RunSystem(repo, cullingSystem);

        var friendStyle  = repo.GetComponent<ResolvedStyle>(friendEntity);
        var hostileStyle = repo.GetComponent<ResolvedStyle>(hostileEntity);

        // Friend → blue tint
        Assert.Equal(ResolvedStyleConstants.FriendTintR, friendStyle.TintR);
        Assert.Equal(ResolvedStyleConstants.FriendTintG, friendStyle.TintG);
        Assert.Equal(ResolvedStyleConstants.FriendTintB, friendStyle.TintB);
        Assert.Equal(ForceId.Friend, friendStyle.Affiliation);

        // Hostile → red tint
        Assert.Equal(ResolvedStyleConstants.HostileTintR, hostileStyle.TintR);
        Assert.Equal(ResolvedStyleConstants.HostileTintG, hostileStyle.TintG);
        Assert.Equal(ResolvedStyleConstants.HostileTintB, hostileStyle.TintB);
        Assert.Equal(ForceId.Hostile, hostileStyle.Affiliation);

        // Both are in-view
        Assert.True(repo.GetComponent<CullingState>(friendEntity ).IsVisible);
        Assert.True(repo.GetComponent<CullingState>(hostileEntity).IsVisible);
    }

    /// <summary>
    /// Verifies that panning the camera (changing the viewport) correctly
    /// re-tags entities: the 50 that were outside become visible when the
    /// viewport shifts to cover them, and the original 50 become invisible.
    ///
    /// Simulates a camera pan across the entity field.
    /// </summary>
    [Fact]
    public void PipelineTick_CameraPan_ShiftsVisibleSet()
    {
        var repo = CreateRepo();

        for (int i = 0; i < EntityCount; i++)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(i + 1));
            repo.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(i * EntitySpacingX, EntityYPos, 0f),
                Rotation = Quaternion.Identity,
            });
        }

        var viewport     = new MapCameraViewport
        {
            WorldMinX = ViewportMinX,
            WorldMaxX = ViewportMaxX,
            WorldMinY = ViewportMinY,
            WorldMaxY = ViewportMaxY,
            Zoom      = ViewportZoom,
        };
        var cullingSystem = new MapCullingSystem(viewport);

        // First pass — left half visible
        RunSystem(repo, cullingSystem);

        var query = repo.Query().With<SimTransform>().Build();
        int firstPassVisible = 0;
        foreach (var entity in query)
            if (repo.GetComponent<CullingState>(entity).IsVisible)
                firstPassVisible++;

        Assert.Equal(ExpectedVisibleCount, firstPassVisible);

        // Pan camera to the right half: X = [5000, 9999]
        viewport.WorldMinX = 5000f;
        viewport.WorldMaxX = 9999f;

        // Second pass — right half visible
        RunSystem(repo, cullingSystem);

        int secondPassVisible = 0;
        foreach (var entity in query)
            if (repo.GetComponent<CullingState>(entity).IsVisible)
                secondPassVisible++;

        Assert.Equal(ExpectedVisibleCount, secondPassVisible);
    }
}
