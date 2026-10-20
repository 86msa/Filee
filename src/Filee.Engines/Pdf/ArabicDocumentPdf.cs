// The rhwp 0.8.7 compositor explicitly rejects RTL shaping. Use the shared document model for Arabic PDFs,
// with the same static font on every OS, real positioned text, and pagination independent of the UI direction.
using System.IO.Compression;
using System.Text;
using System.Xml;
using Filee.Core.Conversion;
using Filee.Engines.Hwp.Hwpx;
using SkiaSharp;

namespace Filee.Engines.Pdf;

internal sealed class ArabicDocumentPdf : IDisposable
{
    private readonly ArabicTextLayout _text = new();
    private readonly ArabicPdfGlyphWriter _glyphs = new();
    private readonly List<Sheet> _sheets = [];
    private Sheet _sheet = null!;
    private HPage _page = DefaultPage;
    private float _y;
    private HSection _section = null!;
    private bool _paginate = true;
    private int _pageNumber;
    private float Bottom => (_page.Height - _page.Bottom - _page.Footer) / 100f;
    private static readonly HPage DefaultPage = new(59528, 84189, 5400, 5400, 5400, 5400, 0, 0, 0);

    private sealed record Sheet(float Width, float Height, HSection Section, HPage Page)
    {
        public List<Action<SKCanvas>> Paint { get; } = [];
    }

    public static bool ContainsArabic(HDocument document) => document.Sections.Any(s =>
        HDocumentWalker.Inlines(s.Blocks.Concat(s.Headers.Concat(s.Footers).SelectMany(h => h.Blocks)))
            .OfType<HText>().Any(t => ArabicTextLayout.ContainsArabic(t.Text)));

