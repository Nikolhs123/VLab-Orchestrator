using UnityEngine;

namespace Thesis.Orchestration.SharedSync
{
    public static class TransformSyncUtilities
    {
        public static Vector3 SmoothPosition(Vector3 current, Vector3 target, float speed)
        {
            return Vector3.Lerp(current, target, Time.deltaTime * speed);
        }

        public static Quaternion SmoothRotation(Quaternion current, Quaternion target, float speed)
        {
            return Quaternion.Slerp(current, target, Time.deltaTime * speed);
        }

        public static void ApplyRemoteTransform(
            Transform targetTransform,
            bool syncPosition,
            bool syncRotation,
            Vector3 targetPosition,
            Quaternion targetRotation,
            float positionLerpSpeed,
            float rotationLerpSpeed)
        {
            if (targetTransform == null)
                return;

            if (syncPosition)
            {
                targetTransform.position = SmoothPosition(
                    targetTransform.position,
                    targetPosition,
                    positionLerpSpeed);
            }

            if (syncRotation)
            {
                targetTransform.rotation = SmoothRotation(
                    targetTransform.rotation,
                    targetRotation,
                    rotationLerpSpeed);
            }
        }

        public static void SnapRemoteTransform(
            Transform targetTransform,
            bool syncPosition,
            bool syncRotation,
            Vector3 targetPosition,
            Quaternion targetRotation)
        {
            if (targetTransform == null)
                return;

            if (syncPosition)
                targetTransform.position = targetPosition;

            if (syncRotation)
                targetTransform.rotation = targetRotation;
        }
    }
}