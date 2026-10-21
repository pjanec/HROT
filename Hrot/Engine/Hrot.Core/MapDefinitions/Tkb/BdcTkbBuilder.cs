using System;
using System.Collections.Generic;
using CarKinem.Core;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Tkb.Parameters;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;

namespace Hrot.Map.Definitions.Tkb
{
    public class NedTkbBuilder
    {
        private readonly TkbDatabase _db;
        
        public NedTkbBuilder(TkbDatabase db)
        {
            _db = db;
        }
        
        /// <summary>
        /// Define new vehicle entity type.
        /// </summary>
        public NedTkbBuilder DefineVehicle(long tkbId, string name)
        {
            var template = new TkbTemplate(name, tkbId);
            template.AddDescriptor(new TkbMasterDto { CustomName = name });
            // ⭐ Birth-criticality is DERIVED from [BirthCritical] on the component type (2026-09-13) —
            //   it is no longer declared per template. 📄 docs/designs/tkb-1/DESIGN.md §6.6a.
            //
            // ⭐⭐⭐ CE-265 — and so are the MANDATORY (promotion-gate) components. This method used to
            //   declare EntityInfo + SimTransform hard by hand; MandatoryComponentResolver now derives
            //   exactly that pair for this template shape, from [PerInstanceValue] ∩ what these descriptors
            //   produce ∩ what the host can ingress ∩ what it registers. ⛔ Restating them here would be
            //   the drift the derivation removes — UrbanCombatTkbCatalog's five identically-shaped
            //   templates declared NONE of it. 📄 §6.6b.
            _db.Register(template);
            return this;
        }
        
        /// <summary>⭐ Weapon systems WS-4 — keeps the type out of the Add Entity picker (it is still created — by a preset, a scenario, a
        /// request — and drawn as whatever its family is).</summary>
        public NedTkbBuilder HiddenFromPalette(long tkbId)
        {
            var template = _db.GetByType(tkbId) ?? throw new InvalidOperationException($"Template {tkbId} not found");
            var master = template.GetDescriptor<TkbMasterDto>() ?? new TkbMasterDto();
            template.AddDescriptor(master with { HideFromPalette = true });
            return this;
        }

        /// <summary>
        /// Assigns a DIS Entity Type to the template.
        /// The type is stamped onto the entity header via
        /// <see cref="Fdp.Interfaces.TkbTemplate.DisType"/> when the blueprint is applied,
        /// enabling the <c>MapLayerAssignmentSystem</c> to classify the entity into the
        /// correct rendering layer without string look-ups in the hot path.
        /// </summary>
        /// <param name="tkbId">The TKB type identifier to look up.</param>
        /// <param name="disType">The DIS entity type to assign.</param>
        public NedTkbBuilder WithDisType(long tkbId, DISEntityType disType)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            template.DisType = disType;
            // ⭐ CE-1017 S0 — the master descriptor carries the same type as text (what a TKB JSON file authors), so the
            //   template field and the descriptor can never disagree.
            var master = template.GetDescriptor<TkbMasterDto>();
            if (master != null) template.AddDescriptor(master with { DisType = disType.ToString() });
            return this;
        }

        /// <summary>
        /// ⭐ <c>CE-2139</c> (C3, rule K2, R-278 (behaviors)) — gives a COMPOSITE its unit DIS type: Kind 11, land, the arm, and
        /// the echelon READ FROM its <see cref="TkbCompositionDef.Echelon"/>, so the echelon is stated once. Call after
        /// <see cref="AsComposite"/>.
        /// </summary>
        public NedTkbBuilder AsLandUnit(long tkbId, Hrot.Core.Tkb.UnitArm arm, ushort country, byte variant = 1)
        {
            var template = _db.GetByType(tkbId) ?? throw new InvalidOperationException($"Template {tkbId} not found");
            var composition = template.GetDescriptor<TkbCompositionDef>()
                ?? throw new InvalidOperationException($"Template {tkbId} is not a composite: AsLandUnit needs AsComposite first.");
            return WithDisType(tkbId, Hrot.Core.Tkb.UnitDis.Land(arm, Hrot.Core.Tkb.UnitDis.EchelonOf(composition.Echelon), country, variant));
        }

