using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Filee.App.Services;

namespace Filee.App.Tests;

public class LocalizationResourceTests
{
    [AvaloniaFact]
    public void Strings_are_available_as_application_resources()
    {
        TestServices.EnsureInitialized("ko");
        Assert.True(Application.Current!.TryGetResource("home.title", null, out var value));
        Assert.Equal("홈", value);
    }

    [AvaloniaFact]
    public void DynamicResource_in_xaml_resolves_and_follows_language_changes()
    {
        TestServices.EnsureInitialized("en");
        var text = new TextBlock();
        text.Bind(TextBlock.TextProperty, text.GetResourceObservable("home.title"));
        var window = new Window { Content = text };
        window.Show();
        Assert.Equal("Home", text.Text);

        TestServices.EnsureInitialized("ko");
        Assert.Equal("홈", text.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Open_and_new_windows_popups_and_resources_follow_arabic_then_every_ltr_language()
    {
        TestServices.EnsureInitialized("en");
        var text = new TextBlock();
        text.Bind(TextBlock.TextProperty, text.GetResourceObservable("home.title"));
        var toggle = new ToggleSwitch();
        var window = new Window { Content = new StackPanel { Children = { text, toggle } } };
        window.Show();
        try
        {
            TestServices.EnsureInitialized("ar");
            Assert.Equal("الرئيسية", text.Text);
            Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
            Assert.Equal(FlowDirection.RightToLeft, text.FlowDirection);
            Assert.Contains("Noto Sans Arabic", window.FontFamily.ToString());
            var loc = AppHost.Get<LocalizationService>();
            Assert.Equal(loc["triggers.enabled"], toggle.OnContent);
            Assert.Equal(loc["watch.status_off"], toggle.OffContent);
            Assert.True(Application.Current!.TryGetResource("AppForwardArrow", null, out var arrow));
            Assert.Equal("←", arrow);
            Assert.True(Application.Current.TryGetResource("AppPopupPlacement", null, out var placement));
            Assert.Equal(PlacementMode.BottomEdgeAlignedRight, placement);
            var filename = "تقرير (Filee 2026).docx";
            Assert.Contains("\u2068" + filename + "\u2069", loc.Format("presets.delete_confirm", filename));

            var popup = new Window { Content = new MenuFlyoutPresenter() };
            popup.Show();
            Assert.Equal(FlowDirection.RightToLeft, popup.FlowDirection);
            foreach (var (code, _) in LocalizationService.Languages.Where(l => l.Code != "ar"))
            {
                TestServices.EnsureInitialized(code);
                Assert.Equal(FlowDirection.LeftToRight, window.FlowDirection);
                Assert.Equal(FlowDirection.LeftToRight, Assert.IsType<MenuFlyoutPresenter>(popup.Content).FlowDirection);
                Assert.Equal(loc["home.title"], text.Text);
                Assert.Equal(loc["triggers.enabled"], toggle.OnContent);
            }
            popup.Close();
        }
        finally
        {
            window.Close();
            TestServices.EnsureInitialized("en");
        }
    }
}
