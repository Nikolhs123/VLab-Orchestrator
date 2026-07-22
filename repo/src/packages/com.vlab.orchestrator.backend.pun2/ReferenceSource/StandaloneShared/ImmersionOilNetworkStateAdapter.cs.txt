using System;
using UnityEngine;

namespace Thesis.Orchestration.SharedSync
{
    [RequireComponent(typeof(ImmersionOil))]
    public class ImmersionOilNetworkStateAdapter : MonoBehaviour, IStandaloneNetworkStateAdapter
    {
        [Serializable]
        private class ImmersionOilNetworkState
        {
            public bool hasCapOn;
        }

        [Header("References")]
        [SerializeField] private ImmersionOil immersionOil;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private void Awake()
        {
            if (immersionOil == null)
                immersionOil = GetComponent<ImmersionOil>();
        }

        public string CaptureNetworkState()
        {
            ImmersionOilNetworkState state = new ImmersionOilNetworkState
            {
                hasCapOn = immersionOil != null && immersionOil.CapOn != null
            };

            return JsonUtility.ToJson(state);
        }

        public void ApplyNetworkState(string stateJson)
        {
            if (string.IsNullOrWhiteSpace(stateJson) || immersionOil == null)
                return;

            ImmersionOilNetworkState state = JsonUtility.FromJson<ImmersionOilNetworkState>(stateJson);
            if (state == null)
                return;

            // We only observe here for now.
            // If you want, next we can add exact cap attach syncing by object id/path.
            Log($"Applied replicated state. hasCapOn={state.hasCapOn}");
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[ImmersionOilNetworkStateAdapter:{name}] {message}", this);
        }
    }
}