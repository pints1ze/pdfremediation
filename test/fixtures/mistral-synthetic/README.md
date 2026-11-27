# Hand-written Mistral OCR responses

These are not recordings. They follow the response shape documented for `POST /v1/ocr` with `include_blocks` and `confidence_scores_granularity: "block"`, and they describe `../headings-and-paragraphs.pdf` rendered at 150 dpi (1275 × 1650 px). Block boxes are the hand-written IR's boxes scaled to pixels and padded by about 4 px. So the Source objects a block links to should be the ones `../headings-and-paragraphs.ir.json` lists for the same Element.

The image block deliberately covers an empty area, to exercise a block type that IR v1 has no Element type for.

Real recordings from live runs are in `../recordings/`. The live response showed one thing these got wrong: block content is Markdown, so a real title reads `## Opening hours`.
