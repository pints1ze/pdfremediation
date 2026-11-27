# Recorded reader responses

Real responses recorded by live benchmark runs (`--ir mistral --mode live`), replayed by tests with no network. Each `<provider>/<name>/page-NNNN.json` is the response body exactly as received, and `page-NNNN.meta.json` holds the request ID, tries, model, endpoint and time.

Only public documents are recorded here (ADR 0004): never customer content. Recordings hold no API key, because the key is sent in a header and only the body is saved.

- `mistral/headings-and-paragraphs/`: `../headings-and-paragraphs.pdf`, mistral-ocr-4-1, recorded 2026-11-27.
