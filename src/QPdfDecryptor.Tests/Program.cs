namespace QPdfDecryptor;

public static class Program
{
    public static async Task<int> Main()
    {
        var failures = 0;
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Catalog registers seven destinations in three groups", CatalogRegistersSevenDestinations),
            ("Catalog groups are Secure Arrange Enhance", CatalogGroupsAreStable),
        };

        foreach (var test in tests)
        {
            try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
            catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}"); }
        }
        return failures;
    }

    private static Task CatalogRegistersSevenDestinations()
    {
        var ids = OperationCatalog.All.Select(operation => operation.Id).ToList();
        Assert(ids.Count == 7 && ids.Distinct().Count() == 7, "expected 7 unique destinations");
        Assert(ids.Contains("decrypt") && ids.Contains("merge") && ids.Contains("watermark"), "missing core ops");
        return Task.CompletedTask;
    }

    private static Task CatalogGroupsAreStable()
    {
        var groups = OperationCatalog.All.Select(o => o.Group).Distinct().ToList();
        Assert(groups.SequenceEqual(["Secure", "Arrange", "Enhance"]), $"unexpected groups: {string.Join(',', groups)}");
        Assert(OperationCatalog.Find("rotate").Group == "Arrange", "rotate belongs in Arrange");
        return Task.CompletedTask;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
