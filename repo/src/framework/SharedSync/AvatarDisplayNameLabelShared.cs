using TMPro;
using Thesis.Orchestration.Runtime;
using UnityEngine;

namespace Thesis.Orchestration.SharedSync
{
    [DisallowMultipleComponent]
    public class AvatarDisplayNameLabelShared : MonoBehaviour
    {
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private string preferredLabelObjectName = "UserText";
        [SerializeField] private string fallbackDisplayName = "Player";
        [SerializeField] private bool verboseLogs = false;

        private string currentDisplayName;

        public string CurrentDisplayName => currentDisplayName;

        private void Awake()
        {
            ResolveLabelIfNeeded();
            RefreshLabel();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ResolveLabelIfNeeded();
            RefreshLabel();
        }
#endif

        public void ApplyDisplayName(string displayName)
        {
            string sanitized = OrchestrationPlayerNameRegistry.SanitizeDisplayName(displayName, fallbackDisplayName);
            if (string.Equals(currentDisplayName, sanitized, System.StringComparison.Ordinal))
                return;

            currentDisplayName = sanitized;
            RefreshLabel();
            Log($"Applied avatar label '{currentDisplayName}'.");
        }

        private void ResolveLabelIfNeeded()
        {
            if (labelText != null)
                return;

            TMP_Text[] labels = GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text candidate in labels)
            {
                if (candidate == null)
                    continue;

                if (string.Equals(candidate.name, preferredLabelObjectName, System.StringComparison.OrdinalIgnoreCase))
                {
                    labelText = candidate;
                    return;
                }
            }

            if (labels.Length > 0)
                labelText = labels[0];
        }

        private void RefreshLabel()
        {
            if (labelText == null)
                return;

            labelText.text = string.IsNullOrWhiteSpace(currentDisplayName)
                ? fallbackDisplayName
                : currentDisplayName;
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[AvatarDisplayNameLabelShared:{name}] {message}", this);
        }
    }
}
