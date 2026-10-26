namespace DevByteLauncher.ViewModels;

public partial class ModItemViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FileName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FilePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SizeFormatted { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = "Синхронизирован";

    [ObservableProperty]
    public partial bool IsInstalled { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBackground))]
    [NotifyPropertyChangedFor(nameof(StatusForeground))]
    public partial bool IsEnabled { get; set; } = true;

    public string StatusBackground => IsEnabled ? "#1b3d2f" : "#3d2222";
    public string StatusForeground => IsEnabled ? "#4ade80" : "#f87171";
}
