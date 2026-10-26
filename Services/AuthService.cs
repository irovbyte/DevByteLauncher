namespace DevByteLauncher.Services;

public static class AuthService
{
    private const string MicrosoftClientId = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb";

    private static async Task<JELoginHandler> CreateLoginHandlerAsync(Func<string, string, Task>? onDeviceCode = null)
    {
        var app = await MsalClientHelper.BuildApplicationWithCache(MicrosoftClientId);
        var provider = new MsalDeviceCodeProvider(app, result =>
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
}
