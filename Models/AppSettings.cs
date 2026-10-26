namespace DevByteLauncher.Models;

public class AppSettings
{
    public string InstallationPath { get; set; } = string.Empty;
    public string Nickname { get; set; } = "Player";
    public bool IsMicrosoftAuth { get; set; }
    public int AllocatedRamMb { get; set; } = 6144;
    public string CustomJavaPath { get; set; } = string.Empty;
    public bool AutoConnectServer { get; set; } = true;
    public string ServerHost { get; set; } = "mc.obxodka.one";
    public int ServerPort { get; set; } = 25565;
    public int SyncPort { get; set; } = 25566;
    public string? SelectedSkinPath { get; set; }
}
