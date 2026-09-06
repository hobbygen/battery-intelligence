using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace BatteryIntelligence.App;

/// <summary>
/// Application entry point.
/// </summary>
/// <remarks>
/// <para>
/// The XAML-generated entry point is disabled (<c>DISABLE_XAML_GENERATED_MAIN</c>)
/// because single-instance redirection has to be decided <em>before</em> the XAML
/// application is constructed. Letting the generated Main run first would start a
/// second monitoring engine and then tear it down, which is exactly what
/// specification section 59 forbids.
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>Key identifying the primary monitoring instance.</summary>
    private const string InstanceKey = "BatteryIntelligence.Main";

    /// <summary>
    /// How long to wait for activation redirection before giving up and exiting
    /// anyway. A hung primary instance must not leave the second process alive.
    /// </summary>
    private static readonly TimeSpan RedirectTimeout = TimeSpan.FromSeconds(5);

    [STAThread]
    private static int Main(string[] args)
    {
        _ = args;

        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (ShouldRedirectToExistingInstance())
        {
            // Another instance owns the key. It has been asked to activate; this
            // process exits without ever creating a window or a host.
            return 0;
        }

        Application.Start(initParams =>
        {
            _ = initParams;

            DispatcherQueueSynchronizationContext context = new(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);

            _ = new App();
        });

        return 0;
    }

    /// <summary>
    /// Registers this process as the primary instance, or redirects activation to
    /// the instance that already holds the key.
    /// </summary>
    /// <returns><see langword="true"/> when this process should exit immediately.</returns>
    private static bool ShouldRedirectToExistingInstance()
    {
        AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        AppInstance primary = AppInstance.FindOrRegisterForKey(InstanceKey);

        if (primary.IsCurrent)
        {
            primary.Activated += OnPrimaryInstanceActivated;
            return false;
        }

        RedirectActivationTo(activation, primary);
        return true;
    }

    private static void OnPrimaryInstanceActivated(object? sender, AppActivationArguments args)
    {
        _ = sender;
        _ = args;

        // Raised on a background thread. App is responsible for marshalling to
        // the UI thread before touching the window.
        (Application.Current as App)?.OnRelaunched();
    }

    /// <summary>
    /// Hands the activation to the primary instance and waits for it to be
    /// accepted.
    /// </summary>
    /// <remarks>
    /// The redirect is awaited on a thread-pool thread while this thread blocks on
    /// an event. Because this process is about to exit and has no COM objects to
    /// service, there is no message loop to keep pumping, so a plain wait is both
    /// correct and simpler than the COM-pumping alternative.
    /// </remarks>
    private static void RedirectActivationTo(AppActivationArguments args, AppInstance primary)
    {
        using ManualResetEventSlim completed = new(initialState: false);

        _ = Task.Run(async () =>
        {
            try
            {
                await primary.RedirectActivationToAsync(args);
            }
            catch (Exception)
            {
                // Nothing useful can be done here: logging is not yet configured
                // and this process is exiting regardless.
            }
            finally
            {
                completed.Set();
            }
        });

        completed.Wait(RedirectTimeout);
    }
}
