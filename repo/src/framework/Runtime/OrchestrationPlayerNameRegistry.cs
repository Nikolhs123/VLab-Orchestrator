using System;
using System.Collections.Generic;
using UnityEngine;

namespace Thesis.Orchestration.Runtime
{
    public static class OrchestrationPlayerNameRegistry
    {
        private const string LocalPlayerNamePrefsKey = "Thesis.Orchestration.LocalPlayerName";
        private const string DefaultLocalPlayerName = "Player";
        private const int MaxDisplayNameLength = 24;

        private static readonly Dictionary<string, string> displayNamesByUserId = new(StringComparer.OrdinalIgnoreCase);

        private static string cachedLocalPlayerName;

        public static event Action<string> LocalPlayerNameChanged;

        public static string LocalPlayerName => GetLocalPlayerName();

        public static string GetLocalPlayerName()
        {
            if (!string.IsNullOrWhiteSpace(cachedLocalPlayerName))
                return cachedLocalPlayerName;

            cachedLocalPlayerName = SanitizeDisplayName(
                PlayerPrefs.GetString(LocalPlayerNamePrefsKey, DefaultLocalPlayerName),
                DefaultLocalPlayerName);

            return cachedLocalPlayerName;
        }

        public static string SetLocalPlayerName(string value)
        {
            string sanitized = SanitizeDisplayName(value, DefaultLocalPlayerName);
            if (string.Equals(cachedLocalPlayerName, sanitized, StringComparison.Ordinal))
                return cachedLocalPlayerName;

            cachedLocalPlayerName = sanitized;
            PlayerPrefs.SetString(LocalPlayerNamePrefsKey, cachedLocalPlayerName);
            LocalPlayerNameChanged?.Invoke(cachedLocalPlayerName);
            return cachedLocalPlayerName;
        }

        public static void RegisterDisplayName(string userId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return;

            string sanitized = SanitizeDisplayName(displayName, userId);
            if (string.IsNullOrWhiteSpace(sanitized))
                return;

            displayNamesByUserId[userId] = sanitized;
        }

        public static string ResolveDisplayName(string userId, string fallback = null)
        {
            if (!string.IsNullOrWhiteSpace(userId) &&
                displayNamesByUserId.TryGetValue(userId, out string mappedName) &&
                !string.IsNullOrWhiteSpace(mappedName))
            {
                return mappedName;
            }

            if (!string.IsNullOrWhiteSpace(fallback))
                return SanitizeDisplayName(fallback, userId ?? DefaultLocalPlayerName);

            if (!string.IsNullOrWhiteSpace(userId))
                return userId;

            return GetLocalPlayerName();
        }

        public static string SanitizeDisplayName(string value, string fallback = null)
        {
            string candidate = string.IsNullOrWhiteSpace(value) ? fallback : value;
            if (string.IsNullOrWhiteSpace(candidate))
                candidate = DefaultLocalPlayerName;

            candidate = candidate.Trim();
            while (candidate.Contains("  "))
                candidate = candidate.Replace("  ", " ");

            if (candidate.Length > MaxDisplayNameLength)
                candidate = candidate.Substring(0, MaxDisplayNameLength).TrimEnd();

            return string.IsNullOrWhiteSpace(candidate) ? DefaultLocalPlayerName : candidate;
        }
    }
}
