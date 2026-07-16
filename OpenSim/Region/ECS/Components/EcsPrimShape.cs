using Friflo.Engine.ECS;
using OpenMetaverse;
using OpenSim.Framework;

namespace OpenSim.Region.ECS.Components
{
    /// <summary>
    /// ECS component holding the visual/shape properties
    /// of a scene object part (prim).
    /// </summary>
    public struct EcsPrimShape : IComponent
    {
        public UUID Uuid;
        public uint LocalId;
        public PrimitiveBaseShape Shape;
        public Vector3 Scale;

        public EcsPrimShape(UUID uuid, uint localId, PrimitiveBaseShape shape, Vector3 scale)
        {
            Uuid = uuid;
            LocalId = localId;
            Shape = shape;
            Scale = scale;
        }
    }
}
