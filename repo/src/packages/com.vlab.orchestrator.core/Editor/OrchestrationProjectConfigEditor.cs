using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VLabOrchestrator.Runtime;

namespace VLabOrchestrator.Editor
{
    [CustomEditor(typeof(OrchestrationProjectConfig))]
    internal sealed class OrchestrationProjectConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var config = (OrchestrationProjectConfig)target;

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();

            if (config.useCanonicalPaperPreset)
            {
                EditorGUILayout.HelpBox(
                    "Paper preset locking is enabled. Selecting a scenario applies its complete versioned protocol and prevents mixed parameters.",
                    MessageType.Info);
            }

            if (!changed)
                return;

            if (config.useCanonicalPaperPreset)
                config.ApplySelectedPaperPreset();

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            EditorApplication.delayCall += () => SoftwareXRuntimeAuthoring.SyncRuntimeConfiguration(config);
        }
    }

    internal sealed class OrchestrationProjectConfigBuildProcessor : IPreprocessBuildWithReport
    {
        private const string ConfigPath =
            "Assets/Resources/VLabOrchestrator/OrchestrationProjectConfig.asset";

        public int callbackOrder => -10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            OrchestrationProjectConfig config =
                AssetDatabase.LoadAssetAtPath<OrchestrationProjectConfig>(ConfigPath);
            if (config == null)
                return;

            if (config.useCanonicalPaperPreset)
                config.ApplySelectedPaperPreset();

            EditorUtility.SetDirty(config);
            SoftwareXRuntimeAuthoring.SyncRuntimeConfiguration(config);
            AssetDatabase.SaveAssets();
        }
    }
}
