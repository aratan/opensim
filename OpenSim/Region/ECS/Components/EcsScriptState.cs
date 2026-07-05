using Friflo.Engine.ECS;

namespace OpenSim.Region.ECS.Components
{
    /// <summary>
    /// ECS component tracking script state for scene objects.
    /// Minimal placeholder — expanded when script systems are
    /// migrated to ECS (Phase 4+).
    /// </summary>
    public struct EcsScriptState : IComponent
    {
        public int RunningScriptCount;
        public bool HasChanged;
        public long LastEventTick;

        public static readonly EcsScriptState Default = new()
        {
            RunningScriptCount = 0,
            HasChanged = false,
            LastEventTick = 0
        };
    }
}
