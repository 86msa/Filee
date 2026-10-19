// Arabic layout regressions: live mirroring, mixed filenames, icon orientation and spatial donut input.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.Views;
using Filee.Core.Localization;

namespace Filee.App.Tests;

public class ArabicLayoutTests
{
    [AvaloniaTheory]
    [InlineData(820)]
    [InlineData(1080)]
    public void Navigation_moves_to_the_right_and_returns_on_a_live_language_change(int width)
    {
        TestServices.EnsureInitialized("ar");
        using var vm = new MainWindowViewModel(AppHost.Services, AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        var window = new MainWindow { DataContext = vm, Width = width, Height = 820 };
        window.Show();
        try
        {
            Pump();
            var nav = window.GetVisualDescendants().OfType<ListBox>().Single(c => c.Classes.Contains("nav"));
            var page = window.FindControl<ScrollViewer>("PageScroller")!;
            Assert.True(CenterX(nav, window) > CenterX(page, window));
            Assert.Equal(FlowDirection.RightToLeft, page.FlowDirection);
            Save(window, $"arabic-home-{width}.png");

            TestServices.EnsureInitialized("en");
            Pump();
            Assert.True(CenterX(nav, window) < CenterX(page, window));
            Assert.Equal(FlowDirection.LeftToRight, page.FlowDirection);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Mixed_filenames_and_long_messages_wrap_without_changing_the_filename()
    {
        TestServices.EnsureInitialized("ar");
        const string filename = "تقرير المشروع النهائي (Filee 2026) - النسخة العربية للمراجعة وإضافة الملاحظات قبل النشر.docx";
        var text = new SelectableTextBlock { Text = filename, TextWrapping = TextWrapping.Wrap, Name = "ArabicFilename" };
        var message = new TextBlock
        {
            Text = AppHost.Get<ILocalizer>()["triggers.linux_x11"],
            TextWrapping = TextWrapping.Wrap,
        };
        var icon = new PathIcon { Data = Geometry.Parse("M0,0 L20,10 L0,20 Z"), Width = 20, Height = 20 };
        var path = new TextBox { Text = "/home/user/مستندات/" + filename, Classes = { "technical" } };
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20), Children = { text, message, path, icon } };
        var window = new Window { Content = panel, Width = 360, SizeToContent = SizeToContent.Height };
        window.Show();
        try
        {
            Pump();
            Assert.Equal(filename, text.Text);
            Assert.Equal(FlowDirection.RightToLeft, text.FlowDirection);
            Assert.True(text.TextLayout.TextLines.Count > 1); // wraps instead of clipping a long filename to a fixed-height row
            Assert.True(message.Bounds.Height > text.Bounds.Height);
            Assert.Equal(FlowDirection.LeftToRight, path.FlowDirection);
            Assert.Equal(FlowDirection.LeftToRight, icon.FlowDirection);
            Assert.Equal("/home/user/مستندات/" + filename, path.Text);
            Save(window, "arabic-mixed-filename-long-message.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Donut_text_follows_the_language_and_pointer_and_keyboard_keep_physical_slice_positions(string language)
    {
        TestServices.EnsureInitialized(language);
        var donut = new DonutMenu
        {
            CenterTitle = "تقرير Filee 2026.pdf",
            Items =
            [
                new() { Id = "a", Label = "تدوير لليسار", Caption = "PNG" },
                new() { Id = "b", Label = "دمج PDF" },
                new() { Id = "c", Label = "إزالة الخلفية" },
                new() { Id = "d", Label = "DOCX" },
            ],
        };
        var window = new Window { Content = donut, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        try
        {
            Pump();
            Assert.Equal(language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, donut.FlowDirection);
            var center = new Point(donut.Bounds.Width / 2, donut.Bounds.Height / 2);
            Assert.True(donut.TranslatePoint(new Point(center.X + 40, center.Y), window)!.Value.X > CenterX(donut, window));
            var point = DonutGeometry.PointAt(center, donut.OuterRadius * 0.7, DonutGeometry.MidAngle(1, 4));
            Assert.Equal(new DonutHit(DonutHitKind.Segment, 1), donut.HitTest(point));
            var invoked = -1;
            donut.SegmentInvoked += (_, index) => invoked = index;
            var screenPoint = donut.TranslatePoint(point, window)!.Value;
            window.MouseMove(screenPoint);
            window.MouseDown(screenPoint, MouseButton.Left);
            window.MouseUp(screenPoint, MouseButton.Left);
            Assert.Equal(1, invoked);
            donut.HandleKey(Key.Right);
            donut.HandleKey(Key.Enter);
            Assert.Equal(2, invoked); // clockwise movement matches the unmirrored geometry
            var shaped = donut.MakeText("ملف Report 2026.pdf", new Typeface(window.FontFamily), 14, Brushes.Black, 120, 2);
            Assert.True(shaped.Width > 0);
            Assert.True(shaped.Height > 0);
            Save(window, $"donut-mixed-{language}.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static double CenterX(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value.X;

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static void Save(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(TestServices.ScreenshotDirectory, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
