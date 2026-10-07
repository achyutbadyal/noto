using Avalonia;
using Avalonia.Headless;
using Noto.App.Tests.Ui;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Noto.App.Tests.Ui;

public static class TestAppBuilder
{
    // Real Skia rendering (not the no-op drawing backend) so screenshots show what users see.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Noto.App.App>()
        .WithInterFont()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
