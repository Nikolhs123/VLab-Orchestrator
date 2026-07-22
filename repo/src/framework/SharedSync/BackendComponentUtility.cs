using Thesis.Orchestration.EditorTools;
using Unity.Netcode;
using UnityEngine;
using FishNetNetworkTransform = FishNet.Component.Transforming.NetworkTransform;
using MirrorNetworkBehaviour = global::Mirror.NetworkBehaviour;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;
using MirrorNetworkManager = global::Mirror.NetworkManager;
using MirrorNetworkTransformBase = global::Mirror.NetworkTransformBase;

#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

using Fusion;

namespace Thesis.Orchestration.SharedSync
{
    public static class BackendComponentUtility
    {
        public static void RemoveForeignBackendComponentsFromHierarchy(GameObject root, OrchestrationBackendType selectedBackend)
        {
            if (root == null)
                return;

            Component[] components = root.GetComponentsInChildren<Component>(true);
            foreach (Component component in GetRemovalOrderedComponents(components))
            {
                if (component == null || component is Transform)
                    continue;

                if (!ShouldRemoveComponentForBackend(component, selectedBackend))
                    continue;

                DestroyComponent(component);
            }
        }

        public static void RemoveForeignBackendComponentsFromGameObject(GameObject target, OrchestrationBackendType selectedBackend)
        {
            if (target == null)
                return;

            Component[] components = target.GetComponents<Component>();
            foreach (Component component in GetRemovalOrderedComponents(components))
            {
                if (component == null || component is Transform)
                    continue;

                if (!ShouldRemoveComponentForBackend(component, selectedBackend))
                    continue;

                DestroyComponent(component);
            }
        }

        public static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            if (target == null)
                return null;

            T existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        public static void DestroyComponent(Component component)
        {
            if (component == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(component);
            else
                Object.DestroyImmediate(component);
        }

        public static bool ShouldRemoveComponentForBackend(Component component, OrchestrationBackendType backendType)
        {
            if (component == null)
                return false;

            switch (backendType)
            {
                case OrchestrationBackendType.Fusion:
                    return IsNetcodeComponent(component) || IsPun2Component(component) || IsFishNetComponent(component) || IsMirrorComponent(component);

                case OrchestrationBackendType.UnityNetcode:
                    return IsFusionComponent(component) || IsPun2Component(component) || IsFishNetComponent(component) || IsMirrorComponent(component);

                case OrchestrationBackendType.PUN2:
                    return IsFusionComponent(component) || IsNetcodeComponent(component) || IsFishNetComponent(component) || IsMirrorComponent(component);

                case OrchestrationBackendType.FishNet:
                    return IsFusionComponent(component) || IsNetcodeComponent(component) || IsPun2Component(component) || IsMirrorComponent(component);

                case OrchestrationBackendType.Mirror:
                    return IsFusionComponent(component) || IsNetcodeComponent(component) || IsPun2Component(component) || IsFishNetComponent(component);

                default:
                    return false;
            }
        }

        public static bool IsFusionComponent(Component component)
        {
            return component is Fusion.NetworkObject ||
                   component is Fusion.NetworkBehaviour ||
                   HasNamespace(component, "Thesis.Orchestration.Backends.Fusion");
        }

        public static bool IsNetcodeComponent(Component component)
        {
            return component is Unity.Netcode.NetworkObject ||
                   component is Unity.Netcode.NetworkBehaviour ||
                   HasNamespace(component, "Thesis.Orchestration.Backends.Netcode");
        }

        public static bool IsPun2Component(Component component)
        {
#if PHOTON_UNITY_NETWORKING
            if (component is PhotonView)
                return true;
#endif

            return HasNamespace(component, "Thesis.Orchestration.PUN2") ||
                   HasNamespace(component, "Thesis.Orchestration.Backends.PUN2");
        }

        public static bool IsFishNetComponent(Component component)
        {
            return component is FishNet.Managing.NetworkManager ||
                   component is FishNet.Object.NetworkObject ||
                   component is FishNet.Object.NetworkBehaviour ||
                   component is FishNetNetworkTransform ||
                   HasNamespace(component, "Thesis.Orchestration.Backends.FishNet") ||
                   HasNamespace(component, "FishNet");
        }

        public static bool IsMirrorComponent(Component component)
        {
            return component is MirrorNetworkManager ||
                   component is MirrorNetworkIdentity ||
                   component is MirrorNetworkBehaviour ||
                   component is MirrorNetworkTransformBase ||
                   HasNamespace(component, "Thesis.Orchestration.Backends.Mirror") ||
                   HasNamespace(component, "Mirror");
        }

        private static bool HasNamespace(Component component, string namespacePrefix)
        {
            string componentNamespace = component.GetType().Namespace;
            return !string.IsNullOrWhiteSpace(componentNamespace) &&
                   componentNamespace.StartsWith(namespacePrefix);
        }

        private static System.Collections.Generic.IEnumerable<Component> GetRemovalOrderedComponents(Component[] components)
        {
            if (components == null)
                yield break;

            System.Collections.Generic.List<Component> first = new();
            System.Collections.Generic.List<Component> last = new();

            foreach (Component component in components)
            {
                if (component == null)
                    continue;

                if (IsTransportRootComponent(component))
                    last.Add(component);
                else
                    first.Add(component);
            }

            foreach (Component component in first)
                yield return component;

            foreach (Component component in last)
                yield return component;
        }

        private static bool IsTransportRootComponent(Component component)
        {
#if PHOTON_UNITY_NETWORKING
            if (component is PhotonView)
                return true;
#endif

            return component is Fusion.NetworkObject ||
                   component is Unity.Netcode.NetworkObject ||
                   component is FishNet.Object.NetworkObject ||
                   component is FishNetNetworkTransform ||
                   component is FishNet.Managing.NetworkManager ||
                   component is MirrorNetworkIdentity ||
                   component is MirrorNetworkTransformBase ||
                   component is MirrorNetworkManager;
        }
    }
}
