import {
  Activity,
  AlertTriangle,
  CalendarClock,
  PauseCircle,
  Play,
  RefreshCw,
  RotateCcw,
  Server,
  Wifi,
  X
} from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';

type ScenarioRunSource = 'manual' | 'schedule';
type ScenarioRunStatus = 'faulted' | 'canceled' | 'skipped';

type ParameterDefinition = {
  name: string;
  label: string;
  kind: 'number' | 'text' | 'textarea';
  unit?: string;
  min?: number;
  max?: number;
  defaultValue: string | number;
};

type ScheduleView = {
  enabled: boolean;
  intervalSeconds: number;
  nextRunAtUtc: string;
};

type RunView = {
  runId: string;
  scenarioId: string;
  source: ScenarioRunSource;
  status: ScenarioRunStatus;
  startedAtUtc: string;
  completedAtUtc: string;
  durationMs: number;
  errorType?: string;
  errorMessage?: string;
  httpStatusCode?: number;
};

type DeliveryView = {
  attempted: boolean;
  success: boolean;
  url: string;
  statusCode?: number;
  signalId?: string;
  faultId?: string;
  jobId?: string;
  error?: string;
  responsePreview?: string;
  deliveredAtUtc: string;
};

type Scenario = {
  id: string;
  name: string;
  summary: string;
  businessPath: string;
  operationName: string;
  enabled: boolean;
  running: boolean;
  activeRunSource?: ScenarioRunSource;
  activeRunStartedAtUtc?: string;
  parameters: Record<string, string | number>;
  parameterDefinitions: ParameterDefinition[];
  schedule?: ScheduleView;
  lastRun?: RunView;
  lastDelivery?: DeliveryView;
};

type HardScenario = {
  id: string;
  name: string;
  summary: string;
  businessPath: string;
  operationName: string;
  tier: string;
};

type RuntimeView = {
  serviceName: string;
  environment: string;
  incidentCompassBaseUrl: string;
  sourceKind: string;
  hardScenarios?: HardScenario[];
};

type ActionResult = {
  status: number;
  ok: boolean;
  body: unknown;
  observedAt: string;
};

type Drafts = Record<string, Record<string, string>>;

type ScheduleDrafts = Record<string, { intervalSeconds: string; startInSeconds: string }>;

const refreshMs = 2500;