        /// <summary>
        /// Add visual properties — the symbol, colour, model and label the map draws from.
        /// </summary>
        /// <remarks>
        /// ⭐⭐⭐ <b><c>CE-118</c> / <c>UXI-23 S1</c>, fixed 2026-08-28.</b> 🔴 This method used to
        /// <b>discard everything it was given</b>: it resolved the template, never called
        /// <paramref name="configure"/> at all, and returned — under the comment
        /// <i>"VisualData ECS component will be applied by IG-side translator in Phase 6."</i>
        /// ⛔ <b>Phase 6 never happened</b>, so all nine catalog call sites authored real symbol codes,
        /// colours, models and scales into a delegate that was never invoked.
        ///
        /// <para>📐 <b>Measured consequence.</b> <c>VisualDefinitionDto</c> was produced by NOTHING in
        /// the entire repository, which made <c>PresentationTkbTranslator</c> — its only consumer —
        /// permanently inert on <i>every</i> host. No entity built from the TKB has ever carried
        /// <c>VisualData</c>; the only entities that had it were those whose <c>scenario.json</c>
        /// authored it directly. On the <c>SimHost</c> perspective, which builds from the TKB, that
        /// left the shared entity gizmos with nothing to draw.</para>
        ///
        /// <para>⚠ <b>This is the identical defect as <c>WithPhysics</c>'s dropped
        /// <c>Height</c>/<c>TurnRate</c>/<c>Mobility</c></b> (<c>CE-113</c>), with the same
        /// "Phase 6" comment — 📌 two instances of one pattern: a builder method that takes authored
        /// data, resolves the template, and forgets to attach a descriptor. ⭐ Neither failed loudly,
        /// because an absent descriptor is indistinguishable from an unauthored one.</para>
        /// </remarks>
        public NedTkbBuilder WithVisual(long tkbId, Action<IgVisualDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            var visualDef = new IgVisualDef();
            configure(visualDef);

            template.AddDescriptor(new VisualDefinitionDto
            {
                SymbolCode   = visualDef.SymbolCode,
                ModelPath    = visualDef.ModelPath,
                ColorHex     = visualDef.ColorHex,
                Scale        = visualDef.Scale,
                ShowLabel    = visualDef.ShowLabel,
                MapShapeName = visualDef.MapShapeName,
                IconName     = visualDef.IconName,   // CE-1017 S0
            });
            // IgVisualDef.LayerName is deliberately NOT carried across: layer membership is
            // COMPUTED by MapLayerAssignmentSystem from the entity's DIS type and components,
            // not declared per TKB type. A second, authored source for the same fact would be
            // the two-producer hazard CE-113 was about. If per-type layer overrides are ever
            // wanted, they belong in the S4 layer configuration, not in this descriptor.
            return this;
        }
        
        /// <summary>
        /// Add physics properties (SimHost).
        /// </summary>
        public NedTkbBuilder WithPhysics(long tkbId, Action<SimVehicleDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            var physicsDef = new SimVehicleDef();
            configure(physicsDef);

            template.AddDescriptor(new VehicleParametersDto
            {
                Mass        = physicsDef.Mass,
                Length      = physicsDef.Length,
                Width       = physicsDef.Width,
                MaxSpeedFwd = physicsDef.MaxSpeed,
                MaxSpeedRev = physicsDef.MaxSpeedRev,
                MaxAccel    = physicsDef.Acceleration,
                WalkSpeed   = physicsDef.WalkSpeed,   // ⭐ CE-1208
                TurnRate    = physicsDef.TurnRate,
                AvoidanceRadius = physicsDef.AvoidanceRadius,   // ⭐ CE-2336 (R-2065)
                VehicleClass = physicsDef.Preset ?? MapMobility(physicsDef.Mobility),   // ⭐ M9 — a stated preset wins
            });
            // ⭐ B8 (M6, DESIGN_Armour_Hit_Model §2) — the hull's SIZE, engine-neutral (Body.Geometry, ground-centred): it gives the
            //   collider its height (VehicleKinematicsTkbTranslator). 📐 Measured 2026-10-14 (hill-attack-close): without it every NED
            //   hull had collider height 0 — no M1 height band, and the blast rule took a tank for a person, so the AI's round choice
            //   picked HEAT-MP (blast "kills" a 50 hp tank) over APFSDS and the fight stalled.
            if (physicsDef.Height > 0f && physicsDef.Mobility != TerrainMobility.Infantry && template.GetDescriptor<BodyGeometryDto>() == null)   // a person's profile is his posture
                template.AddDescriptor(new BodyGeometryDto
                {
                    ReferencePoint = ReferencePointKind.GroundCentre,
                    Length = physicsDef.Length, Width = physicsDef.Width, Height = physicsDef.Height, BodyCentreZ = physicsDef.Height / 2f,
                });
            // ⭐ CE-2168 (Motion Domains MD-A) — how it moves, stated: every ground mobility maps 1:1
            template.AddDescriptor(new LocomotionDto { Class = physicsDef.Mobility switch
            {
                TerrainMobility.Tracked  => LocomotionClass.Tracked,
                TerrainMobility.Infantry => LocomotionClass.Pedestrian,
                _                        => LocomotionClass.Wheeled,
            } });
            // Height stays out of VehicleParametersDto on purpose -- nothing on the
            // kinematics path consumes it (VehicleParams has no height field,
            // PhysicsCollider carries only Radius).  Its home is the render/collider
            // descriptor, StrideRenderModelDefDto.ShapeHeight.
            // FuelCapacity / FuelConsumption likewise have no consumer yet.
            return this;
        }
        
