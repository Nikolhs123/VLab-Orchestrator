using System;

namespace Thesis.Orchestration.Telemetry
{
    [Serializable]
    public class TelemetryEventData
    {
        public string EventType;
        public string UserId;
        public string InstrumentId;
        public string Details;
        public DateTime TimestampUtc;

        public TelemetryEventData(string eventType, string userId, string instrumentId, string details)
        {
            EventType = eventType;
            UserId = userId;
            InstrumentId = instrumentId;
            Details = details;
            TimestampUtc = DateTime.UtcNow;
        }

        public override string ToString()
        {
            string user = string.IsNullOrWhiteSpace(UserId) ? "-" : UserId;
            string instrument = string.IsNullOrWhiteSpace(InstrumentId) ? "-" : InstrumentId;
            string details = string.IsNullOrWhiteSpace(Details) ? "-" : Details;

            return $"[{TimestampUtc:HH:mm:ss}] {EventType} | {user} | {instrument} | {details}";
        }
    }
}