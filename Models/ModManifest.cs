namespace DevByteLauncher.Models;

public class ModManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.21.1";

    [JsonPropertyName("fabricLoaderVersion")]
    public string FabricLoaderVersion { get; set; } = "0.16.9";

    [JsonPropertyName("serverHost")]
    public string ServerHost { get; set; } = "mc.obxodka.one";

    [JsonPropertyName("serverPort")]
    public int ServerPort { get; set; } = 25565;

    [JsonPropertyName("files")]
    public List<ModManifestItem> Files { get; set; } = [];
}

public class ModManifestItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha1")]
    public string Sha1 { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}
