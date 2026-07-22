using System;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.Analytics;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    public interface ISharedUserCardStatsProvider
    {
        bool HasSharedUserCardStats { get; }
        IReadOnlyList<UserCardStatsEntry> GetSharedUserCardStats();
    }

    [Serializable]
    public class UserCardStatsSnapshot
    {
        public List<UserCardStatsEntry> users = new();

        public static string Encode(UserCardStatsSnapshot snapshot)
        {
            if (snapshot == null)
                snapshot = new UserCardStatsSnapshot();

            return JsonUtility.ToJson(snapshot);
        }

        public static UserCardStatsSnapshot Decode(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new UserCardStatsSnapshot();

            try
            {
                UserCardStatsSnapshot snapshot = JsonUtility.FromJson<UserCardStatsSnapshot>(json);
                return snapshot ?? new UserCardStatsSnapshot();
            }
            catch
            {
                return new UserCardStatsSnapshot();
            }
        }
    }

    [Serializable]
    public class UserCardStatsEntry
    {
        public string userId;
        public string displayName;

        public int requestCount;
        public int grantCount;
        public int releaseCount;
        public int timeoutCount;

        public double totalWaitSeconds;
        public double totalUsageSeconds;
        public double currentWaitSeconds;
        public double currentUsageSeconds;

        // Optional ISO timestamps for clients to compute "current" durations locally without
        // requiring frequent snapshot republishing.
        public string waitStartUtcIso;
        public string usageStartUtcIso;

        public bool isHolder;
        public bool isWaiting;
        public bool isMaster;
    }

    public static class SharedUserCardStats
    {
        public static UserCardStatsSnapshot BuildSnapshot(
            IEnumerable<string> activeUserIds,
            Func<string, string> getDisplayName,
            Func<string, bool> isMasterUser,
            string currentHolderUserId,
            IEnumerable<string> currentQueueSnapshot,
            UsageTimingTracker tracker)
        {
            UserCardStatsSnapshot snapshot = new UserCardStatsSnapshot();
            HashSet<string> waitingUsers = currentQueueSnapshot != null
                ? new HashSet<string>(currentQueueSnapshot.Where(x => !string.IsNullOrWhiteSpace(x)))
                : new HashSet<string>();

            foreach (string userId in activeUserIds.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var stats = tracker != null ? tracker.GetUserStats(userId) : null;

                snapshot.users.Add(new UserCardStatsEntry
                {
                    userId = userId,
                    displayName = getDisplayName != null ? getDisplayName(userId) : userId,

                    requestCount = stats != null ? stats.RequestCount : 0,
                    grantCount = stats != null ? stats.GrantCount : 0,
                    releaseCount = stats != null ? stats.ReleaseCount : 0,
                    timeoutCount = stats != null ? stats.TimeoutCount : 0,

                    totalWaitSeconds = stats != null ? stats.TotalWaitTime.TotalSeconds : 0d,
                    totalUsageSeconds = stats != null ? stats.TotalUsageTime.TotalSeconds : 0d,
                    currentWaitSeconds = tracker != null ? tracker.GetCurrentWaitDuration(userId).TotalSeconds : 0d,
                    currentUsageSeconds = tracker != null ? tracker.GetCurrentUsageDuration(userId).TotalSeconds : 0d,

                    waitStartUtcIso = stats != null && stats.WaitStartUtc.HasValue
                        ? stats.WaitStartUtc.Value.ToString("o")
                        : string.Empty,
                    usageStartUtcIso = stats != null && stats.UsageStartUtc.HasValue
                        ? stats.UsageStartUtc.Value.ToString("o")
                        : string.Empty,

                    isHolder = userId == currentHolderUserId,
                    isWaiting = waitingUsers.Contains(userId),
                    isMaster = isMasterUser != null && isMasterUser(userId)
                });
            }

            return snapshot;
        }

        public static IReadOnlyList<UserCardStatsEntry> DecodeEntries(string json)
        {
            return UserCardStatsSnapshot.Decode(json).users;
        }
    }
}