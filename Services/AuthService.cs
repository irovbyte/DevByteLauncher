using System.Net.Http.Headers;
using System.Text.Json;
using XboxAuthNet.Game.SessionStorages;

namespace DevByteLauncher.Services;

public static class AuthService
{
    private const string MicrosoftClientId = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb";
    private static readonly HttpClient t_httpClient = new();

    private static async Task<JELoginHandler> CreateLoginHandlerAsync(Func<string, string, Task>? onDeviceCode = null)
    {
        var app = await MsalClientHelper.BuildApplicationWithCache(MicrosoftClientId);
        var msalOAuth = new MsalOAuthBuilder(app);

        var msalAccounts = await app.GetAccountsAsync();
        var account = msalAccounts.FirstOrDefault();
        if (account != null)
        {
            msalOAuth.LoginHintSource = new InMemorySessionSource<string>(account.Username);
            msalOAuth.HomeAccountIdSource = new InMemorySessionSource<string>(account.HomeAccountId.Identifier);
        }

        var provider = new MsalDeviceCodeProvider(msalOAuth, result =>
            onDeviceCode != null
                ? onDeviceCode(result.UserCode, result.VerificationUrl)
                : Task.CompletedTask);

        var builder = new JELoginHandlerBuilder();
        _ = builder.WithOAuthProvider(provider);
        return builder.Build();
    }

    public static async Task<MSession> AuthenticateAsync(AppSettings settings)
    {
        if (settings.IsMicrosoftAuth)
        {
            try
            {
                var handler = await CreateLoginHandlerAsync();
                return await handler.AuthenticateSilently();
            }
            catch
            {
            }
        }

        var nickname = string.IsNullOrWhiteSpace(settings.Nickname) ? "DevBytePlayer" : settings.Nickname.Trim();
        return MSession.CreateOfflineSession(nickname);
    }

    public static async Task<MSession> LoginMicrosoftDeviceCodeAsync(Func<string, string, Task> onDeviceCodeReceived, CancellationToken cancellationToken = default)
    {
        var handler = await CreateLoginHandlerAsync(onDeviceCodeReceived);
        return await handler.AuthenticateInteractively(cancellationToken);
    }

    public static async Task SignoutMicrosoftAsync()
    {
        try
        {
            var app = await MsalClientHelper.BuildApplicationWithCache(MicrosoftClientId);
            await MsalClientHelper.RemoveAccounts(app);
            var handler = await CreateLoginHandlerAsync();
            await handler.Signout();
        }
        catch
        {
        }
    }

    public static async Task<(string? skinUrl, string variant)> GetActiveSkinAsync(string accessToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var res = await t_httpClient.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                return (null, "classic");
            }

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("skins", out var skinsElem) && skinsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in skinsElem.EnumerateArray())
                {
                    var state = s.TryGetProperty("state", out var st) ? st.GetString() : null;
                    if (string.Equals(state, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    {
                        var url = s.TryGetProperty("url", out var u) ? u.GetString() : null;
                        var variant = s.TryGetProperty("variant", out var v) ? v.GetString() ?? "classic" : "classic";
                        return (url, variant);
                    }
                }
            }
        }
        catch
        {
        }

        return (null, "classic");
    }

    public static async Task<bool> DownloadSkinAsync(string skinUrl, string destinationPath)
    {
        try
        {
            var bytes = await t_httpClient.GetByteArrayAsync(skinUrl);
            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                _ = Directory.CreateDirectory(dir);
            }
            await File.WriteAllBytesAsync(destinationPath, bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> UploadSkinToCloudAsync(string accessToken, string skinFilePath, string variant = "classic")
    {
        try
        {
            if (!File.Exists(skinFilePath))
            {
                return false;
            }

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(variant.ToLowerInvariant()), "variant");

            var fileBytes = await File.ReadAllBytesAsync(skinFilePath);
            var imageContent = new ByteArrayContent(fileBytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(imageContent, "file", Path.GetFileName(skinFilePath));

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.minecraftservices.com/minecraft/profile/skins")
            {
                Content = form
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var res = await t_httpClient.SendAsync(req);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}