using System.Security;
using System.Text;
using PdfRemediation.Domain.AccessibilityIr;
using PdfRemediation.Infrastructure.Pdf;
using PdfRemediation.Infrastructure.SourceObjects;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace PdfRemediation.Infrastructure.TagWriting;

/// <summary>
/// Our tag writer, built on PdfPig. It wraps each Source object in marked content at operator boundaries,
/// builds a new structure tree under a Document root, and saves the result as an incremental update,
/// so the original bytes stay an unchanged prefix and decoded content changes only by insertion.
/// Content that no Element tags is marked as an Artifact.
/// </summary>
public sealed class PdfPigTagWriter : ITagWriter
{
    public TagWriterResult Write(byte[] originalPdf, IrDocument ir)
    {
        var errors = new List<TagWriterError>();
        try
        {
            using var file = PdfFile.Open(originalPdf);
            var sourceObjects = SourceObjectIndex.Scan(file);
            var claims = Claim(ir, sourceObjects.ToDictionary(o => o.Id, StringComparer.Ordinal), errors, out var applied, out var skipped);

            var update = new IncrementalUpdate(file);
            var structTreeRoot = update.Allocate();
            var documentElement = update.Allocate();
            var structureElements = new Dictionary<IrElement, (IndirectReference Reference, List<int> Mcids, PdfPageObject Page)>();
            var parentTree = new List<IToken>();

            foreach (var page in file.Pages)
            {
                var mcidOwners = new List<IToken>();
                foreach (var stream in page.ContentStreams)
                {
                    var wraps = new List<MarkedContentWrap>();
                    foreach (var source in sourceObjects.Where(o => o.PageNumber == page.PageNumber && o.StreamIndex == stream.Index))
                    {
                        if (claims.GetValueOrDefault(source.Id) is not { Type: not ElementType.Artifact } element)
                        {
                            wraps.Add(MarkedContentWrap.Artifact(source.Start, source.End));
                            continue;
                        }

                        if (!structureElements.TryGetValue(element, out var structure))
                        {
                            structure = (update.Allocate(), [], page);
                            structureElements[element] = structure;
                        }

                        var mcid = mcidOwners.Count;
                        structure.Mcids.Add(mcid);
                        mcidOwners.Add(new IndirectReferenceToken(structure.Reference));
                        wraps.Add(MarkedContentWrap.Tagged(source.Start, source.End, element.Type.ToString(), mcid));
                    }

                    update.Set(stream.Reference, stream.WithMarkedContent(wraps));
                }

                var structParents = parentTree.Count / 2;
                parentTree.Add(new NumericToken(structParents));
                parentTree.Add(new ArrayToken(mcidOwners));
                update.Set(page.Reference, page.Dictionary.With("StructParents", new NumericToken(structParents)));
            }

            var readingOrder = ir.Pages.SelectMany(p => p.Elements).Where(structureElements.ContainsKey).ToList();
            foreach (var element in readingOrder)
            {
                var (reference, mcids, page) = structureElements[element];
                update.Set(reference, Dictionary(
                    ("Type", NameToken.Create("StructElem")),
                    ("S", NameToken.Create(element.Type.ToString())),
                    ("P", new IndirectReferenceToken(documentElement)),
                    ("Pg", new IndirectReferenceToken(page.Reference)),
                    ("K", new ArrayToken(mcids.Select(m => (IToken)new NumericToken(m)).ToList()))));
            }

            update.Set(documentElement, Dictionary(
                ("Type", NameToken.Create("StructElem")),
                ("S", NameToken.Create("Document")),
                ("P", new IndirectReferenceToken(structTreeRoot)),
                ("K", new ArrayToken(readingOrder.Select(e => (IToken)new IndirectReferenceToken(structureElements[e].Reference)).ToList()))));

            update.Set(structTreeRoot, Dictionary(
                ("Type", NameToken.Create("StructTreeRoot")),
                ("K", new IndirectReferenceToken(documentElement)),
                ("ParentTree", Dictionary(("Nums", new ArrayToken(parentTree)))),
                ("ParentTreeNextKey", new NumericToken(parentTree.Count / 2))));

            var metadata = update.Allocate();
            var xmp = Encoding.UTF8.GetBytes(XmpMetadata(ir.Title));
            update.Set(metadata, new StreamToken(
                Dictionary(("Type", NameToken.Create("Metadata")), ("Subtype", NameToken.Create("XML")), ("Length", new NumericToken(xmp.Length))),
                xmp));

            var title = new StringToken(ir.Title, ir.Title.All(char.IsAscii) ? StringToken.Encoding.Iso88591 : StringToken.Encoding.Utf16BE);
            if (update.Info is IndirectReferenceToken infoReference && file.Resolve(infoReference) is DictionaryToken info)
            {
                update.Set(infoReference.Data, info.With("Title", title));
            }
            else
            {
                var newInfo = update.Allocate();
                var existingInfo = update.Info is DictionaryToken direct ? direct : Dictionary();
                update.Set(newInfo, existingInfo.With("Title", title));
                update.Info = new IndirectReferenceToken(newInfo);
            }

            var viewerPreferences = file.Catalog.TryGet(NameToken.Create("ViewerPreferences"), out var existing) && file.Resolve(existing) is DictionaryToken preferences
                ? preferences
                : Dictionary();
            update.Set(file.CatalogReference, file.Catalog
                .With("StructTreeRoot", new IndirectReferenceToken(structTreeRoot))
                .With("MarkInfo", Dictionary(("Marked", BooleanToken.True)))
                .With("Lang", new StringToken(ir.Language))
                .With("ViewerPreferences", viewerPreferences.With("DisplayDocTitle", BooleanToken.True))
                .With("Metadata", new IndirectReferenceToken(metadata)));

            return new TagWriterResult(update.Build(), new TagWriterDiagnostics(applied, skipped, errors));
        }
        catch (PdfProcessingException ex)
        {
            errors.Add(new TagWriterError(ex.Code, null, null, ex.Message));
            return new TagWriterResult(null, new TagWriterDiagnostics(0, ir.Pages.Sum(p => p.Elements.Count), errors));
        }
    }

