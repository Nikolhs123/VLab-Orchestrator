using System;
using System.Collections.Generic;

namespace Thesis.Orchestration.Telemetry
{
    // Adapter-side state for one logical authority measurement.
    //
    // A measurement is bound at request time to its measurement ID, tracked object ID and the
    // logical holder ID; terminal events reuse those captured values, so a later identity change
    // (for example after disconnect) cannot redirect them. The measurement covers every native
    // object the adapter requested and completes only when each one has resolved: success when all
    // became locally authoritative, failure when any reported failure. Native observations that
    // arrive after the measurement ended are journaled as late and never close a newer measurement.
    //
    // SDK ownership callbacks carry no request token, so an observation for an object of the
    // current measurement means "local authority observed after this request", not proof that
    // this specific request caused it.
    public sealed class AuthorityMeasurementTracker
    {
        private enum NativeObjectState { Requested, Local, Failed }

        private readonly Func<AuthorityTransferRecorder> recorderProvider;
        private readonly Dictionary<string, NativeObjectState> objects = new();
        private readonly HashSet<string> lastTerminalObjects = new();
        private double startedAtSeconds;

        public AuthorityMeasurementTracker(Func<AuthorityTransferRecorder> recorderProvider)
        {
            this.recorderProvider = recorderProvider ?? (() => null);
        }

        public bool IsPending { get; private set; }
        public string MeasurementId { get; private set; } = string.Empty;
        public string ObjectId { get; private set; } = string.Empty;
        public string HolderId { get; private set; } = string.Empty;
        public string LastTerminalMeasurementId { get; private set; } = string.Empty;
        public int ObjectCount => objects.Count;

        public bool Begin(string objectId, string holderId, string ownerBefore, IEnumerable<string> nativeObjectKeys,
            double nowSeconds, string details)
        {
            if (IsPending)
                return false;

            objects.Clear();
            if (nativeObjectKeys != null)
            {
                foreach (string key in nativeObjectKeys)
                {
                    if (!string.IsNullOrEmpty(key))
                        objects[key] = NativeObjectState.Requested;
                }
            }

            if (objects.Count == 0)
                return false;

            ObjectId = objectId ?? string.Empty;
            HolderId = holderId ?? string.Empty;
            startedAtSeconds = nowSeconds;
            IsPending = true;
            MeasurementId = recorderProvider()?.RecordTransferRequested(
                ObjectId, HolderId, ownerBefore, $"{details} nativeObjects={objects.Count}") ?? string.Empty;
            return true;
        }

        public bool Tracks(string nativeObjectKey)
        {
            return IsPending && nativeObjectKey != null && objects.ContainsKey(nativeObjectKey);
        }

        // Returns true when the observation belongs to the pending measurement.
        public bool ObserveLocal(string nativeObjectKey, string details)
        {
            return Observe(nativeObjectKey, NativeObjectState.Local, details);
        }

        public bool ObserveFailed(string nativeObjectKey, string details)
        {
            return Observe(nativeObjectKey, NativeObjectState.Failed, details);
        }

        // Completes the measurement when every requested native object has resolved.
        public bool TryResolve(string ownerAfter, string details, out bool success, out string completedMeasurementId)
        {
            success = false;
            completedMeasurementId = string.Empty;
            if (!IsPending)
                return false;

            int local = 0, failed = 0;
            foreach (NativeObjectState state in objects.Values)
            {
                if (state == NativeObjectState.Local) local++;
                else if (state == NativeObjectState.Failed) failed++;
            }

            if (local + failed < objects.Count)
                return false;

            success = failed == 0;
            completedMeasurementId = MeasurementId;
            recorderProvider()?.CompleteMeasurement(MeasurementId, ObjectId, HolderId, ownerAfter, success,
                $"{details} nativeObjects={objects.Count} local={local} failed={failed}");
            End();
            return true;
        }

        public bool CheckTimeout(double nowSeconds, double timeoutSeconds, string details)
        {
            if (!IsPending || timeoutSeconds <= 0d || nowSeconds - startedAtSeconds < timeoutSeconds)
                return false;

            recorderProvider()?.TimeoutMeasurement(MeasurementId, ObjectId, HolderId,
                $"{details} timeoutSeconds={timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} {DescribeObjects()}");
            End();
            return true;
        }

        public bool Cancel(string details)
        {
            if (!IsPending)
                return false;

            recorderProvider()?.CancelMeasurement(MeasurementId, ObjectId, HolderId, $"{details} {DescribeObjects()}");
            End();
            return true;
        }

        private bool Observe(string nativeObjectKey, NativeObjectState state, string details)
        {
            if (Tracks(nativeObjectKey))
            {
                if (objects[nativeObjectKey] == NativeObjectState.Requested)
                    objects[nativeObjectKey] = state;
                return true;
            }

            if (!IsPending && nativeObjectKey != null && lastTerminalObjects.Contains(nativeObjectKey))
            {
                recorderProvider()?.RecordLateObservation(LastTerminalMeasurementId, ObjectId, HolderId,
                    $"{state} for {nativeObjectKey} after measurement end. {details}");
            }

            return false;
        }

        private string DescribeObjects()
        {
            int local = 0, failed = 0;
            foreach (NativeObjectState state in objects.Values)
            {
                if (state == NativeObjectState.Local) local++;
                else if (state == NativeObjectState.Failed) failed++;
            }

            return $"nativeObjects={objects.Count} local={local} failed={failed}";
        }

        private void End()
        {
            LastTerminalMeasurementId = MeasurementId;
            lastTerminalObjects.Clear();
            foreach (string key in objects.Keys)
                lastTerminalObjects.Add(key);
            objects.Clear();
            IsPending = false;
            MeasurementId = string.Empty;
        }
    }
}
