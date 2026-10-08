namespace Noto.Server.Config;

// Minimal .env reader: KEY=VALUE per line, optional `export ` prefix, '#' comments, single or double quotes.
// Variables already set in the process environment win, so real env vars and CI secrets override the file.
public static class DotEnv
{
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line["export ".Length..].TrimStart();

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            values[line[..eq].Trim()] = Unquote(line[(eq + 1)..].Trim());
        }
        return values;
    }

    const string FileName = ".env";
    const int MaxParentLevels = 6;

    // `dotnet run --project` starts the app in the project folder, so the working directory alone would miss
    // the repo-root .env. Fall back to the nearest parent of the binary (bin/… sits below the repo root).
    public static string? Locate(string workingDirectory, string baseDirectory)
    {
        var inWorkingDirectory = Path.Combine(workingDirectory, FileName);
        if (File.Exists(inWorkingDirectory))
            return inWorkingDirectory;

        var dir = new DirectoryInfo(baseDirectory);
        for (var level = 0; dir is not null && level <= MaxParentLevels; level++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    // Set by the test host so a developer's .env never leaks secrets into tests.
    public const string OptOutVariable = "NOTO_NO_DOTENV";

    public static void LoadNearest(string workingDirectory, string baseDirectory)
    {
        if (Environment.GetEnvironmentVariable(OptOutVariable) is not null)
            return;
        if (Locate(workingDirectory, baseDirectory) is { } path)
            Load(path);
    }

    public static void Load(string path)
    {
        if (!File.Exists(path))
            return;
        foreach (var (key, value) in Parse(File.ReadAllText(path)))
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
    }

    static string Unquote(string value)
    {
        if (value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0])
            return value[1..^1];

        // Unquoted values may end with a " # comment".
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return (comment >= 0 ? value[..comment] : value).TrimEnd();
    }
}
