namespace DevByteLauncher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        if (this.FindControl<Border>("TitleBarArea") is { } titleBar)
        {
            titleBar.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    BeginMoveDrag(e);
                }
            };
        }
    }

    private async void OnBrowseFolderClickedAsync(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите диск или папку для установки Minecraft",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is MainViewModel vm)
        {
            var selectedPath = folders[0].Path.LocalPath;
            vm.InstallationPath = selectedPath;
        }
    }

    private async void OnSelectSkinClickedAsync(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите файл скина Minecraft (PNG)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("PNG Images")
                {
                    Patterns = ["*.png"]
                }
            ]
        });

        if (files.Count > 0 && DataContext is MainViewModel vm)
        {
            vm.Settings.SelectedSkinPath = files[0].Path.LocalPath;
            vm.SaveSettings();
            vm.ReloadAvatar();
        }
    }
}
