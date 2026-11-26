# PDF tagging, round 2: paid options, margins and simpler stacks

Research date: 2026-11-26. All prices were taken from vendor pages on that date, in USD, and will change. This builds on [round one](pdf-tagging-alternatives.md) and does not repeat it. **UNVERIFIED** marks claims I could not confirm from a primary source. "Docs show" means what the API reference or guide shows; marketing claims such as "PDF/UA compliant" are not counted as evidence. This is not legal advice.

**Margin rule used throughout.** Variable cost is every paid per-page component plus the card fee. It must be at most 10% of the price. The card fee is Stripe's standard US rate, **2.9% + $0.30** per successful charge, with no listed surcharge for manual capture ([stripe.com/pricing](https://stripe.com/pricing)). Because the $0.30 is charged per order, the minimum price depends on order size (N pages):

`P_min = (C + 0.30/N) / (0.10 − 0.029) = (C + 0.30/N) / 0.071`

Compute, storage and staff costs are **excluded**. So is the cost of failed Jobs, which get no revenue because their hold is Released (ADR 0002). If a share *f* of Jobs fail after paid processing, divide C by (1 − f).

## 1. Short answer

**Main finding.** Paid options make one step simpler: the writer. **Aspose.PDF for .NET** is the only product found that is all three of these: native .NET, self-hosted, and documented to wrap *existing* page operators in `BDC`/`EMC` and link them to struct elements we create ([edit-pdf-file-tags](https://docs.aspose.com/pdf/net/edit-pdf-file-tags)). Its price is also published ([pricing](https://purchase.aspose.com/pricing/pdf/net/)). It removes round one's Java OpenDataLoader adapter and its unverified "feed our IR in" problem.

No vendor collapses understanding, tagging and validation into one product that accepts our IR. The products that do collapse the steps (Nutrient DWS, Adobe Auto-Tag, PREP, Equidox) run their own auto-tagger, which bypasses the Accessibility IR. They also either break the margin or are quote-only (§5).

### Stack A: simplest viable (recommended)

```
.NET worker ─ PdfPig/PDFium (preflight, render)
   ├─► Azure Document Intelligence prebuilt-layout (cloud or container) ─► words+polygons, roles, tables w/ spans, figures, sections
   ├─► Claude Haiku 5.5 (heading levels H1–H6, reading-order sanity check, alt-text drafts; JSON out)
   ├─► Accessibility IR (ours)
   ├─► Aspose.PDF for .NET, in process: RemoveTags → new struct tree → BDC/EMC+MCID around existing operators
   │     → invisible OCR text for scanned regions → /Lang, title, XMP pdfuaid → incremental save
   ├─► veraPDF CLI or REST container (only JVM left)
   └─► PDFium pixel diff (PDFtoImage)
```

| | |
|---|---|
| Variable cost per page | **$0.0111** (Azure Layout $0.010 + Haiku 5.5 ≈ $0.00105). Add the Aspose licence, amortised: +$0.060 at 10k pages/month, +$0.006 at 100k, +$0.0006 at 1M. |
| Minimum price for 90% margin (30-page order) | **$1.14** at 10k pages/month, **$0.38** at 100k, **$0.31** at 1M |
| What it removes vs round one | The Java OpenDataLoader adapter and its upstream patching. The Python sidecar (Docling, docling-serve, OCRmyPDF). The GPU node for Qwen3-VL. The open question of whether OpenDataLoader will accept our IR. What is left is one .NET process plus a veraPDF container. |
| What it adds | Two cloud data processors (Microsoft and Anthropic), which matters for government data (OQ-05, OQ-07). An Aspose licence. Content-stream fidelity becomes our job, because Aspose re-serialises `page.Contents` (§6). |

### Stack B: best quality

Stack A, plus three upgrades:
- Mistral OCR 4.1 as a second understanding pass, because it gives per-block confidence ([basic_ocr](https://docs.mistral.ai/capabilities/document_ai/basic_ocr/)).
- Claude Sonnet 5.5 instead of Haiku for structure arbitration and alt text.
- axesPAC (built on the PAC engine, REST, on-prem SDK, quote-only) as a second validator ([axesPAC](https://www.axes4.com/axespac)).

PDFix SDK is the alternative writer to test against Aspose in the spike. Its `AddPageObject` "automatically creates MCR objects" ([PdsStructElement](https://pdfix.github.io/pdfix_sdk_builds/en/6.5.0/html/struct_pds_struct_element.html)), but its price is quote-only.

| | |
|---|---|
| Variable cost per page | **$0.0349** (Azure $0.010 + Mistral $0.004 + Sonnet 5.5 ≈ $0.0209). Add the licences: Aspose as in Stack A, plus axesPAC and PDFix, both quote-only, so **not included**. |
| Minimum price for 90% margin (30-page order) | **$1.48** at 10k pages/month, **$0.72** at 100k, **$0.64** at 1M (all before the axesPAC and PDFix quotes) |
| What it adds vs Stack A | A second understanding call and an IR merge step. A second validator. Two more vendor contracts. |

### Stack C: cheapest

Round one's licence-free stack (Docling + OCRmyPDF + OpenDataLoader + finishing pass), with one change: Qwen3-VL on a GPU is replaced by **Claude Haiku 5.5** through the API, sending figure crops for alt text only.

| | |
|---|---|
| Variable cost per page | **≤ $0.00105**. That is a whole-page Haiku call; sending only figure crops costs less. |
| Minimum price for 90% margin (30-page order) | **$0.16** |
| What it changes vs round one | Removes the GPU node. All of round one's writer risks remain (Tagged PDF only, not PDF/UA; marked content dropped; IR input unverified; [round one §2.4](pdf-tagging-alternatives.md)). |

**Pick.** Use Stack A if the T-005 spike shows Aspose can tag existing content with zero pixel change. Use Stack C if it can't, or if government customers forbid cloud processors. Choose by order size, not stack: with a $0.30 fixed card fee, **any 1-page order needs a price of about $4.24/page or more** to keep 90% margin, whichever stack is used (§4). The product needs a minimum charge (OQ-02).

## 2. Writers and SDKs: can they write *our* tree onto *existing* content?

"Our tree" means an API that lets us build the struct tree and bind MCIDs to existing content. "Auto only" means the product can only run its own tagger.

| Product | Our tree on existing content? | Rendering preservation (what the docs show) | PDF/UA evidence | .NET / Linux | Hosting | Price (2026-11-26) | Verdict |
|---|---|---|---|---|---|---|---|
| **Aspose.PDF for .NET** | **Yes, documented.** The sample calls `ClearChilds()` and builds new elements. It inserts `new BDC("P")` before existing operators in `page.Contents` and `EMC` after them, then `element.Tag(bdc)` returns an `MCRElement`. Links are tagged with `Tag(annotation)` ([edit-pdf-file-tags](https://docs.aspose.com/pdf/net/edit-pdf-file-tags)). Auto-tagging is also available ([AutoTaggingSettings](https://reference.aspose.com/pdf/net/aspose.pdf/autotaggingsettings)). | Operators are inserted into the parsed content list, so the stream is **re-serialised**. Byte and number fidelity is UNVERIFIED. `Document.Save()` with no arguments saves "using incremental update technique" ([Save](https://reference.aspose.com/pdf/net/aspose.pdf/document/save/)). | Has an in-house `Validate(log, PdfFormat.PDF_UA_1)` ([create-tagged-pdf](https://docs.aspose.com/pdf/ko/net/create-tagged-pdf)). No veraPDF evidence. | NuGet `Aspose.PDF` / `Aspose.PDF.Drawing` for Linux without libgdiplus ([drawing](https://docs.aspose.com/pdf/net/drawing/), [docker](https://docs.aspose.com/pdf/net/docker/dotnet8/)) | Self-host | Developer OEM **$3,597** perpetual (1 developer, SaaS allowed, 1 year of updates). Site OEM $16,786. Metered OEM from $1,999/month. Small Business tiers **forbid SaaS** ([pricing](https://purchase.aspose.com/pricing/pdf/net/)). | **Recommended writer (Stack A)** |
| **PDFix SDK** | **Yes, documented.** `RemoveTags`, `CreateStructTree` ([PdfDoc](https://pdfix.github.io/pdfix_sdk_builds/en/8.1.0/html/struct_pdf_doc.html)). `PdsStructElement.AddPageObject(obj, index)` "automatically creates MCR objects" ([PdsStructElement](https://pdfix.github.io/pdfix_sdk_builds/en/6.5.0/html/struct_pds_struct_element.html)). | `PdfPage.SetContent()` writes "all pending page content modifications to the content stream", which means a regenerated stream. `kSaveIncremental` exists ([PdfPage](https://pdfix.github.io/pdfix_sdk_builds/en/8.1.0/html/struct_pdf_page.html)). | The NuGet text claims PDF/UA (marketing). Its validator uses the veraPDF engine ([pdf-validation](https://pdfix.net/pdf-validation/)). | NuGet `PDFix.SDK` 10.0.1, .NET Standard 2.0, Linux x64/arm64 natives ([NuGet](https://www.nuget.org/packages/PDFix.SDK)) | Self-host | **Quote-only**, "per instance" ([pricing](https://pdfix.net/pricing/sdk)). Trial builds watermark output ([docker](https://hub.docker.com/r/pdfix/autotag-textract)). | **Alternative writer**: spike it against Aspose |
| **Apryse (PDFTron) Server SDK** | **Partly.** It has `SElement` and `CreateContentItem` ([SElement](https://sdk.apryse.com/api/PDFTronSDK/dotnetcore/pdftron.PDF.Struct.SElement.html)), but marking existing content means rewriting the page yourself with `ElementWriter` + `CreateMarkedContentBegin` ([ElementBuilder](https://docs.apryse.com/api/PDFTronSDK/dotnet/pdftron.PDF.ElementBuilder.html)). `PDFUAConformance.AutoConvert` is auto-tag only ([convert](https://docs.apryse.com/core/guides/features/pdfua/convert)). | Rewrites the stream. `e_incremental` exists ([SaveOptions](https://sdk.apryse.com/api/PDFTronSDK/dotnetcore/pdftron.SDF.SDFDoc.SaveOptions.html)). AutoConvert may substitute fonts (convert page). | Vendor says AutoConvert "ensures compliance". No validation evidence shown. | .NET Core on Linux ([linux](https://docs.apryse.com/core/guides/get-started/linux)) | Self-host | Quote-only, "as low as $1,500 a year". AutoConvert needs the Accessibility and Data Extraction modules ([guide](https://apryse.com/pricing/guide)). | Possible, but more work than Aspose or PDFix |
| **Datalogics APDFL** (Adobe PDF Library) | **Yes, low level.** `PDSElementInsertMCAsKid` links existing marked content. "No additional APIs specifically related to enforcing the rules of PDF Tagging" ([KB](https://kb.datalogics.com/article/using-apdfl-to-create-tools-for-working-with-pdf-ua-files-146.html)). How much of this the .NET wrapper exposes is UNVERIFIED. | Acrobat engine. Wrapping content still rewrites the stream (inference). | None in docs | NuGet `Adobe.PDF.Library.LM.NET` ([NuGet](https://www.nuget.org/packages/Adobe.PDF.Library.LM.NET/21.0.1/License)) | Self-host | **$7,499/year** OEM list price. SaaS is licensed per server or by custom metric, by quote ([pricing](https://datalogics.com/pricing-and-licensing)). | Fallback if Aspose and PDFix both fail |
| **iText Core 9** (commercial) | **No documented route.** It tags content iText draws itself (`TagTreePointer` → `PdfCanvas.openTag`) ([TagTreePointer](https://api.itextpdf.com/iText/java/9.3.0/com/itextpdf/kernel/pdf/tagutils/TagTreePointer.html)). A canvas on an existing page only appends ([KB](https://kb.itextpdf.com/itext/chapter-5-manipulating-an-existing-pdf-document)). pdfOCR and pdf2Data don't address tagging. | Append mode exists ([PdfDocument](https://api.itextpdf.com/iText/java/9.5.0/com/itextpdf/kernel/pdf/PdfDocument.html)) | `PdfUADocument` runs checks at write time ([PdfUADocument](https://api.itextpdf.com/iText/java/9.7.1/com/itextpdf/pdfua/PdfUADocument.html)) | Native .NET | Self-host | **AGPL-3.0 or commercial.** Volume licences are priced per PDF file processed, by quote ([how-buy](https://itextpdf.com/how-buy)). | Not recommended |
| **Foxit PDF SDK** (Accessibility module) | **Unclear.** Auto-tagging (`TaggedPDF`), and since v12 `TagTreeEditor` and `AccessibilityFixer` ([guide](https://developers.foxit.com/documents/development-guide/pdf-sdk-desktop-server/features/standards-and-compliance/accessibility.html)). Binding our own tree through the .NET API is UNVERIFIED. | Not documented | No conformance claim in the guide | NuGet `Foxit.SDK.Dotnet`, Linux ([NuGet](https://www.nuget.org/packages/Foxit.SDK.Dotnet)) | Self-host | Quote-only ([sdk](https://www.foxit.com/sdk/)) | Not shortlisted |
| **Nutrient** (GdPicture .NET / DWS API) | .NET has low-level `NewTag` and `BeginMarkedContentSequence`, but the documented examples only tag **new** content ([NewTag](https://www.nutrient.io/api/gdpicture/GdPicture.NET.14~GdPicture14.GdPicturePDF~NewTag.html)). `ConvertToPDFUA` and DWS are auto-tag only ([DWS](https://www.nutrient.io/guides/dws-accessibility/developer-guides/introduction-to-pdfua-and-auto-tagging/)). | UNVERIFIED | The vendor itself says to validate separately | .NET | SDK self-host; DWS cloud | DWS **$0.395/page** auto-tag, **$0.079/validation** ([pricing](https://www.nutrient.io/guides/dws-accessibility/pricing/)). SDK quote-only. | DWS breaks the margin (§4) |
| **Adobe PDF Services Auto-Tag** | **No.** Options are only a report and `ShiftHeadings`. It "replaces any existing tags" ([docs](https://developer.adobe.com/document-services/docs/overview/pdf-accessibility-auto-tag-api/)). | UNVERIFIED (cloud regenerates the file) | Adobe says the output "is not guaranteed to comply" with PDF/UA (same page) | .NET SDK, REST | Cloud | 10 Document Transactions per page. Paid use needs a written agreement ([licensing](https://developer.adobe.com/document-services/docs/overview/pdf-accessibility-auto-tag-api/dcserviceslicensing)). Current list price UNVERIFIED. | Out (PRD non-goal; no IR input) |
| **Syncfusion PDF** | **No.** Tagging covers newly drawn elements; existing tags are read-only ([docs](https://help.syncfusion.com/file-formats/pdf/working-with-tagged-pdf)) | n/a | n/a | .NET | Self-host | $1,199/developer/year ([pricing](https://www.syncfusion.com/sales/products/fileformats)) | Out |
| **ComPDFKit** | No struct-tree or MCID API found. Has a PDF/UA "conversion" ([guide](https://www.compdf.com/guides/pdf-sdk/java/conversion)). | UNVERIFIED | None | .NET Core, Linux | Self-host | Quote-only ([buyer-guide](https://www.compdf.com/pdf-sdk/buyer-guide)) | Out |
| **PDF Tools SDK** | `TagAs` + `SetPdfUaConformant()` "add logical structure to an existing PDF", but **content is copied into a new document** ([docs](https://www.pdf-tools.com/docs/pdf-tools-sdk/toolbox/accessibility/add-logical-structure-to-existing-pdf)) | Copying content is a pixel-diff risk | Has a conformance setter | .NET, Java, C | Self-host | Quote-only (UNVERIFIED) | Worth one spike day if the first two writers fail |
| **callas pdfToolbox** | No auto-tagging and no tree-building API. It validates PDF/UA-1 and has a "Fix problems in PDF tagging structure" profile ([help](https://help.callassoftware.com/m/62791/l/782739-pdf-ua-1-validation-for-standards-compliant-universally-accessible-pdfs)) | — | Validator | Server/CLI/SDK | Self-host | About €5,000 one-off (reseller source, UNVERIFIED) ([fourpees](https://www.fourpees.com/en/news/price-increase-callas-pdftoolbox-pdfapilot-product-line)) | Could serve as a fixer or validator only |
| **axesPDF / AbleDocs, CommonLook, Equidox, PREP** | Desktop editors (axesPDF, CommonLook) ([pdfa.org](https://pdfa.org/?p=881), [allyant](https://allyant.com/products/commonlook-pdf)), or hosted auto-tag APIs that use their own models (Equidox ([batch](https://equidox.co/pdf-batch-processing-automation/)), PREP ([API](https://www.continualengine.com/prep-auto-tag-api/))). No API for supplying our structure. | — | — | — | — | Quote-only | Out |
| **UniPDF (Go)** | Tags only content drawn through its Creator: "Tagging is not something you can bolt on afterwards" ([docs](https://docs.unidoc.io/unipdf/guides/accessibility/)) | — | Has a `pdfua` validator | Go | Self-host | Metered from $20/month ([pricing](https://unidoc.io/pricing/)) | Out |

**Open source missed in round one**

| Item | Finding |
|---|---|
| OpenDataLoader PDF | v2.5.24 (2026-11-23). It has recent tagging fixes (MCID, IDTree, PDF 2.0 namespace). PDF/UA-1 and PDF/UA-2 export are still an "Enterprise add-on… available on request", with no published price. There is still no documented input for external structure ([repo](https://github.com/opendataloader-project/opendataloader-pdf), [site](https://opendataloader.org/)). |
| pikepdf 10.18 / qpdf 12.4 | **Not a good writer.** It has no incremental save: it "always coalesces any incremental updates into a single non-incremental PDF file" ([API](https://pikepdf.readthedocs.io/en/latest/api/main.html)). It does not descend into Form XObjects and warns about rounding ([content streams](https://pikepdf.readthedocs.io/en/latest/topics/content_streams.html)). The tagged-PDF feature request was closed as "not planned" on 2026-10-06 ([#461](https://github.com/pikepdf/pikepdf/issues/461)). It has no glyph geometry. It is still fine for a metadata finishing pass. |
| PdfPig | Stable is still 0.1.16. `PdfIncrementalUpdateBuilder` exists only in 0.1.17 alphas ([NuGet](https://www.nuget.org/packages/PdfPig)). |
| PDFBox | 3.0.8 is current; 4.0 is unreleased; no new tagging helpers ([site](https://pdfbox.apache.org/)). |
| Rust / Go / JS | lopdf (MIT) has `IncrementalDocument` but no struct helpers ([repo](https://github.com/J-F-Liu/lopdf)). krilla does PDF/UA for **new** documents only ([repo](https://github.com/LaurenzV/krilla)). pdfcpu only validates struct trees ([repo](https://github.com/pdfcpu/pdfcpu)). pdf-lib is dormant ([repo](https://github.com/Hopding/pdf-lib)). None of them tags existing content. |
| pdf-a11y (Apache-2.0, 2.10.0) | A Python pipeline wrapping OpenDataLoader + veraPDF. It re-applies XMP `pdfuaid` after tagging, which is a useful recipe. It depends on **PyMuPDF (AGPL)**, so do not deploy it as is ([PyPI](https://pypi.org/project/pdf-a11y/)). |
| veraPDF 1.30.4 | `--fixmetadata` can add PDF/UA identification only when the file would otherwise pass ([fixing](https://docs.verapdf.org/cli/fixing)). Whether its save is incremental is UNVERIFIED. |

## 3. Understanding services (paid)

Costs are per page at list price. The structure each service needs to return is defined in GLOSSARY and PRD §10.2.

| Service | $/page | Reading order | Heading levels | Lists | Tables (spans, headers) | Figures (bbox) | Header/footer | Per-element confidence | Words + geometry (for OCR layer) | Hosting |
|---|---|---|---|---|---|---|---|---|---|---|
| **Azure Document Intelligence, prebuilt-layout v4.0** | **$0.010**. Commitment tiers: 100k/month $900 (overage $0.009), 1M/month $7,500 (overage $0.0075). Connected container is 20% less ([Retail Prices API](https://prices.azure.com/api/retail/prices?$filter=productName%20eq%20%27Azure%20Document%20Intelligence%27%20and%20armRegionName%20eq%20%27eastus%27), [page](https://azure.microsoft.com/en-us/pricing/details/ai-document-intelligence/)) | Yes ("reading order concatenated content") | Indirect: `title`/`sectionHeading` roles plus a `sections` tree. Markdown output uses `#`–`######` ([markdown](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/concept/markdown-elements?view=doc-intel-4.0.0)) | Not in the JSON roles. Markdown only (UNVERIFIED depth) | `rowSpan`, `columnSpan`, `kind` = columnHeader/rowHeader, caption ([REST](https://learn.microsoft.com/en-us/rest/api/aiservices/document-models/get-analyze-result?view=rest-aiservices-v4.0%20(2024-11-30))) | Yes | `pageHeader`/`pageFooter`/`pageNumber` | **No**: words and selection marks only | **Yes** (polygon + confidence per word) | Cloud. Layout container; disconnected mode needs approval ([containers](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/containers/disconnected?view=doc-intel-4.0.0)) |
| **AWS Textract** LAYOUT (+TABLES) | Layout **$0.004** ($0.003 above 1M). Tables+Layout **$0.015** ($0.010) ([pricing](https://aws.amazon.com/textract/pricing/)) | Yes, "implied reading order" | Title vs section header only | `LAYOUT_LIST` → `LAYOUT_TEXT` items | Spans and header cells: UNVERIFIED | `LAYOUT_FIGURE` | Yes | **Yes**, per block ([layout](https://docs.aws.amazon.com/textract/latest/dg/layoutresponse.html)) | Yes (WORD blocks; well known, not re-fetched) | Cloud |
| **Google Document AI** Layout Parser | **$0.010** ($0.009 or $0.008 on savings plans) ([pricing](https://cloud.google.com/document-ai/pricing)) | Hierarchical blocks | `heading-1`…`heading-5` | Ordered/unordered (named for PDF: UNVERIFIED) | `rowSpan`/`colSpan`, header rows ([Document](https://docs.cloud.google.com/document-ai/docs/reference/rest/v1/Document)) | Image block | Yes | **No** | Opt-in `returnBoundingBoxes` ([ProcessOptions](https://docs.cloud.google.com/document-ai/docs/reference/rest/v1/ProcessOptions)) | Cloud |
| **Mistral OCR 4.1** (the PRD's choice) | **$0.004** ([pricing](https://docs.mistral.ai/inference/pricing)). Batch 50% off and annotations at $5/1k pages per the announcement ([news](https://mistral.ai/news/ocr-4)); neither appears on the pricing table, so treat both as UNVERIFIED. | Yes (`include_blocks`, "in reading order") | `title` only | `list` block, no items | HTML/markdown. Cell-span fidelity UNVERIFIED | Image bbox | `header`/`footer` blocks | **Yes**: block, word or page granularity ([basic_ocr](https://docs.mistral.ai/capabilities/document_ai/basic_ocr/)) | Word geometry UNVERIFIED | Cloud. Self-host for enterprise customers |
| **Reducto** | r-1 Parse **$0.010**, mini **$0.001**, batch 20% off ([pricing](https://reducto.ai/pricing)) | UNVERIFIED | No levels | List Item | HTML with merged cells | Figure | Yes | Yes: high/low + 0–1 ([response](https://docs.reducto.ai/parse/response-format)) | UNVERIFIED | Cloud |
| **LlamaParse** | Cost-effective 3 credits = **$0.0045**, Agentic **$0.015**. Layout adds 3 credits = $0.0045 ([pricing](https://www.llamaindex.ai/pricing)) | Yes | **Level 1–6** | Nested ordered lists | HTML with merged cells, cell bboxes | Yes | Yes | Page/document only. Per item UNVERIFIED ([format](https://developers.llamaindex.ai/llamaparse/parse/guides/response-format/index.md)) | Word bboxes via `granular_bboxes` | Cloud |
| **Unstructured** | Hi-Res $0.02, Balanced $0.01, Best $0.015 ([pricing](https://unstructured.io/pricing)) | Element order | `category_depth` | `ListItem` | `text_as_html` | Image | Header/Footer | `detection_class_prob` (Hi-Res only) ([elements](https://docs.unstructured.io/api-reference/partition/document-elements)) | No | Cloud |
| **Landing AI ADE** | DPT-3 Pro about $0.015, Verity about $0.0055 ([pricing](https://landing.ai/pricing), [credits](https://docs.landing.ai/dpt3/credit-consumption)) | Yes | No | No | Cell grounding with spans | Figure | Marginalia (UNVERIFIED) | Yes (documented for DPT-2) ([json](https://docs.landing.ai/ade/ade-json-response)) | No | Cloud |
| **Claude Haiku 5.5 / Sonnet 5.5** used directly | Haiku **≈$0.00105**, Sonnet 5.5 **≈$0.0209** (see the note below the table) ([pricing](https://platform.claude.com/docs/en/about-claude/pricing)) | Whatever we ask for (JSON schema) | Yes | Yes | Yes | Only via our bboxes (no native geometry) | Yes | Self-reported, not calibrated | No | Cloud. Batch is **not** ZDR-eligible ([retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)) |
| GPT / Gemini used directly | gpt-6-luna ≈$0.00103, gpt-6.2-sol ≈$0.0206 ([pricing](https://developers.openai.com/api/docs/pricing)). Gemini 3.5 Flash-Lite ≈$0.0044, Gemini 3.8 Flash ≈$0.0072 now and ≈$0.0144 from 2027-01-01 ([pricing](https://aistudio.google.com/docs/pricing)) | Same as Claude | | | | | | | | Cloud. OpenAI batch is not ZDR-eligible ([data](https://developers.openai.com/api/docs/guides/your-data)) |

Note on the VLM costs: they assume one 1,077×1,393 px page image plus 1,000 text tokens in and 1,500 JSON tokens out, standard (not batch) pricing. For Claude 4.7+ models the image costs ⌈w/28⌉×⌈h/28⌉ = 1,950 tokens ([vision](https://platform.claude.com/docs/en/build-with-claude/vision)). For Haiku 5.5: 2,950 tokens × $0.10/M + 1,500 × $0.50/M = $0.00105. For Sonnet 5.5: 2,950 × $2/M + 1,500 × $10/M = $0.0209. Output tokens dominate the cost. Thinking tokens are excluded, so treat these as floors.

**Reading of the table.** No single service returns everything the IR needs. The best fit for the IR is **Azure Layout**: it returns words with geometry for the OCR text layer, table spans with header kinds, figures, header and footer roles, and a section tree. It lacks native per-element confidence and lists. A **cheap LLM pass** fills in heading levels, lists and alt text. Per-element confidence then has to be **derived** (for example, minimum word confidence × LLM agreement). GLOSSARY requires per-element confidence, so this derivation is a product decision.

Textract, Mistral 4.1 and Reducto return native per-block confidence but no heading levels. LlamaParse returns heading levels but its per-item confidence is unverified. Data residency (OQ-05, OQ-07) may decide between them: Azure has a connected or disconnected container, and Mistral offers self-hosting to enterprise customers.

## 4. Margin table

C = paid variable cost per page. Card fee = 2.9% of P + $0.30/N. "Margin" = 1 − (C + card)/P. The minimum price assumes a 30-page order (the NFR-02 test size) unless stated.

| Stack / option | C ($/page) | Min P, 1-page order | Min P, 10-page | **Min P, 30-page** | Min P, 100-page | Margin @ $0.25 | @ $0.50 | @ $1.00 | @ $2.00 |
|---|---|---|---|---|---|---|---|---|---|
| C cheapest (OSS + Haiku 5.5) | 0.00105 | 4.24 | 0.44 | **0.16** | 0.06 | 92.7% | 94.9% | 96.0% | 96.5% |
| PRD today (Mistral 4.1 annotate at $5/1k; PDFix licence excluded) | 0.0050 | 4.30 | 0.49 | **0.21** | 0.11 | 91.1% | 94.1% | 95.6% | 96.4% |
| A simplest, per-page only | 0.01105 | 4.38 | 0.58 | **0.30** | 0.20 | 88.7% | 92.9% | 95.0% | 96.0% |
| A + Aspose (2× Developer OEM, $7,194), 10k pages/month | 0.0710 | — | — | **1.14** | — | 64.7% | 80.9% | 89.0% | 93.0% |
| A + Aspose, 100k pages/month | 0.0170 | — | — | **0.38** | — | 86.3% | 91.7% | 94.4% | 95.7% |
| A + Aspose, 1M pages/month | 0.0116 | — | — | **0.31** | — | 88.4% | 92.8% | 94.9% | 96.0% |
| B best, per-page only (quotes excluded) | 0.0349 | 4.72 | 0.91 | **0.63** | 0.53 | 79.1% | 88.1% | 92.6% | 94.9% |
| B + Aspose, 10k / 100k / 1M pages/month | 0.0949 / 0.0409 / 0.0355 | — | — | **1.48 / 0.72 / 0.64** | — | 55.2 / 76.7 / 78.9% | 76.1 / 86.9 / 88.0% | 86.6 / 92.0 / 92.6% | 91.9 / 94.6 / 94.8% |
| Nutrient DWS auto-tag + 1 validation | 0.3976 | 9.83 | 6.02 | **5.74** | 5.64 | −66% | 15.6% | 56.3% | 76.7% |

Flat licences amortised per page (licence ÷ 12 ÷ monthly volume):

| Licence | Annual | 10k pages/month | 100k pages/month | 1M pages/month |
|---|---|---|---|---|
| Aspose Developer OEM × 2 (two developers; treated as a yearly cost to keep updates) | $7,194 | $0.060 | $0.0060 | $0.0006 |
| Aspose Site OEM | $16,786 | $0.140 | $0.0140 | $0.0014 |
| Aspose Metered OEM (minimum only) | $23,988 | $0.200 | $0.0200 | $0.0020 |
| Datalogics APDFL | $7,499 | $0.0625 | $0.0062 | $0.0006 |
| PDFix, Apryse, Foxit, axesPAC, iText | quote | — | — | — |

Takeaways:
- At the PRD's planned 30k pages/month (A-23), Stack A costs about $0.031/page with the licence included. Its 90% floor is about $0.58/page for 30-page orders.
- The $0.30 fixed card fee is the biggest single cost on small orders. Set a minimum order charge of about **$5** (at $0.50/page that is 10 pages). With a $5 minimum, the fixed fee is 6% of revenue.

## 5. Validators and QA

| Tool | Coverage | CLI / server | Licence / terms | Use |
|---|---|---|---|---|
| **veraPDF 1.30.4** (2026-11-09) | PDF/UA-1, PDF/UA-2, WTPDF. "Only machine verifiable checks" ([docs](https://docs.verapdf.org/validation/)). No Matterhorn coverage matrix is published. A third-party issue says the UA-1 profile has 106 rules and is missing a ListNumbering check (UNVERIFIED) ([issue](https://github.com/A11yDevs/acessilia-toolbox/issues/124)). | CLI and self-hosted `verapdf/rest` Docker ([hub](https://hub.docker.com/r/verapdf/rest)). No official hosted service. | GPL-3+ or MPL-2+ ([repo](https://github.com/veraPDF/veraPDF-library)) | The release gate (unchanged) |
| **PAC 2026** | PDF/UA-1 + WCAG 2.2 AA. PDF/UA-2 is "best effort" ([quickstart](https://pac.pdf-accessibility.org/en/resources/quickstart-guide/introducing-pdf-accessibility-checker-2024)) | Windows desktop only, no CLI ([FAQ](https://pac.pdf-accessibility.org/en/resources/faq)) | Freeware. EULA §2.1 bans commercial hosting ([EULA](https://pac.pdf-accessibility.org/en/resources/general-terms/end-user-license-agreement)) | Manual corpus QA only (matches the PRD non-goal) |
| **axesPAC** (axes4) | PAC engine via REST. Claims "all machine-verifiable PDF/UA and WCAG requirements" (marketing) | Cloud or on-prem SDK ([axesPAC](https://www.axes4.com/axespac), [support](https://support.axes4.com/hc/en-us/articles/26256923356690-What-is-axesPAC-Cloud)) | Quote-only | Optional second gate (Stack B) |
| PDF4WCAG (Dual Lab) | Commercial API from the veraPDF developers, beta ([poster](https://pdfa.org/download-area/posters/PDFDaysEurope2025-TechnicalPoster-DualLab.pdf)) | API | UNVERIFIED | Watch |
| callas pdfToolbox | PDF/UA-1 validation plus tag-structure fix-ups ([help](https://help.callassoftware.com/m/62791/l/782739-pdf-ua-1-validation-for-standards-compliant-universally-accessible-pdfs)) | Server/CLI | About €5k, UNVERIFIED | Not needed |
| Matterhorn Protocol 1.1 | 31 checkpoints, 136 failure conditions. The machine-checkable count is quoted as 86, 87 or 89 depending on source ([Tagged PDF School](https://taggedpdfschool.pdfa.org/matterhorn)). UNVERIFIED: the official PDF returned 403. | — | — | Neither validator proves semantics. Review items still apply. |

No managed veraPDF service was found. Running `verapdf/rest` as a container is the simplest option.

## 6. Risks, unknowns and what the spike (T-005) must prove

| # | Must prove | Why | Pass condition |
|---|---|---|---|
| 1 | **Aspose re-serialises `page.Contents` with zero pixel change.** Test numbers, strings, inline images, Type 3 fonts, Form XObjects (does tagging reach inside forms?), optional content `BDC /OC`, and pages with more than 100k operators. | The docs show insertion but no fidelity guarantee (§2) | Pinned PDFium diff = 0 on the corpus. Run the same test on PDFix. |
| 2 | Aspose can do the whole PDF/UA-1 finishing pass: `RemoveTags`/`ClearChilds` of the old tree (ADR 0001), ParentTree, `/StructParents`, `RoleMap`, `/Lang`, title + `DisplayDocTitle`, XMP `pdfuaid`, Artifact marking of untagged content | Not shown together on one docs page (UNVERIFIED) | veraPDF PDF/UA-1 passes after ≤ 3 Repair passes |
| 3 | Invisible OCR text written by Aspose (`TextRenderingMode.Invisible` = 3 exists ([enum](https://reference2.aspose.com/pdf/net/aspose.pdf.text/textrenderingmode))) passes the 7.21 font rules, especially no `.notdef` | Without it, scans need OCRmyPDF back (a Python sidecar) | veraPDF 7.21.x passes; pixel diff = 0 |
| 4 | Azure Layout + Haiku 5.5 give acceptable heading levels, lists and table headers, and the derived per-element confidence is meaningful | No service returns all of these natively (§3) | Benchmark against the annotated corpus. Compare Mistral 4.1 and Textract on the same pages. |
| 5 | Data terms: Azure region or container, Anthropic ZDR (needs sales; Batch excluded), Aspose and Microsoft as subprocessors for government data | OQ-05, OQ-07 | Written approval from security and legal |
| 6 | Licence terms: Aspose Developer OEM covers a hosted SaaS with two developers (the pricing page says "Web/SaaS/hosted use: Yes"; confirm the EULA), and renewal is needed for fixes | Perpetual licences include only 1 year of updates | Signed quote |
| 7 | Post-repair failure rate | Failed Jobs carry cost but no revenue | Feed it into the margin formula (C ÷ (1 − f)) |
| 8 | Get quotes for PDFix, Apryse, axesPAC, Adobe | All quote-only | Quotes on file before choosing Stack B |

Claims I'm least sure of: Aspose's content-stream fidelity (no evidence either way); Azure's list and markdown heading depth; Textract table-cell spans; Mistral batch and annotation prices; the Matterhorn machine-checkable count; whether Gemini and OpenAI model names will stay stable past 2026 (the Gemini Flash price doubles on 2027-01-01).

## 7. PRD impact (if Stack A is adopted)

| PRD location | Change |
|---|---|
| §1, Goals bullets 7 and 9, §6 Understanding/Remediation, §13.1 constraints 4, 5 and 7 | Mistral OCR 4.1 → **Azure Document Intelligence Layout + Claude Haiku 5.5**. Alternatively keep Mistral 4.1 if the spike favours its per-block confidence. PDFix Enterprise → **Aspose.PDF for .NET (Developer OEM)**, with PDFix as the spike alternative. |
| §10.2, INT-01, A-18, A-51, SC-03, SC-07 | The adapter targets Azure's `analyzeResult` plus LLM JSON. Cost baseline moves from $0.004 to **≈$0.011/page**. A-51 alt text moves from Mistral annotation to Haiku 5.5. |
| §10.3, INT-02, A-19, A-21, A-38, NFR-06, A-48, §6 Operations row | The writer runs **in process** in the .NET worker. The licensed-instance cap goes away (Developer OEM allows unlimited deployment locations), so "Unlicensed elastic PDFix worker" no longer applies. Keep the request/response contract. |
| §3 Non-goals, line 47 ("not build… on PDFBox") | Still holds. A new risk: we own the content-stream rewrite on Aspose. |
| §14 risks rows 1–2 | Replace PDFix licence and IR-fit risks with "Aspose fidelity (§6 #1–3)" and "two cloud processors' data terms". |
| §12.1 NFR-05, §6 Pricing, OQ-02 | Add a **minimum order charge** (about $5). Pricing input: at least about **$0.58/page** at 30k pages/month for 90% margin on 30-page orders, and about $0.38 at 100k. |
| OQ-04 | Replace with "Aspose EULA confirmation for SaaS + renewal policy". |
| OQ-05 | Becomes "Azure region/container + Anthropic ZDR terms". |
| OQ-11 | Pin veraPDF 1.30.4. Its PDF/UA-1 profile is not a full Matterhorn machine-check set. |
| T-005 | "Azure + Haiku → IR → Aspose writer → veraPDF → PDFium diff", running the tests in §6. Fall back to Stack C if #1–3 fail. |
