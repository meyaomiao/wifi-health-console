using Avalonia.Controls;
using WiFiHealthConsole.App.ViewModels;

namespace WiFiHealthConsole.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsMacOS())
        {
            TitleBarHost.Margin = new Avalonia.Thickness(20, 0, 20, 0);
        }
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        };
    }
}
