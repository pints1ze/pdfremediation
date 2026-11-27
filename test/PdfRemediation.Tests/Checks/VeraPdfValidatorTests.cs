using PdfRemediation.Tests.Support;

namespace PdfRemediation.Tests.Checks;

[Trait("Category", Docker.Category)]
public class VeraPdfValidatorTests
{
    [Fact]
    public async Task An_untagged_pdf_fails_pdf_ua_1_with_normalized_failures()
    {
        var report = (await Docker.VeraPdf.ValidateAsync(Fixtures.Bytes("headings-and-paragraphs.pdf"))).Report;

        Assert.Equal("PDF/UA-1", report.Profile);
        Assert.False(report.Passed);
        Assert.Equal(report.Failures.Count, report.FailureCount);
        // ISO 14289-1 7.1, test 3: "Content shall be marked as Artifact or tagged as real content".
        // The fixture paints 38 words and one rule, none of them marked.
        var unmarked = Assert.Single(report.Failures, f => f.RuleId == "7.1-3");
        Assert.Equal(39, unmarked.Count);
        Assert.Contains("Artifact", unmarked.Message);
    }
}
