using System.Text.Json;
using IdCard.Domain.Models;
using SkiaSharp;

namespace IdCard.Api.Services;

public interface ICardRenderService
{
    Task<(string? frontPath, string? backPath)> RenderAsync(
        CardDesign design,
        IDictionary<string, object?> record,
        string outputDirectory,
        Guid jobId,
        bool dualSided,
        CancellationToken ct = default);
}

public class CardRenderService : ICardRenderService
{
    // CR80 at 300 DPI ≈ 1012 x 638 px
    private const int Width = 1012;
    private const int Height = 638;

    public async Task<(string? frontPath, string? backPath)> RenderAsync(
        CardDesign design,
        IDictionary<string, object?> record,
        string outputDirectory,
        Guid jobId,
        bool dualSided,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var frontPath = Path.Combine(outputDirectory, $"{jobId}_front.png");
        await Task.Run(() => RenderSide(design.Front, record, frontPath), ct);
        string? backPath = null;
        if (dualSided)
        {
            backPath = Path.Combine(outputDirectory, $"{jobId}_back.png");
            await Task.Run(() => RenderSide(design.Back, record, backPath), ct);
        }
        return (frontPath, backPath);
    }

    private static void RenderSide(CardSideDesign side, IDictionary<string, object?> record, string path)
    {
        using var bitmap = new SKBitmap(Width, Height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ParseColor(side.BackgroundColor ?? "#FFFFFF"));

        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

        if (!string.IsNullOrWhiteSpace(side.BackgroundImage) && File.Exists(side.BackgroundImage))
        {
            using var bg = SKBitmap.Decode(side.BackgroundImage);
            if (bg is not null)
                canvas.DrawBitmap(bg, SKRect.Create(0, 0, Width, Height), sampling);
        }

        foreach (var el in side.Elements.OrderBy(e => e.ZIndex))
        {
            var x = (float)el.X;
            var y = (float)el.Y;
            var w = (float)Math.Max(1, el.W);
            var h = (float)Math.Max(1, el.H);
            var rect = SKRect.Create(x, y, w, h);

            switch (el.Type.ToLowerInvariant())
            {
                case "rect":
                    using (var paint = new SKPaint { Color = ParseColor(el.Color), IsAntialias = true })
                        canvas.DrawRect(rect, paint);
                    break;
                case "text":
                {
                    var text = ResolveText(el, record);
                    using var font = new SKFont(SKTypeface.FromFamilyName(el.FontFamily) ?? SKTypeface.Default, (float)el.FontSize);
                    using var paint = new SKPaint { Color = ParseColor(el.Color), IsAntialias = true };
                    canvas.DrawText(text, x, y + font.Size, SKTextAlign.Left, font, paint);
                    break;
                }
                case "photo":
                case "image":
                {
                    var src = el.Type.Equals("photo", StringComparison.OrdinalIgnoreCase)
                        ? ResolvePhotoPath(el.Field, record)
                        : el.Src;
                    if (!string.IsNullOrWhiteSpace(src) && File.Exists(src))
                    {
                        using var photo = SKBitmap.Decode(src);
                        if (photo is not null)
                            canvas.DrawBitmap(photo, rect, sampling);
                    }
                    else
                    {
                        using var paint = new SKPaint { Color = SKColors.LightGray };
                        canvas.DrawRect(rect, paint);
                    }
                    break;
                }
                case "qr":
                case "barcode":
                {
                    var value = ResolveText(el, record);
                    using var fill = new SKPaint { Color = SKColors.White };
                    using var border = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
                    using var font = new SKFont(SKTypeface.Default, 12);
                    using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
                    canvas.DrawRect(rect, fill);
                    canvas.DrawRect(rect, border);
                    canvas.DrawText(value, x + 4, y + h / 2 + 4, SKTextAlign.Left, font, textPaint);
                    break;
                }
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.OpenWrite(path);
        data.SaveTo(fs);
    }

    private static SKColor ParseColor(string color)
    {
        var hex = color.TrimStart('#');
        if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return new SKColor((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
        return SKColors.Black;
    }

    private static string ResolveText(DesignElement el, IDictionary<string, object?> record)
    {
        if (!string.IsNullOrWhiteSpace(el.Field) && record.TryGetValue(el.Field, out var value) && value is not null)
            return value.ToString() ?? string.Empty;
        return el.Text ?? string.Empty;
    }

    private static string? ResolvePhotoPath(string? field, IDictionary<string, object?> record)
    {
        if (string.IsNullOrWhiteSpace(field)) return null;
        if (!record.TryGetValue(field, out var value) || value is null) return null;
        return value.ToString();
    }

    public static CardDesign ParseDesign(string json)
    {
        return JsonSerializer.Deserialize<CardDesign>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new CardDesign();
    }
}
