namespace Hrot.Editor.AiShared.Recipes;

/// <summary>
/// ⭐⭐ <b><c>CE-460</c> (E4) — WHAT an author is making, independent of the technology that implements it.</b>
/// 📄 <c>docs/blueprints/DESIGN_Product_First_Authoring.md</c> §3.
///
/// <para>🔒 User, <c>2026-09-30</c>: <i>"User adds certain product features/building blocks like behaviors,
/// conditions, actions and the technology is a secondary choice."</i> ⇒ <b>File / New Behavior… · New
/// Action… · New Condition…</b> open the New-Asset tree ROOTED at one of these, and each
/// <see cref="INewAssetService"/> says which of its recipes produce it
/// (<see cref="INewAssetService.ProductOf"/>).</para>
///
/// <para>⛔ Not an <see cref="AssetKind"/>: a kind is the FILE technology (Blueprint, BTree, Hsm), and one
/// kind yields several products — a blueprint can be a behaviour, an action or a condition.</para>
/// </summary>
public enum AuthoringProduct
{
    /// <summary>A behaviour an entity runs — BTree, HSM or a <c>Dispatch=Behavior</c> blueprint.</summary>
    Behavior,

    /// <summary>An action a BTree / HSM / blueprint behaviour invokes.</summary>
    Action,

    /// <summary>A condition — a BTree condition or an HSM guard.</summary>
    Condition,
}
