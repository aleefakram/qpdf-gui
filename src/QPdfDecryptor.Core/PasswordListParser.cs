namespace QPdfDecryptor.Core;

public static class PasswordListParser
{
    public static IReadOnlyList<string> Parse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var text = File.ReadAllText(path);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var passwords = new List<string>();
        foreach (var line in text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            if (line.Length == 0 || !seen.Add(line))
            {
                continue;
            }

            passwords.Add(line);
        }

        return passwords;
    }
}
