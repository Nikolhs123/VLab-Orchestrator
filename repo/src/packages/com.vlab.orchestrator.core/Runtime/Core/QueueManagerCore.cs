using System;
using System.Collections.Generic;
using Thesis.Orchestration.Core.Models;
using Thesis.Orchestration.Core.Policies;

namespace Thesis.Orchestration.Core
{
    /// <summary>
    /// Core queue and access manager for shared instruments.
    /// Framework-agnostic: no Unity, Photon, or MAGES references.
    ///
    /// Responsibilities:
    /// - maintain registered users and instruments
    /// - receive request/release/heartbeat actions
    /// - update instrument/user state
    /// - delegate next-user selection to PolicyEngineCore
    /// </summary>
    public class QueueManagerCore
    {
        private readonly Dictionary<string, InstrumentStateModel> instruments = new();
        private readonly Dictionary<string, UserStateModel> users = new();

        private readonly PolicyEngineCore policyEngine;

        public QueueManagerCore(PolicyEngineCore policyEngine = null)
        {
            this.policyEngine = policyEngine ?? new PolicyEngineCore();
        }

        public bool RegisterInstrument(string instrumentId)
        {
            if (string.IsNullOrWhiteSpace(instrumentId))
                return false;

            if (instruments.ContainsKey(instrumentId))
                return false;

            instruments[instrumentId] = new InstrumentStateModel(instrumentId);
            return true;
        }

        public bool RegisterUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            if (users.ContainsKey(userId))
                return false;

            users[userId] = new UserStateModel(userId);
            return true;
        }

        public InstrumentStateModel GetInstrumentState(string instrumentId)
        {
            instruments.TryGetValue(instrumentId, out var instrument);
            return instrument;
        }

        public UserStateModel GetUserState(string userId)
        {
            users.TryGetValue(userId, out var user);
            return user;
        }

        public IReadOnlyDictionary<string, InstrumentStateModel> GetAllInstruments()
        {
            return instruments;
        }

        public IReadOnlyDictionary<string, UserStateModel> GetAllUsers()
        {
            return users;
        }

        public string GetCurrentPolicyName()
        {
            return policyEngine.CurrentPolicy?.PolicyName ?? "Unknown";
        }

        public void SetPolicy(IAllocationPolicy policy)
        {
            policyEngine.SetPolicy(policy);
        }

        public QueueDecision RequestAccess(string userId, string instrumentId)
        {
            if (!TryGetRegistered(userId, instrumentId, out var user, out var instrument))
                return QueueDecision.Invalid("Unknown user or instrument.");

            user.RegisterRequest();

            if (instrument.CurrentHolderUserId == userId)
            {
                user.RegisterDenied();
                return QueueDecision.Denied("User already holds the instrument.");
            }

            if (!instrument.IsLocked)
            {
                instrument.AssignHolder(userId);
                user.RegisterGrant();

                return QueueDecision.Granted(userId, instrumentId, 0);
            }

            bool added = instrument.EnqueueUser(userId);
            if (!added)
            {
                user.RegisterDenied();
                return QueueDecision.Denied("User is already in queue or already holds the instrument.");
            }

            int position = instrument.GetQueuePosition(userId);
            return QueueDecision.Queued(userId, instrumentId, position);
        }

        public QueueDecision ReleaseAccess(string userId, string instrumentId)
        {
            if (!TryGetRegistered(userId, instrumentId, out var user, out var instrument))
                return QueueDecision.Invalid("Unknown user or instrument.");

            if (instrument.CurrentHolderUserId != userId)
                return QueueDecision.Denied("User is not the current holder.");

            string releasedUserId = userId;

            instrument.ReleaseHolder();
            user.RegisterRelease();

            QueueDecision nextDecision = PromoteNextUser(instrumentId);

            // If a manual release immediately promotes a next queued user, return a composite decision
            // so analytics and timing can correctly record both a release and a grant.
            if (nextDecision != null && nextDecision.Type == QueueDecisionType.Granted && !string.IsNullOrWhiteSpace(nextDecision.UserId))
                return QueueDecision.ReleasedAndGrantedNext(releasedUserId, nextDecision.UserId, instrumentId);

            return nextDecision;
        }

        public QueueDecision RemoveQueuedUser(string userId, string instrumentId)
        {
            if (!TryGetRegistered(userId, instrumentId, out var user, out var instrument))
                return QueueDecision.Invalid("Unknown user or instrument.");

            bool removed = instrument.RemoveFromQueue(userId);
            if (!removed)
                return QueueDecision.Denied("User was not in queue.");

            user.CancelWaiting();
            return QueueDecision.RemovedFromQueue(userId, instrumentId);
        }

        public bool RegisterInteraction(string userId, string instrumentId)
        {
            if (!TryGetRegistered(userId, instrumentId, out _, out var instrument))
                return false;

            if (instrument.CurrentHolderUserId != userId)
                return false;

            instrument.RegisterInteraction();
            return true;
        }

