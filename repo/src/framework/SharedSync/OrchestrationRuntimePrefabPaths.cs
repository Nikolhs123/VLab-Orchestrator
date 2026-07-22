namespace Thesis.Orchestration.SharedSync
{
    public static class OrchestrationRuntimePrefabPaths
    {
        public const string ManagersResourcesRoot = "AIMultiplayerAssistant/Managers";

        public const string PUN2QueueRuntimeName = "PUN2QueueRuntime";
        public const string FusionQueueRuntimeName = "FusionQueueRuntime";
        public const string NetcodeQueueRuntimeName = "NetcodeQueueRuntime";

        public const string PUN2QueueRuntimeResourcesPath = ManagersResourcesRoot + "/" + PUN2QueueRuntimeName;
        public const string FusionQueueRuntimeResourcesPath = ManagersResourcesRoot + "/" + FusionQueueRuntimeName;
        public const string NetcodeQueueRuntimeResourcesPath = ManagersResourcesRoot + "/" + NetcodeQueueRuntimeName;

        public const string PUN2QueueRuntimeAssetPath = "Assets/Resources/" + PUN2QueueRuntimeResourcesPath + ".prefab";
        public const string FusionQueueRuntimeAssetPath = "Assets/Resources/" + FusionQueueRuntimeResourcesPath + ".prefab";
        public const string NetcodeQueueRuntimeAssetPath = "Assets/Resources/" + NetcodeQueueRuntimeResourcesPath + ".prefab";
    }
}
