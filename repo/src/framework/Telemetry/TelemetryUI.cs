using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Thesis.Orchestration.Analytics;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Runtime;
using Thesis.Orchestration.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Thesis.Orchestration.Telemetry
{
    public class TelemetryUI : MonoBehaviour
    {
        [Header("Core References")]
        [SerializeField] private OrchestrationProjectConfig projectConfig;
        [SerializeField] private TelemetryLoggerCore telemetryLogger;
        [SerializeField] private UsageTimingTracker usageTimingTracker;

        [Header("Canvas Roots")]
        [SerializeField] private GameObject networkingCanvasRoot;
        [SerializeField] private GameObject masterDashboardCanvasRoot;

        [Header("Networking Canvas - Input")]
        [SerializeField] private TMP_InputField networkingRoomNameInput;
        [SerializeField] private TMP_InputField networkingPlayerNameInput;

        [Header("Networking Canvas - Buttons")]
        [SerializeField] private Button networkingConnectButton;
        [SerializeField] private Button networkingCreateRoomButton;
        [SerializeField] private Button networkingJoinRoomButton;
        [SerializeField] private Button networkingLeaveRoomButton;
        [SerializeField] private Button networkingBeginSessionButton;

        [Header("Networking Canvas - Status Texts")]
        [SerializeField] private TMP_Text networkingConnectionStatusText;
        [SerializeField] private TMP_Text networkingRoomStatusText;
        [SerializeField] private TMP_Text networkingSessionStateText;
        [SerializeField] private TMP_Text networkingLocalPlayerText;
        [SerializeField] private TMP_Text networkingAvailableRoomsText;
        [SerializeField] private TMP_Text networkingPlayerListText;

        [Header("Master Dashboard - Resource Summary")]
        [SerializeField] private TMP_Text masterConnectionStatusText;
        [SerializeField] private TMP_Text masterSessionStateText;
        [SerializeField] private TMP_Text masterRoomStatusText;
        [SerializeField] private TMP_Text masterLocalPlayerText;
        [SerializeField] private TMP_Text masterQueueRuntimeStateText;
        [SerializeField] private TMP_Text masterCurrentInstrumentText;
        [SerializeField] private TMP_Text masterCurrentHolderText;
        [SerializeField] private TMP_Text masterCurrentQueueText;

        [Header("Master Dashboard - Analytics Summary")]
        [SerializeField] private TMP_Text masterCurrentUsageTimeText;
        [SerializeField] private TMP_Text masterLocalWaitTimeText;
        [SerializeField] private TMP_Text masterInstrumentTotalUsageText;
        [SerializeField] private TMP_Text masterUserStatsText;

        [Header("Master Dashboard - User Cards")]
        [SerializeField] private Transform masterUserCardsContainer;
        [SerializeField] private UserStatusCardUI userStatusCardPrefab;
        [SerializeField] private string userStatusCardResourcesPath = "UserStatusCard";

        [Header("Master Dashboard - Telemetry")]
        [SerializeField] private TMP_Text masterTelemetryText;
        [SerializeField] private ScrollRect masterTelemetryScrollRect;

        [Header("Master Dashboard - Telemetry Buttons")]
        [SerializeField] private Button masterExportAnalyticsButton;
        [SerializeField] private Button masterClearTelemetryButton;

        [Header("Master Dashboard - Debug Controls")]
        [SerializeField] private Button masterRequestAccessButton;
        [SerializeField] private Button masterReleaseAccessButton;
        [SerializeField] private TMP_Text masterDebugStatusText;

        [Header("Display Settings")]
        [SerializeField] private int maxVisibleTelemetryEntries = 35;
        [SerializeField] private bool autoRefresh = true;
        [SerializeField] private float refreshInterval = 0.25f;
        [SerializeField] private bool autoScrollTelemetryToBottom = false;

        [Header("Button Feedback")]
        [SerializeField] private Color sharedButtonPressedColor = new Color32(22, 138, 173, 255);
        [SerializeField] private float keyboardButtonPressDuration = 0.12f;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = false;

        private readonly Dictionary<string, UserStatusCardUI> spawnedUserCards = new();

        private IOrchestrationBackendRuntime backendRuntime;
        private IOrchestrationSessionView sessionView;
        private IOrchestrationQueueView queueView;

        private float refreshTimer;
        private int lastTelemetryEntryCount = -1;
        private string lastUserCardStateSignature = string.Empty;
        private bool lastSessionStartedState;

        private bool runtimeEventsBound;
        private float runtimeResolveRetryTimer;
        [SerializeField] private float runtimeResolveRetryInterval = 0.25f;
        [SerializeField] private float exportFinalizationTimeoutSeconds = 2f;
        private Coroutine exportCoroutine;
        private GameObject benchmarkStatusBanner;
        private TMP_Text benchmarkStatusBannerText;

        private void Awake()
        {
            ResolveDashboardTextReferences();
            ResolvePlayerNameInputIfNeeded();
            ApplyConfiguredLocalPlayerNameToInputField();
            ResolveRuntimeReferences();
            ResolveEvaluationMetricsManager();
            ConfigureConsistentButtonFeedback();
            RegisterButtonListeners();
        }

        private void ConfigureConsistentButtonFeedback()
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            foreach (Button button in buttons)
            {
                if (button == null)
                    continue;

                ConsistentButtonPressFeedback feedback = button.GetComponent<ConsistentButtonPressFeedback>();
                if (feedback == null)
                    feedback = button.gameObject.AddComponent<ConsistentButtonPressFeedback>();

                feedback.Configure(sharedButtonPressedColor, keyboardButtonPressDuration);
            }
        }

        private void OnEnable()
        {
            ResolvePlayerNameInputIfNeeded();
            ApplyConfiguredLocalPlayerNameToInputField();
            ResolveRuntimeReferences();
            TryBindRuntimeEvents();

            if (networkingRoomNameInput != null)
                networkingRoomNameInput.onValueChanged.AddListener(HandleRoomNameChanged);

            if (networkingPlayerNameInput != null)
                networkingPlayerNameInput.onValueChanged.AddListener(HandlePlayerNameChanged);
        }

        private void OnDisable()
        {
            UnbindRuntimeEvents();

            if (networkingRoomNameInput != null)
                networkingRoomNameInput.onValueChanged.RemoveListener(HandleRoomNameChanged);

            if (networkingPlayerNameInput != null)
                networkingPlayerNameInput.onValueChanged.RemoveListener(HandlePlayerNameChanged);

            if (exportCoroutine != null)
            {
                StopCoroutine(exportCoroutine);
                exportCoroutine = null;
            }
        }

        private void Start()
        {
            ResolvePlayerNameInputIfNeeded();
            ApplyConfiguredLocalPlayerNameToInputField();
            ResolveRuntimeReferences();
            RefreshAllUI(forceTelemetryRefresh: true, forceUserCardRefresh: true);
        }

        private void Update()
        {
            runtimeResolveRetryTimer += Time.deltaTime;
            if ((!runtimeEventsBound || sessionView == null) && runtimeResolveRetryTimer >= runtimeResolveRetryInterval)
            {
                runtimeResolveRetryTimer = 0f;

                ResolveRuntimeReferences();
                TryBindRuntimeEvents();
            }

            RefreshButtonStates();

            if (!autoRefresh)
                return;

            refreshTimer += Time.deltaTime;
            if (refreshTimer < refreshInterval)
                return;

            refreshTimer = 0f;

            RefreshCanvasState();
            RefreshTopBarTexts();
            RefreshNetworkingSessionUI();
            RefreshQueueSummaryUI();
            RefreshAnalyticsUI();
            TryRefreshTelemetryUIIfNeeded();
            TryRefreshUserCardsIfNeeded();
        }

        private void TryBindRuntimeEvents()
        {
            if (runtimeEventsBound)
                return;

            if (sessionView == null && queueView == null)
                return;

            if (sessionView != null)
            {
                sessionView.Connected += HandleConnected;
                sessionView.ConnectionFailed += HandleConnectionFailed;
                sessionView.RoomCreated += HandleRoomCreated;
                sessionView.RoomJoined += HandleRoomJoined;
                sessionView.SessionStateChanged += HandleSessionStateChanged;
                sessionView.RoomsChanged += HandleRoomsChanged;
                sessionView.PlayersChanged += HandlePlayersChanged;
                sessionView.StatusMessage += HandleStatusMessage;
            }

            if (queueView != null)
                queueView.RuntimeStateChanged += HandleQueueRuntimeStateUpdated;

            runtimeEventsBound = true;
            Log("TelemetryUI bound to backend runtime events.");
        }

        private void UnbindRuntimeEvents()
        {
            if (!runtimeEventsBound)
                return;

            if (sessionView != null)
            {
                sessionView.Connected -= HandleConnected;
                sessionView.ConnectionFailed -= HandleConnectionFailed;
                sessionView.RoomCreated -= HandleRoomCreated;
                sessionView.RoomJoined -= HandleRoomJoined;
                sessionView.SessionStateChanged -= HandleSessionStateChanged;
                sessionView.RoomsChanged -= HandleRoomsChanged;
                sessionView.PlayersChanged -= HandlePlayersChanged;
                sessionView.StatusMessage -= HandleStatusMessage;
            }

            if (queueView != null)
                queueView.RuntimeStateChanged -= HandleQueueRuntimeStateUpdated;

            runtimeEventsBound = false;
        }

        private void ResolveProjectConfig()
        {
            if (projectConfig != null)
                return;

            projectConfig = Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");
        }

        private void ResolveRuntimeReferences()
        {
            ResolveProjectConfig();

            if (telemetryLogger == null)
                telemetryLogger = TelemetryLoggerCore.Instance;

            if (usageTimingTracker == null)
                usageTimingTracker = UsageTimingTracker.Instance;

            if (projectConfig != null)
            {
                backendRuntime = BackendRuntimeResolver.Resolve(projectConfig.backendType);
                sessionView = backendRuntime?.SessionView;
                queueView = backendRuntime?.QueueView;

                Debug.Log(
                    $"[TelemetryUI] backend={projectConfig.backendType}, backendRuntime={(backendRuntime != null ? backendRuntime.GetType().Name : "NULL")}, sessionViewNull={sessionView == null}, queueViewNull={queueView == null}",
                    this);
            }
            else
            {
                backendRuntime = null;
                sessionView = null;
                queueView = null;

                Debug.LogWarning("[TelemetryUI] projectConfig is NULL", this);
            }
        }

        private void RegisterButtonListeners()
        {
            RegisterButton(networkingConnectButton, Connect);
            RegisterButton(networkingCreateRoomButton, CreateRoom);
            RegisterButton(networkingJoinRoomButton, JoinRoom);
            RegisterButton(networkingLeaveRoomButton, LeaveRoom);
            RegisterButton(networkingBeginSessionButton, BeginSession);

            RegisterButton(masterRequestAccessButton, RequestAccess);
            RegisterButton(masterReleaseAccessButton, ReleaseAccess);

            RegisterButton(masterClearTelemetryButton, ClearTelemetry);
            RegisterButton(masterExportAnalyticsButton, ExportAnalyticsCsv);
        }

        private void RegisterButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null || action == null)
                return;

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        public void Connect() => sessionView?.Connect();

        public void CreateRoom()
        {
            string roomName = GetTypedRoomName();

            if (string.IsNullOrWhiteSpace(roomName))
            {
                SetDebugStatus("Enter a room name before creating.");
                return;
            }

            sessionView?.CreateRoom(roomName);
        }

        public void JoinRoom()
        {
            string roomName = GetEffectiveRoomName();
            if (string.IsNullOrWhiteSpace(roomName))
            {
                SetDebugStatus("No room available to join.");
                return;
            }

            sessionView?.JoinRoom(roomName);
        }

        public void LeaveRoom() => sessionView?.LeaveRoom();

        public void BeginSession() => sessionView?.BeginSession();

        public void RequestAccess() => queueView?.RequestAccessForLocalPlayer();

        public void ReleaseAccess() => queueView?.ReleaseAccessForLocalPlayer();

        public void ClearTelemetry()
        {
            if (telemetryLogger == null)
                telemetryLogger = TelemetryLoggerCore.Instance;
            telemetryLogger?.ClearAll();
            lastTelemetryEntryCount = telemetryLogger != null ? telemetryLogger.EventCount : 0;
            RefreshTelemetryUI(forceScrollDecision: false);
        }

        private void HandleConnected() => RefreshAllUI(false, true);
        private void HandleConnectionFailed(string message)
        {
            SetDebugStatus(message);
            RefreshAllUI(false, true);
        }

        private void HandleRoomCreated(string roomName) => RefreshAllUI(false, true);
        private void HandleRoomJoined(string roomName) => RefreshAllUI(false, true);
        private void HandleSessionStateChanged() => RefreshAllUI(false, true);
        private void HandleRoomsChanged()
        {
            RefreshNetworkingSessionUI();

            if (networkingRoomNameInput != null &&
                string.IsNullOrWhiteSpace(networkingRoomNameInput.text) &&
                sessionView != null &&
                sessionView.AvailableRooms != null &&
                sessionView.AvailableRooms.Count > 0)
            {
                networkingRoomNameInput.text = sessionView.AvailableRooms[0];
            }

            RefreshButtonStates();
        }
        private void HandlePlayersChanged() => RefreshAllUI(false, true);

        private void HandleStatusMessage(string message)
        {
            SetDebugStatus(message);
            RefreshTopBarTexts();
            RefreshButtonStates();
        }

        private void HandleRoomNameChanged(string _) => RefreshButtonStates();

        private void HandlePlayerNameChanged(string value)
        {
            string sanitized = OrchestrationPlayerNameRegistry.SetLocalPlayerName(value);
            if (networkingPlayerNameInput != null &&
                !string.Equals(networkingPlayerNameInput.text, sanitized, StringComparison.Ordinal))
            {
                networkingPlayerNameInput.SetTextWithoutNotify(sanitized);
            }

            RefreshTopBarTexts();
            RefreshNetworkingSessionUI();
            TryRefreshUserCardsIfNeeded(force: true);
        }

        private void ResolvePlayerNameInputIfNeeded()
        {
            if (networkingPlayerNameInput != null)
                return;

            if (networkingCanvasRoot != null)
            {
                TMP_InputField[] inputFields = networkingCanvasRoot.GetComponentsInChildren<TMP_InputField>(true);
                foreach (TMP_InputField candidate in inputFields)
                {
                    if (candidate == null)
                        continue;

                    if (string.Equals(candidate.name, "PlayerNameInput", StringComparison.OrdinalIgnoreCase))
                    {
                        networkingPlayerNameInput = candidate;
                        ConfigurePlayerNameInput(networkingPlayerNameInput);
                        return;
                    }
                }
            }

            if (networkingRoomNameInput == null)
                return;

            GameObject clone = Instantiate(networkingRoomNameInput.gameObject, networkingRoomNameInput.transform.parent);
            clone.name = "PlayerNameInput";

            RectTransform roomRect = networkingRoomNameInput.transform as RectTransform;
            RectTransform cloneRect = clone.transform as RectTransform;
            if (roomRect != null && cloneRect != null)
            {
                cloneRect.anchorMin = roomRect.anchorMin;
                cloneRect.anchorMax = roomRect.anchorMax;
                cloneRect.pivot = roomRect.pivot;
                cloneRect.sizeDelta = roomRect.sizeDelta;
                cloneRect.anchoredPosition = roomRect.anchoredPosition + new Vector2(0f, -38f);
                cloneRect.localScale = roomRect.localScale;
                cloneRect.localRotation = roomRect.localRotation;
            }

            clone.transform.SetSiblingIndex(networkingRoomNameInput.transform.GetSiblingIndex() + 1);

            networkingPlayerNameInput = clone.GetComponent<TMP_InputField>();
            ConfigurePlayerNameInput(networkingPlayerNameInput);
        }

        private void ConfigurePlayerNameInput(TMP_InputField inputField)
        {
            if (inputField == null)
                return;

            inputField.characterLimit = 24;
            inputField.contentType = TMP_InputField.ContentType.Standard;
            inputField.lineType = TMP_InputField.LineType.SingleLine;

            if (inputField.placeholder is TMP_Text placeholderText)
                placeholderText.text = "User Name";

            if (inputField.textComponent != null && string.IsNullOrWhiteSpace(inputField.textComponent.text))
                inputField.textComponent.text = string.Empty;
        }

        private void ApplyConfiguredLocalPlayerNameToInputField()
        {
            if (networkingPlayerNameInput == null)
                return;

            networkingPlayerNameInput.SetTextWithoutNotify(OrchestrationPlayerNameRegistry.GetLocalPlayerName());
        }

        private void HandleQueueRuntimeStateUpdated()
        {
            RefreshQueueSummaryUI();
            RefreshAnalyticsUI();
            TryRefreshUserCardsIfNeeded(force: true);
        }

        public void RefreshAllUI(bool forceTelemetryRefresh = false, bool forceUserCardRefresh = false)
        {
            ResolveRuntimeReferences();

            RefreshCanvasState();
            RefreshTopBarTexts();
            RefreshNetworkingSessionUI();
            RefreshQueueSummaryUI();
            RefreshAnalyticsUI();

            if (forceTelemetryRefresh)
                RefreshTelemetryUI(forceScrollDecision: false);
            else
                TryRefreshTelemetryUIIfNeeded();

            if (forceUserCardRefresh)
                RefreshUserCards();
            else
                TryRefreshUserCardsIfNeeded();

            RefreshButtonStates();
        }

        private void RefreshCanvasState()
        {
            bool sessionStarted = sessionView != null && sessionView.SessionStarted;

            if (sessionStarted == lastSessionStartedState &&
                networkingCanvasRoot != null &&
                masterDashboardCanvasRoot != null &&
                networkingCanvasRoot.activeSelf == !sessionStarted &&
                masterDashboardCanvasRoot.activeSelf == sessionStarted)
            {
                return;
            }

            lastSessionStartedState = sessionStarted;

            if (networkingCanvasRoot != null)
                networkingCanvasRoot.SetActive(!sessionStarted);

            if (masterDashboardCanvasRoot != null)
                masterDashboardCanvasRoot.SetActive(sessionStarted);

            Log($"Canvas state refreshed. SessionStarted={sessionStarted}");
        }

        private void RefreshTopBarTexts()
        {
            string connectionText = sessionView != null && sessionView.IsConnectedToServer ? "Connected: YES" : "Connected: NO";
            string roomText = sessionView != null && sessionView.IsInRoom ? $"Room: {sessionView.CurrentRoomName}" : "Room: None";
            string sessionText = sessionView != null && sessionView.SessionStarted ? "Session: STARTED" : "Session: WAITING";
            string localPlayer = sessionView != null ? sessionView.LocalPlayerDisplayText : "Local: None";

            SetText(networkingConnectionStatusText, connectionText);
            SetText(networkingRoomStatusText, roomText);
            SetText(networkingSessionStateText, sessionText);
            SetText(networkingLocalPlayerText, localPlayer);

            SetText(masterConnectionStatusText, connectionText);
            SetText(masterRoomStatusText, roomText);
            SetText(masterSessionStateText, sessionText);
            SetText(masterLocalPlayerText, localPlayer);
        }

        private void RefreshNetworkingSessionUI()
        {
            if (sessionView == null)
            {
                RefreshNetworkingAvailableRoomsUI(new List<string>());
                RefreshNetworkingPlayerListUI(new List<OrchestrationPlayerInfo>());
                return;
            }

            RefreshNetworkingAvailableRoomsUI(new List<string>(sessionView.AvailableRooms));
            RefreshNetworkingPlayerListUI(BuildVisiblePlayersForUi());
        }

        private void RefreshNetworkingAvailableRoomsUI(List<string> rooms)
        {
            if (networkingAvailableRoomsText == null)
                return;

            if (rooms == null || rooms.Count == 0)
            {
                bool isDirectJoinBackend =
                    sessionView != null && sessionView.LookupRoom(string.Empty) == RoomLookupResult.Unsupported;

                networkingAvailableRoomsText.text = isDirectJoinBackend
                    ? "Available Rooms:\nNone\n\nDirect join only.\nEnter a room name manually."
                    : "Available Rooms:\nNone";
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Available Rooms:");
            foreach (string room in rooms)
                sb.AppendLine($"- {room}");

            networkingAvailableRoomsText.text = sb.ToString();
        }

        private void RefreshNetworkingPlayerListUI(List<OrchestrationPlayerInfo> players)
        {
            if (networkingPlayerListText == null)
                return;

            if (players == null || players.Count == 0)
            {
                networkingPlayerListText.text = "Players:\nNone";
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Players:");
            foreach (var player in players)
                sb.AppendLine($"- {player.DisplayName}{(player.IsMasterClient ? " [MASTER]" : "")}");

            networkingPlayerListText.text = sb.ToString();
        }

        private void RefreshQueueSummaryUI()
        {
            if (queueView == null || !queueView.QueueRuntimeActive)
            {
                SetText(masterQueueRuntimeStateText, "Queue Runtime: INACTIVE");
                SetText(masterCurrentInstrumentText, "Instrument: None");
                SetText(masterCurrentHolderText, "Current Holder: None");
                SetText(masterCurrentQueueText, "Queue: Empty");
                return;
            }

            SetText(masterQueueRuntimeStateText, "Queue Runtime: ACTIVE");
            SetText(masterCurrentInstrumentText, !string.IsNullOrWhiteSpace(queueView.CurrentInstrumentId) ? $"Instrument: {queueView.CurrentInstrumentId}" : "Instrument: None");
            SetText(masterCurrentHolderText, $"Current Holder: {queueView.CurrentHolderUserId}");

            if (masterCurrentQueueText != null)
            {
                if (queueView.CurrentQueueSnapshot == null || queueView.CurrentQueueSnapshot.Count == 0)
                    masterCurrentQueueText.text = "Queue: Empty";
                else
                    masterCurrentQueueText.text = $"Queue: {string.Join(" -> ", queueView.CurrentQueueSnapshot)}";
            }
        }

        private void RefreshAnalyticsUI()
        {
            if (usageTimingTracker == null)
                usageTimingTracker = UsageTimingTracker.Instance;

            string localUserId = GetLocalUserIdForAnalytics();
            string instrumentId = queueView != null ? queueView.CurrentInstrumentId : null;
            string holderUserId = queueView != null ? queueView.CurrentHolderUserId : null;

            if (masterCurrentUsageTimeText != null)
            {
                if (!string.IsNullOrWhiteSpace(holderUserId) &&
                    holderUserId != "None" &&
                    !string.IsNullOrWhiteSpace(instrumentId) &&
                    usageTimingTracker != null)
                {
                    masterCurrentUsageTimeText.text =
                        $"Current Usage: {FormatTimeSpan(usageTimingTracker.GetCurrentInstrumentUsageDuration(instrumentId))}";
                }
                else
                {
                    masterCurrentUsageTimeText.text = "Current Usage: 00:00";
                }
            }

            if (masterLocalWaitTimeText != null)
            {
                masterLocalWaitTimeText.text = usageTimingTracker != null
                    ? $"Local Wait: {FormatTimeSpan(usageTimingTracker.GetCurrentWaitDuration(localUserId))}"
                    : "Local Wait: 00:00";
            }

            if (masterInstrumentTotalUsageText != null)
            {
                if (usageTimingTracker != null && !string.IsNullOrWhiteSpace(instrumentId))
                {
                    var instrumentStats = usageTimingTracker.GetInstrumentStats(instrumentId);
                    var totalUsage = instrumentStats != null ? instrumentStats.TotalUsageTime : TimeSpan.Zero;
                    masterInstrumentTotalUsageText.text = $"Instrument Total Usage: {FormatTimeSpan(totalUsage)}";
                }
                else
                {
                    masterInstrumentTotalUsageText.text = "Instrument Total Usage: 00:00";
                }
            }

            if (masterUserStatsText != null)
            {
                if (usageTimingTracker != null)
                {
                    var stats = usageTimingTracker.GetUserStats(localUserId);
                    if (stats != null)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine($"Requests: {stats.RequestCount}");
                        sb.AppendLine($"Grants: {stats.GrantCount}");
                        sb.AppendLine($"Releases: {stats.ReleaseCount}");
                        sb.AppendLine($"Timeouts: {stats.TimeoutCount}");
                        sb.AppendLine($"Total Wait: {FormatTimeSpan(stats.TotalWaitTime)}");
                        sb.AppendLine($"Total Usage: {FormatTimeSpan(stats.TotalUsageTime)}");
                        sb.AppendLine($"Last Wait: {FormatTimeSpan(stats.LastWaitDuration)}");
                        sb.AppendLine($"Last Usage: {FormatTimeSpan(stats.LastUsageDuration)}");
                        masterUserStatsText.text = sb.ToString();
                    }
                    else
                    {
                        masterUserStatsText.text =
                            "Requests: 0\n" +
                            "Grants: 0\n" +
                            "Releases: 0\n" +
                            "Timeouts: 0\n" +
                            "Total Wait: 00:00\n" +
                            "Total Usage: 00:00";
                    }
                }
                else
                {
                    masterUserStatsText.text = "Usage tracker not found.";
                }
            }
        }

        private void TryRefreshTelemetryUIIfNeeded()
        {
            if (telemetryLogger == null)
                telemetryLogger = TelemetryLoggerCore.Instance;
            if (telemetryLogger == null)
                return;

            int currentCount = telemetryLogger.EventCount;
            if (currentCount == lastTelemetryEntryCount)
                return;

            lastTelemetryEntryCount = currentCount;
            RefreshTelemetryUI(forceScrollDecision: true);
        }

        private void RefreshTelemetryUI(bool forceScrollDecision)
        {
            if (telemetryLogger == null)
                telemetryLogger = TelemetryLoggerCore.Instance;

            bool shouldFollowBottom = false;
            if (masterTelemetryScrollRect != null)
                shouldFollowBottom = masterTelemetryScrollRect.verticalNormalizedPosition <= 0.02f;

            if (masterTelemetryText != null)
            {
                masterTelemetryText.text = telemetryLogger != null
                    ? telemetryLogger.GetFormattedEntries(maxVisibleTelemetryEntries)
                    : "TelemetryLoggerCore not found.";

                ResizeTelemetryContentToText();
            }

            if (autoScrollTelemetryToBottom &&
                forceScrollDecision &&
                masterTelemetryScrollRect != null &&
                shouldFollowBottom)
            {
                Canvas.ForceUpdateCanvases();
                masterTelemetryScrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private void TryRefreshUserCardsIfNeeded(bool force = false)
        {
            string newSignature = BuildUserCardStateSignature();

            if (force || newSignature != lastUserCardStateSignature)
                lastUserCardStateSignature = newSignature;

            // Always refresh card values so timers update even when holder/queue state is unchanged.
            RefreshUserCards();
        }

        private string BuildUserCardStateSignature()
        {
            StringBuilder sb = new StringBuilder();

            if (sessionView != null)
            {
                sb.Append(sessionView.CurrentPlayers.Count);
                foreach (var player in sessionView.CurrentPlayers)
                    sb.Append('|').Append(player.UserId);
            }

            IReadOnlyList<UserCardStatsEntry> shared = (queueView != null && queueView.HasSharedUserCardStats)
                ? queueView.SharedUserCardStats
                : null;

            if (shared != null)
            {
                foreach (UserCardStatsEntry entry in shared)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.userId))
                        continue;

                    sb.Append("|shared:").Append(entry.userId)
                        .Append(':').Append(entry.isHolder ? '1' : '0')
                        .Append(':').Append(entry.isWaiting ? '1' : '0')
                        .Append(':').Append(entry.requestCount)
                        .Append(':').Append(entry.grantCount)
                        .Append(':').Append(entry.releaseCount)
                        .Append(':').Append(entry.timeoutCount);
                }
            }

            sb.Append('|').Append(queueView != null ? queueView.CurrentHolderUserId : "None");

            if (queueView != null && queueView.CurrentQueueSnapshot != null)
            {
                foreach (string queuedUser in queueView.CurrentQueueSnapshot)
                    sb.Append('|').Append(queuedUser);
            }

            return sb.ToString();
        }

        private UserStatusCardUI GetUserCardPrefab()
        {
            if (userStatusCardPrefab != null)
                return userStatusCardPrefab;

            userStatusCardPrefab = Resources.Load<UserStatusCardUI>(userStatusCardResourcesPath);

            if (userStatusCardPrefab == null)
                Log($"Failed to load UserStatusCardUI prefab from Resources path '{userStatusCardResourcesPath}'.");

            return userStatusCardPrefab;
        }

        private void RefreshUserCards()
        {
            if (usageTimingTracker == null)
                usageTimingTracker = UsageTimingTracker.Instance;

            if (masterUserCardsContainer == null || sessionView == null)
                return;

            UserStatusCardUI prefab = GetUserCardPrefab();
            if (prefab == null)
                return;

            List<OrchestrationPlayerInfo> visiblePlayers = BuildVisiblePlayersForUi();
            List<string> activeUserIds = visiblePlayers
                .Where(player => player != null && !string.IsNullOrWhiteSpace(player.UserId))
                .Select(player => player.UserId)
                .ToList();

            IReadOnlyList<UserCardStatsEntry> shared = (queueView != null && queueView.HasSharedUserCardStats)
                ? queueView.SharedUserCardStats
                : null;

            bool hasLiveQueueState = queueView != null && queueView.QueueRuntimeActive;
            string liveHolderUserId = hasLiveQueueState ? queueView.CurrentHolderUserId : string.Empty;
            IReadOnlyList<string> liveQueueSnapshot = hasLiveQueueState ? queueView.CurrentQueueSnapshot : null;

            // Remove cards for disconnected players
            List<string> keysToRemove = new List<string>();
            foreach (var kvp in spawnedUserCards)
            {
                if (!activeUserIds.Contains(kvp.Key))
                {
                    if (kvp.Value != null)
                        Destroy(kvp.Value.gameObject);

                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (string key in keysToRemove)
                spawnedUserCards.Remove(key);

            // Prefer backend-shared card stats so every client shows consistent values.
            Dictionary<string, UserCardStatsEntry> sharedById = BuildSharedUserCardLookup(shared);

            DateTime nowUtc = DateTime.UtcNow;

            foreach (var player in visiblePlayers)
            {
                string userId = player.UserId;
                string displayName = player.DisplayName;

                if (!spawnedUserCards.TryGetValue(userId, out var card) || card == null)
                {
                    card = Instantiate(prefab);
                    RectTransform rt = card.GetComponent<RectTransform>();
                    rt.SetParent(masterUserCardsContainer, false);
                    rt.localScale = Vector3.one;
                    rt.localRotation = Quaternion.identity;
                    rt.anchoredPosition3D = Vector3.zero;

                    spawnedUserCards[userId] = card;
                    Log($"Spawned user card for {userId}.");
                }

                UserCardStatsEntry sharedEntry = null;
                if (sharedById != null)
                {
                    sharedById.TryGetValue(userId, out sharedEntry);
                }

                bool liveIsHolder = hasLiveQueueState &&
                                    string.Equals(liveHolderUserId, userId, StringComparison.OrdinalIgnoreCase);

                bool liveIsWaiting = hasLiveQueueState &&
                                     liveQueueSnapshot != null &&
                                     liveQueueSnapshot.Contains(userId);

                bool isHolder = hasLiveQueueState
                    ? liveIsHolder
                    : sharedEntry != null
                        ? sharedEntry.isHolder
                        : false;

                bool isWaiting = hasLiveQueueState
                    ? !liveIsHolder && liveIsWaiting
                    : sharedEntry != null
                        ? sharedEntry.isWaiting
                        : false;

                UserStatusCardUI.UserCardState state =
                    isHolder ? UserStatusCardUI.UserCardState.Active :
                    isWaiting ? UserStatusCardUI.UserCardState.Waiting :
                    UserStatusCardUI.UserCardState.Idle;

                // Pull counts/totals either from shared snapshot (preferred) or local tracker (fallback).
                var localStats = usageTimingTracker != null ? usageTimingTracker.GetUserStats(userId) : null;

                int requestCount = sharedEntry != null ? sharedEntry.requestCount : (localStats != null ? localStats.RequestCount : 0);
                int grantCount = sharedEntry != null ? sharedEntry.grantCount : (localStats != null ? localStats.GrantCount : 0);
                int releaseCount = sharedEntry != null ? sharedEntry.releaseCount : (localStats != null ? localStats.ReleaseCount : 0);
                int timeoutCount = sharedEntry != null ? sharedEntry.timeoutCount : (localStats != null ? localStats.TimeoutCount : 0);

                TimeSpan totalWait = sharedEntry != null
                    ? TimeSpan.FromSeconds(sharedEntry.totalWaitSeconds)
                    : (localStats != null ? localStats.TotalWaitTime : TimeSpan.Zero);

                TimeSpan totalUsage = sharedEntry != null
                    ? TimeSpan.FromSeconds(sharedEntry.totalUsageSeconds)
                    : (localStats != null ? localStats.TotalUsageTime : TimeSpan.Zero);

                TimeSpan currentWait = TimeSpan.Zero;
                TimeSpan currentUsage = TimeSpan.Zero;

                if (sharedEntry != null)
                {
                    if (!string.IsNullOrWhiteSpace(sharedEntry.waitStartUtcIso) &&
                        DateTime.TryParse(sharedEntry.waitStartUtcIso, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime waitStart))
                    {
                        currentWait = nowUtc - waitStart;
                    }
                    else
                    {
                        currentWait = TimeSpan.FromSeconds(sharedEntry.currentWaitSeconds);
                    }

                    if (!string.IsNullOrWhiteSpace(sharedEntry.usageStartUtcIso) &&
                        DateTime.TryParse(sharedEntry.usageStartUtcIso, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime usageStart))
                    {
                        currentUsage = nowUtc - usageStart;
                    }
                    else
                    {
                        currentUsage = TimeSpan.FromSeconds(sharedEntry.currentUsageSeconds);
                    }
                }
                else
                {
                    currentWait = usageTimingTracker != null ? usageTimingTracker.GetCurrentWaitDuration(userId) : TimeSpan.Zero;
                    currentUsage = usageTimingTracker != null ? usageTimingTracker.GetCurrentUsageDuration(userId) : TimeSpan.Zero;
                }

                card.SetData(
                    userId,
                    displayName,
                    player.NumericId,
                    player.IsMasterClient,
                    state,
                    isHolder,
                    FormatTimeSpan(currentWait),
                    FormatTimeSpan(totalWait),
                    FormatTimeSpan(currentUsage),
                    FormatTimeSpan(totalUsage),
                    requestCount,
                    grantCount,
                    releaseCount,
                    timeoutCount);
            }
        }

        private List<OrchestrationPlayerInfo> BuildVisiblePlayersForUi()
        {
            Dictionary<string, OrchestrationPlayerInfo> merged = new(StringComparer.OrdinalIgnoreCase);

            if (sessionView != null && sessionView.CurrentPlayers != null)
            {
                foreach (OrchestrationPlayerInfo player in sessionView.CurrentPlayers)
                {
                    if (player == null || string.IsNullOrWhiteSpace(player.UserId))
                        continue;

                    merged[player.UserId] = new OrchestrationPlayerInfo
                    {
                        UserId = player.UserId,
                        DisplayName = string.IsNullOrWhiteSpace(player.DisplayName) ? player.UserId : player.DisplayName,
                        NumericId = player.NumericId,
                        IsMasterClient = player.IsMasterClient
                    };
                }
            }

            if (queueView != null && queueView.HasSharedUserCardStats && queueView.SharedUserCardStats != null)
            {
                foreach (UserCardStatsEntry entry in queueView.SharedUserCardStats)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.userId))
                        continue;

                    merged.TryGetValue(entry.userId, out OrchestrationPlayerInfo existing);

                    merged[entry.userId] = new OrchestrationPlayerInfo
                    {
                        UserId = entry.userId,
                        DisplayName = !string.IsNullOrWhiteSpace(entry.displayName) &&
                                      !string.Equals(entry.displayName, entry.userId, StringComparison.OrdinalIgnoreCase)
                            ? entry.displayName
                            : existing != null && !string.IsNullOrWhiteSpace(existing.DisplayName)
                                ? existing.DisplayName
                                : entry.userId,
                        NumericId = existing != null
                            ? existing.NumericId
                            : ParseNumericId(entry.userId),
                        IsMasterClient = entry.isMaster || (existing != null && existing.IsMasterClient)
                    };
                }
            }

            return merged.Values
                .OrderBy(player => player.NumericId)
                .ThenBy(player => player.UserId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private Dictionary<string, UserCardStatsEntry> BuildSharedUserCardLookup(IReadOnlyList<UserCardStatsEntry> shared)
        {
            if (shared == null || shared.Count == 0)
                return null;

            Dictionary<string, UserCardStatsEntry> lookup = new(StringComparer.OrdinalIgnoreCase);
            foreach (UserCardStatsEntry entry in shared)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.userId))
                    continue;

                lookup[entry.userId] = entry;
            }

            return lookup;
        }

        private int ParseNumericId(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return 0;

            string[] prefixes = { "Actor_", "Client_", "Player_" };
            foreach (string prefix in prefixes)
            {
                if (userId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(userId.Substring(prefix.Length), out int parsed))
                {
                    return parsed;
                }
            }

            return 0;
        }

        private void RefreshButtonStates()
        {
            ResolveRuntimeReferences();

            string typedRoomName = GetTypedRoomName();
            string effectiveRoomName = GetEffectiveRoomName();

            bool hasTypedRoomName = !string.IsNullOrWhiteSpace(typedRoomName);
            bool hasEffectiveRoomName = !string.IsNullOrWhiteSpace(effectiveRoomName);

            bool connected = sessionView != null && sessionView.IsConnectedToServer;
            bool inRoom = sessionView != null && sessionView.IsInRoom;

            RoomLookupResult typedRoomLookup = sessionView != null && hasTypedRoomName
                ? sessionView.LookupRoom(typedRoomName)
                : RoomLookupResult.Unavailable;
            RoomLookupResult effectiveRoomLookup = sessionView != null && hasEffectiveRoomName
                ? sessionView.LookupRoom(effectiveRoomName)
                : RoomLookupResult.Unavailable;
            bool typedRoomExists = typedRoomLookup == RoomLookupResult.Exists;
            bool effectiveRoomExists = effectiveRoomLookup == RoomLookupResult.Exists;

            // Backends without room discovery cannot pre-validate create/join; the attempt itself is the check.
            bool isDirectConnectBackend =
                sessionView != null &&
                sessionView.LookupRoom(hasTypedRoomName ? typedRoomName : effectiveRoomName ?? string.Empty) ==
                RoomLookupResult.Unsupported;

            if (networkingConnectButton != null)
                networkingConnectButton.interactable = sessionView != null && !connected;

            if (isDirectConnectBackend)
            {
                if (networkingCreateRoomButton != null)
                    networkingCreateRoomButton.interactable =
                        sessionView != null &&
                        connected &&
                        !inRoom &&
                        hasTypedRoomName;

                if (networkingJoinRoomButton != null)
                    networkingJoinRoomButton.interactable =
                        sessionView != null &&
                        connected &&
                        !inRoom &&
                        hasTypedRoomName;
            }
            else
            {
                if (networkingCreateRoomButton != null)
                    networkingCreateRoomButton.interactable =
                        sessionView != null &&
                        connected &&
                        !inRoom &&
                        hasTypedRoomName &&
                        !typedRoomExists;

                if (networkingJoinRoomButton != null)
                    networkingJoinRoomButton.interactable =
                        sessionView != null &&
                        connected &&
                        !inRoom &&
                        hasEffectiveRoomName &&
                        effectiveRoomExists;
            }

            if (networkingLeaveRoomButton != null)
                networkingLeaveRoomButton.interactable =
                    sessionView != null && inRoom;

            if (networkingBeginSessionButton != null)
                networkingBeginSessionButton.interactable =
                    sessionView != null &&
                    inRoom &&
                    sessionView.IsMasterClient &&
                    !sessionView.SessionStarted;

            bool queueActive = queueView != null && queueView.QueueRuntimeActive;

            if (masterRequestAccessButton != null)
                masterRequestAccessButton.interactable = queueActive;

            if (masterReleaseAccessButton != null)
                masterReleaseAccessButton.interactable = queueActive;
        }

        private string GetTypedRoomName()
        {
            return networkingRoomNameInput != null ? networkingRoomNameInput.text?.Trim() : string.Empty;
        }

        private string GetEffectiveRoomName()
        {
            string typed = GetTypedRoomName();
            if (!string.IsNullOrWhiteSpace(typed))
                return typed;

            if (sessionView != null && sessionView.AvailableRooms != null && sessionView.AvailableRooms.Count > 0)
                return sessionView.AvailableRooms[0];

            return string.Empty;
        }

        private string GetLocalUserIdForAnalytics()
        {
            if (sessionView == null || sessionView.CurrentPlayers == null)
                return "UnknownUser";

            foreach (var player in sessionView.CurrentPlayers)
            {
                if (player.IsMasterClient == sessionView.IsMasterClient &&
                    sessionView.LocalPlayerDisplayText.Contains(player.DisplayName))
                {
                    return player.UserId;
                }
            }

            return sessionView.LocalPlayerDisplayText ?? "UnknownUser";
        }

        private void SetDebugStatus(string message)
        {
            string backend = projectConfig != null ? projectConfig.backendType.ToString() : "Unknown";
            string fullMessage = $"[{backend}] {message}";

            SetText(masterDebugStatusText, fullMessage);
            Debug.Log($"[TelemetryUI] {fullMessage}", this);
        }

        public void SetBenchmarkStatus(string message)
        {
            ResolveDashboardTextReferences();

            string conciseMessage = string.IsNullOrWhiteSpace(message)
                ? "Benchmark status unavailable."
                : message.Split('\n')[0];
            SetDebugStatus(conciseMessage);

            bool failed = message != null &&
                          (message.Contains("INVALID", StringComparison.OrdinalIgnoreCase) ||
                           message.Contains("FAILED", StringComparison.OrdinalIgnoreCase));
            bool successful = !failed &&
                              message != null &&
                              message.Contains("SAFE TO QUIT", StringComparison.OrdinalIgnoreCase);
            ShowBenchmarkStatusBanner(conciseMessage, successful, failed);

            if (masterDebugStatusText == null)
                return;

            masterDebugStatusText.color = failed
                ? new Color32(255, 105, 97, 255)
                : successful
                    ? new Color32(86, 214, 123, 255)
                    : new Color32(255, 205, 86, 255);
            masterDebugStatusText.fontStyle = FontStyles.Bold;
            masterDebugStatusText.enableAutoSizing = true;
            masterDebugStatusText.fontSizeMin = 14f;
            masterDebugStatusText.fontSizeMax = 24f;
            masterDebugStatusText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 700f);
            masterDebugStatusText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 90f);

            if (!string.Equals(conciseMessage, message, StringComparison.Ordinal))
                Debug.Log($"[TelemetryUI] {message}", this);
        }

        private void ShowBenchmarkStatusBanner(string message, bool successful, bool failed)
        {
            if (masterDashboardCanvasRoot == null)
                return;

            if (benchmarkStatusBanner == null)
            {
                benchmarkStatusBanner = new GameObject(
                    "BenchmarkStatusBanner",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                benchmarkStatusBanner.transform.SetParent(masterDashboardCanvasRoot.transform, false);

                RectTransform bannerRect = benchmarkStatusBanner.GetComponent<RectTransform>();
                bannerRect.anchorMin = new Vector2(0.5f, 1f);
                bannerRect.anchorMax = new Vector2(0.5f, 1f);
                bannerRect.pivot = new Vector2(0.5f, 1f);
                bannerRect.anchoredPosition = new Vector2(0f, -18f);
                bannerRect.sizeDelta = new Vector2(900f, 76f);

                Image bannerImage = benchmarkStatusBanner.GetComponent<Image>();
                bannerImage.raycastTarget = false;

                GameObject textObject = new GameObject(
                    "StatusText",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI));
                textObject.transform.SetParent(benchmarkStatusBanner.transform, false);

                RectTransform textRect = textObject.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(18f, 8f);
                textRect.offsetMax = new Vector2(-18f, -8f);

                benchmarkStatusBannerText = textObject.GetComponent<TextMeshProUGUI>();
                TMP_Text sourceText = masterSessionStateText != null ? masterSessionStateText : masterDebugStatusText;
                if (sourceText != null && sourceText.font != null)
                    benchmarkStatusBannerText.font = sourceText.font;

                benchmarkStatusBannerText.alignment = TextAlignmentOptions.Center;
                benchmarkStatusBannerText.enableAutoSizing = true;
                benchmarkStatusBannerText.fontSizeMin = 18f;
                benchmarkStatusBannerText.fontSizeMax = 30f;
                benchmarkStatusBannerText.fontStyle = FontStyles.Bold;
                benchmarkStatusBannerText.raycastTarget = false;
            }

            benchmarkStatusBanner.transform.SetAsLastSibling();
            benchmarkStatusBanner.SetActive(true);
            benchmarkStatusBanner.GetComponent<Image>().color = failed
                ? new Color32(126, 38, 38, 245)
                : successful
                    ? new Color32(18, 92, 63, 245)
                    : new Color32(122, 82, 18, 245);
            benchmarkStatusBannerText.color = Color.white;
            benchmarkStatusBannerText.text = message;
        }

        private void ResolveDashboardTextReferences()
        {
            if (masterDebugStatusText != null)
                return;

            masterDebugStatusText = GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(text => text != null && text.gameObject.name == "DebugStatusText");
        }

        private void ResizeTelemetryContentToText()
        {
            if (masterTelemetryText == null || masterTelemetryScrollRect == null || masterTelemetryScrollRect.content == null)
                return;

            RectTransform content = masterTelemetryScrollRect.content;
            RectTransform viewport = masterTelemetryScrollRect.viewport;
            RectTransform textRect = masterTelemetryText.rectTransform;
            float availableWidth = content.rect.width;
            if (availableWidth <= 1f && viewport != null)
                availableWidth = viewport.rect.width;

            float contentWidth = Mathf.Max(50f, availableWidth - 12f);
            float preferredHeight = masterTelemetryText
                .GetPreferredValues(masterTelemetryText.text, contentWidth, Mathf.Infinity).y + 12f;
            float viewportHeight = viewport != null ? viewport.rect.height : 0f;
            float contentHeight = Mathf.Max(viewportHeight, preferredHeight);

            content.pivot = new Vector2(0.5f, 1f);
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);

            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = new Vector2(0f, -6f);
            textRect.sizeDelta = new Vector2(-12f, preferredHeight);
        }

        private void SetText(TMP_Text target, string value)
        {
            if (target != null)
                target.text = value;
        }

        private string FormatTimeSpan(TimeSpan value)
        {
            return value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
        }

        private EvaluationMetricsManager ResolveEvaluationMetricsManager()
        {
            if (telemetryLogger == null)
                telemetryLogger = TelemetryLoggerCore.Instance;

            if (usageTimingTracker == null)
                usageTimingTracker = UsageTimingTracker.Instance;
            return EvaluationMetricsManager.ResolveOrCreate(this);
        }

        public void ExportAnalyticsCsv()
        {
            if (exportCoroutine != null)
                StopCoroutine(exportCoroutine);

            exportCoroutine = StartCoroutine(ExportAnalyticsCsvRoutine());
        }

        private IEnumerator ExportAnalyticsCsvRoutine()
        {
            EvaluationMetricsManager manager = ResolveEvaluationMetricsManager();
            if (manager == null)
            {
                SetDebugStatus("EvaluationMetricsManager not found.");
                exportCoroutine = null;
                yield break;
            }

            ResolveRuntimeReferences();

            if (projectConfig != null)
            {
                bool initiatedFinalization = OrchestrationExportFinalizationUtility.TryBeginFinalizeForExport(
                    projectConfig.backendType,
                    queueView,
                    out string finalizationStatus);

                if (!string.IsNullOrWhiteSpace(finalizationStatus))
                    SetDebugStatus(finalizationStatus);

                if (initiatedFinalization)
                {
                    float deadline = Time.unscaledTime + Mathf.Max(0.1f, exportFinalizationTimeoutSeconds);
                    while (Time.unscaledTime < deadline)
                    {
                        if (OrchestrationExportFinalizationUtility.IsReadyForExport(queueView))
                            break;

                        yield return null;
                    }

                    if (!OrchestrationExportFinalizationUtility.IsReadyForExport(queueView))
                        SetDebugStatus("Timed out while finalizing the active holder. Exporting current analytics snapshot.");
                }
            }

            string exportPath = manager.ExportAll();
            if (string.IsNullOrWhiteSpace(exportPath))
            {
                SetDebugStatus("Export failed.");
                exportCoroutine = null;
                yield break;
            }

            SetDebugStatus($"CSV export complete.\nPath:\n{exportPath}");
            exportCoroutine = null;
        }

        public void BuildBackendComparisonSummary()
        {
            EvaluationMetricsManager manager = ResolveEvaluationMetricsManager();
            if (manager == null)
            {
                SetDebugStatus("EvaluationMetricsManager not found.");
                return;
            }

            string outputPath = manager.BuildBackendComparisonSummary();
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                SetDebugStatus("Comparison summary build failed.");
                return;
            }

            SetDebugStatus($"Comparison summary generated.\nPath:\n{outputPath}");
        }

        private void Log(string message)
        {
            if (verboseLogs)
                Debug.Log($"[TelemetryUI] {message}", this);
        }
    }
}
