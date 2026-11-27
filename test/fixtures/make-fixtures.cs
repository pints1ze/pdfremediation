#:package PdfPig@0.1.16
#:property PublishAot=false

// Regenerates the committed fixture PDFs. Run from this folder:
//   dotnet run make-fixtures.cs
// The fixtures are committed so tests never depend on this script; rerun it
// only when a fixture has to change, then update any hand-written IR that
// refers to its Source object IDs.

using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

var font = File.ReadAllBytes(Path.Combine("fonts", "Abel-Regular.ttf"));

WriteHeadingsAndParagraphs();

void WriteHeadingsAndParagraphs()
{
    var builder = new PdfDocumentBuilder();
    var abel = builder.AddTrueTypeFont(font);
    var page = builder.AddPage(612, 792);

    page.AddText("Harbour Library Annual Report", 24, new PdfPoint(72, 700), abel);
    page.AddText("The library served more readers this year than in any year before.", 12, new PdfPoint(72, 660), abel);
    page.AddText("Opening hours were extended on weekday evenings.", 12, new PdfPoint(72, 644), abel);
    page.AddText("Opening hours", 18, new PdfPoint(72, 600), abel);
    page.AddText("The library is open from nine until eight, Monday to Saturday.", 12, new PdfPoint(72, 570), abel);
    page.DrawLine(new PdfPoint(72, 60), new PdfPoint(540, 60), 0.5);
    page.AddText("Page 1", 9, new PdfPoint(72, 44), abel);

    File.WriteAllBytes("headings-and-paragraphs.pdf", builder.Build());
}
