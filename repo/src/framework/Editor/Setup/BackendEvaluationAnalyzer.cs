using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.EditorTools
{
    internal static class BackendEvaluationAnalyzer
    {
        private static readonly string[] RequiredBackends =
        {
            "PUN2",
            "Fusion",
            "FishNet",
            "UnityNetcode",
            "Mirror"
        };

        internal sealed class Settings
        {
            public string scenarioId;
            public int targetReplicates;
            public double responsivenessWeight;
            public double runtimeWeight;
            public double coordinationWeight;
            public double reliabilityWeight;
        }

        internal sealed class Result
        {
            public string scenarioId;
            public int balancedRunCount;
            public int targetReplicates;
            public double responsivenessWeight;
            public double runtimeWeight;
            public double coordinationWeight;
            public double reliabilityWeight;
            public double leaderStabilityPercent;
            public double leaderMargin;
            public string error;
            public readonly List<string> warnings = new();
            public readonly List<BackendScore> rankings = new();

            public bool IsValid => string.IsNullOrWhiteSpace(error) && rankings.Count == RequiredBackends.Length;
            public bool IsPilot => balancedRunCount < targetReplicates;
        }

        internal sealed class BackendScore
        {
            public int rank;
            public string backend;
            public double score;
            public int selectedRunCount;
            public int validRunCount;
            public int invalidRunCount;
            public double reliability;
            public double connectLatencyMs;
            public double sessionStartDelayMs;
            public int authorityTransferEventCount;
            public double transferSuccessRate;
            public double transferLatencyMs;
            public double requestFulfillmentRate;
            public double fairnessIndex;
            public double averageFps;
            public double minFps;
            public double averageFrameTimeMs;
            public double allocatedMemoryMb;
            public double finalRotationErrorDegrees;
        }

        private sealed class RunMetrics
        {
            public DateTime lastWriteUtc;
            public bool valid;
            public double connectLatencyMs;
            public double sessionStartDelayMs;
            public int authorityTransferCount;
            public double transferSuccessRate;
            public double transferLatencyMs;
            public double requestCount;
            public double grantedCount;
            public double fairnessIndex;
            public double averageFps;
            public double minFps;
            public double averageFrameTimeMs;
            public double allocatedMemoryMb;
            public double finalRotationErrorDegrees;
        }

        private sealed class Criterion
        {
            public string name;
            public bool benefit;
            public double weight;
            public Func<BackendScore, double> value;
        }

        internal static IReadOnlyList<string> FindCommonScenarioIds()
        {
            string root = GetExportsRoot();
            if (!Directory.Exists(root))
                return Array.Empty<string>();

            HashSet<string> common = null;
            foreach (string backend in RequiredBackends)
            {
                string backendPath = Path.Combine(root, backend);
                if (!Directory.Exists(backendPath))
                    return Array.Empty<string>();

                HashSet<string> scenarios = Directory.GetDirectories(backendPath)
                    .Where(HasCompleteRun)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                common = common == null
                    ? scenarios
                    : new HashSet<string>(common.Intersect(scenarios, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            }

            return (common ?? new HashSet<string>())
                .OrderByDescending(GetScenarioLastWriteUtc)
                .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal static Result Evaluate(Settings settings)
        {
            Result result = new Result();
            IReadOnlyList<string> commonScenarios = FindCommonScenarioIds();
            if (commonScenarios.Count == 0)
            {
                result.error = "No complete benchmark scenario is shared by all five backends.";
                return result;
            }

            string scenarioId = ResolveScenarioId(settings?.scenarioId, commonScenarios);
            result.scenarioId = scenarioId;
            result.targetReplicates = Math.Max(1, settings?.targetReplicates ?? 10);

            NormalizeWeights(
                settings?.responsivenessWeight ?? 0.35d,
                settings?.runtimeWeight ?? 0.35d,
                settings?.coordinationWeight ?? 0.20d,
                settings?.reliabilityWeight ?? 0.10d,
                out result.responsivenessWeight,
                out result.runtimeWeight,
                out result.coordinationWeight,
                out result.reliabilityWeight);

            Dictionary<string, List<RunMetrics>> runsByBackend = new(StringComparer.OrdinalIgnoreCase);
            foreach (string backend in RequiredBackends)
            {
                List<RunMetrics> runs = LoadRuns(backend, scenarioId);
                if (runs.Count == 0)
                {
                    result.error = $"{backend} has no complete run for scenario '{scenarioId}'.";
                    return result;
                }

                runsByBackend[backend] = runs;
            }

            result.balancedRunCount = runsByBackend.Values.Min(runs => runs.Count);
            foreach (string backend in RequiredBackends)
            {
                List<RunMetrics> selectedRuns = runsByBackend[backend]
                    .OrderByDescending(run => run.lastWriteUtc)
                    .Take(result.balancedRunCount)
                    .ToList();
                List<RunMetrics> validRuns = selectedRuns.Where(run => run.valid).ToList();

                if (validRuns.Count == 0)
                {
                    result.error = $"{backend} has no valid run in the balanced cohort.";
                    return result;
                }

                result.rankings.Add(new BackendScore
                {
                    backend = backend,
                    selectedRunCount = selectedRuns.Count,
                    validRunCount = validRuns.Count,
                    invalidRunCount = selectedRuns.Count - validRuns.Count,
                    reliability = (double)validRuns.Count / selectedRuns.Count,
                    connectLatencyMs = Mean(validRuns, run => run.connectLatencyMs),
                    sessionStartDelayMs = Mean(validRuns, run => run.sessionStartDelayMs),
                    authorityTransferEventCount = validRuns.Sum(run => run.authorityTransferCount),
                    transferSuccessRate = Mean(
                        validRuns.Where(run => run.authorityTransferCount > 0),
                        run => run.transferSuccessRate),
                    transferLatencyMs = Mean(
                        validRuns.Where(run => run.authorityTransferCount > 0),
                        run => run.transferLatencyMs),
                    requestFulfillmentRate = Mean(
                        validRuns,
                        run => run.requestCount > 0d
                            ? Math.Min(1d, run.grantedCount / run.requestCount)
                            : double.NaN),
                    fairnessIndex = Mean(validRuns, run => run.fairnessIndex),
                    averageFps = Mean(validRuns, run => run.averageFps),
                    minFps = Mean(validRuns, run => run.minFps),
                    averageFrameTimeMs = Mean(validRuns, run => run.averageFrameTimeMs),
                    allocatedMemoryMb = Mean(validRuns, run => run.allocatedMemoryMb),
                    finalRotationErrorDegrees = Mean(validRuns, run => run.finalRotationErrorDegrees)
                });
            }

            List<Criterion> criteria = BuildCriteria(result);
            List<string> unavailableCriteria = criteria
                .Where(criterion => result.rankings.Any(row => !IsFiniteNonNegative(criterion.value(row))))
                .Select(criterion => criterion.name)
                .ToList();
            if (unavailableCriteria.Count > 0)
            {
                result.error = "Cannot rank because required metrics are missing: " + string.Join(", ", unavailableCriteria);
                return result;
            }

            Dictionary<string, double> baselineScores = CalculateTopsis(result.rankings, criteria);
            ApplyScoresAndRanks(result.rankings, baselineScores);

            if (result.rankings.Count > 1)
                result.leaderMargin = result.rankings[0].score - result.rankings[1].score;

            result.leaderStabilityPercent = CalculateLeaderStability(result, result.rankings[0].backend);

            if (result.IsPilot)
            {
                result.warnings.Add(
                    $"Pilot evidence only: {result.balancedRunCount} balanced run(s) per backend; " +
                    $"the configured target is {result.targetReplicates}. Use a power analysis to set the final paper sample size.");
            }

            if (!CanIncludeAuthorityTransfer(result.rankings))
            {
                result.warnings.Add(
                    "Physical authority-transfer latency and success were omitted from this scenario because at least one " +
                    "backend emitted no transfer event. Request fulfillment and queue fairness remain comparable.");
            }

            result.warnings.Add(
                "Scores describe this scenario and weighting profile only. Bandwidth, packet loss, WAN jitter, server cost, " +
                "and scalability are not measured by the current same-machine benchmark.");
            return result;
        }

        internal static string ExportReport(Result result)
        {
            if (result == null || !result.IsValid)
                return null;

            string outputFolder = Path.Combine(GetExportsRoot(), "ComparisonSummaries");
            Directory.CreateDirectory(outputFolder);
            string path = Path.Combine(outputFolder, $"backend_evaluation_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            StringBuilder csv = new StringBuilder();
            csv.AppendLine(
                "method,normalization,criterion_weighting,sensitivity_protocol,evidence_level," +
                 "scenario_id,cohort_policy,balanced_runs_per_backend,target_replicates,rank,backend,topsis_score," +
                 "valid_runs,invalid_runs,reliability,mean_connect_latency_ms,mean_session_start_delay_ms," +
                 "authority_transfer_event_count,authority_transfer_success_rate,mean_authority_transfer_latency_ms," +
                 "request_fulfillment_rate,jains_fairness_index_wait," +
                 "average_fps,min_fps,average_frame_time_ms,average_allocated_memory_mb," +
                 "responsiveness_weight,runtime_weight,coordination_weight,reliability_weight," +
                 "leader_margin,leader_weight_sensitivity_percent,mean_final_rotation_error_degrees");

            foreach (BackendScore row in result.rankings)
            {
                csv.AppendLine(
                    $"TOPSIS,VectorNorm,EqualWithinCategory,EachCategoryPlusMinus25Percent," +
                    $"{(result.IsPilot ? "PilotDescriptive" : "ReplicationTargetMet")}," +
                     $"{Quote(result.scenarioId)},LatestEqualCountCompleteRuns,{result.balancedRunCount},{result.targetReplicates}," +
                     $"{row.rank},{Quote(row.backend)},{Format(row.score)},{row.validRunCount},{row.invalidRunCount}," +
                     $"{Format(row.reliability)},{Format(row.connectLatencyMs)},{Format(row.sessionStartDelayMs)}," +
                     $"{row.authorityTransferEventCount},{Format(row.transferSuccessRate)},{Format(row.transferLatencyMs)}," +
                     $"{Format(row.requestFulfillmentRate)},{Format(row.fairnessIndex)}," +
                     $"{Format(row.averageFps)},{Format(row.minFps)},{Format(row.averageFrameTimeMs)}," +
                     $"{Format(row.allocatedMemoryMb)},{Format(result.responsivenessWeight)}," +
                    $"{Format(result.runtimeWeight)},{Format(result.coordinationWeight)}," +
                    $"{Format(result.reliabilityWeight)},{Format(result.leaderMargin)}," +
                    $"{Format(result.leaderStabilityPercent)},{Format(row.finalRotationErrorDegrees)}");
            }

            File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
            return path;
        }

        private static List<Criterion> BuildCriteria(Result result)
        {
            bool includeAuthorityTransfer = CanIncludeAuthorityTransfer(result.rankings);
            bool includeFinalRotationError =
                result.rankings.All(row => IsFiniteNonNegative(row.finalRotationErrorDegrees));
            return BuildCriteria(
                result.responsivenessWeight,
                result.runtimeWeight,
                result.coordinationWeight,
                result.reliabilityWeight,
                includeAuthorityTransfer,
                includeFinalRotationError);
        }

        private static List<Criterion> BuildCriteria(
            double responsivenessWeight,
            double runtimeWeight,
            double coordinationWeight,
            double reliabilityWeight,
            bool includeAuthorityTransfer,
            bool includeFinalRotationError)
        {
            List<Criterion> responsivenessCriteria = new List<Criterion>
            {
                new() { name = "Connect latency", benefit = false, value = row => row.connectLatencyMs },
                new() { name = "Session visibility delay", benefit = false, value = row => row.sessionStartDelayMs }
            };
            if (includeAuthorityTransfer)
            {
                responsivenessCriteria.Add(
                    new Criterion
                    {
                        name = "Authority transfer latency",
                        benefit = false,
                        value = row => row.transferLatencyMs
                    });
            }

            foreach (Criterion criterion in responsivenessCriteria)
                criterion.weight = responsivenessWeight / responsivenessCriteria.Count;

            List<Criterion> runtimeCriteria = new List<Criterion>
            {
                new() { name = "Average FPS", benefit = true, value = row => row.averageFps },
                new() { name = "Minimum FPS", benefit = true, value = row => row.minFps },
                new() { name = "Frame time", benefit = false, value = row => row.averageFrameTimeMs },
                new() { name = "Allocated memory", benefit = false, value = row => row.allocatedMemoryMb }
            };
            foreach (Criterion criterion in runtimeCriteria)
                criterion.weight = runtimeWeight / runtimeCriteria.Count;

            List<Criterion> coordinationCriteria = new List<Criterion>
            {
                new() { name = "Request fulfillment", benefit = true, value = row => row.requestFulfillmentRate },
                new() { name = "Jain fairness", benefit = true, value = row => row.fairnessIndex }
            };
            if (includeAuthorityTransfer)
            {
                coordinationCriteria.Add(
                    new Criterion
                    {
                        name = "Transfer success",
                        benefit = true,
                        value = row => row.transferSuccessRate
                    });
            }

            if (includeFinalRotationError)
            {
                coordinationCriteria.Add(
                    new Criterion
                    {
                        name = "Final rotation error",
                        benefit = false,
                        value = row => row.finalRotationErrorDegrees
                    });
            }

            foreach (Criterion criterion in coordinationCriteria)
                criterion.weight = coordinationWeight / coordinationCriteria.Count;

            List<Criterion> criteria = new List<Criterion>();
            criteria.AddRange(responsivenessCriteria);
            criteria.AddRange(runtimeCriteria);
            criteria.AddRange(coordinationCriteria);
            criteria.Add(
                new Criterion
                {
                    name = "Valid-run reliability",
                    benefit = true,
                    weight = reliabilityWeight,
                    value = row => row.reliability
                });
            return criteria;
        }

        private static bool CanIncludeAuthorityTransfer(IReadOnlyList<BackendScore> rows)
        {
            return rows.All(
                row => row.authorityTransferEventCount > 0 &&
                       IsFiniteNonNegative(row.transferSuccessRate) &&
                       IsFiniteNonNegative(row.transferLatencyMs));
        }

        private static Dictionary<string, double> CalculateTopsis(
            IReadOnlyList<BackendScore> rows,
            IReadOnlyList<Criterion> criteria)
        {
            int rowCount = rows.Count;
            int criterionCount = criteria.Count;
            double[,] weighted = new double[rowCount, criterionCount];

            for (int criterionIndex = 0; criterionIndex < criterionCount; criterionIndex++)
            {
                Criterion criterion = criteria[criterionIndex];
                double denominator = Math.Sqrt(rows.Sum(row => Math.Pow(criterion.value(row), 2d)));
                if (denominator <= double.Epsilon)
                    denominator = 1d;

                for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
                    weighted[rowIndex, criterionIndex] = criterion.value(rows[rowIndex]) / denominator * criterion.weight;
            }

            double[] idealBest = new double[criterionCount];
            double[] idealWorst = new double[criterionCount];
            for (int criterionIndex = 0; criterionIndex < criterionCount; criterionIndex++)
            {
                List<double> values = Enumerable.Range(0, rowCount)
                    .Select(rowIndex => weighted[rowIndex, criterionIndex])
                    .ToList();
                idealBest[criterionIndex] = criteria[criterionIndex].benefit ? values.Max() : values.Min();
                idealWorst[criterionIndex] = criteria[criterionIndex].benefit ? values.Min() : values.Max();
            }

            Dictionary<string, double> scores = new(StringComparer.OrdinalIgnoreCase);
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                double bestDistanceSquared = 0d;
                double worstDistanceSquared = 0d;
                for (int criterionIndex = 0; criterionIndex < criterionCount; criterionIndex++)
                {
                    bestDistanceSquared += Math.Pow(weighted[rowIndex, criterionIndex] - idealBest[criterionIndex], 2d);
                    worstDistanceSquared += Math.Pow(weighted[rowIndex, criterionIndex] - idealWorst[criterionIndex], 2d);
                }

                double bestDistance = Math.Sqrt(bestDistanceSquared);
                double worstDistance = Math.Sqrt(worstDistanceSquared);
                double totalDistance = bestDistance + worstDistance;
                scores[rows[rowIndex].backend] = totalDistance <= double.Epsilon
                    ? 0.5d
                    : worstDistance / totalDistance;
            }

            return scores;
        }

        private static void ApplyScoresAndRanks(
            List<BackendScore> rows,
            IReadOnlyDictionary<string, double> scores)
        {
            foreach (BackendScore row in rows)
                row.score = scores[row.backend];

            rows.Sort((left, right) =>
            {
                int scoreComparison = right.score.CompareTo(left.score);
                return scoreComparison != 0
                    ? scoreComparison
                    : string.Compare(left.backend, right.backend, StringComparison.OrdinalIgnoreCase);
            });

            for (int index = 0; index < rows.Count; index++)
                rows[index].rank = index + 1;
        }

        private static double CalculateLeaderStability(Result result, string baselineLeader)
        {
            double[] baselineWeights =
            {
                result.responsivenessWeight,
                result.runtimeWeight,
                result.coordinationWeight,
                result.reliabilityWeight
            };
            int matchingLeaders = 1;
            int profileCount = 1;

            for (int weightIndex = 0; weightIndex < baselineWeights.Length; weightIndex++)
            {
                foreach (double multiplier in new[] { 0.75d, 1.25d })
                {
                    double[] varied = (double[])baselineWeights.Clone();
                    varied[weightIndex] *= multiplier;
                    NormalizeWeights(
                        varied[0], varied[1], varied[2], varied[3],
                        out double responsiveness,
                        out double runtime,
                        out double coordination,
                        out double reliability);

                    Dictionary<string, double> scores = CalculateTopsis(
                        result.rankings,
                         BuildCriteria(
                             responsiveness,
                             runtime,
                             coordination,
                             reliability,
                             CanIncludeAuthorityTransfer(result.rankings),
                             result.rankings.All(row => IsFiniteNonNegative(row.finalRotationErrorDegrees))));
                    string leader = scores
                        .OrderByDescending(pair => pair.Value)
                        .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .First().Key;
                    if (string.Equals(leader, baselineLeader, StringComparison.OrdinalIgnoreCase))
                        matchingLeaders++;
                    profileCount++;
                }
            }

            return 100d * matchingLeaders / profileCount;
        }

        private static List<RunMetrics> LoadRuns(string backend, string scenarioId)
        {
            string scenarioPath = Path.Combine(GetExportsRoot(), backend, scenarioId);
            if (!Directory.Exists(scenarioPath))
                return new List<RunMetrics>();

            List<RunMetrics> runs = new List<RunMetrics>();
            foreach (string runDirectory in Directory.GetDirectories(scenarioPath)
                         .Where(IsCompleteRunDirectory)
                         .Where(IsCurrentProtocolRun))
            {
                Dictionary<string, string> scenario = ReadSingleRowCsv(
                    Path.Combine(runDirectory, "scenario_metrics_summary.csv"));
                Dictionary<string, string> master = ReadSingleRowCsv(
                    Path.Combine(runDirectory, "run_master_summary.csv"));
                if (scenario.Count == 0 || master.Count == 0)
                    continue;

                bool valid = TryGetBool(scenario, "scenario_valid", out bool scenarioValid) && scenarioValid;
                runs.Add(new RunMetrics
                {
                    lastWriteUtc = Directory.GetLastWriteTimeUtc(runDirectory),
                     valid = valid,
                     connectLatencyMs = GetDouble(master, "mean_connect_latency_ms"),
                     sessionStartDelayMs = GetDouble(master, "mean_session_start_visible_delay_ms"),
                     authorityTransferCount = GetInt(master, "authority_transfer_count"),
                     transferSuccessRate = GetDouble(master, "authority_transfer_success_rate"),
                     transferLatencyMs = GetDouble(master, "mean_authority_transfer_latency_ms"),
                     requestCount = GetDouble(master, "request_count"),
                     grantedCount = GetDouble(master, "granted_count"),
                     fairnessIndex = GetDouble(master, "fairness_jains_index_wait"),
                    averageFps = GetDouble(master, "average_fps"),
                    minFps = GetDouble(master, "min_fps"),
                    averageFrameTimeMs = GetDouble(master, "average_frame_time_ms"),
                    allocatedMemoryMb = GetDouble(master, "average_allocated_memory_mb"),
                    finalRotationErrorDegrees =
                        TryGetBool(master, "scenario_has_final_rotation_outcome", out bool hasRotationOutcome) &&
                        hasRotationOutcome
                            ? GetDouble(master, "scenario_final_rotation_error_degrees")
                            : double.NaN
                });
            }

            return runs;
        }

        private static bool HasCompleteRun(string scenarioDirectory)
        {
            return Directory.Exists(scenarioDirectory) &&
                   Directory.GetDirectories(scenarioDirectory)
                       .Any(runDirectory =>
                           IsCompleteRunDirectory(runDirectory) &&
                           IsCurrentProtocolRun(runDirectory));
        }

        private static bool IsCompleteRunDirectory(string runDirectory)
        {
            string folderName = Path.GetFileName(runDirectory);
            bool hasRequiredFiles =
                File.Exists(Path.Combine(runDirectory, "scenario_metrics_summary.csv")) &&
                File.Exists(Path.Combine(runDirectory, "run_master_summary.csv")) &&
                File.Exists(Path.Combine(runDirectory, "runtime_performance_summary.csv"));
            if (!hasRequiredFiles)
                return false;

            return !folderName.StartsWith("Run_", StringComparison.OrdinalIgnoreCase) ||
                   File.Exists(Path.Combine(runDirectory, "export_manifest.csv"));
        }

        private static bool IsCurrentProtocolRun(string runDirectory)
        {
            Dictionary<string, string> metadata = ReadSingleRowCsv(
                Path.Combine(runDirectory, "run_metadata.csv"));
            return metadata.TryGetValue("protocol_version", out string protocolVersion) &&
                   string.Equals(
                       protocolVersion,
                       BenchmarkScenarioPresetCatalog.ProtocolVersion,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> ReadSingleRowCsv(string path)
        {
            if (!File.Exists(path))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string[] lines = File.ReadAllLines(path);
            if (lines.Length < 2)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string[] headers = SplitCsvLine(lines[0]);
            string[] values = SplitCsvLine(lines[1]);
            Dictionary<string, string> row = new(StringComparer.OrdinalIgnoreCase);
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

        private static double GetDouble(IReadOnlyDictionary<string, string> row, string key)
        {
            return row.TryGetValue(key, out string value) &&
                   double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : double.NaN;
        }

        private static int GetInt(IReadOnlyDictionary<string, string> row, string key)
        {
            return row.TryGetValue(key, out string value) &&
                   int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : 0;
        }

        private static bool TryGetBool(
            IReadOnlyDictionary<string, string> row,
            string key,
            out bool value)
        {
            value = false;
            return row.TryGetValue(key, out string rawValue) && bool.TryParse(rawValue, out value);
        }

        private static double Mean(IEnumerable<RunMetrics> runs, Func<RunMetrics, double> selector)
        {
            List<double> values = runs.Select(selector).Where(value => !double.IsNaN(value)).ToList();
            return values.Count > 0 ? values.Average() : double.NaN;
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }

        private static string ResolveScenarioId(string requested, IReadOnlyList<string> commonScenarios)
        {
            if (!string.IsNullOrWhiteSpace(requested))
            {
                string match = commonScenarios.FirstOrDefault(
                    scenario => string.Equals(scenario, requested.Trim(), StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            return commonScenarios[0];
        }

        private static DateTime GetScenarioLastWriteUtc(string scenarioId)
        {
            DateTime latest = DateTime.MinValue;
            foreach (string backend in RequiredBackends)
            {
                string path = Path.Combine(GetExportsRoot(), backend, scenarioId);
                if (Directory.Exists(path))
                    latest = latest > Directory.GetLastWriteTimeUtc(path) ? latest : Directory.GetLastWriteTimeUtc(path);
            }
            return latest;
        }

        private static void NormalizeWeights(
            double responsiveness,
            double runtime,
            double coordination,
            double reliability,
            out double normalizedResponsiveness,
            out double normalizedRuntime,
            out double normalizedCoordination,
            out double normalizedReliability)
        {
            responsiveness = Math.Max(0d, responsiveness);
            runtime = Math.Max(0d, runtime);
            coordination = Math.Max(0d, coordination);
            reliability = Math.Max(0d, reliability);
            double total = responsiveness + runtime + coordination + reliability;
            if (total <= double.Epsilon)
            {
                normalizedResponsiveness = 0.25d;
                normalizedRuntime = 0.25d;
                normalizedCoordination = 0.25d;
                normalizedReliability = 0.25d;
                return;
            }

            normalizedResponsiveness = responsiveness / total;
            normalizedRuntime = runtime / total;
            normalizedCoordination = coordination / total;
            normalizedReliability = reliability / total;
        }

        private static string GetExportsRoot()
        {
            return EvaluationExportPathUtility.GetExportsRoot();
        }

        private static string Quote(string value)
        {
            return $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
        }

        private static string Format(double value)
        {
            return value.ToString("F6", CultureInfo.InvariantCulture);
        }
    }
}
