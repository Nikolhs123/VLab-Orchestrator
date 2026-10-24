using UnityEngine;

namespace Thesis.Orchestration.SharedSync
{
    // Records that the local participant has asked to release access and the release is not yet visible in the
    // replicated queue state. In that window the replicated holder is still the local participant, but a
    // server-authoritative backend may already have given native ownership to the next holder (for example a
    // host that is granted access takes ownership immediately). Without this record the access controller
    // sees "I am the holder but not the owner" and sends an ownership request that the server must reject;
    // it appeared in the revision campaign as a canceled authority measurement at every handoff from a
    // Netcode client to the host. The marker is cleared when local access is revoked and expires after a short
    // window, so a rejected release does not suppress ownership requests for long.
    public static class LocalReleaseIntent
    {
        public const float DefaultWindowSeconds = 1.5f;

        private static double requestedAt = -1d;

        public static void Mark()
        {
            requestedAt = Time.realtimeSinceStartupAsDouble;
        }

        public static void Clear()
        {
            requestedAt = -1d;
        }

        public static bool IsPending(float windowSeconds = DefaultWindowSeconds)
        {
            return requestedAt >= 0d && Time.realtimeSinceStartupAsDouble - requestedAt < windowSeconds;
        }
    }
}
