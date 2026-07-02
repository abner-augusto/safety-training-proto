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
        var path = await PickJsonAsync("Abrir cenário", Vm?.SuggestedScenarioDirectory);
        if (path != null) Vm?.LoadScenario(path);
    }

    private async void OnOpenCatalog(object? sender, RoutedEventArgs e)
    {
        var path = await PickJsonAsync("Abrir catálogo de capacidades", Vm?.SuggestedCapabilityCatalogDirectory);
        if (path != null) Vm?.LoadCatalog(path);
    }

    private async void OnOpenActionCatalog(object? sender, RoutedEventArgs e)
    {
        var path = await PickJsonAsync("Abrir catálogo de ações", Vm?.SuggestedActionCatalogDirectory);
        if (path != null) Vm?.LoadActionCatalog(path);
    }

    private void OnNewActionCatalog(object? sender, RoutedEventArgs e) => Vm?.NewActionCatalog();

    private void OnValidate(object? sender, RoutedEventArgs e) => Vm?.Validate();

    private void OnAddGroup(object? sender, RoutedEventArgs e) => Vm?.Editor?.AddGroup();
    private void OnAddTask(object? sender, RoutedEventArgs e) => Vm?.Editor?.AddTask();
    private void OnRemove(object? sender, RoutedEventArgs e) => Vm?.Editor?.RemoveSelected();

    private void OnAddAction(object? sender, RoutedEventArgs e) => Vm?.AddAction();
    private void OnRemoveAction(object? sender, RoutedEventArgs e) => Vm?.RemoveSelectedAction();
    private void OnValidateActionCatalog(object? sender, RoutedEventArgs e) => Vm?.ValidateActionCatalog();

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var path = await PickSaveJsonAsync();
        if (path != null) Vm?.Save(path);
    }

    private async void OnSaveActionCatalog(object? sender, RoutedEventArgs e)
    {
        var path = await PickSaveJsonAsync("Salvar catálogo de ações", "actions.json");
        if (path != null) Vm?.SaveActionCatalog(path);
    }

    private async void OnDeploy(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.DeployAsync();
    }

    private async Task<string?> PickJsonAsync(string title, string? suggestedDirectory = null)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = JsonFilters(),
            SuggestedStartLocation = await TryGetFolderAsync(suggestedDirectory),
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task<string?> PickSaveJsonAsync(string title = "Salvar cenário", string suggestedFileName = "default.json")
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = "json",
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = JsonFilters(),
        });

        return file?.Path.LocalPath;
    }

    private async Task<IStorageFolder?> TryGetFolderAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            return await StorageProvider.TryGetFolderFromPathAsync(path);
        }
        catch
        {
            return null;
        }
    }

    private static List<FilePickerFileType> JsonFilters() => new()
    {
        new("JSON") { Patterns = new[] { "*.json" } },
        new("Todos os arquivos") { Patterns = new[] { "*" } },
    };
}
