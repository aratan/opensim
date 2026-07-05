using Friflo.Engine.ECS;

namespace OpenSim.Region.ECS.Components
{
    /// <summary>
    /// ECS component tracking network synchronisation state.
    /// Flags whether this entity needs an update pushed to
    /// connected clients via gRPC (Phase 3).
    /// </summary>
    public struct EcsNetworkSync : IComponent
    {
        public bool Dirty;
        public bool NeedsPositionUpdate;
        public bool NeedsFullUpdate;
        public ulong LastUpdateFrame;

        public void MarkDirty(ulong currentFrame)
        {
            Dirty = true;
            NeedsPositionUpdate = true;
            LastUpdateFrame = currentFrame;
        }

        public void MarkFullDirty(ulong currentFrame)
        {
            Dirty = true;
            NeedsPositionUpdate = true;
            NeedsFullUpdate = true;
            LastUpdateFrame = currentFrame;
        }

        public void Clear()
        {
            Dirty = false;
            NeedsPositionUpdate = false;
            NeedsFullUpdate = false;
        }

        public static readonly EcsNetworkSync Default = new()
        {
            Dirty = false,
            NeedsPositionUpdate = false,
            NeedsFullUpdate = false,
            LastUpdateFrame = 0
        };
    }
}
