using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace Thesis.Orchestration.Telemetry
{
    public class RuntimePerformanceSampler : MonoBehaviour
    {
        public static RuntimePerformanceSampler Instance { get; private set; }

        [Serializable]
        public class RuntimePerformanceRow
        {
            public string runId;
            public string backend;
            public string scenarioId;

            public string timestampUtc;
            public float fps;
            public float frameTimeMs;

            public float totalReservedMemoryMb;
            public float totalAllocatedMemoryMb;
            public float monoUsedMemoryMb;
            public float monoHeapSizeMb;

            public int gcCollectionCountGen0;
            public int gcCollectionCountGen1;
            public int gcCollectionCountGen2;
        }

        [Serializable]
        public class RuntimePerformanceSummary
        {
            public float averageFps;
            public float minFps;
            public float maxFps;

            public float averageFrameTimeMs;
            public float minFrameTimeMs;
            public float maxFrameTimeMs;

            public float averageTotalReservedMemoryMb;
            public float maxTotalReservedMemoryMb;

            public float averageTotalAllocatedMemoryMb;
            public float maxTotalAllocatedMemoryMb;

            public float averageMonoUsedMemoryMb;
            public float maxMonoUsedMemoryMb;

            public float averageMonoHeapSizeMb;
            public float maxMonoHeapSizeMb;

            public int gcCollectionsGen0;
            public int gcCollectionsGen1;
            public int gcCollectionsGen2;
            public int sampleCount;
        }

        [Header("Sampling")]
        [SerializeField] private bool samplingEnabled = true;
        [SerializeField] private float sampleIntervalSeconds = 0.5f;
        [SerializeField] private bool autoStartSampling = true;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        [Header("Warmup")]
        [SerializeField] private bool ignoreWarmupPeriod = true;
        [SerializeField] private float warmupDurationSeconds = 3f;

        private float samplingElapsedTime;

        private readonly List<RuntimePerformanceRow> rows = new();

        private float sampleTimer;
        private bool isSampling;

        private int baselineGcGen0;
        private int baselineGcGen1;
        private int baselineGcGen2;

        public IReadOnlyList<RuntimePerformanceRow> Rows => rows;
        public bool IsSampling => isSampling;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            if (autoStartSampling)
                StartSampling();
        }

        private void Update()
        {
            if (!samplingEnabled || !isSampling)
                return;

            sampleTimer += Time.unscaledDeltaTime;
            samplingElapsedTime += Time.unscaledDeltaTime;

            if (ignoreWarmupPeriod && samplingElapsedTime < warmupDurationSeconds)
                return;

            if (sampleTimer < sampleIntervalSeconds)
                return;

            sampleTimer = 0f;
            CaptureSample();
        }

        public void StartSampling()
        {
            isSampling = true;
            sampleTimer = 0f;
            samplingElapsedTime = 0f;

            baselineGcGen0 = GC.CollectionCount(0);
            baselineGcGen1 = GC.CollectionCount(1);
            baselineGcGen2 = GC.CollectionCount(2);

            Log("Runtime performance sampling started.");
        }

        public void StopSampling()
        {
            isSampling = false;
            Log("Runtime performance sampling stopped.");
        }

        public void ClearAll()
        {
            rows.Clear();
            sampleTimer = 0f;
            samplingElapsedTime = 0f;

            baselineGcGen0 = GC.CollectionCount(0);
            baselineGcGen1 = GC.CollectionCount(1);
            baselineGcGen2 = GC.CollectionCount(2);

            Log("Runtime performance samples cleared.");
        }

        public void RestartSamplingForMeasuredWindow()
        {
            rows.Clear();
            sampleTimer = 0f;
            samplingElapsedTime = ignoreWarmupPeriod ? warmupDurationSeconds : 0f;
            isSampling = true;

            baselineGcGen0 = GC.CollectionCount(0);
            baselineGcGen1 = GC.CollectionCount(1);
            baselineGcGen2 = GC.CollectionCount(2);

            Log("Runtime performance sampling restarted for the measured scenario window.");
        }

        public RuntimePerformanceSummary BuildSummary()
        {
            RuntimePerformanceSummary summary = new RuntimePerformanceSummary();

            if (rows.Count == 0)
                return summary;

            summary.sampleCount = rows.Count;

            summary.minFps = float.MaxValue;
            summary.maxFps = float.MinValue;
            summary.minFrameTimeMs = float.MaxValue;
            summary.maxFrameTimeMs = float.MinValue;

            float fpsSum = 0f;
            float frameTimeSum = 0f;
            float reservedSum = 0f;
            float allocatedSum = 0f;
            float monoUsedSum = 0f;
            float monoHeapSum = 0f;

            float maxReserved = 0f;
            float maxAllocated = 0f;
            float maxMonoUsed = 0f;
            float maxMonoHeap = 0f;

            foreach (RuntimePerformanceRow row in rows)
            {
                fpsSum += row.fps;
                frameTimeSum += row.frameTimeMs;
                reservedSum += row.totalReservedMemoryMb;
                allocatedSum += row.totalAllocatedMemoryMb;
                monoUsedSum += row.monoUsedMemoryMb;
                monoHeapSum += row.monoHeapSizeMb;

                if (row.fps < summary.minFps) summary.minFps = row.fps;
                if (row.fps > summary.maxFps) summary.maxFps = row.fps;

                if (row.frameTimeMs < summary.minFrameTimeMs) summary.minFrameTimeMs = row.frameTimeMs;
                if (row.frameTimeMs > summary.maxFrameTimeMs) summary.maxFrameTimeMs = row.frameTimeMs;

                if (row.totalReservedMemoryMb > maxReserved) maxReserved = row.totalReservedMemoryMb;
                if (row.totalAllocatedMemoryMb > maxAllocated) maxAllocated = row.totalAllocatedMemoryMb;
                if (row.monoUsedMemoryMb > maxMonoUsed) maxMonoUsed = row.monoUsedMemoryMb;
                if (row.monoHeapSizeMb > maxMonoHeap) maxMonoHeap = row.monoHeapSizeMb;
            }

            summary.averageFps = fpsSum / rows.Count;
            summary.averageFrameTimeMs = frameTimeSum / rows.Count;
            summary.averageTotalReservedMemoryMb = reservedSum / rows.Count;
            summary.averageTotalAllocatedMemoryMb = allocatedSum / rows.Count;
            summary.averageMonoUsedMemoryMb = monoUsedSum / rows.Count;
            summary.averageMonoHeapSizeMb = monoHeapSum / rows.Count;

            summary.maxTotalReservedMemoryMb = maxReserved;
            summary.maxTotalAllocatedMemoryMb = maxAllocated;
            summary.maxMonoUsedMemoryMb = maxMonoUsed;
            summary.maxMonoHeapSizeMb = maxMonoHeap;

            summary.gcCollectionsGen0 = rows[rows.Count - 1].gcCollectionCountGen0;
            summary.gcCollectionsGen1 = rows[rows.Count - 1].gcCollectionCountGen1;
            summary.gcCollectionsGen2 = rows[rows.Count - 1].gcCollectionCountGen2;

            if (summary.minFps == float.MaxValue) summary.minFps = 0f;
            if (summary.maxFps == float.MinValue) summary.maxFps = 0f;
            if (summary.minFrameTimeMs == float.MaxValue) summary.minFrameTimeMs = 0f;
            if (summary.maxFrameTimeMs == float.MinValue) summary.maxFrameTimeMs = 0f;

            return summary;
        }

        public void SaveRuntimePerformanceCsvToFile(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,timestamp_utc,fps,frame_time_ms,total_reserved_memory_mb,total_allocated_memory_mb,mono_used_memory_mb,mono_heap_size_mb,gc_collection_count_gen0,gc_collection_count_gen1,gc_collection_count_gen2");

            foreach (RuntimePerformanceRow row in rows)
            {
                sb.AppendLine(
                    $"{Escape(row.runId)}," +
                    $"{Escape(row.backend)}," +
                    $"{Escape(row.scenarioId)}," +
                    $"{Escape(row.timestampUtc)}," +
                    $"{FormatFloat(row.fps)}," +
                    $"{FormatFloat(row.frameTimeMs)}," +
                    $"{FormatFloat(row.totalReservedMemoryMb)}," +
                    $"{FormatFloat(row.totalAllocatedMemoryMb)}," +
                    $"{FormatFloat(row.monoUsedMemoryMb)}," +
                    $"{FormatFloat(row.monoHeapSizeMb)}," +
                    $"{row.gcCollectionCountGen0}," +
                    $"{row.gcCollectionCountGen1}," +
                    $"{row.gcCollectionCountGen2}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public void SaveRuntimePerformanceSummaryCsvToFile(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            RuntimePerformanceSummary summary = BuildSummary();
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("run_id,backend,scenario_id,average_fps,min_fps,max_fps,average_frame_time_ms,min_frame_time_ms,max_frame_time_ms,average_total_reserved_memory_mb,max_total_reserved_memory_mb,average_total_allocated_memory_mb,max_total_allocated_memory_mb,average_mono_used_memory_mb,max_mono_used_memory_mb,average_mono_heap_size_mb,max_mono_heap_size_mb,gc_collections_gen0,gc_collections_gen1,gc_collections_gen2,sample_count");
            sb.AppendLine(
                $"{Escape(GetRunId())}," +
                $"{Escape(GetBackend())}," +
                $"{Escape(GetScenarioId())}," +
                $"{FormatFloat(summary.averageFps)}," +
                $"{FormatFloat(summary.minFps)}," +
                $"{FormatFloat(summary.maxFps)}," +
                $"{FormatFloat(summary.averageFrameTimeMs)}," +
                $"{FormatFloat(summary.minFrameTimeMs)}," +
                $"{FormatFloat(summary.maxFrameTimeMs)}," +
                $"{FormatFloat(summary.averageTotalReservedMemoryMb)}," +
                $"{FormatFloat(summary.maxTotalReservedMemoryMb)}," +
                $"{FormatFloat(summary.averageTotalAllocatedMemoryMb)}," +
                $"{FormatFloat(summary.maxTotalAllocatedMemoryMb)}," +
                $"{FormatFloat(summary.averageMonoUsedMemoryMb)}," +
                $"{FormatFloat(summary.maxMonoUsedMemoryMb)}," +
                $"{FormatFloat(summary.averageMonoHeapSizeMb)}," +
                $"{FormatFloat(summary.maxMonoHeapSizeMb)}," +
                $"{summary.gcCollectionsGen0}," +
                $"{summary.gcCollectionsGen1}," +
                $"{summary.gcCollectionsGen2}," +
                $"{summary.sampleCount}");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private void CaptureSample()
        {
            float fps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
            float frameTimeMs = Time.unscaledDeltaTime * 1000f;

            RuntimePerformanceRow row = new RuntimePerformanceRow
            {
                runId = GetRunId(),
                backend = GetBackend(),
                scenarioId = GetScenarioId(),
                timestampUtc = DateTime.UtcNow.ToString("o"),
                fps = fps,
                frameTimeMs = frameTimeMs,
                totalReservedMemoryMb = BytesToMb(Profiler.GetTotalReservedMemoryLong()),
                totalAllocatedMemoryMb = BytesToMb(Profiler.GetTotalAllocatedMemoryLong()),
                monoUsedMemoryMb = BytesToMb(Profiler.GetMonoUsedSizeLong()),
                monoHeapSizeMb = BytesToMb(Profiler.GetMonoHeapSizeLong()),
                gcCollectionCountGen0 = GC.CollectionCount(0) - baselineGcGen0,
                gcCollectionCountGen1 = GC.CollectionCount(1) - baselineGcGen1,
                gcCollectionCountGen2 = GC.CollectionCount(2) - baselineGcGen2
            };

            rows.Add(row);
        }

        private float BytesToMb(long bytes)
        {
            return bytes / (1024f * 1024f);
        }

        private string GetRunId()
        {
            return EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.RunId : "UnknownRun";
        }

        private string GetBackend()
        {
            return EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.BackendName : "UnknownBackend";
        }

        private string GetScenarioId()
        {
            return EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.ScenarioId : "UnknownScenario";
        }

        private string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        private string FormatFloat(float value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[RuntimePerformanceSampler] {message}", this);
        }
    }
}
