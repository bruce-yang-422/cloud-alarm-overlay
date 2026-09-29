using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;

namespace CloudAlarmOverlay.Core.Services;

// One atomic settings document keeps configuration, occurrence identity and seven-day history
// together. Existing SQLite backup/restore includes it without a schema upgrade.
public sealed class HealthToolsService(ISettingsRepository settings, TimeProvider clock)
{
    public const string SettingsKey = "HealthToolsV1";
    public static readonly TimeSpan MaximumDeferral = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim gate = new(1, 1);
    private HealthToolsState snapshot = new();
    public HealthToolsState Snapshot => Volatile.Read(ref snapshot);
    public DateTime Now => clock.GetLocalNow().DateTime;
    private bool initialized;
    private DateTime? lastTick;
    private bool wasUnavailable;
    public bool IsPaused => Snapshot.PausedDate == DateOnly.FromDateTime(Now);

    private async Task Commit(HealthToolsState value, CancellationToken ct)
    {
        value = value with { Events = value.Events.Where(e => e.RecordedAt >= Now.Date.AddDays(-6)).ToArray() };
        await settings.SaveAsync(new Setting { Key = SettingsKey, Value = JsonSerializer.Serialize(value) }, ct);
        Volatile.Write(ref snapshot, value);
    }
    private async Task Initialize(CancellationToken ct)
    {
        if (initialized) return;
        var json = (await settings.GetAsync(SettingsKey, ct))?.Value;
        var data = json is null ? new HealthToolsState() : JsonSerializer.Deserialize<HealthToolsState>(json) ?? new();
        if (data.Tools.Length != 4 || !data.Tools.Select(t => t.Options.Kind).Order().SequenceEqual(HealthTools.Kinds.Order()))
            throw new FormatException("健康工具設定不完整。");
        foreach (var tool in data.Tools) tool.Options.Validate();
        if (data.NotificationSide is not ("Left" or "Center" or "Right")) throw new FormatException("健康提醒顯示位置無效。");
        if (data.EffectiveMode is not ("AutoAlign" or "Override" or "Delay")) throw new FormatException("番茄鐘協調模式無效。");
        // A restart never replays reminders that elapsed while the app was closed.
        await Commit(ClearPending(data, "離線期間略過") with
        { AlignmentStage = "None", AlignmentKinds = [], Tools = data.Tools.Select(t => t with { Pending = null, NextAt = t.Options.NextAfter(Now, t.Anchor) }).ToArray() }, ct);
        initialized = true;
        lastTick = Now;
    }
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { await Initialize(ct); } finally { gate.Release(); }
    }
    private HealthToolsState ClearPending(HealthToolsState data, string reason) => data with
    {
        Events = data.Events.Concat(data.Tools.Where(t => t.Pending is not null).Select(t =>
            new HealthEvent(t.Pending!.Id, t.Options.Kind, t.Pending.ScheduledAt, Now, reason))).ToArray(),
        Tools = data.Tools.Select(t => t with { Pending = null }).ToArray()
    };
    public async Task SaveToolAsync(HealthToolOptions options, CancellationToken ct = default)
    {
        options.Validate();
        await gate.WaitAsync(ct);
        try
        {
            await Initialize(ct);
            var data = Snapshot;
            var old = data.Tools.Single(t => t.Options.Kind == options.Kind);
            var events = old.Pending is null ? data.Events : [.. data.Events, new HealthEvent(old.Pending.Id, options.Kind, old.Pending.ScheduledAt, Now, "設定變更，略過")];
            await Commit(data with { Events = events, Tools = data.Tools.Select(t => t.Options.Kind == options.Kind ? new HealthToolState(options, options.NextAfter(Now)) : t).ToArray() }, ct);
        }
        finally { gate.Release(); }
    }
    public async Task SetCoordinationAsync(bool enabled, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { await Initialize(ct); await SavePreferences(enabled ? "Delay" : "Override", Snapshot.NotificationSide, ct); }
        finally { gate.Release(); }
    }
    public async Task SavePreferencesAsync(bool coordinatePomodoro, string notificationSide, CancellationToken ct = default)
        => await SavePreferencesAsync(coordinatePomodoro ? "Delay" : "Override", notificationSide, ct);
    public async Task SavePreferencesAsync(string mode, string notificationSide, CancellationToken ct = default)
    {
        if (notificationSide is not ("Left" or "Center" or "Right")) throw new ArgumentException("請選擇左邊、置中或右邊。");
        if (mode is not ("AutoAlign" or "Override" or "Delay")) throw new ArgumentException("請選擇自動對齊、覆蓋或延後。");
        await gate.WaitAsync(ct);
        try { await Initialize(ct); await SavePreferences(mode, notificationSide, ct); }
        finally { gate.Release(); }
    }
    private async Task SavePreferences(string mode, string side, CancellationToken ct)
    {
        var data = Snapshot;
        if (data.EffectiveMode != mode)
            data = data with { AlignmentStage = "None", AlignmentKinds = [],
                Tools = data.Tools.Select(t => t with { Anchor = null, NextAt = t.Options.NextAfter(Now) }).ToArray() };
        await Commit(data with { PomodoroMode = mode, CoordinatePomodoro = mode != "Override", NotificationSide = side }, ct);
    }

    // Align only once per Pomodoro session. A reset (round 1 / idle) arms the next session.
    public async Task ObservePomodoroAsync(PomodoroState state, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await Initialize(ct);
            var data = Snapshot;
            if (data.EffectiveMode != "AutoAlign") return;
            if (data.AlignmentStage == "Break" && (state.Phase != "Focus" && state.Status == "AwaitingConfirmation" || state is { Phase: "Focus", Status: "Idle" }))
            {
                data = data with { AlignmentStage = "Done", Tools = data.Tools.Select(t => data.AlignmentKinds.Contains(t.Options.Kind)
                    ? t with { Anchor = Now, NextAt = t.Options.NextAfter(Now, Now) } : t).ToArray(), AlignmentKinds = [] };
            }
            if (state is { Phase: "Focus", Status: "Idle", Round: 1 } && data.AlignmentStage != "None")
                data = data with { AlignmentStage = "None", AlignmentKinds = [],
                    Tools = data.Tools.Select(t => t with { NextAt = t.Options.NextAfter(Now, t.Anchor) }).ToArray() };
            if (data.AlignmentStage == "None" && state.Phase == "Focus" && state.Status is "Running" or "Paused" or "AwaitingConfirmation")
                data = ClearPending(data, "等待首次番茄鐘休息") with { AlignmentStage = "Focus" };
            if (data.AlignmentStage == "Focus" && (state is { Phase: "Focus", Status: "AwaitingConfirmation" } || state.Phase is "Break" or "LongBreak"))
            {
                var kinds = IsPaused ? [] : data.Tools.Where(t => t.Options.IsActive(Now)).Select(t => t.Options.Kind).ToArray();
                data = data with { AlignmentStage = "Break", AlignmentKinds = kinds, Tools = data.Tools.Select(t => kinds.Contains(t.Options.Kind)
                    ? t with { Pending = new(t.Options.Kind + "@align@" + Now.ToString("O"), t.Options.Kind, Now) } : t).ToArray() };
            }
            if (!ReferenceEquals(data, Snapshot)) await Commit(data, ct);
        }
        finally { gate.Release(); }
    }
    public async Task SetPausedAsync(bool paused, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await Initialize(ct);
            var data = ClearPending(Snapshot, "今日暫停，略過");
            await Commit(data with { PausedDate = paused ? DateOnly.FromDateTime(Now) : null,
                Tools = data.Tools.Select(t => t with { NextAt = t.Options.NextAfter(Now, t.Anchor) }).ToArray() }, ct);
        }
        finally { gate.Release(); }
    }

    public async Task TickAsync(bool unavailable = false, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await Initialize(ct);
            var now = Now;
            var data = Snapshot;
            var gap = lastTick is { } previous && (now - previous > TimeSpan.FromSeconds(15) || now < previous);
            var reset = gap || unavailable || wasUnavailable;
            lastTick = now; wasUnavailable = unavailable;
            var tools = new List<HealthToolState>();
            var events = data.Events.ToList();
            foreach (var tool in data.Tools)
            {
                var next = tool;
                if (reset || IsPaused || !tool.Options.IsActive(now))
                {
                    if (tool.Pending is { } expired) events.Add(new(expired.Id, expired.Kind, expired.ScheduledAt, now, "不在提醒時段，略過"));
                    next = tool with { Pending = null, NextAt = tool.NextAt <= now || reset ? tool.Options.NextAfter(now, tool.Anchor) : tool.NextAt };
                }
                else if (data.EffectiveMode == "AutoAlign" && data.AlignmentStage is "Focus" or "Break")
                    next = tool; // The first shared break replaces the old periodic occurrences.
                else if (tool.NextAt is { } due && due <= now)
                {
                    var id = tool.Options.Kind + "@" + due.ToString("O");
                    var last = events.Where(e => e.Kind == tool.Options.Kind && e.TriggeredAt is not null).MaxBy(e => e.TriggeredAt)?.TriggeredAt;
                    var pending = tool.Pending;
                    if (last is { } shown && now >= shown && now - shown < Cooldown)
                        events.Add(new(id, tool.Options.Kind, due, now, "近期已提醒，略過"));
                    else if (pending is null) pending = new(id, tool.Options.Kind, due);
                    else events.Add(new(id, tool.Options.Kind, due, now, "已合併至待提醒項目"));
                    next = tool with { Pending = pending, NextAt = tool.Options.NextAfter(now, tool.Anchor) };
                }
                tools.Add(next);
            }
            if (!tools.SequenceEqual(data.Tools) || events.Count != data.Events.Length || data.Events.Any(e => e.RecordedAt < now.Date.AddDays(-6)))
                await Commit(data with { Tools = tools.ToArray(), Events = events.ToArray() }, ct);
        }
        finally { gate.Release(); }
    }

    public HealthPending[] Ready(PomodoroState pomodoro, bool quiet, bool unavailable)
    {
        if (quiet || unavailable || IsPaused) return [];
        var data = Snapshot;
        if (data.EffectiveMode == "AutoAlign" && data.AlignmentStage == "Focus") return [];
        var focus = data.EffectiveMode == "Delay" && pomodoro is { Status: "Running", Phase: "Focus" };
        return data.Tools.Where(t => t.Options.IsActive(Now) && t.Pending is not null)
            .Select(t => t.Pending!).Where(p => !focus || Now - p.ScheduledAt >= MaximumDeferral).ToArray();
    }
    public async Task MarkDisplayedAsync(IEnumerable<string> ids, string batchId, CancellationToken ct = default)
    {
        var selected = ids.ToHashSet();
        await gate.WaitAsync(ct);
        try
        {
            await Initialize(ct);
            var data = Snapshot;
            var pending = data.Tools.Where(t => t.Pending is { } p && selected.Contains(p.Id)).Select(t => t.Pending!).ToArray();
            if (pending.Length == 0) return;
            await Commit(data with
            {
                Events = [.. data.Events, .. pending.Select(p => new HealthEvent(p.Id, p.Kind, p.ScheduledAt, Now, "已顯示", batchId, Now))],
                Tools = data.Tools.Select(t => t.Pending is { } p && selected.Contains(p.Id) ? t with { Pending = null } : t).ToArray()
            }, ct);
        }
        finally { gate.Release(); }
    }
    public async Task AcknowledgeAsync(string batchId, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await Initialize(ct);
            await Commit(Snapshot with { Events = Snapshot.Events.Select(e => e.BatchId == batchId && e.TriggeredAt is not null && e.AcknowledgedAt is null
                ? e with { AcknowledgedAt = Now, Result = "已確認" } : e).ToArray() }, ct);
        }
        finally { gate.Release(); }
    }
}
