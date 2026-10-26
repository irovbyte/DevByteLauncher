namespace DevByteLauncher.Services;

public static class SkinService
{
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
}
