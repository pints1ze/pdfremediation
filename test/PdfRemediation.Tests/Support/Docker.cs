using PdfRemediation.Infrastructure.Checks;

namespace PdfRemediation.Tests.Support;

/// <summary>
/// Tests that need Docker carry <c>[Trait("Category", Docker.Category)]</c>.
/// Skip them when Docker isn't running with <c>dotnet test --filter Category!=Docker</c>.
/// </summary>
public static class Docker
{
    public const string Category = "Docker";

    public static VeraPdfValidator VeraPdf { get; } =
        new(VeraPdfOptions.DockerCompose(Path.Combine(Fixtures.RepoRoot, "compose.yaml")));
}
