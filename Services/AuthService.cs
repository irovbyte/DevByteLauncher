namespace DevByteLauncher.Services;

public static class AuthService
{
    public static async Task<MSession> AuthenticateAsync(AppSettings settings)
    {
        if (settings.IsMicrosoftAuth)
        {
            try
            {
                var handler = JELoginHandlerBuilder.BuildDefault();
                return await handler.Authenticate();
            }
            catch
            {
                var handler = JELoginHandlerBuilder.BuildDefault();
                return await handler.AuthenticateInteractively();
            }
        }

        var nickname = string.IsNullOrWhiteSpace(settings.Nickname) ? "DevBytePlayer" : settings.Nickname.Trim();
        return MSession.CreateOfflineSession(nickname);
    }

    public static async Task<MSession> LoginMicrosoftInteractiveAsync()
    {
        var handler = JELoginHandlerBuilder.BuildDefault();
        return await handler.AuthenticateInteractively();
    }

    public static async Task SignoutMicrosoftAsync()
    {
        try
        {
            var handler = JELoginHandlerBuilder.BuildDefault();
            await handler.Signout();
        }
        catch
        {
        }
    }
}
