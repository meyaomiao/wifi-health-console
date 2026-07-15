using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace WiFiHealthConsole.App.Views.Pages;

public partial class SpeedTestPage : UserControl
{
    public SpeedTestPage()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (!double.TryParse(
                    Environment.GetEnvironmentVariable("WIFI_HEALTH_PREVIEW_SCROLL_OFFSET"),
                    out var offset))
            {
                return;
            }

            Dispatcher.UIThread.Post(
                () => PageScroller.Offset = new Vector(0, Math.Max(0, offset)),
                DispatcherPriority.Background);
        };
    }
}
