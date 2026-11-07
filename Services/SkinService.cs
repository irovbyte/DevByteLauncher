using System.Net.Http;
using System.Net.Http.Headers;

namespace DevByteLauncher.Services;

public static class SkinService
{
    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    public static Bitmap? LoadAvatarHead(string? skinPath, int targetSize = 64)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(skinPath) || !File.Exists(skinPath))
            {
                return CreateDefaultSteveHead(targetSize);
            }

            using var skinStream = File.OpenRead(skinPath);
            using var originalBitmap = SKBitmap.Decode(skinStream);
            if (originalBitmap == null)
            {
                return CreateDefaultSteveHead(targetSize);
            }

            using var headBitmap = new SKBitmap(targetSize, targetSize, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(headBitmap);
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint
            {
                IsAntialias = false
            };

            using var originalImage = SKImage.FromBitmap(originalBitmap);
            var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
            var headSrcRect = new SKRect(8, 8, 16, 16);
            var destRect = new SKRect(0, 0, targetSize, targetSize);
            canvas.DrawImage(originalImage, headSrcRect, destRect, sampling, paint);

            var hatSrcRect = new SKRect(40, 8, 48, 16);
            if (originalBitmap.Width >= 64 && originalBitmap.Height >= 32)
            {
                canvas.DrawImage(originalImage, hatSrcRect, destRect, sampling, paint);
            }

            using var image = SKImage.FromBitmap(headBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(data.ToArray());
            return new Bitmap(ms);
        }
        catch
        {
            return CreateDefaultSteveHead(targetSize);
        }
    }

    private static Bitmap CreateDefaultSteveHead(int size)
    {
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0x2d, 0x48, 0x5a));

        using var paint = new SKPaint();

        paint.Color = new SKColor(0xa7, 0x76, 0x56);
        canvas.DrawRect(0, 0, size, size, paint);

        paint.Color = new SKColor(0x45, 0x2e, 0x1d);
        canvas.DrawRect(0, 0, size, size / 4, paint);

        paint.Color = SKColors.White;
        canvas.DrawRect(size / 6, size / 2, size / 4, size / 8, paint);
        canvas.DrawRect(size - (size / 6) - (size / 4), size / 2, size / 4, size / 8, paint);

        paint.Color = new SKColor(0x35, 0x47, 0x93);
        canvas.DrawRect((size / 6) + (size / 8), size / 2, size / 8, size / 8, paint);
        canvas.DrawRect(size - (size / 6) - (size / 8), size / 2, size / 8, size / 8, paint);

        paint.Color = new SKColor(0x65, 0x36, 0x22);
        canvas.DrawRect(size / 3, size * 5 / 8, size / 3, size / 10, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }

    public static async Task<bool> UploadSkinToServerAsync(
        string host,
        int port,
        string nickname,
        string skinFilePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(skinFilePath) || !File.Exists(skinFilePath))
            {
                return false;
            }

            var cleanNickname = nickname?.Trim();
            if (string.IsNullOrWhiteSpace(cleanNickname) || cleanNickname == "Player")
            {
                return false;
            }

            var fileBytes = await File.ReadAllBytesAsync(skinFilePath, cancellationToken).ConfigureAwait(false);
            if (fileBytes.Length is 0 or > 2 * 1024 * 1024)
            {
                return false;
            }

            var url = $"http://{host}:{port}/api/skin/upload?nickname={Uri.EscapeDataString(cleanNickname)}";
            using var content = new ByteArrayContent(fileBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };

            using var resp = await s_httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static void EnsureCustomSkinLoaderConfig(string installationPath, string host, int port)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installationPath) || !Directory.Exists(installationPath))
            {
                return;
            }

            var cslDir = Path.Combine(installationPath, "CustomSkinLoader");
            var extraListDir = Path.Combine(cslDir, "ExtraList");
            _ = Directory.CreateDirectory(cslDir);
            _ = Directory.CreateDirectory(extraListDir);

            var cslConfigPath = Path.Combine(cslDir, "CustomSkinLoader.json");
            var rootUrl = $"http://{host}:{port}/csl/";

            var configJson = $$"""
            {
              "version": "15.0.1",
              "enableTransparentSkin": true,
              "enableCape": true,
              "loadlist": [
                {
                  "name": "DevByteServer",
                  "type": "CustomSkinAPI",
                  "root": "{{rootUrl}}"
                },
                {
                  "name": "Mojang",
                  "type": "MojangAPI"
                },
                {
                  "name": "ElyBy",
                  "type": "ElyByAPI"
                }
              ]
            }
            """;

            File.WriteAllText(cslConfigPath, configJson);

            var extraConfigPath = Path.Combine(extraListDir, "devbyte.json");
            var extraJson = $$"""
            {
              "name": "DevByteServer",
              "type": "CustomSkinAPI",
              "root": "{{rootUrl}}"
            }
            """;

            File.WriteAllText(extraConfigPath, extraJson);
        }
        catch
        {
        }
    }
}