        /// <summary>
        /// ⭐ <c>CE-2151</c> (R-274 / R-2009) — the type CARRIES: its seats (<c>Gen.Carrier</c>). A type without this boards nobody.
        /// </summary>
        public NedTkbBuilder WithCarrier(long tkbId, Fdp.Toolkit.Tkb.Domain.CarrierDefDto carrier)
        {
            var template = _db.GetByType(tkbId)
                ?? throw new InvalidOperationException($"Template {tkbId} not found");
            template.AddDescriptor(carrier);
            return this;
        }

        /// <summary>⭐ Airborne insertion B-5 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §4) — the type JUMPS: its parachute
        /// (<c>Gen.Parachute</c>). A type without one that is made to jump gets a round canopy's defaults.</summary>
        public NedTkbBuilder WithParachute(long tkbId, Fdp.Toolkit.Tkb.Domain.ParachuteDto parachute)
        {
            var template = _db.GetByType(tkbId)
                ?? throw new InvalidOperationException($"Template {tkbId} not found");
            template.AddDescriptor(parachute);
            return this;
        }

        /// <summary>⭐ Airborne insertion B-7 (§7d) — the type's crew can EJECT (<c>Gen.Ejection</c>): the pilot's type, the seat, the
        /// auto-eject health share.</summary>
        public NedTkbBuilder WithEjection(long tkbId, Fdp.Toolkit.Tkb.Domain.EjectionDto ejection)
        {
            var template = _db.GetByType(tkbId)
                ?? throw new InvalidOperationException($"Template {tkbId} not found");
            template.AddDescriptor(ejection);
            return this;
        }

        /// <summary>
        /// ⭐ <c>CE-2155</c> (R-263 D1 / R-2011) — the type's people get HURT before they die (<c>Gen.Injury</c>): wounded, then incapacitated
        /// and bleeding out. A type without this is ok until dead.
        /// </summary>
        /// <summary>⭐ CE-3277 — what a person carries in his pockets (binoculars, a phone — HeldItems, CE-3234); none ⇒ nothing.</summary>
        public NedTkbBuilder WithPocketItems(long tkbId, Fdp.Toolkit.Tkb.Domain.PocketItemsDto pockets)
        {
            var template = _db.GetByType(tkbId)
                ?? throw new InvalidOperationException($"Template {tkbId} not found");
            if (pockets.Binoculars || pockets.Phone) template.AddDescriptor(pockets);
            return this;
        }

        public NedTkbBuilder WithInjury(long tkbId, Fdp.Toolkit.Tkb.Domain.InjuryDefDto injury)
        {
            var template = _db.GetByType(tkbId)
                ?? throw new InvalidOperationException($"Template {tkbId} not found");
            template.AddDescriptor(injury);
            return this;
        }

