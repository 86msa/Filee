using System.Text;
using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Engines.Hwp.Hwpx;
using Filee.Engines.Pdf;
using PDFtoImage;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

#pragma warning disable CA1416

namespace Filee.Engines.Tests;

[Collection("Arabic PDF native")]
public sealed class ArabicPdfTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    internal static readonly string[] Samples =
    [
        "السلام عليكم ورحمة الله وبركاته",
        "الجمهورية العراقية – وزارة التربية",
        "المبلغ الإجمالي: ١٢٥٬٠٠٠ دينار عراقي",
        "Invoice رقم 2026/125 – بغداد",
        "بِسْمِ اللَّهِ الرَّحْمَٰنِ الرَّحِيمِ",
    ];

    [Theory]
    [InlineData("txt")]
    [InlineData("md")]
    [InlineData("html")]
    [InlineData("docx")]
    [InlineData("xlsx")]
    [InlineData("pptx")]
    [InlineData("hwpx")]
    public async Task Arabic_documents_keep_logical_Unicode_and_render_as_embedded_text(string format)
    {
        var input = MakeFixture(format);
        var result = await fx.ConvertAsync([input], new Preset { TargetFormat = "pdf" });
        Assert.True(result.State == JobState.Completed, string.Join("; ", result.Files.Select(f => $"{f.ErrorKey}: {f.ErrorDetail}")));
        var pdf = Assert.Single(result.Outputs);
        Assert.Equal("عينة عربية.pdf", Path.GetFileName(pdf));
        SaveArtifacts(format, input, pdf);
        AssertPdf(pdf);
        var textJob = await fx.ConvertAsync([pdf], new Preset { TargetFormat = "txt" });
        Assert.True(textJob.State == JobState.Completed);
        var text = await File.ReadAllTextAsync(Assert.Single(textJob.Outputs), TestContext.Current.CancellationToken);
        foreach (var sample in Samples)
            Assert.Contains(sample, text);
    }

    [Fact]
    public async Task Long_paragraphs_tables_and_Arabic_paths_survive_pagination_merge_split_and_compression()
    {
        var document = new HDocument();
        var section = new HSection();
        document.Sections.Add(section);
        for (var i = 0; i < 12; i++)
        {
            var paragraph = new HParagraph();
            paragraph.Inlines.Add(new HText(string.Join(' ', Enumerable.Repeat(Samples[i % Samples.Length], 8)), default));
            section.Blocks.Add(paragraph);
        }
        var table = new HTable { ColumnCount = 2 };
        for (var i = 0; i < 25; i++)
        {
            var row = new HRow();
            foreach (var value in new[] { $"الطالب محمد أحمد — {i + 1}", "المبلغ ١٢٥٬٠٠٠ IQD (2026/10/19)" })
            {
                var cell = new HCell();
                var paragraph = new HParagraph();
                paragraph.Inlines.Add(new HText(value, default));
                cell.Blocks.Add(paragraph);
                row.Cells.Add(cell);
            }
            table.Rows.Add(row);
        }
        section.Blocks.Add(table);
        var input = Path.Combine(fx.NewFolder(), "تقرير طويل.hwpx");
        HwpxWriter.Write(document, input);
        var job = await fx.ConvertAsync([input], new Preset { TargetFormat = "pdf" });
        Assert.True(job.State == JobState.Completed, string.Join("; ", job.Files.Select(f => f.ErrorDetail)));
        var pdf = Assert.Single(job.Outputs);
        using (var read = Open(pdf))
        {
            Assert.True(read.NumberOfPages >= 3);
            Assert.Contains("الطالب محمد أحمد", LogicalText(pdf));
        }
        var merged = Path.Combine(fx.NewFolder(), "دمج عربي.pdf");
        await new PdfSharpConverter().MergeAsync([pdf, pdf], merged, TestContext.Current.CancellationToken);
        var split = await fx.ConvertAsync([merged], new Preset { TargetFormat = "pdf", Pdf = { SplitPages = true } });
        Assert.True(split.State == JobState.Completed);
        using var original = Open(pdf);
        Assert.Equal(original.NumberOfPages * 2, split.Outputs.Count());
        var before = LogicalText(merged);
        var compressed = await fx.ConvertAsync([merged], new Preset { TargetFormat = "pdf", Pdf = { Compression = PdfCompression.Ebook } });
        Assert.True(compressed.State == JobState.Completed);
        Assert.Equal(before, LogicalText(Assert.Single(compressed.Outputs)));
        Assert.Equal(before, string.Concat(split.Outputs.Select(LogicalText)));
        SaveArtifacts("long-table", input, pdf);
    }

    private string MakeFixture(string format)
    {
        var path = Path.Combine(fx.NewFolder(), $"عينة عربية.{format}");
        switch (format)
        {
            case "txt":
            case "md":
                File.WriteAllText(path, string.Join("\n\n", Samples), new UTF8Encoding(false));
                break;
            case "html":
                File.WriteAllText(path, "<html lang=\"ar\" dir=\"rtl\"><meta charset=\"utf-8\"><body>" +
                    string.Concat(Samples.Select(s => $"<p>{OpcWriter.Escape(s)}</p>")) + "</body></html>");
                break;
            case "docx":
                var docx = new DocxBuilder();
                foreach (var sample in Samples)
                    docx.Paragraph($"<w:p><w:pPr><w:bidi/><w:jc w:val=\"right\"/></w:pPr><w:r><w:t>{OpcWriter.Escape(sample)}</w:t></w:r></w:p>");
                docx.Save(path);
                break;
            case "xlsx":
                var xlsx = new XlsxBuilder();
                xlsx.Sheet("تقرير عربي", string.Concat(Samples.Select((s, i) => XlsxBuilder.Row(i + 1, xlsx.Text($"A{i + 1}", s)))),
                    before: "<cols><col min=\"1\" max=\"1\" width=\"100\" customWidth=\"1\"/></cols>");
                xlsx.Save(path);
                break;
            case "pptx":
                var pptx = new PptxBuilder();
                pptx.Slide(pptx.Body(Samples.Select(s => (s, 0)).ToArray()));
                pptx.Save(path);
                break;
            case "hwpx":
                var document = new HDocument();
                var section = new HSection();
                document.Sections.Add(section);
                foreach (var sample in Samples)
                {
                    var paragraph = new HParagraph();
                    paragraph.Inlines.Add(new HText(sample, default));
                    section.Blocks.Add(paragraph);
                }
                HwpxWriter.Write(document, path);
                break;
        }
        return path;
    }

    [Fact]
    public async Task Arabic_SVG_text_stays_searchable_in_PDF()
    {
        var input = Path.Combine(fx.NewFolder(), "رسم عربي.svg");
        File.WriteAllText(input, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"800\" height=\"400\">" +
            string.Concat(Samples.Select((s, i) => $"<text direction=\"rtl\" x=\"760\" y=\"{50 + i * 60}\" font-size=\"20\">{OpcWriter.Escape(s)}</text>")) + "</svg>");
        var result = await fx.ConvertAsync([input], new Preset { TargetFormat = "pdf" });
        Assert.True(result.State == JobState.Completed, string.Join("; ", result.Files.Select(f => f.ErrorDetail)));
        var pdf = Assert.Single(result.Outputs);
        SaveArtifacts("svg", input, pdf);
        AssertPdf(pdf);
    }

    [Theory]
    [InlineData(0, "السلام عليكم ورحمة الله وبركاته")]
    [InlineData(1, "بِسْمِ اللَّهِ الرَّحْمَٰنِ الرَّحِيمِ")]
    [InlineData(2, "لَا إلهَ إلّا الله، آلاء أحمد وإيمان")]
    public void Shaped_PDF_matches_independent_Raqm_reference(int index, string text)
    {
        var path = Path.Combine(fx.NewFolder(), "مرجع بصري.pdf");
        using var layout = new ArabicTextLayout();
        using var glyphs = new ArabicPdfGlyphWriter();
        var line = layout.Wrap(text, 1050, 48, rtl: true)[0];
        using (var document = SKDocument.CreatePdf(path))
        {
            var canvas = document.BeginPage(1100, 160);
            canvas.Clear(SKColors.White);
            ArabicTextLayout.Draw(canvas, line, 1050 - line.Width, 100, SKColors.Black, glyphs);
            document.EndPage();
            document.Close();
        }
        glyphs.Finish(path);
        using var actual = Conversion.ToImage(File.ReadAllBytes(path), options: new RenderOptions(Dpi: 72));
        using var expected = SKBitmap.Decode(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Arabic", $"reference-{index}.png"));
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        static bool Ink(SKBitmap image, int x, int y) => x >= 0 && y >= 0 && x < image.Width && y < image.Height
            && image.GetPixel(x, y).Red < 128;
        static double Difference(SKBitmap first, SKBitmap second)
        {
            var ink = 0;
            var unmatched = 0;
            for (var y = 0; y < first.Height; y++)
                for (var x = 0; x < first.Width; x++)
                    if (Ink(first, x, y))
                    {
                        ink++;
                        var found = false;
                        for (var dy = -2; dy <= 2 && !found; dy++)
                            for (var dx = -2; dx <= 2 && !found; dx++)
                                found = Ink(second, x + dx, y + dy);
                        if (!found) unmatched++;
                    }
            return unmatched / (double)Math.Max(1, ink);
        }
        Assert.True(Difference(actual, expected) < 0.025, "PDF has extra/displaced glyph ink.");
        Assert.True(Difference(expected, actual) < 0.025, "PDF has missing/disconnected glyph ink.");
        var root = Environment.GetEnvironmentVariable("FILEE_ARABIC_PDF_ARTIFACTS");
        if (!string.IsNullOrEmpty(root))
        {
            var folder = Path.Combine(root, "visual-" + index);
            Directory.CreateDirectory(folder);
            File.Copy(path, Path.Combine(folder, "after.pdf"), true);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Arabic", $"reference-{index}.png"), Path.Combine(folder, "reference.png"), true);
            using var image = SKImage.FromBitmap(actual);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(Path.Combine(folder, "after.png"));
            encoded.SaveTo(output);
        }
    }

    [Fact]
    public async Task Existing_external_Arabic_PDF_keeps_pixels_and_Unicode_after_merge_split_compress()
    {
        var input = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Arabic", "libreoffice-original.pdf");
        var merged = Path.Combine(fx.NewFolder(), "دمج ملف أصلي.pdf");
        await new PdfSharpConverter().MergeAsync([input, input], merged, TestContext.Current.CancellationToken);
        var split = await fx.ConvertAsync([merged], new Preset { TargetFormat = "pdf", Pdf = { SplitPages = true } });
        Assert.True(split.State == JobState.Completed);
        var compressed = await fx.ConvertAsync([input], new Preset { TargetFormat = "pdf", Pdf = { Compression = PdfCompression.Ebook } });
        Assert.True(compressed.State == JobState.Completed);
        using var before = Open(input);
        using var expected = Conversion.ToImage(File.ReadAllBytes(input), options: new RenderOptions(Dpi: 96));
        foreach (var output in split.Outputs.Concat(compressed.Outputs))
        {
            using var after = Open(output);
            Assert.Equal(before.GetPage(1).Text, after.GetPage(1).Text);
            using var actual = Conversion.ToImage(File.ReadAllBytes(output), options: new RenderOptions(Dpi: 96));
            Assert.Equal(expected.Width, actual.Width);
            Assert.Equal(expected.Height, actual.Height);
            Assert.Equal(expected.Pixels, actual.Pixels);
        }
        SaveArtifacts("external-pdf", input, Assert.Single(compressed.Outputs));
    }

    [Fact]
    public void Style_changes_inside_Arabic_words_keep_Unicode_and_decorations()
    {
        var document = new HDocument();
        var section = new HSection();
        document.Sections.Add(section);
        var paragraph = new HParagraph { Format = new HParaFormat(RightToLeft: true, Align: HAlign.Right) };
        paragraph.Inlines.Add(new HText("الس", new HCharFormat(Bold: true, Underline: true, Shade: "#FFFF00")));
        paragraph.Inlines.Add(new HText("لام", new HCharFormat(Italic: true, Strike: true, Color: "#003366")));
        paragraph.Inlines.Add(new HText(" عليكم — آلاء أحمد، إيمان (Invoice 2026/125) ١٢٥٬٠٠٠", default));
        section.Blocks.Add(paragraph);
        var path = Path.Combine(fx.NewFolder(), "تنسيق عربي.pdf");
        ArabicDocumentPdf.Write(document, path, TestContext.Current.CancellationToken);
        var text = PdfDocumentReader.PlainText(PdfDocumentReader.ReadText(path));
        Assert.Contains("السلام عليكم", text);
        Assert.Contains("آلاء أحمد، إيمان (Invoice 2026/125) ١٢٥٬٠٠٠", text);
        using var bitmap = Conversion.ToImage(File.ReadAllBytes(path));
        Assert.Contains(bitmap.Pixels, c => c.Red > 200 && c.Green > 200 && c.Blue < 100);
        SaveArtifacts("styles", path, path);
    }

    private static PdfDocument Open(string path) => PdfDocument.Open(path, new ParsingOptions { UseActualText = true });
    private static string LogicalText(string path)
    {
        using var pdf = Open(path);
        return string.Concat(PdfDocumentReader.PageTexts(path, Enumerable.Range(0, pdf.NumberOfPages).ToArray()));
    }

    private static void AssertPdf(string path)
    {
        using var pdf = Open(path);
        var logical = PdfDocumentReader.PlainText(PdfDocumentReader.ReadText(path));
        foreach (var sample in Samples)
            Assert.Contains(sample, logical);
        Assert.DoesNotContain("\uFFFD", logical);
        Assert.DoesNotContain("\u25A1", logical);
        Assert.All(pdf.GetPages(), p =>
        {
            Assert.NotEmpty(p.Letters);
            Assert.DoesNotContain(p.Letters, l => l.RenderingMode == UglyToad.PdfPig.Core.TextRenderingMode.Neither);
            Assert.Contains(p.Letters, l => l.FontName?.StartsWith("FileeArabicSubset_", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(p.Letters, l => l.FontName?.Contains("NotoSans-Medium", StringComparison.Ordinal) == true);
        });
        ArabicPdfNative.AssertSearchAndVowels(path);
        // Rendering is a separate gate. Text extraction alone would accept a PDF with invisible/missing glyphs.
        using var bitmap = Conversion.ToImage(File.ReadAllBytes(path), options: new RenderOptions(Dpi: 96));
        Assert.True(bitmap.Width > 300 && bitmap.Height > 300);
        var dark = bitmap.Pixels.Count(c => c.Red < 200 && c.Green < 200 && c.Blue < 200);
        Assert.True(dark > 500);
    }

    private static void SaveArtifacts(string name, string input, string pdf)
    {
        var root = Environment.GetEnvironmentVariable("FILEE_ARABIC_PDF_ARTIFACTS");
        if (string.IsNullOrEmpty(root))
            return;
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        var inputCopy = Path.Combine(folder, Path.GetFileName(input));
        if (Path.GetFullPath(input) != Path.GetFullPath(inputCopy)) File.Copy(input, inputCopy, true);
        File.Copy(pdf, Path.Combine(folder, "after.pdf"), true);
        var bytes = File.ReadAllBytes(pdf);
        var count = Conversion.GetPageCount(bytes);
        for (var page = 0; page < count; page++)
        {
            using var bitmap = Conversion.ToImage(bytes, page: page, options: new RenderOptions(Dpi: 144));
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(Path.Combine(folder, page == 0 ? "after.png" : $"after-{page + 1}.png"));
            encoded.SaveTo(output);
        }
        File.WriteAllText(Path.Combine(folder, "logical-text.txt"), LogicalText(pdf), new UTF8Encoding(false));
    }
}
