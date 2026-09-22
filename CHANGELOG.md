# Changelog

## 0.1.0

Initial open-source release, including:

- Immutable-by-default basic invoice domain models with read-only collection snapshots.
- Generic tax code and tax detail abstractions.
- Invoice line quantity and unit price calculation.
- Allowance and charge calculation.
- Generic withholding calculation abstraction.
- Configurable decimal rounding scale and midpoint mode.
- Configurable reconciliation tolerance with explicit mismatch issues.
- Validation issues with codes, severity, field paths, and messages.
- Secure XML parsing and basic UBL invoice reading and writing.
- DTD prohibition and external entity protection.
- Configurable maximum XML document size protection.
- Synthetic invoice fixtures using fictional data.
- 29 automated tests.
