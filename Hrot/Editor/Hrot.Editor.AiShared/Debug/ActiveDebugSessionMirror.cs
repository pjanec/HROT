using Hrot.Editor.AiShared.Documents;

namespace Hrot.Editor.AiShared.Debug;

/// <summary>
/// ⭐⭐⭐ <b>Mirrors the ACTIVE document's debug session into the registry the toolbar reads.</b>
///
/// <para>📄 <c>AiDebugCommands</c> gates every <c>debug.*</c> button's <c>IsEnabled</c> on
/// <see cref="IDebugSessionRegistry.ActiveSession"/>. ⛔ Nothing sets that by itself — opening a document
/// does not push its session — so a host that registers the buttons without this wire gets a group that
/// is <b>permanently disabled</b>, which is worse than absent (ruling 49).</para>
///
/// <para>⭐⭐ <b>Why it is shared:</b> the editor had this as a LOCAL FUNCTION
/// (<c>EditorSubsystem.SyncActiveDebugSession</c>) inside a ~200-line block, so CGF could not reach it
/// and its own <c>DebugSessionRegistry</c> stayed empty for the life of the process. ⇒ one
/// implementation (ruling 9), and the <c>kind → session</c> policy is stated ONCE instead of twice.</para>
///
/// <para>⚠ <b>The BTree/HSM arms map to <c>null</c> deliberately</b>, carried over verbatim from the
/// editor's note: those debug sessions exist as types but are not attached, so answering with one would
/// enable buttons that cannot step. ⛔ Do not "fix" this by passing the BTree/HSM sessions — attach them
/// first.</para>
/// </summary>
public static class ActiveDebugSessionMirror
{
    /// <summary>
    /// Subscribes to <see cref="AiDocumentManager.ActiveChanged"/> and pushes the matching session into
    /// <paramref name="registry"/>, then runs once so a document already open is reflected immediately.
    /// </summary>
    /// <param name="documents">The host's document manager. No-op when null (bare-ctor hosts).</param>
    /// <param name="registry">The registry <c>AiDebugCommands</c> reads.</param>
    /// <param name="blueprintSession">
    /// The host's attached blueprint debug session, resolved at CALL TIME (⛔ not captured), so a host
    /// that builds it after this wire still works and a host that never builds one passes <c>null</c>
    /// honestly.
    /// </param>
    /// <remarks>
    /// ⭐ Uses <see cref="IDebugSessionRegistry.SetActiveSession"/>, NOT TryAcquire/Release: the
    /// blueprint session is eagerly attached and is <c>DebugProbe.Sink</c>, so it must never be detached
    /// by a document switch. 📌 That reasoning is the editor's own and is preserved here because it is
    /// the non-obvious half.
    /// </remarks>
    public static void Wire(
        AiDocumentManager?               documents,
        IDebugSessionRegistry            registry,
        Func<IAiDebugSession?>           blueprintSession)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(blueprintSession);
        if (documents == null) return;

        void Sync() => registry.SetActiveSession(Resolve(documents, blueprintSession));

        documents.ActiveChanged += Sync;
        Sync();
    }

    /// <summary>
    /// ⭐ The pure <c>active kind → session</c> decision, exposed so a rail can assert the policy without
    /// an ImGui frame or a live document manager.
    /// </summary>
    public static IAiDebugSession? Resolve(
        AiDocumentManager?     documents,
        Func<IAiDebugSession?> blueprintSession)
        => documents?.Active?.Kind switch
        {
            AssetKind.Blueprint => blueprintSession(),
            // ⚠ BTree/HSM debug sessions are not attached on either host — see the type remarks.
            _                   => null,
        };
}
