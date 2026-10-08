using Noto.Server.Config;

namespace Noto.Server.Tests;

public class DotEnvTests
{
    [Fact]
    public void Parses_pairs_comments_export_and_quotes()
    {
        var values = DotEnv.Parse(
            """
            # a comment
            JWT_SIGNING_KEY=abc123

            export PUBLIC_URL="https://noto.example.com"
            SLACK_CLIENT_SECRET='quoted # not a comment'
            DATA_DIR=./data # trailing comment
            not a pair
            """
        );

        values["JWT_SIGNING_KEY"].ShouldBe("abc123");
        values["PUBLIC_URL"].ShouldBe("https://noto.example.com");
        values["SLACK_CLIENT_SECRET"].ShouldBe("quoted # not a comment");
        values["DATA_DIR"].ShouldBe("./data");
        values.ContainsKey("not a pair").ShouldBeFalse();
    }

    [Fact]
    public void Load_sets_missing_variables_and_keeps_existing_ones()
    {
        var dir = Directory.CreateTempSubdirectory("dotenv-test");
        var path = Path.Combine(dir.FullName, ".env");
        const string Missing = "NOTO_DOTENV_TEST_MISSING";
        const string Present = "NOTO_DOTENV_TEST_PRESENT";
        try
        {
            File.WriteAllText(path, $"{Missing}=from-file\n{Present}=from-file\n");
            Environment.SetEnvironmentVariable(Present, "from-process");

            DotEnv.Load(path);

            Environment.GetEnvironmentVariable(Missing).ShouldBe("from-file");
            Environment.GetEnvironmentVariable(Present).ShouldBe("from-process");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Missing, null);
            Environment.SetEnvironmentVariable(Present, null);
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_ignores_a_missing_file()
    {
        Should.NotThrow(() => DotEnv.Load(Path.Combine(Path.GetTempPath(), "does-not-exist.env")));
    }
}