export default function App() {
  const [scenarios, setScenarios] = useState<Scenario[]>([]);
  const [runtime, setRuntime] = useState<RuntimeView | null>(null);
  const [hardScenarios, setHardScenarios] = useState<HardScenario[]>([]);
  const [selectedId, setSelectedId] = useState<string>('');
  const [drafts, setDrafts] = useState<Drafts>({});
  const [scheduleDrafts, setScheduleDrafts] = useState<ScheduleDrafts>({});
  const [busyActions, setBusyActions] = useState<string[]>([]);
  const [apiError, setApiError] = useState<string | null>(null);
  const [lastAction, setLastAction] = useState<ActionResult | null>(null);

  const selected = useMemo(
    () => scenarios.find((scenario) => scenario.id === selectedId) ?? scenarios[0],
    [scenarios, selectedId]
  );

  const load = useCallback(async () => {
    try {
      const [scenarioList, runtimeView] = await Promise.all([
        api<Scenario[]>('/api/scenarios'),
        api<RuntimeView>('/api/runtime')
      ]);

      setScenarios(scenarioList);
      setRuntime(runtimeView);
      setHardScenarios(runtimeView.hardScenarios ?? []);
      setApiError(null);

      setSelectedId((current) => current || scenarioList[0]?.id || '');
      setDrafts((current) => mergeParameterDrafts(current, scenarioList));
      setScheduleDrafts((current) => mergeScheduleDrafts(current, scenarioList));
    } catch (error) {
      setApiError(error instanceof Error ? error.message : String(error));
    }
  }, []);

  useEffect(() => {
    void load();
    const handle = window.setInterval(() => void load(), refreshMs);
    return () => window.clearInterval(handle);
  }, [load]);

  const selectedDraft = selected ? drafts[selected.id] ?? {} : {};
  const selectedScheduleDraft = selected
    ? scheduleDrafts[selected.id] ?? { intervalSeconds: '30', startInSeconds: '0' }
    : { intervalSeconds: '30', startInSeconds: '0' };

  async function triggerScenario(scenario: Scenario) {
    await runBusy(`trigger:${scenario.id}`, async () => {
      const parameters = collectParameters(scenario, drafts[scenario.id]);
      await api(`/api/scenarios/${scenario.id}/trigger`, {
        method: 'POST',
        body: JSON.stringify({ parameters })
      });

      const result = await callBusinessPath(scenario.businessPath);
      setLastAction(result);
      await load();
    });
  }

  async function triggerHardScenario(scenario: HardScenario) {
    await runBusy(`hard:${scenario.id}`, async () => {
      const result = await callBusinessPath(scenario.businessPath);
      setLastAction(result);
      await load();
    });
  }

  async function disableScenario(scenario: Scenario) {
    await runBusy(`disable:${scenario.id}`, async () => {
      await api(`/api/scenarios/${scenario.id}/disable`, { method: 'POST' });
      await load();
    });
  }

  async function resetAll() {
    await runBusy('reset-all', async () => {
      await api('/api/scenarios/reset-all', { method: 'POST' });
      setLastAction(null);
      await load();
    });
  }

  async function scheduleScenario(scenario: Scenario) {
    await runBusy(`schedule:${scenario.id}`, async () => {
      const draft = scheduleDrafts[scenario.id] ?? { intervalSeconds: '30', startInSeconds: '0' };
      await api(`/api/scenarios/${scenario.id}/schedule`, {
        method: 'POST',
        body: JSON.stringify({
          intervalSeconds: toNumber(draft.intervalSeconds, 30),
          startInSeconds: toNumber(draft.startInSeconds, 0),
          parameters: collectParameters(scenario, drafts[scenario.id])
        })
      });
      await load();
    });
  }

  async function cancelSchedule(scenario: Scenario) {
    await runBusy(`cancel-schedule:${scenario.id}`, async () => {
      await api(`/api/scenarios/${scenario.id}/schedule`, { method: 'DELETE' });
      await load();
    });
  }

  async function runBusy(key: string, action: () => Promise<void>) {
    setBusyActions((current) => current.includes(key) ? current : [...current, key]);
    setApiError(null);
    try {
      await action();
    } catch (error) {
      setApiError(error instanceof Error ? error.message : String(error));
    } finally {
      setBusyActions((current) => current.filter((item) => item !== key));
    }
  }

  function isBusy(key: string) {
    return busyActions.includes(key);
  }

  function updateParameter(scenarioId: string, name: string, value: string) {
    setDrafts((current) => ({
      ...current,
      [scenarioId]: {
        ...(current[scenarioId] ?? {}),
        [name]: value
      }
    }));
  }

  function updateSchedule(scenarioId: string, name: 'intervalSeconds' | 'startInSeconds', value: string) {
    setScheduleDrafts((current) => ({
      ...current,
      [scenarioId]: {
        ...(current[scenarioId] ?? { intervalSeconds: '30', startInSeconds: '0' }),
        [name]: value
      }
    }));
  }

  return (
    <main className="shell">
      <header className="topbar">
        <div>
          <h1>incident-factory</h1>
          <div className="subline">{runtime?.serviceName ?? 'incident-factory'} / {runtime?.environment ?? 'demo'}</div>
        </div>
        <div className="status-strip" aria-label="runtime status">
          <StatusToken tone={apiError ? 'danger' : 'ok'} icon={<Wifi size={15} />} label={apiError ? 'API offline' : 'API online'} />
          <StatusToken tone="neutral" icon={<Server size={15} />} label={runtime?.incidentCompassBaseUrl ?? 'IC pending'} />
          <StatusToken tone="neutral" icon={<Activity size={15} />} label={runtime?.sourceKind ?? 'otel'} />
          <button className="icon-button" onClick={() => void load()} title="Refresh">
            <RefreshCw size={17} />
          </button>
        </div>
      </header>

      {apiError && (
        <div className="banner danger">
          <AlertTriangle size={16} />
          <span>{apiError}</span>
        </div>
      )}

      <section className="workspace">
        <aside className="scenario-list" aria-label="scenarios">
          <div className="scenario-section-label">Tier 1 signal scenarios</div>
          {scenarios.map((scenario) => (
            <button
              className={`scenario-row ${selected?.id === scenario.id ? 'selected' : ''}`}
              key={scenario.id}
              onClick={() => setSelectedId(scenario.id)}
            >
              <span className={`dot ${scenario.running ? 'running' : scenario.enabled ? 'enabled' : ''}`} />
              <span>
                <strong>{scenario.name}</strong>
                <small>{scenario.operationName}</small>
              </span>
              {scenario.schedule && <CalendarClock size={15} />}
            </button>
          ))}

          {hardScenarios.length > 0 && (
            <>
              <div className="scenario-section-label hard">Tier 2 hard scenarios</div>
              {hardScenarios.map((scenario) => (
                <button
                  className="scenario-row hard"
                  key={scenario.id}
                  onClick={() => void triggerHardScenario(scenario)}
                  disabled={isBusy(`hard:${scenario.id}`)}
                >
                  <span className="dot hard" />
                  <span>
                    <strong>{scenario.name}</strong>
                    <small>{scenario.operationName}</small>
                  </span>
                  <Play size={15} />
                </button>
              ))}
            </>
          )}
        </aside>

        {selected && (
          <section className="detail">
            <div className="detail-head">
              <div>
                <h2>{selected.name}</h2>
                <p>{selected.summary}</p>
              </div>
              <div className="badges">
                <span className={`badge ${selected.enabled ? 'enabled' : ''}`}>{selected.enabled ? 'enabled' : 'disabled'}</span>
                <span className={`badge ${selected.running ? 'running' : ''}`}>{selected.running ? 'running' : 'idle'}</span>
              </div>
            </div>

            <div className="control-grid">
              <section className="panel">
                <h3>Controls</h3>
                <div className="fields">
                  {selected.parameterDefinitions.map((parameter) => (
                    <label key={parameter.name}>
                      <span>{parameter.label}</span>
                      <div className="input-row">
                        {parameter.kind === 'textarea' ? (
                          <textarea
                            rows={3}
                            value={selectedDraft[parameter.name] ?? String(selected.parameters[parameter.name] ?? parameter.defaultValue)}
                            onChange={(event) => updateParameter(selected.id, parameter.name, event.target.value)}
                          />
                        ) : (
                          <input
                            type={parameter.kind === 'number' ? 'number' : 'text'}
                            min={parameter.min}
                            max={parameter.max}
                            value={selectedDraft[parameter.name] ?? String(selected.parameters[parameter.name] ?? parameter.defaultValue)}
                            onChange={(event) => updateParameter(selected.id, parameter.name, event.target.value)}
                          />
                        )}
                        {parameter.unit && <em>{parameter.unit}</em>}
                      </div>
                    </label>
                  ))}
                </div>
                <div className="button-row">
                  <button
                    className="primary"
                    onClick={() => void triggerScenario(selected)}
                    disabled={isBusy(`trigger:${selected.id}`) || selected.running}
                    title="Trigger"
                  >
                    <Play size={16} />
                    Trigger
                  </button>
                  <button onClick={() => void disableScenario(selected)} disabled={isBusy(`disable:${selected.id}`)} title="Disable">
                    <PauseCircle size={16} />
                    Disable
                  </button>
                  <button onClick={() => void resetAll()} disabled={isBusy('reset-all')} title="Reset all">
                    <RotateCcw size={16} />
                    Reset all
                  </button>
                </div>
              </section>

              <section className="panel">
                <h3>Schedule</h3>
                <div className="fields two">
                  <label>
                    <span>Interval</span>
                    <div className="input-row">
                      <input
                        type="number"
                        min={1}
                        max={3600}
                        value={selectedScheduleDraft.intervalSeconds}
                        onChange={(event) => updateSchedule(selected.id, 'intervalSeconds', event.target.value)}
                      />
                      <em>sec</em>
                    </div>
                  </label>
                  <label>
                    <span>Start in</span>
                    <div className="input-row">
                      <input
                        type="number"
                        min={0}
                        max={3600}
                        value={selectedScheduleDraft.startInSeconds}
                        onChange={(event) => updateSchedule(selected.id, 'startInSeconds', event.target.value)}
                      />
                      <em>sec</em>
                    </div>
                  </label>
                </div>
                <div className="button-row">
                  <button onClick={() => void scheduleScenario(selected)} disabled={isBusy(`schedule:${selected.id}`)} title="Schedule">
                    <CalendarClock size={16} />
                    Schedule
                  </button>
                  <button onClick={() => void cancelSchedule(selected)} disabled={isBusy(`cancel-schedule:${selected.id}`) || !selected.schedule} title="Cancel schedule">
                    <X size={16} />
                    Cancel
                  </button>
                </div>
                <dl className="facts">
                  <div>
                    <dt>Next run</dt>
                    <dd>{selected.schedule ? formatDate(selected.schedule.nextRunAtUtc) : 'none'}</dd>
                  </div>
                  <div>
                    <dt>Business route</dt>
                    <dd>{selected.businessPath}</dd>
                  </div>
                </dl>
              </section>

              {hardScenarios.length > 0 && (
                <section className="panel wide">
                  <h3>Tier 2 hard scenarios</h3>
                  <div className="hard-list">
                    {hardScenarios.map((scenario) => (
                      <div className="hard-item" key={scenario.id}>
                        <div>
                          <h4>{scenario.name}</h4>
                          <p>{scenario.summary}</p>
                          <div className="hard-meta">{scenario.operationName} / {scenario.businessPath}</div>
                        </div>
                        <button
                          className="primary"
                          onClick={() => void triggerHardScenario(scenario)}
                          disabled={isBusy(`hard:${scenario.id}`)}
                          title="Trigger"
                        >
                          <Play size={16} />
                          Trigger
                        </button>
                      </div>
                    ))}
                  </div>
                </section>
              )}

              <section className="panel wide">
                <h3>Last run</h3>
                {selected.lastRun ? (
                  <dl className="facts grid">
                    <Fact label="Run" value={shortRunId(selected.lastRun.runId)} />
                    <Fact label="Source" value={selected.lastRun.source} />
                    <Fact label="Status" value={selected.lastRun.status} />
                    <Fact label="HTTP" value={selected.lastRun.httpStatusCode?.toString() ?? 'n/a'} />
                    <Fact label="Duration" value={`${selected.lastRun.durationMs} ms`} />
                    <Fact label="Completed" value={formatDate(selected.lastRun.completedAtUtc)} />
                    <Fact label="Error" value={selected.lastRun.errorType ?? 'none'} />
                    <Fact label="Message" value={selected.lastRun.errorMessage ?? 'none'} />
                  </dl>
                ) : (
                  <p className="empty">No run recorded.</p>
                )}
              </section>

              <section className="panel wide">
                <h3>IncidentCompass delivery</h3>
                {selected.lastDelivery ? (
                  <dl className="facts grid">
                    <Fact label="Delivered" value={selected.lastDelivery.success ? 'yes' : 'no'} />
                    <Fact label="Status" value={selected.lastDelivery.statusCode?.toString() ?? 'n/a'} />
                    <Fact label="Signal" value={selected.lastDelivery.signalId ?? 'n/a'} />
                    <Fact label="Fault" value={selected.lastDelivery.faultId ?? 'n/a'} />
                    <Fact label="Job" value={selected.lastDelivery.jobId ?? 'n/a'} />
                    <Fact label="Time" value={formatDate(selected.lastDelivery.deliveredAtUtc)} />
                    <Fact label="URL" value={selected.lastDelivery.url} />
                    <Fact label="Error" value={selected.lastDelivery.error ?? 'none'} />
                  </dl>
                ) : (
                  <p className="empty">No delivery recorded.</p>
                )}
              </section>

              {lastAction && (
                <section className="panel wide compact">
                  <h3>Last business response</h3>
                  <div className="response-line">
                    <span className={`badge ${lastAction.ok ? 'enabled' : 'danger'}`}>{lastAction.status}</span>
                    <span>{formatDate(lastAction.observedAt)}</span>
                  </div>
                  <pre>{JSON.stringify(lastAction.body, null, 2)}</pre>
                </section>
              )}
            </div>
          </section>
        )}
      </section>
    </main>
  );
}

