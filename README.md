# TrEInvoice

TrEInvoice is an open-source .NET toolkit for parsing, calculating, validating, and reconciling electronic invoice data using UBL concepts. The v0.1.0 packages are provider-independent and target .NET 10.

## What it is

- Provider-neutral invoice models with generic, caller-supplied tax codes.
- Decimal invoice calculations with explicit rounding and reconciliation settings.
- Deterministic structural and monetary validation.
- A size-limited XML reader with DTD processing disabled and a basic UBL invoice reader/writer.

## What it is not

- Not an official GİB library.
- Not an e-invoice provider or service.
- Does not submit or send invoices to GİB or any provider.
- Does not digitally sign invoices or apply a Mali Mühür.
- Does not claim legal, regulatory, or GİB profile compliance.
- Official GİB datasets are not bundled in v0.1.0.

## Features

- Immutable-by-default invoice records and read-only snapshots of invoice collections.
- Generic tax and withholding abstractions; applications supply tax codes and rates.
- Configurable decimal scale, midpoint rounding mode, and reconciliation tolerance.
- Reconciliation issues report mismatches instead of silently changing caller totals.
- Secure XML parsing defaults: DTD prohibited, resolver disabled, bounded input size.
- Synthetic tests with fictional names and identifiers only.

## Architecture

| Package | Responsibility |
| --- | --- |
| `TrEInvoice.Core` | Provider-independent invoice and money models |
| `TrEInvoice.Calculation` | Calculation, rounding, and total reconciliation |
| `TrEInvoice.Validation` | Generic invoice validation and structured issues |
| `TrEInvoice.Ubl` | Secure XML reading and basic UBL invoice serialization |

The three higher-level libraries depend only on `TrEInvoice.Core`. No database, web framework, provider SDK, or external runtime package is required by the libraries.

## Example

```csharp
using TrEInvoice.Core;
using TrEInvoice.Calculation;
using TrEInvoice.Validation;

var currency = "EUR";
var line = new InvoiceLine("1", "Fictional item", 2m,
    new CurrencyAmount(10m, currency), CurrencyAmount.Zero(currency), [],
    CurrencyAmount.Zero(currency), CurrencyAmount.Zero(currency));
var totals = new MonetaryTotals(CurrencyAmount.Zero(currency), CurrencyAmount.Zero(currency),
    CurrencyAmount.Zero(currency), CurrencyAmount.Zero(currency), CurrencyAmount.Zero(currency), CurrencyAmount.Zero(currency));
var invoice = new InvoiceDocument("SYN-INV-001", Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
    new DateOnly(2026, 1, 2), currency, new Party("Fictional Seller"), new Party("Fictional Buyer"),
    [line], [], totals);

var calculated = new InvoiceCalculator().Calculate(invoice, CalculationOptions.Default);
var validation = new InvoiceValidator().Validate(calculated);
```

Tax catalogs and jurisdiction-specific rules are left to applications or separately maintained packages. Review `TotalReconciliationResult.Issues` when reconciling supplied totals.

## Security

`UblInvoiceReader` reads from the stream's current position and leaves the caller-owned stream open. The default maximum XML document size is 5 MiB and can be configured with `SecureXmlReader`. DTDs are prohibited and external resolution is disabled. The writer also leaves its output stream open. Callers should apply application-specific limits and validate parsed invoices before use.

See [SECURITY.md](SECURITY.md) for vulnerability reporting guidance. Do not submit real invoice data, tax identifiers, or credentials in issues.

## UBL scope

The reader and writer cover a deliberately small set of common invoice fields: identifiers, issue date, currency, parties, lines and prices, tax subtotals, line adjustments, monetary totals, and generic tax/withholding values. This is not a complete UBL implementation and does not validate against UBL or UBL-TR schemas. Unsupported elements may not survive a read/write cycle. No XSD files are redistributed.

## Limitations

- .NET 10 is required for v0.1.0.
- No GİB or provider submission, status polling, or archive workflow.
- No signature, certificate, or seal support.
- No official tax-code catalog, provider dataset, schema validation, or compliance assertion.
- UBL mapping is basic; review serialized output for your intended use.
- Calculation behavior is generic and must not be treated as tax or accounting advice.

## Regulatory disclaimer

TrEInvoice is a general-purpose software toolkit. It is not an official GİB library, does not send or digitally sign invoices, and makes no legal, regulatory, or GİB compliance claim. Users are responsible for verifying their requirements and outputs.

## Build and test

```sh
dotnet restore TrEInvoice.sln
dotnet build TrEInvoice.sln --configuration Release
dotnet test TrEInvoice.sln --configuration Release
```

## Roadmap

See [ROADMAP.md](ROADMAP.md). Near-term work includes broader generic UBL mapping, stronger mapping diagnostics, and independently reviewed optional jurisdiction-specific packages.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request. Keep fixtures fictional and preserve provider independence and XML safety controls.

## License

MIT. See [LICENSE](LICENSE) and [docs/PROVENANCE.md](docs/PROVENANCE.md).
