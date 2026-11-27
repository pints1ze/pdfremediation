namespace PdfRemediation.Tests.Support;

/// <summary>Locates committed fixtures and repo files from the test output folder.</summary>
public static class Fixtures
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string Path(string relativeToFixtures) =>
        System.IO.Path.Combine(RepoRoot, "test", "fixtures", relativeToFixtures);

    public static byte[] Bytes(string relativeToFixtures) => File.ReadAllBytes(Path(relativeToFixtures));

    public static string Text(string relativeToFixtures) => File.ReadAllText(Path(relativeToFixtures));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "PdfRemediation.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find the repo root (PdfRemediation.slnx).");
    }
}
