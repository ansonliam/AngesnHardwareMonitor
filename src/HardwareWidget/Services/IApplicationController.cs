namespace HardwareWidget.Services;

/// <summary>
/// What the tray icon is allowed to ask of the application. Mirrors the AI Usage Monitor's
/// controller seam so the tray service has no direct dependency on App or on any window.
/// </summary>
public interface IApplicationController
{
    bool IsWidgetVisible();

    void ShowWidget();

    void HideWidget();

    void ShowSettings();

    /// <summary>Named to avoid colliding with WPF's Application.Exit event.</summary>
    void ExitApplication();
}
