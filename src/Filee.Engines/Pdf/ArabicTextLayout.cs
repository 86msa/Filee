// Arabic PDF text stays text: UAX #9 paragraph levels, UAX #14 line breaks, HarfBuzz OpenType shaping,
// and embedded PDF font subsets with genuine Unicode mappings.
using System.Text;
using Filee.Engines.Hwp.Hwpx;
using HarfBuzzSharp;
using Meziantou.Framework;
using SkiaSharp;
using Buffer = HarfBuzzSharp.Buffer;

namespace Filee.Engines.Pdf;

internal sealed class ArabicTextLayout : IDisposable
{
    private const int Scale = 512;
    private static readonly Feature[] LatinFeatures = [new(new Tag('l', 'i', 'g', 'a'), 0), new(new Tag('c', 'l', 'i', 'g'), 0)];
    private readonly Dictionary<(bool Bold, bool Arabic), SKTypeface> _faces = [];
    private readonly Dictionary<SKTypeface, HarfBuzzSharp.Font> _shapers = [];
    private readonly Dictionary<int, SKTypeface> _fallback = [];

    internal sealed record Run(string Text, SKTypeface Face, float Size, ushort[] Glyphs, SKPoint[] Positions,
        uint[] Clusters, float[] Advances, float Width, byte Level, int LogicalStart, HCharFormat Format);
    internal sealed record Line(IReadOnlyList<Run> Runs, float Width, float Ascent, float Descent, bool Rtl)
    {
        public float Height => (Ascent + Descent) * 1.15f;
    }

    public static bool ContainsArabic(string text) => text.EnumerateRunes().Any(r =>
        r.Value is >= 0x0600 and <= 0x06ff or >= 0x0750 and <= 0x077f or >= 0x0870 and <= 0x08ff
            or >= 0xfb50 and <= 0xfdff or >= 0xfe70 and <= 0xfeff or >= 0x1ee00 and <= 0x1eeff);

    private SKTypeface Face(bool bold, bool arabic = true)
    {
        if (_faces.TryGetValue((bold, arabic), out var face))
            return face;
        var name = $"Filee.Engines.Fonts.NotoSans{(arabic ? "Arabic" : "")}-{(bold ? "Bold" : "Regular")}.ttf";
        using var stream = typeof(ArabicTextLayout).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("The Arabic PDF font is missing. Run build/fetch-fonts.ps1 before building.");
        using var data = SKData.Create(stream);
        face = SKTypeface.FromData(data) ?? throw new InvalidOperationException("The Arabic PDF font could not be read.");
        _faces.Add((bold, arabic), face);
        return face;
    }

    private SKTypeface FaceAt(string text, int at, bool bold)
    {
        var rune = Rune.GetRuneAt(text, at);
        if (rune.Value is > 0x20 and < 0x7f)
            return Face(bold, arabic: false);
        var arabic = Face(bold);
        if (arabic.GetGlyph(rune.Value) != 0 || Rune.IsControl(rune))
            return arabic;
        var latin = Face(bold, arabic: false);
        if (latin.GetGlyph(rune.Value) != 0)
            return latin;
        if (!_fallback.TryGetValue(rune.Value, out var fallback))
        {
            fallback = SKFontManager.Default.MatchCharacter(rune.Value) ?? latin;
            _fallback.Add(rune.Value, fallback);
        }
        return fallback;
    }

    private HarfBuzzSharp.Font Shaper(SKTypeface face)
    {
        if (_shapers.TryGetValue(face, out var cached))
            return cached;
        using var stream = face.OpenStream(out var index);
        using var memory = SKData.Create(stream);
        // HarfBuzz duplicates the bytes: its lifetime is independent of the Skia stream/data.
        using var blob = new Blob(memory.Data, (int)memory.Size, MemoryMode.Duplicate, () => { });
        using var hbFace = new HarfBuzzSharp.Face(blob, index);
        var font = new HarfBuzzSharp.Font(hbFace);
        font.SetFunctionsOpenType();
        font.SetScale(Scale, Scale);
        _shapers.Add(face, font);
        return font;
    }

