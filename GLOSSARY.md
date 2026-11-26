# PDF Remediation

A paid service that makes a customer's PDF more accessible without changing how its pages look, and reports honestly on what automation could and could not verify.

## Purchase

**Customer**:
One person's account. Every Document, Quote, and Job belongs to exactly one Customer; organizations are not modeled yet.
_Avoid_: Account, organization, tenant

**Document**:
One PDF uploaded by a customer, plus what Preflight learned about it.
_Avoid_: File, upload

**Preflight**:
The smallest set of checks needed to price a Document: that it is a readable, modifiable, unsigned PDF within the limits, and how many Billable pages it has. It deliberately does no expensive processing before payment.
_Avoid_: Inspection, analysis

**Billable page**:
Every page the PDF contains, including blank and cover pages. A Quote's total is the Billable page count × unit price.
_Avoid_: Processed page

**Quote**:
An immutable price offer for one Document (page count × unit price), valid until it expires.
_Avoid_: Estimate, price

**Payment**:
The provider-confirmed transaction for a Quote. It is Authorized at checkout, then either Captured or Released depending on how the Job ends.
_Avoid_: Charge, checkout

**Authorization**:
A hold on the customer's funds for a Quote's total, placed at checkout. A Job may start only once its Payment is Authorized.
_Avoid_: Paid, escrow, deposit

**Capture**:
Charging the held funds. It happens only when the Job is Completed.
_Avoid_: Settlement, payment

**Release**:
Cancelling the hold without charging. It happens when the Job Fails, is Cancelled, or cannot complete before the Authorization expires.
_Avoid_: Refund, void

**Order**:
Customer-facing word for a paid Quote. It is not a separate concept.
_Avoid_: Purchase

## Processing

**Job**:
The processing run for a paid Quote, from Preprocessing to a final Job status.
_Avoid_: Task, remediation request

**Preprocessing**:
The Job's first stage. It prepares the Document's pages for analysis after payment.
_Avoid_: Preflight

**Source object**:
Something physically present in the original PDF: a text run, an image, a vector drawing, or a form field. A scanned page is typically a single image Source object.
_Avoid_: Element, content block

**Element**:
A semantic unit in the Accessibility IR, such as a heading, paragraph, list item, table cell, or figure. It points to the Source objects, or the region of an image, that it describes.
_Avoid_: Block, node, Source object

**Accessibility IR**:
The service-owned, vendor-neutral description of a Document's Elements, their reading order, and their Source objects. It is the single source of truth for the tag tree written into the output.
_Avoid_: Model output, OCR result

**Document classification**:
Whether a Document is born-digital, scanned, or mixed, derived from its Source objects. A mixed Document combines native content (e.g. pages exported from Word) with image-only content (e.g. a scanned page inserted into the PDF). It is descriptive only.
_Avoid_: Page classification, page type

**Attempt**:
One try at one stage of a Job. Retries, automatic or by an operator, add Attempts to the same Job and never create a new Job, Quote, or Payment.
_Avoid_: Run, retry job

**Repair pass**:
An Attempt that changes the remediation to clear Validation failures, escalating from targeted fix-ups to structural fallbacks to re-analysis. Each structural fallback raises a Review item.
_Avoid_: Retry, rerun

**Visual preservation**:
The guarantee that the output's pages render identically to the original: zero differing pixels at the pinned renderer and DPI.
_Avoid_: Visual similarity, appearance check

**Job status**:
Whether processing produced a deliverable output. It ends at Completed, Failed, or Cancelled (only possible while the Job is still queued, which Releases the hold). A Job is Completed only when its output passes Visual preservation and veraPDF validation; only a Completed Job's output can be downloaded.
_Avoid_: Result, outcome

## Results

**Result verdict**:
What a Completed Job's output earned: `Manual review recommended` or `Automated checks passed`. It is never a compliance claim.
_Avoid_: Compliance status, score

**Validation failure**:
A machine-checkable PDF/UA rule that the output breaks, as reported by veraPDF. A Job cannot complete while any remain.
_Avoid_: Error, review item

**Review item**:
A semantic concern on a page or element that a human should check, such as low confidence, a complex table, reading order, alt text, or headings.
_Avoid_: Warning, issue, validation failure

**Confidence**:
A model's certainty, from 0 to 1, about a single Element. There is no document-level confidence, and confidence is never a compliance score.
_Avoid_: Quality score, semantic confidence

**Refund**:
Returning funds after Capture, as a rare exception an operator handles. Failed Jobs are never refunded because they are never Captured.
_Avoid_: Release, credit, chargeback

**Failure report**:
What the customer receives instead of an output when a Job Fails: customer-safe failure categories with page numbers, and confirmation that the hold was Released.
_Avoid_: Error page, results

## PDF accessibility

**Alt text**:
A text description of an informative Figure. The service drafts it with a model and always raises a Review item for it.
_Avoid_: Caption, description

**Artifact**:
PDF content that assistive technology should ignore, such as page headers, footers, and decoration (the PDF/UA meaning).
_Avoid_: Using "artifact" for stored files

**Stored file**:
Any file the service keeps for a Document or Job: the original, page renders, the Accessibility IR, the remediated PDF, or reports.
_Avoid_: Artifact, blob, attachment
