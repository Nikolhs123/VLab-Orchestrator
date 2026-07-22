using System;
using System.Linq;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Runtime;
using Thesis.Orchestration.Runtime.Scenarios;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.UI
{
    [DisallowMultipleComponent]
    public sealed class SoftwareXRuntimeUI : MonoBehaviour
    {
        private OrchestrationProjectConfig config;
        private IOrchestrationBackendRuntime backendRuntime;
        private string playerName;
        private string roomName = "softwarex-study";
        private string status = "Waiting for backend runtime...";
        private string exportPath = string.Empty;
        private float nextResolveTime;
        private Vector2 scroll;
        private Transform avatarAnchor;
        private Transform[] avatarSpawnSlots = Array.Empty<Transform>();
        private int assignedAvatarSlot = -1;

        private GUIStyle titleStyle;
        private GUIStyle headingStyle;
        private GUIStyle bodyStyle;
        private GUIStyle statusStyle;

        private void Awake()
        {
            config = Resources.Load<OrchestrationProjectConfig>(
                "AIMultiplayerAssistant/OrchestrationProjectConfig");
            playerName = OrchestrationPlayerNameRegistry.GetLocalPlayerName();
            avatarAnchor = EnsureAvatarAnchor();
            avatarSpawnSlots = ResolveAndHideShowcaseParticipants();
            DisableLocalOnlyShowcaseController();
        }

        private void OnEnable()
        {
            OrchestrationScenarioRunner.BenchmarkStatusChanged += HandleBenchmarkStatusChanged;
            if (!string.IsNullOrWhiteSpace(OrchestrationScenarioRunner.LastBenchmarkStatus))
                status = OrchestrationScenarioRunner.LastBenchmarkStatus;
        }

        private void OnDisable()
        {
            OrchestrationScenarioRunner.BenchmarkStatusChanged -= HandleBenchmarkStatusChanged;
        }

        private void Update()
        {
            if (backendRuntime == null && config != null && Time.unscaledTime >= nextResolveTime)
            {
                nextResolveTime = Time.unscaledTime + 0.5f;
                backendRuntime = BackendRuntimeResolver.Resolve(config.backendType);
                if (backendRuntime != null)
                    status = $"{config.backendType} runtime ready.";
            }

            UpdateAvatarAnchor();
        }

        private void OnGUI()
        {
            EnsureStyles();

            float width = Mathf.Min(520f, Screen.width - 24f);
            float height = Mathf.Min(690f, Screen.height - 24f);
            Rect panel = new Rect(12f, 12f, width, height);

            GUILayout.BeginArea(panel, GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("VLAB ORCHESTRATOR", titleStyle);
            GUILayout.Label("SoftwareX five-backend reproducibility showcase", bodyStyle);
            GUILayout.Space(8f);

            DrawIdentitySection();
            DrawSessionSection();
            DrawAllocationSection();
            DrawBenchmarkSection();
            DrawStateSection();

            GUILayout.Space(8f);
            GUILayout.Label(status, statusStyle);
            if (!string.IsNullOrWhiteSpace(exportPath))
                GUILayout.Label($"Last export: {exportPath}", bodyStyle);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawIdentitySection()
        {
            GUILayout.Label("Participant", headingStyle);
            GUILayout.BeginHorizontal();
            playerName = GUILayout.TextField(playerName ?? string.Empty, GUILayout.MinWidth(180f));
            if (GUILayout.Button("Apply name", GUILayout.Width(120f)))
            {
                playerName = OrchestrationPlayerNameRegistry.SetLocalPlayerName(playerName);
                status = $"Participant name set to '{playerName}'.";
            }
            GUILayout.EndHorizontal();
        }

        private void DrawSessionSection()
        {
            IOrchestrationSessionView session = backendRuntime?.SessionView;

            GUILayout.Space(8f);
            GUILayout.Label("Session", headingStyle);
            roomName = GUILayout.TextField(roomName ?? string.Empty);

            GUILayout.BeginHorizontal();
            SetGuiEnabled(session != null && !session.IsConnectedToServer);
            if (GUILayout.Button("Connect"))
                session.Connect();

            SetGuiEnabled(session != null && session.IsConnectedToServer && !session.IsInRoom);
            if (GUILayout.Button("Create"))
                session.CreateRoom(roomName);
            if (GUILayout.Button("Join"))
                session.JoinRoom(roomName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            SetGuiEnabled(session != null && session.IsInRoom && session.IsMasterClient && !session.SessionStarted);
            if (GUILayout.Button("Begin session"))
                session.BeginSession();

            SetGuiEnabled(session != null && session.IsInRoom);
            if (GUILayout.Button("Leave"))
                session.LeaveRoom();
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawAllocationSection()
        {
            IOrchestrationQueueView queue = backendRuntime?.QueueView;

            GUILayout.Space(8f);
            GUILayout.Label("Shared instrument", headingStyle);
            GUILayout.BeginHorizontal();
            SetGuiEnabled(queue != null && queue.QueueRuntimeActive);
            if (GUILayout.Button("Request access"))
                queue.RequestAccessForLocalPlayer();
            if (GUILayout.Button("Release access"))
                queue.ReleaseAccessForLocalPlayer();
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawBenchmarkSection()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Research protocol", headingStyle);
            GUILayout.BeginHorizontal();
            SetGuiEnabled(config != null && backendRuntime?.SessionView?.SessionStarted == true);
            if (GUILayout.Button("Run configured benchmark"))
            {
                config.enableAutomatedBenchmarkScenario = true;
                OrchestrationScenarioRunner runner = FindFirstObjectByType<OrchestrationScenarioRunner>();
                if (runner == null)
                    runner = gameObject.AddComponent<OrchestrationScenarioRunner>();
                runner.Configure(config);
                status = $"Started {config.ResolveAutomatedScenarioId()}.";
            }

            GUI.enabled = true;
            if (GUILayout.Button("Export metrics"))
            {
                EvaluationMetricsManager manager = EvaluationMetricsManager.ResolveOrCreate(this);
                exportPath = manager != null ? manager.ExportAll() : string.Empty;
                status = string.IsNullOrWhiteSpace(exportPath)
                    ? "No metrics manager was available."
                    : "Metrics exported successfully.";
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawStateSection()
        {
            IOrchestrationSessionView session = backendRuntime?.SessionView;
            IOrchestrationQueueView queue = backendRuntime?.QueueView;

            GUILayout.Space(8f);
            GUILayout.Label("Live state", headingStyle);
            GUILayout.Label(
                session == null
                    ? "Backend runtime: resolving"
                    : $"Connected: {session.IsConnectedToServer}   Room: {session.CurrentRoomName}   " +
                      $"Master: {session.IsMasterClient}   Started: {session.SessionStarted}",
                bodyStyle);

            if (session?.CurrentPlayers != null && session.CurrentPlayers.Count > 0)
            {
                string players = string.Join(
                    ", ",
                    session.CurrentPlayers.Select(player =>
                        string.IsNullOrWhiteSpace(player.DisplayName) ? player.UserId : player.DisplayName));
                GUILayout.Label($"Participants: {players}", bodyStyle);
            }

            if (queue != null)
            {
                string queued = queue.CurrentQueueSnapshot == null || queue.CurrentQueueSnapshot.Count == 0
                    ? "none"
                    : string.Join(", ", queue.CurrentQueueSnapshot);
                GUILayout.Label(
                    $"Holder: {EmptyAsNone(queue.CurrentHolderUserId)}   Queue: {queued}",
                    bodyStyle);
            }
        }

        private static void SetGuiEnabled(bool enabled)
        {
            GUI.enabled = enabled;
        }

        private static string EmptyAsNone(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "none" : value;
        }

        private static Transform EnsureAvatarAnchor()
        {
            GameObject existing = GameObject.Find("Ego/Camera/AvatarAnchor");
            if (existing != null)
                return existing.transform;

            var ego = new GameObject("Ego");
            var cameraRoot = new GameObject("Camera");
            cameraRoot.transform.SetParent(ego.transform, false);

            Transform source = Camera.main != null ? Camera.main.transform : null;
            if (source != null)
            {
                cameraRoot.transform.position = source.position;
                cameraRoot.transform.rotation = source.rotation;
            }

            var anchor = new GameObject("AvatarAnchor");
            anchor.transform.SetParent(cameraRoot.transform, false);
            anchor.transform.localPosition = new Vector3(1.2f, -0.8f, 1.5f);
            return anchor.transform;
        }

        private static Transform[] ResolveAndHideShowcaseParticipants()
        {
            MonoBehaviour[] participants = FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(behaviour =>
                    behaviour != null &&
                    behaviour.GetType().FullName ==
                    "VLabOrchestrator.Showcase.ShowcaseParticipantDefinition")
                .OrderBy(behaviour => behaviour.name, StringComparer.Ordinal)
                .ToArray();

            foreach (MonoBehaviour participant in participants)
            {
                foreach (Renderer renderer in participant.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
            }

            return participants.Select(participant => participant.transform).ToArray();
        }

        private void UpdateAvatarAnchor()
        {
            IOrchestrationSessionView session = backendRuntime?.SessionView;
            if (avatarAnchor == null || avatarSpawnSlots.Length == 0 || session == null || !session.IsInRoom)
                return;

            int slotIndex = ResolveLocalAvatarSlot(session);
            if (slotIndex < 0)
                return;

            slotIndex %= avatarSpawnSlots.Length;
            Transform slot = avatarSpawnSlots[slotIndex];
            if (slot == null)
                return;

            avatarAnchor.SetPositionAndRotation(slot.position, slot.rotation);
            assignedAvatarSlot = slotIndex;
        }

        private int ResolveLocalAvatarSlot(IOrchestrationSessionView session)
        {
            if (session.CurrentPlayers != null && session.CurrentPlayers.Count > 0)
            {
                var orderedPlayers = session.CurrentPlayers
                    .Where(player => player != null)
                    .OrderBy(player => player.NumericId)
                    .ThenBy(player => player.UserId, StringComparer.Ordinal)
                    .ToList();
                int localIndex = orderedPlayers.FindIndex(player =>
                    string.Equals(player.UserId, session.LocalUserId, StringComparison.OrdinalIgnoreCase));
                if (localIndex >= 0)
                    return localIndex;
            }

            if (assignedAvatarSlot >= 0)
                return assignedAvatarSlot;

            return session.IsMasterClient ? 0 : Math.Min(1, avatarSpawnSlots.Length - 1);
        }

        private void HandleBenchmarkStatusChanged(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                status = message;
        }

        private static void DisableLocalOnlyShowcaseController()
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour != null && behaviour.GetType().FullName ==
                    "VLabOrchestrator.Showcase.OrchestrationShowcase")
                {
                    behaviour.enabled = false;
                }
            }

            GameObject localInstrument = GameObject.Find("Generic Shared Instrument");
            if (localInstrument != null)
                localInstrument.SetActive(false);
        }

        private void EnsureStyles()
        {
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.2f, 0.85f, 0.78f) }
            };
            headingStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.95f, 0.74f, 0.3f) }
            };
            bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            statusStyle ??= new GUIStyle(bodyStyle)
            {
                fontStyle = FontStyle.Italic,
                normal = { textColor = new Color(0.75f, 0.85f, 0.87f) }
            };
        }
    }
}
