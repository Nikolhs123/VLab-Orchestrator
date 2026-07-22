using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Thesis.Orchestration.Telemetry
{
    /// <summary>
    /// UI controller for one runtime-spawned user status card.
    /// Displays current user state, access, wait/use timings, and counts.
    /// </summary>
    public class UserStatusCardUI : MonoBehaviour
    {
        public enum UserCardState
        {
            Idle = 0,
            Waiting = 1,
            Active = 2
        }

        [Header("Visual References")]
        [SerializeField] private Image background;
        [SerializeField] private Image accentBorder;

        [Header("Header Texts")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text roleText;
        [SerializeField] private TMP_Text stateBadgeText;

        [Header("Body Left Texts")]
        [SerializeField] private TMP_Text accessText;
        [SerializeField] private TMP_Text currentWaitText;
        [SerializeField] private TMP_Text totalWaitText;

        [Header("Body Right Texts")]
        [SerializeField] private TMP_Text currentUsageText;
        [SerializeField] private TMP_Text totalUsageText;
        [SerializeField] private TMP_Text countsText;

        [Header("State Colors")]
        [SerializeField] private Color idleBackgroundColor = new Color(0.18f, 0.24f, 0.34f, 1f);
        [SerializeField] private Color waitingBackgroundColor = new Color(0.30f, 0.23f, 0.10f, 1f);
        [SerializeField] private Color activeBackgroundColor = new Color(0.14f, 0.32f, 0.18f, 1f);

        [SerializeField] private Color idleAccentColor = new Color(0.55f, 0.60f, 0.68f, 1f);
        [SerializeField] private Color waitingAccentColor = new Color(0.95f, 0.68f, 0.20f, 1f);
        [SerializeField] private Color activeAccentColor = new Color(0.35f, 0.95f, 0.45f, 1f);

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = false;

        public string UserId { get; private set; }

        public void SetData(
            string userId,
            string displayName,
            int actorNumber,
            bool isMasterClient,
            UserCardState state,
            bool hasAccess,
            string currentWait,
            string totalWait,
            string currentUsage,
            string totalUsage,
            int requestCount,
            int grantCount,
            int releaseCount,
            int timeoutCount)
        {
            UserId = userId;

            if (nameText != null)
                nameText.text = $"{displayName} (Actor {actorNumber})";

            if (roleText != null)
                roleText.text = isMasterClient ? "MASTER" : "CLIENT";

            if (stateBadgeText != null)
                stateBadgeText.text = StateToLabel(state);

            if (accessText != null)
                accessText.text = $"Access: {(hasAccess ? "YES" : "NO")}";

            if (currentWaitText != null)
                currentWaitText.text = $"Current Wait: {currentWait}";

            if (totalWaitText != null)
                totalWaitText.text = $"Total Wait: {totalWait}";

            if (currentUsageText != null)
                currentUsageText.text = $"Current Usage: {currentUsage}";

            if (totalUsageText != null)
                totalUsageText.text = $"Total Usage: {totalUsage}";

            if (countsText != null)
                countsText.text = $"Req/Grant/Rel/TO: {requestCount} / {grantCount} / {releaseCount} / {timeoutCount}";

            ApplyStateVisuals(state);

            Log($"Updated card for {displayName} ({userId}) with state {state}.");
        }

        private void ApplyStateVisuals(UserCardState state)
        {
            if (background != null)
                background.color = GetBackgroundColor(state);

            if (accentBorder != null)
                accentBorder.color = GetAccentColor(state);
        }

        private string StateToLabel(UserCardState state)
        {
            return state switch
            {
                UserCardState.Active => "ACTIVE",
                UserCardState.Waiting => "WAITING",
                _ => "IDLE"
            };
        }

        private Color GetBackgroundColor(UserCardState state)
        {
            return state switch
            {
                UserCardState.Active => activeBackgroundColor,
                UserCardState.Waiting => waitingBackgroundColor,
                _ => idleBackgroundColor
            };
        }

        private Color GetAccentColor(UserCardState state)
        {
            return state switch
            {
                UserCardState.Active => activeAccentColor,
                UserCardState.Waiting => waitingAccentColor,
                _ => idleAccentColor
            };
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[UserStatusCardUI] {message}", this);
        }
    }
}