using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexUsageCompanion;

public partial class App : Application
{
    private const string MutexName = @"Local\CodexUsageCompanion.SingleInstance";
    private const string ShutdownEventName = @"Local\CodexUsageCompanion.Shutdown";
    private Mutex? _singleInstance;
    private EventWaitHandle? _shutdownEvent;
    private RegisteredWaitHandle? _shutdownRegistration;
    private Timer? _workingSetTimer;
    private CompanionHost? _host;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _host = CompanionHost.CreateDefault();
        _host.Start();
        _shutdownEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ShutdownEventName);
        _shutdownRegistration = ThreadPool.RegisterWaitForSingleObject(
            _shutdownEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    _ = Dispatcher.BeginInvoke(new Action(Shutdown));
                }
            },
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: true);
        _workingSetTimer = new Timer(
            _ => WorkingSetTrimmer.Trim(),
            null,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMinutes(5));
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _shutdownRegistration?.Unregister(null);
        _shutdownRegistration = null;
        _shutdownEvent?.Dispose();
        _shutdownEvent = null;
        _workingSetTimer?.Dispose();
        _workingSetTimer = null;

        if (_host is not null)
        {
            _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _host = null;
        }

        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
            _singleInstance = null;
        }

        base.OnExit(eventArgs);
    }
}
