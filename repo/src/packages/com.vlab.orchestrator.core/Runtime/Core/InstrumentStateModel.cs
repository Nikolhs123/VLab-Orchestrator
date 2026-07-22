using System;
using System.Collections.Generic;

namespace Thesis.Orchestration.Core.Models
{
    /// <summary>
    /// Represents the runtime state of one shared instrument
    /// (e.g., one microscope) independently from Unity, Photon, or MAGES.
    /// </summary>
    [Serializable]
    public class InstrumentStateModel
    {
        /// <summary>
        /// Unique identifier of the instrument.
        /// Example: "Microscope_A"
        /// </summary>
        public string InstrumentId { get; private set; }

        /// <summary>
        /// User ID of the current holder.
        /// Null means no one currently holds the instrument.
        /// </summary>
        public string CurrentHolderUserId { get; private set; }

        /// <summary>
        /// Queue of waiting users for this instrument.
        /// Stored as user IDs in request order.
        /// </summary>
        public Queue<string> WaitingQueue { get; private set; }

        /// <summary>
        /// Whether the instrument is currently locked for use.
        /// This is true whenever there is a current holder.
        /// </summary>
        public bool IsLocked => !string.IsNullOrEmpty(CurrentHolderUserId);

        /// <summary>
        /// UTC timestamp of the last meaningful interaction
        /// performed by the current holder.
        /// Used for timeout logic.
        /// </summary>
        public DateTime? LastInteractionUtc { get; private set; }

        /// <summary>
        /// UTC timestamp of when the current holder acquired access.
        /// Useful for usage duration and time-slice policies later.
        /// </summary>
        public DateTime? LockAcquiredUtc { get; private set; }

        public InstrumentStateModel(string instrumentId)
        {
            if (string.IsNullOrWhiteSpace(instrumentId))
                throw new ArgumentException("Instrument ID cannot be null or empty.", nameof(instrumentId));

            InstrumentId = instrumentId;
            WaitingQueue = new Queue<string>();
            CurrentHolderUserId = null;
            LastInteractionUtc = null;
            LockAcquiredUtc = null;
        }

        /// <summary>
        /// Assigns the instrument to a specific user.
        /// This method does not validate queue logic;
        /// that will be handled by QueueManagerCore.
        /// </summary>
        public void AssignHolder(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));

            CurrentHolderUserId = userId;
            LockAcquiredUtc = DateTime.UtcNow;
            LastInteractionUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Releases the current holder and unlocks the instrument.
        /// </summary>
        public void ReleaseHolder()
        {
            CurrentHolderUserId = null;
            LockAcquiredUtc = null;
            LastInteractionUtc = null;
        }

        /// <summary>
        /// Updates the interaction timestamp for timeout tracking.
        /// Only meaningful when the instrument is currently locked.
        /// </summary>
        public void RegisterInteraction()
        {
            if (IsLocked)
            {
                LastInteractionUtc = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Adds a user to the waiting queue if they are not already queued
        /// and are not already the current holder.
        /// Returns true if added, false otherwise.
        /// </summary>
        public bool EnqueueUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            if (CurrentHolderUserId == userId)
                return false;

            if (WaitingQueue.Contains(userId))
                return false;

            WaitingQueue.Enqueue(userId);
            return true;
        }

        /// <summary>
        /// Removes and returns the next queued user.
        /// Returns null if the queue is empty.
        /// </summary>
        public string DequeueNextUser()
        {
            if (WaitingQueue.Count == 0)
                return null;

            return WaitingQueue.Dequeue();
        }

        /// <summary>
        /// Removes a specific user from the waiting queue if present.
        /// Returns true if removed, false otherwise.
        /// </summary>
        public bool RemoveFromQueue(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId) || WaitingQueue.Count == 0)
                return false;

            bool removed = false;
            var rebuiltQueue = new Queue<string>();

            while (WaitingQueue.Count > 0)
            {
                var queuedUser = WaitingQueue.Dequeue();
                if (queuedUser == userId)
                {
                    removed = true;
                    continue;
                }

                rebuiltQueue.Enqueue(queuedUser);
            }

            WaitingQueue = rebuiltQueue;
            return removed;
        }

        /// <summary>
        /// Returns the 1-based queue position of a user.
        /// Returns -1 if the user is not in queue.
        /// </summary>
        public int GetQueuePosition(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return -1;

            int position = 1;
            foreach (var queuedUser in WaitingQueue)
            {
                if (queuedUser == userId)
                    return position;

                position++;
            }

            return -1;
        }

        /// <summary>
        /// Returns how long the current holder has been idle,
        /// based on the last interaction timestamp.
        /// Returns null if there is no current holder.
        /// </summary>
        public TimeSpan? GetIdleDuration()
        {
            if (!IsLocked || !LastInteractionUtc.HasValue)
                return null;

            return DateTime.UtcNow - LastInteractionUtc.Value;
        }

        /// <summary>
        /// Returns how long the current holder has held the instrument.
        /// Returns null if there is no current holder.
        /// </summary>
        public TimeSpan? GetLockDuration()
        {
            if (!IsLocked || !LockAcquiredUtc.HasValue)
                return null;

            return DateTime.UtcNow - LockAcquiredUtc.Value;
        }
    }
}