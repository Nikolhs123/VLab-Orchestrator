using System;
using System.IO;
using Thesis.Orchestration.Analytics;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Thesis.Orchestration.Telemetry
{
    [DisallowMultipleComponent]
    public class EvaluationMetricsManager : MonoBehaviour
    {
        private const string ExportManifestFileName = "export_manifest.csv";

        public static EvaluationMetricsManager Instance { get; private set; }

        [Header("Core References")]
        [SerializeField] private EvaluationRunContext runContext;
        [SerializeField] private TelemetryLoggerCore telemetryLogger;
        [SerializeField] private UsageTimingTracker usageTimingTracker;

        [Header("Evaluation Helpers")]
        [SerializeField] private SessionLifecycleTracker sessionLifecycleTracker;
        [SerializeField] private QueueTransitionRecorder queueTransitionRecorder;
        [SerializeField] private AuthorityTransferRecorder authorityTransferRecorder;
        [SerializeField] private RuntimePerformanceSampler runtimePerformanceSampler;
        [SerializeField] private ScenarioMetricsRecorder scenarioMetricsRecorder;
        [SerializeField] private AiDecisionRecorder aiDecisionRecorder;
        [SerializeField] private EvaluationSummaryBuilder evaluationSummaryBuilder;
        [SerializeField] private BackendComparisonBuilder backendComparisonBuilder;

        [Header("Setup")]
        [SerializeField] private bool autoCreateMissingHelpers = true;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        public EvaluationRunContext RunContext => runContext;
        public TelemetryLoggerCore TelemetryLogger => telemetryLogger;
        public UsageTimingTracker UsageTimingTracker => usageTimingTracker;
        public SessionLifecycleTracker SessionLifecycleTracker => sessionLifecycleTracker;
        public QueueTransitionRecorder QueueTransitionRecorder => queueTransitionRecorder;
        public AuthorityTransferRecorder AuthorityTransferRecorder => authorityTransferRecorder;
        public RuntimePerformanceSampler RuntimePerformanceSampler => runtimePerformanceSampler;
        public ScenarioMetricsRecorder ScenarioMetricsRecorder => scenarioMetricsRecorder;
        public AiDecisionRecorder AiDecisionRecorder => aiDecisionRecorder;
        public EvaluationSummaryBuilder EvaluationSummaryBuilder => evaluationSummaryBuilder;
        public BackendComparisonBuilder BackendComparisonBuilder => backendComparisonBuilder;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            EnsureHelpers();
            ResolveReferences();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying && autoCreateMissingHelpers)
            {
                EnsureHelpers();
                ResolveReferences();
            }
        }
