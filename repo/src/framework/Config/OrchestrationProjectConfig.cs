using UnityEngine;
using System.Collections.Generic;

namespace Thesis.Orchestration.EditorTools
{
    [System.Serializable]
    public class OrchestrationConfiguredObjectEntry
    {
        [Tooltip("Optional prefab asset used for scene lookup by prefab name and for runtime spawn when needed.")]
        public GameObject prefab;

        [Tooltip("Optional exact scene object path or name. Runtime tries this before prefab-name lookup.")]
        public string sceneObjectPathOrName;

        [Tooltip("If no scene object is found and a prefab is assigned, instantiate it at runtime.")]
        public bool spawnIfMissing;

        public Vector3 spawnPosition;
        public Vector3 spawnEulerAngles;
    }

    public enum OrchestrationBackendType
    {
        PUN2 = 0,
        Fusion = 1,
        FishNet = 2,
        UnityNetcode = 3,
        Mirror = 4
    }

    public enum OrchestrationAutomatedScenario
    {
        OwnershipPingPong2Users = 0,
        SimultaneousContention2Users = 1,
        RapidOwnershipHandoffs2Users = 2,
        TransformReplication2Users = 3,
        OpenLoopRequestStress2Users = 4
    }

    public enum OrchestrationScenarioExecutionMode
    {
        ClosedLoop = 0,
        OpenLoop = 1
    }

    public enum OrchestrationAiMode
    {
        Disabled = 0,
        Advisor = 1,
        Controlled = 2
    }

    public enum OrchestrationAiProviderType
    {
        LocalHeuristic = 0,
        ExternalHttpJson = 1,
        OpenAIProxy = 2,
        UnitySentisBridge = 3,
        UnityMLAgentsBridge = 4,
        Custom = 5,
        RemoteHeuristicGateway = 6
    }

    [CreateAssetMenu(
        fileName = "OrchestrationProjectConfig",
        menuName = "Thesis/Orchestration/Project Config")]
    public class OrchestrationProjectConfig : ScriptableObject
    {
        [Header("Backend")]
        public OrchestrationBackendType backendType = OrchestrationBackendType.PUN2;

        [Header("Bootstrap")]
        public bool autoBootstrapOnSceneLoad = true;
        public bool spawnTelemetryUI = true;
        public bool dontDestroyRuntimeRoots = false;

        [Header("System Creation")]
        public bool createDefaultUI = true;
        public bool createAvatarSpawner = true;

        [Header("Runtime Root Prefabs")]
        public GameObject pun2RuntimeRootPrefab;
        public GameObject fusionRuntimeRootPrefab;
        public GameObject fishNetRuntimeRootPrefab;
        public GameObject unityNetcodeRuntimeRootPrefab;
        public GameObject mirrorRuntimeRootPrefab;
        public GameObject telemetryUiRootPrefab;

        [Header("Package Source Prefabs")]
        public GameObject networkingCanvasSourcePrefab;
        public GameObject masterDashboardCanvasSourcePrefab;
        public GameObject userStatusCardSourcePrefab;
        public GameObject networkAvatarSourcePrefab;

        [Header("Branding")]
        public Sprite orchestrationLogoSprite;
        public string orchestrationLogoResourcesPath = "OrchestrationAILogo";

        [Header("Resources Export")]
        [Tooltip("Must stay under Assets/Resources so runtime Resources.Load works.")]
        public string resourcesExportRootFolder = "Assets/Resources/AIMultiplayerAssistant";

        [Header("Runtime Resources Paths (auto-updated by export)")]
        public string projectConfigResourcesPath = "AIMultiplayerAssistant/OrchestrationProjectConfig";
        public string networkingCanvasPrefabPath = "AIMultiplayerAssistant/UIs/NetworkingCanvas";
        public string masterDashboardCanvasPrefabPath = "AIMultiplayerAssistant/UIs/MasterDashboardCanvas";
        public string userStatusCardPrefabPath = "AIMultiplayerAssistant/UIs/UserStatusCard";
        public string networkAvatarPrefabPath = "AIMultiplayerAssistant/NetworkAvatar";

        [Header("Configured Shared Objects")]
        public List<OrchestrationConfiguredObjectEntry> microscopeEquivalentObjects = new();
        public List<OrchestrationConfiguredObjectEntry> standaloneSharedObjects = new();

        [Header("Names")]
        public string orchestrationRootName = "OrchestrationSystem";
        public string runtimeManagersGroupName = "RuntimeManagers";
        public string registriesGroupName = "Registries";
        public string uiGroupName = "UI";
        public string optionalGroupName = "Optional";

        [Header("Connection")]
        public bool autoConnectOnStartup = true;
        public bool autoJoinLobbyOnConnect = true;

