using System;
using System.Collections.Generic;
using UnityEngine;

namespace VLabOrchestrator.Runtime
{
    public enum OrchestrationBackendType
    {
        PUN2 = 0,
        Fusion = 1,
        UnityNetcode = 2,
        FishNet = 3,
        Mirror = 4
    }

    public enum OrchestrationBenchmarkScenario
    {
        BaselineFcfs = 0,
        ContentionBurst = 1,
        RapidHandoff = 2,
        TransformReplication = 3,
        OpenLoopStress = 4
    }

    [Serializable]
    public sealed class ConfiguredSharedPrefab
    {
        public string stableObjectId = "shared_object_01";
        public GameObject prefab;
        public string controlledChildName;
        public bool spawnIfMissing = true;
    }

    [CreateAssetMenu(
        fileName = "OrchestrationProjectConfig",
        menuName = "VLab Orchestrator/Project Config")]
    public sealed class OrchestrationProjectConfig : ScriptableObject
    {
        public const string CanonicalBenchmarkProtocolVersion = "1.5";

        [Header("Backend")]
        public OrchestrationBackendType backendType = OrchestrationBackendType.UnityNetcode;

        [Header("Reusable Visual Sources")]
        public GameObject participantVisualPrefab;
        public GameObject primarySharedInstrumentPrefab;
        public List<ConfiguredSharedPrefab> additionalSharedInstruments = new();
        public List<ConfiguredSharedPrefab> standaloneSharedObjects = new();

        [Header("Shared Instrument Contract")]
        public string primaryInstrumentId = "generic_instrument_01";
        public string controlledChildName = "Controlled Turret";
        public string participantAId = "Researcher_A";
        public string participantBId = "Researcher_B";

        [Header("Runtime")]
        public bool autoBootstrapOnSceneLoad = true;
        public bool createTelemetry = true;
        public bool verboseLogs = true;

        [Header("Automated Benchmark")]
        public bool enableAutomatedBenchmark;
        public bool autoExportOnComplete = true;
        [Tooltip("Keeps every publication scenario locked to its versioned paper protocol.")]
        public bool useCanonicalPaperPreset = true;
        public OrchestrationBenchmarkScenario benchmarkScenario =
            OrchestrationBenchmarkScenario.BaselineFcfs;
        [Min(2)] public int expectedParticipants = 2;
        [Min(1)] public int repeatCount = 3;
        [Min(0f)] public float warmupSeconds = 5f;
        [Min(0.25f)] public float holdSeconds = 10f;
        [Min(0f)] public float cooldownSeconds = 1f;
        [Min(1f)] public float stepTimeoutSeconds = 15f;
        public bool preQueueNextParticipant = true;
        public bool manipulateControlledChild = true;
        [Min(0f)] public float manipulationStartDelaySeconds = 1f;
        [Min(0.01f)] public float manipulationDurationSeconds = 6f;
        public Vector3 controlledRotationDelta = new(0f, 60f, 0f);
        public bool validateFinalRotation = true;
        [Min(0f)] public float finalRotationSettleSeconds = 1f;
        [Min(0.01f)] public float rotationToleranceDegrees = 2f;

        [Header("Evaluation")]
        [Min(1)] public int targetReplicatesPerBackend = 10;
        [Range(0f, 1f)] public float responsivenessWeight = 0.35f;
        [Range(0f, 1f)] public float runtimeWeight = 0.35f;
        [Range(0f, 1f)] public float coordinationWeight = 0.20f;
        [Range(0f, 1f)] public float reliabilityWeight = 0.10f;

        public string ResolveScenarioId()
        {
            return benchmarkScenario switch
            {
                OrchestrationBenchmarkScenario.ContentionBurst =>
                    "S2_Contention_Burst_2Users_5Cycles_5s_Turret30",
                OrchestrationBenchmarkScenario.RapidHandoff =>
                    "S3_Rapid_Handoff_2Users_10Cycles_2.5s_Turret15",
                OrchestrationBenchmarkScenario.TransformReplication =>
                    "S4_Transform_Replication_2Users_5Cycles_8s_Turret90",
                OrchestrationBenchmarkScenario.OpenLoopStress =>
                    "S5_OpenLoop_Stress_2Users_20Cycles_0_5s",
                _ => "S1_Baseline_FCFS_2Users_3Cycles_10s_Turret60"
            };
        }

        public void ApplySelectedPaperPreset()
        {
            expectedParticipants = 2;
            warmupSeconds = 5f;
            rotationToleranceDegrees = 2f;

            switch (benchmarkScenario)
            {
                case OrchestrationBenchmarkScenario.ContentionBurst:
                    repeatCount = 5;
                    holdSeconds = 5f;
                    cooldownSeconds = 1f;
                    stepTimeoutSeconds = 15f;
                    preQueueNextParticipant = false;
                    manipulateControlledChild = true;
                    manipulationStartDelaySeconds = 0.5f;
                    manipulationDurationSeconds = 3f;
                    controlledRotationDelta = new Vector3(0f, 30f, 0f);
                    validateFinalRotation = true;
                    finalRotationSettleSeconds = 1f;
                    break;

                case OrchestrationBenchmarkScenario.RapidHandoff:
                    repeatCount = 10;
                    holdSeconds = 2.5f;
                    cooldownSeconds = 0.25f;
                    stepTimeoutSeconds = 10f;
                    preQueueNextParticipant = true;
                    manipulateControlledChild = true;
                    manipulationStartDelaySeconds = 0.25f;
                    manipulationDurationSeconds = 1.5f;
                    controlledRotationDelta = new Vector3(0f, 15f, 0f);
                    validateFinalRotation = true;
                    finalRotationSettleSeconds = 1f;
                    break;

                case OrchestrationBenchmarkScenario.TransformReplication:
                    repeatCount = 5;
                    holdSeconds = 8f;
                    cooldownSeconds = 1f;
                    stepTimeoutSeconds = 15f;
                    preQueueNextParticipant = true;
                    manipulateControlledChild = true;
                    manipulationStartDelaySeconds = 0.5f;
                    manipulationDurationSeconds = 6f;
                    controlledRotationDelta = new Vector3(0f, 90f, 0f);
                    validateFinalRotation = true;
                    finalRotationSettleSeconds = 1.5f;
                    break;

                case OrchestrationBenchmarkScenario.OpenLoopStress:
                    repeatCount = 20;
                    holdSeconds = 0.5f;
                    cooldownSeconds = 0.1f;
                    stepTimeoutSeconds = 10f;
                    preQueueNextParticipant = false;
                    manipulateControlledChild = false;
                    manipulationStartDelaySeconds = 0f;
                    manipulationDurationSeconds = 0.25f;
                    controlledRotationDelta = Vector3.zero;
                    validateFinalRotation = false;
                    finalRotationSettleSeconds = 1f;
                    break;

                default:
                    repeatCount = 3;
                    holdSeconds = 10f;
                    cooldownSeconds = 1f;
                    stepTimeoutSeconds = 15f;
                    preQueueNextParticipant = true;
                    manipulateControlledChild = true;
                    manipulationStartDelaySeconds = 1f;
                    manipulationDurationSeconds = 6f;
                    controlledRotationDelta = new Vector3(0f, 60f, 0f);
                    validateFinalRotation = true;
                    finalRotationSettleSeconds = 1f;
                    break;
            }
        }
    }
}
