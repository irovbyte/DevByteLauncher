namespace DevByteLauncher.Services;

public static class AuthService
{
    private const string MicrosoftClientId = "00000000402b5328";

    private static async Task<JELoginHandler> CreateLoginHandlerAsync()
    {
        var app = await MsalClientHelper.BuildApplicationWithCache(MicrosoftClientId);
        var msalProvider = new MsalCodeFlowProvider(app);
        var builder = new JELoginHandlerBuilder();
        _ = builder.WithOAuthProvider(msalProvider);
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
                var handler = await CreateLoginHandlerAsync();
                return await handler.AuthenticateInteractively();
            }
        }

        var nickname = string.IsNullOrWhiteSpace(settings.Nickname) ? "DevBytePlayer" : settings.Nickname.Trim();
        return MSession.CreateOfflineSession(nickname);
    }

    public static async Task<MSession> LoginMicrosoftInteractiveAsync()
    {
        var handler = await CreateLoginHandlerAsync();
        return await handler.AuthenticateInteractively();
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
