# Build our own tag writer instead of buying a tagging SDK

We write the tag tree with our own .NET module built on PdfPig (MIT). It takes the Accessibility IR, discards any existing tag tree (ADR 0001), inserts `BDC`/`EMC` marked-content operators around the original content-stream bytes, builds the StructTreeRoot, ParentTree, RoleMap and PDF/UA metadata, and saves incrementally. We chose this over three alternatives. PDFix is quote-only and licensed per instance. Aspose.PDF for .NET has the best documented API, but its Developer OEM licence costs about $3,597 up front, which is too much for a solo developer before there is revenue. OpenDataLoader is free, but it is a Java sidecar, re-serialises content streams (a risk to Visual preservation), and could not be shown to accept our IR. The hardest part, matching IR Elements to the Source objects that draw them, has to be built with any writer, so buying one saves only the mechanical serialisation work.

## Scope

The first version is deliberately narrow. Content it does not support, such as Form XObject content, Type 3 fonts, optional content and form fields, produces a stable error code and fails the Job, which Releases the hold (ADR 0002). Coverage grows from the corpus and the failure log. The writer sits behind one interface (IR plus original PDF in, tagged PDF plus diagnostics out), so it can be replaced without touching anything upstream.

## Trigger for buying Aspose instead

Buy Aspose.PDF for .NET (Developer OEM) if either of these happens:

- By the end of the two-week T-005 spike, our writer cannot tag simple born-digital pages and a scanned page so that they pass veraPDF with a `0.0000` visual diff, while Aspose, run under a 30-day licence on the same files, can.
- Revenue reaches about $3,000 a month and extending our writer's coverage is costing more developer time than the licence would.

Before buying, confirm in the EULA that Developer OEM covers the hosted SaaS (OQ-04).

## Considered options

- **PDFix SDK:** a documented struct-tree API, but quote-only pricing per instance.
- **Aspose.PDF for .NET:** the documented `Tag(bdc)` API and incremental save; the fallback above.
- **OpenDataLoader PDF:** Apache-2.0, but its free tier emits Tagged PDF, not PDF/UA, and it rewrites content streams.
- **PDFsharp 6.2.4:** MIT, but it has no incremental save and drops inline images; later versions are AGPL.

See `docs/research/pdf-tagging-alternatives.md`, `pdf-tagging-round-2.md` and `pdf-tagging-round-3.md`.
