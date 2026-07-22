using System;

namespace Thesis.Orchestration.Core.Models
{
    /// <summary>
    /// Represents baseline runtime state for one user in the orchestration layer.
    /// This model is framework-agnostic and is used for fairness tracking,
    /// policy evaluation, telemetry support, and future lightweight AI features.
    /// </summary>
    [Serializable]
    public class UserStateModel
    {
        /// <summary>
        /// Unique identifier of the user.
        /// This can later map to Photon ActorNumber, MAGES user ID, etc.
        /// </summary>
        public string UserId { get; private set; }

        /// <summary>
        /// Total number of times the user requested access to a shared instrument.
        /// </summary>
        public int TotalRequests { get; private set; }

        /// <summary>
        /// Total number of times the user was actually granted access.
        /// </summary>
        public int GrantedTurns { get; private set; }

        /// <summary>
        /// Total number of denied access attempts.
        /// </summary>
        public int DeniedRequests { get; private set; }

        /// <summary>
        /// Total number of times access was revoked due to timeout or inactivity.
        /// </summary>
        public int TimeoutCount { get; private set; }

        /// <summary>
        /// Total accumulated waiting time across all turns.
        /// </summary>
        public TimeSpan TotalWaitingTime { get; private set; }

        /// <summary>
        /// Total accumulated usage time across all granted turns.
        /// </summary>
        public TimeSpan TotalUsageTime { get; private set; }

        /// <summary>
        /// Whether the user is currently waiting in queue.
        /// </summary>
        public bool IsWaiting { get; private set; }

        /// <summary>
        /// Whether the user is currently using the instrument.
        /// </summary>
        public bool IsUsingInstrument { get; private set; }

        /// <summary>
        /// Timestamp when the current waiting period started.
        /// Null if the user is not waiting.
        /// </summary>
        public DateTime? WaitStartedUtc { get; private set; }

        /// <summary>
        /// Timestamp when the current usage period started.
        /// Null if the user is not currently using the instrument.
        /// </summary>
        public DateTime? UsageStartedUtc { get; private set; }

        public UserStateModel(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));

            UserId = userId;
            TotalRequests = 0;
            GrantedTurns = 0;
            DeniedRequests = 0;
            TimeoutCount = 0;
            TotalWaitingTime = TimeSpan.Zero;
            TotalUsageTime = TimeSpan.Zero;
            IsWaiting = false;
            IsUsingInstrument = false;
            WaitStartedUtc = null;
            UsageStartedUtc = null;
        }

        /// <summary>
        /// Registers a new access request.
        /// Starts waiting time if the user is not already waiting or using the instrument.
        /// </summary>
        public void RegisterRequest()
        {
            TotalRequests++;

            if (!IsWaiting && !IsUsingInstrument)
            {
                IsWaiting = true;
                WaitStartedUtc = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Registers that the user has been granted access.
        /// Finalizes the current waiting period and starts a usage period.
        /// </summary>
        public void RegisterGrant()
        {
            GrantedTurns++;

            if (IsWaiting && WaitStartedUtc.HasValue)
            {
                TotalWaitingTime += DateTime.UtcNow - WaitStartedUtc.Value;
            }

            IsWaiting = false;
            WaitStartedUtc = null;

            IsUsingInstrument = true;
            UsageStartedUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Registers a normal release of access.
        /// Finalizes the current usage period.
        /// </summary>
        public void RegisterRelease()
        {
            if (IsUsingInstrument && UsageStartedUtc.HasValue)
            {
                TotalUsageTime += DateTime.UtcNow - UsageStartedUtc.Value;
            }

            IsUsingInstrument = false;
            UsageStartedUtc = null;
        }

        /// <summary>
        /// Registers that the request was denied.
        /// Denials do not automatically stop waiting unless the outer logic decides so.
        /// </summary>
        public void RegisterDenied()
        {
            DeniedRequests++;
        }

        /// <summary>
        /// Registers that the user lost access due to timeout/inactivity.
        /// Finalizes the current usage period and increments timeout count.
        /// </summary>
        public void RegisterTimeout()
        {
            TimeoutCount++;

            if (IsUsingInstrument && UsageStartedUtc.HasValue)
            {
                TotalUsageTime += DateTime.UtcNow - UsageStartedUtc.Value;
            }

            IsUsingInstrument = false;
            UsageStartedUtc = null;
        }

        /// <summary>
        /// Cancels the current waiting state, for example if the user leaves the queue,
        /// disconnects, or is removed before being granted access.
        /// </summary>
        public void CancelWaiting()
        {
            IsWaiting = false;
            WaitStartedUtc = null;
        }

        /// <summary>
        /// Returns the duration of the current waiting period, if any.
        /// </summary>
        public TimeSpan? GetCurrentWaitingDuration()
        {
            if (!IsWaiting || !WaitStartedUtc.HasValue)
                return null;

            return DateTime.UtcNow - WaitStartedUtc.Value;
        }

        /// <summary>
        /// Returns the duration of the current usage period, if any.
        /// </summary>
        public TimeSpan? GetCurrentUsageDuration()
        {
            if (!IsUsingInstrument || !UsageStartedUtc.HasValue)
                return null;

            return DateTime.UtcNow - UsageStartedUtc.Value;
        }

        /// <summary>
        /// Returns the average waiting time per granted turn.
        /// Returns TimeSpan.Zero if the user has no granted turns yet.
        /// </summary>
        public TimeSpan GetAverageWaitingTime()
        {
            if (GrantedTurns <= 0)
                return TimeSpan.Zero;

            return TimeSpan.FromTicks(TotalWaitingTime.Ticks / GrantedTurns);
        }

        /// <summary>
        /// Returns the average usage time per granted turn.
        /// Returns TimeSpan.Zero if the user has no granted turns yet.
        /// </summary>
        public TimeSpan GetAverageUsageTime()
        {
            if (GrantedTurns <= 0)
                return TimeSpan.Zero;

            return TimeSpan.FromTicks(TotalUsageTime.Ticks / GrantedTurns);
        }
    }
}