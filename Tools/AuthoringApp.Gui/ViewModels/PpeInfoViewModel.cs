namespace SafetyProto.AuthoringApp.Gui.ViewModels;

/// <summary>Read-only row of the EPIs tab: one PPE type the build implements, the name the
/// participant sees for it, and the open scenario's tasks that require it.</summary>
public sealed record PpeInfoViewModel(string TypeId, string DisplayName, string RequiredBy);
