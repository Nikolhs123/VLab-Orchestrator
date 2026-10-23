using UnityEngine;

namespace Thesis.Orchestration.SharedSync
{
    public abstract class BaseInteractionGate : MonoBehaviour
    {
        private const float MinimumConservativeAutoReleaseIdleSeconds = 8f;

        protected Vector3 lastPosition;
        protected Quaternion lastRotation;

        protected float lastHeartbeatTime = -999f;
        protected float lastMeaningfulInteractionTime = -999f;
        protected float lastAutoReleaseTime = -999f;
        protected float lastAnalyticsInteractionTime = -999f;

        protected bool lastKnownLocalAccessState = false;
        protected bool waitingForFirstInteractionAfterGrant = false;
        protected bool lastInteractionState = true;

        // The gate's current decision (logical access and local native authority), for access-control probes.
        public bool InteractionPermitted => lastInteractionState;

        protected virtual void BaseInitialize()
        {
            lastPosition = transform.position;
            lastRotation = transform.rotation;
            lastMeaningfulInteractionTime = Time.time;
        }

        protected virtual void BaseStart()
        {
            lastPosition = transform.position;
            lastRotation = transform.rotation;
            lastMeaningfulInteractionTime = Time.time;
        }

        protected virtual void BaseUpdate()
        {
            RefreshAccessLifecycleState();
            RefreshInteractionStateInternal();
            TrySendAutomaticHeartbeatInternal();
            TryAutoReleaseAfterIdleInternal();
        }

        protected void RefreshAccessLifecycleState()
        {
            bool hasLocalAccess = GetHasLocalAccess();

            if (hasLocalAccess && !lastKnownLocalAccessState)
            {
                lastMeaningfulInteractionTime = Time.time;
                lastHeartbeatTime = Time.time;
                lastAutoReleaseTime = Time.time;
                waitingForFirstInteractionAfterGrant = true;

                lastPosition = transform.position;
                lastRotation = transform.rotation;

                OnAccessGranted();
                Log("Access granted locally. Reset idle timers.");
            }

            if (!hasLocalAccess && lastKnownLocalAccessState)
            {
                waitingForFirstInteractionAfterGrant = false;
                OnAccessRevoked();
                Log("Access revoked locally.");
            }

            lastKnownLocalAccessState = hasLocalAccess;
        }

        protected void RefreshInteractionStateInternal(bool force = false)
        {
            bool shouldEnableInteraction = EvaluateInteractionPermission();

            if (!force && shouldEnableInteraction == lastInteractionState)
                return;

            lastInteractionState = shouldEnableInteraction;
            ApplyInteractionState(shouldEnableInteraction);

            Log($"Interaction enabled = {shouldEnableInteraction}");
        }

        protected void TrySendAutomaticHeartbeatInternal()
        {
            if (!GetSendHeartbeatOnTransformChange())
                return;

            if (!IsNetworkSessionUsable())
                return;

            if (!IsQueueRuntimeActive())
                return;

            if (!GetHasLocalAccess())
            {
                lastPosition = transform.position;
                lastRotation = transform.rotation;
                return;
            }

            if (!HasNetworkAuthorityForInteraction())
            {
                lastPosition = transform.position;
                lastRotation = transform.rotation;
                return;
            }

            bool positionChanged = Vector3.Distance(transform.position, lastPosition) > GetPositionChangeThreshold();
            bool rotationChanged = Quaternion.Angle(transform.rotation, lastRotation) > GetRotationChangeThresholdDegrees();

            if (!positionChanged && !rotationChanged)
                return;

            lastMeaningfulInteractionTime = Time.time;
            waitingForFirstInteractionAfterGrant = false;

            if (Time.time - lastHeartbeatTime >= GetHeartbeatCooldownSeconds())
            {
                SendHeartbeat();
                lastHeartbeatTime = Time.time;
                Log($"Automatic heartbeat sent due to transform change on '{name}'.");
            }

            TryRegisterInteractionAnalyticsInternal();

            lastPosition = transform.position;
            lastRotation = transform.rotation;
        }

        protected void TryRegisterInteractionAnalyticsInternal()
        {
            if (!GetTrackInteractionAnalytics())
                return;

            if (Time.time - lastAnalyticsInteractionTime < GetAnalyticsInteractionCooldownSeconds())
                return;

            RegisterInteractionAnalytics();
            lastAnalyticsInteractionTime = Time.time;
        }

        protected void TryAutoReleaseAfterIdleInternal()
        {
            if (!GetAutoReleaseWhenIdleAndQueueWaiting())
                return;

            if (!IsNetworkSessionUsable())
                return;

            if (!IsQueueRuntimeActive())
                return;

            if (!GetHasLocalAccess())
                return;

            if (!HasNetworkAuthorityForInteraction())
                return;

            if (!IsLocalHolder())
                return;

            if (!QueueHasWaitingUsers())
                return;

            if (Time.time - lastAutoReleaseTime < GetAutoReleaseCooldownSeconds())
                return;

            if (waitingForFirstInteractionAfterGrant)
                return;

            float idleDuration = Time.time - lastMeaningfulInteractionTime;
            float requiredIdleDuration = Mathf.Max(GetAutoReleaseAfterIdleSeconds(), MinimumConservativeAutoReleaseIdleSeconds);
            if (idleDuration < requiredIdleDuration)
                return;

            RequestRelease();
            lastAutoReleaseTime = Time.time;
            lastMeaningfulInteractionTime = Time.time;

            Log($"Automatic release triggered after {idleDuration:F2}s idle while queue was waiting (threshold {requiredIdleDuration:F2}s).");
        }

        protected virtual void OnAccessGranted() { }
        protected virtual void OnAccessRevoked() { }

        protected abstract bool EvaluateInteractionPermission();
        protected abstract void ApplyInteractionState(bool shouldEnableInteraction);
        protected abstract bool GetHasLocalAccess();
        protected abstract bool IsNetworkSessionUsable();
        protected abstract bool IsQueueRuntimeActive();
        protected abstract bool HasNetworkAuthorityForInteraction();
        protected abstract bool IsLocalHolder();
        protected abstract bool QueueHasWaitingUsers();
        protected abstract void SendHeartbeat();
        protected abstract void RequestRelease();
        protected abstract void RegisterInteractionAnalytics();

        protected abstract bool GetSendHeartbeatOnTransformChange();
        protected abstract float GetHeartbeatCooldownSeconds();
        protected abstract float GetPositionChangeThreshold();
        protected abstract float GetRotationChangeThresholdDegrees();
        protected abstract bool GetAutoReleaseWhenIdleAndQueueWaiting();
        protected abstract float GetAutoReleaseAfterIdleSeconds();
        protected abstract float GetAutoReleaseCooldownSeconds();
        protected abstract bool GetTrackInteractionAnalytics();
        protected abstract float GetAnalyticsInteractionCooldownSeconds();

        protected abstract void Log(string message);
    }
}
