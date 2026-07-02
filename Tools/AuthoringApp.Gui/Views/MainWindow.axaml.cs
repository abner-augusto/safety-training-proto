using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using SafetyProto.AuthoringApp.Gui.ViewModels;

namespace SafetyProto.AuthoringApp.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private async void OnOpenScenario(object? sender, RoutedEventArgs e)
    {
        var path = await PickJsonAsync("Abrir cenário");
        if (path != null) Vm?.LoadScenario(path);
    }

    private async void OnOpenCatalog(object? sender, RoutedEventArgs e)
    {
        var path = await PickJsonAsync("Abrir catálogo de capacidades");
        if (path != null) Vm?.LoadCatalog(path);
    }

    private void OnValidate(object? sender, RoutedEventArgs e) => Vm?.Validate();

    private async Task<string?> PickJsonAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("JSON") { Patterns = new[] { "*.json" } },
                new("Todos os arquivos") { Patterns = new[] { "*" } },
            },
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
