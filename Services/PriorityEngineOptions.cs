namespace CRM.Api.Services
{
    /// Priority engine settings, editable from appsettings.json.
    public class PriorityEngineOptions
    {
        public const string SectionName = "PriorityEngine";

        /// Weight added to the final score per intent.
        public Dictionary<string, int> IntentWeights { get; set; } = new()
        {
            ["security_incident"] = 20,
            ["technical_issue"] = 10,
            ["billing_issue"] = 10,
            ["complaint"] = 5,
            ["cancellation_request"] = 5,
            ["refund_request"] = 5,
            ["account_management"] = 5,
            ["service_request"] = 0,
            ["order_inquiry"] = 0,
            ["information_request"] = 0,
            ["feature_request"] = 0,
            ["other"] = 0,
        };
    }
}
