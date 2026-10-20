// Draws an Svg.Skia drawing with its text converted to glyph outlines, for PDF output. SkiaSharp's PDF backend is
// built without a font subsetter and embeds every font file whole: one line of Korean text adds the 13 MB Malgun
// Gothic font to the PDF. Outlines keep the text's exact shape at a few kilobytes (it is no longer selectable).
// Text is shaped with HarfBuzz the way Svg.Skia does it (adapted from SkiaModel.TextShaping.cs, Svg.Skia, MIT).

using System.Runtime.InteropServices;
using Filee.Engines.Pdf;
using HarfBuzzSharp;
using ShimSkiaSharp;
using Svg.Skia;
using Buffer = HarfBuzzSharp.Buffer;
using SKCanvas = SkiaSharp.SKCanvas;
using SKFont = SkiaSharp.SKFont;
using SKPath = SkiaSharp.SKPath;
using SKPoint = SkiaSharp.SKPoint;
using SKTextAlign = SkiaSharp.SKTextAlign;
using SKTypeface = SkiaSharp.SKTypeface;

namespace Filee.Engines.Vector;

/// <summary>Plays back an Svg.Skia picture model, drawing text commands as paths.</summary>
internal sealed class OutlinedTextPlayback(SkiaModel model, IReadOnlyList<SvgTextSource>? sources = null,
    ArabicPdfGlyphWriter? glyphWriter = null) : IDisposable
{
    /// <summary>HarfBuzz works in integer units: positions are computed at this scale and divided back.</summary>
    private const int HarfBuzzScale = 512;

    private readonly Dictionary<string, Font?> _shapers = new(StringComparer.Ordinal);
    private ArabicTextLayout? _arabic;
    private readonly HashSet<SvgTextSource> _usedSources = new(ReferenceEqualityComparer.Instance);

    /// <summary>Draws every command of <paramref name="picture"/> onto <paramref name="canvas"/>.</summary>
    public void Draw(ShimSkiaSharp.SKPicture picture, SKCanvas canvas)
    {
        if (picture.Commands is null)
            return;
        if (DrawTextPicture(picture, canvas))
            return;
        foreach (var command in picture.Commands)
            Draw(command, canvas);
    }

    private bool DrawTextPicture(ShimSkiaSharp.SKPicture picture, SKCanvas canvas)
    {
        // A uniform SVG text element is one child picture. Its old per-word commands can already be in visual
        // order, including punctuation merged into a Latin run. Shape its original paragraph once instead.
        if (sources is null || picture.Commands is not { Count: > 0 } commands)
            return false;
        if (commands.Count == 1 && commands[0] is DrawTextBlobCanvasCommand { TextBlob.Text: null } opaque)
            return DrawOpaqueText(opaque, canvas);
        var draws = new List<(string Text, float X, float Y, ShimSkiaSharp.SKPaint? Paint, ShimSkiaSharp.SKFont? Font)>();
        foreach (var command in commands)
        {
            switch (command)
            {
                case DrawTextCanvasCommand text:
                    draws.Add((text.Text ?? "", text.X, text.Y, text.Paint, text.Font));
                    break;
                case DrawPositionedTextRunCanvasCommand run when run.Fragments is { Count: > 0 } fragments
                    && fragments.All(f => f.RotationDegrees == 0 && f.ScaleX == 1):
                    draws.AddRange(fragments.Select(f => (f.Text, f.Point.X, f.Point.Y, run.Paint, run.Font)));
                    break;
                case DrawTextBlobCanvasCommand blob when blob.TextBlob?.Text is { } text:
                    draws.Add((text, blob.X, blob.Y, blob.Paint, blob.TextBlob.Font));
                    break;
                default:
                    return false;
            }
        }
        if (draws.Count == 0)
            return false;
        var signature = SvgTextSource.Characters(string.Concat(draws.Select(d => d.Text)));
        var source = sources.FirstOrDefault(s => s.Simple && ArabicTextLayout.ContainsArabic(s.Text) && !_usedSources.Contains(s) && SvgTextSource.Characters(s.Text) == signature);
        if (source is null || draws[0].Paint is not { } shimPaint)
            return false;
        using var font = draws[0].Font is { } f ? model.ToSKFont(f) : model.ToSKFont(shimPaint);
        using var paint = model.ToSKPaint(shimPaint);
        if (font is null || paint is null || draws.Any(d => d.Y != draws[0].Y || d.Paint?.TextSize != shimPaint.TextSize
            || !Equals(d.Paint?.Color, shimPaint.Color)))
            return false;
        _arabic ??= new ArabicTextLayout();
        var line = _arabic.Wrap(source.Text, float.MaxValue, font.Size, font.Typeface?.FontWeight >= 600, source.Rtl)[0];
        var align = source.Anchor switch
        {
            "middle" => SKTextAlign.Center,
            "end" => source.Rtl ? SKTextAlign.Left : SKTextAlign.Right,
            _ => source.Rtl ? SKTextAlign.Right : SKTextAlign.Left,
        };
        var x = source.X ?? draws.Min(d => d.X);
        x -= align switch { SKTextAlign.Right => line.Width, SKTextAlign.Center => line.Width / 2, _ => 0 };
        ArabicTextLayout.Draw(canvas, line, x, draws[0].Y, paint.Color, glyphWriter, paint);
        _usedSources.Add(source);
        return true;
    }

    private bool DrawOpaqueText(DrawTextBlobCanvasCommand command, SKCanvas canvas)
    {
        // On Windows Svg.Skia emits a single glyph-only blob per text element. Bind it to the original simple
        // XML element by its baseline and anchor; never infer Unicode from the already substituted glyph IDs.
        if (sources is null || command.TextBlob is not { Points: { Length: > 0 } points, Glyphs: { } glyphs } blob
            || points.Length != glyphs.Length || command.Paint is not { } shimPaint)
            return false;
        using var font = blob.Font is { } shimFont ? model.ToSKFont(shimFont) : model.ToSKFont(shimPaint);
        using var paint = model.ToSKPaint(shimPaint);
        if (font is null || paint is null)
            return false;
        var widths = font.GetGlyphWidths(glyphs);
        var bases = Enumerable.Range(0, points.Length).Where(i => widths[i] > 0.01f).ToArray();
        if (bases.Length == 0)
            return false;
        var baseline = command.Y + bases.GroupBy(i => MathF.Round(points[i].Y, 2))
            .OrderByDescending(g => g.Count()).First().Key;
        var left = command.X + bases.Min(i => points[i].X);
        var right = command.X + bases.Max(i => points[i].X + widths[i]);
        var candidates = sources.Where(s => !_usedSources.Contains(s) && Math.Abs((s.Y ?? 0) - baseline) < 0.1f).ToArray();
        var source = candidates.FirstOrDefault(s => s.Simple && ArabicTextLayout.ContainsArabic(s.Text)
            && (candidates.Length == 1 || s.X is null || Math.Abs(s.X.Value - (s.Anchor == "middle" ? (left + right) / 2
                : s.Anchor == "end" ^ s.Rtl ? right : left)) < Math.Max(1, font.Size * 0.1f)));
        if (source is null)
            return false;
        _arabic ??= new ArabicTextLayout();
        var line = _arabic.Wrap(source.Text, float.MaxValue, font.Size, font.Typeface?.FontWeight >= 600, source.Rtl)[0];
        var x = source.X ?? left;
        x -= source.Anchor == "middle" ? line.Width / 2 : source.Anchor == "end" ^ source.Rtl ? line.Width : 0;
        ArabicTextLayout.Draw(canvas, line, x, baseline, paint.Color, glyphWriter, paint);
        _usedSources.Add(source);
        return true;
    }

    private void Draw(CanvasCommand command, SKCanvas canvas)
    {
        if (command is DrawPictureCanvasCommand { Picture: { } nested })
        {
            Draw(nested, canvas);
            return;
        }
        // Everything else, and text that can't be shaped (text on a path, fonts without outlines), is drawn by
        // Svg.Skia itself.
        using var outline = TextOutline(command, out var shimPaint);
        if (outline is null)
        {
            model.Draw(command, canvas);
            return;
        }
        // The outlines with the text's paint (fill or stroke, colour, gradient, opacity).
        using var paint = shimPaint is null ? null : model.ToSKPaint(shimPaint);
        if (paint is not null && !outline.IsEmpty)
            canvas.DrawPath(outline, paint);
    }

    /// <summary>
    /// The glyph outlines of a text command, in the command's coordinates, with the text's paint; <c>null</c> when the
    /// command is no text or its text can't be shaped. Empty text gives an empty path.
    /// </summary>
    public SKPath? TextOutline(CanvasCommand command, out SKPaint? paint)
    {
        paint = command switch
        {
            DrawTextCanvasCommand text => text.Paint,
            DrawTextBlobCanvasCommand blob => blob.Paint,
            DrawPositionedTextRunCanvasCommand run => run.Paint,
            _ => null,
        };
        if (paint is null)
            return null;
        var path = new SKPath();
        var shaped = command switch
        {
            DrawTextCanvasCommand text => OutlineText(text, path),
            DrawTextBlobCanvasCommand blob => OutlineTextBlob(blob, path),
            DrawPositionedTextRunCanvasCommand run => OutlinePositionedRun(run, path),
            _ => false,
        };
        if (shaped)
            return path;
        path.Dispose();
        return null;
    }

    private bool OutlineText(DrawTextCanvasCommand command, SKPath path)
    {
        if (string.IsNullOrEmpty(command.Text))
            return true;
        using var font = command.Font is { } shimFont ? model.ToSKFont(shimFont) : model.ToSKFont(command.Paint!);
        var align = model.ToSKTextAlign(command.TextAlign ?? command.Paint!.TextAlign);
        return font?.Typeface is not null && AppendShaped(path, command.Text, font, command.X, command.Y, align);
    }

    private bool OutlineTextBlob(DrawTextBlobCanvasCommand command, SKPath path)
    {
        if (command.TextBlob?.Points is not { } points)
            return true;
        using var font = command.TextBlob.Font is { } shimFont ? model.ToSKFont(shimFont) : model.ToSKFont(command.Paint!);
        if (font is null)
            return false;
        var glyphs = command.TextBlob.Glyphs is { Length: > 0 } ids ? ids
            : command.TextBlob.Text is { } text ? font.GetGlyphs(text)
            : [];
        if (glyphs.Length != points.Length)
            return false;
        for (var i = 0; i < glyphs.Length; i++)
            AppendGlyph(path, font, glyphs[i], command.X + points[i].X, command.Y + points[i].Y);
        return true;
    }

    private bool OutlinePositionedRun(DrawPositionedTextRunCanvasCommand command, SKPath path)
    {
        if (command.Fragments is not { Count: > 0 })
            return true;
        using var font = command.Font is { } shimFont ? model.ToSKFont(shimFont) : model.ToSKFont(command.Paint!);
        if (font?.Typeface is null)
            return false;
        var align = model.ToSKTextAlign(command.TextAlign ?? command.Paint!.TextAlign);
        foreach (var fragment in command.Fragments)
        {
            using var part = new SKPath();
            if (!AppendShaped(part, fragment.Text, font, fragment.Point.X, fragment.Point.Y, align))
                continue;
            // Per-character rotation (SVG "rotate") and horizontal scaling (textLength), as Svg.Skia applies them.
            var transform = SkiaSharp.SKMatrix.Identity;
            if (fragment.RotationDegrees != 0f)
                transform = SkiaSharp.SKMatrix.CreateRotationDegrees(fragment.RotationDegrees, fragment.Point.X, fragment.Point.Y);
            if (fragment.ScaleX != 1f)
                transform = transform.PreConcat(SkiaSharp.SKMatrix.CreateScale(fragment.ScaleX, 1f, fragment.ScaleOriginX, fragment.Point.Y));
            part.Transform(transform);
            path.AddPath(part);
        }
        return true;
    }

    /// <summary>Shapes <paramref name="text"/> and appends its glyph outlines, aligned at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private bool AppendShaped(SKPath path, string text, SKFont font, float x, float y, SKTextAlign align)
    {
        if (string.IsNullOrEmpty(text))
            return true;
        var shaper = ShaperFor(font.Typeface);
        if (shaper is null)
            return false;

        using var buffer = new Buffer();
        buffer.ClusterLevel = ClusterLevel.Characters;
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();
        shaper.Shape(buffer);

        var infos = buffer.GlyphInfos;
        var positions = buffer.GlyphPositions;
        var scaleY = font.Size / HarfBuzzScale;
        var scaleX = scaleY * font.ScaleX;
        var width = 0f;
        foreach (var position in positions)
            width += position.XAdvance * scaleX;
        var penX = x - align switch
        {
            SKTextAlign.Center => width / 2,
            SKTextAlign.Right => width,
            _ => 0f,
        };
        var penY = y;
        for (var i = 0; i < infos.Length; i++)
        {
            AppendGlyph(path, font, (ushort)infos[i].Codepoint, penX + positions[i].XOffset * scaleX, penY - positions[i].YOffset * scaleY);
            penX += positions[i].XAdvance * scaleX;
            penY -= positions[i].YAdvance * scaleY;
        }
        return true;
    }

    private static void AppendGlyph(SKPath path, SKFont font, ushort glyph, float x, float y)
    {
        using var outline = font.GetGlyphPath(glyph);
        if (outline is { IsEmpty: false })
            path.AddPath(outline, x, y);
    }

    /// <summary>A HarfBuzz font for the typeface (cached), or null when its font data can't be read.</summary>
    private Font? ShaperFor(SKTypeface typeface)
    {
        var key = $"{typeface.FamilyName}|{typeface.FontWeight}|{typeface.FontWidth}|{typeface.FontSlant}";
        if (_shapers.TryGetValue(key, out var cached))
            return cached;

        Font? font = null;
        var asset = typeface.OpenStream(out var index);
        if (asset is not null)
        {
            using var blob = ToBlob(asset);
            using var face = new Face(blob, index) { Index = index, UnitsPerEm = typeface.UnitsPerEm };
            font = new Font(face);
            font.SetScale(HarfBuzzScale, HarfBuzzScale);
            font.SetFunctionsOpenType();
        }
        _shapers[key] = font;
        return font;
    }

    /// <summary>Wraps Skia's font data for HarfBuzz without copying when Skia has it in memory.</summary>
    private static Blob ToBlob(SkiaSharp.SKStreamAsset asset)
    {
        var size = asset.Length;
        var memory = asset.GetMemoryBase();
        Blob blob;
        if (memory != IntPtr.Zero)
        {
            blob = new Blob(memory, size, MemoryMode.ReadOnly, asset.Dispose);
        }
        else
        {
            var copy = Marshal.AllocCoTaskMem(size);
            asset.Read(copy, size);
            blob = new Blob(copy, size, MemoryMode.ReadOnly, () =>
            {
                Marshal.FreeCoTaskMem(copy);
                asset.Dispose();
            });
        }
        blob.MakeImmutable();
        return blob;
    }

    public void Dispose()
    {
        _arabic?.Dispose();
        foreach (var font in _shapers.Values)
            font?.Dispose();
        _shapers.Clear();
    }
}