    internal static bool HasArabicSource(string path, string format)
    {
        if (format is "txt" or "md" or "html")
            return ArabicTextLayout.ContainsArabic(DocumentReaders.DecodeText(File.ReadAllBytes(path)));
        if (format is not ("hwpx" or "docx" or "docm" or "dotx" or "dotm" or "xlsx" or "xlsm" or "xltx"
            or "pptx" or "pptm" or "potx" or "ppsx"))
            return false;
        using var zip = ZipFile.OpenRead(path);
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = entry.Open();
            using var xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (xml.Read())
                if (xml.NodeType is XmlNodeType.Text or XmlNodeType.CDATA && ArabicTextLayout.ContainsArabic(xml.Value))
                    return true;
        }
        return false;
    }

    /// <summary>The same shaping path also covers a built-in document routed directly to LibreOffice.</summary>
    internal static async Task<IReadOnlyList<string>?> TryConvertAsync(ConversionStep step, IProgress<double>? progress, CancellationToken ct)
    {
        if (step.To != "pdf" || !HasArabicSource(step.InputPath, step.From))
            return null;
        var model = await DocumentReaders.ReadAsync(step.InputPath, step.From, step.WorkDirectory, ct);
        if (!ContainsArabic(model))
            return null;
        var temporary = Path.Combine(step.WorkDirectory, $"arabic-{Guid.NewGuid():N}.pdf");
        Write(model, temporary, ct, step.Preset.Document.PdfA);
        var target = step.Output.Allocate("pdf");
        if (target is null)
            return [];
        File.Move(temporary, target, true);
        progress?.Report(1);
        return [target];
    }

    public static void Write(HDocument document, string output, CancellationToken ct, bool pdfA = false)
    {
        using var writer = new ArabicDocumentPdf();
        foreach (var section in document.Sections)
        {
            ct.ThrowIfCancellationRequested();
            writer._page = section.Page ?? DefaultPage;
            writer._section = section;
            writer.NewSheet();
            writer.Blocks(section.Blocks, writer._page.Left / 100f, writer._page.TextWidth / 100f, ct);
        }
        if (writer._sheets.Count == 0)
            writer.NewSheet();
        writer.HeadersAndFooters(ct);
        using var pdf = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata
        {
            Title = document.Title ?? "",
            Creator = "Filee — Arabic OpenType/HarfBuzz",
            PdfA = pdfA,
        });
        foreach (var sheet in writer._sheets)
        {
            ct.ThrowIfCancellationRequested();
            var canvas = pdf.BeginPage(sheet.Width, sheet.Height);
            foreach (var draw in sheet.Paint)
                draw(canvas);
            pdf.EndPage();
        }
        pdf.Close();
        pdf.Dispose();
        writer._glyphs.Finish(output);
    }

    private void NewSheet()
    {
        _sheet = new Sheet(_page.Width / 100f, _page.Height / 100f, _section, _page);
        _sheets.Add(_sheet);
        _pageNumber = _sheets.Count;
        _y = (_page.Top + _page.Header) / 100f;
    }

    private void Ensure(float height)
    {
        if (_paginate && _y + height > Bottom && _y > (_page.Top + _page.Header) / 100f + 0.1f)
            NewSheet();
    }

    private void HeadersAndFooters(CancellationToken ct)
    {
        _paginate = false;
        for (var i = 0; i < _sheets.Count; i++)
        {
            _sheet = _sheets[i];
            _page = _sheet.Page;
            _section = _sheet.Section;
            _pageNumber = i + 1;
            var first = i == 0 || _sheets[i - 1].Section != _section;
            foreach (var header in _section.Headers.Where(h => Applies(h.Pages, _pageNumber) && !(first && _section.HideFirstHeader)))
            {
                _y = _page.Top / 100f;
                Blocks(header.Blocks, _page.Left / 100f, _page.TextWidth / 100f, ct);
            }
            foreach (var footer in _section.Footers.Where(h => Applies(h.Pages, _pageNumber) && !(first && _section.HideFirstFooter)))
            {
                _y = (_page.Height - _page.Bottom - _page.Footer) / 100f;
                Blocks(footer.Blocks, _page.Left / 100f, _page.TextWidth / 100f, ct);
            }
        }
    }

    private static bool Applies(HPageType type, int number) =>
        type == HPageType.Both || type == HPageType.Even && number % 2 == 0 || type == HPageType.Odd && number % 2 != 0;

    private void Blocks(IEnumerable<HBlock> blocks, float x, float width, CancellationToken ct)
    {
        foreach (var block in blocks)
        {
            ct.ThrowIfCancellationRequested();
            switch (block)
            {
                case HParagraph paragraph:
                    Paragraph(paragraph, x, width);
                    break;
                case HTable table:
                    Table(table, x, width, ct);
                    break;
            }
        }
    }

    private sealed record ParagraphLines(IReadOnlyList<ArabicTextLayout.Line> Lines, HAlign? Align, bool ExplicitDirection, SKColor Color,
        float Left, float Right, float Before, float After);

    private ParagraphLines Layout(HParagraph paragraph, float width)
    {
        var format = paragraph.Format;
        var runs = HDocumentWalker.InlinesOf(paragraph.Inlines).OfType<HText>().ToArray();
        var size = Math.Max(1, (runs.FirstOrDefault()?.Format.Size ?? paragraph.MarkFormat.Size ?? 1200) / 100f);
        var bold = paragraph.HeadingLevel > 0 || runs.FirstOrDefault()?.Format.Bold == true;
        var left = (format.Left ?? 0) / 100f;
        var right = (format.Right ?? 0) / 100f;
        var color = SKColor.TryParse(runs.FirstOrDefault()?.Format.Color, out var parsed) ? parsed : SKColors.Black;
        var content = new StringBuilder();
        var formats = new List<HCharFormat>();
        foreach (var inline in HDocumentWalker.InlinesOf(paragraph.Inlines))
        {
            var (value, charFormat) = inline switch
            {
                HText text => (text.Text, text.Format),
                HLineBreak line => ("\n", line.Format),
                HTab tab => ("    ", tab.Format),
                HField field => (field.Kind == HFieldKind.PageNumber ? _pageNumber.ToString() : _sheets.Count.ToString(), field.Format),
                _ => ("", default(HCharFormat)),
            };
            content.Append(value);
            formats.AddRange(Enumerable.Repeat(charFormat, value.Length));
        }
        var valueText = content.ToString();
        var rtl = format.RightToLeft;
        if (rtl is null)
        {
            // Plain text and HWPX have no BiDi paragraph property. Infer the document language from letters;
            // English paragraphs stay LTR, and an Arabic invoice label may start with the Latin word Invoice.
            var letters = valueText.EnumerateRunes().Where(Rune.IsLetter).ToArray();
            var arabic = letters.Count(r => ArabicTextLayout.ContainsArabic(r.ToString()));
            rtl = arabic > 0 && arabic * 2 >= letters.Length;
        }
        var lines = _text.Wrap(valueText, Math.Max(1, width - left - right), size, bold, rtl: rtl, formats: formats);
        return new ParagraphLines(lines, format.Align, format.RightToLeft.HasValue, color, left, right,
            (format.Before ?? 0) / 100f, (format.After ?? 600) / 100f);
    }

    private void Paragraph(HParagraph paragraph, float x, float width)
    {
        if (paragraph.PageBreakBefore && _sheet.Paint.Count > 0)
            NewSheet();
        var layout = Layout(paragraph, width);
        _y += layout.Before;
        foreach (var line in layout.Lines)
        {
            Ensure(line.Height);
            AddLine(layout, line, x, _y, width);
            _y += line.Height;
        }
        _y += layout.After;
        // Pictures and text boxes remain document objects, never a rasterized substitute for the text.
        foreach (var inline in HDocumentWalker.InlinesOf(paragraph.Inlines))
        {
            if (inline is HImage image)
                Image(image, x, width);
            else if (inline is HTextBox box)
                TextBox(box, x, width);
            else if (inline is HShape shape)
                Shape(shape, x, width);
            else if (inline is HNote note)
                Blocks(note.Blocks, x, width, CancellationToken.None);
        }
    }

    private void AddLine(ParagraphLines paragraph, ArabicTextLayout.Line line, float x, float y, float width)
    {
        var available = width - paragraph.Left - paragraph.Right;
        var align = paragraph.Align ?? (line.Rtl ? HAlign.Right : HAlign.Left);
        var offset = align switch
        {
            HAlign.Right => available - line.Width,
            HAlign.Center => (available - line.Width) / 2,
            _ => 0,
        };
        var px = x + paragraph.Left + Math.Max(0, offset);
        var baseline = y + line.Ascent;
        _sheet.Paint.Add(c => ArabicTextLayout.Draw(c, line, px, baseline, paragraph.Color, _glyphs));
    }

    private void Table(HTable table, float x, float width, CancellationToken ct)
    {
        if (table.PageBreakBefore && _sheet.Paint.Count > 0)
            NewSheet();
        Blocks(table.Caption, x, width, ct);
        var count = Math.Max(1, table.ColumnCount);
        var weights = table.ColumnWidths?.Select(v => (double)v).ToArray()
            ?? table.RelativeWidths?.ToArray() ?? Enumerable.Repeat(1d, count).ToArray();
        var sum = weights.Sum();
        var widths = Enumerable.Range(0, count).Select(i =>
            sum > 0 && i < weights.Length ? (float)(weights[i] / sum * width) : width / count).ToArray();
        var padding = table.CellMargin ?? new HInsets(400, 400, 300, 300);
        var top = padding.Top / 100f;
        var bottom = padding.Bottom / 100f;
        foreach (var row in table.Rows)
        {
            ct.ThrowIfCancellationRequested();
            var cells = new List<(HCell Cell, float X, float Width, Sheet Content, float Height)>();
            var column = 0;
            var height = Math.Max(1, (row.Height ?? 0) / 100f);
            foreach (var cell in row.Cells)
            {
                if (cell.Column >= 0)
                    column = cell.Column;
                var cellWidth = widths.Skip(column).Take(Math.Max(1, cell.ColSpan)).Sum();
                if (cellWidth <= 0)
                    cellWidth = width / count;
                var innerWidth = Math.Max(1, cellWidth - (padding.Left + padding.Right) / 100f);
                var savedSheet = _sheet;
                var savedY = _y;
                var savedPaginate = _paginate;
                var content = new Sheet(_sheet.Width, _sheet.Height, _section, _page);
                _sheet = content;
                _y = 0;
                _paginate = false;
                Blocks(cell.Blocks, 0, innerWidth, ct);
                var contentHeight = _y;
                _sheet = savedSheet;
                _y = savedY;
                _paginate = savedPaginate;
                height = Math.Max(height, top + bottom + contentHeight);
                cells.Add((cell, x + widths.Take(column).Sum(), cellWidth, content, contentHeight));
                column += Math.Max(1, cell.ColSpan);
            }
            Ensure(height);
            var rowY = _y;
            foreach (var cell in cells)
            {
                var rect = SKRect.Create(cell.X, rowY, cell.Width, height);
                var fill = SKColor.TryParse(cell.Cell.Fill, out var parsed) ? parsed : SKColors.White;
                _sheet.Paint.Add(c =>
                {
                    using var brush = new SKPaint { Color = fill };
                    c.DrawRect(rect, brush);
                    brush.Color = SKColors.Gray;
                    brush.Style = SKPaintStyle.Stroke;
                    brush.StrokeWidth = 0.35f;
                    c.DrawRect(rect, brush);
                });
                var contentHeight = cell.Height;
                var y = rowY + top + (cell.Cell.VerticalAlign switch
                {
                    HVerticalAlign.Center => Math.Max(0, (height - top - bottom - contentHeight) / 2),
                    HVerticalAlign.Bottom => Math.Max(0, height - top - bottom - contentHeight),
                    _ => 0,
                });
                var contentY = y;
                _sheet.Paint.Add(c =>
                {
                    c.Save();
                    c.Translate(cell.X + padding.Left / 100f, contentY);
                    foreach (var draw in cell.Content.Paint) draw(c);
                    c.Restore();
                });
            }
            _y += height;
        }
    }

    private SKRect ObjectRect(HAnchor? anchor, float x, float width, float height)
    {
        if (anchor is null)
            return SKRect.Create(x, _y, width, height);
        var px = anchor.HorizontalRelativeTo == "PAPER" ? 0 : x;
        var py = anchor.VerticalRelativeTo == "PAPER" ? 0
            : anchor.VerticalRelativeTo == "PAGE" ? (_page.Top + _page.Header) / 100f : _y;
        if (anchor.HorizontalOffset == 0)
            px += anchor.HorizontalAlign switch
            {
                "CENTER" => (_page.TextWidth / 100f - width) / 2,
                "RIGHT" or "OUTSIDE" => _page.TextWidth / 100f - width,
                _ => 0,
            };
        return SKRect.Create(px + anchor.HorizontalOffset / 100f, py + anchor.VerticalOffset / 100f, width, height);
    }

    private void DrawShape(SKRect rect, HShapeKind kind, string? fill, HGradient? gradient, HBorder border,
        int corners = 0, bool flipX = false, bool flipY = false)
    {
        _sheet.Paint.Add(canvas =>
        {
            using var paint = new SKPaint { IsAntialias = true };
            using var shader = gradient is { Colors.Count: > 1 } ? SKShader.CreateLinearGradient(
                new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
                gradient.Colors.Select(c => SKColor.TryParse(c, out var color) ? color : SKColors.Transparent).ToArray(),
                null, SKShaderTileMode.Clamp) : null;
            void Draw()
            {
                if (kind == HShapeKind.Line)
                    canvas.DrawLine(flipX ? rect.Right : rect.Left, flipY ? rect.Bottom : rect.Top,
                        flipX ? rect.Left : rect.Right, flipY ? rect.Top : rect.Bottom, paint);
                else if (kind == HShapeKind.Ellipse) canvas.DrawOval(rect, paint);
                else if (corners > 0) canvas.DrawRoundRect(rect, rect.Width * corners / 200f, rect.Height * corners / 200f, paint);
                else canvas.DrawRect(rect, paint);
            }
            if (kind != HShapeKind.Line && (shader is not null || SKColor.TryParse(fill, out _)))
            {
                paint.Color = SKColor.TryParse(fill, out var color) ? color : SKColors.White;
                paint.Shader = shader;
                Draw();
                paint.Shader = null;
            }
            if (border.Style != HBorderStyle.None)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = (float)(border.WidthMm * 72 / 25.4);
                paint.Color = SKColor.TryParse(border.Color, out var color) ? color : SKColors.Black;
                Draw();
            }
        });
    }

    private void Shape(HShape shape, float x, float width)
    {
        var rect = ObjectRect(shape.Anchor, x, shape.Width / 100f, shape.Height / 100f);
        DrawShape(rect, shape.Kind, shape.Fill, shape.Gradient, shape.Line, shape.CornerRatio, shape.FlipHorizontal, shape.FlipVertical);
        if (shape.Anchor is null) _y = rect.Bottom;
    }

    private void TextBox(HTextBox box, float x, float width)
    {
        var rect = ObjectRect(box.Anchor, x, box.Width / 100f, box.Height / 100f);
        DrawShape(rect, box.Shape, box.Fill, box.Gradient, box.Line, box.CornerRatio);
        var savedY = _y;
        var savedPaginate = _paginate;
        _paginate = false;
        _y = rect.Top + box.Padding.Top / 100f;
        Blocks(box.Blocks, rect.Left + box.Padding.Left / 100f,
            Math.Max(1, rect.Width - (box.Padding.Left + box.Padding.Right) / 100f), CancellationToken.None);
        _paginate = savedPaginate;
        _y = box.Anchor is null ? Math.Max(rect.Bottom, _y) : savedY;
    }

    private void Image(HImage image, float x, float width)
    {
        using var bitmap = SKBitmap.Decode(image.Path);
        if (bitmap is null)
            return;
        var naturalWidth = image.Width is { } w ? w / 100f : bitmap.Width * 0.75f;
        var naturalHeight = image.Height is { } h ? h / 100f : bitmap.Height * 0.75f;
        var scale = Math.Min(1, Math.Min(width / naturalWidth,
            (Bottom - (_page.Top + _page.Header) / 100f) / naturalHeight));
        var targetWidth = naturalWidth * scale;
        var targetHeight = naturalHeight * scale;
        if (image.Anchor is null) Ensure(targetHeight);
        var rect = ObjectRect(image.Anchor, x, targetWidth, targetHeight);
        var path = image.Path;
        _sheet.Paint.Add(c =>
        {
            using var decoded = SKBitmap.Decode(path);
            c.DrawBitmap(decoded, rect);
        });
        if (image.Anchor is null) _y += targetHeight;
    }

    public void Dispose()
    {
        _text.Dispose();
        _glyphs.Dispose();
    }
}
