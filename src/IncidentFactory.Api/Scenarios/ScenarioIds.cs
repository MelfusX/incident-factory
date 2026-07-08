namespace IncidentFactory.Api.Scenarios;

public static class ScenarioIds
{
    public const string LatencySpike = "latency-spike";
    public const string UnhandledException = "unhandled-exception";
    public const string NullReference = "null-reference";
    public const string SerializationError = "serialization-error";
    public const string RetryStorm = "retry-storm";
    public const string BrokenDownstreamCall = "broken-downstream-call";
    public const string SimulatedDbTimeout = "simulated-db-timeout";
    public const string BadConfigMisleadingSymptom = "bad-config-misleading-symptom";
}
