using System.Text.Json;
using IncidentFactory.Api.IncidentCompass;

namespace IncidentFactory.Api.Scenarios;

public sealed class ScenarioState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ScenarioDefinition> _definitions;
    private readonly Dictionary<string, ScenarioRuntime> _runtime;

    public ScenarioState(ScenarioCatalog catalog)
    {
        _definitions = catalog.Scenarios.ToDictionary(scenario => scenario.Id, StringComparer.OrdinalIgnoreCase);
        _runtime = catalog.Scenarios.ToDictionary(
            scenario => scenario.Id,
            scenario => new ScenarioRuntime(CreateDefaultParameters(scenario)),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ScenarioView> GetAllViews()
    {
        lock (_gate)
        {
            return _definitions.Values.Select(CreateViewLocked).ToArray();
        }
    }

    public bool TryGetView(string scenarioId, out ScenarioView? view)
    {
        lock (_gate)
        {
            if (!_definitions.ContainsKey(scenarioId))
            {
                view = null;
                return false;
            }

            view = CreateViewLocked(_definitions[scenarioId]);
            return true;
        }
    }

    public bool IsEnabled(string scenarioId)
    {
        lock (_gate)
        {
            return _runtime.TryGetValue(scenarioId, out var runtime) && runtime.Enabled;
        }
    }

    public bool Enable(string scenarioId, IReadOnlyDictionary<string, JsonElement>? parameters, out ScenarioView? view)
    {
        lock (_gate)
        {
            if (!TryGetDefinitionLocked(scenarioId, out var definition, out var runtime))
            {
                view = null;
                return false;
            }

            ApplyParametersLocked(definition, runtime, parameters);
            runtime.Enabled = true;
            view = CreateViewLocked(definition);
            return true;
        }
    }

    public bool Disable(string scenarioId, out ScenarioView? view)
    {
        CancellationTokenSource? cancellation = null;

        lock (_gate)
        {
            if (!TryGetDefinitionLocked(scenarioId, out var definition, out var runtime))
            {
                view = null;
                return false;
            }

            runtime.Enabled = false;
            runtime.Schedule = null;
            cancellation = runtime.ActiveRunCancellation;
            view = CreateViewLocked(definition);
        }

        cancellation?.Cancel();
        return true;
    }

    public IReadOnlyList<ScenarioView> ResetAll()
    {
        List<CancellationTokenSource> cancellations = [];
        ScenarioView[] views;

        lock (_gate)
        {
            foreach (var runtime in _runtime.Values)
            {
                runtime.Enabled = false;
                runtime.Schedule = null;
                if (runtime.ActiveRunCancellation is not null)
                {
                    cancellations.Add(runtime.ActiveRunCancellation);
                }
            }

            views = _definitions.Values.Select(CreateViewLocked).ToArray();
        }

        foreach (var cancellation in cancellations)
        {
            cancellation.Cancel();
        }

        return views;
    }

    public bool Schedule(
        string scenarioId,
        ScheduleScenarioRequest request,
        out ScenarioView? view,
        out string? validationError)
    {
        validationError = null;

        if (request.IntervalSeconds < 1 || request.IntervalSeconds > 3600)
        {
            view = null;
            validationError = "intervalSeconds must be between 1 and 3600.";
            return false;
        }

        if (request.StartInSeconds < 0 || request.StartInSeconds > 3600)
        {
            view = null;
            validationError = "startInSeconds must be between 0 and 3600.";
            return false;
        }

        lock (_gate)
        {
            if (!TryGetDefinitionLocked(scenarioId, out var definition, out var runtime))
            {
                view = null;
                return false;
            }

            ApplyParametersLocked(definition, runtime, request.Parameters);
            runtime.Enabled = true;
            runtime.Schedule = new ScenarioSchedule(
                request.IntervalSeconds,
                DateTimeOffset.UtcNow.AddSeconds(request.StartInSeconds));
            view = CreateViewLocked(definition);
            return true;
        }
    }

    public bool CancelSchedule(string scenarioId, out ScenarioView? view)
    {
        CancellationTokenSource? cancellation = null;

        lock (_gate)
        {
            if (!TryGetDefinitionLocked(scenarioId, out var definition, out var runtime))
            {
                view = null;
                return false;
            }

            runtime.Schedule = null;
            if (runtime.ActiveRunSource == ScenarioRunSource.Schedule)
            {
                cancellation = runtime.ActiveRunCancellation;
            }

            view = CreateViewLocked(definition);
        }

        cancellation?.Cancel();
        return true;
    }

    public IReadOnlyList<string> TakeDueScheduledRuns(DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            List<string> due = [];

            foreach (var (scenarioId, runtime) in _runtime)
            {
                if (runtime.Schedule is not { } schedule || !runtime.Enabled || nowUtc < schedule.NextRunAtUtc)
                {
                    continue;
                }

                schedule.NextRunAtUtc = nowUtc.AddSeconds(schedule.IntervalSeconds);

                if (!runtime.Running)
                {
                    due.Add(scenarioId);
                }
            }

            return due;
        }
    }

    public bool TryBeginRun(
        string scenarioId,
        ScenarioRunSource source,
        CancellationToken cancellationToken,
        out ActiveScenarioRun? activeRun)
    {
        lock (_gate)
        {
            if (!TryGetDefinitionLocked(scenarioId, out var definition, out var runtime) || runtime.Running)
            {
                activeRun = null;
                return false;
            }

            var startedAtUtc = DateTimeOffset.UtcNow;
            var runId = $"{scenarioId}-{startedAtUtc:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            runtime.Running = true;
            runtime.ActiveRunId = runId;
            runtime.ActiveRunSource = source;
            runtime.ActiveRunStartedAtUtc = startedAtUtc;
            runtime.ActiveRunCancellation = linkedCancellation;

            activeRun = new ActiveScenarioRun(
                scenarioId,
                runId,
                source,
                definition.BusinessPath,
                definition.OperationName,
                definition.Signal,
                CloneParameters(runtime.Parameters),
                startedAtUtc,
                linkedCancellation.Token);
            return true;
        }
    }

    public void CompleteRun(string scenarioId, string runId, ScenarioRunView run, IncidentDeliveryResult? delivery)
    {
        CancellationTokenSource? cancellation = null;

        lock (_gate)
        {
            if (!_runtime.TryGetValue(scenarioId, out var runtime) || runtime.ActiveRunId != runId)
            {
                return;
            }

            cancellation = runtime.ActiveRunCancellation;
            runtime.Running = false;
            runtime.ActiveRunId = null;
            runtime.ActiveRunSource = null;
            runtime.ActiveRunStartedAtUtc = null;
            runtime.ActiveRunCancellation = null;
            runtime.LastRun = run;
            runtime.LastDelivery = delivery;
        }

        cancellation?.Dispose();
    }

    private static Dictionary<string, object> CreateDefaultParameters(ScenarioDefinition definition)
    {
        return definition.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.DefaultValue);
    }

    private static Dictionary<string, object> CloneParameters(IReadOnlyDictionary<string, object> source)
    {
        return source.ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    private bool TryGetDefinitionLocked(
        string scenarioId,
        out ScenarioDefinition definition,
        out ScenarioRuntime runtime)
    {
        if (_definitions.TryGetValue(scenarioId, out definition!) && _runtime.TryGetValue(scenarioId, out runtime!))
        {
            return true;
        }

        definition = null!;
        runtime = null!;
        return false;
    }

    private void ApplyParametersLocked(
        ScenarioDefinition definition,
        ScenarioRuntime runtime,
        IReadOnlyDictionary<string, JsonElement>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return;
        }

        foreach (var parameter in definition.Parameters)
        {
            if (!parameters.TryGetValue(parameter.Name, out var value))
            {
                continue;
            }

            runtime.Parameters[parameter.Name] = CoerceParameter(parameter, value);
        }
    }

    private static object CoerceParameter(ScenarioParameterDefinition definition, JsonElement value)
    {
        if (definition.Kind == "number")
        {
            var number = value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var direct)
                ? direct
                : int.TryParse(value.ToString(), out var parsed)
                    ? parsed
                    : Convert.ToInt32(definition.DefaultValue);

            if (definition.Min.HasValue)
            {
                number = Math.Max(definition.Min.Value, number);
            }

            if (definition.Max.HasValue)
            {
                number = Math.Min(definition.Max.Value, number);
            }

            return number;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return string.IsNullOrWhiteSpace(text) ? definition.DefaultValue : text;
    }

    private ScenarioView CreateViewLocked(ScenarioDefinition definition)
    {
        var runtime = _runtime[definition.Id];
        return new ScenarioView(
            definition.Id,
            definition.Name,
            definition.Summary,
            definition.BusinessPath,
            definition.OperationName,
            runtime.Enabled,
            runtime.Running,
            runtime.ActiveRunSource,
            runtime.ActiveRunStartedAtUtc,
            CloneParameters(runtime.Parameters),
            definition.Parameters,
            runtime.Schedule is null
                ? null
                : new ScheduleView(true, runtime.Schedule.IntervalSeconds, runtime.Schedule.NextRunAtUtc),
            runtime.LastRun,
            runtime.LastDelivery);
    }

    private sealed class ScenarioRuntime(Dictionary<string, object> parameters)
    {
        public bool Enabled { get; set; }
        public bool Running { get; set; }
        public string? ActiveRunId { get; set; }
        public ScenarioRunSource? ActiveRunSource { get; set; }
        public DateTimeOffset? ActiveRunStartedAtUtc { get; set; }
        public CancellationTokenSource? ActiveRunCancellation { get; set; }
        public Dictionary<string, object> Parameters { get; } = parameters;
        public ScenarioSchedule? Schedule { get; set; }
        public ScenarioRunView? LastRun { get; set; }
        public IncidentDeliveryResult? LastDelivery { get; set; }
    }

    private sealed class ScenarioSchedule(int intervalSeconds, DateTimeOffset nextRunAtUtc)
    {
        public int IntervalSeconds { get; } = intervalSeconds;
        public DateTimeOffset NextRunAtUtc { get; set; } = nextRunAtUtc;
    }
}


