using System;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Core
{
    // Binds queue and native-authority requests to the identity of the transport sender observed by
    // the authoritative peer. A participant ID claimed in a request payload is accepted only when it
    // equals that sender identity; mismatches are rejected and journaled, so one participant cannot
    // join, release, heartbeat or acquire authority on behalf of another.
    public static class QueueRequestIdentity
    {
        public static bool Accept(string claimedUserId, string transportSenderUserId, string operation, string instrumentId)
        {
            if (!string.IsNullOrWhiteSpace(transportSenderUserId) &&
                string.Equals(claimedUserId, transportSenderUserId, StringComparison.Ordinal))
                return true;

            string details = $"{operation} rejected: claimed={claimedUserId ?? "null"} transportSender={transportSenderUserId ?? "null"}.";
            TelemetryLoggerCore.Instance?.LogEvent("QueueIdentityRejected", transportSenderUserId ?? "Unknown", instrumentId, details);
            Debug.LogWarning($"[QueueRequestIdentity] {details}");
            return false;
        }

        // Native-authority requests for a queue-controlled object are honored only for the current holder.
        public static bool IsHolder(string transportSenderUserId, string currentHolderUserId, string operation, string objectId)
        {
            if (!string.IsNullOrWhiteSpace(transportSenderUserId) &&
                string.Equals(transportSenderUserId, currentHolderUserId, StringComparison.Ordinal))
                return true;

            string details = $"{operation} rejected: requester={transportSenderUserId ?? "null"} holder={currentHolderUserId ?? "None"}.";
            TelemetryLoggerCore.Instance?.LogEvent("NativeAuthorityRequestRejected", transportSenderUserId ?? "Unknown", objectId, details);
            Debug.LogWarning($"[QueueRequestIdentity] {details}");
            return false;
        }
    }
}
