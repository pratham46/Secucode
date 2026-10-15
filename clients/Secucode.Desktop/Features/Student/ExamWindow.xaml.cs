using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Shape = System.Windows.Shapes.Shape;
using System.Windows.Threading;
using Secucode.Contracts;
using Secucode.Desktop.Api;
using Secucode.Desktop.Outbox;
using Secucode.Desktop.Proctoring;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop.Features.Student;

// The proctored exam window. The rules live in ExamSession; this wires hardware, timers, the outbox and the hub
// to it. Every way out goes through FinishAsync, which shows the outcome before the window may close.
public partial class ExamWindow : Window
{
#if DEBUG
    private static readonly bool DevMode = true; // focus-loss checks are off while debugging
#else
    private static readonly bool DevMode = false;
#endif
    private static readonly string[] Languages = ["Python", "JavaScript", "Java", "C++"];

    private readonly ApiClient _api;
    private readonly FaceMonitor _face;
    private readonly AttemptOutbox _outbox;
    private readonly ExamSession _session;
    private readonly LiveConnection _live;
    private readonly AudioRecorder _audio = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _faceTimer = new() { Interval = TimeSpan.FromSeconds(FaceMonitor.CheckSeconds) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly string _localCopy;
    private bool _proctoring;
    private bool _finishing;
    private bool _canClose;

    public ExamWindow(ApiClient api, string examTitle, AttemptDto attempt, FaceMonitor face, byte[] referencePng)
    {
        InitializeComponent();
        _api = api;
        _face = face;
        _outbox = new AttemptOutbox(AttemptOutbox.DefaultRoot, attempt.Id, new ApiAttemptSender(api));
        _session = new ExamSession(attempt, _outbox, () => DateTime.UtcNow);
        _live = new LiveConnection(api.Http.BaseAddress!, api.FreshAccessTokenAsync);
        _localCopy = Path.Combine(AppSettings.DataDir, "code", $"{attempt.Id}.txt");

        ExamTitle.Text = examTitle;
        QuestionText.Text = attempt.QuestionText;
        CodeEditor.Text = attempt.Code;
        LanguageBox.ItemsSource = Languages;
        LanguageBox.SelectedItem = Languages.Contains(attempt.Language) ? attempt.Language : "Python";

        _outbox.Evidence(EvidenceKind.ReferenceFace, referencePng, "image/png");
        _outbox.OnlineChanged += online => Dispatcher.BeginInvoke(() => ShowConnection(online));
        _outbox.AttemptClosed += () => Dispatcher.BeginInvoke(() => _ = FinishAsync());
        _session.AutoSubmitRequested += reason => Dispatcher.BeginInvoke(() => Submit(reason));
        _session.Closed += reason => Dispatcher.BeginInvoke(() => _ = FinishAsync());
        _live.Received += (method, message) => Dispatcher.BeginInvoke(() => _session.OnLive(method, message));
        _live.Connected += () => Dispatcher.InvokeAsync(ResyncAsync).Task.Unwrap();
        _api.SessionExpired += OnSessionExpired;

        Loaded += (_, _) => StartProctoring();
        Closing += OnClosing;
    }

    private string SelectedLanguage => LanguageBox.SelectedItem as string ?? "Python";

    private void StartProctoring()
    {
        _proctoring = true;
        try { Lockdown.Enable(); }
        catch (Win32Exception)
        {
            // No exam event type fits, so this is only shown; the exam continues without the keyboard hook.
            Banner.Show(InfoSeverity.Warning, "Keyboard lockdown is unavailable on this PC.");
        }
        // Block copy, cut and paste everywhere in the window (shortcuts and context menus).
        CommandManager.AddPreviewExecutedHandler(this, (_, e) =>
        {
            if (e.Command == ApplicationCommands.Copy || e.Command == ApplicationCommands.Cut || e.Command == ApplicationCommands.Paste)
                e.Handled = true;
        });
        CodeEditor.AllowDrop = false;
        StdinBox.AllowDrop = false;
        if (!DevMode) Deactivated += OnDeactivated;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) _session.Minimized();
        };

        _audio.ChunkReady += chunk => _outbox.Evidence(EvidenceKind.Audio, chunk, "audio/wav");
        if (!_audio.Start(out var audioError)) _session.AudioUnavailable(audioError ?? "No microphone.");

