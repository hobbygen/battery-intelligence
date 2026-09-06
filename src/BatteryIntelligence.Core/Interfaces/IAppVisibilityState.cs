namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Whether the main window is currently shown to the user (as opposed to
/// tray-only). Read by the samplers so a hidden window can suspend the chart feed
/// (docs/monitoring-dataflow.md section 3) without the Core layer taking a
/// dependency on WinUI. The App sets it from the window show/hide paths.
/// </summary>
public interface IAppVisibilityState
{
    /// <summary>Whether the main window is visible. Defaults to <see langword="true"/> until told otherwise.</summary>
    bool IsWindowVisible { get; }

    /// <summary>Raised when <see cref="IsWindowVisible"/> changes.</summary>
    event EventHandler? Changed;
}

/// <inheritdoc cref="IAppVisibilityState"/>
public sealed class AppVisibilityState : IAppVisibilityState
{
    private int _visible = 1;

    /// <inheritdoc />
    public bool IsWindowVisible => Volatile.Read(ref _visible) != 0;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Records the window's current visibility, raising <see cref="Changed"/> on a transition.</summary>
    public void SetVisible(bool visible)
    {
        int next = visible ? 1 : 0;
        if (Interlocked.Exchange(ref _visible, next) != next)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
