// PDF readers disagree on ActualText containing a base plus Arabic vowels, especially when HarfBuzz decomposes
// one letter into several glyphs. A small Type 3 subset gives each original scalar its own Unicode mapping and
// puts offsets inside the glyph program, keeping every text origin on the baseline. These are real PDF font
// glyphs (text operators + ToUnicode), not an image or an invisible text layer.
using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace Filee.Engines.Pdf;

internal sealed class ArabicPdfGlyphWriter : IDisposable
{
    private readonly SKTypeface _markerFace;
    private readonly SKFont _marker;
    private sealed record Placement(ArabicTextLayout.Run Run, float Ascent, float Descent,
        IReadOnlyList<(ArabicTextLayout.Run Run, float Offset)>? Parts = null);
    private readonly Dictionary<IntPtr, SKTypeface> _retainedFaces = [];
    private readonly List<Placement> _runs = [];
    private readonly Dictionary<string, PdfDictionary> _programs = [];
    private int _next;
    private PdfDocument _document = null!;

    public ArabicPdfGlyphWriter()
    {
        using var stream = typeof(ArabicPdfGlyphWriter).Assembly.GetManifestResourceStream("Filee.Engines.Fonts.NotoSans-Medium.ttf")
            ?? throw new InvalidOperationException("The PDF font assets are missing; run build/fetch-fonts.ps1.");
        using var data = SKData.Create(stream);
        _markerFace = SKTypeface.FromData(data);
        _marker = new SKFont(_markerFace, 1);
    }

    internal bool Place(SKCanvas canvas, ArabicTextLayout.Run run, float x, float baseline, SKPaint paint,
        float ascent, float descent)
    {
        if (paint.Shader is not null || paint.Style != SKPaintStyle.Fill)
            return false;
        // A private intermediate marker preserves Skia's exact graphics state and drawing order. Finish replaces
        // every occurrence and removes its font resource before the caller can publish/move the output file.
        _runs.Add(new Placement(Retain(run), ascent, descent));
        canvas.DrawText("!", x, baseline, _marker, paint);
        return true;
    }

    private ArabicTextLayout.Run Retain(ArabicTextLayout.Run run)
    {
        if (!_retainedFaces.TryGetValue(run.Face.Handle, out var face))
        {
            using var stream = run.Face.OpenStream(out var index);
            using var data = SKData.Create(stream);
            face = SKTypeface.FromData(data, index);
            _retainedFaces.Add(run.Face.Handle, face);
        }
        return run with { Face = face };
    }

    internal bool PlaceLine(SKCanvas canvas, ArabicTextLayout.Line line, float x, float baseline, SKPaint paint)
    {
        if (line.Runs.Count == 0 || paint.Shader is not null || paint.Style != SKPaintStyle.Fill
            || line.Runs.Any(r => r.Size != line.Runs[0].Size || r.Format.Color != line.Runs[0].Format.Color))
            return false;
        var offset = 0f;
        var parts = new List<(ArabicTextLayout.Run Run, float Offset)>();
        foreach (var run in line.Runs)
        {
            parts.Add((Retain(run), offset));
            offset += run.Width;
        }
        _runs.Add(new Placement(parts[0].Run, line.Ascent, line.Descent, parts));
        canvas.DrawText("!", x, baseline, _marker, paint);
        return true;
    }