    /// <summary>An XMP packet with the Document's title and the PDF/UA-1 identification schema.</summary>
    private static string XmpMetadata(string title) => $"""
        <?xpacket begin="{'﻿'}" id="W5M0MpCehiHzreSzNTczkc9d"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/">
              <dc:title><rdf:Alt><rdf:li xml:lang="x-default">{SecurityElement.Escape(title)}</rdf:li></rdf:Alt></dc:title>
            </rdf:Description>
            <rdf:Description rdf:about="" xmlns:pdfuaid="http://www.aiim.org/pdfua/ns/id/">
              <pdfuaid:part>1</pdfuaid:part>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        <?xpacket end="w"?>
        """;

    private static DictionaryToken Dictionary(params (string Key, IToken Value)[] entries) =>
        new(entries.ToDictionary(e => NameToken.Create(e.Key), e => e.Value));

    /// <summary>Matches each Element to its Source objects, skipping Elements whose Source objects can't be used.</summary>
    private static Dictionary<string, IrElement> Claim(IrDocument ir, Dictionary<string, SourceObject> sourceObjects, List<TagWriterError> errors, out int applied, out int skipped)
    {
        var claims = new Dictionary<string, IrElement>(StringComparer.Ordinal);
        applied = 0;
        skipped = 0;
        foreach (var page in ir.Pages)
        {
            foreach (var element in page.Elements)
            {
                var problem = element.SourceObjects.Select(id => Check(id, page.PageNumber)).FirstOrDefault(p => p is not null);
                if (problem is not null)
                {
                    errors.Add(problem with { ElementId = element.Id });
                    skipped++;
                    continue;
                }

                foreach (var id in element.SourceObjects)
                {
                    claims[id] = element;
                }

                applied++;
            }
        }

        return claims;

        TagWriterError? Check(string id, int pageNumber)
        {
            if (!sourceObjects.TryGetValue(id, out var source) || source.PageNumber != pageNumber)
            {
                return new TagWriterError("SOURCE_OBJECT_NOT_FOUND", pageNumber, null, $"Source object '{id}' is not on page {pageNumber}.");
            }

            return claims.ContainsKey(id)
                ? new TagWriterError("SOURCE_OBJECT_CLAIMED_TWICE", pageNumber, null, $"Source object '{id}' already belongs to another Element.")
                : null;
        }
    }
}
