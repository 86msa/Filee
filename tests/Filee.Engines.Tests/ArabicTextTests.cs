// Unicode conversion checks, separate from UI localization. Text preservation does not prove PDF shaping fidelity.

using Filee.Core.Conversion;
using Filee.Core.Presets;

namespace Filee.Engines.Tests;

public class ArabicTextTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    [Fact]
    public async Task Arabic_docx_filename_and_text_survive_conversion_to_plain_text()
    {
        const string text = "مرحباً بالعالم — تقرير Filee 2026، الإصدار 10.0";
        var input = new DocxBuilder().Paragraph($"""
            <w:p><w:pPr><w:bidi/><w:jc w:val="right"/></w:pPr>
              <w:r><w:rPr><w:rtl/><w:lang w:bidi="ar-IQ"/></w:rPr><w:t>{text}</w:t></w:r>
            </w:p>
            """).Save(Path.Combine(fx.NewFolder(), "تقرير عربي (Filee 2026).docx"));

        var job = await fx.ConvertAsync([input], new Preset { TargetFormat = "txt" });

        Assert.True(job.State == JobState.Completed, string.Join("; ", job.Files.Select(f => $"{f.ErrorKey} {f.ErrorDetail}")));
        var output = Assert.Single(job.Outputs);
        Assert.Equal("تقرير عربي (Filee 2026).txt", Path.GetFileName(output));
        Assert.Equal(text, (await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken)).Trim('\uFEFF', '\r', '\n'));
    }
}
