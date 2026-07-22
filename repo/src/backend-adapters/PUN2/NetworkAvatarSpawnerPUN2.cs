using Photon.Pun;
using Thesis.Orchestration.SharedSync;
using UnityEngine;

namespace Thesis.Orchestration.Backends.PUN2
{
    public class NetworkAvatarSpawnerPUN2 : MonoBehaviourPunCallbacks
    {
        public static NetworkAvatarSpawnerPUN2 Instance { get; private set; }

        [Header("References")]
        [SerializeField] private SessionOrchestratorPUN2 sessionOrchestrator;

        [Header("Prefab")]
        [Tooltip("Prefab name inside a Resources folder, used by PhotonNetwork.Instantiate.")]
        [SerializeField] private string avatarPrefabName = SharedAvatarRuntimeUtility.DefaultAvatarPrefabResourcesPath;

        [Header("Anchor")]
        [SerializeField] private Transform avatarAnchor;
        [SerializeField] private string avatarAnchorPath = "Ego/Camera/AvatarAnchor";

        [Header("Spawn")]
        [SerializeField] private bool spawnAutomaticallyOnJoinedRoom = true;
        [SerializeField] private bool spawnOnlyOnce = true;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private GameObject localSpawnedAvatar;

        public GameObject LocalSpawnedAvatar => localSpawnedAvatar;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[NetworkAvatarSpawnerPUN2] Duplicate instance found. Destroying duplicate.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (sessionOrchestrator == null)
                sessionOrchestrator = SessionOrchestratorPUN2.Instance;
        }

        private void Start()
        {
            if (PhotonNetwork.InRoom && spawnAutomaticallyOnJoinedRoom)
            {
                TrySpawnAvatar();
            }
        }

        private void OnDestroy()
        {
            localSpawnedAvatar = null;

            if (Instance == this)
                Instance = null;
        }

        public override void OnJoinedRoom()
        {
            base.OnJoinedRoom();

            if (spawnAutomaticallyOnJoinedRoom)
            {
                TrySpawnAvatar();
            }
        }

        public override void OnLeftRoom()
        {
            base.OnLeftRoom();
            localSpawnedAvatar = null;
        }

        [ContextMenu("Try Spawn Avatar")]
        public void TrySpawnAvatar()
        {
            if (!PhotonNetwork.InRoom)
            {
                Log("Spawn skipped: not in room.");
                return;
            }

            if (spawnOnlyOnce && localSpawnedAvatar != null)
            {
                Log("Spawn skipped: local avatar already exists.");
                return;
            }

            Transform anchor = ResolveAnchor();
            if (anchor == null)
            {
                Log($"Spawn failed: could not find avatar anchor. Path='{avatarAnchorPath}'");
                return;
            }

            localSpawnedAvatar = PhotonNetwork.Instantiate(
                SharedAvatarRuntimeUtility.ResolveAvatarPrefabResourcesPath(avatarPrefabName),
                anchor.position,
                anchor.rotation);

            if (localSpawnedAvatar == null)
            {
                Log("Spawn failed: PhotonNetwork.Instantiate returned null.");
                return;
            }

            if (SharedAvatarRuntimeUtility.TryAssignAnchor(localSpawnedAvatar, anchor))
            {
                Log($"Assigned anchor '{anchor.name}' to the shared avatar follow component.");
            }
            else
            {
                Log("Spawned avatar has no compatible follow-anchor component.");
            }

            Log($"Spawned local avatar prefab '{SharedAvatarRuntimeUtility.ResolveAvatarPrefabResourcesPath(avatarPrefabName)}' at anchor '{anchor.name}'.");
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

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[NetworkAvatarSpawnerPUN2] {message}", this);
        }
    }
}
