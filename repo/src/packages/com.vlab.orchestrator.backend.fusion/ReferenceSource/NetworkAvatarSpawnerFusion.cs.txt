using Fusion;
using Thesis.Orchestration.SharedSync;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Fusion
{
    public class NetworkAvatarSpawnerFusion : MonoBehaviour
    {
        public static NetworkAvatarSpawnerFusion Instance { get; private set; }

        [Header("Prefab")]
        [SerializeField] private NetworkObject avatarPrefab;
        [SerializeField] private string avatarPrefabResourcesPath = SharedAvatarRuntimeUtility.DefaultAvatarPrefabResourcesPath;

        [Header("Anchor")]
        [SerializeField] private Transform avatarAnchor;
        [SerializeField] private string avatarAnchorPath = "Ego/Camera/AvatarAnchor";

        [Header("Spawn")]
        [SerializeField] private bool spawnAutomatically = false; // IMPORTANT: keep false for now
        [SerializeField] private float retryDelaySeconds = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private SessionOrchestratorFusion orchestrator;
        private NetworkObject localSpawnedAvatar;
        private bool spawnPending;
        private float nextRetryTime;

        public NetworkObject LocalSpawnedAvatar => localSpawnedAvatar;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            orchestrator = SessionOrchestratorFusion.Instance;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            orchestrator ??= SessionOrchestratorFusion.Instance;
        }

        private void Update()
        {
            orchestrator ??= SessionOrchestratorFusion.Instance;

            if (!spawnPending)
                return;

            if (Time.time < nextRetryTime)
                return;

            TrySpawnAvatar();
        }

        public void RequestSpawn()
        {
            spawnPending = true;
            nextRetryTime = Time.time;
        }

        public void TrySpawnAvatar()
        {
            orchestrator ??= SessionOrchestratorFusion.Instance;
            if (orchestrator == null)
            {
                Retry("No SessionOrchestratorFusion found yet.");
                return;
            }

            NetworkRunner runner = orchestrator.Runner;
            if (runner == null || !runner.IsRunning)
            {
                Retry("Fusion runner not ready yet.");
                return;
            }

            if (!orchestrator.IsInRoom)
            {
                Retry("Not in Fusion session yet.");
                return;
            }

            if (runner.LocalPlayer == PlayerRef.None)
            {
                Retry("LocalPlayer not assigned yet.");
                return;
            }

            // Prevent duplicate avatars
            if (runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject existing) && existing != null)
            {
                localSpawnedAvatar = existing;
                spawnPending = false;
                Log("Local player object already exists. Reusing existing avatar.");
                return;
            }

            Transform anchor = ResolveAnchor();
            if (anchor == null)
            {
                Retry($"Could not resolve avatar anchor at '{avatarAnchorPath}'.");
                return;
            }

            NetworkObject prefabToSpawn = ResolveAvatarPrefab();
            if (prefabToSpawn == null)
            {
                Retry("Avatar prefab missing or does not contain NetworkObject.");
                return;
            }

            try
            {
                localSpawnedAvatar = runner.Spawn(
                    prefabToSpawn,
                    anchor.position,
                    anchor.rotation,
                    runner.LocalPlayer);

                if (localSpawnedAvatar == null)
                {
                    Retry("Runner.Spawn returned null.");
                    return;
                }

                // Shared Mode: associate exactly one player object with this player
                runner.SetPlayerObject(runner.LocalPlayer, localSpawnedAvatar);

                SharedAvatarRuntimeUtility.TryAssignAnchor(localSpawnedAvatar.gameObject, anchor);

                spawnPending = false;
                Log($"Spawned Fusion avatar '{prefabToSpawn.name}' and set PlayerObject.");
            }
            catch (System.Exception ex)
            {
                Retry($"Spawn failed: {ex.Message}");
            }
        }

        private NetworkObject ResolveAvatarPrefab()
        {
            string preferredPath = SharedAvatarRuntimeUtility.ResolveAvatarPrefabResourcesPath(avatarPrefabResourcesPath);

            GameObject loaded = Resources.Load<GameObject>(preferredPath);
            if (loaded != null)
            {
                avatarPrefab = loaded.GetComponent<NetworkObject>();
                if (IsFusionAvatarPrefabValid(avatarPrefab))
                    return avatarPrefab;
            }

            if (IsFusionAvatarPrefabValid(avatarPrefab))
                return avatarPrefab;

            GameObject fallback = Resources.Load<GameObject>(avatarPrefabResourcesPath);
            avatarPrefab = fallback != null ? fallback.GetComponent<NetworkObject>() : null;
            return IsFusionAvatarPrefabValid(avatarPrefab) ? avatarPrefab : null;
        }

        private bool IsFusionAvatarPrefabValid(NetworkObject candidate)
        {
            if (candidate == null)
                return false;

            return candidate.GetComponent<UserPresenceTransformSyncFusion>() != null &&
                   SharedAvatarRuntimeUtility.HasCompatibleFollowAnchor(candidate.gameObject);
        }

        private Transform ResolveAnchor()
        {
            if (avatarAnchor != null)
                return avatarAnchor;

            GameObject found = GameObject.Find(avatarAnchorPath);
            if (found != null)
            {
                avatarAnchor = found.transform;
                return avatarAnchor;
            }

            return null;
        }

        private void Retry(string reason)
        {
            nextRetryTime = Time.time + retryDelaySeconds;
            spawnPending = true;
            Log($"Retrying avatar spawn later. Reason: {reason}");
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[NetworkAvatarSpawnerFusion] {message}", this);
        }
    }
}