        [Header("Automated Benchmark Scenario")]
        public bool enableAutomatedBenchmarkScenario = false;
        public OrchestrationAutomatedScenario automatedScenario = OrchestrationAutomatedScenario.OwnershipPingPong2Users;
        public OrchestrationScenarioExecutionMode automatedScenarioMode = OrchestrationScenarioExecutionMode.ClosedLoop;
        public bool automatedScenarioAutoBeginSession = true;
        public bool automatedScenarioAutoExportOnComplete = false;
        public string automatedScenarioIdOverride = "";
        public string automatedScenarioProtocolVersion = "1.5";
        [Min(2)] public int automatedScenarioExpectedParticipants = 2;
        [Min(1)] public int automatedScenarioRepeatCount = 3;
        [Min(0f)] public float automatedScenarioWarmupSeconds = 5f;
        [Min(0.25f)] public float automatedScenarioHoldSeconds = 10f;
        [Min(0f)] public float automatedScenarioCooldownSeconds = 1f;
        [Min(1f)] public float automatedScenarioStepTimeoutSeconds = 15f;
        public bool automatedScenarioPreQueueNextUser = true;
        public bool automatedScenarioManipulateChildObject = true;
        public string automatedScenarioManipulatedChildName = "Controlled Turret";
        [Min(0f)] public float automatedScenarioManipulationStartDelaySeconds = 1f;
        [Min(0.1f)] public float automatedScenarioManipulationDurationSeconds = 6f;
        public Vector3 automatedScenarioManipulationEulerDelta = new Vector3(0f, 60f, 0f);
        public bool automatedScenarioResetPerformanceSamplesAfterWarmup = true;
        public bool automatedScenarioSuppressInfoLogsDuringMeasurement = true;
        public bool automatedScenarioValidateFinalRotation = true;
        [Min(0f)] public float automatedScenarioFinalRotationSettleSeconds = 1f;
        [Min(0.01f)] public float automatedScenarioRotationToleranceDegrees = 2f;

        [Header("Backend Evaluation (TOPSIS)")]
        [Tooltip("Leave empty to use the newest benchmark scenario shared by all five backends.")]
        public string backendEvaluationScenarioId = "";
        [Min(1)] public int backendEvaluationTargetReplicatesPerBackend = 10;
        [Range(0f, 1f)] public float backendEvaluationResponsivenessWeight = 0.35f;
        [Range(0f, 1f)] public float backendEvaluationRuntimeWeight = 0.35f;
        [Range(0f, 1f)] public float backendEvaluationCoordinationWeight = 0.20f;
        [Range(0f, 1f)] public float backendEvaluationReliabilityWeight = 0.10f;

        [Header("AI Orchestration")]
        public bool enableAiOrchestration = false;
        public OrchestrationAiMode aiMode = OrchestrationAiMode.Advisor;
        public OrchestrationAiProviderType aiProvider = OrchestrationAiProviderType.LocalHeuristic;
        public bool aiFallbackToFcfs = true;
        public bool aiRecordAdvisorMetrics = true;
        public bool aiAllowExternalProviderControl = false;
        [Min(50f)] public float aiDecisionTimeoutMs = 1500f;
        [Min(0f)] public float aiMinimumDecisionIntervalMs = 250f;
        [Min(0f)] public float aiSuggestReleaseAfterHolderSeconds = 10f;
        [Range(0f, 1f)] public float aiFairnessWeight = 0.65f;
        [Range(0f, 1f)] public float aiFcfsWeight = 0.35f;
        public string aiExternalEndpointUrl = "";
        public string aiExternalAuthorizationHeader = "";
        public string aiModelName = "gpt-4.1-mini";
        [TextArea(2, 6)] public string aiSystemPrompt =
            "You are an orchestration advisor for a multiplayer virtual laboratory. Return a safe JSON decision only.";

        [Header("Debug")]
        public bool verboseLogs = true;

        public string ResolveAutomatedScenarioId()
        {
            if (!string.IsNullOrWhiteSpace(automatedScenarioIdOverride))
                return SanitizeScenarioId(automatedScenarioIdOverride);

            string protocolId = automatedScenario switch
            {
                OrchestrationAutomatedScenario.SimultaneousContention2Users => "S2",
                OrchestrationAutomatedScenario.RapidOwnershipHandoffs2Users => "S3",
                OrchestrationAutomatedScenario.TransformReplication2Users => "S4",
                OrchestrationAutomatedScenario.OpenLoopRequestStress2Users => "S5",
                _ => "S1"
            };

            return SanitizeScenarioId(
                $"{protocolId}_{automatedScenario}_{automatedScenarioMode}_{automatedScenarioHoldSeconds:0.#}s_x{automatedScenarioRepeatCount}");
        }

        private static string SanitizeScenarioId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "S0_Unspecified";

            string trimmed = value.Trim();
            char[] chars = trimmed.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                    chars[i] = '_';
            }

            return new string(chars);
        }
    }
}
