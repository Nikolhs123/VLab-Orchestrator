using System;
using System.Collections;
using System.Text;
using Thesis.Orchestration.Core;
using Thesis.Orchestration.Core.Policies;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Telemetry;
using UnityEngine;
using UnityEngine.Networking;

namespace Thesis.Orchestration.AI
{
    [DisallowMultipleComponent]
    public class OrchestrationAiService : MonoBehaviour
    {
        public static OrchestrationAiService Instance { get; private set; }

        [SerializeField] private OrchestrationProjectConfig projectConfig;
        [SerializeField] private bool verboseLogs = true;

        private IOrchestrationAiDecisionProvider localHeuristicProvider;
        private IOrchestrationAiDecisionProvider externalHttpProvider;
        private float lastDecisionRealtime;

        public bool IsEnabled =>
            projectConfig != null &&
            projectConfig.enableAiOrchestration &&
            projectConfig.aiMode != OrchestrationAiMode.Disabled;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveConfig();
            BuildProviders();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Configure(OrchestrationProjectConfig config)
        {
            projectConfig = config;
            BuildProviders();
        }

        public void ApplyControlledPolicyIfNeeded(QueueManagerCore queueManager)
        {
            if (queueManager == null || !IsEnabled)
                return;

            if (projectConfig.aiMode != OrchestrationAiMode.Controlled)
                return;

            IOrchestrationAiDecisionProvider provider = ResolveSynchronousProvider();
            if (provider == null)
            {
                Log("AI controlled mode requested, but selected provider is not synchronous. Keeping FCFS fallback.");
                return;
            }

            queueManager.SetPolicy(new OrchestrationAiAllocationPolicy(
                projectConfig,
                provider,
                new FCFSPolicy()));

            Log($"Applied AI controlled allocation policy with provider {provider.ProviderName}.");
        }

        public void EvaluateQueueDecision(
            string trigger,
            QueueManagerCore queueManager,
            string instrumentId,
            QueueDecision baselineDecision,
            string holderBefore,
            string holderAfter,
            System.Collections.Generic.IEnumerable<string> queueSnapshot)
        {
            if (!IsEnabled || !projectConfig.aiRecordAdvisorMetrics)
                return;

            if (Time.realtimeSinceStartup - lastDecisionRealtime < projectConfig.aiMinimumDecisionIntervalMs / 1000f)
                return;

            lastDecisionRealtime = Time.realtimeSinceStartup;

            OrchestrationAiStateSnapshot snapshot = OrchestrationAiSnapshotBuilder.Build(
                projectConfig,
                trigger,
                queueManager,
                instrumentId,
                baselineDecision,
                holderBefore,
                holderAfter,
                queueSnapshot);

            if (UsesExternalBridge(projectConfig.aiProvider))
            {
                StartCoroutine(EvaluateExternalBridge(snapshot));
                return;
            }

            IOrchestrationAiDecisionProvider provider = ResolveSynchronousProvider();
            double start = Time.realtimeSinceStartupAsDouble;
            OrchestrationAiDecision decision = provider != null
                ? provider.Evaluate(snapshot)
                : OrchestrationAiDecision.Fallback(projectConfig.aiProvider.ToString(), projectConfig.aiMode.ToString(), "No synchronous provider available.");

            double latencyMs = (Time.realtimeSinceStartupAsDouble - start) * 1000d;
            AiDecisionRecorder.Instance?.RecordDecision(snapshot, decision, latencyMs, false);
        }

        private IEnumerator EvaluateExternalBridge(OrchestrationAiStateSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(projectConfig.aiExternalEndpointUrl))
            {
                OrchestrationAiDecision fallback = OrchestrationAiDecision.Fallback(
                    projectConfig.aiProvider.ToString(),
                    projectConfig.aiMode.ToString(),
                    "External AI provider selected but endpoint URL is empty.");
                AiDecisionRecorder.Instance?.RecordDecision(snapshot, fallback, 0d, false);
                yield break;
            }