        _tick.Tick += (_, _) => Tick();
        _faceTimer.Tick += (_, _) =>
        {
            var (result, jpeg) = _face.Check();
            _session.FaceResult(result, jpeg);
            ShowStrikes();
        };
        _saveTimer.Tick += (_, _) => Save();
        _tick.Start();
        _faceTimer.Start();
        _saveTimer.Start();
        _outbox.Start();
        _ = _live.StartWithRetryAsync(_cts.Token);
        Tick();
    }

    private void Tick()
    {
        var left = _session.Clock.Remaining(DateTime.UtcNow);
        TimerText.Text = left.ToString(left.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        TimerText.SetResourceReference(ForegroundProperty, left < TimeSpan.FromMinutes(5) ? "Danger" : "TextPrimary");
        if (left == TimeSpan.Zero && !_session.Ended) Submit("Time limit reached.");
    }

    private void Save()
    {
        _session.SaveCode(CodeEditor.Text, SelectedLanguage);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_localCopy)!);
            File.WriteAllText(_localCopy, CodeEditor.Text);
        }
        catch (IOException) { } // the outbox copy is the one that matters
        SavedText.Text = $"Saved at {DateTime.Now:HH:mm}";
    }

    private void ShowStrikes()
    {
        foreach (var (dot, n) in new[] { (Strike1, 1), (Strike2, 2), (Strike3, 3) })
            dot.SetResourceReference(Shape.FillProperty, _session.FaceStrikes >= n ? "Danger" : "BorderStrong");
    }

    private void ShowConnection(bool online)
    {
        if (online) Banner.Hide();
        else Banner.Show(InfoSeverity.Warning, "Offline — your work is saved on this PC and will be sent when the connection returns.");
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_session.Ended) return;
        // Let activation settle, then count it only if focus went to another process (our own dialogs also deactivate us).
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_session.Ended || IsActive || Lockdown.ForegroundIsOurProcess()) return;
            _session.FocusLost();
            if (!_session.Ended)
                Banner.Show(InfoSeverity.Error,
                    $"You switched away from the exam ({_session.FocusMisses}/{ExamSession.MaxFocusMisses}). Reaching the limit submits your exam.");
        });
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        OutputBox.Text = "Running…";
        try
        {
            var r = await _api.RunAsync(new RunRequest(SelectedLanguage, CodeEditor.Text, StdinBox.Text));
            OutputBox.Text = r.Stdout
                + (r.Stderr.Length > 0 ? "\n" + r.Stderr : "")
                + (r.TimedOut ? "\n[Timed out]" : "")
                + $"\n[exit code {r.ExitCode} · {r.DurationMs} ms]";
        }
        catch (ApiError error) when (error.Status == 429)
        {
            OutputBox.Text = "Too many runs. Wait a moment and try again.";
        }
        catch (ApiError error)
        {
            OutputBox.Text = error.Display;
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Submit your code now? You can't change it afterwards.", "Submit exam",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Submit("Submitted by the student.");
    }

    // Final frame and screenshot first, then stop proctoring (which queues the last audio chunk), then submit.
    private void Submit(string reason)
    {
        if (_session.Ended) return;
        var face = _face.CaptureJpeg();
        var screen = ScreenCapture.Jpeg();
        StopProctoring();
        _session.Submit(CodeEditor.Text, SelectedLanguage, reason, face, screen);
        _ = FinishAsync();
    }

    private void StopProctoring()
    {
        if (!_proctoring) return;
        _proctoring = false;
        _tick.Stop();
        _faceTimer.Stop();
        _saveTimer.Stop();
        Deactivated -= OnDeactivated;
        _audio.Stop();
        Lockdown.Disable();
    }

    // Waits until the queue is sent, the server closes the attempt, or the submit grace period is over; then shows the outcome.
    private async Task FinishAsync()
    {
        if (_finishing || _canClose) return; // _canClose: already leaving (signed out)
        _finishing = true;
        StopProctoring();
        Overlay.Visibility = Visibility.Visible;
        Transitions.PopIn(OverlayCard);
        OverlayTitle.Text = "Submitting…";
        OverlayText.Text = "Sending your code and evidence.";

        while (_outbox.Pending > 0 && !_outbox.Closed && !_session.Clock.PastSubmitGrace(DateTime.UtcNow))
        {
            if (!_outbox.IsOnline)
                OverlayText.Text = "Offline — your submission will be sent when the connection returns. Keep this window open.";
            await Task.Delay(500);
        }

        (OverlayTitle.Text, OverlayText.Text) = await OutcomeAsync();
        OverlayButton.Visibility = Visibility.Visible;
        Topmost = false;
    }

    private async Task<(string Title, string Text)> OutcomeAsync()
    {
        try
        {
            var a = (await _api.AttemptAsync(_session.Attempt.Id)).Attempt;
            return a.Status switch
            {
                AttemptStatus.Submitted => ("Submitted", $"Your exam was submitted. {a.SubmitReason}"),
                AttemptStatus.AutoSubmitted => ("Submitted by the server", "The deadline passed, so the server submitted your last saved code."),
                AttemptStatus.Voided => ("Attempt voided", "Staff voided this attempt. Ask your teacher what to do next."),
                _ => ("Not submitted yet", "The server hasn't received your submission. Sign in again and choose Resume; your work is saved on this PC."),
            };
        }
        catch (ApiError)
        {
            return _outbox.Pending == 0
                ? ("Submitted", "Your exam was submitted.")
                : ("Couldn't reach the server", "The deadline has passed, so the server has submitted your last saved code.");
        }
    }

    private void OverlayButton_Click(object sender, RoutedEventArgs e)
    {
        _canClose = true;
        Close();
    }

    // Signed out (session revoked): leave without submitting; the attempt can be resumed after signing in again.
    private void OnSessionExpired(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_finishing) return;
        _session.Abandon("Signed out");
        StopProctoring();
        _canClose = true;
        MessageBox.Show(this, "You were signed out. Sign in again and choose Resume to continue — your work is saved on this PC.",
            "Signed out", MessageBoxButton.OK, MessageBoxImage.Warning);
        Close();
    });

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_canClose)
        {
            e.Cancel = true; // a close we didn't start (e.g. a key combination the hook missed) submits
            if (!_session.Ended) Submit("Test window was closed.");
            return;
        }
        _api.SessionExpired -= OnSessionExpired;
        StopProctoring();
        _cts.Cancel();
        _face.Dispose();
        _audio.Dispose();
        _ = _live.DisposeAsync().AsTask();
        _ = CloseOutboxAsync();
    }

    private async Task CloseOutboxAsync()
    {
        await _outbox.DisposeAsync();
        _outbox.DeleteIfEmpty(); // anything left stays on disk and is sent when the attempt is resumed
    }

    private async Task ResyncAsync()
    {
        try { _session.Resync((await _api.AttemptAsync(_session.Attempt.Id)).Attempt); }
        catch (ApiError) { } // offline: the hub reconnects and this runs again
    }
}
