using System;
using UnityEngine;

namespace Thesis.Orchestration.SharedSync
{
    [RequireComponent(typeof(Slide))]
    public class SlideNetworkStateAdapter : MonoBehaviour, IStandaloneNetworkStateAdapter
    {
        [Serializable]
        private class SlideNetworkState
        {
            public bool hasOilDrop;
        }

        [Header("References")]
        [SerializeField] private Slide slide;

        [Header("Optional Spawn References")]
        [SerializeField] private GameObject oilDropPrototype;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private void Awake()
        {
            if (slide == null)
                slide = GetComponent<Slide>();
        }

        public string CaptureNetworkState()
        {
            SlideNetworkState state = new SlideNetworkState
            {
                hasOilDrop = slide != null && slide.OilDropOn != null
            };

            return JsonUtility.ToJson(state);
        }

        public void ApplyNetworkState(string stateJson)
        {
            if (string.IsNullOrWhiteSpace(stateJson) || slide == null)
                return;

            SlideNetworkState state = JsonUtility.FromJson<SlideNetworkState>(stateJson);
            if (state == null)
                return;

            bool localHasOilDrop = slide.OilDropOn != null;

            if (state.hasOilDrop && !localHasOilDrop)
            {
                if (oilDropPrototype != null)
                {
                    slide.OilDropOn = Instantiate(oilDropPrototype, slide.transform);
                    slide.OilDropOn.transform.localPosition = new Vector3(0.00058f, 0.00033f, 0f);
                    slide.OilDropOn.transform.localEulerAngles = Vector3.zero;
                    slide.OilDropOn.transform.localScale = Vector3.one;

                    Log("Created oil drop from replicated state.");
                }
            }
            else if (!state.hasOilDrop && localHasOilDrop)
            {
                Destroy(slide.OilDropOn);
                slide.OilDropOn = null;
                Log("Removed oil drop from replicated state.");
            }
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[SlideNetworkStateAdapter:{name}] {message}", this);
        }
    }
}