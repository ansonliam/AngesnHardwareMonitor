using System.Windows;
using HardwareWidget.Services;
using HardwareWidget.ViewModels;

namespace HardwareWidget.Views;

/// <summary>
/// A plain Window rather than a MahApps MetroWindow: the application theme is Dark.Teal and this
/// dialog is deliberately light, matching the AI Usage Monitor's Settings window.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsService settings)
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(settings, Close);
    }
}
