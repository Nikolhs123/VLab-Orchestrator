using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Thesis.Orchestration.UserSync
{
    /// <summary>
    /// Defines one user-owned synchronized object in the scene.
    ///
    /// Typical examples:
    /// - avatar root
    /// - head / HMD
    /// - left hand
    /// - right hand
    /// - pointer / ray origin
    ///
    /// This component describes how the object participates in
    /// user-presence synchronization, independently of Photon, MAGES, or Fusion.
    /// </summary>
    public class UserSyncObjectDefinition : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string objectId = "user_object_01";
        [SerializeField] private string ownerUserId = "User_1";
        [SerializeField] private UserSyncObjectType objectType = UserSyncObjectType.AvatarRoot;
        [SerializeField] private string displayName = "User Sync Object";

        [Header("Synchronization Flags")]
        [SerializeField] private bool isUserOwnedObject = true;
        [SerializeField] private bool syncTransform = true;
        [SerializeField] private bool syncState = false;
        [SerializeField] private bool syncAnimation = false;

        [Header("Scene References")]
        [SerializeField] private GameObject rootObject;
        [SerializeField] private List<GameObject> controlledChildObjects = new List<GameObject>();

        [Header("Optional Events")]
        [SerializeField] private UnityEvent onOwnershipAssigned;
        [SerializeField] private UnityEvent onPresenceUpdated;
        [SerializeField] private UnityEvent onStateChanged;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        /// <summary>
        /// Unique ID of this user-owned synchronized object.
        /// </summary>
        public string ObjectId => objectId;

        /// <summary>
        /// User ID of the owner of this object.
        /// </summary>
        public string OwnerUserId => ownerUserId;

        /// <summary>
        /// Category/type of this user-owned object.
        /// </summary>
        public UserSyncObjectType ObjectType => objectType;

        /// <summary>
        /// Human-readable label for UI/debugging.
        /// </summary>
        public string DisplayName => displayName;

        /// <summary>
        /// Whether this object participates in user-presence synchronization.
        /// </summary>
        public bool IsUserOwnedObject => isUserOwnedObject;

        /// <summary>
        /// Whether transform synchronization should be applied.
        /// </summary>
        public bool SyncTransform => syncTransform;

        /// <summary>
        /// Whether custom state synchronization should be applied.
        /// </summary>
        public bool SyncState => syncState;

        /// <summary>
        /// Whether animation-related data should be synchronized.
        /// </summary>
        public bool SyncAnimation => syncAnimation;

        /// <summary>
        /// Root GameObject of this user-owned object.
        /// Defaults to this GameObject if not assigned.
        /// </summary>
        public GameObject RootObject => rootObject != null ? rootObject : gameObject;

        /// <summary>
        /// Optional child objects that are part of this user-owned sync definition.
        /// </summary>
        public IReadOnlyList<GameObject> ControlledChildObjects => controlledChildObjects;

        /// <summary>
        /// Whether this object currently belongs to the local user.
        /// This is a local runtime mirror used by future adapters.
        /// </summary>
        public bool IsLocallyOwned { get; private set; }

        private void Reset()
        {
            if (rootObject == null)
            {
                rootObject = gameObject;
            }
        }

        private void Awake()
        {
            if (rootObject == null)
            {
                rootObject = gameObject;
            }

            if (string.IsNullOrWhiteSpace(objectId))
            {
                objectId = gameObject.name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = gameObject.name;
            }
        }

        /// <summary>
        /// Sets the logical owner of this user-owned object.
        /// This does not perform network ownership transfer by itself.
        /// </summary>
        public void SetOwnerUserId(string newOwnerUserId)
        {
            if (string.IsNullOrWhiteSpace(newOwnerUserId))
                return;

            ownerUserId = newOwnerUserId;
            Log($"Owner user ID set to {newOwnerUserId}");
            onOwnershipAssigned?.Invoke();
        }

        /// <summary>
        /// Updates whether this object is considered locally owned on this client.
        /// Useful for future adapter logic.
        /// </summary>
        public void SetLocalOwnership(bool isLocalOwner)
        {
            IsLocallyOwned = isLocalOwner;
            Log($"Local ownership set to {isLocalOwner}");
            onOwnershipAssigned?.Invoke();
        }

        /// <summary>
        /// Notifies listeners that user-presence related data changed.
        /// </summary>
        public void NotifyPresenceUpdated()
        {
            Log("Presence updated.");
            onPresenceUpdated?.Invoke();
        }

        /// <summary>
        /// Notifies listeners that custom synchronized state changed.
        /// </summary>
        public void NotifyStateChanged()
        {
            Log("User-owned state changed.");
            onStateChanged?.Invoke();
        }

        /// <summary>
        /// Checks whether the given GameObject belongs to this user-owned object
        /// either as root or registered child.
        /// </summary>
        public bool ContainsObject(GameObject target)
        {
            if (target == null)
                return false;

            if (target == RootObject)
                return true;

            return controlledChildObjects.Contains(target);
        }

        /// <summary>
        /// Adds a child object to the controlled list if not already present.
        /// </summary>
        public void RegisterChildObject(GameObject child)
        {
            if (child == null)
                return;

            if (!controlledChildObjects.Contains(child))
            {
                controlledChildObjects.Add(child);
                Log($"Registered child object: {child.name}");
            }
        }

        /// <summary>
        /// Removes a child object from the controlled list.
        /// </summary>
        public void UnregisterChildObject(GameObject child)
        {
            if (child == null)
                return;

            if (controlledChildObjects.Remove(child))
            {
                Log($"Unregistered child object: {child.name}");
            }
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[UserSyncObjectDefinition:{objectId}] {message}", this);
        }
    }

    /// <summary>
    /// Classification of user-owned synchronized object types.
    /// </summary>
    public enum UserSyncObjectType
    {
        AvatarRoot = 0,
        Head = 1,
        LeftHand = 2,
        RightHand = 3,
        Pointer = 4,
        ToolAnchor = 5,
        Other = 6
    }
}
