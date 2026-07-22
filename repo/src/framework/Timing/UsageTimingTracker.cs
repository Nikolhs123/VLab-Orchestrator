using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Thesis.Orchestration.Analytics
{
    /// <summary>
    /// Central timing/analytics brain for queue-based shared instrument usage.
    ///
    /// Tracks:
    /// - per-user waiting durations
    /// - per-user active usage durations
    /// - per-user counts (requests, grants, releases, timeouts)
    /// - per-instrument totals
    /// </summary>
    public class UsageTimingTracker : MonoBehaviour
    {
        public static UsageTimingTracker Instance { get; private set; }

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private readonly Dictionary<string, UserUsageStats> userStats = new();
        private readonly Dictionary<string, InstrumentUsageStats> instrumentStats = new();

        public IReadOnlyDictionary<string, UserUsageStats> UserStats => userStats;
        public IReadOnlyDictionary<string, InstrumentUsageStats> InstrumentStats => instrumentStats;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[UsageTimingTracker] Duplicate instance found. Destroying duplicate.", this);
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

        // ----------------------------------------------------
        // Public tracking API
        // ----------------------------------------------------

        public void RegisterRequest(string userId, string instrumentId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(instrumentId))
                return;

            UserUsageStats user = GetOrCreateUserStats(userId);
            InstrumentUsageStats instrument = GetOrCreateInstrumentStats(instrumentId);

            user.RequestCount++;

            if (!user.IsWaiting)
            {
                user.IsWaiting = true;
                user.WaitStartUtc = DateTime.UtcNow;
            }

            instrument.RequestCount++;

            Log($"Request registered. User={userId}, Instrument={instrumentId}");
        }

        public void RegisterGrant(string userId, string instrumentId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(instrumentId))
                return;

            DateTime now = DateTime.UtcNow;

            UserUsageStats user = GetOrCreateUserStats(userId);
            InstrumentUsageStats instrument = GetOrCreateInstrumentStats(instrumentId);

            user.GrantCount++;

            if (user.IsWaiting && user.WaitStartUtc.HasValue)
            {
                TimeSpan waitDuration = now - user.WaitStartUtc.Value;
                user.TotalWaitTime += waitDuration;
                user.LastWaitDuration = waitDuration;
            }

            user.IsWaiting = false;
            user.WaitStartUtc = null;

            user.IsUsingInstrument = true;
            user.UsageStartUtc = now;

            instrument.GrantCount++;
            instrument.CurrentHolderUserId = userId;
            instrument.IsCurrentlyInUse = true;
            instrument.CurrentUsageStartUtc = now;

            Log($"Grant registered. User={userId}, Instrument={instrumentId}");
        }

        public void RegisterRelease(string userId, string instrumentId)
        {
            RegisterUsageEnd(userId, instrumentId, wasTimeout: false);
        }

        public void RegisterTimeout(string userId, string instrumentId)
        {
            RegisterUsageEnd(userId, instrumentId, wasTimeout: true);
        }

        public void RegisterQueueRemoval(string userId, string instrumentId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(instrumentId))
                return;

            DateTime now = DateTime.UtcNow;

            UserUsageStats user = GetOrCreateUserStats(userId);

            if (user.IsWaiting && user.WaitStartUtc.HasValue)
            {
                TimeSpan waitDuration = now - user.WaitStartUtc.Value;
                user.TotalWaitTime += waitDuration;
                user.LastWaitDuration = waitDuration;
            }

            user.IsWaiting = false;
            user.WaitStartUtc = null;

            Log($"Queue removal registered. User={userId}, Instrument={instrumentId}");
        }

        // ----------------------------------------------------
        // Live query helpers
        // ----------------------------------------------------

        public TimeSpan GetCurrentUsageDuration(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return TimeSpan.Zero;

            if (!userStats.TryGetValue(userId, out var user))
                return TimeSpan.Zero;

            if (!user.IsUsingInstrument || !user.UsageStartUtc.HasValue)
                return TimeSpan.Zero;

            return DateTime.UtcNow - user.UsageStartUtc.Value;
        }

        public TimeSpan GetCurrentWaitDuration(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return TimeSpan.Zero;

            if (!userStats.TryGetValue(userId, out var user))
                return TimeSpan.Zero;

            if (!user.IsWaiting || !user.WaitStartUtc.HasValue)
                return TimeSpan.Zero;

            return DateTime.UtcNow - user.WaitStartUtc.Value;
        }

        public TimeSpan GetCurrentInstrumentUsageDuration(string instrumentId)
        {
            if (string.IsNullOrWhiteSpace(instrumentId))
                return TimeSpan.Zero;

            if (!instrumentStats.TryGetValue(instrumentId, out var instrument))
                return TimeSpan.Zero;

            if (!instrument.IsCurrentlyInUse || !instrument.CurrentUsageStartUtc.HasValue)
                return TimeSpan.Zero;

            return DateTime.UtcNow - instrument.CurrentUsageStartUtc.Value;
        }

        public UserUsageStats GetUserStats(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            userStats.TryGetValue(userId, out var stats);
            return stats;
        }

        public InstrumentUsageStats GetInstrumentStats(string instrumentId)
        {
            if (string.IsNullOrWhiteSpace(instrumentId))
                return null;

            instrumentStats.TryGetValue(instrumentId, out var stats);
            return stats;
        }

        public void ClearAll()
        {
            userStats.Clear();
            instrumentStats.Clear();
            Log("Cleared all timing stats.");
        }

        public void ClearAllStats()
        {
            ClearAll();
        }

        // ----------------------------------------------------
        // Internals
        // ----------------------------------------------------

        private void RegisterUsageEnd(string userId, string instrumentId, bool wasTimeout)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(instrumentId))
                return;

            DateTime now = DateTime.UtcNow;

            UserUsageStats user = GetOrCreateUserStats(userId);
            InstrumentUsageStats instrument = GetOrCreateInstrumentStats(instrumentId);

            if (user.IsUsingInstrument && user.UsageStartUtc.HasValue)
            {
                TimeSpan usageDuration = now - user.UsageStartUtc.Value;
                user.TotalUsageTime += usageDuration;
                user.LastUsageDuration = usageDuration;
            }

            user.IsUsingInstrument = false;
            user.UsageStartUtc = null;

            if (wasTimeout)
                user.TimeoutCount++;
            else
                user.ReleaseCount++;

            if (instrument.IsCurrentlyInUse && instrument.CurrentUsageStartUtc.HasValue)
            {
                TimeSpan instrumentUsageDuration = now - instrument.CurrentUsageStartUtc.Value;
                instrument.TotalUsageTime += instrumentUsageDuration;
                instrument.LastUsageDuration = instrumentUsageDuration;
            }

            instrument.IsCurrentlyInUse = false;
            instrument.CurrentUsageStartUtc = null;
            instrument.CurrentHolderUserId = null;

            if (wasTimeout)
                instrument.TimeoutCount++;
            else
                instrument.ReleaseCount++;

            Log($"{(wasTimeout ? "Timeout" : "Release")} registered. User={userId}, Instrument={instrumentId}");
        }

        private UserUsageStats GetOrCreateUserStats(string userId)
        {
            if (!userStats.TryGetValue(userId, out var stats))
            {
                stats = new UserUsageStats(userId);
                userStats.Add(userId, stats);
            }

            return stats;
        }

        private InstrumentUsageStats GetOrCreateInstrumentStats(string instrumentId)
        {
            if (!instrumentStats.TryGetValue(instrumentId, out var stats))
            {
                stats = new InstrumentUsageStats(instrumentId);
                instrumentStats.Add(instrumentId, stats);
            }

            return stats;
        }

        private TimeSpan GetTotalWaitIncludingCurrent(UserUsageStats stats, DateTime nowUtc)
        {
            if (stats == null)
                return TimeSpan.Zero;

            TimeSpan total = stats.TotalWaitTime;
            if (stats.IsWaiting && stats.WaitStartUtc.HasValue)
                total += nowUtc - stats.WaitStartUtc.Value;

            return total;
        }

        private TimeSpan GetTotalUsageIncludingCurrent(UserUsageStats stats, DateTime nowUtc)
        {
            if (stats == null)
                return TimeSpan.Zero;

            TimeSpan total = stats.TotalUsageTime;
            if (stats.IsUsingInstrument && stats.UsageStartUtc.HasValue)
                total += nowUtc - stats.UsageStartUtc.Value;

            return total;
        }

        private TimeSpan GetInstrumentTotalUsageIncludingCurrent(InstrumentUsageStats stats, DateTime nowUtc)
        {
            if (stats == null)
                return TimeSpan.Zero;

            TimeSpan total = stats.TotalUsageTime;
            if (stats.IsCurrentlyInUse && stats.CurrentUsageStartUtc.HasValue)
                total += nowUtc - stats.CurrentUsageStartUtc.Value;

            return total;
        }

        public string ExportUserStatsCsvString()
        {
            DateTime now = DateTime.UtcNow;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("UserId,RequestCount,GrantCount,ReleaseCount,TimeoutCount,IsWaiting,IsUsingInstrument,TotalWaitSeconds,LastWaitSeconds,TotalUsageSeconds,LastUsageSeconds");

            foreach (var kvp in userStats)
            {
                var stats = kvp.Value;
                if (stats == null)
                    continue;

                sb.AppendLine(
                    $"{CsvEscape(stats.UserId)}," +
                    $"{stats.RequestCount}," +
                    $"{stats.GrantCount}," +
                    $"{stats.ReleaseCount}," +
                    $"{stats.TimeoutCount}," +
                    $"{stats.IsWaiting}," +
                    $"{stats.IsUsingInstrument}," +
                    $"{GetTotalWaitIncludingCurrent(stats, now).TotalSeconds.ToString(CultureInfo.InvariantCulture)}," +
                    $"{stats.LastWaitDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)}," +
                    $"{GetTotalUsageIncludingCurrent(stats, now).TotalSeconds.ToString(CultureInfo.InvariantCulture)}," +
                    $"{stats.LastUsageDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)}");
            }

            return sb.ToString();
        }

        public string ExportInstrumentStatsCsvString()
        {
            DateTime now = DateTime.UtcNow;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("InstrumentId,RequestCount,GrantCount,ReleaseCount,TimeoutCount,IsCurrentlyInUse,CurrentHolderUserId,TotalUsageSeconds,LastUsageSeconds");

            foreach (var kvp in instrumentStats)
            {
                var stats = kvp.Value;
                if (stats == null)
                    continue;

                sb.AppendLine(
                    $"{CsvEscape(stats.InstrumentId)}," +
                    $"{stats.RequestCount}," +
                    $"{stats.GrantCount}," +
                    $"{stats.ReleaseCount}," +
                    $"{stats.TimeoutCount}," +
                    $"{stats.IsCurrentlyInUse}," +
                    $"{CsvEscape(stats.CurrentHolderUserId)}," +
                    $"{GetInstrumentTotalUsageIncludingCurrent(stats, now).TotalSeconds.ToString(CultureInfo.InvariantCulture)}," +
                    $"{stats.LastUsageDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)}");
            }

            return sb.ToString();
        }

        public string SaveUserStatsCsvToFile(string filePath)
        {
            string csv = ExportUserStatsCsvString();
            File.WriteAllText(filePath, csv, Encoding.UTF8);
            return filePath;
        }

        public string SaveInstrumentStatsCsvToFile(string filePath)
        {
            string csv = ExportInstrumentStatsCsvString();
            File.WriteAllText(filePath, csv, Encoding.UTF8);
            return filePath;
        }

        private string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            string escaped = value.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[UsageTimingTracker] {message}", this);
        }
    }

    [Serializable]
    public class UserUsageStats
    {
        public string UserId;

        public int RequestCount;
        public int GrantCount;
        public int ReleaseCount;
        public int TimeoutCount;

        public bool IsWaiting;
        public DateTime? WaitStartUtc;
        public TimeSpan TotalWaitTime;
        public TimeSpan LastWaitDuration;

        public bool IsUsingInstrument;
        public DateTime? UsageStartUtc;
        public TimeSpan TotalUsageTime;
        public TimeSpan LastUsageDuration;

        public UserUsageStats(string userId)
        {
            UserId = userId;
        }
    }

    [Serializable]
    public class InstrumentUsageStats
    {
        public string InstrumentId;

        public int RequestCount;
        public int GrantCount;
        public int ReleaseCount;
        public int TimeoutCount;

        public bool IsCurrentlyInUse;
        public string CurrentHolderUserId;
        public DateTime? CurrentUsageStartUtc;
        public TimeSpan TotalUsageTime;
        public TimeSpan LastUsageDuration;

        public InstrumentUsageStats(string instrumentId)
        {
            InstrumentId = instrumentId;
        }
    }
}
