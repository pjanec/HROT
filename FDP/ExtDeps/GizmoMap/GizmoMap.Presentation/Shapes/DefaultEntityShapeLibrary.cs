using System.Collections.Generic;
using System.Numerics;

namespace GizmoMap.Presentation.Shapes
{
    public sealed class DefaultEntityShapeLibrary : IEntityShapeLibrary
    {
        private const string GroundVehicle = "ground_vehicle";
        private const string Humanoid = "humanoid";
        private const string FixedWing = "fixed_wing";
        private const string RotaryWing = "rotary_wing";

        private readonly Dictionary<string, EntityShapeProfile> _profiles = new();

        public DefaultEntityShapeLibrary()
        {
            _profiles[GroundVehicle] = BuildGroundVehicle();
            _profiles[Humanoid] = BuildHumanoid();
            _profiles[FixedWing] = BuildFixedWing();
            _profiles[RotaryWing] = BuildRotaryWing();
        }

        public EntityShapeProfile GetShape(string? shapeName, ulong fallbackDisType)
        {
            if (!string.IsNullOrEmpty(shapeName) && _profiles.TryGetValue(shapeName, out var named))
                return named;

            byte kind = (byte)(fallbackDisType >> 56);
            byte domain = (byte)((fallbackDisType >> 48) & 0xFF);
            byte cat = (byte)((fallbackDisType >> 40) & 0xFF);

            if (kind == 1)
            {
                if (domain == 1) return _profiles[GroundVehicle];
                if (domain == 2) return cat >= 20 ? _profiles[RotaryWing] : _profiles[FixedWing];
            }
            else if (kind == 3)
            {
                return _profiles[Humanoid];
            }

            return new EntityShapeProfile { Name = "_fallback" };
        }

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
                        IsClosed = true, IsFilled = false, LineThickness = 2f
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
                        IsClosed = false, IsFilled = false, LineThickness = 2f
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
                        IsClosed = false, IsFilled = false, LineThickness = 2f
                    }
                }
            };
        }

        private static EntityShapeProfile BuildRotaryWing()
        {
            return BuildFixedWing();
        }
    }
}
