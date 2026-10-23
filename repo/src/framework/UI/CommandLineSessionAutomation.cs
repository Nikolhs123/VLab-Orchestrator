using System;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Runtime;
using Thesis.Orchestration.Runtime.Scenarios;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.UI
{
    // Drives the same session controls as SoftwareXRuntimeUI from command-line arguments, so a
    // two-participant run can be launched without manual clicks:
    //
    //   -vlabSession host|client  -vlabRoom <name>  [-vlabName <participant>]
    //   [-vlabQuitWhenDone]  [-vlabTimeoutSeconds <s>]  [-vlabTargetFrameRate <fps>]
    //
    // The host creates the room, the client joins it (retrying until the room is reachable). The
    // configured benchmark scenario (armed in the project configuration) then starts and exports as
    // usual. With -vlabQuitWhenDone the host quits after its terminal export status; a client quits
    // after its participant export once the host has left the session. Every step
    // and the exit reason are logged with the prefix [VLab Automation].
    [DisallowMultipleComponent]
    public sealed class CommandLineSessionAutomation : MonoBehaviour
    {
        private enum Role { None, Host, Client }

        // Long enough for a cloud connection/lobby join to finish: re-issuing Connect while one is in
        // progress interrupted the Fusion runner.
        private const float RetrySeconds = 20f;
        private const float StableConnectionSeconds = 2f;
        private const float UnconfirmedConnectionSeconds = 10f;
        private const float AutomaticConnectGraceSeconds = 20f;
        private const float RoomRetrySeconds = 30f;

        private OrchestrationProjectConfig config;
        private IOrchestrationBackendRuntime backendRuntime;
        private Role role;
        private string roomName;
        private bool quitWhenDone;
        private float timeoutSeconds;
        private float nextActionTime;
        private float connectedSince = -1f;
        private bool connectedEventSeen;
        private IOrchestrationSessionView subscribedSession;
        private bool joinedRoom;
        private bool clientExported;

        public static bool IsRequested() => ParseRole(GetArgument("-vlabSession")) != Role.None;

        private void Awake()
        {
            role = ParseRole(GetArgument("-vlabSession"));
            if (role == Role.None)
            {
                enabled = false;
                return;
            }

            config = Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");
            roomName = GetArgument("-vlabRoom");
            if (string.IsNullOrWhiteSpace(roomName))
                roomName = "vlab-automation";
            quitWhenDone = HasFlag("-vlabQuitWhenDone");
            timeoutSeconds = float.TryParse(GetArgument("-vlabTimeoutSeconds"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float t)
                ? t
                : 0f;

            // Headless players are otherwise uncapped (thousands of frames per second), which changes
            // per-frame behavior; historical runs were rendered near 120 FPS.
            if (int.TryParse(GetArgument("-vlabTargetFrameRate"), out int targetFrameRate) && targetFrameRate > 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = targetFrameRate;
            }

            string participantName = GetArgument("-vlabName");
            if (!string.IsNullOrWhiteSpace(participantName))
                OrchestrationPlayerNameRegistry.SetLocalPlayerName(participantName);

            Log($"role={role} room={roomName} quitWhenDone={quitWhenDone} timeout={timeoutSeconds} " +
                $"targetFrameRate={Application.targetFrameRate} " +
                $"backend={(config != null ? config.backendType.ToString() : "NoConfig")} " +
                $"benchmarkArmed={(config != null && config.enableAutomatedBenchmarkScenario)}");
        }

        private void OnEnable()
        {
            OrchestrationScenarioRunner.BenchmarkStatusChanged += HandleBenchmarkStatus;
        }

        private void OnDisable()
        {
            OrchestrationScenarioRunner.BenchmarkStatusChanged -= HandleBenchmarkStatus;
            if (subscribedSession != null)
                subscribedSession.Connected -= HandleConnected;
            subscribedSession = null;
        }

        private void HandleConnected()
        {
            connectedEventSeen = true;
            Log("Connected event received.");
        }

        private void Update()
        {
            if (timeoutSeconds > 0f && Time.realtimeSinceStartup > timeoutSeconds)
            {
                Quit(2, $"Timeout after {timeoutSeconds} s. Last status: {OrchestrationScenarioRunner.LastBenchmarkStatus}");
                return;
            }

            if (backendRuntime == null)
            {
                if (config == null || Time.unscaledTime < nextActionTime)
                    return;
                nextActionTime = Time.unscaledTime + 0.5f;
                backendRuntime = BackendRuntimeResolver.Resolve(config.backendType);
                if (backendRuntime == null)
                    return;
                Log("Backend runtime resolved.");
            }

            IOrchestrationSessionView session = backendRuntime.SessionView;
            if (session == null)
                return;

            if (subscribedSession != session)
            {
                if (subscribedSession != null)
                    subscribedSession.Connected -= HandleConnected;
                subscribedSession = session;
                session.Connected += HandleConnected;
            }

            // Server backends disconnect the client when the host leaves; with PUN2/Fusion the client
            // stays in the room and becomes master. Player counts were not reliable for this.
            if (clientExported && quitWhenDone && (!session.IsInRoom || session.IsMasterClient))
            {
                Quit(0, "Participant export completed and the host has left the session.");
                return;
            }

            if (session.IsInRoom)
            {
                if (!joinedRoom)
                {
                    joinedRoom = true;
                    Log($"In room '{session.CurrentRoomName}' as {session.LocalUserId} (master={session.IsMasterClient}).");
                }
                return;
            }

            if (joinedRoom)
                return;

            // Same order as a person using the UI: connect, wait until the connection is stable,
            // then create/join once and wait for the room before any retry.
            if (!session.IsConnectedToServer)
            {
                connectedSince = -1f;
                // Backends that connect automatically on start (Fusion joins its lobby) must not get a
                // second, concurrent Connect: wait for the automatic connection first, like the UI flow.
                if (Time.unscaledTime < nextActionTime || Time.realtimeSinceStartup < AutomaticConnectGraceSeconds)
                    return;
                nextActionTime = Time.unscaledTime + RetrySeconds;
                Log("Connect.");
                session.Connect();
                return;
            }

            // Fusion reports cloud readiness before its automatic lobby join completes and raises
            // Connected only afterwards; creating/joining in between shut the runner down. Without a
            // Connected event (already raised before subscription) a longer stable period is required.
            if (connectedSince < 0f)
                connectedSince = Time.unscaledTime;
            float requiredStableSeconds = connectedEventSeen ? StableConnectionSeconds : UnconfirmedConnectionSeconds;
            if (Time.unscaledTime - connectedSince < requiredStableSeconds || Time.unscaledTime < nextActionTime)
                return;

            nextActionTime = Time.unscaledTime + RoomRetrySeconds;
            if (role == Role.Host)
            {
                Log($"CreateRoom '{roomName}'.");
                session.CreateRoom(roomName);
            }
            else
            {
                Log($"JoinRoom '{roomName}'.");
                session.JoinRoom(roomName);
            }
        }

        private void HandleBenchmarkStatus(string status)
        {
            Log($"Benchmark status: {status}");
            if (string.IsNullOrEmpty(status))
                return;

            if (role == Role.Host && quitWhenDone && status.Contains("SAFE TO QUIT"))
                Quit(status.Contains("INVALID") ? 3 : 0, "Host terminal export status reached.");
            else if (role == Role.Host && quitWhenDone && status.Contains("EXPORT FAILED"))
                Quit(4, "Host export failed.");
            else if (role == Role.Client && status.Contains("CLIENT STEPS COMPLETE"))
                clientExported = true;
        }

        private void Quit(int exitCode, string reason)
        {
            Log($"Quit exitCode={exitCode}: {reason}");
            enabled = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(exitCode);
#endif
        }

        private static Role ParseRole(string value)
        {
            if (string.Equals(value, "host", StringComparison.OrdinalIgnoreCase))
                return Role.Host;
            if (string.Equals(value, "client", StringComparison.OrdinalIgnoreCase))
                return Role.Client;
            return Role.None;
        }

        private static string GetArgument(string key)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], key, StringComparison.OrdinalIgnoreCase))
                    return arguments[i + 1];
            }

            return string.Empty;
        }

        private static bool HasFlag(string key)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(argument, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void Log(string message)
        {
            Debug.Log($"[VLab Automation] {message}");
        }
    }
}
