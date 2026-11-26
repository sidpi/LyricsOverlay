using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using LyricOverlay.Diagnostics;

namespace LyricOverlay.Updates;

public enum UpdatePhase
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Installing,
    Restarting,
    Error,
}

/// <summary>
/// The panel's state machine: check → available → download → install → restart, with cancel,
/// retry and dismiss. Every transition raises <see cref="PropertyChanged"/> so the view repaints.
/// </summary>
public sealed class UpdateController : INotifyPropertyChanged
{
    private readonly IUpdateService _service;
    private readonly Action? _quit;
    private CancellationTokenSource? _check;
    private CancellationTokenSource? _download;
    private UpdatePhase _phase = UpdatePhase.Idle;
    private UpdateInfo? _info;
    private DownloadProgress _progress;
    private string? _errorMessage;
    private UpdateErrorKind? _errorKind;

    public UpdateController(IUpdateService service, Action? quit = null)
    {
        _service = service;
        _quit = quit;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<UpdatePhase>? PhaseChanged;

    public UpdatePhase Phase => _phase;

    public UpdateInfo? Info => _info;

    public DownloadProgress Progress => _progress;

    public string? ErrorMessage => _errorMessage;

    public UpdateErrorKind? ErrorKind => _errorKind;

    public string CurrentVersion => _service.CurrentVersion;

    /// <summary>Installing cannot be interrupted; the NSIS hand-off needs a stable app.</summary>
    public bool IsLocked => !_service.Simulated && _phase is UpdatePhase.Installing or UpdatePhase.Restarting;

    public bool Simulated => _service.Simulated;

    public async Task CheckAsync(bool silent = false)
    {
        if (_phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing
            or UpdatePhase.Restarting)
        {
            return;
        }

        Log.Info($"update check requested (silent={silent})");
        CancelCheck();
        _check = new CancellationTokenSource();
        var token = _check.Token;

        _errorMessage = null;
        _errorKind = null;
        SetPhase(UpdatePhase.Checking);

        try
        {
            var info = await _service.CheckAsync(token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (info is null)
            {
                // A silent startup check never interrupts with "up to date".
                SetPhase(silent ? UpdatePhase.Idle : UpdatePhase.UpToDate);
                return;
            }

            _info = info;
            _progress = default;
            SetPhase(UpdatePhase.Available);
        }
        catch (OperationCanceledException)
        {
            if (_phase == UpdatePhase.Checking)
            {
                SetPhase(UpdatePhase.Idle);
            }
        }
        catch (Exception ex)
        {
            Fail(ex, silent);
        }
    }

    public async Task StartUpdateAsync()
    {
        if (_phase != UpdatePhase.Available || _info is null)
        {
            return;
        }

        var info = _info;
        CancelDownload();
        _download = new CancellationTokenSource();
        var token = _download.Token;

        _progress = default;
        _errorMessage = null;
        _errorKind = null;
        SetPhase(UpdatePhase.Downloading);

        string path;
        try
        {
            var progress = new Progress<DownloadProgress>(value =>
            {
                if (_phase != UpdatePhase.Downloading)
                {
                    return;
                }

                _progress = value;
                Notify(nameof(Progress));
            });

            path = await _service.DownloadAsync(info, progress, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cancel already moved the panel back to "available"; nothing else to do.
            return;
        }
        catch (Exception ex)
        {
            Fail(ex);
            return;
        }

        if (_phase != UpdatePhase.Downloading)
        {
            return;
        }

        try
        {
            // Verification is part of installing, so a bad hash lands in the error state.
            SetPhase(UpdatePhase.Installing);
            await _service.VerifyAsync(path, info, CancellationToken.None).ConfigureAwait(true);
            await Task.Delay(480).ConfigureAwait(true);

            SetPhase(UpdatePhase.Restarting);
            await Task.Delay(850).ConfigureAwait(true);

            await _service.InstallAsync(path, info, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Fail(ex);
            return;
        }

        if (_service.Simulated)
        {
            // Preview mode: nothing restarts, so return the panel to a usable state.
            await Task.Delay(1400).ConfigureAwait(true);
            _info = null;
            SetPhase(UpdatePhase.Idle);
            return;
        }

        // NSIS kills this process; the installer relaunches the new build when it is done.
        _quit?.Invoke();
    }

    public void CancelDownload()
    {
        _download?.Cancel();
        _download = null;

        if (_phase == UpdatePhase.Downloading)
        {
            _progress = default;
            SetPhase(UpdatePhase.Available);
        }
    }

    public void Dismiss()
    {
        if (IsLocked)
        {
            return;
        }

        if (_phase == UpdatePhase.Downloading)
        {
            CancelDownload();
        }

        CancelCheck();

        if (_phase is UpdatePhase.UpToDate or UpdatePhase.Error)
        {
            _errorMessage = null;
            _errorKind = null;
            SetPhase(UpdatePhase.Idle);
        }
    }

    public async Task RetryAsync()
    {
        _errorMessage = null;
        _errorKind = null;
        await CheckAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Preview-only jump to any visual state, used by <see cref="UpdatePreviewWindow"/> and the
    /// mock tray flow. Never called by the shipping update path.
    /// </summary>
    internal void Preview(UpdatePhase phase)
    {
        Log.Info($"update preview -> {phase}");
        CancelCheck();
        CancelDownload();
        _errorMessage = null;
        _errorKind = null;

        switch (phase)
        {
            case UpdatePhase.Idle:
                _info = null;
                _progress = default;
                break;
            case UpdatePhase.Checking:
                break;
            case UpdatePhase.UpToDate:
                break;
            case UpdatePhase.Available:
                _info = MockUpdateService.Sample;
                break;
            case UpdatePhase.Downloading:
                _info = MockUpdateService.Sample;
                _progress = new DownloadProgress(16_420_000, MockUpdateService.InstallerSize, 7_400_000);
                break;
            case UpdatePhase.Installing:
                _info ??= MockUpdateService.Sample;
                _progress = new DownloadProgress(MockUpdateService.InstallerSize, MockUpdateService.InstallerSize, 0);
                break;
            case UpdatePhase.Restarting:
                _info ??= MockUpdateService.Sample;
                break;
            case UpdatePhase.Error:
                _errorKind = UpdateErrorKind.Network;
                _errorMessage =
                    "The connection dropped before the download finished. Check your network and retry.";
                break;
        }

        SetPhase(phase);
    }

    private void Fail(Exception ex, bool silent = false)
    {
        var kind = ex is UpdateException known ? known.Kind : UpdateErrorKind.Unknown;
        var message = ex is UpdateException ? ex.Message : "Something went wrong while updating.";

        Log.Error($"update failed ({kind}): {ex.Message}", ex);

        // An offline launch check must never nag; the tray item is still there.
        if (silent)
        {
            SetPhase(UpdatePhase.Idle);
            return;
        }

        _errorKind = kind;
        _errorMessage = message;
        _progress = default;
        SetPhase(UpdatePhase.Error);
    }

    private void SetPhase(UpdatePhase phase)
    {
        _phase = phase;
        Log.Info($"update phase -> {phase}");
        Notify(nameof(Phase));
        PhaseChanged?.Invoke(this, phase);
    }

    private void CancelCheck()
    {
        _check?.Cancel();
        _check = null;
    }

    private void Notify(string name)
    {
        // The panel re-reads every value on any change; there are only a handful per tick.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name != nameof(Progress))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Info)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorMessage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorKind)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentVersion)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLocked)));
        }
    }
}
