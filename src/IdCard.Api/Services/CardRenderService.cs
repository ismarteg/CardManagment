using System.Text.Json;
using IdCard.Domain.Models;
using QRCoder;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

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
    private readonly string _contentRoot;
    private readonly string _uploadsRoot;

    public CardRenderService(IWebHostEnvironment env)
    {
        _contentRoot = env.ContentRootPath;
        _uploadsRoot = Path.Combine(_contentRoot, "uploads");
        Directory.CreateDirectory(_uploadsRoot);
    }

    public async Task<(string? frontPath, string? backPath)> RenderAsync(
        CardDesign design,
        IDictionary<string, object?> record,
        string outputDirectory,
        Guid jobId,
        bool dualSided,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);
        MaterializeDesignImages(design);
        var typefaces = LoadCustomTypefaces(design);
        try
        {
            var frontPath = Path.Combine(outputDirectory, $"{jobId}_front.png");
            await Task.Run(() => RenderSide(design.Front, record, frontPath, typefaces), ct);
            string? backPath = null;
            if (dualSided)
            {
                backPath = Path.Combine(outputDirectory, $"{jobId}_back.png");
                await Task.Run(() => RenderSide(design.Back, record, backPath, typefaces), ct);
            }
            return (frontPath, backPath);
        }
        finally
        {
            foreach (var tf in typefaces.Values)
                tf.Dispose();
        }
    }

    /// <summary>
    /// Converts data-URL /files/uploads design images into absolute local paths for Skia.
    /// </summary>
    private void MaterializeDesignImages(CardDesign design)
    {
        foreach (var side in new[] { design.Front, design.Back })
        {
            foreach (var el in side.Elements)
            {
                if (!el.Type.Equals("image", StringComparison.OrdinalIgnoreCase) &&
                    !el.Type.Equals("photo", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Static design images use Src; record photos are resolved via Field.
                if (string.IsNullOrWhiteSpace(el.Src)) continue;
                el.Src = ResolveToLocalPath(el.Src);
            }
        }
    }

    private string? ResolveToLocalPath(string src)
    {
        if (string.IsNullOrWhiteSpace(src)) return src;

        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = DecodeDataUrl(src);
            if (bytes is null || bytes.Length == 0) return null;
            var cacheDir = Path.Combine(_uploadsRoot, "design-cache");
            Directory.CreateDirectory(cacheDir);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16].ToLowerInvariant();
            var ext = GuessImageExtension(src);
            var path = Path.Combine(cacheDir, hash + ext);
            if (!File.Exists(path))
                File.WriteAllBytes(path, bytes);
            return path;
        }

        if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(src);
                // http://localhost:5080/files/uploads/... → /files/uploads/...
                if (uri.AbsolutePath.StartsWith("/files/uploads/", StringComparison.OrdinalIgnoreCase))
                    src = uri.AbsolutePath;
                else
                    return src;
            }
            catch
            {
                return src;
            }
        }

        if (src.StartsWith("/files/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            var relative = src["/files/uploads/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var abs = Path.Combine(_uploadsRoot, relative);
            return File.Exists(abs) ? abs : null;
        }

        return File.Exists(src) ? src : src;
    }

    private static byte[]? DecodeDataUrl(string src)
    {
        var comma = src.IndexOf(',');
        if (comma < 0) return null;
        var meta = src[..comma];
        var payload = src[(comma + 1)..].Replace("\r", "").Replace("\n", "").Replace(" ", "");
        try
        {
            if (meta.Contains(";base64", StringComparison.OrdinalIgnoreCase))
                return Convert.FromBase64String(payload);
            return System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
        }
        catch
        {
            return null;
        }
    }

    private static string GuessImageExtension(string dataUrl)
    {
        var head = dataUrl[..Math.Min(64, dataUrl.Length)].ToLowerInvariant();
        if (head.Contains("image/png")) return ".png";
        if (head.Contains("image/webp")) return ".webp";
        if (head.Contains("image/gif")) return ".gif";
        if (head.Contains("image/jpeg") || head.Contains("image/jpg")) return ".jpg";
        return ".png";
    }

    private static Dictionary<string, SKTypeface> LoadCustomTypefaces(CardDesign design)
    {
        var map = new Dictionary<string, SKTypeface>(StringComparer.OrdinalIgnoreCase);
        foreach (var font in design.Fonts ?? [])
        {
            if (string.IsNullOrWhiteSpace(font.Family) || string.IsNullOrWhiteSpace(font.DataBase64))
                continue;
            try
            {
                var raw = font.DataBase64;
                var comma = raw.IndexOf(',');
                if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
                    raw = raw[(comma + 1)..];
                var bytes = Convert.FromBase64String(raw);
                var data = SKData.CreateCopy(bytes);
                var tf = SKTypeface.FromData(data);
                data.Dispose();
                if (tf is not null)
                    map[font.Family] = tf;
            }
            catch
            {
                // skip invalid font payloads
            }
        }
        return map;
    }

    private static SKTypeface ResolveTypeface(
        string? family,
        IReadOnlyDictionary<string, SKTypeface> custom,
        string text)
    {
        if (!string.IsNullOrWhiteSpace(family) && custom.TryGetValue(family, out var customTf))
            return customTf;

        var preferred = SKTypeface.FromFamilyName(family ?? "Segoe UI") ?? SKTypeface.Default;
        var arabicChar = text.FirstOrDefault(IsArabicChar);
        if (arabicChar == '\0')
            return preferred;

        // Ensure the face can render Arabic; fall back to a system Arabic-capable font.
        using var probe = new SKFont(preferred, 12);
        if (probe.ContainsGlyph(arabicChar))
            return preferred;

        var matched = SKFontManager.Default.MatchCharacter(arabicChar);
        return matched ?? preferred;
    }

    private static bool IsArabicChar(char c) =>
        (c >= '\u0600' && c <= '\u06FF') ||
        (c >= '\u0750' && c <= '\u077F') ||
        (c >= '\u08A0' && c <= '\u08FF') ||
        (c >= '\uFB50' && c <= '\uFDFF') ||
        (c >= '\uFE70' && c <= '\uFEFF');

    private static bool ContainsRtlScript(string text) => text.Any(IsArabicChar) ||
        text.Any(c => c >= '\u0590' && c <= '\u05FF');

    private void RenderSide(
        CardSideDesign side,
        IDictionary<string, object?> record,
        string path,
        IReadOnlyDictionary<string, SKTypeface> typefaces)
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
                    if (string.IsNullOrEmpty(text)) break;

                    var typeface = ResolveTypeface(el.FontFamily, typefaces, text);
                    using var font = new SKFont(typeface, (float)el.FontSize)
                    {
                        Edging = SKFontEdging.Antialias,
                        Hinting = SKFontHinting.Normal,
                        Subpixel = true
                    };
                    using var paint = new SKPaint { Color = ParseColor(el.Color), IsAntialias = true };

                    // Explicit direction wins; otherwise auto-detect Arabic/Hebrew.
                    var rtl = string.Equals(el.Direction, "rtl", StringComparison.OrdinalIgnoreCase)
                        || (!string.Equals(el.Direction, "ltr", StringComparison.OrdinalIgnoreCase)
                            && ContainsRtlScript(text));
                    var align = ResolveTextAlign(el.Align, rtl);

                    if (!string.IsNullOrWhiteSpace(el.BackgroundColor))
                    {
                        using var bgPaint = new SKPaint { Color = ParseColor(el.BackgroundColor), IsAntialias = true };
                        canvas.DrawRect(rect, bgPaint);
                    }

                    using var shaper = new SKShaper(typeface);

                    if (el.WrapText)
                    {
                        DrawWrappedShapedText(canvas, shaper, text, font, paint, align, x, y, w, h);
                    }
                    else
                    {
                        if (el.AutoSize)
                        {
                            var measured = MeasureShapedWidth(shaper, text, font);
                            if (measured > w && measured > 0)
                                font.Size = Math.Max(8, font.Size * (w / measured));
                        }

                        var drawX = align switch
                        {
                            SKTextAlign.Center => x + w / 2f,
                            SKTextAlign.Right => x + w,
                            _ => x
                        };
                        var metrics = font.Metrics;
                        var baseline = y - metrics.Ascent;
                        canvas.DrawShapedText(shaper, text, drawX, baseline, align, font, paint);
                    }
                    break;
                }
                case "photo":
                case "image":
                {
                    var opacity = Math.Clamp(el.Opacity ?? 1.0, 0, 1);
                    if (opacity <= 0)
                        break;

                    var src = el.Type.Equals("photo", StringComparison.OrdinalIgnoreCase)
                        ? ResolvePhotoPath(el.Field, record)
                        : el.Src;
                    // Src may already be materialized to a local path; also resolve record photo paths.
                    var local = ResolveToLocalPath(src ?? string.Empty) ?? src;
                    using var photo = LoadBitmapFromPath(local);
                    if (photo is not null)
                    {
                        if (opacity >= 0.999)
                        {
                            canvas.DrawBitmap(photo, rect, sampling);
                        }
                        else
                        {
                            using var layerPaint = new SKPaint
                            {
                                Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255))
                            };
                            var layer = canvas.SaveLayer(layerPaint);
                            canvas.DrawBitmap(photo, rect, sampling);
                            canvas.RestoreToCount(layer);
                        }
                    }
                    else
                    {
                        using var paint = new SKPaint { Color = SKColors.LightGray.WithAlpha((byte)Math.Round(opacity * 255)) };
                        canvas.DrawRect(rect, paint);
                    }
                    break;
                }
                case "qr":
                {
                    var value = ResolveText(el, record);
                    if (string.IsNullOrWhiteSpace(value))
                        value = " ";
                    DrawQrCode(canvas, rect, value);
                    break;
                }
                case "barcode":
                {
                    var value = ResolveText(el, record);
                    using var fill = new SKPaint { Color = SKColors.White };
                    using var border = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
                    using var font = new SKFont(SKTypeface.Default, 12);
                    using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
                    canvas.DrawRect(rect, fill);
                    canvas.DrawRect(rect, border);
                    canvas.DrawShapedText(value, x + 4, y + h / 2 + 4, SKTextAlign.Left, font, textPaint);
                    break;
                }
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }

    private static float MeasureShapedWidth(SKShaper shaper, string text, SKFont font)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var shaped = shaper.Shape(text, font);
        return shaped.Width;
    }

    private static void DrawWrappedShapedText(
        SKCanvas canvas,
        SKShaper shaper,
        string text,
        SKFont font,
        SKPaint paint,
        SKTextAlign align,
        float x,
        float y,
        float width,
        float height)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            var test = string.IsNullOrEmpty(current) ? word : current + " " + word;
            if (MeasureShapedWidth(shaper, test, font) <= width || string.IsNullOrEmpty(current))
                current = test;
            else
            {
                lines.Add(current);
                current = word;
            }
        }
        if (!string.IsNullOrEmpty(current)) lines.Add(current);

        var metrics = font.Metrics;
        var lineHeight = font.Size * 1.25f;
        var maxLines = Math.Max(1, (int)(height / lineHeight));
        for (var i = 0; i < Math.Min(lines.Count, maxLines); i++)
        {
            var drawX = align switch
            {
                SKTextAlign.Center => x + width / 2f,
                SKTextAlign.Right => x + width,
                _ => x
            };
            var baseline = y - metrics.Ascent + i * lineHeight;
            canvas.DrawShapedText(shaper, lines[i], drawX, baseline, align, font, paint);
        }
    }

    private static void DrawQrCode(SKCanvas canvas, SKRect rect, string value)
    {
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
        var modules = qrData.ModuleMatrix;
        var count = modules.Count;
        if (count <= 0) return;

        var size = Math.Min(rect.Width, rect.Height);
        var cell = size / count;
        if (cell <= 0) return;

        var offsetX = rect.Left + (rect.Width - cell * count) / 2f;
        var offsetY = rect.Top + (rect.Height - cell * count) / 2f;

        using var light = new SKPaint { Color = SKColors.White, IsAntialias = false };
        using var dark = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        canvas.DrawRect(rect, light);

        for (var row = 0; row < count; row++)
        {
            for (var col = 0; col < count; col++)
            {
                if (!modules[row][col]) continue;
                canvas.DrawRect(
                    SKRect.Create(offsetX + col * cell, offsetY + row * cell, cell + 0.5f, cell + 0.5f),
                    dark);
            }
        }
    }

    private static SKTextAlign ResolveTextAlign(string? align, bool rtl)
    {
        return (align?.ToLowerInvariant(), rtl) switch
        {
            ("center", _) => SKTextAlign.Center,
            ("right", false) => SKTextAlign.Right,
            ("left", true) => SKTextAlign.Right,
            ("right", true) => SKTextAlign.Left,
            (_, true) => SKTextAlign.Right,
            _ => SKTextAlign.Left
        };
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

    private SKBitmap? LoadBitmapFromPath(string? pathOrData)
    {
        if (string.IsNullOrWhiteSpace(pathOrData)) return null;
        try
        {
            if (pathOrData.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = DecodeDataUrl(pathOrData);
                return bytes is null ? null : SKBitmap.Decode(bytes);
            }

            var local = ResolveToLocalPath(pathOrData) ?? pathOrData;
            if (!string.IsNullOrWhiteSpace(local) && File.Exists(local))
                return SKBitmap.Decode(local);
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static CardDesign ParseDesign(string json)
    {
        return JsonSerializer.Deserialize<CardDesign>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new CardDesign();
    }
}