    /// <summary>Resolve the whole paragraph before wrapping; line breaks never split an Arabic grapheme.</summary>
    public IReadOnlyList<Line> Wrap(string paragraph, float width, float size = 12, bool bold = false, bool? rtl = null,
        IReadOnlyList<HCharFormat>? formats = null)
    {
        width = Math.Max(1, width);
        var levels = new byte[paragraph.Length];
        var baseLevel = UnicodeBidi.ResolveLevels(paragraph,
            rtl is true ? UnicodeBidiDirection.RightToLeft : rtl is false ? UnicodeBidiDirection.LeftToRight : UnicodeBidiDirection.Auto, levels);
        var lines = new List<Line>();
        var start = 0;
        while (start < paragraph.Length)
        {
            var end = start;
            Line? best = null;
            foreach (var opportunity in Unicode.EnumerateLineBreaks(paragraph.AsSpan(start)))
            {
                var candidateEnd = start + opportunity.Index;
                var candidate = Shape(paragraph, start, candidateEnd, levels, baseLevel, size, bold, formats);
                if (candidate.Width > width && best is not null)
                    break;
                if (candidate.Width > width)
                {
                    // A URL or an unbroken word still wraps, at UAX #29 boundaries, with fresh joining at the break.
                    foreach (var cluster in Unicode.EnumerateGraphemeClusters(paragraph.AsSpan(start, candidateEnd - start)))
                    {
                        var clusterEnd = start + cluster.End.Value;
                        var part = Shape(paragraph, start, clusterEnd, levels, baseLevel, size, bold, formats);
                        if (part.Width > width && best is not null)
                            break;
                        best = part;
                        end = clusterEnd;
                    }
                    break;
                }
                best = candidate;
                end = candidateEnd;
                if (opportunity.IsMandatory)
                    break;
            }
            if (best is null || end <= start)
                throw new InvalidOperationException("Arabic PDF line breaking did not advance.");
            lines.Add(best);
            start = end;
        }
        if (lines.Count == 0)
            lines.Add(Shape(" ", 0, 1, [(byte)baseLevel], baseLevel, size, bold));
        return lines;
    }

    private Line Shape(string paragraph, int start, int end, byte[] paragraphLevels, int baseLevel, float size, bool bold,
        IReadOnlyList<HCharFormat>? formats = null)
    {
        var text = paragraph[start..end].TrimEnd('\r', '\n');
        var levels = paragraphLevels.AsSpan(start, text.Length).ToArray();
        UnicodeBidi.ResetLineLevels(text, baseLevel, levels);
        var runs = new List<Run>();
        var ascent = 0f;
        var descent = 0f;
        // Split by script as well as direction. Numbers/Latin must not inherit an Arabic shaper's script.
        foreach (var script in Unicode.EnumerateScriptRuns(text))
        {
            var scriptStart = script.Index;
            var scriptEnd = script.Index + script.Length;
            for (var at = scriptStart; at < scriptEnd;)
            {
                var format = formats is not null ? formats[start + at] : default;
                var runSize = Math.Max(1, format.Size / 100f ?? size);
                var runBold = format.Bold ?? bold;
                var chosen = FaceAt(text, at, runBold);
                var stop = at + (char.IsHighSurrogate(text[at]) ? 2 : 1);
                var level = levels[at];
                while (stop < scriptEnd && levels[stop] == level && FaceAt(text, stop, runBold) == chosen
                    && (formats is null || formats[start + stop] == format))
                    stop += char.IsHighSurrogate(text[stop]) ? 2 : 1;
                var part = text[at..stop];
                // Keep combining marks with their base. Arabic is always the bundled face; other scripts may use
                // a system fallback, exactly as the existing document renderer does.
                using var buffer = new Buffer();
                buffer.ClusterLevel = ClusterLevel.Characters;
                // Full line context preserves joining at a style/font boundary inside an Arabic word.
                buffer.AddUtf16(text, at, stop - at);
                buffer.GuessSegmentProperties();
                buffer.Direction = (level & 1) != 0 ? Direction.RightToLeft : Direction.LeftToRight;
                Shaper(chosen).Shape(buffer, script.Script == UnicodeScript.Latin ? LatinFeatures : []);
                var infos = buffer.GlyphInfos;
                var positions = buffer.GlyphPositions;
                var glyphs = new ushort[infos.Length];
                var points = new SKPoint[infos.Length];
                var clusters = new uint[infos.Length];
                var advances = new float[infos.Length];
                var x = 0f;
                for (var i = 0; i < infos.Length; i++)
                {
                    glyphs[i] = checked((ushort)infos[i].Codepoint);
                    points[i] = new SKPoint(x + positions[i].XOffset * runSize / Scale, -positions[i].YOffset * runSize / Scale);
                    clusters[i] = (uint)Encoding.UTF8.GetByteCount(text.AsSpan(at, (int)infos[i].Cluster - at));
                    advances[i] = positions[i].XAdvance * runSize / Scale;
                    x += advances[i];
                }
                using var font = new SKFont(chosen, runSize);
                ascent = Math.Max(ascent, -font.Metrics.Ascent);
                descent = Math.Max(descent, font.Metrics.Descent);
                runs.Add(new Run(part, chosen, runSize, glyphs, points, clusters, advances, x, level, at, format));
                at = stop;
            }
        }
        // UAX #9 L2, applied to directional/script runs; shaping already orders the glyphs within a run.
        if (runs.Count > 0)
        {
            var highest = runs.Max(r => r.Level);
            var lowestOdd = runs.Where(r => (r.Level & 1) != 0).Select(r => (int)r.Level).DefaultIfEmpty(highest + 1).Min();
            for (var level = (int)highest; level >= lowestOdd; level--)
                for (var at = 0; at < runs.Count; at++)
                    if (runs[at].Level >= level)
                    {
                        var stop = at + 1;
                        while (stop < runs.Count && runs[stop].Level >= level)
                            stop++;
                        runs.Reverse(at, stop - at);
                        at = stop - 1;
                    }
        }
        return new Line(runs, runs.Sum(r => r.Width), Math.Max(ascent, size), Math.Max(descent, size * 0.3f), (baseLevel & 1) != 0);
    }

