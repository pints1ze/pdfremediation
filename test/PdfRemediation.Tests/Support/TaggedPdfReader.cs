using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Tokens;

namespace PdfRemediation.Tests.Support;

/// <summary>A structure element as read back from a tagged PDF, with the text its MCIDs mark on the page.</summary>
public sealed record ReadBackElement(string Type, int PageNumber, IReadOnlyList<int> Mcids, string Text);

/// <summary>What a tagged PDF says about itself, read with PdfPig's raw object access, independently of the writer.</summary>
public sealed record ReadBackDocument(
    string RootType,
    IReadOnlyList<ReadBackElement> Elements,
    IReadOnlyList<string> ArtifactContent,
    IReadOnlyList<string> UnmarkedText,
    bool ParentTreeMatches);

public static class TaggedPdfReader
{
    public static ReadBackDocument Read(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        IToken Resolve(IToken token) => token is IndirectReferenceToken r ? document.Structure.GetObject(r.Data).Data : token;

        var catalog = document.Structure.Catalog.CatalogDictionary;
        var structTreeRoot = (DictionaryToken)Resolve(catalog.Data["StructTreeRoot"]);
        var root = (DictionaryToken)Resolve(structTreeRoot.Data["K"]);
        var pageReferences = PageReferences(document, catalog);

        var marked = new Dictionary<(int Page, int Mcid), MarkedContentElement>();
        var artifacts = new List<string>();
        var unmarked = new List<string>();
        foreach (var page in document.GetPages())
        {
            var all = Flatten(page.GetMarkedContents()).ToList();
            foreach (var element in all.Where(e => !e.IsArtifact && e.MarkedContentIdentifier >= 0))
            {
                marked[(page.Number, element.MarkedContentIdentifier)] = element;
            }

            artifacts.AddRange(all.Where(e => e.IsArtifact).Select(e => e.Paths.Count > 0 ? "<path>" : string.Concat(e.Letters.Select(l => l.Value))));
            var markedLetters = all.SelectMany(e => e.Letters).ToHashSet();
            unmarked.AddRange(page.Letters.Where(l => !markedLetters.Contains(l)).Select(l => l.Value));
        }

        var elements = new List<(long Reference, ReadBackElement Element)>();
        foreach (var kid in ((ArrayToken)Resolve(root.Data["K"])).Data.Cast<IndirectReferenceToken>())
        {
            var element = (DictionaryToken)Resolve(kid);
            var pageNumber = pageReferences[((IndirectReferenceToken)element.Data["Pg"]).Data.ObjectNumber];
            int[] mcids = Resolve(element.Data["K"]) switch
            {
                NumericToken single => [single.Int],
                ArrayToken array => array.Data.Cast<NumericToken>().Select(n => n.Int).ToArray(),
                var other => throw new InvalidOperationException($"Unexpected K: {other}"),
            };
            var text = string.Concat(mcids.SelectMany(m =>
                marked.TryGetValue((pageNumber, m), out var mc) && mc.Tag == ((NameToken)element.Data["S"]).Data
                    ? mc.Letters.Select(l => l.Value)
                    : ["<missing>"]));
            elements.Add((kid.Data.ObjectNumber, new ReadBackElement(((NameToken)element.Data["S"]).Data, pageNumber, mcids, text)));
        }

        return new ReadBackDocument(
            ((NameToken)root.Data["S"]).Data,
            elements.Select(e => e.Element).ToList(),
            artifacts,
            unmarked,
            ParentTreeMatches(document, structTreeRoot, pageReferences, elements, Resolve));
    }

    /// <summary>Checks that the ParentTree maps every MCID on every page back to the element that owns it.</summary>
    private static bool ParentTreeMatches(PdfDocument document, DictionaryToken structTreeRoot, Dictionary<long, int> pageReferences, List<(long Reference, ReadBackElement Element)> elements, Func<IToken, IToken> resolve)
    {
        var parentTree = (DictionaryToken)resolve(structTreeRoot.Data["ParentTree"]);
        var nums = ((ArrayToken)resolve(parentTree.Data["Nums"])).Data;
        var byKey = new Dictionary<int, IReadOnlyList<IToken>>();
        for (var i = 0; i + 1 < nums.Count; i += 2)
        {
            byKey[((NumericToken)nums[i]).Int] = ((ArrayToken)resolve(nums[i + 1])).Data;
        }

        foreach (var (pageReference, pageNumber) in pageReferences)
        {
            var owned = elements.Where(e => e.Element.PageNumber == pageNumber)
                .SelectMany(e => e.Element.Mcids.Select(m => (Mcid: m, e.Reference)))
                .ToList();
            if (owned.Count == 0)
            {
                continue;
            }

            var page = (DictionaryToken)document.Structure.GetObject(new(pageReference, 0)).Data;
            if (!page.TryGet<NumericToken>(NameToken.Create("StructParents"), out var key) || !byKey.TryGetValue(key.Int, out var array))
            {
                return false;
            }

            if (owned.Any(o => o.Mcid >= array.Count || ((IndirectReferenceToken)array[o.Mcid]).Data.ObjectNumber != o.Reference))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Page object numbers mapped to 1-based page numbers, in page-tree order.</summary>
    private static Dictionary<long, int> PageReferences(PdfDocument document, DictionaryToken catalog)
    {
        var pages = new Dictionary<long, int>();
        Walk((IndirectReferenceToken)catalog.Data["Pages"]);
        return pages;

        void Walk(IndirectReferenceToken node)
        {
            var dictionary = (DictionaryToken)document.Structure.GetObject(node.Data).Data;
            if (dictionary.TryGet<ArrayToken>(NameToken.Kids, out var kids))
            {
                foreach (var kid in kids.Data.Cast<IndirectReferenceToken>())
                {
                    Walk(kid);
                }
            }
            else
            {
                pages[node.Data.ObjectNumber] = pages.Count + 1;
            }
        }
    }

    private static IEnumerable<MarkedContentElement> Flatten(IEnumerable<MarkedContentElement> elements) =>
        elements.SelectMany(e => Flatten(e.Children).Prepend(e));
}
