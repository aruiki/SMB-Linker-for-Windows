using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SMBLinker.Core;

public sealed class StateChangedEventArgs : EventArgs
{
    public required MountProfile Profile { get; init; }
    public required MountState Previous { get; init; }
    public required MountState State { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Keeps every enabled profile mounted. Reacts to sign-in, network changes, wake from sleep,
/// session unlock and a periodic health check, and always waits until the server answers on
/// TCP 445 before mapping (this is what Windows' own logon restore does not do).
/// </summary>
internal sealed class ReconnectService : IDisposable
{
    private readonly Func<AppConfig> _config;
    private readonly SemaphoreSlim _cycleLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, (MountState state, string message)> _states = new();
    private readonly HashSet<Guid> _manuallyDisconnected = new();
    private readonly object _gate = new();
    private Timer? _healthTimer;
    private Timer? _debounceTimer;
    private CancellationTokenSource _burstCts = new();
    private bool _disposed;

    public event EventHandler<StateChangedEventArgs>? StateChanged;

    public ReconnectService(Func<AppConfig> config) => _config = config;

    public (MountState state, string message) GetState(Guid id) =>
        _states.TryGetValue(id, out var s) ? s : (MountState.Unknown, "");

    public void Start()
    {
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        RestartHealthTimer();
        StartBurst("startup");
    }

    public void RestartHealthTimer()
    {
        var seconds = Math.Clamp(_config().Settings.HealthCheckSeconds, 15, 3600);
        _healthTimer?.Dispose();
        _healthTimer = new Timer(_ => _ = RunCycleAsync("health", userInitiated: false), null,
            TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(seconds));
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => Debounced("network change", 3000);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Debounced("resume", 4000);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.RemoteConnect)
            Debounced("unlock", 1500);
    }

    private void Debounced(string reason, int delayMs)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => StartBurst(reason), null, delayMs, Timeout.Infinite);
        }
    }

    /// <summary>
    /// After an event that usually means "the network is coming up" retry quickly for a few minutes
    /// (2s, 3s, 5s ... up to 30s) instead of waiting for the next health check.
    /// </summary>
    private void StartBurst(string reason)
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed) return;
            _burstCts.Cancel();
            _burstCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            token = _burstCts.Token;
        }

        _ = Task.Run(async () =>
        {
            int delay = 2000;
            while (!token.IsCancellationRequested)
            {
                bool pending = await RunCycleAsync(reason, userInitiated: false).ConfigureAwait(false);
                if (!pending) return;
                try { await Task.Delay(delay, token).ConfigureAwait(false); } catch { return; }
                delay = Math.Min(delay * 3 / 2, 30000);
            }
        });
    }

    /// <summary>Called when the user presses "Connect" or edits a profile.</summary>
    public Task ConnectNowAsync(MountProfile p)
    {
        lock (_manuallyDisconnected) _manuallyDisconnected.Remove(p.Id);
        return RunCycleAsync("user", userInitiated: true, only: p.Id);
    }

    public Task RefreshAllAsync() => RunCycleAsync("user", userInitiated: true);

    public void Disconnect(MountProfile p)
    {
        lock (_manuallyDisconnected) _manuallyDisconnected.Add(p.Id);
        int rc = DriveMapper.Unmount(p);
        Log.Info($"User disconnect {p.LocalName}: {rc}");
        SetState(p, MountState.Disabled, Strings.T("state.disconnectedByUser"));
    }

    public void Forget(MountProfile p)
    {
        _states.TryRemove(p.Id, out _);
        lock (_manuallyDisconnected) _manuallyDisconnected.Remove(p.Id);
    }

    /// <summary>Runs one pass over all profiles. Returns true if something is still waiting for the network.</summary>
    private async Task<bool> RunCycleAsync(string reason, bool userInitiated, Guid? only = null)
    {
        if (_disposed) return false;
        await _cycleLock.WaitAsync().ConfigureAwait(false);
        bool pending = false;
        try
        {
            var cfg = _config();
            foreach (var p in cfg.Profiles.ToList())
            {
                if (only.HasValue && p.Id != only.Value) continue;
                try
                {
                    pending |= await ProcessAsync(p, cfg.Settings, reason, userInitiated).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error($"Cycle error for {p.LocalName}", ex);
                    SetState(p, MountState.Error, ex.Message);
                }
            }
        }
        finally
        {
            _cycleLock.Release();
        }
        return pending;
    }

    private async Task<bool> ProcessAsync(MountProfile p, AppSettings settings, string reason, bool userInitiated)
    {
        if (!p.Enabled)
        {
            SetState(p, MountState.Disabled, Strings.T("state.disabled"));
            return false;
        }
        lock (_manuallyDisconnected)
            if (_manuallyDisconnected.Contains(p.Id) && !userInitiated) return false;

        var prev = GetState(p.Id).state;

        // Never hammer the server with a password it already rejected (account lockout risk).
        if (prev == MountState.AuthFailed && !userInitiated) return false;

        // Already fine?
        var (code, remote) = Native.WNet.GetConnection(p.LocalName);
        if (code == Native.WNet.NO_ERROR && DriveMapper.SameRemote(remote, p.RemotePath)
            && await DriveMapper.IsAccessibleAsync(p.LocalName + "\\").ConfigureAwait(false))
        {
            if (prev != MountState.Connected)
            {
                // Mapped by someone else (e.g. Windows at sign-in, or before the app was installed):
                // take it over - stop Windows from racing us at the next sign-in and apply our name/icon.
                DriveMapper.ForgetWindowsPersistence(p);
                ExplorerCustomizer.Apply(p);
            }
            SetState(p, MountState.Connected, Strings.T("state.connected"));
            return false;
        }

        if (!await DriveMapper.IsReachableAsync(p.Server).ConfigureAwait(false))
        {
            SetState(p, MountState.WaitingForNetwork, Strings.F("state.waiting", p.Server));
            return true;
        }

        SetState(p, MountState.Connecting, Strings.T("state.connecting"));
        Log.Info($"Mounting {p.LocalName} ({reason})");
        var result = DriveMapper.Mount(p, settings);
        SetState(p, result.State, result.Message);
        if (result.State == MountState.Connected)
            ExplorerCustomizer.Apply(p);
        return result.State == MountState.Unreachable;
    }

    private void SetState(MountProfile p, MountState state, string message)
    {
        var prev = GetState(p.Id).state;
        _states[p.Id] = (state, message);
        if (prev != state)
        {
            Log.Info($"{p.LocalName} {prev} -> {state}: {message}");
            StateChanged?.Invoke(this, new StateChangedEventArgs { Profile = p, Previous = prev, State = state, Message = message });
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _burstCts.Cancel();
            _debounceTimer?.Dispose();
        }
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _healthTimer?.Dispose();
    }
}
