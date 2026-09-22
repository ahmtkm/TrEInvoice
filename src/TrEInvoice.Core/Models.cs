namespace TrEInvoice.Core;

public readonly record struct CurrencyAmount
{
    public decimal Value { get; }
    public string Currency { get; }
    public CurrencyAmount(decimal value, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        Value = value;
        Currency = currency.Trim().ToUpperInvariant();
    }
    public static CurrencyAmount Zero(string currency) => new(0m, currency);
}

public sealed record TaxCode(string Code, string Name, string Category);
public sealed record TaxDetail(TaxCode TaxCode, decimal Rate, CurrencyAmount Amount, decimal? WithholdingRate = null, CurrencyAmount? WithholdingAmount = null);
public sealed record Party(string Name, string? TaxIdentifier = null, string? Address = null);
public sealed record InvoiceLine
{
    private IReadOnlyList<TaxDetail> _taxes = Array.Empty<TaxDetail>();
    public string Id { get; init; }
    public string Description { get; init; }
    public decimal Quantity { get; init; }
    public CurrencyAmount UnitPrice { get; init; }
    public CurrencyAmount LineExtensionAmount { get; init; }
    public IReadOnlyList<TaxDetail> Taxes { get => _taxes; init => _taxes = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public CurrencyAmount AllowanceAmount { get; init; }
    public CurrencyAmount ChargeAmount { get; init; }
    public InvoiceLine(string id, string description, decimal quantity, CurrencyAmount unitPrice, CurrencyAmount lineExtensionAmount, IReadOnlyList<TaxDetail> taxes, CurrencyAmount allowanceAmount, CurrencyAmount chargeAmount)
    {
        ArgumentNullException.ThrowIfNull(taxes);
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineExtensionAmount = lineExtensionAmount;
        Taxes = taxes;
        AllowanceAmount = allowanceAmount;
        ChargeAmount = chargeAmount;
    }
}
public sealed record MonetaryTotals(CurrencyAmount LineExtensionAmount, CurrencyAmount TaxExclusiveAmount, CurrencyAmount TaxInclusiveAmount, CurrencyAmount PayableAmount, CurrencyAmount AllowanceTotalAmount, CurrencyAmount ChargeTotalAmount);
public enum DocumentType { Invoice, CreditNote, DebitNote, Unknown }
public enum InvoiceProfile { Basic, Commercial, Export, Unknown }

public sealed record InvoiceDocument
{
    private IReadOnlyList<InvoiceLine> _lines = Array.Empty<InvoiceLine>();
    private IReadOnlyList<TaxDetail> _taxes = Array.Empty<TaxDetail>();
    public string Id { get; init; }
    public Guid? Uuid { get; init; }
    public DateOnly? IssueDate { get; init; }
    public string Currency { get; init; }
    public Party? Supplier { get; init; }
    public Party? Customer { get; init; }
    public IReadOnlyList<InvoiceLine> Lines { get => _lines; init => _lines = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public IReadOnlyList<TaxDetail> Taxes { get => _taxes; init => _taxes = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public MonetaryTotals Totals { get; init; }
    public DocumentType DocumentType { get; init; }
    public InvoiceProfile Profile { get; init; }

    public InvoiceDocument(string id, Guid? uuid, DateOnly? issueDate, string currency, Party? supplier, Party? customer,
        IReadOnlyList<InvoiceLine> lines, IReadOnlyList<TaxDetail> taxes, MonetaryTotals totals,
        DocumentType documentType = DocumentType.Invoice, InvoiceProfile profile = InvoiceProfile.Basic)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(taxes);
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Uuid = uuid;
        IssueDate = issueDate;
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));
        Supplier = supplier;
        Customer = customer;
        Lines = lines;
        Taxes = taxes;
        Totals = totals ?? throw new ArgumentNullException(nameof(totals));
        DocumentType = documentType;
        Profile = profile;
    }

    public static InvoiceDocument Empty(string currency = "TRY") => new(string.Empty, null, null, currency, null, null, [], [],
        new(CurrencyAmount.Zero(currency), CurrencyAmount.Zero(currency), CurrencyAmount.Zero(currency), CurrencyAmount.Zero(currency), CurrencyAmount.Zero(currency), CurrencyAmount.Zero(currency)));
}
