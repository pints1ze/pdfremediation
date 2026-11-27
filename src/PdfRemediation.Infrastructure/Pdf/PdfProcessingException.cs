namespace PdfRemediation.Infrastructure.Pdf;

/// <summary>A PDF the pipeline cannot process, identified by a stable error code.</summary>
public sealed class PdfProcessingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
