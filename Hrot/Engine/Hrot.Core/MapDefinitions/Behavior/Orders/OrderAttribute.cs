using System;
using System.Runtime.CompilerServices;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior.Orders
{
    /// <summary>
    /// ⭐ <c>CE-2140</c> — marks a params DTO as an ORDER's DTO (docs/DESIGN_Orders_Params_Entity_Classes.md §1 O1, §2). A behaviour
    /// fulfils order <see cref="Name"/> exactly when its params DTO is this DTO; nothing else marks it (R-277 (behaviors)).
    /// <para>⭐ The name is a string so that code outside <c>Hrot.AI.Behaviors</c> (menus, HTTP, RTS) reads orders from the params-DTO
    /// registry without referencing the AI assembly's <c>OrderKind</c> (AQ90 §2.3l); inside the AI assembly it is written
    /// <c>nameof(OrderKind.X)</c>, so no literal is typed twice.</para>
    /// <para>⭐ <c>CE-3185</c> (§13.7 P1-c) — a <see cref="DtoAttribute"/>: an order DTO IS a params DTO, so the registry gives it the
    /// Params purpose and it needs no <c>[ParamsDto]</c> beside it; the compiler records its origin (file + line).</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class OrderAttribute : DtoAttribute
    {
        public OrderAttribute(string name, [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
            : base(sourceFile, sourceLine)
            => Name = name ?? throw new ArgumentNullException(nameof(name));

        /// <summary>The order's name, e.g. <c>Move</c>: the second half of a fulfilling behaviour's <c>&lt;Class&gt;&lt;Order&gt;</c> name.</summary>
        public string Name { get; }

        /// <summary>The map gesture that issues this order with no menu (an RTS click), if any.</summary>
        public OrderGesture Gesture { get; set; }

        /// <summary>⭐ <c>CE-2141</c> — the tactical-intent id of an ORDER (§3.1): <c>Order.&lt;Name&gt;</c>; the receiver resolves it
        /// to its own behaviour (D3). A behaviour name never starts with it.</summary>
        public const string IntentPrefix = "Order.";

        /// <summary>The intent id of order <paramref name="orderName"/>.</summary>
        public static string IntentFor(string orderName) => IntentPrefix + orderName;
    }

    /// <summary>⭐ <c>CE-2140</c> — the map gestures an order DTO may claim, so no order name is written in UI code (AQ90 §2.3k ①).</summary>
    public enum OrderGesture : byte
    {
        /// <summary>Issued only from a menu or HTTP.</summary>
        None = 0,
        /// <summary>A command click on open ground (RTS).</summary>
        GroundClick = 1,
        /// <summary>A command click on a hostile entity (RTS).</summary>
        EnemyClick = 2,
    }
}
