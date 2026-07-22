using System.Collections.Generic;
using Thesis.Orchestration.SharedSync;
using UnityEngine;

namespace Thesis.Orchestration.SharedSync
{
    public abstract class BaseMicroscopeAccessController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] protected SharedSyncObjectDefinition sharedDefinition;

        [Header("Local Control")]
        [Tooltip("Objects/scripts that should only be enabled for the current holder.")]
        [SerializeField] protected Behaviour[] behavioursToEnableForHolderOnly;

        [Tooltip("Optional GameObjects that should only be active for the holder.")]
        [SerializeField] protected GameObject[] gameObjectsToEnableForHolderOnly;

        [Header("Debug")]
        [SerializeField] protected bool verboseLogs = true;

        public bool HasLocalAccess { get; protected set; }

        protected virtual void BaseAwake()
        {
            if (sharedDefinition == null)
                sharedDefinition = GetComponent<SharedSyncObjectDefinition>();
        }

        protected virtual void BaseStart()
        {
            RefreshAccessState();
        }

        protected virtual void BaseUpdate()
        {
            RefreshAccessState();
        }

        public virtual void RefreshAccessState()
        {
            if (!CanEvaluateAccessState())
            {
                SetLocalAccess(false);
                return;
            }

            if (!IsQueueRuntimeActive())
            {
                SetLocalAccess(false);
                return;
            }

            if (!IsCurrentInstrumentThisObject())
            {
                SetLocalAccess(false);
                return;
            }

            string localUserId = GetLocalUserId();
            bool shouldHaveAccess = GetCurrentHolderUserId() == localUserId;

            SetLocalAccess(shouldHaveAccess);

            if (shouldHaveAccess && RequiresAuthorityTransfer())
            {
                RequestAuthorityIfNeeded();
            }
        }

        protected virtual void SetLocalAccess(bool hasAccess)
        {
            if (HasLocalAccess == hasAccess)
                return;

            HasLocalAccess = hasAccess;

            if (sharedDefinition != null)
            {
                if (hasAccess)
                    sharedDefinition.SetLocalAccessGranted(GetLocalUserId());
                else
                    sharedDefinition.SetLocalAccessRevoked(GetCurrentHolderUserIdOrNull());
            }

            if (behavioursToEnableForHolderOnly != null)
            {
                foreach (var behaviour in behavioursToEnableForHolderOnly)
                {
                    if (behaviour != null)
                        behaviour.enabled = hasAccess;
                }
            }

            if (gameObjectsToEnableForHolderOnly != null)
            {
                foreach (var go in gameObjectsToEnableForHolderOnly)
                {
                    if (go != null)
                        go.SetActive(hasAccess);
                }
            }

            OnLocalAccessChanged(hasAccess);
            Log($"Local access changed -> {hasAccess}");
        }

        protected virtual string GetCurrentHolderUserIdOrNull()
        {
            string holder = GetCurrentHolderUserId();
            return string.IsNullOrWhiteSpace(holder) || holder == "None" ? null : holder;
        }

        protected virtual bool IsCurrentInstrumentThisObject()
        {
            if (sharedDefinition == null)
                return false;

            return GetCurrentInstrumentId() == sharedDefinition.ObjectId;
        }

        protected virtual bool RequiresAuthorityTransfer()
        {
            return sharedDefinition != null && sharedDefinition.RequiresAuthorityTransfer;
        }

        protected IEnumerable<GameObject> GetControlledChildObjects()
        {
            if (sharedDefinition == null || sharedDefinition.ControlledChildObjects == null)
                yield break;

            foreach (var child in sharedDefinition.ControlledChildObjects)
            {
                if (child != null)
                    yield return child;
            }
        }

        protected virtual void OnLocalAccessChanged(bool hasAccess) { }

        protected abstract bool CanEvaluateAccessState();
        protected abstract bool IsQueueRuntimeActive();
        protected abstract string GetCurrentInstrumentId();
        protected abstract string GetCurrentHolderUserId();
        protected abstract string GetLocalUserId();
        protected abstract void RequestAuthorityIfNeeded();

        protected void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[{GetType().Name}:{name}] {message}", this);
        }
    }
}
