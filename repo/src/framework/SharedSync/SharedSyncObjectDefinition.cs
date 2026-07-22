using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using FishNetNetworkObject = FishNet.Object.NetworkObject;
using FishNetNetworkTransform = FishNet.Component.Transforming.NetworkTransform;

#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
using Thesis.Orchestration.Backends.PUN2;
#endif

using Fusion;
using Thesis.Orchestration.Backends.FishNet;
using Thesis.Orchestration.Backends.FishNet.StandaloneShared;
using Thesis.Orchestration.Backends.Mirror;
using Thesis.Orchestration.Backends.Mirror.StandaloneShared;
using Thesis.Orchestration.Backends.Fusion;
using Thesis.Orchestration.Backends.Netcode;
using Thesis.Orchestration.EditorTools;
using Unity.Netcode;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;
using MirrorNetworkTransformUnreliable = global::Mirror.NetworkTransformUnreliable;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Thesis.Orchestration.SharedSync
{
    public class SharedSyncObjectDefinition : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string objectId = "shared_object_01";
        [SerializeField] private SharedObjectCategory category = SharedObjectCategory.Instrument;
        [SerializeField] private string displayName = "Shared Instrument";

        [Header("Shared Behavior")]
        [SerializeField] private bool isSharedObject = true;
        [SerializeField] private bool requiresQueueControl = true;
        [SerializeField] private bool requiresAuthorityTransfer = true;

        [Header("Synchronization Flags")]
        [SerializeField] private bool syncTransform = true;
        [SerializeField] private bool syncState = true;
        [SerializeField] private bool syncEvents = false;

        [Header("Child Object Management")]
        [Tooltip("If enabled, eligible child objects are automatically discovered and configured for the currently selected backend. If disabled, backend child sync scripts are removed.")]
        [SerializeField] private bool autoManageBackendChildSyncInEditor = false;

        [Tooltip("Only children containing a component named 'MouseUI' are eligible.")]
        [SerializeField] private bool onlyConfigureChildrenWithMouseUI = true;

        [Tooltip("Skip children that already have their own SharedSyncObjectDefinition or UserSyncObjectDefinition.")]
        [SerializeField] private bool skipChildrenWithOwnSyncDefinitions = true;

        [Header("Scene References")]
        [SerializeField] private GameObject rootObject;
        [SerializeField] private List<GameObject> controlledChildObjects = new List<GameObject>();

        [Header("Optional Events")]
        [SerializeField] private UnityEvent onAccessGranted;
        [SerializeField] private UnityEvent onAccessRevoked;
        [SerializeField] private UnityEvent onStateChanged;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        public string ObjectId => objectId;
        public string DisplayName => displayName;
        public SharedObjectCategory Category => category;
        public bool IsSharedObject => isSharedObject;
        public bool RequiresQueueControl => requiresQueueControl;
        public bool RequiresAuthorityTransfer => requiresAuthorityTransfer;
        public bool SyncTransform => syncTransform;
        public bool SyncState => syncState;
        public bool SyncEvents => syncEvents;
        public GameObject RootObject => rootObject != null ? rootObject : gameObject;
        public IReadOnlyList<GameObject> ControlledChildObjects => controlledChildObjects;
        public bool HasLocalAccess { get; private set; }
        public string CurrentHolderUserId { get; private set; }

#if UNITY_EDITOR
        private bool previousAutoManageBackendChildSyncInEditor;
        private bool applyScheduled;
#endif

        private void Reset()
        {
            if (rootObject == null)
                rootObject = gameObject;
        }

        private void Awake()
        {
            if (rootObject == null)
                rootObject = gameObject;

            if (string.IsNullOrWhiteSpace(objectId))
                objectId = gameObject.name;

            if (string.IsNullOrWhiteSpace(displayName))
                displayName = gameObject.name;
        }

        private bool IsEligibleChild(GameObject childObject)
        {
            if (childObject == null)
                return false;

            if (skipChildrenWithOwnSyncDefinitions)
            {
                if (childObject.GetComponent<SharedSyncObjectDefinition>() != null)
                    return false;

                if (childObject.GetComponent("UserSyncObjectDefinition") != null)
                    return false;
            }

            if (onlyConfigureChildrenWithMouseUI &&
                childObject.GetComponent("MouseUI") == null &&
                childObject.GetComponent<SoftwareXControllablePart>() == null)
                return false;

            return true;
        }


        private List<GameObject> GetEligibleChildren()
        {
            List<GameObject> result = new List<GameObject>();

            if (RootObject == null)
                return result;

            Transform[] allChildren = RootObject.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in allChildren)
            {
                if (child == null || child == RootObject.transform)
                    continue;

                GameObject childObject = child.gameObject;

                if (!IsEligibleChild(childObject))
                    continue;

                result.Add(childObject);
            }

            return result;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (rootObject == null)
                rootObject = gameObject;

            if (previousAutoManageBackendChildSyncInEditor != autoManageBackendChildSyncInEditor)
            {
                previousAutoManageBackendChildSyncInEditor = autoManageBackendChildSyncInEditor;
                ScheduleBackendChildSyncRefresh();
            }
        }

        private void ScheduleBackendChildSyncRefresh()
        {
            if (applyScheduled)
                return;

            applyScheduled = true;

            EditorApplication.delayCall += () =>
            {
                applyScheduled = false;

                if (this == null)
                    return;

                if (autoManageBackendChildSyncInEditor)
                    ApplyBackendChildSyncConfigurationInEditor();
                else
                    RemoveAllBackendChildSyncConfigurationInEditor();
            };
        }
