using System;
using System.IO;
using Thesis.Orchestration.EditorTools;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    public class EvaluationRunContext : MonoBehaviour
    {
        public static EvaluationRunContext Instance { get; private set; }

        [Header("Config")]
        [SerializeField] private OrchestrationProjectConfig projectConfig;

        [Header("Run Metadata")]
        [SerializeField] private string scenarioId = "S0_Unspecified";
        [SerializeField] private bool sameMachine = true;
        [SerializeField] private string hostMachineNameOverride = "";
        [SerializeField] private string clientMachineNameOverride = "";
        [SerializeField] private float pingMs = 0f;
        [SerializeField] private float downloadMbps = 0f;
        [SerializeField] private float uploadMbps = 0f;
        [SerializeField] private float simulatedLatencyMs = 0f;
        [SerializeField] private float simulatedPacketLossPercent = 0f;
        [SerializeField] private string notes = "";

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private string runId;
        private DateTime runStartUtc;

        public string RunId => runId;
        public string ScenarioId => scenarioId;
        public DateTime RunStartUtc => runStartUtc;
        public bool SameMachine => sameMachine;
        public float PingMs => pingMs;
        public float DownloadMbps => downloadMbps;
        public float UploadMbps => uploadMbps;
        public float SimulatedLatencyMs => simulatedLatencyMs;
        public float SimulatedPacketLossPercent => simulatedPacketLossPercent;
        public string Notes => notes;

        public string BackendName
        {
            get
            {
                ResolveProjectConfig();
                return projectConfig != null ? projectConfig.backendType.ToString() : "UnknownBackend";
            }
        }

        public string HostMachineName =>
            string.IsNullOrWhiteSpace(hostMachineNameOverride) ? Environment.MachineName : hostMachineNameOverride;

        public string ClientMachineName =>
            string.IsNullOrWhiteSpace(clientMachineNameOverride) ? Environment.MachineName : clientMachineNameOverride;

        public string UnityVersion => Application.unityVersion;
        public string BuildType => Application.isEditor ? "Editor" : "Build";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveProjectConfig();
            BeginNewRun();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void BeginNewRun()
        {
            runStartUtc = DateTime.UtcNow;
            runId = $"{BackendName}_{scenarioId}_{runStartUtc:yyyyMMdd_HHmmss_fff}";
            Log($"Started new evaluation run: {runId}");
        }

        public void SetScenarioId(string newScenarioId)
        {
            if (string.IsNullOrWhiteSpace(newScenarioId))
                return;

            scenarioId = newScenarioId.Trim();
            BeginNewRun();
        }

        public string GetRunFolderPath()
        {
            string root = EvaluationExportPathUtility.GetExportsRoot();
            string backend = BackendName;
            string scenario = scenarioId;
            string scenarioFolder = Path.Combine(root, backend, scenario);
            string folder = Path.Combine(scenarioFolder, $"Run_{runStartUtc:yyyyMMdd_HHmmss_fff}");
            Directory.CreateDirectory(scenarioFolder);
            return folder;
        }

        private void ResolveProjectConfig()
        {
            if (projectConfig != null)
                return;

            projectConfig = Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[EvaluationRunContext] {message}", this);
        }
    }
}
