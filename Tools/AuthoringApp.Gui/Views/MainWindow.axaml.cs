using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using SafetyProto.AuthoringApp.Gui.Themes;
using SafetyProto.AuthoringApp.Gui.ViewModels;

namespace SafetyProto.AuthoringApp.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SyncThemeButton();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    /// <summary>
    /// Flips the app between the light and dark variants and remembers the choice.
    /// The palette is bound with DynamicResource throughout, so the window repaints
    /// in place — no restart, no reload of the open scenario.
    /// </summary>
    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not { } app) return;

        var next = app.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        app.RequestedThemeVariant = next;
        ThemePreference.Save(next);
        SyncThemeButton();
    }

    /// <summary>Labels the toggle with the variant currently on screen.</summary>
    private void SyncThemeButton()
    {
        var button = this.FindControl<Button>("ThemeButton");
        if (button == null) return;

        button.Content = Application.Current?.ActualThemeVariant == ThemeVariant.Dark
            ? "Tema: escuro"
            : "Tema: claro";
    }

    /// <summary>Refreshes the EPIs tab whenever a tab is shown, so PPE ticked on a task since the
    /// last visit is listed. SelectionChanged bubbles up from every ListBox and ComboBox inside
    /// the tabs too; only the TabControl's own change counts.</summary>
    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender)) Vm?.RefreshPpeList();
    }

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

    private void OnAddReportOption(object? sender, RoutedEventArgs e) =>
        (Vm?.Editor?.SelectedNode as TaskViewModel)?.AddReportOption();

    private void OnRemoveReportOption(object? sender, RoutedEventArgs e)
    {
        if ((Vm?.Editor?.SelectedNode as TaskViewModel) is not { } task) return;
        if ((sender as Control)?.DataContext is ReportOptionViewModel option)
            task.RemoveReportOption(option);
    }

    private void OnAddDistractor(object? sender, RoutedEventArgs e) => Vm?.Editor?.AddDistractor();

    private void OnRemoveDistractor(object? sender, RoutedEventArgs e) => Vm?.Editor?.RemoveSelectedDistractor();

    private void OnLinkDistractor(object? sender, RoutedEventArgs e) =>
        (Vm?.Editor?.SelectedNode as TaskViewModel)?.LinkDistractor();

    private void OnUnlinkDistractor(object? sender, RoutedEventArgs e)
    {
        if ((Vm?.Editor?.SelectedNode as TaskViewModel) is not { } task) return;
        if ((sender as Control)?.DataContext is DistractorViewModel distractor)
            task.UnlinkDistractor(distractor);
    }

    private void OnAddFinishTip(object? sender, RoutedEventArgs e) => Vm?.Editor?.AddFinishTip();

    private void OnRemoveFinishTip(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FinishTipViewModel tip)
            Vm?.Editor?.RemoveFinishTip(tip);
    }

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
