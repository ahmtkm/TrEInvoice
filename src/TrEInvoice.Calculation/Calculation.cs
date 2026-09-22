using TrEInvoice.Core;

namespace TrEInvoice.Calculation;

public sealed record RoundingPolicy
{
    public int DecimalPlaces { get; }
    public MidpointRounding MidpointRounding { get; }
    public RoundingPolicy(int decimalPlaces = 2, MidpointRounding midpointRounding = MidpointRounding.AwayFromZero)
    {
        if (decimalPlaces is < 0 or > 28) throw new ArgumentOutOfRangeException(nameof(decimalPlaces));
        if (!Enum.IsDefined(midpointRounding)) throw new ArgumentOutOfRangeException(nameof(midpointRounding));
        DecimalPlaces = decimalPlaces;
        MidpointRounding = midpointRounding;
    }
    public decimal Round(decimal value) => Math.Round(value, DecimalPlaces, MidpointRounding);
}

public sealed record CalculationOptions
{
    public RoundingPolicy Rounding { get; }
    public decimal ReconciliationTolerance { get; }
    public CalculationOptions(RoundingPolicy rounding, decimal reconciliationTolerance = 0.01m)
    {
        Rounding = rounding ?? throw new ArgumentNullException(nameof(rounding));
        if (reconciliationTolerance < 0) throw new ArgumentOutOfRangeException(nameof(reconciliationTolerance));
        ReconciliationTolerance = reconciliationTolerance;
    }
    public static CalculationOptions Default => new(new RoundingPolicy());
}

public enum ReconciliationIssueKind { LineTotal, TaxTotal, TaxExclusiveTotal, TaxInclusiveTotal, PayableTotal }
public sealed record ReconciliationIssue(ReconciliationIssueKind Kind, decimal Expected, decimal Actual, decimal Difference);
public sealed record TotalReconciliationResult
{
    public MonetaryTotals Totals { get; }
    public IReadOnlyList<ReconciliationIssue> Issues { get; }
    public bool IsReconciled => Issues.Count == 0;
    public TotalReconciliationResult(MonetaryTotals totals, IReadOnlyList<ReconciliationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(issues);
        Totals = totals;
        Issues = Array.AsReadOnly(issues.ToArray());
    }
}

public sealed class TotalReconciler
{
    public TotalReconciliationResult Reconcile(InvoiceDocument invoice, CalculationOptions options)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(options);
        var p = options.Rounding;
        var currency = invoice.Currency;
        var lineTotal = p.Round(invoice.Lines.Sum(x => x.Quantity * x.UnitPrice.Value));
        var allowance = p.Round(invoice.Lines.Sum(x => x.AllowanceAmount.Value));
        var charge = p.Round(invoice.Lines.Sum(x => x.ChargeAmount.Value));
        var exclusive = p.Round(lineTotal - allowance + charge);
        var taxTotal = p.Round(invoice.Taxes.Sum(x => x.Amount.Value));
        var inclusive = p.Round(exclusive + taxTotal);
        var withholding = p.Round(invoice.Taxes.Sum(x => x.WithholdingAmount?.Value ?? 0m));
        var payable = p.Round(inclusive - withholding);
        var totals = new MonetaryTotals(new(lineTotal,currency), new(exclusive,currency), new(inclusive,currency), new(payable,currency), new(allowance,currency), new(charge,currency));
        var issues = new List<ReconciliationIssue>();
        Add(issues, ReconciliationIssueKind.LineTotal, lineTotal, invoice.Totals.LineExtensionAmount.Value, options.ReconciliationTolerance);
        Add(issues, ReconciliationIssueKind.TaxExclusiveTotal, exclusive, invoice.Totals.TaxExclusiveAmount.Value, options.ReconciliationTolerance);
        Add(issues, ReconciliationIssueKind.TaxInclusiveTotal, inclusive, invoice.Totals.TaxInclusiveAmount.Value, options.ReconciliationTolerance);
        Add(issues, ReconciliationIssueKind.TaxTotal, taxTotal, invoice.Totals.TaxInclusiveAmount.Value - invoice.Totals.TaxExclusiveAmount.Value, options.ReconciliationTolerance);
        Add(issues, ReconciliationIssueKind.PayableTotal, payable, invoice.Totals.PayableAmount.Value, options.ReconciliationTolerance);
        return new(totals, issues.AsReadOnly());
    }
    private static void Add(List<ReconciliationIssue> issues, ReconciliationIssueKind kind, decimal expected, decimal actual, decimal tolerance)
    {
        var diff = expected - actual;
        if (Math.Abs(diff) > tolerance) issues.Add(new(kind, expected, actual, diff));
    }
}

public sealed class InvoiceCalculator
{
    public InvoiceDocument Calculate(InvoiceDocument invoice, CalculationOptions options)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(options);
        var p = options.Rounding;
        var lines = invoice.Lines.Select(line => line with { LineExtensionAmount = new(p.Round(line.Quantity * line.UnitPrice.Value), invoice.Currency) }).ToArray();
        var lineTotal = p.Round(lines.Sum(x => x.LineExtensionAmount.Value));
        var allowance = p.Round(lines.Sum(x => x.AllowanceAmount.Value));
        var charge = p.Round(lines.Sum(x => x.ChargeAmount.Value));
        var exclusive = p.Round(lineTotal - allowance + charge);
        var taxes = invoice.Taxes.Select(t => t with
        {
            Amount = new(p.Round(exclusive * t.Rate / 100m), invoice.Currency),
            WithholdingAmount = t.WithholdingRate is null ? null : new CurrencyAmount(p.Round(exclusive * t.WithholdingRate.Value / 100m), invoice.Currency)
        }).ToArray();
        var taxTotal = p.Round(taxes.Sum(x => x.Amount.Value));
        var inclusive = p.Round(exclusive + taxTotal);
        var withholding = p.Round(taxes.Sum(x => x.WithholdingAmount?.Value ?? 0m));
        return new InvoiceDocument(invoice.Id, invoice.Uuid, invoice.IssueDate, invoice.Currency, invoice.Supplier, invoice.Customer, lines, taxes,
            new(new(lineTotal, invoice.Currency), new(exclusive, invoice.Currency), new(inclusive, invoice.Currency), new(p.Round(inclusive - withholding), invoice.Currency), new(allowance, invoice.Currency), new(charge, invoice.Currency)), invoice.DocumentType, invoice.Profile);
    }
}
