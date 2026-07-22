using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Thesis.Orchestration.UserSync
{
    /// <summary>
    /// Central registry of all UserSyncObjectDefinition components in the scene.
    ///
    /// Responsibilities:
    /// - discover and register user-owned synchronized objects
    /// - provide lookup by object ID
    /// - provide lookup by owner user ID
    /// - provide lookup by user sync object type
    /// - provide lookup by GameObject membership
    ///
    /// This script does NOT perform synchronization itself.
    /// It only acts as a scene-level registry for user presence objects.
    /// </summary>
    public class UserPresenceSyncRegistry : MonoBehaviour
    {
        [Header("Discovery")]
        [SerializeField] private bool autoDiscoverOnAwake = true;
        [SerializeField] private bool includeInactiveObjects = true;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private readonly Dictionary<string, UserSyncObjectDefinition> objectsById = new();
        private readonly List<UserSyncObjectDefinition> registeredObjects = new();

        /// <summary>
        /// Read-only list of all registered user-owned sync objects.
        /// </summary>
        public IReadOnlyList<UserSyncObjectDefinition> RegisteredObjects => registeredObjects;

        public static UserPresenceSyncRegistry Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[UserPresenceSyncRegistry] Duplicate instance found. Destroying duplicate.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (autoDiscoverOnAwake)
            {
                DiscoverSceneObjects();
            }
        }

        /// <summary>
        /// Clears existing registrations and re-discovers all user-owned objects in the scene.
        /// </summary>
        [ContextMenu("Discover Scene Objects")]
        public void DiscoverSceneObjects()
        {
            ClearRegistry();

#if UNITY_2023_1_OR_NEWER
            var foundObjects = FindObjectsByType<UserSyncObjectDefinition>(
                includeInactiveObjects ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
            var foundObjects = FindObjectsOfType<UserSyncObjectDefinition>(includeInactiveObjects);
#endif

            foreach (var definition in foundObjects)
            {
                Register(definition);
            }

            Log($"Discovery completed. Registered {registeredObjects.Count} user-owned object(s).");
        }

        /// <summary>
        /// Registers one user-owned object definition manually.
        /// Returns false if registration fails.
        /// </summary>
        public bool Register(UserSyncObjectDefinition definition)
        {
            if (definition == null)
            {
                Log("Register failed: definition is null.");
                return false;
            }

            if (!definition.IsUserOwnedObject)
            {
                Log($"Skipping '{definition.name}' because IsUserOwnedObject is false.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.ObjectId))
            {
                Log($"Register failed: object '{definition.name}' has empty ObjectId.");
                return false;
            }

            if (objectsById.ContainsKey(definition.ObjectId))
            {
                Log($"Register failed: duplicate ObjectId '{definition.ObjectId}'.");
                return false;
            }

            objectsById.Add(definition.ObjectId, definition);
            registeredObjects.Add(definition);

            Log($"Registered user-owned object '{definition.DisplayName}' with ID '{definition.ObjectId}', owner '{definition.OwnerUserId}'.");
            return true;
        }

        /// <summary>
        /// Removes one user-owned object from the registry.
        /// </summary>
        public bool Unregister(UserSyncObjectDefinition definition)
        {
            if (definition == null)
                return false;

            bool removedFromList = registeredObjects.Remove(definition);
            bool removedFromDictionary = objectsById.Remove(definition.ObjectId);

            if (removedFromList || removedFromDictionary)
            {
                Log($"Unregistered user-owned object '{definition.DisplayName}' with ID '{definition.ObjectId}'.");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns a user-owned object definition by its unique object ID.
        /// </summary>
        public UserSyncObjectDefinition GetById(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return null;

            objectsById.TryGetValue(objectId, out var definition);
            return definition;
        }

        /// <summary>
        /// Returns all user-owned object definitions belonging to a given user ID.
        /// </summary>
        public List<UserSyncObjectDefinition> GetByOwnerUserId(string ownerUserId)
        {
            if (string.IsNullOrWhiteSpace(ownerUserId))
                return new List<UserSyncObjectDefinition>();

            return registeredObjects
                .Where(obj => obj.OwnerUserId == ownerUserId)
                .ToList();
        }

        /// <summary>
        /// Returns all registered user-owned objects of a given type.
        /// </summary>
        public List<UserSyncObjectDefinition> GetByType(UserSyncObjectType objectType)
        {
            return registeredObjects
                .Where(obj => obj.ObjectType == objectType)
                .ToList();
        }

        /// <summary>
        /// Returns the user-owned object definition that owns the target GameObject,
        /// either as root object or one of its controlled child objects.
        /// </summary>
        public UserSyncObjectDefinition GetDefinitionForGameObject(GameObject target)
        {
            if (target == null)
                return null;

            foreach (var definition in registeredObjects)
            {
                if (definition.ContainsObject(target))
                    return definition;
            }

            return null;
        }

        /// <summary>
        /// Returns true if a user-owned object with the given ID exists.
        /// </summary>
        public bool ContainsId(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return false;

            return objectsById.ContainsKey(objectId);
        }

        /// <summary>
        /// Returns true if the given GameObject belongs to any registered user-owned sync object.
        /// </summary>
        public bool ContainsGameObject(GameObject target)
        {
            return GetDefinitionForGameObject(target) != null;
        }

        /// <summary>
        /// Clears all registry contents.
        /// </summary>
        public void ClearRegistry()
        {
            objectsById.Clear();
            registeredObjects.Clear();
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[UserPresenceSyncRegistry] {message}", this);
        }
    }
}