    public static void Draw(SKCanvas canvas, Line line, float x, float baseline, SKColor color,
        ArabicPdfGlyphWriter? glyphWriter = null, SKPaint? template = null)
    {
        using var paint = template?.Clone() ?? new SKPaint { Color = color, IsAntialias = true };
        var shadeX = x;
        foreach (var run in line.Runs)
        {
            if (SKColor.TryParse(run.Format.Shade, out var shade))
            {
                using var background = new SKPaint { Color = shade };
                canvas.DrawRect(shadeX, baseline - line.Ascent, run.Width, line.Ascent + line.Descent, background);
            }
            shadeX += run.Width;
        }
        var first = line.Runs.FirstOrDefault();
        paint.Color = first is not null && SKColor.TryParse(first.Format.Color, out var firstColor) ? firstColor : color;
        var wholeLine = glyphWriter?.PlaceLine(canvas, line, x, baseline, paint) == true;
        var placed = new List<(Run Run, float X)>();
        foreach (var run in line.Runs)
        {
            placed.Add((run, x));
            x += run.Width;
        }
        foreach (var (run, runX) in placed.OrderBy(p => p.Run.LogicalStart))
        {
            paint.Color = SKColor.TryParse(run.Format.Color, out var runColor) ? runColor : color;
            var intercepted = wholeLine || glyphWriter?.Place(canvas, run, runX, baseline, paint, line.Ascent, line.Descent) == true;
            if (!intercepted)
            {
                using var font = new SKFont(run.Face, run.Size) { SkewX = run.Format.Italic == true ? -0.2f : 0 };
                using var builder = new SKTextBlobBuilder();
                var bytes = Encoding.UTF8.GetBytes(run.Text);
                // A decomposed glyph cluster can start with a dot or vowel above the baseline. Mapping the text to
                // that component makes PDF readers infer phantom lines. Anchor the cluster's Unicode on its visible
                // advancing glyph; the remaining components are vector decorations at HarfBuzz's exact positions.
                // The base glyph remains embedded, selectable text. No invisible text or page image is added.
                var order = Enumerable.Range(0, run.Glyphs.Length).GroupBy(i => run.Clusters[i])
                    .Select(g => g.OrderByDescending(i => run.Advances[i]).First())
                    .OrderBy(i => run.Clusters[i]).ToArray();
                var target = builder.AllocatePositionedTextRun(font, order.Length, bytes.Length);
                target.SetGlyphs(order.Select(i => run.Glyphs[i]).ToArray());
                target.SetPositions(order.Select(i => run.Positions[i]).ToArray());
                target.SetClusters(order.Select(i => run.Clusters[i]).ToArray());
                target.SetText(bytes);
                using var blob = builder.Build();
                canvas.DrawText(blob, runX, baseline, paint);
                foreach (var i in Enumerable.Range(0, run.Glyphs.Length).Except(order))
                {
                    using var decoration = font.GetGlyphPath(run.Glyphs[i]);
                    if (decoration is not null)
                    {
                        decoration.Transform(SKMatrix.CreateTranslation(runX + run.Positions[i].X, baseline + run.Positions[i].Y));
                        canvas.DrawPath(decoration, paint);
                    }
                }
            }
            if (run.Format.Underline == true || run.Format.Strike == true)
            {
                paint.StrokeWidth = Math.Max(0.5f, run.Size / 16);
                if (run.Format.Underline == true)
                    canvas.DrawLine(runX, baseline + run.Size * 0.12f, runX + run.Width, baseline + run.Size * 0.12f, paint);
                if (run.Format.Strike == true)
                    canvas.DrawLine(runX, baseline - run.Size * 0.3f, runX + run.Width, baseline - run.Size * 0.3f, paint);
            }
        }
    }

    public void Dispose()
    {
        foreach (var font in _shapers.Values)
            font.Dispose();
        foreach (var face in _fallback.Values.Distinct().Except(_faces.Values))
            face.Dispose();
        foreach (var face in _faces.Values)
            face.Dispose();
    }
}
