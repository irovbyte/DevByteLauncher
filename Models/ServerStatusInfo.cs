namespace DevByteLauncher.Models;

public class ServerStatusInfo
{
    public bool IsOnline { get; set; }
    public int OnlinePlayers { get; set; }
    public int MaxPlayers { get; set; }
    public string Motd { get; set; } = "DevByte Homelab Fabric 1.21.1";
    public long LatencyMs { get; set; }
    public string Version { get; set; } = "1.21.1";

    public string StatusColor => IsOnline ? "#4ade80" : "#ef4444";

    public string StatusBadgeText => IsOnline
        ? $"Онлайн • {OnlinePlayers}/{MaxPlayers} игроков • {LatencyMs} ms"
        : "Сервер оффлайн (или запускается)";
}
