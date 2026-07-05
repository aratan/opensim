using Friflo.Engine.ECS;
using OpenMetaverse;

namespace OpenSim.Region.ECS.Components
{
    /// <summary>
    /// ECS component holding physics state for avatars and prims.
    /// Used by BepuPhysics integration in Phase 2.
    /// </summary>
    public struct EcsPhysics : IComponent
    {
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public float Mass;
        public bool IsPhysical;
        public bool IsAvatar;
        public bool Flying;
        public bool InWater;

        /// <summary>
        /// BepuPhysics BodyHandle — populated during Phase 2.
        /// -1 means no body created yet.
        /// </summary>
        public long PhysicsBodyHandle;

        public EcsPhysics(
            Vector3 velocity, Vector3 angularVelocity, float mass,
            bool isPhysical, bool isAvatar)
        {
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            Mass = mass;
            IsPhysical = isPhysical;
            IsAvatar = isAvatar;
            Flying = false;
            InWater = false;
            PhysicsBodyHandle = -1;
        }

        public static readonly EcsPhysics Default = new()
        {
            Velocity = Vector3.Zero,
            AngularVelocity = Vector3.Zero,
            Mass = 1f,
            IsPhysical = false,
            IsAvatar = false,
            Flying = false,
            InWater = false,
            PhysicsBodyHandle = -1
        };
    }
}
