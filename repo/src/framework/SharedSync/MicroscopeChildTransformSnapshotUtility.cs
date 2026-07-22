using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Thesis.Orchestration.SharedSync
{
    public static class MicroscopeChildTransformSnapshotUtility
    {
        private const float PositionScale = 10000f;
        private const float RotationScale = 100f;

        public static void RefreshControlledChildTransforms(
            SharedSyncObjectDefinition sharedDefinition,
            List<Transform> destination)
        {
            if (destination == null)
                return;

            destination.Clear();

            if (sharedDefinition == null)
                return;

            sharedDefinition.RefreshControlledChildren();
            if (sharedDefinition.ControlledChildObjects == null)
                return;

            List<Transform> sortedChildren = new List<Transform>();
            foreach (GameObject childObject in sharedDefinition.ControlledChildObjects)
            {
                if (childObject == null)
                    continue;

                sortedChildren.Add(childObject.transform);
            }

            sortedChildren.Sort(CompareTransformsByStablePath);
            destination.AddRange(sortedChildren);
        }

        public static string BuildSnapshot(IReadOnlyList<Transform> controlledChildren)
        {
            if (controlledChildren == null || controlledChildren.Count == 0)
                return string.Empty;

            List<string> entries = new List<string>(controlledChildren.Count);
            foreach (Transform child in controlledChildren)
            {
                if (child == null)
                {
                    entries.Add(string.Empty);
                    continue;
                }

                Vector3 localPosition = child.localPosition;
                Vector3 localEulerAngles = child.localEulerAngles;

                entries.Add(string.Join(",",
                    Quantize(localPosition.x, PositionScale).ToString(CultureInfo.InvariantCulture),
                    Quantize(localPosition.y, PositionScale).ToString(CultureInfo.InvariantCulture),
                    Quantize(localPosition.z, PositionScale).ToString(CultureInfo.InvariantCulture),
                    Quantize(NormalizeAngle(localEulerAngles.x), RotationScale).ToString(CultureInfo.InvariantCulture),
                    Quantize(NormalizeAngle(localEulerAngles.y), RotationScale).ToString(CultureInfo.InvariantCulture),
                    Quantize(NormalizeAngle(localEulerAngles.z), RotationScale).ToString(CultureInfo.InvariantCulture)));
            }

            return string.Join(";", entries);
        }

        public static void ApplySnapshot(string snapshot, IReadOnlyList<Transform> controlledChildren)
        {
            if (string.IsNullOrWhiteSpace(snapshot) || controlledChildren == null || controlledChildren.Count == 0)
                return;

            string[] entries = snapshot.Split(';');
            if (entries.Length == 0)
                return;

            int count = Mathf.Min(entries.Length, controlledChildren.Count);
            for (int i = 0; i < count; i++)
            {
                Transform child = controlledChildren[i];
                if (child == null || string.IsNullOrWhiteSpace(entries[i]))
                    continue;

                ApplyEntryPayload(entries[i], child);
            }
        }

        private static void ApplyEntryPayload(string payload, Transform child)
        {
            if (child == null || string.IsNullOrWhiteSpace(payload))
                return;

            string[] values = payload.Split(',');
            if (values.Length != 6)
                return;

            child.localPosition = new Vector3(
                Dequantize(values[0], PositionScale),
                Dequantize(values[1], PositionScale),
                Dequantize(values[2], PositionScale));

            child.localRotation = Quaternion.Euler(
                Dequantize(values[3], RotationScale),
                Dequantize(values[4], RotationScale),
                Dequantize(values[5], RotationScale));
        }

        private static int CompareTransformsByStablePath(Transform left, Transform right)
        {
            string leftPath = GetStableTransformPath(left);
            string rightPath = GetStableTransformPath(right);
            return string.CompareOrdinal(leftPath, rightPath);
        }

        private static string GetStableTransformPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            Stack<string> segments = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", segments);
        }

        private static int Quantize(float value, float scale)
        {
            return Mathf.RoundToInt(value * scale);
        }

        private static float Dequantize(string rawValue, float scale)
        {
            if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return 0f;

            return parsed / scale;
        }

        private static float NormalizeAngle(float angle)
        {
            while (angle > 180f)
                angle -= 360f;

            while (angle < -180f)
                angle += 360f;

            return angle;
        }
    }
}