#endif

        private void EnsureHelpers()
        {
            if (!autoCreateMissingHelpers)
                return;

            if (runContext == null)
                runContext = GetOrAdd<EvaluationRunContext>();

            if (telemetryLogger == null)
                telemetryLogger = GetOrAdd<TelemetryLoggerCore>();

            if (usageTimingTracker == null)
                usageTimingTracker = GetOrAdd<UsageTimingTracker>();

            if (sessionLifecycleTracker == null)
                sessionLifecycleTracker = GetOrAdd<SessionLifecycleTracker>();

            if (queueTransitionRecorder == null)
                queueTransitionRecorder = GetOrAdd<QueueTransitionRecorder>();

            if (authorityTransferRecorder == null)
                authorityTransferRecorder = GetOrAdd<AuthorityTransferRecorder>();

            if (runtimePerformanceSampler == null)
                runtimePerformanceSampler = GetOrAdd<RuntimePerformanceSampler>();

            if (scenarioMetricsRecorder == null)
                scenarioMetricsRecorder = GetOrAdd<ScenarioMetricsRecorder>();

            if (aiDecisionRecorder == null)
                aiDecisionRecorder = GetOrAdd<AiDecisionRecorder>();

            if (evaluationSummaryBuilder == null)
                evaluationSummaryBuilder = GetOrAdd<EvaluationSummaryBuilder>();

            if (backendComparisonBuilder == null)
                backendComparisonBuilder = GetOrAdd<BackendComparisonBuilder>();
        }

        private void ResolveReferences()
        {
            if (runContext == null)
                runContext = EvaluationRunContext.Instance;
            if (runContext == null)
                runContext = GetComponent<EvaluationRunContext>();

            if (telemetryLogger == null)
                telemetryLogger = TelemetryLoggerCore.Instance;
            if (telemetryLogger == null)
                telemetryLogger = GetComponent<TelemetryLoggerCore>();

            if (usageTimingTracker == null)
                usageTimingTracker = UsageTimingTracker.Instance;
            if (usageTimingTracker == null)
                usageTimingTracker = GetComponent<UsageTimingTracker>();

            if (sessionLifecycleTracker == null)
                sessionLifecycleTracker = SessionLifecycleTracker.Instance;
            if (sessionLifecycleTracker == null)
                sessionLifecycleTracker = GetComponent<SessionLifecycleTracker>();

            if (queueTransitionRecorder == null)
                queueTransitionRecorder = QueueTransitionRecorder.Instance;
            if (queueTransitionRecorder == null)
                queueTransitionRecorder = GetComponent<QueueTransitionRecorder>();

            if (authorityTransferRecorder == null)
                authorityTransferRecorder = AuthorityTransferRecorder.Instance;
            if (authorityTransferRecorder == null)
                authorityTransferRecorder = GetComponent<AuthorityTransferRecorder>();

            if (runtimePerformanceSampler == null)
                runtimePerformanceSampler = RuntimePerformanceSampler.Instance;
            if (runtimePerformanceSampler == null)
                runtimePerformanceSampler = GetComponent<RuntimePerformanceSampler>();

            if (scenarioMetricsRecorder == null)
                scenarioMetricsRecorder = ScenarioMetricsRecorder.Instance;
            if (scenarioMetricsRecorder == null)
                scenarioMetricsRecorder = GetComponent<ScenarioMetricsRecorder>();

            if (aiDecisionRecorder == null)
                aiDecisionRecorder = AiDecisionRecorder.Instance;
            if (aiDecisionRecorder == null)
                aiDecisionRecorder = GetComponent<AiDecisionRecorder>();

            if (evaluationSummaryBuilder == null)
                evaluationSummaryBuilder = EvaluationSummaryBuilder.Instance;
            if (evaluationSummaryBuilder == null)
                evaluationSummaryBuilder = GetComponent<EvaluationSummaryBuilder>();

            if (backendComparisonBuilder == null)
                backendComparisonBuilder = BackendComparisonBuilder.Instance;
            if (backendComparisonBuilder == null)
                backendComparisonBuilder = GetComponent<BackendComparisonBuilder>();
        }

        public void BeginNewRun(string scenarioId = null)
        {
            ResolveReferences();

            if (runContext == null)
            {
                Log("Cannot begin run: EvaluationRunContext missing.");
                return;
            }

            ClearAllRecorders();

            if (!string.IsNullOrWhiteSpace(scenarioId))
                runContext.SetScenarioId(scenarioId);
            else
                runContext.BeginNewRun();

            runtimePerformanceSampler?.StartSampling();

            Log($"Started new run: {runContext.RunId}");
        }

        public void ClearAllRecorders()
        {
            ResolveReferences();

            telemetryLogger?.ClearAll();
            usageTimingTracker?.ClearAll();
            sessionLifecycleTracker?.ClearAll();
            queueTransitionRecorder?.ClearAll();
            authorityTransferRecorder?.ClearAll();
            runtimePerformanceSampler?.ClearAll();
            scenarioMetricsRecorder?.ClearAll();
            aiDecisionRecorder?.ClearAll();

            Log("Cleared all evaluation recorders.");
        }

        public string ExportAll()
        {
            ResolveReferences();

            if (runContext == null)
            {
                Log("Cannot export: EvaluationRunContext missing.");
                return null;
            }

            string requestedRunFolder = runContext.GetRunFolderPath();
            string stagingRoot = Path.Combine(EvaluationExportPathUtility.GetExportsRoot(), ".staging");
            string pendingId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string stagingFolder = Path.Combine(
                stagingRoot,
                $"P_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{pendingId}");

            try
            {
                Directory.CreateDirectory(stagingRoot);
                Directory.CreateDirectory(stagingFolder);

                telemetryLogger?.SaveRunMetadataCsvToFile(Path.Combine(stagingFolder, "run_metadata.csv"));
                sessionLifecycleTracker?.SaveSessionLifecycleCsvToFile(Path.Combine(stagingFolder, "session_lifecycle.csv"));
                queueTransitionRecorder?.SaveQueueTransitionsCsvToFile(Path.Combine(stagingFolder, "queue_transitions.csv"));
                authorityTransferRecorder?.SaveAuthorityTransfersCsvToFile(Path.Combine(stagingFolder, "authority_transfers.csv"));
                runtimePerformanceSampler?.SaveRuntimePerformanceCsvToFile(Path.Combine(stagingFolder, "runtime_performance.csv"));
                runtimePerformanceSampler?.SaveRuntimePerformanceSummaryCsvToFile(Path.Combine(stagingFolder, "runtime_performance_summary.csv"));
                scenarioMetricsRecorder?.SaveScenarioEventsCsvToFile(Path.Combine(stagingFolder, "scenario_events.csv"));
                scenarioMetricsRecorder?.SaveScenarioAssertionsCsvToFile(Path.Combine(stagingFolder, "scenario_assertions.csv"));
                scenarioMetricsRecorder?.SaveScenarioMetricsSummaryCsvToFile(Path.Combine(stagingFolder, "scenario_metrics_summary.csv"));
                aiDecisionRecorder?.SaveAiDecisionsCsvToFile(Path.Combine(stagingFolder, "ai_decisions.csv"));
                aiDecisionRecorder?.SaveAiMetricsSummaryCsvToFile(Path.Combine(stagingFolder, "ai_metrics_summary.csv"));

                telemetryLogger?.SaveTelemetryCsvToFile(Path.Combine(stagingFolder, "telemetry_events.csv"));
                telemetryLogger?.SaveInteractionAnalyticsCsvToFile(Path.Combine(stagingFolder, "interaction_analytics.csv"));
                usageTimingTracker?.SaveUserStatsCsvToFile(Path.Combine(stagingFolder, "user_stats.csv"));
                usageTimingTracker?.SaveInstrumentStatsCsvToFile(Path.Combine(stagingFolder, "instrument_stats.csv"));

                evaluationSummaryBuilder?.SaveAllSummariesToFolder(stagingFolder);

                string runFolder = GetAvailableRunFolder(requestedRunFolder);
                PublishStagedExport(stagingFolder, runFolder);
                TryDeletePublishedStagingFolder(stagingFolder);

#if UNITY_EDITOR
                try
                {
                    AssetDatabase.Refresh();
                }
                catch (Exception refreshException)
                {
                    Debug.LogWarning(
                        $"[EvaluationMetricsManager] Export completed, but AssetDatabase refresh failed: " +
                        refreshException.Message,
                        this);
                }
#endif

                Log($"Exported evaluation data to: {runFolder}");
                return runFolder;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[EvaluationMetricsManager] Export failed. Staged data was retained and any incomplete " +
                    $"destination will be ignored. " +
                    $"Staging='{stagingFolder}'. Error={ex}",
                    this);
                return null;
            }
        }

        private static void PublishStagedExport(string stagingFolder, string runFolder)
        {
            Directory.CreateDirectory(runFolder);

            string[] stagedFiles = Directory.GetFiles(stagingFolder, "*", SearchOption.TopDirectoryOnly);
            for (int index = 0; index < stagedFiles.Length; index++)
            {
                string sourceFile = stagedFiles[index];
                string destinationFile = Path.Combine(runFolder, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, destinationFile, false);
            }

            string manifest =
                "status,completed_utc,file_count\n" +
                $"complete,{DateTime.UtcNow:O},{stagedFiles.Length}\n";
            File.WriteAllText(Path.Combine(runFolder, ExportManifestFileName), manifest);
        }

        private static void TryDeletePublishedStagingFolder(string stagingFolder)
        {
            try
            {
                if (Directory.Exists(stagingFolder))
                    Directory.Delete(stagingFolder, true);
            }
            catch (Exception exception)
            {
                // The published run remains valid because its manifest was already written.
                Debug.LogWarning(
                    $"[EvaluationMetricsManager] Export completed, but its staging copy could not be removed: " +
                    exception.Message);
            }
        }

        private static string GetAvailableRunFolder(string requestedRunFolder)
        {
            if (!Directory.Exists(requestedRunFolder))
                return requestedRunFolder;

            for (int suffix = 2; suffix < 1000; suffix++)
            {
                string candidate = $"{requestedRunFolder}_{suffix}";
                if (!Directory.Exists(candidate))
                    return candidate;
            }

            return $"{requestedRunFolder}_{Guid.NewGuid():N}";
        }

        public string BuildBackendComparisonSummary()
        {
            ResolveReferences();
            return backendComparisonBuilder != null ? backendComparisonBuilder.BuildComparisonFromExports() : null;
        }

        public static EvaluationMetricsManager ResolveOrCreate(MonoBehaviour context = null)
        {
            EvaluationMetricsManager manager = Instance;
            if (manager == null)
                manager = UnityEngine.Object.FindFirstObjectByType<EvaluationMetricsManager>();

            if (manager != null)
            {
                manager.EnsureHelpers();
                manager.ResolveReferences();
                return manager;
            }

            if (!Application.isPlaying)
                return null;

            GameObject host = null;

            if (TelemetryLoggerCore.Instance != null)
                host = TelemetryLoggerCore.Instance.gameObject;
            else if (UsageTimingTracker.Instance != null)
                host = UsageTimingTracker.Instance.gameObject;
            else if (context != null)
                host = context.gameObject;

            if (host == null)
                host = new GameObject("EvaluationMetricsManager");

            manager = host.GetComponent<EvaluationMetricsManager>();
            manager = manager != null ? manager : host.AddComponent<EvaluationMetricsManager>();
            manager.EnsureHelpers();
            manager.ResolveReferences();
            return manager;
        }

        private T GetOrAdd<T>() where T : Component
        {
            T existing = GetComponent<T>();
            if (existing != null)
                return existing;

            return gameObject.AddComponent<T>();
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[EvaluationMetricsManager] {message}", this);
        }
    }
}
