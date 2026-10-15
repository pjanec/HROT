using System.Collections.Generic;
using System.Numerics;

namespace GizmoMap.Presentation.Shapes
{
    public sealed class DefaultEntityShapeLibrary : IEntityShapeLibrary
    {
        public const string GroundVehicle = "ground_vehicle";
        public const string Humanoid = "humanoid";
        public const string FixedWing = "fixed_wing";
        public const string RotaryWing = "rotary_wing";
        /// <summary>⭐ <c>CE-3148</c> — a cultural feature (DIS kind 5: a sandbag wall, a concrete block, a crate): its footprint outline.</summary>
        public const string CulturalFeature = "cultural_feature";
        /// <summary>The name of the empty profile returned when nothing matches (drawn as nothing).</summary>
        public const string Fallback = "_fallback";

        private readonly Dictionary<string, EntityShapeProfile> _profiles = new();

        public DefaultEntityShapeLibrary()
        {
            _profiles[GroundVehicle] = BuildGroundVehicle();
            _profiles[Humanoid] = BuildHumanoid();
            _profiles[FixedWing] = BuildFixedWing();
            _profiles[RotaryWing] = BuildRotaryWing();
            _profiles[CulturalFeature] = BuildCulturalFeature();
        }

        public EntityShapeProfile GetShape(string? shapeName, ulong fallbackDisType)
        {
            if (!string.IsNullOrEmpty(shapeName) && _profiles.TryGetValue(shapeName, out var named))
                return named;

            // DISEntityType packing: Kind bits 56-63, Domain 48-55, Country 32-47, Category 24-31.
            // ⛔ CE-2127 — Category was read from bits 40-47 (Country's high byte), so every US/RU aircraft read category 0
            //   and drew fixed-wing, and the rotary test (>= 20) also caught trainers, UAVs and airliners (40, 50, 57).
            byte kind = (byte)(fallbackDisType >> 56);
            byte domain = (byte)((fallbackDisType >> 48) & 0xFF);
            byte cat = (byte)((fallbackDisType >> 24) & 0xFF);

            if (kind == 1)
            {
                if (domain == 1) return _profiles[GroundVehicle];
                if (domain == 2) return cat is >= 20 and <= 25 ? _profiles[RotaryWing] : _profiles[FixedWing];
            }
            else if (kind == 3)
            {
                return _profiles[Humanoid];
            }
            else if (kind == 5)
            {
                return _profiles[CulturalFeature];   // ⭐ CE-3148 — it drew the magenta emergency box
            }

            return new EntityShapeProfile { Name = Fallback };
        }

        /// <summary>The built-in profile named <paramref name="name"/> (one of the public constants), if any.</summary>
        public bool TryGetProfile(string name, out EntityShapeProfile profile) => _profiles.TryGetValue(name, out profile!);

        private static EntityShapeProfile BuildGroundVehicle()
        {
            return new EntityShapeProfile
            {
                Name = GroundVehicle,
                Elements = new[]
                {
                    new PolylineDefinition
                    {
                        LocalVertices = new[]
                        {
                            new Vector3(-0.5f, -0.35f, 0f), new Vector3(0.5f, -0.35f, 0f),
                            new Vector3(0.5f, 0.35f, 0f), new Vector3(-0.5f, 0.35f, 0f)
                        },
                        IsClosed = true, IsFilled = false, LineThickness = 1f
                    }
                }
            };
        }

        /// <summary>⭐ <c>CE-3148</c> — the whole footprint (length × width), closed: an obstacle is the box it is.</summary>
        private static EntityShapeProfile BuildCulturalFeature()
        {
            return new EntityShapeProfile
            {
                Name = CulturalFeature,
                Elements = new[]
                {
                    new PolylineDefinition
                    {
                        LocalVertices = new[]
                        {
                            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                            new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
                        },
                        IsClosed = true, IsFilled = false, LineThickness = 1f
                    }
                }
            };
        }

        private static EntityShapeProfile BuildHumanoid()
        {
            return new EntityShapeProfile
            {
                Name = Humanoid,
                Elements = new[]
                {
                    new PolylineDefinition
                    {
                        LocalVertices = new[]
                        {
                            new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f)
                        },
                        IsClosed = false, IsFilled = false, LineThickness = 1f
                    }
                }
            };
        }

        private static EntityShapeProfile BuildFixedWing()
        {
            return new EntityShapeProfile
            {
                Name = FixedWing,
                Elements = new[]
                {
                    new PolylineDefinition
                    {
                        LocalVertices = new[]
                        {
                            new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                            new Vector3(0f, -0.15f, 0f), new Vector3(0f, 0.15f, 0f)
                        },
                        IsClosed = false, IsFilled = false, LineThickness = 1f
                    }
                }
            };
        }

        /// <summary>⭐ CE-2127 — a fuselage and tail boom under a rotor disc. 🔴 Was a placeholder returning the fixed-wing outline
        /// (and its name), so a helicopter could not draw as one even once its category was read right.</summary>
        private static EntityShapeProfile BuildRotaryWing()
        {
            const int segments = 12;
            var disc = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = i * 2f * System.MathF.PI / segments;
                disc[i] = new Vector3(0.4f * System.MathF.Cos(a), 0.4f * System.MathF.Sin(a), 0f);
            }
            return new EntityShapeProfile
            {
                Name = RotaryWing,
                Elements = new[]
                {
                    new PolylineDefinition
                    {
                        LocalVertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.3f, 0f, 0f) },
                        IsClosed = false, IsFilled = false, LineThickness = 1f
                    },
                    new PolylineDefinition { LocalVertices = disc, IsClosed = true, IsFilled = false, LineThickness = 1f },
                }
            };
        }
    }
}
