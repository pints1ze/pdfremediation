using PdfRemediation.Domain.AccessibilityIr;

namespace PdfRemediation.Infrastructure.TagWriting;

/// <summary>
/// Writes a new tag tree into a copy of a PDF from valid Accessibility IR (ADR 0003).
/// Taking an <see cref="IrDocument"/> means invalid IR is rejected by <see cref="IrParser"/> before a writer runs.
/// </summary>
public interface ITagWriter
{
    TagWriterResult Write(byte[] originalPdf, IrDocument ir);
}

/// <summary>The tagged PDF, or null when the document could not be tagged at all, plus what happened.</summary>
public sealed record TagWriterResult(byte[]? TaggedPdf, TagWriterDiagnostics Diagnostics);

public sealed record TagWriterDiagnostics(int ElementsApplied, int ElementsSkipped, IReadOnlyList<TagWriterError> Errors);

/// <summary>A stable error code, with the page and Element it concerns when there is one.</summary>
public sealed record TagWriterError(string Code, int? PageNumber, string? ElementId, string Message);
