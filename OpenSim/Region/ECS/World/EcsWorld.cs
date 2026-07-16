using Friflo.Engine.ECS;
using OpenSim.Region.ECS.Components;
using OpenMetaverse;
using OpenSim.Framework;

namespace OpenSim.Region.ECS.World
{
    /// <summary>
    /// Wraps an Friflo.Engine.ECS EntityStore (the "world") and
    /// provides typed factory methods for creation of region entities.
    /// </summary>
    public class EcsWorld
    {
        public EntityStore Store { get; }
        public ulong CurrentFrame { get; private set; }

        public EcsWorld()
        {
            Store = new EntityStore();
            CurrentFrame = 0;
        }

        /// <summary>
        /// Advance the frame counter. Return the new frame number.
        /// Called once per heartbeat tick.
        /// </summary>
        public ulong Tick() => ++CurrentFrame;

        /// <summary>
        /// Create an ECS entity for a scene object part (prim).
        /// Returns the entity id.
        /// </summary>
        public int CreatePrimEntity(
            UUID uuid, uint localId,
            PrimitiveBaseShape shape,
            Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var entity = Store.CreateEntity();
            entity.Add(new EcsTransform(position, rotation, scale));
            entity.Add(new EcsPrimShape(uuid, localId, shape, scale));
            entity.Add(EcsPhysics.Default);
            entity.Add(EcsNetworkSync.Default);
            entity.Add(EcsScriptState.Default);
            return entity.Id;
        }

        /// <summary>
        /// Create an ECS entity for an avatar (agent presence).
        /// </summary>
        public int CreateAvatarEntity(
            UUID agentId,
            Vector3 position, Quaternion rotation)
        {
            var entity = Store.CreateEntity();
            entity.Add(new EcsTransform(position, rotation, Vector3.One));
            entity.Add(EcsPhysics.Default);
            entity.Add(EcsNetworkSync.Default);
            return entity.Id;
        }

        /// <summary>
        /// Remove an entity by id.
        /// </summary>
        public void RemoveEntity(int entityId)
        {
            if (Store.TryGetEntityById(entityId, out var entity))
                entity.Delete();
        }

        /// <summary>
        /// Query all entities that have both transform and prim shape
        /// (i.e. scene object parts).
        /// </summary>
        public ArchetypeQuery<EcsTransform, EcsPrimShape> QueryPrims()
            => Store.Query<EcsTransform, EcsPrimShape>();

        /// <summary>
        /// Query all entities that have a transform component.
        /// </summary>
        public ArchetypeQuery<EcsTransform> QueryAll()
            => Store.Query<EcsTransform>();
    }
}