            string payload = JsonUtility.ToJson(snapshot);
            byte[] body = Encoding.UTF8.GetBytes(payload);
            double start = Time.realtimeSinceStartupAsDouble;

            using UnityWebRequest request = new UnityWebRequest(projectConfig.aiExternalEndpointUrl, "POST");
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(1, Mathf.CeilToInt(projectConfig.aiDecisionTimeoutMs / 1000f));
            request.SetRequestHeader("Content-Type", "application/json");

            if (!string.IsNullOrWhiteSpace(projectConfig.aiExternalAuthorizationHeader))
                request.SetRequestHeader("Authorization", projectConfig.aiExternalAuthorizationHeader);

            yield return request.SendWebRequest();

            double latencyMs = (Time.realtimeSinceStartupAsDouble - start) * 1000d;
            OrchestrationAiDecision decision;

            if (request.result != UnityWebRequest.Result.Success)
            {
                decision = OrchestrationAiDecision.Fallback(
                    projectConfig.aiProvider.ToString(),
                    projectConfig.aiMode.ToString(),
                    $"External AI request failed: {request.error}");
            }
            else
            {
                try
                {
                    decision = JsonUtility.FromJson<OrchestrationAiDecision>(request.downloadHandler.text);
                    if (decision == null)
                    {
                        decision = OrchestrationAiDecision.Fallback(
                            projectConfig.aiProvider.ToString(),
                            projectConfig.aiMode.ToString(),
                            "External AI response could not be parsed.");
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(decision.provider))
                            decision.provider = projectConfig.aiProvider.ToString();
                        if (string.IsNullOrWhiteSpace(decision.mode))
                            decision.mode = projectConfig.aiMode.ToString();
                    }
                }
                catch (Exception ex)
                {
                    decision = OrchestrationAiDecision.Fallback(
                        projectConfig.aiProvider.ToString(),
                        projectConfig.aiMode.ToString(),
                        $"External AI response parse exception: {ex.Message}");
                }
            }

            AiDecisionRecorder.Instance?.RecordDecision(snapshot, decision, latencyMs, false);
        }

        private bool ResolveConfig()
        {
            if (projectConfig != null)
                return true;

            projectConfig = Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");
            return projectConfig != null;
        }

        private void BuildProviders()
        {
            if (projectConfig == null)
                ResolveConfig();

            localHeuristicProvider = new LocalHeuristicAiDecisionProvider(projectConfig);
            externalHttpProvider = new ExternalHttpAiDecisionProvider(projectConfig);
        }

        private IOrchestrationAiDecisionProvider ResolveSynchronousProvider()
        {
            if (projectConfig == null)
                return null;

            return projectConfig.aiProvider == OrchestrationAiProviderType.LocalHeuristic
                ? localHeuristicProvider
                : ResolveExternalSynchronousProvider();
        }

        private IOrchestrationAiDecisionProvider ResolveExternalSynchronousProvider()
        {
            if (projectConfig == null || !projectConfig.aiAllowExternalProviderControl)
                return null;

            return UsesExternalBridge(projectConfig.aiProvider)
                ? externalHttpProvider
                : null;
        }

        private bool UsesExternalBridge(OrchestrationAiProviderType providerType)
        {
            return providerType == OrchestrationAiProviderType.ExternalHttpJson ||
                   providerType == OrchestrationAiProviderType.OpenAIProxy ||
                   providerType == OrchestrationAiProviderType.UnitySentisBridge ||
                   providerType == OrchestrationAiProviderType.UnityMLAgentsBridge ||
                   providerType == OrchestrationAiProviderType.RemoteHeuristicGateway ||
                   providerType == OrchestrationAiProviderType.Custom;
        }

        private void Log(string message)
        {
            if (!verboseLogs && (projectConfig == null || !projectConfig.verboseLogs))
                return;

            Debug.Log($"[OrchestrationAiService] {message}", this);
        }
    }
}
