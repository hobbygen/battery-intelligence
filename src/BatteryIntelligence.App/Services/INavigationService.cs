using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Services;

/// <summary>
/// Moves the shell between pages.
/// </summary>
/// <remarks>
/// Pages are addressed by string tag rather than by <see cref="Type"/> so that a
/// persisted setting such as "default page" survives a class being renamed or
/// moved, and so the navigation pane in XAML need not reference view types.
/// </remarks>
public interface INavigationService
{
    /// <summary>Tag of the page currently displayed, or <see langword="null"/>.</summary>
    string? CurrentTag { get; }

    /// <summary>Whether a back navigation is possible.</summary>
    bool CanGoBack { get; }

    /// <summary>Raised after a successful navigation.</summary>
    event EventHandler<string>? Navigated;

    /// <summary>Associates the service with the shell's content frame.</summary>
    void Initialize(Frame frame);

    /// <summary>
    /// Navigates to the page registered under <paramref name="tag"/>.
    /// </summary>
    /// <param name="tag">Registered page tag.</param>
    /// <returns>
    /// <see langword="true"/> if navigation occurred; <see langword="false"/> if
    /// the tag is unknown or the page is already displayed.
    /// </returns>
    bool NavigateTo(string tag);

    /// <summary>Navigates back, if possible.</summary>
    /// <returns><see langword="true"/> if a back navigation occurred.</returns>
    bool GoBack();
}
