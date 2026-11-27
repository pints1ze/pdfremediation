using PdfRemediation.Domain.AccessibilityIr;
using PdfRemediation.Infrastructure.SourceObjects;

namespace PdfRemediation.Infrastructure.Reading;

/// <summary>One page as the reader sees it: a render, plus the page size in PDF points to map its geometry back.</summary>
public sealed record ReaderPage(int PageNumber, double Width, double Height, byte[] Png);

/// <summary>
/// What a reader adapter works from. The title and language go straight into the IR, because readers report neither.
/// </summary>
public sealed record ReaderInput(string Title, string Language, IReadOnlyList<ReaderPage> Pages, SourceObjectIndex SourceObjects);

/// <summary>A stable code for something the reader got wrong or could not do. Errors stop the IR; warnings don't.</summary>
public sealed record ReaderDiagnostic(string Code, int? PageNumber, string Message);

/// <summary>One provider call, live or replayed, with its estimated cost.</summary>
public sealed record ReaderCall(int PageNumber, string? RequestId, int Tries, int PagesProcessed, double EstimatedCostUsd, bool Replayed);

/// <summary>
/// Either valid IR (with the JSON it was parsed from) or the errors that stopped it.
/// <see cref="Calls"/> lists every provider call made, so cost is known even when the IR isn't.
/// </summary>
public sealed record ReaderResult(
    IrDocument? Ir,
    string? IrJson,
    IReadOnlyList<ReaderDiagnostic> Errors,
    IReadOnlyList<ReaderDiagnostic> Warnings,
    IReadOnlyList<ReaderCall> Calls);

/// <summary>Seam 3: page renders plus the Source object index in, Accessibility IR out.</summary>
public interface IReaderAdapter
{
    Task<ReaderResult> ReadAsync(ReaderInput input, CancellationToken cancellationToken);
}

/// <summary>A reader failure with a stable code, such as a provider error or a missing recording.</summary>
public sealed class ReaderException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
