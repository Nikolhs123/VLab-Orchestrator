using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEngine;

namespace Thesis.Orchestration.Backends.FishNet
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    public class SharedTransformSyncFishNet : MonoBehaviour
    {
    }
}
