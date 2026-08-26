using System.Text.Json;

namespace Fdp.Toolkit.Replication.Patching;

/// <summary>
/// Delegate for mutating an unmanaged struct ECS component via ref.
/// </summary>
/// <typeparam name="T">Unmanaged struct component type.</typeparam>
/// <remarks>
/// The <c>indices</c> parameter is <c>scoped</c> — it must not be captured or stored
/// by the delegate body because it references stack-allocated memory.
/// </remarks>
public delegate void ValueAttributeSetter<T>(
    ref T component,
    scoped ReadOnlySpan<int> indices,
    ref Utf8JsonReader reader) where T : struct;

/// <summary>
/// Delegate for mutating a managed class ECS component via reference.
/// </summary>
/// <typeparam name="T">Managed class component type.</typeparam>
/// <remarks>
/// The <c>indices</c> parameter is <c>scoped</c> — it must not be captured or stored
/// by the delegate body because it references stack-allocated memory.
/// </remarks>
public delegate void ReferenceAttributeSetter<T>(
    T component,
    scoped ReadOnlySpan<int> indices,
    ref Utf8JsonReader reader) where T : class;

/// <summary>
/// Provides the JSON attribute compiler with access to baseline ECS component instances.
/// Two implementations exist: <see cref="ListPatchContext"/> for entity creation,
/// <see cref="EcsPatchContext"/> for live updates.
/// </summary>
public interface IEntityPatchContext
{
    /// <summary>
    /// Returns a ref to an unmanaged struct component.
    /// In <see cref="ListPatchContext"/> this comes from the seed list (or a new default).
    /// In <see cref="EcsPatchContext"/> this wraps <c>repo.GetComponentRW&lt;T&gt;(entity)</c>.
    /// </summary>
    ref T GetUnmanagedComponent<T>() where T : struct;

    /// <summary>
    /// Returns a managed class component instance.
    /// In <see cref="ListPatchContext"/> this comes from the seed list (or a new Activator instance).
    /// In <see cref="EcsPatchContext"/> this wraps the live ECS managed component.
    /// </summary>
    T GetManagedComponent<T>() where T : class;

    /// <summary>
    /// Called after all JSON compilation is complete to flush dirty-marks for every
    /// component type touched during this session.  ListPatchContext is a no-op;
    /// EcsPatchContext calls SmartEgressUtil.MarkDirty for each distinct ordinal.
    /// </summary>
    void FlushDirtyMarks();

    /// <summary>
    /// ⭐⭐⭐ <b><c>AX-015</c> — declares that <paramref name="descriptorOrdinal"/> must be republished, so
    /// <see cref="FlushDirtyMarks"/> reaches <c>SmartEgressUtil.MarkDirty</c> for it.</b>
    ///
    /// <para>🔴🔴 <b>THE DEFECT THIS FIXES, measured `2026-08-26`.</b> The JSON path derives its ordinals
    /// from a routing table, so <see cref="FlushDirtyMarks"/> knows what to mark. ⛔ The BINARY path builds
    /// its context with <c>EcsPatchContext.Create(repo, entity)</c> — the standalone factory whose ordinal
    /// map is EMPTY — and the installers announced their descriptor through
    /// <c>BinaryPatchContext.MarkDescriptorDirty</c>, which only set a local <c>ulong</c> mask that
    /// <b>nothing in production ever read</b> *(grep: written by the installers, reset by
    /// <c>BinaryInterpreter.Apply</c>, read only by tests)*. ⇒ <c>FlushDirtyMarks</c> marked NOTHING and the
    /// binary path never told SmartEgress anything.</para>
    ///
    /// <para>⚠⚠ <b>Why nobody noticed:</b> the one attribute exercised end-to-end is <c>Heading</c> →
    /// <c>SimTransform</c>, and <c>GeoSpatialEgressTranslator</c> <b>diffs <c>lastSent</c> every tick</b> — so
    /// it republishes regardless of dirty marks. ⛔ <c>EntityInfoEgressTranslator</c> does NOT diff: it gates
    /// on <c>SmartEgressUtil.ShouldPublish(view, entity, DescriptorOrdinal, …)</c>. ⇒ 🔴 <b>a binary
    /// attribute change to <c>EntityData</c>/<c>EntityInfo</c> — an entity RENAME, say — landed in local ECS
    /// and was NEVER republished to any other node.</b></para>
    ///
    /// <para>⭐⭐ <b>Network-agnostic by construction:</b> <c>SmartEgressUtil</c> lives in
    /// <c>Fdp.Toolkit.Replication.Utilities</c> — FDP-side. The ordinal is a plain <c>long</c> here; ⛔ this
    /// seam names no DDS type and no enum.</para>
    ///
    /// <para>⭐ <b>Default implementation is a NO-OP</b> so the test doubles and <c>ListPatchContext</c> —
    /// which have no ECS to dirty — need no change. ⚠ That is deliberate: only a context backed by a real
    /// repository can meaningfully mark anything.</para>
    /// </summary>
    void MarkDescriptorDirty(long descriptorOrdinal) { }

    /// <summary>
    /// Returns true if the current context has authority to write the unmanaged struct component
    /// <typeparamref name="T"/>. Always returns <c>true</c> in <see cref="ListPatchContext"/> (creation
    /// path). Delegates to <c>EntityRepository.HasAuthority&lt;T&gt;</c> in <see cref="EcsPatchContext"/>,
    /// which reads <c>EntityHeader.AuthorityMask</c> using the ECS component type ID — exactly
    /// matching the kernel's own <c>ValidateWriteAccess&lt;T&gt;</c> guard.
    /// </summary>
    bool CanWrite<T>() where T : struct;

    /// <summary>
    /// Returns true if the current context has authority to write the managed class component
    /// <typeparamref name="T"/>. Always returns <c>true</c> in <see cref="ListPatchContext"/> (creation
    /// path). Delegates to <c>EntityRepository.HasAuthority</c> using
    /// <c>ManagedComponentType&lt;T&gt;.ID</c> in <see cref="EcsPatchContext"/>.
    /// </summary>
    bool CanWriteManaged<T>() where T : class;
}
