using Avalonia.Controls;
using HttpMonitorApp.ViewModels;

namespace HttpMonitorApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}