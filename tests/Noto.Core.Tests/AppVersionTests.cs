using Noto.Core.Updates;

namespace Noto.Core.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("1.2.3+abc123", 1, 2, 3, null)]
    [InlineData("0.1.0-beta.1+sha", 0, 1, 0, "beta.1")]
    public void Parses_tags_and_informational_versions(
        string text,
        int major,
        int minor,
        int patch,
        string? pre
    ) => AppVersion.Parse(text).ShouldBe(new AppVersion(major, minor, patch, pre));

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("latest")]
    public void Rejects_non_versions(string text) =>
        AppVersion.TryParse(text, out _).ShouldBeFalse();

    [Theory]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("1.10.0", "1.9.9")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.10", "1.0.0-rc.9")]
    [InlineData("1.0.0-beta", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-alpha.1")]
    public void Orders_by_semver_precedence(string newer, string older)
    {
        AppVersion.Parse(newer).ShouldBeGreaterThan(AppVersion.Parse(older));
        AppVersion.Parse(older).ShouldBeLessThan(AppVersion.Parse(newer));
    }
}
