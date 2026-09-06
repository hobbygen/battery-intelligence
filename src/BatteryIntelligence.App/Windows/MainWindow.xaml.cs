using BatteryIntelligence.App.Services;
using BatteryIntelligence.App.ViewModels;
using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace BatteryIntelligence.App.Windows;

/// <summary>
/// The application shell: navigation pane, content frame and title bar.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly IWindowStateService _windowState;
    private readonly ISettingsService _settings;
    private readonly AppVisibilityState _visibility;

    /// <summary>Set when the user chooses Exit, so the close is not turned into a hide.</summary>
    private bool _isExiting;

    public MainWindow(
        ILogger<MainWindow> logger,
        INavigationService navigation,
        IThemeService theme,
        IWindowStateService windowState,
        ISettingsService settings,
        AppVisibilityState visibility,
        ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(windowState);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(visibility);
        ArgumentNullException.ThrowIfNull(viewModel);

        _logger = logger;
        _navigation = navigation;
        _theme = theme;
        _windowState = windowState;
        _settings = settings;
        _visibility = visibility;
        ViewModel = viewModel;

        InitializeComponent();

        Title = ShellViewModel.AppTitle;

        ConfigureTitleBar();
        ConfigureMinimumSize();

        _theme.Initialize(this);
        _windowState.Restore(this);

        _navigation.Initialize(ContentFrame);
        _navigation.Navigated += OnNavigated;

        NavView.IsPaneOpen = ViewModel.IsPaneOpen;
        NavView.PaneClosing += (_, _) => ViewModel.IsPaneOpen = false;
        NavView.PaneOpening += (_, _) => ViewModel.IsPaneOpen = true;

        RegisterNavigationAccelerators();
        ConfigureTrayIcon();

        AppWindow.Closing += OnAppWindowClosing;
        Closed += OnClosed;

        // Feeds the adaptive-sampling "window hidden → chart feed suspended" case
        // (docs/monitoring-dataflow.md section 3). AppWindow.Hide() for the tray
        // raises this with Visible == false; showing it again raises it true.
        VisibilityChanged += (_, e) => _visibility.SetVisible(e.Visible);

        NavigateToStartupPage();
    }

    /// <summary>Shell state.</summary>
    public ShellViewModel ViewModel { get; }

    /// <summary>Shows and foregrounds the window, restoring it if minimized.</summary>
    public void BringToFront()
    {
        AppWindow.Show();

        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    /// <summary>
    /// Starts without showing the window, for the "start minimized" setting.
    /// </summary>
    /// <remarks>
    /// The window is created but never activated, so monitoring can run while the
    /// user sees nothing. Once the notification-area icon exists this is how the
    /// application launches at logon.
    /// </remarks>
    public void LaunchHidden()
    {
        _logger.LogInformation("Starting without showing the main window.");
        AppWindow.Hide();
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void ConfigureMinimumSize()
    {
        // AppWindow has no minimum-size property, so the constraint is applied by
        // correcting any resize that goes below it. See docs/ui-navigation.md
        // section 3: content must reflow rather than clip.
        AppWindow.Changed += OnAppWindowChanged;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange)
        {
            return;
        }

        int width = Math.Max(sender.Size.Width, WindowStateSettings.MinimumWidth);
        int height = Math.Max(sender.Size.Height, WindowStateSettings.MinimumHeight);

        if (width != sender.Size.Width || height != sender.Size.Height)
        {
            // Fully qualified: this file's namespace ends in ".Windows", which
            // would otherwise shadow the global Windows namespace.
            sender.Resize(new global::Windows.Graphics.SizeInt32(width, height));
        }
    }

    private void NavigateToStartupPage()
    {
        string tag = ViewModel.DefaultPageTag;

        if (!_navigation.NavigateTo(tag))
        {
            // A stale or hand-edited default page must not leave the shell blank.
            _logger.LogWarning("Default page {Tag} could not be shown; falling back to Dashboard.", tag);
            _navigation.NavigateTo("Dashboard");
        }

        SelectNavigationItem(_navigation.CurrentTag);
    }

    private void OnNavigationSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        _ = sender;

        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            _navigation.NavigateTo(tag);
        }
    }

    private void OnNavigationBackRequested(
        NavigationView sender,
        NavigationViewBackRequestedEventArgs args)
    {
        _ = sender;
        _ = args;

        _navigation.GoBack();
    }

    private void OnNavigated(object? sender, string tag)
    {
        _ = sender;

        NavView.IsBackEnabled = _navigation.CanGoBack;
        SelectNavigationItem(tag);
    }

    /// <summary>
    /// Keeps the pane selection in step with the frame, including after a back
    /// navigation or a programmatic jump.
    /// </summary>
    private void SelectNavigationItem(string? tag)
    {
        if (tag is null)
        {
            return;
        }

        foreach (object item in NavView.MenuItems.Concat(NavView.FooterMenuItems))
        {
            if (item is NavigationViewItem navItem &&
                string.Equals(navItem.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                if (!ReferenceEquals(NavView.SelectedItem, navItem))
                {
                    NavView.SelectedItem = navItem;
                }

                return;
            }
        }
    }

    /// <summary>
    /// Registers Ctrl+1 through Ctrl+9 for the primary navigation items.
    /// Specification section 6 requires keyboard navigation.
    /// </summary>
    private void RegisterNavigationAccelerators()
    {
        string[] tags =
        [
            "Dashboard", "Battery", "Sessions", "Power", "Temperature",
            "AppUsage", "Statistics", "History", "Alerts",
        ];

        for (int i = 0; i < tags.Length; i++)
        {
            string tag = tags[i];

            KeyboardAccelerator accelerator = new()
            {
                Modifiers = VirtualKeyModifiers.Control,
                Key = VirtualKey.Number1 + i,
            };

            accelerator.Invoked += (_, args) =>
            {
                args.Handled = true;
                _navigation.NavigateTo(tag);
            };

            RootGrid.KeyboardAccelerators.Add(accelerator);
        }
    }

    private void ConfigureTrayIcon()
    {
        // Create the icon eagerly. When the application starts minimized the
        // window is never shown, and without this the only way back to the UI
        // would not exist.
        TrayIcon.ForceCreate();
        TrayIcon.LeftClickCommand = new RelayCommand(BringToFront);
    }

    private void OnThemeToggleClick(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;

        // Resolve the effective theme, then flip it. "System" collapses to whichever
        // side it is currently showing.
        ElementTheme actual = (Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Light;
        ThemePreference next = actual == ElementTheme.Dark ? ThemePreference.Light : ThemePreference.Dark;

        _theme.Apply(next);
        _ = _settings.UpdateAsync(s => s.Appearance.Theme = next, "Appearance");
    }

    private void OnSettingsButtonClick(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        _navigation.NavigateTo("Settings");
    }

    private void OnTrayOpenClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;

        BringToFront();
    }

    private void OnTraySettingsClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;

        BringToFront();
        _navigation.NavigateTo("Settings");
    }

    private void OnTrayExitClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;

        _logger.LogInformation("Exit requested from the notification area.");
        _isExiting = true;
        Close();
    }

    /// <summary>
    /// Turns a window close into a hide when the user has asked the application
    /// to keep running in the notification area.
    /// </summary>
    /// <remarks>
    /// Specification section 22 makes minimize-to-tray the default close
    /// behaviour, because monitoring that stops when the window is dismissed
    /// would leave gaps in exactly the history the application exists to build.
    /// </remarks>
    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        _ = sender;

        if (_isExiting || !_settings.Current.General.MinimizeToTray)
        {
            return;
        }

        args.Cancel = true;

        // Geometry is captured now rather than at exit: the window may never be
        // shown again before the process ends.
        _ = _windowState.PersistAsync(this);

        AppWindow.Hide();
        _logger.LogInformation("Window hidden to the notification area; monitoring continues.");
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _ = sender;
        _ = args;

        // Persisting geometry has to complete before the window is gone, so this
        // one shutdown step is awaited synchronously. It writes a single small
        // file.
        _windowState.PersistAsync(this).GetAwaiter().GetResult();

        TrayIcon.Dispose();
    }
}
