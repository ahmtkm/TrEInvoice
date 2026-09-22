# Architecture

The solution is split into four libraries. `TrEInvoice.Core` owns provider-neutral records and currency amounts. `TrEInvoice.Calculation` calculates totals and reports reconciliation differences. `TrEInvoice.Validation` produces ordered validation issues with stable codes and field paths. `TrEInvoice.Ubl` maps a limited set of UBL invoice XML fields to and from the core model.

Calculation and validation reference Core only. Ubl references Core only. No persistence, transport, provider SDK, or jurisdiction-specific catalog is included.

## Data flow

```text
caller-owned XML stream -> SecureXmlReader -> UblInvoiceReader -> InvoiceDocument
InvoiceDocument -> InvoiceCalculator -> calculated InvoiceDocument
InvoiceDocument -> InvoiceValidator / TotalReconciler -> issues and computed totals
InvoiceDocument -> UblInvoiceWriter -> caller-owned XML stream
```

The reader and writer are synchronous and leave the passed stream open. The reader has a configurable byte limit; the default is 5 MiB. XML DTD processing is prohibited and the resolver is disabled. The writer emits basic unsigned XML and does not perform schema validation.

`InvoiceDocument` copies input line and tax collections into read-only snapshots. Nested records are immutable. Tax codes are generic values supplied by callers; this library does not determine jurisdictional meaning or rates.
