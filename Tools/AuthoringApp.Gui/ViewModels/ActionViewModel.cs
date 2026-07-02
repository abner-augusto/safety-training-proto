using System.Collections.Generic;
using System.Linq;
using SafetyProto.Domain.Actions;

namespace SafetyProto.AuthoringApp.Gui.ViewModels;

public sealed class ActionViewModel : ViewModelBase
{
    private string _actionId;
    private string _displayName;
    private string _description;
    private string _category;
    private string _telemetryName;
    private string _tagsCsv;
    private string _regulatoryRefsCsv;
    private bool _isSafetyCritical;
    private bool _isHiddenInUI;
    private float _cooldownSeconds;
    private float _expectedDurationSeconds;
    private int _baseScore;
    private int _severity;

    public ActionViewModel(ActionDef def)
    {
        _actionId = def.ActionId;
        _displayName = def.DisplayName;
        _description = def.Description;
        _category = def.Category;
        _telemetryName = def.TelemetryName;
        _tagsCsv = string.Join(", ", def.Tags ?? new List<string>());
        _regulatoryRefsCsv = string.Join(", ", def.RegulatoryRefs ?? new List<string>());
        _isSafetyCritical = def.IsSafetyCritical;
        _isHiddenInUI = def.IsHiddenInUI;
        _cooldownSeconds = def.CooldownSeconds;
        _expectedDurationSeconds = def.ExpectedDurationSeconds;
        _baseScore = def.BaseScore;
        _severity = def.Severity;
    }

    public string ActionId
    {
        get => _actionId;
        set
        {
            var previous = _actionId;
            if (!SetField(ref _actionId, value)) return;

            if (string.IsNullOrWhiteSpace(_telemetryName) || string.Equals(_telemetryName, previous, System.StringComparison.OrdinalIgnoreCase))
            {
                _telemetryName = _actionId;
                OnPropertyChanged(nameof(TelemetryName));
            }

            OnPropertyChanged(nameof(TreeLabel));
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set { if (SetField(ref _displayName, value)) OnPropertyChanged(nameof(TreeLabel)); }
    }

    public string Description { get => _description; set => SetField(ref _description, value); }
    public string Category { get => _category; set => SetField(ref _category, value); }
    public string TelemetryName { get => _telemetryName; set => SetField(ref _telemetryName, value); }
    public string TagsCsv { get => _tagsCsv; set => SetField(ref _tagsCsv, value); }
    public string RegulatoryRefsCsv { get => _regulatoryRefsCsv; set => SetField(ref _regulatoryRefsCsv, value); }
    public bool IsSafetyCritical { get => _isSafetyCritical; set => SetField(ref _isSafetyCritical, value); }
    public bool IsHiddenInUI { get => _isHiddenInUI; set => SetField(ref _isHiddenInUI, value); }
    public float CooldownSeconds { get => _cooldownSeconds; set => SetField(ref _cooldownSeconds, value); }
    public float ExpectedDurationSeconds { get => _expectedDurationSeconds; set => SetField(ref _expectedDurationSeconds, value); }
    public int BaseScore { get => _baseScore; set => SetField(ref _baseScore, value); }
    public int Severity { get => _severity; set => SetField(ref _severity, value); }

    public string TreeLabel => string.IsNullOrWhiteSpace(ActionId) ? "(ação sem id)" : ActionId;

    public ActionDef ToDef()
    {
        var actionId = (ActionId ?? string.Empty).Trim();
        var telemetryName = string.IsNullOrWhiteSpace(TelemetryName) ? actionId : TelemetryName.Trim();

        return new ActionDef
        {
            ActionId = actionId,
            DisplayName = (DisplayName ?? string.Empty).Trim(),
            Description = Description ?? string.Empty,
            Category = (Category ?? string.Empty).Trim(),
            TelemetryName = telemetryName,
            Tags = SplitCsv(TagsCsv),
            RegulatoryRefs = SplitCsv(RegulatoryRefsCsv),
            IsSafetyCritical = IsSafetyCritical,
            IsHiddenInUI = IsHiddenInUI,
            CooldownSeconds = CooldownSeconds < 0f ? 0f : CooldownSeconds,
            ExpectedDurationSeconds = ExpectedDurationSeconds < 0f ? 0f : ExpectedDurationSeconds,
            BaseScore = BaseScore,
            Severity = Severity,
        };
    }

    private static List<string> SplitCsv(string value) =>
        (value ?? string.Empty)
        .Split(',')
        .Select(s => s.Trim())
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Distinct(System.StringComparer.OrdinalIgnoreCase)
        .ToList();
}
