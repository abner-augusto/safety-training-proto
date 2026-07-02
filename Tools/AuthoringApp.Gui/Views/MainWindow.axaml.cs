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

    private void OnNew(object? sender, RoutedEventArgs e) => Vm?.NewScenario();

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

    private void OnAddGroup(object? sender, RoutedEventArgs e) => Vm?.Editor?.AddGroup();
    private void OnAddTask(object? sender, RoutedEventArgs e) => Vm?.Editor?.AddTask();
    private void OnRemove(object? sender, RoutedEventArgs e) => Vm?.Editor?.RemoveSelected();

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var path = await PickSaveJsonAsync();
        if (path != null) Vm?.Save(path);
    }

    private async void OnDeploy(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.DeployAsync();
    }

    private async Task<string?> PickJsonAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = JsonFilters(),
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task<string?> PickSaveJsonAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar cenário",
            DefaultExtension = "json",
            SuggestedFileName = "default.json",
            FileTypeChoices = JsonFilters(),
        });

        return file?.Path.LocalPath;
    }

    private static List<FilePickerFileType> JsonFilters() => new()
    {
        new("JSON") { Patterns = new[] { "*.json" } },
        new("Todos os arquivos") { Patterns = new[] { "*" } },
    };
}
