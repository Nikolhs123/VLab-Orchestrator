using System;
using UnityEngine;

namespace Thesis.Orchestration.Runtime
{
    // "-vlabPhotonRegion <code>" (for example eu) fixes the Photon Cloud region of this process for
    // PUN 2 and Fusion. Without it each process selects the best region at connect time, so two
    // processes can end up in different regions and then cannot see each other's room.
    // The setting is applied before the first scene loads and only changes the in-memory settings
    // of the running player; the manifest of every export records the effective value.
    internal static class PhotonRegionOverride
    {
        private const string Argument = "-vlabPhotonRegion";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            string region = ReadArgument();
            if (string.IsNullOrWhiteSpace(region))
                return;

            try
            {
                var pun = global::Photon.Pun.PhotonNetwork.PhotonServerSettings;
                if (pun != null)
                    pun.AppSettings.FixedRegion = region;

                if (global::Fusion.Photon.Realtime.PhotonAppSettings.TryGetGlobal(out var fusion) && fusion != null)
                    fusion.AppSettings.FixedRegion = region;

                Debug.Log($"[VLab Automation] Photon region fixed to '{region}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VLab Automation] Could not fix the Photon region to '{region}': {ex.Message}");
            }
        }

        private static string ReadArgument()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], Argument, StringComparison.OrdinalIgnoreCase))
                    return arguments[i + 1];
            }

            return string.Empty;
        }
    }
}
