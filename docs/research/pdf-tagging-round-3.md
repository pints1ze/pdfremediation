# PDF tagging, round 3: a solo developer with no upfront licence budget

Research date: 2026-11-26. Prices are in USD unless stated, taken from vendor pages on that date, and will change. This builds on [round one](pdf-tagging-alternatives.md) and [round two](pdf-tagging-round-2.md) and does not repeat them. **UNVERIFIED** marks anything I could not confirm from a primary source. "Self-reported" means the benchmark number comes from the model's own authors. This is not legal advice.

**New constraint.** One developer. No licence costing $3,000 or more up front. Per-page and per-month costs are fine while gross margin is at least 90%.

**Margin rule.** This is round two's rule, unchanged: `P_min = (C + 0.30/N) / 0.071`, with Stripe at 2.9% + $0.30 ([stripe.com/pricing](https://stripe.com/pricing)). C is variable cost per page, including any licence amortised per page. N is the order size; this round uses **N = 30** pages and **5,000 pages/month**. Compute and hosting are excluded, as before.

## 1. Short answer

1. **No affordable Aspose path exists for production.** The free temporary licence lasts 30 days, at most three times in 12 months, and is "used for testing only" ([temporary-license](https://purchase.aspose.com/temporary-license/), [metered FAQ](https://purchase.aspose.com/faqs/licensing/metered)). Metered OEM costs a flat **$1,999/month** for the first 8,000 credits ([metered-oem](https://purchase.aspose.com/pricing/metered-pricing/metered-oem)). That is $0.40/page at 5k pages/month. Aspose.PDF **Cloud** and "Aspose.PDF for AWS" ($0.10/file) are REST APIs, and **neither has any tag-editing or PDF/UA endpoint** (§2.1). Use the temporary licence only to run the spike as a reference writer.
2. **Apryse, PDFix, Datalogics, Foxit and Nutrient are all quote-only or cost $1,300+/year.** None has a usage-based plan that fits (§2.2).
3. **The writer has to be free at launch.** There are two candidates, and we should spike both for two weeks:
   - **(a) a minimal custom .NET writer.** It splices `BDC`/`EMC` into the original content-stream bytes without re-serialising any operand, then appends an incremental update. It is built on PdfPig (Apache-2.0) plus our own small incremental writer.
   - **(b) OpenDataLoader** (Apache-2.0, Java), round one's pick.
   - **PDFsharp 6.2.4 (MIT) is not a good base.** It has no incremental save, and its content parser drops inline images (§2.4).
4. **Buy Aspose later, only if it beats our writer on the corpus.** The Developer OEM licence costs $3,597 ([pricing](https://purchase.aspose.com/pricing/pdf/net/)).
   - On its own, it is under 10% of revenue once revenue is **≥ $3.0k/month**.
   - To keep the whole business at 90% margin (card fees and API costs included), revenue must be **≥ about $7.9k/month**. At $0.50/page that is about 16k pages/month (§2.6).
5. **Understanding:** two options fit the IR, and I recommend both stacks below.
   - **Mistral OCR 4.1** gives block bounding boxes, list, header and footer blocks, and per-block confidence for $0.004/page, or $0.002 in batch ([pricing](https://docs.mistral.ai/inference/pricing), [basic_ocr](https://docs.mistral.ai/capabilities/document_ai/basic_ocr/)).
   - The best open-weight model is **PaddleOCR-VL-1.6** (Apache-2.0). It leads OmniDocBench v1.6 at 96.34 and returns block bboxes, labels, reading order and **per-box scores** ([OmniDocBench](https://github.com/opendatalab/OmniDocBench), [PaddleOCR-VL docs](https://www.paddleocr.ai/latest/en/version3.x/pipeline_usage/PaddleOCR-VL.html)).
   - Azure Layout costs 2.5× Mistral and has no list detection and no element confidence. Use only its cheap **Read** model ($0.0015/page) where scans need word boxes.

| Stack | Upfront | C ($/page) | Min price, 90% margin (5k pages/month, 30-page orders) | Complexity |
|---|---|---|---|---|
| **S1: Hosted understanding.** Mistral OCR 4.1 + Claude Haiku 5.5 + Azure Read (scans only) → IR → free writer → veraPDF | **$0** | **$0.0066** (worst case: every page scanned) | **$0.24** | Lowest. One .NET process, two or three APIs, and the veraPDF container. |
| **S2: Self-hosted understanding.** PaddleOCR-VL-1.6 on serverless GPU + Haiku 5.5 → IR → free writer → veraPDF | **$0** | **≈ $0.0021** | **$0.17** | Medium. Adds a Python/vLLM GPU image and cold starts. Documents never go to an OCR vendor. |
| S1 after buying Aspose Developer OEM (at 5k pages/month) | $3,597 | $0.066 | $1.08 | Low writer risk, but the licence is hard to justify at this volume |

**Pick S1 to launch.** It has the least to build, and the developer's time is the scarce resource. Move to S2 when a customer forbids cloud OCR (OQ-05, OQ-07) or when volume makes the $0.004 matter. With a $5 minimum order (round two), both stacks clear 90% at any realistic per-page price.

## 2. Writers with no or low upfront cost

### 2.1 Aspose

| Option | Cost to start | How cost grows | SaaS for 1 developer? | Can it tag existing content? |
|---|---|---|---|---|
| **Evaluation mode** (no licence) | $0 | — | No | Yes (same library), but every page gets an "Evaluation Only. Created with Aspose.PDF…" watermark and only the **first four pages** are processed ([licensing](https://docs.aspose.com/pdf/net/licensing/)). The "four elements of any collection" limit is documented only for Python-via-.NET (UNVERIFIED for .NET). |
| **Temporary licence** | $0 | — | **No, testing only**: "This free temporary license is unmetered and is used for testing only" ([metered FAQ](https://purchase.aspose.com/faqs/licensing/metered)) | Yes, full features for 30 days. Limited to "three (3), thirty (30) day licenses per customer per twelve (12) month period", and needs a business email ([temporary-license](https://purchase.aspose.com/temporary-license/)) |
| **Metered OEM** | **$1,999/month** flat for the first 8,000 credits | $0.02/credit for the next 80k, $0.01 for the next 160k, $0.005 for the next 800k, $0.002 beyond. One credit = one document open or save ([metered-oem](https://purchase.aspose.com/pricing/metered-pricing/metered-oem)) | Yes. Metered Small Business is the same price but marked "No" for SaaS ([pricing](https://purchase.aspose.com/pricing/pdf/net/)) | Yes. It needs the licence server; it falls back to evaluation mode if offline (grace period quoted as 7 days or 24 hours, UNVERIFIED which) ([metered FAQ](https://purchase.aspose.com/faqs/licensing/metered)) |
| **Developer OEM** (round two) | $3,597 perpetual, 1 year of updates | Flat | Yes ([pricing](https://purchase.aspose.com/pricing/pdf/net/)) | Yes ([edit-pdf-file-tags](https://docs.aspose.com/pdf/net/edit-pdf-file-tags)) |
| **Aspose.PDF Cloud** (REST, also a self-hosted `aspose/pdf-cloud` Docker image) | Free tier, then per call. The PDF tier table could not be read (the site loops on redirects). The Cells Cloud tiers are 150 free calls, then 1,000 calls for $30, then $0.09→$0.007/call ([cells pricing](https://docs.aspose.cloud/cells/pricing-plan/)). PDF assumed to be the same: UNVERIFIED. Self-hosting uses the same pricing ([self-hosting](https://purchase.aspose.cloud/self-hosting), [hub](https://hub.docker.com/r/aspose/pdf-cloud)) | Per call | Yes | **No.** None of the 385 public methods in the official .NET SDK's `PdfApi.cs` touch tags, structure, marked content or PDF/UA. The only standards conversion is PDF/A (1A/1B/3A/3B) ([aspose-pdf-cloud-dotnet](https://github.com/aspose-pdf-cloud/aspose-pdf-cloud-dotnet)). |
| **Aspose.PDF for AWS** (Marketplace container) | $0 upfront | $0.10/file for the first 8,000, then $0.02, $0.01, $0.005, $0.001 ([listing](https://aws.amazon.com/marketplace/pp/prodview-zc64pent6p6lo)) | Yes | Presumably the Cloud API, so **no** (UNVERIFIED) |

**Conclusion.** Only the Developer OEM and Metered OEM licences cover the BDC/MCR tagging API in production, and both cost too much for now. Use the temporary licence once, during the spike, as a reference output for checking our own writer.

### 2.2 Other commercial SDKs

| SDK | Cost to start | Growth | SaaS for 1 developer | Tags existing content with our tree? |
|---|---|---|---|---|
| **Apryse Server SDK** | Quote only, "as low as $1500 a year" ([guide](https://apryse.com/pricing/guide)). No startup programme found. The trial runs **40 active days** and then throws errors ([demo](https://docs.apryse.com/core/guides/info/demo)). Trial output is watermarked (search snippet, UNVERIFIED). | Modular quote | Quote | Partly. `SElement.CreateContentItem` exists ([SElement](https://sdk.apryse.com/api/PDFTronSDK/dotnetcore/pdftron.PDF.Struct.SElement.html)), but marking content means rewriting the page with `ElementWriter` (round two). Whether manual structure edits need a paid add-on is UNVERIFIED. |
| **PDFix SDK** | SDK Lite is "Free forever" but only reads and renders. Basic, Pro and Enterprise are quote-only "by instance count", and remediation and auto-tag are **Enterprise only** ([sdk pricing](https://pdfix.net/pricing/sdk)). Desktop Pro €350/user/year ([pricing](https://pdfix.net/pricing/)). **No AWS Marketplace listing and no per-page plan found.** The trial "may apply a watermark… and redact random parts" ([hub](https://hub.docker.com/r/pdfix/autotag-paddle)). | Per instance | Quote | Yes (round two) |
| **Datalogics APDFL** | $7,499/year (round two). The pricing page now returns 404 (not re-verified). | Quote | Quote | Low-level, yes |
| **Foxit PDF SDK** | Quote, after a 30-day trial ([sdk](https://www.foxit.com/sdk/)). The hosted Document APIs cost $1,750/year for 5,000 credits ([pricing](https://app.developer-api.foxit.com/pricing)), but have no tagging endpoint. | Credits | Yes (API) | SDK UNVERIFIED; API no |
| **Nutrient (GdPicture / DWS)** | Quote. 30-day trial with watermark ([pricing](https://www.nutrient.io/sdk/pricing/)). DWS auto-tag costs $0.395/page (round two), which breaks the margin. | Usage | Quote | Documented for new content only (round two) |
| **ComPDF** | From $1,300 by quote. 200+ free API calls a month ([pricing](https://www.compdf.com/pricing)) | Quote | Quote | No tagging API advertised (UNVERIFIED) |
| **Syncfusion Community** | **Free** under $1M revenue, ≤5 developers, ≤10 employees ([community](https://www.syncfusion.com/products/communitylicense)) | — | Commercial use allowed | **No.** Tags are written only at creation time, and loaded documents are read-only ([docs](https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-tagged-pdf)) |
| **IronPDF** | From $999 perpetual. SaaS needs a quoted add-on ([licensing](https://ironpdf.com/licensing/)) | — | Add-on | **No.** `SaveAsPdfUA` "does not build a content-level structure tree from the existing page content" ([reference](https://ironpdf.com/object-reference/api/IronPdf.PdfDocument.html)) |
| **iText, Pdftools, Spire.PDF** | AGPL or quote; "Per Page" pricing by quote; pricing page returned 403 ([iText](https://itextpdf.com/how-buy), [Pdftools](https://www.pdf-tools.com/pricing/)) | Quote | Quote | iText: no route (round two). Spire: new content only ([guide](https://www.e-iceblue.com/Tutorials/Java/Spire.PDF-for-Java/Program-Guide/Document-Operation/Java-Create-a-Tagged-PDF-Document.html)) |
| **PDFsharp 6.5 / 7.0 commercial** | **€750 perpetual**, unlimited developers and servers, SaaS allowed, covers the 6.5.x and 7.0.x lines ([pricing](https://pdfsharp.com/Pricing)) | Flat | Yes | No tagging of existing content (§2.4). It would only be a low-level object library. |

No vendor found sells per-page or per-month access to an API that binds **our** structure to existing content. That rules out the "pay as you go" writer the constraint hoped for.

### 2.3 The free paths, re-estimated for one developer

All estimates below are mine, not measured.

| Path | What already exists | What we build | Realistic effort, solo | Main risks |
|---|---|---|---|---|
| **A. Minimal custom .NET writer, byte-splicing** (recommended to spike first) | PdfPig 0.1.16 stable (Apache-2.0, 2026-08-22): parsing, objects, letters with geometry ([NuGet](https://www.nuget.org/packages/PdfPig)). `PdfIncrementalUpdateBuilder` was merged 2026-11-01 but is **alpha only** (0.1.17-alpha) ([PR #1495](https://github.com/UglyToad/PdfPig/pull/1495)). PDFium is already shipped for the pixel diff. | (1) A lexer that records the **byte offset** of every operator. (2) An operator → bbox map, by running our own graphics and text state, or by matching PdfPig letters in content order (PdfPig doesn't expose operator indices: UNVERIFIED). (3) A splicer that copies the original bytes and inserts `/Tag <</MCID n>> BDC` / `EMC` and `/Artifact BMC` / `EMC` at operator boundaries. It drops only old structural `BDC`s that carry an MCID and **keeps `/OC` sequences**. (4) Builders for the struct tree, ParentTree, `/StructParents`, link OBJRs, `/Lang`, title, `DisplayDocTitle` and XMP `pdfuaid`. (5) An append-only incremental writer (new objects + xref + `/Prev` trailer), or PdfPig's alpha builder. (6) A glyphless-font invisible text layer for scans (Tesseract's technique, round one §4). | **MVP in 6–10 weeks**, covering born-digital pages with P/H1–H6/L/Table/Figure/Artifact and shared Form XObjects treated as Artifact. **About 3–5 more months** to pass veraPDF across a broad corpus (Type 3 fonts, annotations, forms drawn more than once, huge streams). Round one's 5–10k LOC figure still holds for the full version; the MVP is about 3–5k. | Operator-to-geometry mapping is the hardest part. Nesting with `q`/`Q` and `BT`/`ET` (a marked-content sequence that starts inside a text object must end inside it). Content split across a `/Contents` array (concatenate into one new stream). |
| **B. OpenDataLoader 2.5.x** (Apache-2.0, Java sidecar) | Content rewriting with MCIDs, Artifact marking, struct tree, forms and annotation edge cases (round one §2.2) | An IR → DoclingDocument adapter or a Java API call (round one §2.5), plus a PDF/UA-1 finishing pass | **3–6 weeks** if the IR input works; much more if we have to fork its matching | It **re-serialises tokens** (fidelity is UNVERIFIED). It **drops `/OC` BDCs** (round one §2.4). The IR input is still unverified. It runs one document per JVM, and weekly releases need pinning. A JVM is needed anyway for veraPDF. |
| **C. Custom on PDFBox 3.0.8** (Java) | Every primitive, including `saveIncremental` (round one) | Everything in A, but in Java | Like A, plus Java | Contradicts the PRD non-goal (line 47); only worth it if A is blocked by .NET gaps |

The byte-splicing design is the important difference from rounds one and two. Every candidate writer, Aspose, PDFix, OpenDataLoader and PDFsharp alike, **parses and re-serialises** the content stream. That made "content-stream fidelity" round two's top risk. If only marked-content operators are inserted between untouched original bytes, operands never change. Pixel identity then depends only on not breaking operator order or `q`/`Q` state. The diff gate still has to prove it.

### 2.4 Is PDFsharp 6.2.4 (MIT) a viable base for the minimal writer?

**No, not as the base.** At most it is a dictionary-building helper.

| Need | PDFsharp 6.2.4 | Source |
|---|---|---|
| Licence | 6.2.4 (2026-01-06) is the **last MIT release**. 6.5.0 and 7.0.0-preview-2 (2026-10-14) are AGPL-3.0-only or €750 commercial. | [NuGet 6.2.4](https://www.nuget.org/packages/PDFsharp/6.2.4/license), [repo](https://github.com/empira/PDFsharp), [pricing](https://pdfsharp.com/Pricing) |
| Parse content into operators | Yes: `ContentReader.ReadContent` → `CSequence` | [Pdf.Content](https://github.com/empira/PDFsharp/tree/master/src/foundation/src/PDFsharp/src/PdfSharp/Pdf.Content) |
| Write content back faithfully | **No.** `CLexer.ScanInlineImage` discards inline images ("We currently do nothing with inline images"). Hex strings come back as literals. Strings starting `FE FF` are decoded as UTF-16, which can corrupt CID glyph codes. Reals are reformatted. | Same source tree, read at tag v6.2.4 |
| Struct tree / number trees | `PdfStructureTreeRoot` and `PdfNumberTreeNode` exist, and any `PdfDictionary` can be edited | same |
| Incremental save | **None.** PR #386 (append-only incremental update) has been open since 2026-08-19. Issue #302 (open) reports content loss when **reading** incrementally updated PDFs. | [PR #386](https://github.com/empira/PDFsharp/pull/386), [#302](https://github.com/empira/PDFsharp/issues/302) |

A writer that uses neither PDFsharp's content writer nor its full save gets little from it. A full save rewrites every object, and #302 suggests it can lose content. Build path A on PdfPig and a byte-level splicer instead. Round one said to avoid PDFsharp because 6.2.4 is a frozen line; that still holds. The new €750 commercial licence fixes the licence problem but not these functional gaps (whether 6.5 fixed inline images is UNVERIFIED).

### 2.5 Staged path

| Stage | Trigger | Writer | Understanding | Cost |
|---|---|---|---|---|
| **0. Spike** (weeks 1–2) | Now | Spike A (byte-splice) and B (OpenDataLoader) on 20–50 corpus files. Run Aspose under a **temporary licence** as the reference writer: compare veraPDF pass rate and pixel diff. | Mistral OCR 4.1 + Haiku 5.5; PaddleOCR-VL-1.6 on Modal using the $30/month free credit ([modal](https://modal.com/pricing)); Azure F0 free 500 pages/month ([pricing](https://azure.microsoft.com/en-us/pricing/details/ai-document-intelligence/)) | ≈ $0 |
| **1. Launch** | Spike done | Whichever of A or B passes more files with diff = 0; A wins ties because it is all .NET. Route hard cases to Review or Failed. | S1 | $0 upfront; C ≈ $0.0066 |
| **2. Licence** | Revenue ≥ **$7.9k/month** for 3 months **and** Aspose passes clearly more corpus files than our writer | Aspose Developer OEM behind the same IR → writer interface | S1 or S2 | $3,597 once, plus renewal for updates (price UNVERIFIED) |
| **3. Scale** | Volume or data-residency requirement | Unchanged | S2 (self-hosted) or Azure/Mistral containers | GPU time |

### 2.6 When the Aspose licence is under 10% of revenue

Assumption: the $3,597 is counted as a yearly cost, because it includes only one year of updates.

- **Licence alone ≤ 10% of revenue:** annual revenue ≥ $35,970, i.e. **$3.0k/month**. At $0.50/page that is 6,000 pages/month.
- **Whole business stays at ≥ 90% margin** (S1, P = $0.50, N = 30): the card fee is 2.9% + $0.30/$15 = 4.9% of revenue, and APIs are 0.0066/0.50 = 1.3%. That leaves **3.8%** for the licence. Revenue must then be ≥ $3,597/0.038 ≈ **$95k/year (≈ $7.9k/month, ≈ 16k pages/month at $0.50)**.
- At 5k pages/month, the licence adds $0.060/page and pushes the 90% floor from $0.24 to $1.08.

## 3. Document understanding, head to head

### 3.1 Hosted APIs, for our task

| | **Azure DI Layout v4.0** (2024-11-30, still the newest GA ([what's new](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/whats-new))) | **Mistral OCR 4.1** (`mistral-ocr-4-1`, GA ([card](https://docs.mistral.ai/models/model-cards/ocr-4-1))) | **GLM-OCR API** (Z.ai `glm-ocr`, [guide](https://docs.z.ai/guides/vlm/glm-ocr)) |
|---|---|---|---|
| Price | $0.010/page. Batch Layout also $0.010. F0 free 500 pages/month. Read model $0.0015 ([Retail Prices API](https://prices.azure.com/api/retail/prices), [pricing](https://azure.microsoft.com/en-us/pricing/details/ai-document-intelligence/)) | **$0.004**; batch $0.002 and annotations $0.005 UNVERIFIED (not visible on the pricing page as of 2026-11-26 check) ([pricing](https://docs.mistral.ai/inference/pricing)). | **$0.03 per 1M tokens**, input and output ([pricing](https://docs.z.ai/guides/overview/pricing)). Per-page token count is unpublished. At an assumed 3–5k tokens/page that is ≈ $0.0001–0.00015 (UNVERIFIED). |
| Reading order | Yes | Yes (`include_blocks` are returned in reading order) | `index` per layout item |
| Heading levels | `title`, `sectionHeading` and a `sections[]` tree; no levels ([layout](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/prebuilt/layout)) | `title` block only; no levels ([basic_ocr](https://docs.mistral.ai/capabilities/document_ai/basic_ocr/)) | No heading label; labels are text, image, table, formula ([layout-parsing](https://docs.z.ai/api-reference/tools/layout-parsing)) |
| Lists | **None documented** | `list` block | No |
| Tables with spans | `rowSpan`/`columnSpan`, `kind: columnHeader`, a polygon per cell | `table_format=html`. rowspan/colspan claimed; fidelity UNVERIFIED | Table HTML; spans UNVERIFIED |
| Figures | `figures[]` with polygons and caption | `image` block with bbox | `image` with `bbox_2d` |
| Header/footer | `pageHeader`, `pageFooter`, `pageNumber`, `footnote` | `header`, `footer` blocks | No |
| Element bbox | Polygons on paragraphs, lines, **words** and cells | Bbox on every block; word bbox UNVERIFIED | Normalised `bbox_2d` per item |
| Per-element confidence | **Words only**; nothing on paragraphs | **Block-level** content and type confidence; also word level | **None** |
| Handwriting / scans | `styles[].isHandwritten` with confidence | Not claimed by Mistral (only an Azure Foundry listing claims it, UNVERIFIED) | Self-reported 87.0 in-house handwritten ([arXiv 2603.10910](https://arxiv.org/html/2603.10910v2)) |
| Independent benchmark | **None found** on OmniDocBench or olmOCR-Bench | Only the **2025** Mistral OCR is benchmarked: OmniDocBench v1.6 85.66, v1.5 78.83, olmOCR-Bench 72.0 ([OmniDocBench](https://github.com/opendatalab/OmniDocBench), [olmocr](https://github.com/allenai/olmocr)). OCR 4's 93.07 OmniDocBench (version unstated) and 85.2 olmOCR-Bench ("directional") are **self-reported** ([news](https://mistral.ai/news/ocr-4)). | Model scores OmniDocBench v1.6 95.22 (leaderboard); olmOCR-Bench 75.2 excluding headers/footers (third-party run, [eval yaml](https://huggingface.co/zai-org/GLM-OCR/blob/main/.eval_results/olmocrbench.yaml)) |
| Self-host | Layout container (connected tiers are cheaper) | Enterprise only, unpriced ([news](https://mistral.ai/news/ocr-4)) | Yes: MIT weights (§3.2) |

**GLM family.** The dedicated OCR model is **GLM-OCR**: about 0.9B parameters (0.4B CogViT + 0.5B GLM decoder), with PP-DocLayoutV3 for layout in its SDK. Its tech report is arXiv 2603.10910 (March 2026). Weights are **MIT**, the SDK is **Apache-2.0**, and PP-DocLayoutV3 is Apache-2.0 ([HF](https://huggingface.co/zai-org/GLM-OCR), [GitHub](https://github.com/zai-org/GLM-OCR)). It serves on vLLM ≥ 0.19, SGLang ≥ 0.5.10, Ollama or Transformers ([README](https://raw.githubusercontent.com/zai-org/GLM-OCR/main/README.md)). No VRAM figure is published; a 1B BF16 model should fit a 16–24 GB GPU (UNVERIFIED). The general GLM-4.6V/4.5V vision models cost $0.3/$0.9 and $0.6/$1.8 per MTok, and GLM-4.6V-Flash is free ([pricing](https://docs.z.ai/guides/overview/pricing)). Their weight licences were not checked (UNVERIFIED). They are not document-layout models.

**Reading for our IR.** Mistral 4.1 covers the most IR fields from one call: reading order, lists, header and footer, figures, and per-block confidence. It still needs Haiku for heading levels, which round two already planned. Azure's strength is **word** polygons with confidence, which is exactly what the invisible text layer for scans needs, and its Read model provides them for $0.0015. GLM-OCR is very cheap but has no header/footer, list or heading labels and no confidence, so it is not enough for tagging alone.

### 3.2 Open-weight models

Benchmark sources:
- **OmniDocBench v1.6_full** is the current leaderboard, last updated 2026-11-26; v1.6 shipped 2026-04-10 ([README](https://github.com/opendatalab/OmniDocBench)).
- **v1.5** dates from 2025-09-25 ([v1_5 branch](https://github.com/opendatalab/OmniDocBench/blob/v1_5/README.md)).
- v1.5 and v1.6 scores **can't be compared with each other**: read-order edit is about 0.04 on v1.5 and 0.12–0.14 on v1.6.
- **olmOCR-Bench** figures come from the [allenai/olmocr README](https://github.com/allenai/olmocr) unless marked; every entry on the HF leaderboard is `verified=false`.
- For OmniDocBench, Overall, CDM and TEDS are higher-is-better; TextEdit and ReadOrder are lower-is-better.

| Model | Weights licence | OmniDocBench **v1.6** Overall / TextEdit / TEDS / ReadOrder | OmniDocBench **v1.5** Overall / TEDS / ReadOrder | olmOCR-Bench overall (H&F, Old scans) | **Bbox per element?** | Labels useful for tagging | Confidence | Handwriting |
|---|---|---|---|---|---|---|---|---|
| **PaddleOCR-VL-1.6** (0.9B, 2026-05-28) | **Apache-2.0** ([HF](https://huggingface.co/PaddlePaddle/PaddleOCR-VL-1.6)) | **96.34** / 0.0326 / 94.76 / 0.128 | 1.5: 94.50 / 92.76 / 0.042 | v1: 80.0 (97.0, 37.8). 1.6 Old scans only: 38.6, self-reported | **Yes**: `block_bbox`, `block_label`, `block_order` | doc_title, paragraph_title, text, header, footer, number, footnote, image, table, formula, chart, aside_text ([docs](https://www.paddleocr.ai/latest/en/version3.x/pipeline_usage/PaddleOCR-VL.html)) | **Per layout box** (`layout_det_res.boxes[].score`) | Ocean-OCR-HW edit 0.118 EN (paper, self-reported) ([arXiv 2510.14528](https://arxiv.org/html/2510.14528)) |
| MinerU2.5-Pro-2605 (1.2B) | Apache-2.0 weights, but the **code licence is custom**: online services must prominently credit MinerU, plus caps at 100M MAU / $20M monthly revenue ([HF](https://huggingface.co/opendatalab/MinerU2.5-Pro-2605-1.2B), [LICENSE.md](https://github.com/opendatalab/MinerU/blob/master/LICENSE.md)) | 95.75 / 0.036 / 93.42 / 0.120 | — | MinerU 2.5.4: 75.2 | Yes (layout stage) | text, title, list, header, footer, page number, captions, table, image ([arXiv 2509.22186](https://arxiv.org/html/2509.22186)) | UNVERIFIED | Handwritten equations 94.4 CDM |
| MinerU2.5-2509 (1.2B) | **AGPL-3.0** ([HF](https://huggingface.co/opendatalab/MinerU2.5-2509-1.2B)) | 93.04 / 0.045 / 87.88 / 0.130 | 90.93 / 88.44 / 0.044 | 75.2 | Yes | as above | UNVERIFIED | — |
| **GLM-OCR** (0.9B) | **MIT** | 95.22 / 0.044 / 92.83 / 0.133 | 94.35 / 93.89 / 0.047 (paper, self-reported: 94.62) | 75.2 excl. H&F (third party) | Yes via the SDK / API `bbox_2d` | text, image, table, formula only | No | 87.0 in-house (self-reported) |
| dots.ocr (3B) / dots.mocr (2026-03-19) | MIT tag plus a **custom licence agreement** that permits SaaS; breaching its acceptable-use list terminates the licence automatically ([file](https://huggingface.co/dots-studio/dots.mocr/blob/main/dots.mocr%20LICENSE%20AGREEMENT)) | dots.ocr: 90.77 / 0.048 / 87.18 / 0.138 | 88.41 / 86.78 / 0.053 | dots.ocr 79.1; dots.mocr **83.9** (self-reported) ([repo](https://github.com/rednote-hilab/dots.ocr)) | **Yes**: `{bbox, category, text}` | Title, Section-header, List-item, Page-header, Page-footer, Caption, Footnote, Table, Picture | No | None claimed |
| DeepSeek-OCR-2 (3B, 2026-01-27) | Apache-2.0 ([HF](https://huggingface.co/deepseek-ai/DeepSeek-OCR-2)); v1 MIT | 90.25 / 0.050 / 83.89 / 0.144 | 89.17 / 85.60 / 0.060 | v1 75.7; v2 76.3 (tweet-sourced, UNVERIFIED) | Only in `<|grounding|>` mode, format undocumented | UNVERIFIED | No | None claimed |
| Qwen3-VL (2B–235B) | Apache-2.0 ([HF](https://huggingface.co/Qwen/Qwen3-VL-8B-Instruct)) | 235B: 89.78 / 0.063 / 83.07 / 0.166 | 235B: 89.15 / 86.21 / 0.068 | — | Grounding by prompt | Whatever is prompted | No | — |
| olmOCR-2-7B-1025 | Apache-2.0 ([HF](https://huggingface.co/allenai/olmOCR-2-7B-1025)) | olmOCR (version unstated): 85.74 / 0.139 / 83.00 / 0.216 | 81.79 / 68.92 / 0.121 | **82.4** (96.1, 47.7) | **No** (Markdown only) | None | No | None claimed |
| Nanonets-OCR2-3B | **No licence declared** on HF (base model Qwen2.5-VL-3B) ([HF](https://huggingface.co/nanonets/Nanonets-OCR2-3B)) | OCR-s: 83.61 | OCR-s: 85.59 | **69.5** (H&F 32.1) | **No** | Semantic tags only | No | Claimed, no number |
| Granite-Docling-258M | Apache-2.0 ([HF](https://huggingface.co/ibm-granite/granite-docling-258M)) | Not listed | Not listed | Not listed | Yes (DocTags `<loc_>` tokens) | Section headers, footers (full list UNVERIFIED) | No | — |

For context, gemini 3 Pro scores 92.91 and GPT-5.2 scores 86.59 on v1.6, so the small specialised models beat the frontier general models.

**Which return boxes.**
- **Yes:** PaddleOCR-VL, MinerU, GLM-OCR (SDK), dots.ocr/mocr, Granite-Docling and Mistral OCR 4.1 return boxes natively. Azure returns polygons down to word level.
- **Prompt only:** DeepSeek-OCR and Qwen3-VL give boxes only when prompted for grounding.
- **No:** olmOCR and Nanonets return Markdown without geometry. They can't map to the PDF's content without a second layout pass, so they are **unsuitable as the IR source**.
- **Spans and heading levels:** none of the open models documents table rowspan/colspan output or heading levels 1–6. Test both on the corpus.

**Licence verdict.**
- **Clean:** PaddleOCR-VL, GLM-OCR, DeepSeek-OCR(-2), Qwen3-VL, olmOCR and Granite-Docling.
- **Caution:** dots.mocr (its custom AUP can terminate the licence) and MinerU (an attribution clause for online services, and the code licence changed four times in March–April 2026).
- **Avoid:** MinerU2.5-2509 (AGPL) and Nanonets (no licence granted).

### 3.3 Self-hosting cost per page

Rental prices are from [RunPod](https://www.runpod.io/pricing), [Modal](https://modal.com/pricing) and Vast.ai (a live market snapshot, not list prices). Cost/page = $/hr ÷ (pages/s × 3600), at **100% utilisation**.

| Model | Published throughput (source) | GPU, $/hr | $/page |
|---|---|---|---|
| PaddleOCR-VL (v1; 1.6 not separately measured) | 1.345 pages/s, vLLM, 1× A100 ([paper Table 13](https://arxiv.org/html/2510.14528)) | RunPod Community A100 SXM $1.39 / Modal A100-40 $2.10 / RunPod Serverless A100 $2.99 | **$0.00029 / $0.00043 / $0.00062** |
| MinerU2.5 | 2.12 pages/s A100-80; 1.70 on RTX 4090 48G; 4.47 on H200 ([paper Table 3](https://arxiv.org/html/2509.22186)) | A100 SXM $1.39; 4090 $0.34 (Community) | $0.00018; $0.000056 |
| DeepSeek-OCR | "200k+ pages per day" on 1× A100-40G (≈ 2.3 pages/s) ([arXiv 2510.18234](https://arxiv.org/abs/2510.18234)) | Modal A100-40 $2.10 | $0.00025 |
| olmOCR | $176 per 1M pages on L40S, $178 on H100 ([arXiv 2502.18443](https://arxiv.org/html/2502.18443)) | L40S $0.79 (the paper's price, still RunPod Community's) | $0.00018 |
| dots.ocr | 0.352 pages/s on A100 (measured in the PaddleOCR-VL paper) | A100 SXM $1.39 | $0.0011 |
| GLM-OCR | 1.86 pages/s, single concurrency, **GPU not named** ([arXiv 2603.10910](https://arxiv.org/html/2603.10910v2)) | Can't compute; on an A100 at $1.39 it would be ≈ $0.00021 (assumption) | UNVERIFIED |
| Granite-Docling, Nanonets-OCR2, Qwen3-VL | None published | — | — |

**At 5k pages/month, utilisation is the real cost.** 5,000 pages at 1.3 pages/s is about 64 GPU-minutes a month. A rented always-on A100 (~$1,000/month) would be absurd at this volume. Serverless (RunPod Serverless or Modal per-second) costs about $3/month of GPU time plus cold starts, so I budget **$0.001/page** for PaddleOCR-VL including cold-start overhead. Cold-start time is UNVERIFIED.

## 4. Recommended stacks

Both stacks share the same .NET worker, the free writer from §2.5 behind an IR → writer interface, `verapdf/rest`, and the PDFium diff. C includes Claude Haiku 5.5 at $0.10/$0.50 per MTok (confirmed, [pricing](https://platform.claude.com/docs/en/about-claude/pricing)), which comes to $0.00105/page per round two's token model.

| | **S1: Hosted (launch)** | **S2: Self-hosted OCR** |
|---|---|---|
| Understanding | Mistral OCR 4.1 with `include_blocks` and block confidence, $0.004. Haiku 5.5 for heading levels, list items and alt text, $0.00105. Azure **Read** for word polygons on scanned or mixed pages only, $0.0015. | PaddleOCR-VL-1.6 (Apache-2.0) on RunPod or Modal serverless, ≈ $0.001 including cold starts. Haiku 5.5, $0.00105. Word boxes for the OCR layer from PaddleOCR text lines (1.5+ line spotting ([HF](https://huggingface.co/PaddlePaddle/PaddleOCR-VL-1.5))) or Tesseract (free). |
| Per-element confidence | Native, per block | Native layout-box score |
| Upfront | $0 | $0 |
| **C / page** | **$0.0066** worst case (all pages scanned); $0.0051 born-digital. With Mistral batch: $0.0046 / $0.0031. | **≈ $0.0021** |
| **Min price, 90% margin** (5k pages/month, 30-page order) | **$0.24** ($0.21 born-digital) | **$0.17** |
| Min price, 1-page order | $4.32 | $4.26 |
| Fixed monthly | $0, apart from hosting | $0 plus GPU minimums; Modal includes $30/month free credit |
| Complexity | Low: three HTTP adapters, all .NET | Medium: a Python/vLLM image, a GPU provider account, cold starts, model upgrades |
| Data residency | Two cloud processors (Mistral, Microsoft) plus Anthropic | Only Anthropic (Haiku). Replace it with a self-hosted Qwen3-VL to remove that too. |
| Benchmark evidence | Weak: OCR 4 numbers are self-reported | Strong: #1 on OmniDocBench v1.6 among open models |

Margin check at $0.50/page on 30-page orders: S1 costs 1.3% + 4.9% card = **93.8%** margin; S2 costs 0.4% + 4.9% = **94.7%**. The card fee, not the APIs, dominates, so a minimum order charge still matters more than the choice of stack.

## 5. Corrections to rounds one and two

| Earlier claim | Correction |
|---|---|
| Round two: Mistral batch 50% off and $5/1k annotations are UNVERIFIED | Still UNVERIFIED: a 2026-11-26 check of the pricing page shows only the $4/1,000 standard price ([pricing](https://docs.mistral.ai/inference/pricing)) |
| Round one: PaddleOCR-VL-1.5 is current | **1.6** (2026-05-28) is current, and tops OmniDocBench v1.6 among open models |
| Round one: PDFsharp should be avoided only for licence reasons | A €750 commercial licence now exists. The blockers are functional instead: no incremental save, and inline images lost on round-trip (§2.4). |
| Round two: PdfPig's `PdfIncrementalUpdateBuilder` was "added 2026-10-20" | The PR merged **2026-11-01** ([PR #1495](https://github.com/UglyToad/PdfPig/pull/1495)). It is still alpha only. |
| Round two: content-stream fidelity is an unavoidable writer risk | It is avoidable if the writer byte-splices instead of re-serialising (§2.3). This is an untested design claim. |
| Round two: Azure Layout is the best IR fit | For the IR's per-element confidence and lists, **Mistral 4.1 fits better**. Azure is best only for word polygons, and its Read model is enough for that. |

## 6. Least certain claims

- How long the byte-splicing writer takes to build (6–10 weeks for an MVP) and how well it performs. Both are estimates; spike first.
- Whether operators can be mapped to geometry through PdfPig letters or PDFium page objects in content order (UNVERIFIED).
- GLM-OCR tokens per page, and therefore its per-page API cost.
- Serverless GPU cold-start time, and PaddleOCR-VL-**1.6** throughput (only v1's is published).
- Mistral OCR 4.1 table-span fidelity and word bboxes. Its independent benchmark is a year old.
- Aspose PDF Cloud per-call price and Developer OEM renewal price.
- Whether the Apryse trial watermarks output, and whether its structure edits need an add-on.
