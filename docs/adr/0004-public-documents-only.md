# Accept only public-release documents, processed by third-party services

The MVP sends page content to third-party services: the document-understanding reader if it is Mistral OCR 4.1, and Claude Haiku 5.5 for heading levels, lists and alt text. A pipeline that keeps documents private would need a self-hosted reader and a self-hosted vision model, which is more infrastructure than a solo developer should take on first. So the MVP accepts only Documents that the Customer attests, at upload, are intended for public release (the Public-release attestation). The service never publishes Documents; the attestation covers what third-party processing may expose them to. The customer is responsible for the attestation, and the service does not try to detect non-public content.

## Consequences

- Government customers with confidential or regulated documents are out of scope until a private pipeline exists. Building one is phase 2, most likely with the self-hosted PaddleOCR-VL-1.6 reader that T-005 already benchmarks, plus a self-hosted vision model for alt text.
- The processor terms of Mistral and Anthropic (retention, training, region, and zero data retention) still have to be confirmed before the pilot (OQ-05).
