using Unity.Netcode;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Netcode
{
    internal static class NetcodeRuntimeUtility
    {
        public static bool EnsureNetworkManagerConfigInitialized(Unity.Netcode.NetworkManager networkManager)
        {
            if (networkManager == null)
                return false;

            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.Prefabs ??= new NetworkPrefabs();
            return true;
        }

        public static bool EnsureNetworkPrefabRegistered(Unity.Netcode.NetworkManager networkManager, GameObject prefab)
        {
            if (networkManager == null || prefab == null)
                return false;

            if (!EnsureNetworkManagerConfigInitialized(networkManager))
                return false;

            if (networkManager.NetworkConfig.Prefabs.Contains(prefab))
                return false;

            networkManager.AddNetworkPrefab(prefab);
            return true;
        }
    }
}