        /// <summary>
        /// Add combat properties (future).
        /// </summary>
        public NedTkbBuilder WithCombat(long tkbId, Action<SimCombatDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            var combatDef = new SimCombatDef();
            configure(combatDef);

            // Store the combat definition as a descriptor for inspector / ORBAT display.
            template.AddDescriptor(combatDef);

            // Typed DTO consumed by CombatTkbTranslator.
            // Derive max health from armor: ArmorFront * 5 gives roughly 500 HP for a 100 mm armour.
            // Entities without armour default to 100 HP.
            // ⭐ Stage 0 — the formulas live in EngineFallbacks and every derived number is RECORDED, so /tkb/resolve
            //   reports it as Generated(formula, inputs) rather than as if an author had stated it.
            var generated = new TkbGeneratedValuesDto();
            // ⭐ M6 (D-F) — a type that STATES its health keeps it; the front × 5 derivation is only the fallback (and only then recorded)
            if (combatDef.MaxHealth <= 0f)
                generated.Formulas[ParameterNames.MaxHealth] = combatDef.ArmorFront > 0f
                    ? $"armourFront × {EngineFallbacks.HealthPerArmourMm} (armourFront = {combatDef.ArmorFront})"
                    : $"no armour ⇒ {EngineFallbacks.HealthWithoutArmour}";
            template.AddDescriptor(new CombatPlatformDefDto
            {
                MaxHealth  = combatDef.MaxHealth > 0f ? combatDef.MaxHealth : EngineFallbacks.HealthFromArmour(combatDef.ArmorFront),
                MobilityKillBelowFraction = combatDef.MobilityKillBelowFraction,
                ArmorFront = combatDef.ArmorFront,
                ArmorSide  = combatDef.ArmorSide,
                ArmorRear  = combatDef.ArmorRear,
                // ⭐ M6 — the roof and the shaped-charge column (0 ⇒ the rear / the kinetic value, ArmorModel.ArmourFor)
                ArmorTop     = combatDef.ArmorTop,
                ArmorFrontCe = combatDef.ArmorFrontCe,
                ArmorSideCe  = combatDef.ArmorSideCe,
                ArmorRearCe  = combatDef.ArmorRearCe,
                ArmorTopCe   = combatDef.ArmorTopCe,
                BlastSusceptibility = combatDef.BlastSusceptibility,   // ⭐ R-2019 D-M2
                PresentedAreaM2 = combatDef.PresentedAreaM2,           // ⭐ CE-3203
            });

            // Derived capability DTO for the general TKB pipeline.
            if (combatDef.Weapons.Count > 0)
            {
                var primary = combatDef.Weapons[0];
                template.AddDescriptor(new WeaponCapabilitiesDto
                {
                    EffectiveRange   = primary.Range,
                    RateOfFire       = primary.RateOfFire,
                    MagazineCapacity = primary.Ammunition,
                });

                // Typed weapon suite consumed by CombatTkbTranslator.
                var suite = new WeaponSuiteDto();
                foreach (var wm in combatDef.Weapons)
                {
                    if (wm.MuzzleVelocity <= 0f)   // ⭐ CE-3233 — a stated muzzle speed is data, not a formula
                        generated.Formulas[ParameterNames.Mount(suite.Mounts.Count, ParameterNames.MuzzleVelocity)] = wm.Range > 0f
                            ? $"range × {EngineFallbacks.MuzzleVelocityPerRangeMetre} (range = {wm.Range})"
                            : $"no range ⇒ {EngineFallbacks.MuzzleVelocity}";
                    suite.Mounts.Add(new WeaponMountDto
                    {
                        InitialAmmunition = wm.Ammunition,
                        MuzzleVelocity    = wm.MuzzleVelocity > 0f ? wm.MuzzleVelocity : EngineFallbacks.MuzzleVelocityFromRange(wm.Range),   // ⭐ CE-3233
                        // ⭐ CE-3071 (A4) — each mount keeps its OWN range and munition (the TOW is not the 25 mm).
                        Range             = wm.Range,
                        Penetration       = wm.Penetration,
                        DamagePerHit      = wm.DamagePerHit,
                        DispersionMils    = wm.DispersionMils,   // ⭐ AQ85 C
                        Category          = wm.Category,             // ⭐ WL W-5
                        TurretIndex       = wm.TurretIndex ?? -1,    // ⭐ WL-1 W-9
                        MuzzleOffsetX     = wm.MuzzleOffset.X,       // ⭐ E4 VE-K
                        MuzzleOffsetY     = wm.MuzzleOffset.Y,
                        MuzzleOffsetZ     = wm.MuzzleOffset.Z,
                        AmmoGuid          = unchecked((ulong)wm.AmmoTkb),        // ⭐ LR-1 (R-271)
                        TracerAmmoGuid    = unchecked((ulong)wm.TracerAmmoTkb),
                        TracerEvery       = wm.TracerEvery,
                        // ⭐ M5 — the loadout, firing on the move, the aim / lock time and the magazine
                        Loadout           = wm.Loadout?.ConvertAll(l => new AmmoLoadDto { AmmoGuid = unchecked((ulong)l.AmmoTkb), Rounds = l.Rounds }) ?? new(),
                        FireOnMove        = wm.FireOnMove,
                        AimSeconds        = wm.AimSeconds,
                        MagazineSize      = wm.MagazineSize,
                        ReloadSeconds     = wm.ReloadSeconds,
                        FixedConeDeg      = wm.FixedConeDeg,   // ⭐ Air Ops 6a (AW-1) — a fixed gun / pod fires inside its nose cone
                        ScopeMagnification = wm.ScopeMagnification,   // ⭐ CE-3254 (BR-9) — the scope on the mount
                        ScopeFovDegrees   = wm.ScopeFovDegrees,
                        Arc               = wm.Arc,            // ⭐ IF-1 (I2) — the indirect arcs and the elevation limits
                        MinElevationDeg   = wm.MinElevationDeg,
                        MaxElevationDeg   = wm.MaxElevationDeg,
                        RateOfFire        = wm.RateOfFire,       // ⭐ CE-2188 (BS-1 §5.4c) — a dispenser's release interval
                        Appearance        = wm.Look is { } look ? new ItemAppearanceDto { Look = look } : null,   // ⭐ CE-3233 (B-11, ST-7)
                    });
                }
                template.AddDescriptor(suite);
            }

            // Perception DTO from sensor range (if specified).
            if (combatDef.SensorRange > 0f)
            {
                template.AddDescriptor(new SensorCapabilitiesDto
                {
                    VisionRange        = combatDef.SensorRange,
                    HearingRange       = combatDef.SensorRange * 1.5f,
                    FieldOfViewDegrees = 360f,
                    EyeHeightStanding  = combatDef.EyeHeight,   // ⭐ M5 — a vehicle sees from its sight (0 ⇒ a standing man's)
                });
            }

            template.AddDescriptor(generated);   // ⭐ Stage 0 — provenance of the derived numbers above

            // ECS components (PerceptionReceptor, WeaponState, Health, PhysicsCollider)
            // will be stamped by translators in Phase 6.

            return this;
        }

