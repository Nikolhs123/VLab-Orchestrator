using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Thesis.Orchestration.Analytics;
using Thesis.Orchestration.Backends.Fusion;
using Thesis.Orchestration.Backends.FishNet;
using Thesis.Orchestration.Backends.Mirror;
using Thesis.Orchestration.Backends.Netcode;
using Thesis.Orchestration.Backends.PUN2;
using Thesis.Orchestration.Editor;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using Thesis.Orchestration.UserSync;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Thesis.Orchestration.EditorTools
{
    public class OrchestrationSetupWindow : EditorWindow
    {
        private OrchestrationProjectConfig config;
        private SerializedObject serializedConfig;
        private Vector2 scrollPosition;
        private BackendEvaluationAnalyzer.Result backendEvaluationResult;
        private bool showBackendEvaluation = true;
        private bool showSharedObjects = true;
        private bool showAiOrchestration;
        private bool showDeveloperOptions;
        private bool showAdvancedScenarioOverrides;
        private string backendEvaluationStatus;
        private string scenarioCatalogStatus;
        private readonly Dictionary<string, BenchmarkScenarioPresetCatalog.Coverage> scenarioCoverage = new();
        private double nextScenarioCoverageRefreshTime;

        private const float HeaderBannerHeight = 130f;

        [MenuItem("Thesis/Orchestration/Setup Window")]
        public static void ShowWindow()
        {
            OrchestrationSetupWindow window = GetWindow<OrchestrationSetupWindow>("Orchestration Setup");
            window.minSize = new Vector2(760f, 620f);
        }

        private void OnEnable()
        {
            minSize = new Vector2(760f, 620f);
            TryAutoLoadConfigFromResources();
        }

        private void OnGUI()
        {
            TryAutoLoadConfigFromResources();
            DrawTopBannerSpace();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Space(8);
            EditorGUILayout.LabelField("Orchestration Framework Setup", EditorStyles.boldLabel);
            GUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            config = (OrchestrationProjectConfig)EditorGUILayout.ObjectField(
                "Project Config",
                config,
                typeof(OrchestrationProjectConfig),
                false);

            if (EditorGUI.EndChangeCheck())
            {
                serializedConfig = config != null ? new SerializedObject(config) : null;
            }

            if (config == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign or create an OrchestrationProjectConfig asset first.",
                    MessageType.Info);

                GUILayout.Space(6);

                if (GUILayout.Button("Create Project Config Asset", GUILayout.Height(28)))
                {
                    CreateConfigAsset();
                }

                EditorGUILayout.EndScrollView();
                return;
            }

            if (serializedConfig == null || serializedConfig.targetObject != config)
            {
                serializedConfig = new SerializedObject(config);
            }

            OrchestrationInstaller.InitializeNewConfigAsset(config);
            serializedConfig.Update();

            DrawProjectOverview();
            DrawSharedObjectsPanel();
            DrawScenarioCatalogPanel();
            DrawBackendEvaluationPanel();
            DrawAiOrchestrationPanel();
            DrawDeveloperOptionsPanel();

            bool configChanged = serializedConfig.ApplyModifiedProperties();
            if (configChanged)
            {
                MarkModifiedCatalogScenarioAsCustom();
                SharedAvatarPrefabAuthoringEditor.ApplySelectedBackendAuthoring(config);
                ConfiguredSharedObjectPrefabAuthoringEditor.ApplySelectedBackendAuthoring(config);
                backendEvaluationResult = null;
            }

            GUILayout.Space(14);

            EditorGUILayout.EndScrollView();
        }

        private void DrawProjectOverview()
        {
            DrawSectionBanner("RESEARCH WORKSPACE", "Select the active networking implementation and review the current study state.");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawProperty("backendType");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                config.enableAutomatedBenchmarkScenario ? "Benchmark protocol armed" : "Manual session mode",
                EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                config.enableAiOrchestration ? $"AI: {config.aiMode}" : "AI: Disabled",
                EditorStyles.miniLabel,
                GUILayout.Width(130f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawSharedObjectsPanel()
        {
            GUILayout.Space(8f);
            showSharedObjects = EditorGUILayout.Foldout(
                showSharedObjects,
                "Shared Laboratory Objects",
                true,
                EditorStyles.foldoutHeader);
            if (showSharedObjects)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    "Objects configured here are provisioned consistently across the selected backend.",
                    EditorStyles.wordWrappedMiniLabel);
                DrawProperty("microscopeEquivalentObjects");
                DrawProperty("standaloneSharedObjects");
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawScenarioCatalogPanel()
        {
            GUILayout.Space(10f);
            DrawSectionBanner(
                "PAPER BENCHMARK PROTOCOLS",
                $"Version {BenchmarkScenarioPresetCatalog.ProtocolVersion} | fixed, exportable, and repeatable configurations");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawProperty("enableAutomatedBenchmarkScenario");

            DrawScenarioTableHeader();
            foreach (BenchmarkScenarioPresetCatalog.Preset preset in BenchmarkScenarioPresetCatalog.Presets)
                DrawScenarioTableRow(preset);

            BenchmarkScenarioPresetCatalog.Preset selected = BenchmarkScenarioPresetCatalog.FindSelected(config);
            if (selected != null)
            {
                EditorGUILayout.HelpBox(
                    $"Selected: {selected.id}\n{selected.objective}\n" +
                    $"Estimated measured workflow: ~{selected.EstimatedDurationSeconds:0.#} seconds per backend run.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "The current values are a custom protocol. Select a catalog row to restore a versioned paper preset.",
                    MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Export Protocol Manifest", GUILayout.Height(28f)))
            {
                string manifestPath = BenchmarkScenarioPresetCatalog.ExportProtocolManifest();
                AssetDatabase.Refresh();
                scenarioCatalogStatus = $"Protocol manifest saved to: {manifestPath}";
            }

            EditorGUI.BeginDisabledGroup(EditorApplication.isPlaying || selected == null);
            if (GUILayout.Button("Start Selected Scenario In Editor", GUILayout.Height(28f)))
            {
                AssetDatabase.SaveAssets();
                scenarioCatalogStatus = "Starting the Editor peer. Ensure the Windows peer was rebuilt with this same protocol.";
                EditorApplication.isPlaying = true;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(scenarioCatalogStatus))
                EditorGUILayout.HelpBox(scenarioCatalogStatus, MessageType.None);

            EditorGUILayout.HelpBox(
                "Protocol changes are stored in the Resources config. Rebuild the Windows peer after selecting a different " +
                "scenario so both clients execute identical parameters.",
                MessageType.Warning);
            EditorGUILayout.EndVertical();
        }

        private void DrawScenarioTableHeader()
        {
            Color previousColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.20f, 0.35f, 0.36f, 1f);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("ID", EditorStyles.boldLabel, GUILayout.Width(34f));
            EditorGUILayout.LabelField("Protocol", EditorStyles.boldLabel, GUILayout.Width(180f));
            EditorGUILayout.LabelField("Users", EditorStyles.boldLabel, GUILayout.Width(44f));
            EditorGUILayout.LabelField("Cycles", EditorStyles.boldLabel, GUILayout.Width(48f));
            EditorGUILayout.LabelField("Hold", EditorStyles.boldLabel, GUILayout.Width(55f));
            EditorGUILayout.LabelField("Est.", EditorStyles.boldLabel, GUILayout.Width(55f));
            EditorGUILayout.LabelField("Coverage", EditorStyles.boldLabel, GUILayout.Width(76f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Action", EditorStyles.boldLabel, GUILayout.Width(88f));
            EditorGUILayout.EndHorizontal();
            GUI.backgroundColor = previousColor;
            GUILayout.Space(3f);
        }

        private void DrawScenarioTableRow(BenchmarkScenarioPresetCatalog.Preset preset)
        {
            bool selected = preset.Matches(config);
            Color previousColor = GUI.backgroundColor;
            if (selected)
                GUI.backgroundColor = new Color(0.20f, 0.62f, 0.50f, 1f);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(preset.id.Substring(0, 2), EditorStyles.boldLabel, GUILayout.Width(34f));
            EditorGUILayout.LabelField(new GUIContent(preset.title, preset.objective), GUILayout.Width(180f));
            EditorGUILayout.LabelField(preset.participants.ToString(), GUILayout.Width(44f));
            EditorGUILayout.LabelField(preset.cycles.ToString(), GUILayout.Width(48f));
            EditorGUILayout.LabelField($"{preset.holdSeconds:0.#}s", GUILayout.Width(55f));
            EditorGUILayout.LabelField($"~{preset.EstimatedDurationSeconds:0}s", GUILayout.Width(55f));
            EditorGUILayout.LabelField(GetScenarioCoverage(preset.id).Label, GUILayout.Width(76f));
            GUILayout.FlexibleSpace();

            EditorGUI.BeginDisabledGroup(selected);
            if (GUILayout.Button(selected ? "Selected" : "Select", GUILayout.Width(88f)))
                SelectScenarioPreset(preset);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            GUI.backgroundColor = previousColor;
        }

        private BenchmarkScenarioPresetCatalog.Coverage GetScenarioCoverage(string scenarioId)
        {
            if (EditorApplication.timeSinceStartup >= nextScenarioCoverageRefreshTime)
            {
                scenarioCoverage.Clear();
                foreach (BenchmarkScenarioPresetCatalog.Preset preset in BenchmarkScenarioPresetCatalog.Presets)
                    scenarioCoverage[preset.id] = BenchmarkScenarioPresetCatalog.GetCoverage(preset.id);
                nextScenarioCoverageRefreshTime = EditorApplication.timeSinceStartup + 2d;
            }

            return scenarioCoverage.TryGetValue(scenarioId, out BenchmarkScenarioPresetCatalog.Coverage coverage)
                ? coverage
                : new BenchmarkScenarioPresetCatalog.Coverage();
        }

        private void SelectScenarioPreset(BenchmarkScenarioPresetCatalog.Preset preset)
        {
            Undo.RecordObject(config, $"Select Benchmark Protocol {preset.id}");
            preset.ApplyTo(config);
            config.backendEvaluationScenarioId = preset.id;
            EditorUtility.SetDirty(config);
            serializedConfig.Update();
            backendEvaluationResult = null;
            backendEvaluationStatus = null;
            scenarioCatalogStatus =
                $"Selected {preset.id}. Save the project and rebuild the Windows peer before collecting this protocol.";
        }

        private void DrawAiOrchestrationPanel()
        {
            GUILayout.Space(10f);
            showAiOrchestration = EditorGUILayout.Foldout(
                showAiOrchestration,
                config.enableAiOrchestration
                    ? $"AI Orchestration ({config.aiMode})"
                    : "AI Orchestration (Optional)",
                true,
                EditorStyles.foldoutHeader);
            if (showAiOrchestration)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawProperty("enableAiOrchestration");
                if (serializedConfig.FindProperty("enableAiOrchestration")?.boolValue == true)
                {
                    DrawProperty("aiMode");
                    DrawProperty("aiProvider");
                    DrawProperty("aiFallbackToFcfs");
                    DrawProperty("aiRecordAdvisorMetrics");
                    EditorGUILayout.HelpBox(
                        "Provider credentials, endpoint configuration, and tuning remain under Developer Options.",
                        MessageType.None);
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawDeveloperOptionsPanel()
        {
            GUILayout.Space(10f);
            showDeveloperOptions = EditorGUILayout.Foldout(
                showDeveloperOptions,
                "Developer Options",
                true,
                EditorStyles.foldoutHeader);
            if (!showDeveloperOptions)
                return;

            EditorGUILayout.HelpBox(
                "Machine-specific authoring and protocol overrides live here. Researchers normally do not need these " +
                "controls once the project is configured.",
                MessageType.Warning);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Bootstrap and Session", EditorStyles.boldLabel);
            DrawProperty("autoBootstrapOnSceneLoad");
            DrawProperty("spawnTelemetryUI");
            DrawProperty("dontDestroyRuntimeRoots");
            DrawProperty("autoConnectOnStartup");
            DrawProperty("autoJoinLobbyOnConnect");
            DrawProperty("createDefaultUI");
            DrawProperty("createAvatarSpawner");
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Package Sources and Branding", EditorStyles.boldLabel);
            DrawProperty("networkingCanvasSourcePrefab");
            DrawProperty("masterDashboardCanvasSourcePrefab");
            DrawProperty("userStatusCardSourcePrefab");
            DrawProperty("networkAvatarSourcePrefab");
            DrawProperty("orchestrationLogoSprite");
            DrawProperty("orchestrationLogoResourcesPath");
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Runtime Root Prefabs", EditorStyles.boldLabel);
            DrawProperty("pun2RuntimeRootPrefab");
            DrawProperty("fusionRuntimeRootPrefab");
            DrawProperty("fishNetRuntimeRootPrefab");
            DrawProperty("unityNetcodeRuntimeRootPrefab");
            DrawProperty("mirrorRuntimeRootPrefab");
            DrawProperty("telemetryUiRootPrefab");
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Resource Paths", EditorStyles.boldLabel);
            DrawProperty("resourcesExportRootFolder");
            DrawProperty("networkingCanvasPrefabPath");
            DrawProperty("masterDashboardCanvasPrefabPath");
            DrawProperty("userStatusCardPrefabPath");
            DrawProperty("networkAvatarPrefabPath");
            EditorGUILayout.EndVertical();

            showAdvancedScenarioOverrides = EditorGUILayout.Foldout(
                showAdvancedScenarioOverrides,
                "Advanced Scenario Overrides",
                true);
            if (showAdvancedScenarioOverrides)
                DrawAdvancedScenarioOverrides();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("AI Provider and Tuning", EditorStyles.boldLabel);
            DrawProperty("aiAllowExternalProviderControl");
            DrawProperty("aiDecisionTimeoutMs");
            DrawProperty("aiMinimumDecisionIntervalMs");
            DrawProperty("aiSuggestReleaseAfterHolderSeconds");
            DrawProperty("aiFairnessWeight");
            DrawProperty("aiFcfsWeight");
            DrawProperty("aiExternalEndpointUrl");
            DrawProperty("aiExternalAuthorizationHeader");
            DrawProperty("aiModelName");
            DrawProperty("aiSystemPrompt");
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Names and Debug", EditorStyles.boldLabel);
            DrawProperty("orchestrationRootName");
            DrawProperty("runtimeManagersGroupName");
            DrawProperty("registriesGroupName");
            DrawProperty("uiGroupName");
            DrawProperty("optionalGroupName");
            DrawProperty("verboseLogs");
            EditorGUILayout.EndVertical();

            DrawDeveloperActions();
        }

        private void DrawAdvancedScenarioOverrides()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox(
                "Changing these values makes the protocol custom. Exported metadata will retain every value, but the " +
                "run should not be pooled with a catalog preset unless the protocol is documented separately.",
                MessageType.Warning);
            DrawProperty("automatedScenario");
            DrawProperty("automatedScenarioMode");
            DrawProperty("automatedScenarioAutoBeginSession");
            DrawProperty("automatedScenarioAutoExportOnComplete");
            DrawProperty("automatedScenarioIdOverride");
            DrawProperty("automatedScenarioProtocolVersion");
            DrawProperty("automatedScenarioExpectedParticipants");
            DrawProperty("automatedScenarioRepeatCount");
            DrawProperty("automatedScenarioWarmupSeconds");
            DrawProperty("automatedScenarioHoldSeconds");
            DrawProperty("automatedScenarioCooldownSeconds");
            DrawProperty("automatedScenarioStepTimeoutSeconds");
            DrawProperty("automatedScenarioPreQueueNextUser");
            DrawProperty("automatedScenarioManipulateChildObject");
            DrawProperty("automatedScenarioManipulatedChildName");
            DrawProperty("automatedScenarioManipulationStartDelaySeconds");
            DrawProperty("automatedScenarioManipulationDurationSeconds");
            DrawProperty("automatedScenarioManipulationEulerDelta");
            DrawProperty("automatedScenarioResetPerformanceSamplesAfterWarmup");
            DrawProperty("automatedScenarioSuppressInfoLogsDuringMeasurement");
            DrawProperty("automatedScenarioValidateFinalRotation");
            DrawProperty("automatedScenarioFinalRotationSettleSeconds");
            DrawProperty("automatedScenarioRotationToleranceDegrees");
            EditorGUILayout.EndVertical();
        }

        private void MarkModifiedCatalogScenarioAsCustom()
        {
            foreach (BenchmarkScenarioPresetCatalog.Preset preset in BenchmarkScenarioPresetCatalog.Presets)
            {
                if (!string.Equals(
                        config.automatedScenarioIdOverride,
                        preset.id,
                        System.StringComparison.OrdinalIgnoreCase) ||
                    preset.Matches(config))
                {
                    continue;
                }

                config.automatedScenarioIdOverride = preset.id + "_Custom";
                config.automatedScenarioProtocolVersion = "Custom";
                EditorUtility.SetDirty(config);
                serializedConfig.Update();
                scenarioCatalogStatus =
                    "Advanced values changed. The protocol was relabeled as Custom to prevent accidental pooling with the catalog preset.";
                break;
            }
        }

        private void DrawDeveloperActions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Authoring Actions", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Export Default Prefabs", GUILayout.Height(28f)))
                OrchestrationInstaller.ExportDefaultPrefabsToResources(config);
            if (GUILayout.Button("Create Orchestration System", GUILayout.Height(28f)))
                OrchestrationInstaller.CreateDefaultOrchestrationSystem(config);
            if (GUILayout.Button("Create Default UI", GUILayout.Height(28f)))
                OrchestrationInstaller.CreateDefaultUI(config);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Configure Selected Instrument", GUILayout.Height(28f)))
                OrchestrationInstaller.ConfigureSelectedSharedInstrument(config);
            if (GUILayout.Button("Configure Selected Standalone Object", GUILayout.Height(28f)))
                OrchestrationInstaller.ConfigureSelectedStandaloneSharedObject(config);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static void DrawSectionBanner(string title, string subtitle)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 58f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.29f, 0.30f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 3f, rect.width, 3f), new Color(0.91f, 0.55f, 0.20f, 1f));

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft
            };
            titleStyle.normal.textColor = Color.white;

            GUIStyle subtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            subtitleStyle.normal.textColor = new Color(0.78f, 0.90f, 0.88f, 1f);

            EditorGUI.LabelField(new Rect(rect.x + 12f, rect.y + 4f, rect.width - 24f, 24f), title, titleStyle);
            EditorGUI.LabelField(new Rect(rect.x + 12f, rect.y + 29f, rect.width - 24f, 20f), subtitle, subtitleStyle);
        }

        private void DrawBackendEvaluationPanel()
        {
            GUILayout.Space(10f);
            DrawSectionBanner(
                "DECISION SUPPORT",
                "Balanced cohorts, weighted TOPSIS ranking, and sensitivity analysis");
            showBackendEvaluation = EditorGUILayout.Foldout(
                showBackendEvaluation,
                "Five-Backend Evaluation",
                true,
                EditorStyles.foldoutHeader);

            if (!showBackendEvaluation)
                return;

            EditorGUILayout.HelpBox(
                "Ranks only complete runs from a scenario shared by PUN2, Fusion, FishNet, Unity Netcode, and Mirror. " +
                "The cohort is balanced to the same number of latest runs per backend before weighted TOPSIS scoring.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Import Standalone Runs", GUILayout.Height(28)))
                ImportStandaloneBenchmarkRuns();

            if (GUILayout.Button("Open Standalone Exports", GUILayout.Height(28)))
            {
                EditorUtility.RevealInFinder(
                    StandaloneBenchmarkExportImporter.GetStandaloneExportsRoot());
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Decision Profile", EditorStyles.boldLabel);
            DrawProperty(
                "backendEvaluationTargetReplicatesPerBackend",
                new GUIContent("Target Runs / Backend", "Configured replication target; final sample size should follow power analysis."));
            DrawProperty(
                "backendEvaluationResponsivenessWeight",
                new GUIContent("Responsiveness", "Connection, session visibility, and authority-transfer latency."));
            DrawProperty(
                "backendEvaluationRuntimeWeight",
                new GUIContent("Runtime Performance", "Average/minimum FPS, frame time, and allocated memory."));
            DrawProperty(
                "backendEvaluationCoordinationWeight",
                new GUIContent("Coordination", "Transfer success and Jain fairness."));
            DrawProperty(
                "backendEvaluationReliabilityWeight",
                new GUIContent("Reliability", "Ratio of valid runs in the balanced cohort."));
            EditorGUILayout.EndVertical();

            IReadOnlyList<string> commonScenarios = BackendEvaluationAnalyzer.FindCommonScenarioIds();
            if (commonScenarios.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No complete benchmark scenario is currently shared by all five backends.",
                    MessageType.Warning);
                return;
            }

            string[] scenarioOptions = new List<string>(commonScenarios).ToArray();
            int scenarioIndex = ResolveSelectedScenarioIndex(scenarioOptions);
            EditorGUI.BeginChangeCheck();
            int selectedIndex = EditorGUILayout.Popup("Evaluation Scenario", scenarioIndex, scenarioOptions);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(config, "Change Backend Evaluation Scenario");
                config.backendEvaluationScenarioId = scenarioOptions[selectedIndex];
                EditorUtility.SetDirty(config);
                serializedConfig.Update();
                backendEvaluationResult = null;
                backendEvaluationStatus = null;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Analyze Five Backends", GUILayout.Height(28)))
                RunBackendEvaluation();

            EditorGUI.BeginDisabledGroup(backendEvaluationResult == null || !backendEvaluationResult.IsValid);
            if (GUILayout.Button("Export Ranked Report", GUILayout.Height(28)))
                ExportBackendEvaluationReport();
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Open Exports", GUILayout.Height(28)))
            {
                EditorUtility.RevealInFinder(
                    EvaluationExportPathUtility.GetExportsRoot());
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(backendEvaluationStatus))
                EditorGUILayout.HelpBox(backendEvaluationStatus, MessageType.None);

            if (backendEvaluationResult == null)
            {
                EditorGUILayout.HelpBox(
                    "Press Analyze Five Backends to calculate the current descriptive ranking.",
                    MessageType.None);
                return;
            }

            if (!backendEvaluationResult.IsValid)
            {
                EditorGUILayout.HelpBox(backendEvaluationResult.error, MessageType.Error);
                return;
            }

            BackendEvaluationAnalyzer.BackendScore leader = backendEvaluationResult.rankings[0];
            EditorGUILayout.HelpBox(
                $"Current leader: {leader.backend} | Relative TOPSIS score: {leader.score * 100d:F1}/100 | " +
                $"Margin over rank 2: {backendEvaluationResult.leaderMargin * 100d:F2} points | " +
                $"Weight sensitivity stability: {backendEvaluationResult.leaderStabilityPercent:F0}%",
                MessageType.Info);

            EditorGUILayout.LabelField("Normalized weights", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"Responsiveness {backendEvaluationResult.responsivenessWeight:P0}  |  " +
                $"Runtime {backendEvaluationResult.runtimeWeight:P0}  |  " +
                $"Coordination {backendEvaluationResult.coordinationWeight:P0}  |  " +
                $"Reliability {backendEvaluationResult.reliabilityWeight:P0}",
                EditorStyles.wordWrappedMiniLabel);

            foreach (string warning in backendEvaluationResult.warnings)
            {
                EditorGUILayout.HelpBox(
                    warning,
                    backendEvaluationResult.IsPilot ? MessageType.Warning : MessageType.Info);
            }

            foreach (BackendEvaluationAnalyzer.BackendScore row in backendEvaluationResult.rankings)
                DrawBackendScore(row);

            EditorGUILayout.HelpBox(
                "TOPSIS scores are relative decision-support values, not percentages of absolute performance. " +
                "Keep the raw metrics and weight profile with any reported ranking.",
                MessageType.None);
        }

        private int ResolveSelectedScenarioIndex(IReadOnlyList<string> scenarioOptions)
        {
            for (int index = 0; index < scenarioOptions.Count; index++)
            {
                if (string.Equals(
                    scenarioOptions[index],
                    config.backendEvaluationScenarioId,
                    System.StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return 0;
        }

        private void RunBackendEvaluation()
        {
            backendEvaluationResult = BackendEvaluationAnalyzer.Evaluate(
                new BackendEvaluationAnalyzer.Settings
                {
                    scenarioId = config.backendEvaluationScenarioId,
                    targetReplicates = config.backendEvaluationTargetReplicatesPerBackend,
                    responsivenessWeight = config.backendEvaluationResponsivenessWeight,
                    runtimeWeight = config.backendEvaluationRuntimeWeight,
                    coordinationWeight = config.backendEvaluationCoordinationWeight,
                    reliabilityWeight = config.backendEvaluationReliabilityWeight
                });
            backendEvaluationStatus = backendEvaluationResult.IsValid
                ? $"Analyzed scenario '{backendEvaluationResult.scenarioId}'."
                : "Backend evaluation could not be completed.";
        }

        private void ExportBackendEvaluationReport()
        {
            string reportPath = BackendEvaluationAnalyzer.ExportReport(backendEvaluationResult);
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                backendEvaluationStatus = "Could not export the ranked backend report.";
                return;
            }

            AssetDatabase.Refresh();
            backendEvaluationStatus = $"Ranked report saved to: {reportPath}";
        }

        private static void DrawBackendScore(BackendEvaluationAnalyzer.BackendScore row)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"#{row.rank}  {row.backend}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                $"Score {row.score * 100d:F1}   Runs {row.validRunCount}/{row.selectedRunCount}",
                EditorStyles.boldLabel,
                GUILayout.Width(190f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                $"Connect {row.connectLatencyMs:F2} ms  |  Session visible {row.sessionStartDelayMs:F2} ms  |  " +
                (row.authorityTransferEventCount > 0
                    ? $"Transfer {row.transferLatencyMs:F2} ms ({row.transferSuccessRate:P0}, n={row.authorityTransferEventCount})"
                    : "Transfer n/a (no physical transfer event)"),
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                $"FPS avg/min {row.averageFps:F2}/{row.minFps:F2}  |  Frame {row.averageFrameTimeMs:F2} ms  |  " +
                $"Memory {row.allocatedMemoryMb:F1} MB  |  Requests granted {row.requestFulfillmentRate:P0}  |  " +
                $"Fairness {row.fairnessIndex:F4}",
                EditorStyles.wordWrappedMiniLabel);
            if (!double.IsNaN(row.finalRotationErrorDegrees))
            {
                EditorGUILayout.LabelField(
                    $"Final child-rotation error {row.finalRotationErrorDegrees:F3} degrees",
                    EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(5f);
        }

        private void ImportStandaloneBenchmarkRuns()
        {
            StandaloneBenchmarkExportImporter.Result result =
                StandaloneBenchmarkExportImporter.ImportCurrentProtocolRuns();
            AssetDatabase.Refresh();
            backendEvaluationResult = null;
            backendEvaluationStatus = result.BuildMessage();
            scenarioCoverage.Clear();
            nextScenarioCoverageRefreshTime = 0d;
        }

        private void DrawTopBannerSpace()
        {
            Rect rect = GUILayoutUtility.GetRect(0, HeaderBannerHeight, GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f, 1f));

            Rect inner = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f);
            EditorGUI.DrawRect(inner, new Color(0.22f, 0.22f, 0.22f, 1f));

            Sprite logoSprite = ResolveBannerLogoSprite();
            if (logoSprite != null && logoSprite.texture != null)
            {
                Rect logoRect = new Rect(inner.x + 2f, inner.y + 2f, inner.width - 4f, inner.height - 4f);
                GUI.DrawTexture(logoRect, logoSprite.texture, ScaleMode.ScaleToFit, true);
                return;
            }

            GUIStyle centeredStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic
            };

            EditorGUI.LabelField(inner, "Reserved space for future image / logo / banner", centeredStyle);
        }

        private Sprite ResolveBannerLogoSprite()
        {
            if (config != null)
            {
                OrchestrationInstaller.InitializeNewConfigAsset(config);

                if (config.orchestrationLogoSprite != null)
                    return config.orchestrationLogoSprite;

                Sprite configuredLogo = OrchestrationInstaller.LoadBrandLogoSprite(config.orchestrationLogoResourcesPath);
                if (configuredLogo != null)
                {
                    config.orchestrationLogoSprite = configuredLogo;
                    EditorUtility.SetDirty(config);
                    return configuredLogo;
                }
            }

            return OrchestrationInstaller.LoadBrandLogoSprite(null);
        }

        private void DrawProperty(string propertyName)
        {
            SerializedProperty prop = serializedConfig.FindProperty(propertyName);
            if (prop != null)
            {
                EditorGUILayout.PropertyField(prop, true);
            }
        }

        private void DrawProperty(string propertyName, GUIContent label)
        {
            SerializedProperty prop = serializedConfig.FindProperty(propertyName);
            if (prop != null)
                EditorGUILayout.PropertyField(prop, label, true);
        }

        private void CreateConfigAsset()
        {
            var asset = CreateInstance<OrchestrationProjectConfig>();
            OrchestrationInstaller.InitializeNewConfigAsset(asset);

            string path = EditorUtility.SaveFilePanelInProject(
                "Create Orchestration Project Config",
                "OrchestrationProjectConfig",
                "asset",
                "Choose a location for the config asset.");

            if (string.IsNullOrWhiteSpace(path))
                return;

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            config = asset;
            serializedConfig = new SerializedObject(asset);
            Selection.activeObject = asset;
        }

        private void TryAutoLoadConfigFromResources()
        {
            if (config != null)
                return;

            OrchestrationProjectConfig loadedConfig = FindProjectConfigAsset();
            if (loadedConfig == null)
                return;

            config = loadedConfig;
            serializedConfig = new SerializedObject(config);
        }

        private static OrchestrationProjectConfig FindProjectConfigAsset()
        {
            const string defaultResourcesAssetPath = "Assets/Resources/AIMultiplayerAssistant/OrchestrationProjectConfig.asset";

            OrchestrationProjectConfig config =
                AssetDatabase.LoadAssetAtPath<OrchestrationProjectConfig>(defaultResourcesAssetPath);
            if (config != null)
                return config;

            string[] configGuids = AssetDatabase.FindAssets("t:OrchestrationProjectConfig");
            string preferredResourcesPath = null;
            string fallbackPath = null;

            foreach (string guid in configGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(assetPath))
                    continue;

                fallbackPath ??= assetPath;

                if (!assetPath.Contains("/Resources/"))
                    continue;

                preferredResourcesPath ??= assetPath;

                if (string.Equals(
                        Path.GetFileName(assetPath),
                        "OrchestrationProjectConfig.asset",
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    preferredResourcesPath = assetPath;
                    break;
                }
            }

            string resolvedPath = !string.IsNullOrWhiteSpace(preferredResourcesPath)
                ? preferredResourcesPath
                : fallbackPath;

            return string.IsNullOrWhiteSpace(resolvedPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<OrchestrationProjectConfig>(resolvedPath);
        }
    }

    internal static class OrchestrationInstaller
    {
        private const string DefaultLogoResourcesPath = "OrchestrationAILogo";
        private const string DefaultSharedAvatarAssetPath = "Assets/Resources/AIMultiplayerAssistant/NetworkAvatar.prefab";
        private const string DefaultFishNetRuntimeRootAssetPath = "Assets/FishNet/Demos/Prefabs/NetworkManager.prefab";

        public static void ExportDefaultPrefabsToResources(OrchestrationProjectConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[OrchestrationInstaller] Missing config.");
                return;
            }

            EnsureSharedAvatarConfigured(config, recordUndo: true);

            string exportRoot = NormalizeFolder(config.resourcesExportRootFolder);

            if (!exportRoot.StartsWith("Assets/Resources"))
            {
                Debug.LogError("[OrchestrationInstaller] Export root must be under Assets/Resources.");
                return;
            }

            EnsureFolder(exportRoot);
            EnsureFolder(Path.Combine(exportRoot, "UIs").Replace("\\", "/"));
            EnsureFolder(Path.Combine(exportRoot, "Avatar").Replace("\\", "/"));

            ExportPrefab(config.networkingCanvasSourcePrefab, Path.Combine(exportRoot, "UIs/NetworkingCanvas.prefab").Replace("\\", "/"));
            ExportPrefab(config.masterDashboardCanvasSourcePrefab, Path.Combine(exportRoot, "UIs/MasterDashboardCanvas.prefab").Replace("\\", "/"));
            ExportPrefab(config.userStatusCardSourcePrefab, Path.Combine(exportRoot, "UIs/UserStatusCard.prefab").Replace("\\", "/"));
            string exportedAvatarAssetPath = Path.Combine(exportRoot, "NetworkAvatar.prefab").Replace("\\", "/");
            ExportPrefab(ResolveAvatarSourcePrefab(config), exportedAvatarAssetPath);

            Undo.RecordObject(config, "Update Orchestration Runtime Resource Paths");

            config.networkingCanvasPrefabPath = ToResourcesPath(Path.Combine(exportRoot, "UIs/NetworkingCanvas.prefab").Replace("\\", "/"));
            config.masterDashboardCanvasPrefabPath = ToResourcesPath(Path.Combine(exportRoot, "UIs/MasterDashboardCanvas.prefab").Replace("\\", "/"));
            config.userStatusCardPrefabPath = ToResourcesPath(Path.Combine(exportRoot, "UIs/UserStatusCard.prefab").Replace("\\", "/"));
            config.networkAvatarPrefabPath = ToResourcesPath(exportedAvatarAssetPath);

            EditorUtility.SetDirty(config);
            SharedAvatarPrefabAuthoringEditor.ApplySelectedBackendAuthoring(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (config.verboseLogs)
            {
                Debug.Log($"[OrchestrationInstaller] Exported default prefabs to '{exportRoot}' and updated runtime resource paths.");
            }
        }

        public static void CreateDefaultOrchestrationSystem(OrchestrationProjectConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[OrchestrationInstaller] Missing config.");
                return;
            }

            EnsureSharedAvatarConfigured(config, recordUndo: true);

            GameObject root = GetOrCreateRoot(config.orchestrationRootName);
            GameObject runtimeManagers = GetOrCreateChild(root, config.runtimeManagersGroupName);
            GameObject registries = GetOrCreateChild(root, config.registriesGroupName);
            GameObject ui = GetOrCreateChild(root, config.uiGroupName);
            GameObject optional = GetOrCreateChild(root, config.optionalGroupName);

            switch (config.backendType)
            {
                case OrchestrationBackendType.PUN2:
                    CreateOrGetManager<SessionOrchestratorPUN2>(runtimeManagers, "SessionOrchestratorPUN2");
                    CreateOrGetManager<SessionQueueRuntimePUN2>(runtimeManagers, "SessionQueueRuntimePUN2");
                    break;

                case OrchestrationBackendType.Fusion:
                case OrchestrationBackendType.UnityNetcode:
                case OrchestrationBackendType.FishNet:
                case OrchestrationBackendType.Mirror:
                    break;

                default:
                    Debug.LogWarning($"[OrchestrationInstaller] Backend '{config.backendType}' is not implemented yet. Creating only common managers.");
                    break;
            }

            CreateOrGetManager<TelemetryLoggerCore>(runtimeManagers, "TelemetryLoggerCore");
            CreateOrGetManager<UsageTimingTracker>(runtimeManagers, "UsageTimingTracker");

            CreateOrGetManager<SharedObjectSyncRegistry>(registries, "SharedObjectSyncRegistry");
            CreateOrGetManager<UserPresenceSyncRegistry>(registries, "UserPresenceSyncRegistry");

            if (config.createAvatarSpawner)
            {
                CreateAvatarSpawnerForSelectedBackend(config, optional);
            }

            if (config.createDefaultUI)
            {
                CreateDefaultUI(config, ui.transform);
            }

            WireCoreReferences(config);
            FocusRoot(root);

            if (config.verboseLogs)
            {
                Debug.Log("[OrchestrationInstaller] Default orchestration system created/updated.");
            }
        }

        public static void CreateDefaultUI(OrchestrationProjectConfig config, Transform parentOverride = null)
        {
            if (config == null)
            {
                Debug.LogError("[OrchestrationInstaller] Missing config.");
                return;
            }

            GameObject root = GetOrCreateRoot(config.orchestrationRootName);
            GameObject uiGroup = parentOverride != null ? parentOverride.gameObject : GetOrCreateChild(root, config.uiGroupName);

            GameObject networkingCanvas = LoadOrInstantiatePrefabIfMissing(
                config.networkingCanvasPrefabPath,
                "NetworkingCanvas",
                uiGroup.transform);

            GameObject masterDashboardCanvas = LoadOrInstantiatePrefabIfMissing(
                config.masterDashboardCanvasPrefabPath,
                "MasterDashboardCanvas",
                uiGroup.transform);

            if (networkingCanvas != null)
                networkingCanvas.SetActive(true);

            if (masterDashboardCanvas != null)
                masterDashboardCanvas.SetActive(false);

            WireCoreReferences(config);
            WireTelemetryUI(config, networkingCanvas, masterDashboardCanvas);

            if (config.verboseLogs)
            {
                Debug.Log("[OrchestrationInstaller] Default UI created/updated.");
            }
        }

        public static void ConfigureSelectedSharedInstrument(OrchestrationProjectConfig config)
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("[OrchestrationInstaller] No selected GameObject.");
                return;
            }

            SharedSyncObjectDefinition sharedDefinition = selected.GetComponent<SharedSyncObjectDefinition>();
            if (sharedDefinition == null)
            {
                sharedDefinition = Undo.AddComponent<SharedSyncObjectDefinition>(selected);
            }

            AutomaticBackendObjectBinding binding = selected.GetComponent<AutomaticBackendObjectBinding>();
            if (binding == null)
            {
                binding = Undo.AddComponent<AutomaticBackendObjectBinding>(selected);
            }
            binding.ConfigureForMicroscope(sharedDefinition);

            EditorUtility.SetDirty(selected);

            if (config != null && config.verboseLogs)
            {
                Debug.Log($"[OrchestrationInstaller] Configured shared instrument for automatic backend binding: {selected.name}");
            }
        }

        public static void ConfigureSelectedStandaloneSharedObject(OrchestrationProjectConfig config)
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("[OrchestrationInstaller] No selected GameObject.");
                return;
            }

            AutomaticBackendObjectBinding binding = selected.GetComponent<AutomaticBackendObjectBinding>();
            if (binding == null)
            {
                binding = Undo.AddComponent<AutomaticBackendObjectBinding>(selected);
            }
            binding.ConfigureForStandalone();

            EditorUtility.SetDirty(selected);

            if (config != null && config.verboseLogs)
            {
                Debug.Log($"[OrchestrationInstaller] Configured standalone shared object for automatic backend binding: {selected.name}");
            }
        }

        private static void WireCoreReferences(OrchestrationProjectConfig config)
        {
            SessionOrchestratorPUN2 orchestrator = Object.FindObjectOfType<SessionOrchestratorPUN2>();
            SessionQueueRuntimePUN2 queueRuntime = Object.FindObjectOfType<SessionQueueRuntimePUN2>();
            SharedObjectSyncRegistry sharedRegistry = Object.FindObjectOfType<SharedObjectSyncRegistry>();
            TelemetryLoggerCore telemetryLogger = Object.FindObjectOfType<TelemetryLoggerCore>();
            UsageTimingTracker usageTimingTracker = Object.FindObjectOfType<UsageTimingTracker>();
            TelemetryUI telemetryUI = Object.FindObjectOfType<TelemetryUI>();

            if (queueRuntime != null)
            {
                SerializedObject so = new SerializedObject(queueRuntime);
                SetObjectReferenceIfEmpty(so, "sessionOrchestrator", orchestrator);
                SetObjectReferenceIfEmpty(so, "sharedRegistry", sharedRegistry);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(queueRuntime);
            }

            if (telemetryUI != null)
            {
                SerializedObject so = new SerializedObject(telemetryUI);
                SetObjectReferenceIfEmpty(so, "telemetryLogger", telemetryLogger);
                SetObjectReferenceIfEmpty(so, "sessionOrchestrator", orchestrator);
                SetObjectReferenceIfEmpty(so, "queueRuntime", queueRuntime);
                SetObjectReferenceIfEmpty(so, "usageTimingTracker", usageTimingTracker);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(telemetryUI);
            }

            if (config != null && config.verboseLogs)
            {
                Debug.Log("[OrchestrationInstaller] Core references wired where possible.");
            }
        }

        private static void WireTelemetryUI(OrchestrationProjectConfig config, GameObject networkingCanvas, GameObject masterDashboardCanvas)
        {
            TelemetryUI telemetryUI = Object.FindObjectOfType<TelemetryUI>();
            if (telemetryUI == null)
                return;

            SerializedObject so = new SerializedObject(telemetryUI);

            SetObjectReference(so, "networkingCanvasRoot", networkingCanvas);
            SetObjectReference(so, "masterDashboardCanvasRoot", masterDashboardCanvas);

            if (masterDashboardCanvas != null)
            {
                Transform cardsContainer = masterDashboardCanvas.transform.Find("BackgroundPanel/CenterUsersPanel/Scroll View/Viewport/Content");
                if (cardsContainer != null)
                {
                    SetObjectReference(so, "masterUserCardsContainer", cardsContainer);
                }

                UserStatusCardUI cardPrefab = null;
                if (!string.IsNullOrWhiteSpace(config.userStatusCardPrefabPath))
                {
                    cardPrefab = Resources.Load<UserStatusCardUI>(config.userStatusCardPrefabPath);
                }

                if (cardPrefab != null)
                {
                    SetObjectReference(so, "userStatusCardPrefab", cardPrefab);
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(telemetryUI);

            if (config.verboseLogs)
            {
                Debug.Log("[OrchestrationInstaller] TelemetryUI canvas references wired where possible.");
            }
        }

        private static void SetObjectReferenceIfEmpty(SerializedObject so, string propertyName, Object value)
        {
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop != null && prop.objectReferenceValue == null && value != null)
            {
                prop.objectReferenceValue = value;
            }
        }

        private static void SetObjectReference(SerializedObject so, string propertyName, Object value)
        {
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
            }
        }

        private static T CreateOrGetManager<T>(GameObject parent, string objectName) where T : Component
        {
            T existing = Object.FindObjectOfType<T>();
            if (existing != null)
                return existing;

            GameObject go = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(go, $"Create {objectName}");
            go.transform.SetParent(parent.transform);
            return Undo.AddComponent<T>(go);
        }

        private static GameObject GetOrCreateRoot(string rootName)
        {
            GameObject root = GameObject.Find(rootName);
            if (root != null)
                return root;

            root = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(root, $"Create {rootName}");
            return root;
        }

        private static GameObject GetOrCreateChild(GameObject parent, string childName)
        {
            Transform child = parent.transform.Find(childName);
            if (child != null)
                return child.gameObject;

            GameObject go = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(go, $"Create {childName}");
            go.transform.SetParent(parent.transform);
            return go;
        }

        private static GameObject LoadOrInstantiatePrefabIfMissing(string resourcesPath, string fallbackName, Transform parent)
        {
            GameObject existing = parent.Find(fallbackName)?.gameObject;
            if (existing != null)
                return existing;

            GameObject prefab = null;
            if (!string.IsNullOrWhiteSpace(resourcesPath))
            {
                prefab = Resources.Load<GameObject>(resourcesPath);
            }

            if (prefab != null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                Undo.RegisterCreatedObjectUndo(instance, $"Create {instance.name}");
                return instance;
            }

            GameObject fallback = new GameObject(fallbackName);
            Undo.RegisterCreatedObjectUndo(fallback, $"Create {fallbackName}");
            fallback.transform.SetParent(parent);

            Canvas canvas = fallback.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            fallback.AddComponent<CanvasScaler>();
            fallback.AddComponent<GraphicRaycaster>();

            return fallback;
        }

        private static void ExportPrefab(GameObject sourcePrefab, string targetAssetPath)
        {
            if (sourcePrefab == null)
                return;

            string sourcePath = AssetDatabase.GetAssetPath(sourcePrefab);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                Debug.LogWarning($"[OrchestrationInstaller] Could not resolve asset path for prefab '{sourcePrefab.name}'.");
                return;
            }

            if (string.Equals(sourcePath.Replace("\\", "/"), targetAssetPath.Replace("\\", "/"), System.StringComparison.OrdinalIgnoreCase))
                return;

            string folder = Path.GetDirectoryName(targetAssetPath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                EnsureFolder(folder);
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(targetAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(targetAssetPath);
            }

            bool copied = AssetDatabase.CopyAsset(sourcePath, targetAssetPath);
            if (!copied)
            {
                Debug.LogError($"[OrchestrationInstaller] Failed to copy prefab from '{sourcePath}' to '{targetAssetPath}'.");
            }
        }

        private static void EnsureFolder(string folderPath)
        {
            folderPath = NormalizeFolder(folderPath);

            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string parent = Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
            string folderName = Path.GetFileName(folderPath);

            if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }

        private static string NormalizeFolder(string folder)
        {
            return folder.Replace("\\", "/").TrimEnd('/');
        }

        private static string ToResourcesPath(string assetPath)
        {
            assetPath = assetPath.Replace("\\", "/");

            int resourcesIndex = assetPath.IndexOf("/Resources/", System.StringComparison.Ordinal);
            if (resourcesIndex < 0)
            {
                if (assetPath.StartsWith("Assets/Resources/"))
                {
                    return assetPath.Substring("Assets/Resources/".Length).Replace(".prefab", "");
                }

                return assetPath.Replace(".prefab", "");
            }

            string result = assetPath.Substring(resourcesIndex + "/Resources/".Length + 1);
            return result.Replace(".prefab", "");
        }

        private static void FocusRoot(GameObject root)
        {
            if (root == null)
                return;

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
        }

        internal static void InitializeNewConfigAsset(OrchestrationProjectConfig config)
        {
            EnsureSharedAvatarConfigured(config, recordUndo: false);
            EnsureBrandingConfigured(config, recordUndo: false);
        }

        private static void EnsureSharedAvatarConfigured(OrchestrationProjectConfig config, bool recordUndo)
        {
            if (config == null)
                return;

            bool changed = false;
            if (recordUndo)
                Undo.RecordObject(config, "Configure Shared Network Avatar");

            string normalizedAvatarPath = config.networkAvatarPrefabPath?.Replace("\\", "/").Trim();
            if (string.IsNullOrWhiteSpace(normalizedAvatarPath) ||
                SharedAvatarRuntimeUtility.IsLegacyAvatarResourcesPath(normalizedAvatarPath))
            {
                config.networkAvatarPrefabPath = SharedAvatarRuntimeUtility.DefaultAvatarPrefabResourcesPath;
                changed = true;
            }

            if (config.networkAvatarSourcePrefab == null)
            {
                GameObject sharedAvatarPrefab = LoadDefaultSharedAvatarPrefab();
                if (sharedAvatarPrefab != null)
                {
                    config.networkAvatarSourcePrefab = sharedAvatarPrefab;
                    changed = true;
                }
            }

            if (config.fishNetRuntimeRootPrefab == null)
            {
                GameObject fishNetRuntimeRootPrefab = LoadDefaultFishNetRuntimeRootPrefab();
                if (fishNetRuntimeRootPrefab != null)
                {
                    config.fishNetRuntimeRootPrefab = fishNetRuntimeRootPrefab;
                    changed = true;
                }
            }

            if (changed)
                EditorUtility.SetDirty(config);
        }

        private static void EnsureBrandingConfigured(OrchestrationProjectConfig config, bool recordUndo)
        {
            if (config == null)
                return;

            bool changed = false;
            if (recordUndo)
                Undo.RecordObject(config, "Configure Orchestration Branding");

            if (string.IsNullOrWhiteSpace(config.orchestrationLogoResourcesPath))
            {
                config.orchestrationLogoResourcesPath = DefaultLogoResourcesPath;
                changed = true;
            }

            if (config.orchestrationLogoSprite == null)
            {
                Sprite logoSprite = LoadBrandLogoSprite(config.orchestrationLogoResourcesPath);
                if (logoSprite != null)
                {
                    config.orchestrationLogoSprite = logoSprite;
                    changed = true;
                }
            }

            if (changed)
                EditorUtility.SetDirty(config);
        }

        internal static Sprite LoadBrandLogoSprite(string resourcesPath)
        {
            string normalizedResourcesPath = string.IsNullOrWhiteSpace(resourcesPath)
                ? DefaultLogoResourcesPath
                : resourcesPath.Replace("\\", "/").Trim().Trim('/');

            string logoFileName = Path.GetFileName(normalizedResourcesPath);
            string[] guids = AssetDatabase.FindAssets($"{logoFileName} t:Sprite");
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.Contains("/Resources/"))
                    continue;

                string candidateResourcesPath = ConvertAssetPathToResourcesPath(assetPath);
                if (string.Equals(candidateResourcesPath, normalizedResourcesPath, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(candidateResourcesPath), logoFileName, System.StringComparison.OrdinalIgnoreCase))
                {
                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                    if (sprite != null)
                        return sprite;
                }
            }

            return Resources.Load<Sprite>(normalizedResourcesPath);
        }

        private static string ConvertAssetPathToResourcesPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return string.Empty;

            string normalizedAssetPath = assetPath.Replace("\\", "/");
            int resourcesIndex = normalizedAssetPath.IndexOf("/Resources/", System.StringComparison.OrdinalIgnoreCase);
            if (resourcesIndex < 0)
                return Path.GetFileNameWithoutExtension(normalizedAssetPath);

            string resourcesRelativePath = normalizedAssetPath.Substring(resourcesIndex + "/Resources/".Length + 1);
            return Path.ChangeExtension(resourcesRelativePath, null)?.Replace("\\", "/") ?? string.Empty;
        }

        private static GameObject ResolveAvatarSourcePrefab(OrchestrationProjectConfig config)
        {
            if (config != null && config.networkAvatarSourcePrefab != null)
                return config.networkAvatarSourcePrefab;

            return LoadDefaultSharedAvatarPrefab();
        }

        private static GameObject LoadDefaultSharedAvatarPrefab()
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(DefaultSharedAvatarAssetPath);
        }

        private static GameObject LoadDefaultFishNetRuntimeRootPrefab()
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(DefaultFishNetRuntimeRootAssetPath);
        }

        private static void CreateAvatarSpawnerForSelectedBackend(OrchestrationProjectConfig config, GameObject parent)
        {
            switch (config.backendType)
            {
                case OrchestrationBackendType.PUN2:
                    CreateOrGetManager<NetworkAvatarSpawnerPUN2>(parent, nameof(NetworkAvatarSpawnerPUN2));
                    break;

                case OrchestrationBackendType.Fusion:
                    CreateOrGetManager<NetworkAvatarSpawnerFusion>(parent, nameof(NetworkAvatarSpawnerFusion));
                    break;

                case OrchestrationBackendType.UnityNetcode:
                    CreateOrGetManager<NetworkAvatarSpawnerNetcode>(parent, nameof(NetworkAvatarSpawnerNetcode));
                    break;

                case OrchestrationBackendType.FishNet:
                    CreateOrGetManager<NetworkAvatarSpawnerFishNet>(parent, nameof(NetworkAvatarSpawnerFishNet));
                    break;

                case OrchestrationBackendType.Mirror:
                    CreateOrGetManager<NetworkAvatarSpawnerMirror>(parent, nameof(NetworkAvatarSpawnerMirror));
                    break;
            }
        }
    }

    internal static class StandaloneBenchmarkExportImporter
    {
        internal sealed class Result
        {
            public string sourceRoot;
            public string destinationRoot;
            public int imported;
            public int skippedExisting;
            public int skippedOtherProtocol;
            public string error;

            public string BuildMessage()
            {
                if (!string.IsNullOrWhiteSpace(error))
                    return error;

                return $"Imported {imported} standalone run(s); kept {skippedExisting} existing run(s); " +
                       $"ignored {skippedOtherProtocol} run(s) from another protocol version.";
            }
        }

        internal static string GetStandaloneExportsRoot()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(projectRoot, "Builds", "EvaluationExports");
        }

        internal static Result ImportCurrentProtocolRuns()
        {
            Result result = new Result
            {
                sourceRoot = GetStandaloneExportsRoot(),
                destinationRoot = EvaluationExportPathUtility.GetExportsRoot()
            };

            if (!Directory.Exists(result.sourceRoot))
            {
                result.error = $"Standalone exports were not found at: {result.sourceRoot}";
                return result;
            }

            Directory.CreateDirectory(result.destinationRoot);
            foreach (string runDirectory in Directory.GetDirectories(
                         result.sourceRoot,
                         "Run_*",
                         SearchOption.AllDirectories))
            {
                if (!IsCompleteRun(runDirectory))
                    continue;

                if (!IsCurrentProtocolRun(runDirectory))
                {
                    result.skippedOtherProtocol++;
                    continue;
                }

                string relativePath = runDirectory
                    .Substring(result.sourceRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destination = Path.Combine(result.destinationRoot, relativePath);
                if (Directory.Exists(destination))
                {
                    if (IsCompleteRun(destination))
                    {
                        result.skippedExisting++;
                        continue;
                    }

                    destination += $"_imported_{System.DateTime.Now:yyyyMMdd_HHmmss_fff}";
                }

                CopyRunDirectory(runDirectory, destination);
                result.imported++;
            }

            return result;
        }

        private static bool IsCompleteRun(string runDirectory)
        {
            return File.Exists(Path.Combine(runDirectory, "export_manifest.csv")) &&
                   File.Exists(Path.Combine(runDirectory, "run_metadata.csv")) &&
                   File.Exists(Path.Combine(runDirectory, "scenario_metrics_summary.csv")) &&
                   File.Exists(Path.Combine(runDirectory, "run_master_summary.csv")) &&
                   File.Exists(Path.Combine(runDirectory, "runtime_performance_summary.csv"));
        }

        private static bool IsCurrentProtocolRun(string runDirectory)
        {
            Dictionary<string, string> metadata = ReadSingleRowCsv(
                Path.Combine(runDirectory, "run_metadata.csv"));
            return metadata.TryGetValue("protocol_version", out string version) &&
                   string.Equals(
                       version,
                       BenchmarkScenarioPresetCatalog.ProtocolVersion,
                       System.StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyRunDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            string manifestPath = Path.Combine(source, "export_manifest.csv");
            foreach (string sourceFile in Directory.GetFiles(source, "*", SearchOption.AllDirectories)
                         .Where(path => !string.Equals(
                             path,
                             manifestPath,
                             System.StringComparison.OrdinalIgnoreCase)))
            {
                CopyFile(source, destination, sourceFile);
            }

            // The manifest is copied last so an interrupted import cannot look like a complete run.
            CopyFile(source, destination, manifestPath);
        }

        private static void CopyFile(string sourceRoot, string destinationRoot, string sourceFile)
        {
            string relativePath = sourceFile
                .Substring(sourceRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string destinationFile = Path.Combine(destinationRoot, relativePath);
            string destinationDirectory = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);
            File.Copy(sourceFile, destinationFile, false);
        }

        private static Dictionary<string, string> ReadSingleRowCsv(string path)
        {
            if (!File.Exists(path))
                return new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

            string[] lines = File.ReadAllLines(path);
            if (lines.Length < 2)
                return new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

            string[] headers = SplitCsvLine(lines[0]);
            string[] values = SplitCsvLine(lines[1]);
            Dictionary<string, string> row = new(System.StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < headers.Length && index < values.Length; index++)
                row[headers[index].Trim()] = values[index].Trim().Trim('"');
            return row;
        }

        private static string[] SplitCsvLine(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder current = new StringBuilder();
            bool quoted = false;
            for (int index = 0; index < line.Length; index++)
            {
                char character = line[index];
                if (character == '"')
                {
                    if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (character == ',' && !quoted)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(character);
                }
            }

            fields.Add(current.ToString());
            return fields.ToArray();
        }
    }
}
