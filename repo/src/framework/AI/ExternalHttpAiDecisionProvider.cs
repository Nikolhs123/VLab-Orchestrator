using System;
using System.IO;
using System.Net;
using System.Text;
using Thesis.Orchestration.EditorTools;
using UnityEngine;

namespace Thesis.Orchestration.AI
{
    public class ExternalHttpAiDecisionProvider : IOrchestrationAiDecisionProvider
    {
        private readonly OrchestrationProjectConfig config;

        public ExternalHttpAiDecisionProvider(OrchestrationProjectConfig config)
        {
            this.config = config;
        }

        public string ProviderName => config != null ? config.aiProvider.ToString() : "ExternalHttpJson";
        public bool SupportsSynchronousDecisions => true;

        public OrchestrationAiDecision Evaluate(OrchestrationAiStateSnapshot snapshot)
        {
            if (snapshot == null)
                return OrchestrationAiDecision.Fallback(ProviderName, "Unknown", "Snapshot was null.");

            if (config == null || string.IsNullOrWhiteSpace(config.aiExternalEndpointUrl))
            {
                return OrchestrationAiDecision.Fallback(
                    ProviderName,
                    snapshot.mode,
                    "External AI provider selected but endpoint URL is empty.");
            }

            try
            {
                string payload = JsonUtility.ToJson(snapshot);
                byte[] body = Encoding.UTF8.GetBytes(payload);
                int timeoutMs = Mathf.Max(50, Mathf.CeilToInt(config.aiDecisionTimeoutMs));

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(config.aiExternalEndpointUrl);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Accept = "application/json";
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;

                if (!string.IsNullOrWhiteSpace(config.aiExternalAuthorizationHeader))
                    request.Headers[HttpRequestHeader.Authorization] = config.aiExternalAuthorizationHeader;

                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(body, 0, body.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    OrchestrationAiDecision decision = JsonUtility.FromJson<OrchestrationAiDecision>(json);
                    return NormalizeDecision(decision, snapshot, "External AI response could not be parsed.");
                }
            }
            catch (WebException ex)
            {
                return OrchestrationAiDecision.Fallback(
                    ProviderName,
                    snapshot.mode,
                    $"External AI request failed: {BuildWebExceptionMessage(ex)}");
            }
            catch (Exception ex)
            {
                return OrchestrationAiDecision.Fallback(
                    ProviderName,
                    snapshot.mode,
                    $"External AI request exception: {ex.Message}");
            }
        }

        private OrchestrationAiDecision NormalizeDecision(
            OrchestrationAiDecision decision,
            OrchestrationAiStateSnapshot snapshot,
            string fallbackMessage)
        {
            if (decision == null)
                return OrchestrationAiDecision.Fallback(ProviderName, snapshot.mode, fallbackMessage);

            if (string.IsNullOrWhiteSpace(decision.provider))
                decision.provider = ProviderName;

            if (string.IsNullOrWhiteSpace(decision.mode))
                decision.mode = snapshot.mode;

            if (string.IsNullOrWhiteSpace(decision.action))
                decision.action = "NoAction";

            return decision;
        }

        private string BuildWebExceptionMessage(WebException ex)
        {
            if (ex.Response == null)
                return ex.Message;

            try
            {
                using (Stream responseStream = ex.Response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    return string.IsNullOrWhiteSpace(body) ? ex.Message : body;
                }
            }
            catch
            {
                return ex.Message;
            }
        }
    }
}