        /// <summary>
        /// Add force affiliation for perception/combat systems.
        /// </summary>
        /// <summary>⭐ CE-1041 — the body's geometry relative to its reference point (size, box offset, ground contacts).
        /// 📄 docs/DESIGN_Body_Geometry_And_Ground_Contact.md.</summary>
        public NedTkbBuilder WithBodyGeometry(long tkbId, BodyGeometryDto geometry)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            template.AddDescriptor(geometry ?? throw new ArgumentNullException(nameof(geometry)));
            return this;
        }

        /// <summary>⭐ WL-1 (docs/DESIGN_Weapon_Layers.md W-9) — the platform's turrets, index 0 first; each becomes a turret part
        /// child on every node. Weapons name theirs with <see cref="WeaponMount.TurretIndex"/>.</summary>
        public NedTkbBuilder WithTurrets(long tkbId, params TurretDto[] turrets)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            template.AddDescriptor(new TurretSuiteDto { Turrets = new List<TurretDto>(turrets ?? Array.Empty<TurretDto>()) });
            return this;
        }

        /// <summary>⭐ M7 / CE-2168 (Motion Domains MD-A) — a FLYER: <c>Gen.Locomotion</c> (<paramref name="cls"/>, an air class) and
        /// <c>Gen.Flight</c>. ⛔ Not <see cref="WithPhysics"/> — that is ground physics.</summary>
        public NedTkbBuilder WithFlight(long tkbId, LocomotionClass cls, FlightParamsDto flight)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            template.AddDescriptor(new LocomotionDto { Class = cls });
            template.AddDescriptor(flight ?? throw new ArgumentNullException(nameof(flight)));
            return this;
        }

        /// <summary>⭐ Weapon systems WS-1 (R-2016) — a WEAPON SYSTEM: an entity of its own (<c>Gen.WeaponSystem</c>), placed, mounted,
        /// carried, deployed, manned. Its gun is the type's own <see cref="WithCombat"/> mount 0, its pintle <see cref="WithTurrets"/>.</summary>
        public NedTkbBuilder WithWeaponSystem(long tkbId, Fdp.Toolkit.Tkb.Domain.WeaponSystemDto weaponSystem)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            template.AddDescriptor(weaponSystem ?? throw new ArgumentNullException(nameof(weaponSystem)));
            return this;
        }

        /// <summary>⭐ M9 — what the type gives off to sensors (<c>CE-2106</c>: the sound it makes moving and firing).</summary>
        public NedTkbBuilder WithSignatures(long tkbId, SignaturesDto signatures)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            template.AddDescriptor(signatures ?? throw new ArgumentNullException(nameof(signatures)));
            return this;
        }

        public NedTkbBuilder WithFaction(long tkbId, byte factionId)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            // ⭐⭐ CE-3051 — the faction reaches the BEHAVIOUR PROFILE, which is what stamps EntityInfo.ForceId on a node with
            //    no presentation tier (SimHost). 🔴 This used to be a no-op ("stamped by translator in Phase 6", which never
            //    landed — the WithHeavyMemory pattern below): CGF derived the force from the map symbol (M1 = Friend, T-72 =
            //    Hostile) while SimHost left every unit Neutral, so vision — which ignores its own force — saw nobody on a
            //    cluster. Found by the CE-3050 rail. Either call order works.
            _factions[tkbId] = (ForceId)factionId;
            var profile = template.GetDescriptor<BehaviorProfileDto>();
            if (profile != null) template.AddDescriptor(profile with { Faction = (ForceId)factionId });
            return this;
        }

        private readonly System.Collections.Generic.Dictionary<long, ForceId> _factions = new();

        /// <summary>
        /// Stamps the navigation and behavior-brain components required for the
        /// CarKinem locomotion and FDP BTree behavior systems to process this entity.
        ///
        /// Must be called for every vehicle that should accept mission assignments
        /// (e.g. WanderMilitary behavior via <c>MissionControlRequest</c>).
        ///
        /// Components added (all zero-initialised; systems populate them at runtime):
        /// <list type="bullet">
        ///   <item><see cref="VehicleState"/> / <see cref="NavState"/> — CarKinem kinematics.</item>
        ///   <item><see cref="SimVelocity"/> — world-space velocity written by locomotion.</item>
        ///   <item><see cref="BehaviorState"/> (BrainTier = BTree) — active behavior hash.</item>
        ///   <item><see cref="MissionPlanQueue"/> — phase queue maintained by MissionAdapterSystem.</item>
        ///   <item><see cref="BrainBTreeState"/> / <c>BrainBlackboard</c> — brain execution state.</item>
        ///   <item><see cref="LocomotionChannel"/> / <see cref="WeaponChannel"/> / <see cref="InteractionChannel"/> — action dispatch channels.</item>
        ///   <item><see cref="ActorCapabilityState"/> (CanMove | CanShoot) — capability bits.</item>
        /// </list>
        /// </summary>
        /// <remarks>⭐ CE-1118 H-3 (Airborne Insertion §6.5 D-23) — <paramref name="defaultSop"/>: the type's own standing orders (a SPAWNED
        /// unit — the ejected pilot — gets no scenario task, so its type must give it one).</remarks>
        public NedTkbBuilder WithBehavior(long tkbId, string? defaultSop = null)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            template.AddDescriptor(new BehaviorProfileDto
            {
                SimTier    = BehaviorConstants.SimTierTactical,
                BrainTier  = BehaviorConstants.BrainTierBTree,
                CanMove    = true,
                CanShoot   = true,
                CanInteract = true,
                Faction    = _factions.TryGetValue(tkbId, out var faction) ? faction : ForceId.Neutral,   // CE-3051
                DefaultSop = defaultSop,
            });

            return this;
        }

        // ⛔⛔ `WithHeavyMemory(long)` WAS HERE — deleted by `P4`-⑥ (`2026-09-22`).
        //
        // 🔴 It was a NO-OP WITH TWO LIVE CALL SITES. Its body looked the template up, threw if it
        //    was missing, and returned `this`; the one line that would have done the work was a
        //    comment — "Blackboard1024 ECS component will be applied by translator in Phase 6" —
        //    and that Phase 6 never landed. ⚠ It survived the P4-① deletion untouched precisely
        //    BECAUSE it never referenced the type in code, only in prose.
        //
        // ⭐ Its INTENT — "this template's commander needs heavy working memory" — is not lost, it
        //    is SUBSUMED: commander working state now lives in the occurrence store, whose tier
        //    components are registered Hrot-wide (BlueprintBlackboardTiers.RegisterAll), so a
        //    template does not declare the need at all. 📄 §30.13, §30.15.
        
        /// <summary>
        /// Add composite (ORBAT) definition.
        /// </summary>
        public NedTkbBuilder AsComposite(long tkbId, Action<TkbCompositionDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            // ⭐⭐ CE-265 — the "ensure EntityInfo is mandatory" block that stood here is GONE. It existed
            //   because a composite could be built on a template that DefineVehicle had not touched; the
            //   derivation is keyed on the template's DESCRIPTORS rather than on which builder method ran,
            //   so it covers that case without the existence check — and without the drift a hand-written
            //   check invites. 📄 docs/designs/tkb-1/DESIGN.md §6.6b.

            // Evaluate composition immediately to populate TkbTemplate metadata.
            var compositionDef = new TkbCompositionDef();
            configure(compositionDef);

            if (compositionDef.AutoCreateChildren)
            {
                foreach (var slot in compositionDef.Subordinates)
                {
                    for (int i = 0; i < slot.Count; i++)
                    {
                        template.ChildBlueprints.Add(new ChildBlueprintDefinition(
                            instanceId:   template.ChildBlueprints.Count + 1,
                            childTkbType: slot.TkbType,
                            designation:  slot.Designation
                        ));
                    }
                }
            }

            // Store as descriptor instead of managed component.
            template.AddDescriptor(compositionDef);
            return this;
        }

        /// <summary>
        /// Maps HROT's authoring-level <see cref="TerrainMobility"/> onto the FDP
        /// kinematics <see cref="VehicleClass"/> that selects a
        /// <see cref="VehiclePresets"/> baseline.
        /// </summary>
        /// <remarks>
        /// This half of the mapping stays here because <see cref="TerrainMobility"/> is an
        /// <c>Hrot.Core</c> concept and <c>Fdp.Toolkits</c> -- which owns the DTO and the
        /// consuming translator -- cannot reference it.  The other half (preset baseline
        /// plus per-field overrides) now lives in <c>VehicleKinematicsTkbTranslator</c>,
        /// the single writer of <c>VehicleParams</c> on the cluster path.
        /// <para>
        /// The mapping is lossy -- <c>Air</c> and <c>Naval</c> both collapse to
        /// <c>PersonalCar</c> -- but that loss predates this routing and is unchanged.
        /// </para>
        /// </remarks>
        internal static VehicleClass MapMobility(TerrainMobility mobility) => mobility switch
        {
            TerrainMobility.Tracked  => VehicleClass.Tank,
            TerrainMobility.Wheeled  => VehicleClass.Truck,
            TerrainMobility.Infantry => VehicleClass.Pedestrian,
            // ⭐ CE-2168 (MD-A) — the lossy "Air / Naval → PersonalCar" mapping ends: a flyer or a boat is not ground physics — it states
            //   Gen.Locomotion and its domain's own parameters (Gen.Flight) instead. Fail loudly rather than drive an aircraft like a car.
            TerrainMobility.Air or TerrainMobility.Naval => throw new NotSupportedException(
                $"{mobility} is not a ground mobility: state Gen.Locomotion and its domain's parameters (DESIGN_Motion_Domains.md MD-A)"),
            _                        => VehicleClass.PersonalCar
        };
    }
}