    private static bool IsMark(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    public void Finish(string path)
    {
        if (_runs.Count == 0)
            return;
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        _document = document;
        foreach (var page in document.Pages)
            Patch(page);
        if (_next != _runs.Count)
            throw new InvalidOperationException("The Arabic PDF font pass could not replace every intermediate marker.");
        var temporary = path + ".fonts.pdf";
        document.Save(temporary);
        document.Close();
        File.Move(temporary, path, true);
    }

    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);
        public Matrix Translate(double x, double y) => this with { E = E + A * x + C * y, F = F + B * x + D * y };
        public string Operator => $"{N(A)} {N(B)} {N(C)} {N(D)} {N(E)} {N(F)} Tm\n";
        public double DistanceTo(Matrix other)
        {
            var determinant = A * D - B * C;
            return Math.Abs(determinant) < 1e-12 ? 0 : (D * (other.E - E) - C * (other.F - F)) / determinant;
        }
    }

    private void Patch(PdfPage page)
    {
        var fonts = page.Resources.Elements.GetDictionary("/Font");
        if (fonts is null)
            return;
        var markerNames = fonts.Elements.Where(p => Resolve(p.Value) is PdfDictionary f
                && f.Elements.GetName("/BaseFont").Contains("NotoSans-Medium", StringComparison.Ordinal))
            .Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        if (markerNames.Count == 0)
            return;
        var content = ContentReader.ReadContent(page);
        var output = new CSequence();
        var font = "";
        var size = 1d;
        var scale = 1d;
        var charSpace = 0d;
        var leading = 0d;
        var line = Matrix.Identity;
        var current = line;
        foreach (var item in content)
        {
            if (item is not COperator op)
            {
                output.Add(item);
                continue;
            }
            var operands = op.Operands;
            switch (op.OpCode.Name)
            {
                case "BT":
                    line = current = Matrix.Identity;
                    break;
                case "Tf":
                    font = ((CName)operands[0]).Name;
                    size = Number(operands[1]);
                    break;
                case "Tz": scale = Number(operands[0]) / 100; break;
                case "Tc": charSpace = Number(operands[0]); break;
                case "TL": leading = Number(operands[0]); break;
                case "Tm":
                    line = current = new Matrix(Number(operands[0]), Number(operands[1]), Number(operands[2]),
                        Number(operands[3]), Number(operands[4]), Number(operands[5]));
                    break;
                case "TD":
                    leading = -Number(operands[1]);
                    goto case "Td";
                case "Td":
                    line = current = line.Translate(Number(operands[0]), Number(operands[1]));
                    break;
                case "T*": line = current = line.Translate(0, -leading); break;
            }
            if (!markerNames.Contains(font) || op.OpCode.Name is not ("Tj" or "TJ"))
            {
                if (!(op.OpCode.Name == "Tf" && markerNames.Contains(font)))
                    output.Add(op);
                if (op.OpCode.Name is "Tj" or "TJ")
                {
                    var textItems = op.OpCode.Name == "TJ" ? (CArray)operands[0] : new CArray { operands[0] };
                    foreach (var textItem in textItems)
                        current = current.Translate(textItem is CString value
                            ? Advance(value, fonts.Elements.GetDictionary(font), size, scale, charSpace)
                            : -Number(textItem) / 1000 * size * scale, 0);
                }
                continue;
            }
            var originalSize = size;
            var entries = op.OpCode.Name == "TJ" ? (CArray)operands[0] : new CArray { operands[0] };
            foreach (var entry in entries)
            {
                if (entry is not CString text)
                {
                    current = current.Translate(-Number(entry) / 1000 * size * scale, 0);
                    continue;
                }
                if (text.Value.Length % 2 != 0)
                    throw new InvalidOperationException("Unexpected intermediate PDF font encoding.");
                for (var glyph = 0; glyph < text.Value.Length / 2; glyph++)
                {
                    if (_next >= _runs.Count)
                        throw new InvalidOperationException("Unexpected extra intermediate Arabic PDF glyph.");
                    var placement = _runs[_next++];
                    var replacement = WriteRun(placement, fonts, current);
                    output.Add(ContentReader.ReadContent(Encoding.ASCII.GetBytes(replacement)));
                    var glyphString = new CString { Value = text.Value.Substring(glyph * 2, 2) };
                    current = current.Translate(Advance(glyphString, fonts.Elements.GetDictionary(font), size, scale, charSpace), 0);
                }
            }
            // Restore both the line matrix and current text position; a later Td is relative to the former.
            var restoreFont = fonts.Elements.Keys.First(name => !markerNames.Contains(name));
            var restore = $"{restoreFont} {N(originalSize)} Tf\n" + line.Operator +
                $"[{N(-line.DistanceTo(current) * 1000 / (originalSize * scale))}] TJ\n";
            output.Add(ContentReader.ReadContent(Encoding.ASCII.GetBytes(restore)));
        }
        foreach (var name in markerNames)
            fonts.Elements.Remove(name);
        page.Contents.ReplaceContent(output);
    }

    private static double Advance(CString text, PdfDictionary? font, double size, double scale, double spacing)
    {
        if (font is null)
            return 0;
        var composite = font.Elements.GetName("/Subtype") == "/Type0";
        var descendant = composite && font.Elements.GetArray("/DescendantFonts") is { Elements.Count: > 0 } descendants
            ? Resolve(descendants.Elements[0]) as PdfDictionary : font;
        var advance = 0d;
        for (var at = 0; at < text.Value.Length; at += composite ? 2 : 1)
        {
            var code = composite ? text.Value[at] * 256 + text.Value[at + 1] : text.Value[at];
            var width = descendant?.Elements.GetReal("/DW") ?? 0;
            if (width == 0)
                width = 1000;
            if (composite && descendant?.Elements.GetArray("/W") is { } widths)
            {
                for (var i = 0; i < widths.Elements.Count;)
                {
                    var first = (int)PdfNumber(widths.Elements[i++]);
                    if (Resolve(widths.Elements[i]) is PdfArray array)
                    {
                        if (code >= first && code < first + array.Elements.Count)
                            width = PdfNumber(array.Elements[code - first]);
                        i++;
                    }
                    else
                    {
                        var last = (int)PdfNumber(widths.Elements[i++]);
                        var value = PdfNumber(widths.Elements[i++]);
                        if (code >= first && code <= last)
                            width = value;
                    }
                }
            }
            else if (!composite && font.Elements.GetArray("/Widths") is { } simple)
            {
                var index = code - font.Elements.GetInteger("/FirstChar");
                if (index >= 0 && index < simple.Elements.Count)
                    width = PdfNumber(simple.Elements[index]);
            }
            advance += (width / 1000 * size + spacing) * scale;
        }
        return advance;
    }

    private static double PdfNumber(PdfItem item) => Resolve(item) switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => throw new InvalidDataException("Unexpected PDF font width."),
    };

    private sealed record Character(Rune Unicode, float Anchor, float Advance, string Program, int LogicalIndex, bool Mark,
        float Left, float Right);

    private string WriteRun(Placement placement, PdfDictionary resources, Matrix origin)
    {
        var run = placement.Run;
        var characters = Characters(placement);
        var fonts = new List<(PdfDictionary Font, string Name)>();
        for (var offset = 0; offset < characters.Count; offset += 255)
        {
            var slice = characters.Skip(offset).Take(255).ToArray();
            var dictionary = Font(slice, placement);
            var name = "/FileeArabicSubset_" + _next + (placement.Parts is not null ? "_M_" : "_S_") + fonts.Count;
            dictionary.Elements.SetName("/Name", name);
            dictionary.Elements.GetDictionary("/FontDescriptor")!.Elements.SetName("/FontName", name);
            resources.Elements.SetReference(name, dictionary);
            fonts.Add((dictionary, name));
        }
        var operators = new StringBuilder();
        // Marks precede their base in visual text order, while marks on the same base keep their original order.
        // This avoids reversing fatha/shadda or putting the first kasra before the first letter on extraction.
        var ordered = characters.OrderBy(c => c.Anchor)
            .ThenBy(c => c.Mark ? 0 : 1).ThenBy(c => c.LogicalIndex).ToArray();
        for (var at = 0; at < ordered.Length;)
        {
            var bank = ordered[at].LogicalIndex / 255;
            var stop = at + 1;
            while (stop < ordered.Length && ordered[stop].LogicalIndex / 255 == bank)
                stop++;
            operators.Append(fonts[bank].Name).Append(' ').Append(N(run.Size)).Append(" Tf\n")
                .Append(origin.Translate(ordered[at].Anchor, 0).Operator).Append('[');
            for (var i = at; i < stop; i++)
            {
                var character = ordered[i];
                operators.Append('<').Append((character.LogicalIndex % 255 + 1).ToString("X2", CultureInfo.InvariantCulture)).Append("> ");
                if (i + 1 < stop)
                    operators.Append(N((character.Anchor + character.Advance - ordered[i + 1].Anchor) * 1000 / run.Size)).Append(' ');
            }
            operators.Append("] TJ\n");
            at = stop;
        }
        return operators.ToString();
    }

    private List<Character> Characters(Placement placement)
    {
        if (placement.Parts is { } parts)
        {
            var combined = new List<Character>();
            foreach (var (part, offset) in parts.OrderBy(p => p.Run.LogicalStart))
                foreach (var character in Characters(new Placement(part, placement.Ascent, placement.Descent)))
                    combined.Add(character with { Anchor = character.Anchor + offset, LogicalIndex = combined.Count });
            return combined;
        }
        var run = placement.Run;
        var groups = new Dictionary<uint, List<(int Glyph, float Pen)>>();
        var pen = 0f;
        for (var i = 0; i < run.Glyphs.Length; i++)
        {
            if (!groups.TryGetValue(run.Clusters[i], out var list))
                groups.Add(run.Clusters[i], list = []);
            list.Add((i, pen));
            pen += run.Advances[i];
        }
        var result = new List<Character>();
        using var font = new SKFont(run.Face, 1000) { SkewX = run.Format.Italic == true ? -0.2f : 0 };
        var byteOffset = 0u;
        var baseAnchor = 0f;
        var baseAdvance = run.Size * 0.5f;
        foreach (var rune in run.Text.EnumerateRunes())
        {
            groups.TryGetValue(byteOffset, out var group);
            var mark = IsMark(rune);
            var anchor = mark ? baseAnchor : group?.Min(g => g.Pen) ?? baseAnchor;
            var advance = group?.Sum(g => run.Advances[g.Glyph]) ?? 0;
            if (!mark)
            {
                baseAnchor = anchor;
                baseAdvance = Math.Max(advance, run.Size * 0.1f);
            }
            var program = new StringBuilder();
            var inkLeft = 0f;
            var inkRight = (mark ? baseAdvance : advance) * 1000 / run.Size;
            if (group is not null)
                foreach (var (glyph, _) in group)
                {
                    using var path = font.GetGlyphPath(run.Glyphs[glyph]);
                    if (path is null)
                        continue;
                    var dx = (run.Positions[glyph].X - anchor) * 1000 / run.Size;
                    var dy = run.Positions[glyph].Y * 1000 / run.Size;
                    inkLeft = Math.Min(inkLeft, path.Bounds.Left + dx);
                    inkRight = Math.Max(inkRight, path.Bounds.Right + dx);
                    program.Append("q 1 0 0 1 ").Append(N(dx)).Append(' ').Append(N(-dy)).Append(" cm\n")
                        .Append(Path(path)).Append("f Q\n");
                }
            var width = advance * 1000 / run.Size;
            inkRight = Math.Max(inkRight, inkLeft + 1);
            var header = $"{N(width)} 0 {N(inkLeft)} {N(-placement.Descent * 1000 / run.Size)} {N(inkRight)} {N(placement.Ascent * 1000 / run.Size)} d1\n";
            result.Add(new Character(rune, anchor, advance, header + program, result.Count, mark, inkLeft, inkRight));
            byteOffset += (uint)rune.Utf8SequenceLength;
        }
        return result;
    }

    private PdfDictionary Font(IReadOnlyList<Character> characters, Placement placement)
    {
        var run = placement.Run;
        var dictionary = new PdfDictionary(_document);
        _document.Internals.AddObject(dictionary);
        dictionary.Elements.SetName("/Type", "/Font");
        dictionary.Elements.SetName("/Subtype", "/Type3");
        var fontName = "/FileeArabicSubset_" + _next + ((run.Level & 1) != 0 ? "_R" : "_L");
        dictionary.Elements.SetName("/Name", fontName);
        using var metricsFont = new SKFont(run.Face, 1000);
        dictionary.Elements["/FontBBox"] = Array(characters.Min(c => c.Left), -placement.Descent * 1000 / run.Size,
            characters.Max(c => c.Right), placement.Ascent * 1000 / run.Size);
        dictionary.Elements["/FontMatrix"] = Array(0.001, 0, 0, 0.001, 0, 0);
        var descriptor = new PdfDictionary(_document);
        _document.Internals.AddObject(descriptor);
        descriptor.Elements.SetName("/Type", "/FontDescriptor");
        descriptor.Elements.SetName("/FontName", fontName);
        descriptor.Elements.SetInteger("/Flags", 4);
        descriptor.Elements["/FontBBox"] = Array(characters.Min(c => c.Left), -placement.Descent * 1000 / run.Size,
            characters.Max(c => c.Right), placement.Ascent * 1000 / run.Size);
        descriptor.Elements.SetReal("/Ascent", placement.Ascent * 1000 / run.Size);
        descriptor.Elements.SetReal("/Descent", -placement.Descent * 1000 / run.Size);
        descriptor.Elements.SetReal("/CapHeight", metricsFont.Metrics.CapHeight);
        descriptor.Elements.SetInteger("/ItalicAngle", 0);
        descriptor.Elements.SetInteger("/StemV", 80);
        dictionary.Elements.SetReference("/FontDescriptor", descriptor);
        var procedures = new PdfDictionary(_document);
        var differences = new PdfArray(_document);
        differences.Elements.Add(new PdfInteger(1));
        var widths = new PdfArray(_document);
        var unicode = new List<string>();
        for (var i = 0; i < characters.Count; i++)
        {
            var character = characters[i];
            if (!_programs.TryGetValue(character.Program, out var program))
            {
                program = Stream(character.Program);
                _programs.Add(character.Program, program);
            }
            var name = "/g" + (i + 1);
            procedures.Elements.SetReference(name, program);
            differences.Elements.Add(new PdfName(name));
            widths.Elements.Add(new PdfReal(character.Advance * 1000 / run.Size));
            unicode.Add($"<{(i + 1).ToString("X2", CultureInfo.InvariantCulture)}> <{Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(character.Unicode.ToString()))}>");
        }
        var cmap = Stream("/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n" +
            "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n" +
            "/CMapName /FileeArabic def /CMapType 2 def\n1 begincodespacerange <00> <FF> endcodespacerange\n" +
            string.Concat(unicode.Chunk(100).Select(chunk => chunk.Length + " beginbfchar\n" + string.Join('\n', chunk) + "\nendbfchar\n")) +
            "endcmap CMapName currentdict /CMap defineresource pop end end\n");
        var encoding = new PdfDictionary(_document);
        encoding.Elements.SetName("/Type", "/Encoding");
        encoding.Elements["/Differences"] = differences;
        dictionary.Elements["/Encoding"] = encoding;
        dictionary.Elements["/CharProcs"] = procedures;
        dictionary.Elements["/Widths"] = widths;
        dictionary.Elements.SetInteger("/FirstChar", 1);
        dictionary.Elements.SetInteger("/LastChar", characters.Count);
        dictionary.Elements["/Resources"] = new PdfDictionary(_document);
        dictionary.Elements.SetReference("/ToUnicode", cmap);
        return dictionary;
    }

    private PdfArray Array(params double[] values) => new(_document, values.Select(v => (PdfItem)new PdfReal(v)).ToArray());
    private PdfDictionary Stream(string content)
    {
        var stream = new PdfDictionary(_document);
        stream.CreateStream(Encoding.ASCII.GetBytes(content));
        _document.Internals.AddObject(stream);
        return stream;
    }

    private static string Path(SKPath path)
    {
        var output = new StringBuilder();
        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            string Point(SKPoint p) => N(p.X) + " " + N(-p.Y);
            switch (verb)
            {
                case SKPathVerb.Move: output.Append(Point(points[0])).Append(" m\n"); break;
                case SKPathVerb.Line: output.Append(Point(points[1])).Append(" l\n"); break;
                case SKPathVerb.Quad:
                    var p0 = points[0];
                    var p1 = points[1];
                    var p2 = points[2];
                    var a = new SKPoint(p0.X + (p1.X - p0.X) * 2 / 3, p0.Y + (p1.Y - p0.Y) * 2 / 3);
                    var b = new SKPoint(p2.X + (p1.X - p2.X) * 2 / 3, p2.Y + (p1.Y - p2.Y) * 2 / 3);
                    output.Append(Point(a)).Append(' ').Append(Point(b)).Append(' ').Append(Point(p2)).Append(" c\n");
                    break;
                case SKPathVerb.Cubic:
                    output.Append(Point(points[1])).Append(' ').Append(Point(points[2])).Append(' ').Append(Point(points[3])).Append(" c\n");
                    break;
                case SKPathVerb.Close: output.Append("h\n"); break;
                default: throw new NotSupportedException("Unsupported Arabic font outline curve.");
            }
        }
        return output.ToString();
    }

    private static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;
    private static double Number(CObject item) => item switch { CInteger i => i.Value, CReal r => r.Value, _ => throw new InvalidDataException("Unexpected PDF number.") };
    private static string N(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        foreach (var face in _retainedFaces.Values)
            face.Dispose();
        _marker.Dispose();
        _markerFace.Dispose();
    }
}
