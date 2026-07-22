using Thesis.Orchestration.Backends.Fusion;
using Thesis.Orchestration.Backends.Fusion.StandaloneShared;
using Thesis.Orchestration.Backends.FishNet;
using Thesis.Orchestration.Backends.FishNet.StandaloneShared;
using Thesis.Orchestration.Backends.Mirror;
using Thesis.Orchestration.Backends.Mirror.StandaloneShared;
using Thesis.Orchestration.Backends.Netcode;
using Thesis.Orchestration.Backends.Netcode.StandaloneShared;
using Thesis.Orchestration.Backends.PUN2;
using Thesis.Orchestration.EditorTools;
using Unity.Netcode;
using UnityEngine;
using FishNetNetworkObject = FishNet.Object.NetworkObject;
using FishNetNetworkTransform = FishNet.Component.Transforming.NetworkTransform;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;
using MirrorNetworkTransformUnreliable = global::Mirror.NetworkTransformUnreliable;
using MirrorSyncDirection = global::Mirror.SyncDirection;

#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

using Fusion;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Thesis.Orchestration.SharedSync
{
    public enum AutomaticBackendBindingKind
    {
        Microscope = 0,
        StandaloneSharedObject = 1
    }

    [DisallowMultipleComponent]
    public class AutomaticBackendObjectBinding : MonoBehaviour
    {
        [SerializeField] private OrchestrationProjectConfig projectConfig;
        [SerializeField] private AutomaticBackendBindingKind bindingKind = AutomaticBackendBindingKind.Microscope;
        [SerializeField] private SharedSyncObjectDefinition sharedDefinition;
        [SerializeField] private bool applyOnAwake = true;
        [SerializeField] private bool verboseLogs = true;

        private void Awake()
        {
            if (!applyOnAwake)
                return;

            ApplySelectedBackend();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying)
                return;

            EditorApplication.delayCall += ApplySelectedBackendInEditor;
        }
#endif

        public void ConfigureForMicroscope(SharedSyncObjectDefinition definition)
        {
            bindingKind = AutomaticBackendBindingKind.Microscope;
            sharedDefinition = definition;
        }

        public void ConfigureForStandalone()
        {
            bindingKind = AutomaticBackendBindingKind.StandaloneSharedObject;
            sharedDefinition = null;
        }

        [ContextMenu("Apply Selected Backend")]
        public void ApplySelectedBackend()
        {
            if (projectConfig == null)
                projectConfig = Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");

            if (projectConfig == null)
            {
                Log("Missing OrchestrationProjectConfig. Cannot apply backend binding.");
                return;
            }

            switch (bindingKind)
            {
                case AutomaticBackendBindingKind.Microscope:
                    ApplyMicroscopeBackend(projectConfig.backendType);
                    break;

                case AutomaticBackendBindingKind.StandaloneSharedObject:
                    ApplyStandaloneBackend(projectConfig.backendType);
                    break;
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Apply Selected Backend (Editor Authoring)")]
        public void ApplySelectedBackendInEditor()
        {
            if (this == null || Application.isPlaying)
                return;

            if (projectConfig == null)
                projectConfig = Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");

            if (projectConfig == null)
                return;

            switch (bindingKind)
            {
                case AutomaticBackendBindingKind.Microscope:
                    ApplyMicroscopeBackendInEditor(projectConfig.backendType);
                    break;

                case AutomaticBackendBindingKind.StandaloneSharedObject:
                    ApplyStandaloneBackendInEditor(projectConfig.backendType);
                    break;
            }
        }
#endif

        private void ApplyMicroscopeBackend(OrchestrationBackendType backendType)
        {
            sharedDefinition ??= GetComponent<SharedSyncObjectDefinition>();
            if (sharedDefinition == null)
            {
                Log("Missing SharedSyncObjectDefinition for microscope binding.");
                return;
            }

            GameObject root = sharedDefinition.RootObject != null ? sharedDefinition.RootObject : gameObject;
            if (Application.isPlaying)
            {
                ValidateMicroscopeRuntimeComponents(root, backendType);
                sharedDefinition.RefreshControlledChildren();
                Log($"Validated microscope backend binding at runtime: {backendType}");
                return;
            }

            BackendComponentUtility.RemoveForeignBackendComponentsFromHierarchy(root, backendType);
            EnsureMicroscopeRootComponents(root, backendType);
            sharedDefinition.ApplyBackendChildSyncConfigurationInRuntime(backendType);

            Log($"Applied microscope backend binding: {backendType}");
        }

#if UNITY_EDITOR
        private void ApplyMicroscopeBackendInEditor(OrchestrationBackendType backendType)
        {
            sharedDefinition ??= GetComponent<SharedSyncObjectDefinition>();
            if (sharedDefinition == null)
                return;

            GameObject root = sharedDefinition.RootObject != null ? sharedDefinition.RootObject : gameObject;
            BackendComponentUtility.RemoveForeignBackendComponentsFromHierarchy(root, backendType);

            switch (backendType)
            {
                case OrchestrationBackendType.PUN2:
#if PHOTON_UNITY_NETWORKING
                    PhotonView photonView = root.GetComponent<PhotonView>();
                    if (photonView == null)
                        photonView = Undo.AddComponent<PhotonView>(root);

                    photonView.OwnershipTransfer = OwnershipOption.Takeover;

                    if (root.GetComponent<MicroscopeAccessControllerPUN2>() == null)
                        Undo.AddComponent<MicroscopeAccessControllerPUN2>(root);
#endif
                    break;

                case OrchestrationBackendType.Fusion:
                    Fusion.NetworkObject fusionRoot = root.GetComponent<Fusion.NetworkObject>();
                    if (fusionRoot == null)
                        fusionRoot = Undo.AddComponent<Fusion.NetworkObject>(root);

                    ConfigureFusionRootNetworkObject(fusionRoot);

                    if (root.GetComponent<MicroscopeChildTransformSyncFusion>() == null)
                        Undo.AddComponent<MicroscopeChildTransformSyncFusion>(root);

                    if (root.GetComponent<MicroscopeAccessControllerFusion>() == null)
                        Undo.AddComponent<MicroscopeAccessControllerFusion>(root);
                    break;

                case OrchestrationBackendType.UnityNetcode:
                    if (root.GetComponent<Unity.Netcode.NetworkObject>() == null)
                        Undo.AddComponent<Unity.Netcode.NetworkObject>(root);

                    if (root.GetComponent<NetcodeOwnershipRelay>() == null)
                        Undo.AddComponent<NetcodeOwnershipRelay>(root);

                    if (root.GetComponent<MicroscopeAccessControllerNetcode>() == null)
                        Undo.AddComponent<MicroscopeAccessControllerNetcode>(root);

                    if (root.GetComponent<MicroscopeChildTransformSyncNetcode>() == null)
                        Undo.AddComponent<MicroscopeChildTransformSyncNetcode>(root);
                    break;

                case OrchestrationBackendType.FishNet:
                    if (root.GetComponent<FishNetNetworkObject>() == null)
                        Undo.AddComponent<FishNetNetworkObject>(root);

                    if (root.GetComponent<FishNetNetworkTransform>() == null)
                        Undo.AddComponent<FishNetNetworkTransform>(root);

                    if (root.GetComponent<FishNetOwnershipRelay>() == null)
                        Undo.AddComponent<FishNetOwnershipRelay>(root);

                    if (root.GetComponent<MicroscopeAccessControllerFishNet>() == null)
                        Undo.AddComponent<MicroscopeAccessControllerFishNet>(root);

                    if (root.GetComponent<MicroscopeChildTransformSyncFishNet>() == null)
                        Undo.AddComponent<MicroscopeChildTransformSyncFishNet>(root);
                    break;

                case OrchestrationBackendType.Mirror:
                    if (root.GetComponent<MirrorNetworkIdentity>() == null)
                        Undo.AddComponent<MirrorNetworkIdentity>(root);

                    if (root.GetComponent<MirrorOwnershipRelay>() == null)
                        Undo.AddComponent<MirrorOwnershipRelay>(root);

                    if (root.GetComponent<MicroscopeAccessControllerMirror>() == null)
                        Undo.AddComponent<MicroscopeAccessControllerMirror>(root);

                    if (root.GetComponent<MicroscopeChildTransformSyncMirror>() == null)
                        Undo.AddComponent<MicroscopeChildTransformSyncMirror>(root);
                    break;
            }

            sharedDefinition.ApplyBackendChildSyncConfigurationInEditor();
            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(sharedDefinition);
        }

        private static void ConfigureFusionRootNetworkObject(Fusion.NetworkObject networkObject)
        {
            if (networkObject == null)
                return;

            // The microscope root hosts the child-transform sync behaviour, so the
            // current holder must be able to take state authority over the root.
            networkObject.Flags &= ~NetworkObjectFlags.MasterClientObject;
            networkObject.Flags |= NetworkObjectFlags.AllowStateAuthorityOverride;
            networkObject.Flags &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
        }
#endif

        private void ApplyStandaloneBackend(OrchestrationBackendType backendType)
        {
            if (Application.isPlaying)
            {
                ValidateStandaloneRuntimeComponents(backendType);
                Log($"Validated standalone backend binding at runtime: {backendType}");
                return;
            }

            BackendComponentUtility.RemoveForeignBackendComponentsFromGameObject(gameObject, backendType);
            EnsureStandaloneRootComponents(backendType);
            Log($"Applied standalone backend binding: {backendType}");
        }

#if UNITY_EDITOR
        private void ApplyStandaloneBackendInEditor(OrchestrationBackendType backendType)
        {
            BackendComponentUtility.RemoveForeignBackendComponentsFromGameObject(gameObject, backendType);

            switch (backendType)
            {
                case OrchestrationBackendType.PUN2:
#if PHOTON_UNITY_NETWORKING
                    PhotonView photonView = gameObject.GetComponent<PhotonView>();
                    if (photonView == null)
                        photonView = Undo.AddComponent<PhotonView>(gameObject);

                    photonView.OwnershipTransfer = OwnershipOption.Takeover;

                    StandaloneSharedObjectPUN2 punStandalone = gameObject.GetComponent<StandaloneSharedObjectPUN2>();
                    if (punStandalone == null)
                        punStandalone = Undo.AddComponent<StandaloneSharedObjectPUN2>(gameObject);

                    if (photonView.ObservedComponents == null)
                        photonView.ObservedComponents = new System.Collections.Generic.List<Component>();

                    if (!photonView.ObservedComponents.Contains(punStandalone))
                        photonView.ObservedComponents.Add(punStandalone);
#endif
                    break;

                case OrchestrationBackendType.Fusion:
                    if (gameObject.GetComponent<Fusion.NetworkObject>() == null)
                        Undo.AddComponent<Fusion.NetworkObject>(gameObject);

                    if (gameObject.GetComponent<StandaloneSharedObjectFusion>() == null)
                        Undo.AddComponent<StandaloneSharedObjectFusion>(gameObject);
                    break;

                case OrchestrationBackendType.UnityNetcode:
                    if (gameObject.GetComponent<Unity.Netcode.NetworkObject>() == null)
                        Undo.AddComponent<Unity.Netcode.NetworkObject>(gameObject);

                    if (gameObject.GetComponent<NetcodeOwnershipRelay>() == null)
                        Undo.AddComponent<NetcodeOwnershipRelay>(gameObject);

                    if (gameObject.GetComponent<StandaloneSharedObjectNetcode>() == null)
                        Undo.AddComponent<StandaloneSharedObjectNetcode>(gameObject);
                    break;

                case OrchestrationBackendType.FishNet:
                    if (gameObject.GetComponent<FishNetNetworkObject>() == null)
                        Undo.AddComponent<FishNetNetworkObject>(gameObject);

                    if (gameObject.GetComponent<FishNetNetworkTransform>() == null)
                        Undo.AddComponent<FishNetNetworkTransform>(gameObject);

                    if (gameObject.GetComponent<FishNetOwnershipRelay>() == null)
                        Undo.AddComponent<FishNetOwnershipRelay>(gameObject);

                    if (gameObject.GetComponent<StandaloneSharedObjectFishNet>() == null)
                        Undo.AddComponent<StandaloneSharedObjectFishNet>(gameObject);
                    break;

                case OrchestrationBackendType.Mirror:
                    if (gameObject.GetComponent<MirrorNetworkIdentity>() == null)
                        Undo.AddComponent<MirrorNetworkIdentity>(gameObject);

                    MirrorNetworkTransformUnreliable mirrorTransform = gameObject.GetComponent<MirrorNetworkTransformUnreliable>();
                    if (mirrorTransform == null)
                        mirrorTransform = Undo.AddComponent<MirrorNetworkTransformUnreliable>(gameObject);

                    mirrorTransform.syncDirection = MirrorSyncDirection.ClientToServer;

                    if (gameObject.GetComponent<MirrorOwnershipRelay>() == null)
                        Undo.AddComponent<MirrorOwnershipRelay>(gameObject);

                    if (gameObject.GetComponent<StandaloneSharedObjectMirror>() == null)
                        Undo.AddComponent<StandaloneSharedObjectMirror>(gameObject);
                    break;
            }

            EditorUtility.SetDirty(gameObject);
        }
#endif

        private void EnsureMicroscopeRootComponents(GameObject root, OrchestrationBackendType backendType)
        {
            switch (backendType)
            {
                case OrchestrationBackendType.PUN2:
#if PHOTON_UNITY_NETWORKING
                    PhotonView photonView = root.GetComponent<PhotonView>();
                    if (photonView == null)
                    {
                        Log("PUN2 requires a pre-authored PhotonView on scene objects. Skipping runtime PhotonView creation.");
                        return;
                    }

                    photonView.OwnershipTransfer = OwnershipOption.Takeover;
                    BackendComponentUtility.GetOrAddComponent<MicroscopeAccessControllerPUN2>(root);
#endif
                    break;

                case OrchestrationBackendType.Fusion:
                    Fusion.NetworkObject fusionRoot = root.GetComponent<Fusion.NetworkObject>();
                    if (fusionRoot == null)
                    {
                        Log("Fusion requires a pre-authored NetworkObject on scene objects. Skipping runtime NetworkObject creation.");
                        return;
                    }

                    BackendComponentUtility.GetOrAddComponent<MicroscopeChildTransformSyncFusion>(root);
                    BackendComponentUtility.GetOrAddComponent<MicroscopeAccessControllerFusion>(root);
                    break;

                case OrchestrationBackendType.UnityNetcode:
                    Unity.Netcode.NetworkObject netcodeRoot = root.GetComponent<Unity.Netcode.NetworkObject>();
                    if (netcodeRoot == null)
                    {
                        Log("Netcode requires a pre-authored NetworkObject on scene objects. Skipping runtime NetworkObject creation.");
                        return;
                    }

                    BackendComponentUtility.GetOrAddComponent<NetcodeOwnershipRelay>(root);
                    BackendComponentUtility.GetOrAddComponent<MicroscopeAccessControllerNetcode>(root);
                    BackendComponentUtility.GetOrAddComponent<MicroscopeChildTransformSyncNetcode>(root);
                    break;

                case OrchestrationBackendType.FishNet:
                    FishNetNetworkObject fishNetRoot = root.GetComponent<FishNetNetworkObject>();
                    if (fishNetRoot == null)
                    {
                        Log("FishNet requires a pre-authored NetworkObject on scene objects. Skipping runtime NetworkObject creation.");
                        return;
                    }

                    BackendComponentUtility.GetOrAddComponent<FishNetNetworkTransform>(root);
                    BackendComponentUtility.GetOrAddComponent<FishNetOwnershipRelay>(root);
                    BackendComponentUtility.GetOrAddComponent<MicroscopeAccessControllerFishNet>(root);
                    BackendComponentUtility.GetOrAddComponent<MicroscopeChildTransformSyncFishNet>(root);
                    break;

                case OrchestrationBackendType.Mirror:
                    MirrorNetworkIdentity mirrorRoot = root.GetComponent<MirrorNetworkIdentity>();
                    if (mirrorRoot == null)
                    {
                        Log("Mirror requires a pre-authored NetworkIdentity on scene objects. Skipping runtime NetworkIdentity creation.");
                        return;
                    }

                    BackendComponentUtility.GetOrAddComponent<MirrorOwnershipRelay>(root);
                    BackendComponentUtility.GetOrAddComponent<MicroscopeAccessControllerMirror>(root);
                    BackendComponentUtility.GetOrAddComponent<MicroscopeChildTransformSyncMirror>(root);
                    break;
            }
        }

        private void ValidateMicroscopeRuntimeComponents(GameObject root, OrchestrationBackendType backendType)
        {
            switch (backendType)
            {
                case OrchestrationBackendType.PUN2:
#if PHOTON_UNITY_NETWORKING
                    if (root.GetComponent<PhotonView>() == null)
                        Log("PUN2 microscope root is missing a pre-authored PhotonView.");
#endif
                    break;

                case OrchestrationBackendType.Fusion:
                    if (root.GetComponent<Fusion.NetworkObject>() == null)
                        Log("Fusion microscope root is missing a pre-authored NetworkObject.");
                    if (root.GetComponent<MicroscopeChildTransformSyncFusion>() == null)
                        Log("Fusion microscope root is missing MicroscopeChildTransformSyncFusion.");
                    break;

                case OrchestrationBackendType.UnityNetcode:
                    if (root.GetComponent<Unity.Netcode.NetworkObject>() == null)
                        Log("Netcode microscope root is missing a pre-authored NetworkObject.");
                    break;

                case OrchestrationBackendType.FishNet:
                    if (root.GetComponent<FishNetNetworkObject>() == null)
                        Log("FishNet microscope root is missing a pre-authored NetworkObject.");
                    break;

                case OrchestrationBackendType.Mirror:
                    if (root.GetComponent<MirrorNetworkIdentity>() == null)
                        Log("Mirror microscope root is missing a pre-authored NetworkIdentity.");
                    break;
            }
        }

        private void EnsureStandaloneRootComponents(OrchestrationBackendType backendType)
        {
            switch (backendType)
            {
                case OrchestrationBackendType.PUN2:
#if PHOTON_UNITY_NETWORKING
                    PhotonView photonView = gameObject.GetComponent<PhotonView>();
                    if (photonView == null)
                    {
                        Log("PUN2 requires a pre-authored PhotonView on scene objects. Skipping runtime PhotonView creation.");
                        return;
                    }

                    photonView.OwnershipTransfer = OwnershipOption.Takeover;
                    BackendComponentUtility.GetOrAddComponent<StandaloneSharedObjectPUN2>(gameObject);
#endif
                    break;

                case OrchestrationBackendType.Fusion:
                    Fusion.NetworkObject fusionObject = gameObject.GetComponent<Fusion.NetworkObject>();
                    if (fusionObject == null)
                    {
                        Log("Fusion requires a pre-authored NetworkObject on scene objects. Skipping runtime NetworkObject creation.");
                        return;
                    }

                    BackendComponentUtility.GetOrAddComponent<StandaloneSharedObjectFusion>(gameObject);
                    break;

                case OrchestrationBackendType.UnityNetcode:
                    Unity.Netcode.NetworkObject netcodeObject = gameObject.GetComponent<Unity.Netcode.NetworkObject>();
                    if (netcodeObject == null)
                    {
                        Log("Netcode requires a pre-authored NetworkObject on scene objects. Skipping runtime NetworkObject creation.");
                        return;
                    }

                    BackendComponentUtility.GetOrAddComponent<NetcodeOwnershipRelay>(gameObject);
                    BackendComponentUtility.GetOrAddComponent<StandaloneSharedObjectNetcode>(gameObject);
                    break;

                case OrchestrationBackendType.FishNet:
                    FishNetNetworkObject fishNetObject = gameObject.GetComponent<FishNetNetworkObject>();
                    if (fishNetObject == null)
                    {
                        Log("FishNet requires a pre-authored NetworkObject on scene objects. Skipping runtime NetworkObject creation.");
                        return;
                    }

                    BackendComponentUtility.GetOrAddComponent<FishNetNetworkTransform>(gameObject);
                    BackendComponentUtility.GetOrAddComponent<FishNetOwnershipRelay>(gameObject);
                    BackendComponentUtility.GetOrAddComponent<StandaloneSharedObjectFishNet>(gameObject);
                    break;

                case OrchestrationBackendType.Mirror:
                    MirrorNetworkIdentity mirrorIdentity = gameObject.GetComponent<MirrorNetworkIdentity>();
                    if (mirrorIdentity == null)
                    {
                        Log("Mirror requires a pre-authored NetworkIdentity on scene objects. Skipping runtime NetworkIdentity creation.");
                        return;
                    }

                    MirrorNetworkTransformUnreliable mirrorTransform = BackendComponentUtility.GetOrAddComponent<MirrorNetworkTransformUnreliable>(gameObject);
                    if (mirrorTransform != null)
                        mirrorTransform.syncDirection = MirrorSyncDirection.ClientToServer;

                    BackendComponentUtility.GetOrAddComponent<MirrorOwnershipRelay>(gameObject);
                    BackendComponentUtility.GetOrAddComponent<StandaloneSharedObjectMirror>(gameObject);
                    break;
            }
        }

        private void ValidateStandaloneRuntimeComponents(OrchestrationBackendType backendType)
        {
            switch (backendType)
            {
                case OrchestrationBackendType.PUN2:
#if PHOTON_UNITY_NETWORKING
                    if (gameObject.GetComponent<PhotonView>() == null)
                        Log("PUN2 standalone object is missing a pre-authored PhotonView.");
#endif
                    break;

                case OrchestrationBackendType.Fusion:
                    if (gameObject.GetComponent<Fusion.NetworkObject>() == null)
                        Log("Fusion standalone object is missing a pre-authored NetworkObject.");
                    break;

                case OrchestrationBackendType.UnityNetcode:
                    if (gameObject.GetComponent<Unity.Netcode.NetworkObject>() == null)
                        Log("Netcode standalone object is missing a pre-authored NetworkObject.");
                    break;

                case OrchestrationBackendType.FishNet:
                    if (gameObject.GetComponent<FishNetNetworkObject>() == null)
                        Log("FishNet standalone object is missing a pre-authored NetworkObject.");
                    break;

                case OrchestrationBackendType.Mirror:
                    if (gameObject.GetComponent<MirrorNetworkIdentity>() == null)
                        Log("Mirror standalone object is missing a pre-authored NetworkIdentity.");
                    break;
            }
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[AutomaticBackendObjectBinding:{name}] {message}", this);
        }
    }
}