#endif

        [ContextMenu("Refresh Controlled Children")]
        public void RefreshControlledChildren()
        {
            controlledChildObjects.Clear();

            if (RootObject == null)
                return;

            foreach (GameObject child in GetEligibleChildren())
            {
                if (!controlledChildObjects.Contains(child))
                    controlledChildObjects.Add(child);
            }

            Log($"Refreshed controlled children. Count = {controlledChildObjects.Count}");
        }

        private OrchestrationBackendType ResolveConfiguredBackendType()
        {
            OrchestrationProjectConfig config = Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");
            if (config != null)
                return config.backendType;

            Log("OrchestrationProjectConfig not found in Resources. Falling back to Fusion.");
            return OrchestrationBackendType.Fusion;
        }

        public void ApplyBackendChildSyncConfigurationInRuntime(OrchestrationBackendType? backendOverride = null)
        {
            RefreshControlledChildren();

            OrchestrationBackendType backend = backendOverride ?? ResolveConfiguredBackendType();

            foreach (GameObject child in controlledChildObjects)
            {
                if (child == null)
                    continue;

                RemoveBackendComponentsFromChildExceptInRuntime(child, backend);

                switch (backend)
                {
                    case OrchestrationBackendType.PUN2:
#if PHOTON_UNITY_NETWORKING
                        EnsurePun2ComponentsOnChildInRuntime(child);
#endif
                        break;

                    case OrchestrationBackendType.Fusion:
                        EnsureFusionComponentsOnChildInRuntime(child);
                        break;

                    case OrchestrationBackendType.UnityNetcode:
                        EnsureNetcodeComponentsOnChildInRuntime(child);
                        break;

                    case OrchestrationBackendType.FishNet:
                        EnsureFishNetComponentsOnChildInRuntime(child);
                        break;

                    case OrchestrationBackendType.Mirror:
                        EnsureMirrorComponentsOnChildInRuntime(child);
                        break;
                }
            }

            Log($"Applied runtime backend child sync configuration for backend: {backend}");
        }

        public void RemoveAllBackendChildSyncConfigurationInRuntime()
        {
            foreach (GameObject child in GetEligibleChildren())
            {
                if (child == null)
                    continue;

                RemoveAllBackendComponentsFromChildInRuntime(child);
            }

            controlledChildObjects.Clear();
            Log("Removed all runtime backend child sync configuration.");
        }

        private void RemoveAllBackendComponentsFromChildInRuntime(GameObject child)
        {
#if PHOTON_UNITY_NETWORKING
            RemovePun2ComponentsFromChildInRuntime(child);
#endif
            RemoveFusionComponentsFromChildInRuntime(child);
            RemoveNetcodeComponentsFromChildInRuntime(child);
            RemoveFishNetComponentsFromChildInRuntime(child);
            RemoveMirrorComponentsFromChildInRuntime(child);
        }

        private void RemoveBackendComponentsFromChildExceptInRuntime(GameObject child, OrchestrationBackendType backendToKeep)
        {
            switch (backendToKeep)
            {
                case OrchestrationBackendType.PUN2:
                    RemoveFusionComponentsFromChildInRuntime(child);
                    RemoveNetcodeComponentsFromChildInRuntime(child);
                    RemoveFishNetComponentsFromChildInRuntime(child);
                    break;

                case OrchestrationBackendType.Fusion:
#if PHOTON_UNITY_NETWORKING
                    RemovePun2ComponentsFromChildInRuntime(child);
#endif
                    RemoveNetcodeComponentsFromChildInRuntime(child);
                    RemoveFishNetComponentsFromChildInRuntime(child);
                    break;

                case OrchestrationBackendType.UnityNetcode:
#if PHOTON_UNITY_NETWORKING
                    RemovePun2ComponentsFromChildInRuntime(child);
#endif
                    RemoveFusionComponentsFromChildInRuntime(child);
                    RemoveFishNetComponentsFromChildInRuntime(child);
                    break;

                case OrchestrationBackendType.FishNet:
 #if PHOTON_UNITY_NETWORKING
                    RemovePun2ComponentsFromChildInRuntime(child);
 #endif
                    RemoveFusionComponentsFromChildInRuntime(child);
                    RemoveNetcodeComponentsFromChildInRuntime(child);
                    break;

                case OrchestrationBackendType.Mirror:
#if PHOTON_UNITY_NETWORKING
                    RemovePun2ComponentsFromChildInRuntime(child);
#endif
                    RemoveFusionComponentsFromChildInRuntime(child);
                    RemoveNetcodeComponentsFromChildInRuntime(child);
                    RemoveFishNetComponentsFromChildInRuntime(child);
                    break;
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Apply Backend Child Sync Configuration (Editor Only)")]
        public void ApplyBackendChildSyncConfigurationInEditor()
        {
            RefreshControlledChildren();

            OrchestrationBackendType backend = ResolveConfiguredBackendType();

            foreach (GameObject child in controlledChildObjects)
            {
                if (child == null)
                    continue;

                RemoveBackendComponentsFromChildExcept(child, backend);

                switch (backend)
                {
                    case OrchestrationBackendType.PUN2:
#if PHOTON_UNITY_NETWORKING
                        EnsurePun2ComponentsOnChild(child);
#endif
                        break;

                    case OrchestrationBackendType.Fusion:
                        EnsureFusionComponentsOnChild(child);
                        break;

                    case OrchestrationBackendType.UnityNetcode:
                        EnsureNetcodeComponentsOnChild(child);
                        break;

                    case OrchestrationBackendType.FishNet:
                        EnsureFishNetComponentsOnChild(child);
                        break;

                    case OrchestrationBackendType.Mirror:
                        EnsureMirrorComponentsOnChild(child);
                        break;
                }

                EditorUtility.SetDirty(child);
            }

            EditorUtility.SetDirty(this);
            Log($"Applied backend child sync configuration for backend: {backend}");
        }

        [ContextMenu("Rebuild Backend Child Sync Configuration (Editor Only)")]
        public void RebuildBackendChildSyncConfigurationInEditor()
        {
            RefreshControlledChildren();

            OrchestrationBackendType backend = ResolveConfiguredBackendType();

            foreach (GameObject child in controlledChildObjects)
            {
                if (child == null)
                    continue;

                RemoveAllBackendComponentsFromChild(child);

                switch (backend)
                {
                    case OrchestrationBackendType.PUN2:
#if PHOTON_UNITY_NETWORKING
                        EnsurePun2ComponentsOnChild(child);
#endif
                        break;

                    case OrchestrationBackendType.Fusion:
                        EnsureFusionComponentsOnChild(child);
                        break;

                    case OrchestrationBackendType.UnityNetcode:
                        EnsureNetcodeComponentsOnChild(child);
                        break;

                    case OrchestrationBackendType.FishNet:
                        EnsureFishNetComponentsOnChild(child);
                        break;

                    case OrchestrationBackendType.Mirror:
                        EnsureMirrorComponentsOnChild(child);
                        break;
                }

                EditorUtility.SetDirty(child);
            }

            EditorUtility.SetDirty(this);
            Log($"Rebuilt backend child sync configuration for backend: {backend}");
        }

        [ContextMenu("Remove All Backend Child Sync Configuration (Editor Only)")]
        public void RemoveAllBackendChildSyncConfigurationInEditor()
        {
            foreach (GameObject child in GetEligibleChildren())
            {
                if (child == null)
                    continue;

                RemoveAllBackendComponentsFromChild(child);
                EditorUtility.SetDirty(child);
            }

            controlledChildObjects.Clear();
            EditorUtility.SetDirty(this);
            Log("Removed all backend child sync configuration.");
        }


        private OrchestrationBackendType GetConfiguredBackendType()
        {
            return ResolveConfiguredBackendType();
        }

        private void RemoveAllBackendComponentsFromChild(GameObject child)
        {
#if PHOTON_UNITY_NETWORKING
            RemovePun2ComponentsFromChild(child);
#endif
            RemoveFusionComponentsFromChild(child);
            RemoveNetcodeComponentsFromChild(child);
            RemoveFishNetComponentsFromChild(child);
            RemoveMirrorComponentsFromChild(child);
        }

        private void RemoveBackendComponentsFromChildExcept(GameObject child, OrchestrationBackendType backendToKeep)
        {
            switch (backendToKeep)
            {
                case OrchestrationBackendType.PUN2:
                    RemoveFusionComponentsFromChild(child);
                    RemoveNetcodeComponentsFromChild(child);
                    RemoveFishNetComponentsFromChild(child);
                    break;

                case OrchestrationBackendType.Fusion:
#if PHOTON_UNITY_NETWORKING
                    RemovePun2ComponentsFromChild(child);
#endif
                    RemoveNetcodeComponentsFromChild(child);
                    RemoveFishNetComponentsFromChild(child);
                    break;

                case OrchestrationBackendType.UnityNetcode:
#if PHOTON_UNITY_NETWORKING
                    RemovePun2ComponentsFromChild(child);
#endif
                    RemoveFusionComponentsFromChild(child);
                    RemoveFishNetComponentsFromChild(child);
                    break;

                case OrchestrationBackendType.FishNet:
 #if PHOTON_UNITY_NETWORKING
                    RemovePun2ComponentsFromChild(child);
 #endif
                    RemoveFusionComponentsFromChild(child);
                    RemoveNetcodeComponentsFromChild(child);
                    break;

                case OrchestrationBackendType.Mirror:
#if PHOTON_UNITY_NETWORKING
                    RemovePun2ComponentsFromChild(child);
#endif
                    RemoveFusionComponentsFromChild(child);
                    RemoveNetcodeComponentsFromChild(child);
                    RemoveFishNetComponentsFromChild(child);
                    break;
            }
        }

#if PHOTON_UNITY_NETWORKING
        private void EnsurePun2ComponentsOnChild(GameObject child)
        {
            PhotonView photonView = child.GetComponent<PhotonView>();
            if (photonView == null)
                photonView = Undo.AddComponent<PhotonView>(child);

            SharedTransformSyncPUN2 sync = child.GetComponent<SharedTransformSyncPUN2>();
            if (sync == null)
                sync = Undo.AddComponent<SharedTransformSyncPUN2>(child);

            NetworkInteractionGatePUN2 gate = child.GetComponent<NetworkInteractionGatePUN2>();
            if (gate == null)
                gate = Undo.AddComponent<NetworkInteractionGatePUN2>(child);

            photonView.OwnershipTransfer = OwnershipOption.Takeover;

            if (photonView.ObservedComponents == null)
                photonView.ObservedComponents = new List<Component>();

            if (!photonView.ObservedComponents.Contains(sync))
                photonView.ObservedComponents.Add(sync);

            EditorUtility.SetDirty(photonView);
            EditorUtility.SetDirty(sync);
            EditorUtility.SetDirty(gate);
        }

        private void RemovePun2ComponentsFromChild(GameObject child)
        {
            PhotonView photonView = child.GetComponent<PhotonView>();
            SharedTransformSyncPUN2 sync = child.GetComponent<SharedTransformSyncPUN2>();
            NetworkInteractionGatePUN2 gate = child.GetComponent<NetworkInteractionGatePUN2>();

            if (photonView != null && photonView.ObservedComponents != null && sync != null)
                photonView.ObservedComponents.Remove(sync);

            if (gate != null)
                Undo.DestroyObjectImmediate(gate);

            if (sync != null)
                Undo.DestroyObjectImmediate(sync);

            if (photonView != null)
                Undo.DestroyObjectImmediate(photonView);
        }
#endif

        private void EnsureFusionComponentsOnChild(GameObject child)
        {
            Fusion.NetworkObject childNetworkObject = child.GetComponent<Fusion.NetworkObject>();
            if (childNetworkObject == null)
                childNetworkObject = Undo.AddComponent<Fusion.NetworkObject>(child);

            ConfigureFusionChildNetworkObject(childNetworkObject);

            SharedTransformSyncFusion sync = child.GetComponent<SharedTransformSyncFusion>();
            if (sync != null)
                Undo.DestroyObjectImmediate(sync);

            NetworkInteractionGateFusion gate = child.GetComponent<NetworkInteractionGateFusion>();
            if (gate == null)
                gate = Undo.AddComponent<NetworkInteractionGateFusion>(child);

            EditorUtility.SetDirty(childNetworkObject);
            EditorUtility.SetDirty(gate);
        }

        private void RemoveFusionComponentsFromChild(GameObject child)
        {
            NetworkInteractionGateFusion gate = child.GetComponent<NetworkInteractionGateFusion>();
            SharedTransformSyncFusion sync = child.GetComponent<SharedTransformSyncFusion>();

            if (gate != null)
                Undo.DestroyObjectImmediate(gate);

            if (sync != null)
                Undo.DestroyObjectImmediate(sync);

            // IMPORTANT:
            // Do NOT remove child Fusion.NetworkObject components automatically here.
            // Destroying them from editor auto-setup code can leave stale baked nested-object
            // metadata on the root Fusion NetworkObject and break later room initialization.
        }

        private static void ConfigureFusionChildNetworkObject(Fusion.NetworkObject childNetworkObject)
        {
            if (childNetworkObject == null)
                return;

            childNetworkObject.Flags &= ~NetworkObjectFlags.MasterClientObject;
            childNetworkObject.Flags |= NetworkObjectFlags.AllowStateAuthorityOverride;
            childNetworkObject.Flags &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
        }

        private void EnsureNetcodeComponentsOnChild(GameObject child)
        {
            SharedTransformSyncNetcode sync = child.GetComponent<SharedTransformSyncNetcode>();
            if (sync != null)
                Undo.DestroyObjectImmediate(sync);

            NetworkInteractionGateNetcode gate = child.GetComponent<NetworkInteractionGateNetcode>();
            if (gate == null)
                gate = Undo.AddComponent<NetworkInteractionGateNetcode>(child);

            EditorUtility.SetDirty(gate);
        }

        private void RemoveNetcodeComponentsFromChild(GameObject child)
        {
            NetworkInteractionGateNetcode gate = child.GetComponent<NetworkInteractionGateNetcode>();
            SharedTransformSyncNetcode sync = child.GetComponent<SharedTransformSyncNetcode>();

            if (gate != null)
                Undo.DestroyObjectImmediate(gate);

            if (sync != null)
                Undo.DestroyObjectImmediate(sync);

            // IMPORTANT:
            // Do NOT remove a child NetworkObject here anymore because NGO child setup
            // should not be adding one in the first place for microscope child controls.
        }

        private void EnsureFishNetComponentsOnChild(GameObject child)
        {
            FishNetNetworkObject childNetworkObject = child.GetComponent<FishNetNetworkObject>();
            FishNetNetworkTransform childNetworkTransform = child.GetComponent<FishNetNetworkTransform>();
            FishNetOwnershipRelay ownershipRelay = child.GetComponent<FishNetOwnershipRelay>();
            SharedTransformSyncFishNet sync = child.GetComponent<SharedTransformSyncFishNet>();

            if (sync != null)
                Undo.DestroyObjectImmediate(sync);

            if (ownershipRelay != null)
                Undo.DestroyObjectImmediate(ownershipRelay);

            if (childNetworkTransform != null)
                Undo.DestroyObjectImmediate(childNetworkTransform);

            if (childNetworkObject != null)
                Undo.DestroyObjectImmediate(childNetworkObject);

            NetworkInteractionGateFishNet gate = child.GetComponent<NetworkInteractionGateFishNet>();
            if (gate == null)
                gate = Undo.AddComponent<NetworkInteractionGateFishNet>(child);

            EditorUtility.SetDirty(gate);
        }

        private void RemoveFishNetComponentsFromChild(GameObject child)
        {
            NetworkInteractionGateFishNet gate = child.GetComponent<NetworkInteractionGateFishNet>();
            SharedTransformSyncFishNet sync = child.GetComponent<SharedTransformSyncFishNet>();
            FishNetOwnershipRelay ownershipRelay = child.GetComponent<FishNetOwnershipRelay>();
            FishNetNetworkTransform networkTransform = child.GetComponent<FishNetNetworkTransform>();
            FishNetNetworkObject networkObject = child.GetComponent<FishNetNetworkObject>();

            if (gate != null)
                Undo.DestroyObjectImmediate(gate);

            if (sync != null)
                Undo.DestroyObjectImmediate(sync);

            if (ownershipRelay != null)
                Undo.DestroyObjectImmediate(ownershipRelay);

            if (networkTransform != null)
                Undo.DestroyObjectImmediate(networkTransform);

            if (networkObject != null)
                Undo.DestroyObjectImmediate(networkObject);
        }

        private void EnsureMirrorComponentsOnChild(GameObject child)
        {
            MirrorNetworkIdentity childNetworkIdentity = child.GetComponent<MirrorNetworkIdentity>();
            MirrorNetworkTransformUnreliable childNetworkTransform = child.GetComponent<MirrorNetworkTransformUnreliable>();
            MirrorOwnershipRelay ownershipRelay = child.GetComponent<MirrorOwnershipRelay>();

            if (ownershipRelay != null)
                Undo.DestroyObjectImmediate(ownershipRelay);

            if (childNetworkTransform != null)
                Undo.DestroyObjectImmediate(childNetworkTransform);

            if (childNetworkIdentity != null)
                Undo.DestroyObjectImmediate(childNetworkIdentity);

            NetworkInteractionGateMirror gate = child.GetComponent<NetworkInteractionGateMirror>();
            if (gate == null)
                gate = Undo.AddComponent<NetworkInteractionGateMirror>(child);

            EditorUtility.SetDirty(gate);
        }

        private void RemoveMirrorComponentsFromChild(GameObject child)
        {
            NetworkInteractionGateMirror gate = child.GetComponent<NetworkInteractionGateMirror>();
            MirrorOwnershipRelay ownershipRelay = child.GetComponent<MirrorOwnershipRelay>();
            MirrorNetworkTransformUnreliable networkTransform = child.GetComponent<MirrorNetworkTransformUnreliable>();
            MirrorNetworkIdentity networkIdentity = child.GetComponent<MirrorNetworkIdentity>();

            if (gate != null)
                Undo.DestroyObjectImmediate(gate);

            if (ownershipRelay != null)
                Undo.DestroyObjectImmediate(ownershipRelay);

            if (networkTransform != null)
                Undo.DestroyObjectImmediate(networkTransform);

            if (networkIdentity != null)
                Undo.DestroyObjectImmediate(networkIdentity);
        }
#endif

#if PHOTON_UNITY_NETWORKING
        private void EnsurePun2ComponentsOnChildInRuntime(GameObject child)
        {
            PhotonView photonView = child.GetComponent<PhotonView>();
            if (photonView == null)
                return;

            SharedTransformSyncPUN2 sync = BackendComponentUtility.GetOrAddComponent<SharedTransformSyncPUN2>(child);
            BackendComponentUtility.GetOrAddComponent<NetworkInteractionGatePUN2>(child);

            photonView.OwnershipTransfer = OwnershipOption.Takeover;

            if (photonView.ObservedComponents == null)
                photonView.ObservedComponents = new List<Component>();

            if (!photonView.ObservedComponents.Contains(sync))
                photonView.ObservedComponents.Add(sync);
        }

        private void RemovePun2ComponentsFromChildInRuntime(GameObject child)
        {
            PhotonView photonView = child.GetComponent<PhotonView>();
            SharedTransformSyncPUN2 sync = child.GetComponent<SharedTransformSyncPUN2>();
            NetworkInteractionGatePUN2 gate = child.GetComponent<NetworkInteractionGatePUN2>();

            if (photonView != null && photonView.ObservedComponents != null && sync != null)
                photonView.ObservedComponents.Remove(sync);

            BackendComponentUtility.DestroyComponent(gate);
            BackendComponentUtility.DestroyComponent(sync);
            BackendComponentUtility.DestroyComponent(photonView);
        }
#endif

        private void EnsureFusionComponentsOnChildInRuntime(GameObject child)
        {
            BackendComponentUtility.DestroyComponent(child.GetComponent<SharedTransformSyncFusion>());

            BackendComponentUtility.GetOrAddComponent<NetworkInteractionGateFusion>(child);
        }

        private void RemoveFusionComponentsFromChildInRuntime(GameObject child)
        {
            BackendComponentUtility.DestroyComponent(child.GetComponent<NetworkInteractionGateFusion>());
            BackendComponentUtility.DestroyComponent(child.GetComponent<SharedTransformSyncFusion>());
        }

        private void EnsureNetcodeComponentsOnChildInRuntime(GameObject child)
        {
            BackendComponentUtility.DestroyComponent(child.GetComponent<SharedTransformSyncNetcode>());
            BackendComponentUtility.GetOrAddComponent<NetworkInteractionGateNetcode>(child);
        }

        private void RemoveNetcodeComponentsFromChildInRuntime(GameObject child)
        {
            BackendComponentUtility.DestroyComponent(child.GetComponent<NetworkInteractionGateNetcode>());
            BackendComponentUtility.DestroyComponent(child.GetComponent<SharedTransformSyncNetcode>());
        }

        private void EnsureFishNetComponentsOnChildInRuntime(GameObject child)
        {
            BackendComponentUtility.GetOrAddComponent<NetworkInteractionGateFishNet>(child);
        }

        private void RemoveFishNetComponentsFromChildInRuntime(GameObject child)
        {
            BackendComponentUtility.DestroyComponent(child.GetComponent<NetworkInteractionGateFishNet>());
            BackendComponentUtility.DestroyComponent(child.GetComponent<SharedTransformSyncFishNet>());
            BackendComponentUtility.DestroyComponent(child.GetComponent<FishNetOwnershipRelay>());
            BackendComponentUtility.DestroyComponent(child.GetComponent<FishNetNetworkTransform>());
            BackendComponentUtility.DestroyComponent(child.GetComponent<FishNetNetworkObject>());
        }

        private void EnsureMirrorComponentsOnChildInRuntime(GameObject child)
        {
            BackendComponentUtility.GetOrAddComponent<NetworkInteractionGateMirror>(child);
        }

        private void RemoveMirrorComponentsFromChildInRuntime(GameObject child)
        {
            BackendComponentUtility.DestroyComponent(child.GetComponent<NetworkInteractionGateMirror>());
            BackendComponentUtility.DestroyComponent(child.GetComponent<MirrorOwnershipRelay>());
            BackendComponentUtility.DestroyComponent(child.GetComponent<MirrorNetworkTransformUnreliable>());
            BackendComponentUtility.DestroyComponent(child.GetComponent<MirrorNetworkIdentity>());
        }

        public void SetLocalAccessGranted(string holderUserId)
        {
            HasLocalAccess = true;
            CurrentHolderUserId = holderUserId;
            onAccessGranted?.Invoke();
        }

        public void SetLocalAccessRevoked(string holderUserId = null)
        {
            HasLocalAccess = false;
            CurrentHolderUserId = holderUserId;
            onAccessRevoked?.Invoke();
        }

        public void NotifyStateChanged()
        {
            onStateChanged?.Invoke();
        }

        public bool ContainsObject(GameObject target)
        {
            if (target == null)
                return false;

            if (target == RootObject)
                return true;

            return controlledChildObjects.Contains(target);
        }

        public void RegisterChildObject(GameObject child)
        {
            if (child == null)
                return;

            if (!controlledChildObjects.Contains(child))
                controlledChildObjects.Add(child);
        }

        public void UnregisterChildObject(GameObject child)
        {
            if (child == null)
                return;

            controlledChildObjects.Remove(child);
        }

        public void InitializeRuntimeMicroscopeDefaults(string suggestedObjectId = null, string suggestedDisplayName = null)
        {
            rootObject ??= gameObject;
            isSharedObject = true;
            category = SharedObjectCategory.Instrument;
            requiresQueueControl = true;
            requiresAuthorityTransfer = true;
            syncTransform = true;
            syncState = true;

            if (string.IsNullOrWhiteSpace(objectId) || objectId == "shared_object_01")
                objectId = !string.IsNullOrWhiteSpace(suggestedObjectId) ? suggestedObjectId : BuildRuntimeDefaultObjectId(gameObject.name);

            if (string.IsNullOrWhiteSpace(displayName) || displayName == "Shared Instrument")
                displayName = !string.IsNullOrWhiteSpace(suggestedDisplayName) ? suggestedDisplayName : gameObject.name;
        }

        private static string BuildRuntimeDefaultObjectId(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
                return "shared_object_runtime";

            System.Text.StringBuilder builder = new System.Text.StringBuilder(sourceName.Length);
            foreach (char character in sourceName)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
                else if (builder.Length == 0 || builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }

            string sanitized = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(sanitized)
                ? "shared_object_runtime"
                : $"shared_{sanitized}";
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[SharedSyncObjectDefinition:{objectId}] {message}", this);
        }
    }

    public enum SharedObjectCategory
    {
        Instrument = 0,
        Control = 1,
        ExperimentObject = 2,
        EnvironmentObject = 3,
        Indicator = 4,
        Other = 5
    }
}
