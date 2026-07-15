using Avalonia.Controls;
using WiFiHealthConsole.App.ViewModels;

namespace WiFiHealthConsole.App.Views.Pages;

public partial class HistoryPage : UserControl
{
    public HistoryPage() => InitializeComponent();

    private async void MarkerSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (sender is not ComboBox
            {
                DataContext: HistoryRowViewModel row,
                SelectedItem: HistoryMarkerOptionViewModel option,
            }
            || DataContext is not HistoryPageViewModel viewModel)
        {
            return;
        }

        await viewModel.SetMarkerAsync(row.Id, option.Value);
    }
}
