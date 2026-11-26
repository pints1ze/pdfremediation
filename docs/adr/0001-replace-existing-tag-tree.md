# Always replace an existing tag tree

Many incoming PDFs are already partly tagged, often incorrectly. We always discard the existing structure tree and rebuild it from the Accessibility IR rather than merging with it, because deciding which existing tags to trust is a hard semantic problem the MVP cannot solve reliably, and a single source of truth (the IR) keeps remediation and review items explainable. The report records that the original had tags, so a customer can tell when correct prior work may have been overwritten.