function StatusToken({ tone, icon, label }: { tone: 'ok' | 'danger' | 'neutral'; icon: React.ReactNode; label: string }) {
  return (
    <span className={`status-token ${tone}`}>
      {icon}
      {label}
    </span>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {})
    }
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `${response.status} ${response.statusText}`);
  }

  return response.json() as Promise<T>;
}

async function callBusinessPath(path: string): Promise<ActionResult> {
  const response = await fetch(path, { method: 'POST' });
  const text = await response.text();
  return {
    status: response.status,
    ok: response.ok,
    body: parseJson(text),
    observedAt: new Date().toISOString()
  };
}

function mergeParameterDrafts(current: Drafts, scenarios: Scenario[]): Drafts {
  const next = { ...current };

  for (const scenario of scenarios) {
    if (next[scenario.id]) {
      continue;
    }

    next[scenario.id] = Object.fromEntries(
      scenario.parameterDefinitions.map((parameter) => [
        parameter.name,
        String(scenario.parameters[parameter.name] ?? parameter.defaultValue)
      ])
    );
  }

  return next;
}

function mergeScheduleDrafts(current: ScheduleDrafts, scenarios: Scenario[]): ScheduleDrafts {
  const next = { ...current };

  for (const scenario of scenarios) {
    if (next[scenario.id]) {
      continue;
    }

    next[scenario.id] = {
      intervalSeconds: String(scenario.schedule?.intervalSeconds ?? 30),
      startInSeconds: '0'
    };
  }

  return next;
}

function collectParameters(scenario: Scenario, draft?: Record<string, string>) {
  return Object.fromEntries(
    scenario.parameterDefinitions.map((parameter) => {
      const raw = draft?.[parameter.name] ?? String(scenario.parameters[parameter.name] ?? parameter.defaultValue);
      return [parameter.name, parameter.kind === 'number' ? toNumber(raw, Number(parameter.defaultValue)) : raw];
    })
  );
}

function toNumber(value: string, fallback: number) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function parseJson(text: string) {
  try {
    return text ? JSON.parse(text) : null;
  } catch {
    return text;
  }
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  }).format(new Date(value));
}

function shortRunId(runId: string) {
  return runId.length > 18 ? `${runId.slice(0, 18)}...` : runId;
}









