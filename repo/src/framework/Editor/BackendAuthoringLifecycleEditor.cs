using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.EditorTools;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Thesis.Orchestration.Editor
{
    [InitializeOnLoad]
    public sealed class BackendAuthoringLifecycleEditor : IPreprocessBuildWithReport
    {
        private static bool isApplying;

        static BackendAuthoringLifecycleEditor()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            EditorApplication.delayCall += () => ApplySelectedBackendAuthoring(saveScenes: false, "on editor load");
        }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            ApplySelectedBackendAuthoring(saveScenes: true, "before build");
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    ApplySelectedBackendAuthoring(saveScenes: ShouldPersistAuthoringBeforePlay(), "before play");
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    EditorApplication.delayCall += () => ApplySelectedBackendAuthoring(saveScenes: false, "after play");
                    break;
            }
        }

        private static bool ShouldPersistAuthoringBeforePlay()
        {
            OrchestrationProjectConfig config =
                Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");

            return config != null &&
                   (config.backendType == OrchestrationBackendType.PUN2 ||
                    config.backendType == OrchestrationBackendType.FishNet ||
                    config.backendType == OrchestrationBackendType.Mirror);
        }

        private static void ApplySelectedBackendAuthoring(bool saveScenes, string reason)
        {
            if (isApplying || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            try
            {
                isApplying = true;
                OrchestrationProjectConfig config =
                    Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");

                bool scenesWereDirtyBefore = AreAnyOpenScenesDirty();

                AutomaticBackendObjectBinding[] bindings = Object.FindObjectsByType<AutomaticBackendObjectBinding>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                foreach (AutomaticBackendObjectBinding binding in bindings)
                {
                    if (binding == null)
                        continue;

                    binding.ApplySelectedBackendInEditor();
                }

                bool skipMirrorPrefabAuthoringDuringBuild = ShouldSkipMirrorPrefabAuthoringDuringBuild(config, reason);
                if (!skipMirrorPrefabAuthoringDuringBuild)
                {
                    SharedAvatarPrefabAuthoringEditor.ApplySelectedBackendAuthoring(config);
                    ConfiguredSharedObjectPrefabAuthoringEditor.ApplySelectedBackendAuthoring(config);
                    QueueRuntimePrefabAuthoringEditor.ApplySelectedBackendAuthoring();
                }

                TryRefreshPunSceneViewAssignments();
                bool scenesDirtyAfterAuthoring = AreAnyOpenScenesDirty();
                bool fishNetSceneAuthoringChanged =
                    config != null &&
                    config.backendType == OrchestrationBackendType.FishNet &&
                    !scenesWereDirtyBefore &&
                    scenesDirtyAfterAuthoring;

                if (config != null &&
                    config.backendType == OrchestrationBackendType.FishNet &&
                    (reason == "before build" || reason == "before play" || fishNetSceneAuthoringChanged))
                {
                    FishNetAuthoringUtilityEditor.ReserializeConfiguredFishNetObjects(config);
                }

                if (!saveScenes)
                    return;

                if (ShouldPersistAuthoringBeforePlay())
                    EditorSceneManager.MarkAllScenesDirty();

                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveOpenScenes();
                Debug.Log($"[BackendAuthoringLifecycleEditor] Reapplied selected backend authoring {reason} and saved open scenes.");
            }
            finally
            {
                isApplying = false;
            }
        }

        private static bool AreAnyOpenScenesDirty()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    return true;
            }

            return false;
        }

        private static bool ShouldSkipMirrorPrefabAuthoringDuringBuild(OrchestrationProjectConfig config, string reason)
        {
            return config != null &&
                   config.backendType == OrchestrationBackendType.Mirror &&
                   reason == "before build" &&
                   BuildPipeline.isBuildingPlayer;
        }

        private static void TryRefreshPunSceneViewAssignments()
        {
            if (!ShouldPersistAuthoringBeforePlay())
                return;

            try
            {
                System.Type handlerType = null;
                foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    handlerType = assembly.GetType("Photon.Pun.PhotonViewHandler", throwOnError: false);
                    if (handlerType != null)
                        break;
                }

                if (handlerType == null)
                    return;

                System.Reflection.MethodInfo refreshMethod = handlerType.GetMethod(
                    "OnHierarchyChanged",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);

                refreshMethod?.Invoke(null, null);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[BackendAuthoringLifecycleEditor] Failed to refresh Photon scene view assignments: {ex.Message}");
            }
        }
    }
}
