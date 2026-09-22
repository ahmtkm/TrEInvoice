using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using TrEInvoice.Core;

namespace TrEInvoice.Ubl;

/// <summary>Reads a UBL invoice from the current stream position and leaves the caller-owned stream open.</summary>
public interface IUblInvoiceReader { InvoiceDocument Read(Stream stream); }
/// <summary>Writes an unsigned basic UBL invoice and leaves the caller-owned stream open.</summary>
public interface IUblInvoiceWriter { void Write(InvoiceDocument invoice, Stream stream); }

public sealed class SecureXmlReader
{
    public int MaximumDocumentSize { get; }
    public SecureXmlReader(int maximumDocumentSize = 5 * 1024 * 1024)
    {
        if (maximumDocumentSize <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDocumentSize));
        MaximumDocumentSize = maximumDocumentSize;
    }

    /// <summary>Parses XML without owning or closing <paramref name="input"/>.</summary>
    /// <exception cref="ArgumentNullException">The stream is null.</exception>
    /// <exception cref="InvalidDataException">The input exceeds the configured byte limit.</exception>
    /// <exception cref="XmlException">The XML is malformed or contains a prohibited DTD.</exception>
    public XDocument Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("The stream must be readable.", nameof(input));
        if (input.CanSeek && input.Length - input.Position > MaximumDocumentSize)
            throw new InvalidDataException("XML document exceeds the configured maximum size.");
        using var bounded = new BoundedStream(input, MaximumDocumentSize);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumDocumentSize,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            CloseInput = false
        };
        using var reader = XmlReader.Create(bounded, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private sealed class BoundedStream(Stream inner, int max) : Stream
    {
        private int total;
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, Math.Min(count, max - total));
            total += n;
            if (total == max && inner.ReadByte() >= 0) throw new InvalidDataException("XML document exceeds the configured maximum size.");
            return n;
        }
        public override int Read(Span<byte> buffer)
        {
            var n = inner.Read(buffer[..Math.Min(buffer.Length, max - total)]);
            total += n;
            if (total == max && inner.ReadByte() >= 0) throw new InvalidDataException("XML document exceeds the configured maximum size.");
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => total; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public sealed class UblInvoiceReader(SecureXmlReader? secureReader = null) : IUblInvoiceReader
{
    private readonly SecureXmlReader secure = secureReader ?? new();

    public InvoiceDocument Read(Stream stream)
    {
        var document = secure.Read(stream);
        string Value(XElement? scope, string local, string fallback = "") => scope?.Descendants().FirstOrDefault(x => x.Name.LocalName == local)?.Value.Trim() ?? fallback;
        decimal Number(XElement? scope, string local) => decimal.TryParse(Value(scope, local, "0"), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : 0m;
        var currency = Value(document.Root, "DocumentCurrencyCode");
        var supplier = ReadParty(document, "AccountingSupplierParty");
        var customer = ReadParty(document, "AccountingCustomerParty");
        var lines = document.Descendants().Where(x => x.Name.LocalName == "InvoiceLine").Select((line, index) =>
        {
            var adjustments = line.Elements().Where(x => x.Name.LocalName == "AllowanceCharge").ToArray();
            var allowance = adjustments.Where(x => Value(x, "ChargeIndicator").Equals("false", StringComparison.OrdinalIgnoreCase)).Sum(x => Number(x, "Amount"));
            var charge = adjustments.Where(x => Value(x, "ChargeIndicator").Equals("true", StringComparison.OrdinalIgnoreCase)).Sum(x => Number(x, "Amount"));
            var price = Number(line, "PriceAmount");
            return new InvoiceLine(Value(line, "ID", (index + 1).ToString(CultureInfo.InvariantCulture)), Value(line, "Description"), Number(line, "InvoicedQuantity"), new(price, currency), new(Number(line, "LineExtensionAmount"), currency), ReadTaxes(line, currency, Value, Number), new(allowance, currency), new(charge, currency));
        }).ToArray();
        var taxes = ReadTaxes(document.Root, currency, Value, Number);
        var monetary = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "LegalMonetaryTotal");
        var totals = new MonetaryTotals(new(Number(monetary, "LineExtensionAmount"), currency), new(Number(monetary, "TaxExclusiveAmount"), currency), new(Number(monetary, "TaxInclusiveAmount"), currency), new(Number(monetary, "PayableAmount"), currency), new(Number(monetary, "AllowanceTotalAmount"), currency), new(Number(monetary, "ChargeTotalAmount"), currency));
        Guid? uuid = Guid.TryParse(Value(document.Root, "UUID"), out var parsedUuid) ? parsedUuid : null;
        DateOnly? issueDate = DateOnly.TryParseExact(Value(document.Root, "IssueDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate) ? parsedDate : null;
        return new(Value(document.Root, "ID"), uuid, issueDate, currency, supplier, customer, lines, taxes, totals, ParseDocumentType(Value(document.Root, "InvoiceTypeCode")), ParseProfile(Value(document.Root, "ProfileID")));
    }

    private static Party? ReadParty(XDocument document, string aggregateName)
    {
        var aggregate = document.Descendants().FirstOrDefault(x => x.Name.LocalName == aggregateName);
        var party = aggregate?.Descendants().FirstOrDefault(x => x.Name.LocalName == "Party");
        if (party is null) return null;
        var name = party.Descendants().FirstOrDefault(x => x.Name.LocalName == "Name")?.Value.Trim() ?? string.Empty;
        var taxId = party.Descendants().FirstOrDefault(x => x.Name.LocalName == "CompanyID")?.Value.Trim();
        var address = party.Descendants().FirstOrDefault(x => x.Name.LocalName == "StreetName")?.Value.Trim();
        return new Party(name, taxId, address);
    }

    private static TaxDetail[] ReadTaxes(XElement? scope, string currency, Func<XElement?, string, string, string> value, Func<XElement?, string, decimal> number)
    {
        var subtotals = scope?.Descendants().Where(x => x.Name.LocalName == "TaxSubtotal").ToArray() ?? [];
        var regular = subtotals.Where(x => !x.Ancestors().Any(a => a.Name.LocalName == "WithholdingTaxTotal")).Select(x =>
            new TaxDetail(new TaxCode(value(x, "TaxTypeCode", "UNKNOWN"), value(x, "TaxName", "Custom tax"), "Custom"), number(x, "Percent"), new(number(x, "TaxAmount"), currency))).ToList();
        foreach (var subtotal in subtotals.Where(x => x.Ancestors().Any(a => a.Name.LocalName == "WithholdingTaxTotal")))
        {
            var code = value(subtotal, "TaxTypeCode", "UNKNOWN");
            var index = regular.FindIndex(tax => tax.TaxCode.Code == code);
            var detail = index >= 0 ? regular[index] : new TaxDetail(new TaxCode(code, "Custom withholding", "Custom"), 0m, CurrencyAmount.Zero(currency));
            detail = detail with { WithholdingRate = number(subtotal, "Percent"), WithholdingAmount = new(number(subtotal, "TaxAmount"), currency) };
            if (index >= 0) regular[index] = detail; else regular.Add(detail);
        }
        return regular.ToArray();
    }

    private static DocumentType ParseDocumentType(string value) => value.ToUpperInvariant() switch { "381" => DocumentType.CreditNote, "383" => DocumentType.DebitNote, "380" or "" => DocumentType.Invoice, _ => DocumentType.Unknown };
    private static InvoiceProfile ParseProfile(string value) => value.ToUpperInvariant() switch { "TEMELFATURA" or "BASIC" => InvoiceProfile.Basic, "TICARIFATURA" or "COMMERCIAL" => InvoiceProfile.Commercial, "IHRACAT" or "EXPORT" => InvoiceProfile.Export, _ => InvoiceProfile.Unknown };
}

public sealed class UblInvoiceWriter : IUblInvoiceWriter
{
    public void Write(InvoiceDocument invoice, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite) throw new ArgumentException("The stream must be writable.", nameof(stream));
        XNamespace u = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
        XNamespace c = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
        XNamespace a = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
        string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
        var root = new XElement(u + "Invoice",
            new XAttribute(XNamespace.Xmlns + "cbc", c), new XAttribute(XNamespace.Xmlns + "cac", a),
            new XElement(c + "ID", invoice.Id), invoice.Uuid is null ? null : new XElement(c + "UUID", invoice.Uuid.Value),
            invoice.IssueDate is null ? null : new XElement(c + "IssueDate", invoice.IssueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XElement(c + "DocumentCurrencyCode", invoice.Currency),
            new XElement(c + "InvoiceTypeCode", invoice.DocumentType switch { DocumentType.CreditNote => "381", DocumentType.DebitNote => "383", _ => "380" }),
            new XElement(c + "ProfileID", invoice.Profile.ToString()),
            WriteParty(a, c, "AccountingSupplierParty", invoice.Supplier), WriteParty(a, c, "AccountingCustomerParty", invoice.Customer),
            invoice.Lines.Select(line => new XElement(a + "InvoiceLine", new XElement(c + "ID", line.Id), new XElement(c + "Description", line.Description),
                new XElement(c + "InvoicedQuantity", N(line.Quantity)), new XElement(c + "LineExtensionAmount", N(line.LineExtensionAmount.Value)),
                WriteAdjustment(a, c, false, line.AllowanceAmount.Value), WriteAdjustment(a, c, true, line.ChargeAmount.Value),
                new XElement(a + "Price", new XElement(c + "PriceAmount", N(line.UnitPrice.Value))))),
            new XElement(a + "TaxTotal", invoice.Taxes.Select(tax => WriteTaxSubtotal(a, c, tax.TaxCode.Code, invoice.Totals.TaxExclusiveAmount.Value, tax.Amount.Value, tax.Rate))),
            invoice.Taxes.Any(tax => tax.WithholdingRate is not null || tax.WithholdingAmount is not null)
                ? new XElement(a + "WithholdingTaxTotal", invoice.Taxes.Where(tax => tax.WithholdingRate is not null || tax.WithholdingAmount is not null)
                    .Select(tax => WriteTaxSubtotal(a, c, tax.TaxCode.Code, invoice.Totals.TaxExclusiveAmount.Value, tax.WithholdingAmount?.Value ?? 0m, tax.WithholdingRate ?? 0m)))
                : null,
            new XElement(a + "LegalMonetaryTotal", new XElement(c + "LineExtensionAmount", N(invoice.Totals.LineExtensionAmount.Value)),
                new XElement(c + "TaxExclusiveAmount", N(invoice.Totals.TaxExclusiveAmount.Value)), new XElement(c + "TaxInclusiveAmount", N(invoice.Totals.TaxInclusiveAmount.Value)),
                new XElement(c + "AllowanceTotalAmount", N(invoice.Totals.AllowanceTotalAmount.Value)), new XElement(c + "ChargeTotalAmount", N(invoice.Totals.ChargeTotalAmount.Value)),
                new XElement(c + "PayableAmount", N(invoice.Totals.PayableAmount.Value))));
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false), Indent = true, CloseOutput = false });
        root.Save(writer);
    }

    private static XElement WriteParty(XNamespace aggregate, XNamespace basic, string name, Party? party) => new(aggregate + name,
        new XElement(aggregate + "Party", new XElement(aggregate + "PartyName", new XElement(basic + "Name", party?.Name ?? "")),
            party?.TaxIdentifier is null ? null : new XElement(aggregate + "PartyTaxScheme", new XElement(basic + "CompanyID", party.TaxIdentifier)),
            party?.Address is null ? null : new XElement(aggregate + "PostalAddress", new XElement(basic + "StreetName", party.Address))));

    private static XElement WriteTaxSubtotal(XNamespace aggregate, XNamespace basic, string taxCode, decimal taxableAmount, decimal taxAmount, decimal rate) =>
        new XElement(aggregate + "TaxSubtotal", new XElement(basic + "TaxableAmount", taxableAmount.ToString(CultureInfo.InvariantCulture)),
            new XElement(basic + "TaxAmount", taxAmount.ToString(CultureInfo.InvariantCulture)),
            new XElement(aggregate + "TaxCategory", new XElement(basic + "Percent", rate.ToString(CultureInfo.InvariantCulture)),
                new XElement(aggregate + "TaxScheme", new XElement(basic + "TaxTypeCode", taxCode))));

    private static XElement? WriteAdjustment(XNamespace aggregate, XNamespace basic, bool charge, decimal amount) => amount == 0 ? null :
        new XElement(aggregate + "AllowanceCharge", new XElement(basic + "ChargeIndicator", charge ? "true" : "false"), new XElement(basic + "Amount", amount.ToString(CultureInfo.InvariantCulture)));
}
