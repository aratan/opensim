using Friflo.Engine.ECS;
using OpenMetaverse;

namespace OpenSim.Region.ECS.Components
{
    /// <summary>
    /// ECS component holding world-space position, rotation, and scale
    /// of a scene object part.
    /// </summary>
    public struct EcsTransform : IComponent
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        public EcsTransform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public static readonly EcsTransform Identity = new()
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
        };
    }
}