        public QueueDecision CheckTimeout(string instrumentId, TimeSpan idleTimeout)
        {
            if (!instruments.TryGetValue(instrumentId, out var instrument))
                return QueueDecision.Invalid("Unknown instrument.");

            if (!instrument.IsLocked)
                return null;

            var idle = instrument.GetIdleDuration();
            if (!idle.HasValue || idle.Value < idleTimeout)
                return null;

            string timedOutUserId = instrument.CurrentHolderUserId;
            if (string.IsNullOrWhiteSpace(timedOutUserId))
                return null;

            if (users.TryGetValue(timedOutUserId, out var user))
            {
                user.RegisterTimeout();
            }

            instrument.ReleaseHolder();

            var nextDecision = PromoteNextUser(instrumentId);
            if (nextDecision != null && nextDecision.Type == QueueDecisionType.Granted)
            {
                return QueueDecision.TimeoutAndGrantedNext(timedOutUserId, nextDecision.UserId, instrumentId);
            }

            return QueueDecision.TimeoutReleased(timedOutUserId, instrumentId);
        }

        public int GetQueuePosition(string userId, string instrumentId)
        {
            if (!instruments.TryGetValue(instrumentId, out var instrument))
                return -1;

            return instrument.GetQueuePosition(userId);
        }

        private QueueDecision PromoteNextUser(string instrumentId)
        {
            if (!instruments.TryGetValue(instrumentId, out var instrument))
                return QueueDecision.Invalid("Unknown instrument.");

            string nextUserId = policyEngine.SelectNextUser(instrument, users);

            if (string.IsNullOrWhiteSpace(nextUserId))
                return QueueDecision.ReleasedWithoutNext(instrumentId);

            bool removed = instrument.RemoveFromQueue(nextUserId);
            if (!removed)
                return QueueDecision.Invalid("Selected user could not be removed from queue.");

            if (!users.TryGetValue(nextUserId, out var nextUser))
                return QueueDecision.Invalid("Selected user is not registered.");

            instrument.AssignHolder(nextUserId);
            nextUser.RegisterGrant();

            return QueueDecision.Granted(nextUserId, instrumentId, 0);
        }

        private bool TryGetRegistered(
            string userId,
            string instrumentId,
            out UserStateModel user,
            out InstrumentStateModel instrument)
        {
            users.TryGetValue(userId, out user);
            instruments.TryGetValue(instrumentId, out instrument);

            return user != null && instrument != null;
        }
    }

    public class QueueDecision
    {
        public QueueDecisionType Type { get; private set; }
        public string UserId { get; private set; }
        public string SecondaryUserId { get; private set; }
        public string InstrumentId { get; private set; }
        public int QueuePosition { get; private set; }
        public string Reason { get; private set; }

        private QueueDecision() { }

        public static QueueDecision Granted(string userId, string instrumentId, int queuePosition)
        {
            return new QueueDecision
            {
                Type = QueueDecisionType.Granted,
                UserId = userId,
                InstrumentId = instrumentId,
                QueuePosition = queuePosition
            };
        }

        public static QueueDecision Queued(string userId, string instrumentId, int queuePosition)
        {
            return new QueueDecision
            {
                Type = QueueDecisionType.Queued,
                UserId = userId,
                InstrumentId = instrumentId,
                QueuePosition = queuePosition
            };
        }

        public static QueueDecision Denied(string reason)
        {
            return new QueueDecision
            {
                Type = QueueDecisionType.Denied,
                Reason = reason
            };
        }

        public static QueueDecision RemovedFromQueue(string userId, string instrumentId)
        {
            return new QueueDecision
            {
                Type = QueueDecisionType.RemovedFromQueue,
                UserId = userId,
                InstrumentId = instrumentId
            };
        }

        public static QueueDecision ReleasedWithoutNext(string instrumentId)
        {
            return new QueueDecision
            {
                Type = QueueDecisionType.ReleasedWithoutNext,
                InstrumentId = instrumentId
            };
        }

        public static QueueDecision TimeoutReleased(string timedOutUserId, string instrumentId)
        {
            return new QueueDecision
            {
                Type = QueueDecisionType.TimeoutReleased,
                UserId = timedOutUserId,
                InstrumentId = instrumentId
            };
        }

        public static QueueDecision TimeoutAndGrantedNext(string timedOutUserId, string grantedUserId, string instrumentId)
        {
            return new QueueDecision
            {
                Type = QueueDecisionType.TimeoutAndGrantedNext,
                UserId = grantedUserId,
                SecondaryUserId = timedOutUserId,
                InstrumentId = instrumentId
            };
        }


        public static QueueDecision ReleasedAndGrantedNext(string releasedUserId, string grantedUserId, string instrumentId)
        {
            return new QueueDecision
            {
                Type = QueueDecisionType.ReleasedAndGrantedNext,
                UserId = grantedUserId,
                SecondaryUserId = releasedUserId,
                InstrumentId = instrumentId
            };
        }

        public static QueueDecision Invalid(string reason)
        {
            return new QueueDecision
            {
                Type = QueueDecisionType.Invalid,
                Reason = reason
            };
        }
    }

    public enum QueueDecisionType
    {
        Invalid = 0,
        Granted = 1,
        Queued = 2,
        Denied = 3,
        RemovedFromQueue = 4,
        ReleasedWithoutNext = 5,
        ReleasedAndGrantedNext = 6,
        TimeoutReleased = 7,
        TimeoutAndGrantedNext = 8
    }
}
