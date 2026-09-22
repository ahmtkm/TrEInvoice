using TrEInvoice.Core;

namespace TrEInvoice.Validation;

public enum ValidationSeverity { Info, Warning, Error }
public sealed record ValidationIssue(string Code, ValidationSeverity Severity, string Path, string Message);
public sealed record ValidationResult
{
    public IReadOnlyList<ValidationIssue> Issues { get; }
    public ValidationResult(IReadOnlyList<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = Array.AsReadOnly(issues.ToArray());
    }
    public bool IsValid => Issues.All(x => x.Severity != ValidationSeverity.Error);
}
public sealed record ReconciliationResult
{
    public bool IsReconciled { get; }
    public IReadOnlyList<ValidationIssue> Issues { get; }
    public ReconciliationResult(bool isReconciled, IReadOnlyList<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        IsReconciled = isReconciled;
        Issues = Array.AsReadOnly(issues.ToArray());
    }
}

public sealed class InvoiceValidator
{
    public ReconciliationResult Reconcile(InvoiceDocument invoice, decimal tolerance = 0.01m)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var issues = new List<ValidationIssue>();
        void Add(string code, string path, decimal expected, decimal actual, string label)
        {
            if (Math.Abs(expected - actual) > tolerance)
                issues.Add(new(code, ValidationSeverity.Error, path, $"{label} differs: expected {expected}, received {actual}."));
        }
        var lineTotal = invoice.Lines.Sum(x => x.LineExtensionAmount.Value);
        var taxTotal = invoice.Taxes.Sum(x => x.Amount.Value);
        var withholding = invoice.Taxes.Sum(x => x.WithholdingAmount?.Value ?? 0m);
        Add("REC001", "Totals.LineExtensionAmount", lineTotal, invoice.Totals.LineExtensionAmount.Value, "Line total");
        Add("REC002", "Totals.TaxInclusiveAmount", invoice.Totals.TaxExclusiveAmount.Value + taxTotal, invoice.Totals.TaxInclusiveAmount.Value, "Tax inclusive total");
        Add("REC003", "Totals.PayableAmount", invoice.Totals.TaxInclusiveAmount.Value - withholding, invoice.Totals.PayableAmount.Value, "Payable total");
        return new ReconciliationResult(issues.Count == 0, Array.AsReadOnly(issues.ToArray()));
    }

    public ValidationResult Validate(InvoiceDocument invoice, decimal tolerance = 0.01m)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var issues = new List<ValidationIssue>();
        void Error(string code, string path, string message) => issues.Add(new(code, ValidationSeverity.Error, path, message));

        if (string.IsNullOrWhiteSpace(invoice.Id)) Error("INV001", "Id", "Invoice ID is required.");
        if (invoice.Uuid is null || invoice.Uuid == Guid.Empty) Error("INV002", "Uuid", "Invoice UUID is required.");
        if (invoice.IssueDate is null) Error("INV003", "IssueDate", "Issue date is required.");
        if (invoice.Supplier is null || string.IsNullOrWhiteSpace(invoice.Supplier.Name)) Error("INV004", "Supplier", "Supplier name is required.");
        if (invoice.Customer is null || string.IsNullOrWhiteSpace(invoice.Customer.Name)) Error("INV005", "Customer", "Customer name is required.");
        if (invoice.Currency.Length != 3 || invoice.Currency.Any(c => !char.IsAsciiLetter(c))) Error("INV006", "Currency", "Currency must be a three-letter code.");
        if (invoice.Lines.Count == 0) Error("INV007", "Lines", "At least one invoice line is required.");
        for (var i = 0; i < invoice.Lines.Count; i++)
        {
            var line = invoice.Lines[i];
            var path = $"Lines[{i}]";
            if (line.Quantity < 0 || line.UnitPrice.Value < 0 || line.LineExtensionAmount.Value < 0 || line.AllowanceAmount.Value < 0 || line.ChargeAmount.Value < 0)
                Error("INV008", path, "Line quantity and monetary values cannot be negative.");
            if (Math.Abs(line.Quantity * line.UnitPrice.Value - line.LineExtensionAmount.Value) > tolerance)
                Error("INV009", $"{path}.LineExtensionAmount", "Line total is inconsistent with quantity and unit price.");
        }
        for (var i = 0; i < invoice.Taxes.Count; i++)
        {
            var tax = invoice.Taxes[i];
            if (tax.Rate < 0 || tax.WithholdingRate is < 0 || tax.Amount.Value < 0 || (tax.WithholdingAmount?.Value ?? 0m) < 0)
                Error("INV014", $"Taxes[{i}]", "Tax rates and tax amounts cannot be negative.");
        }
        if (invoice.Totals.PayableAmount.Value < 0 || invoice.Totals.TaxExclusiveAmount.Value < 0 || invoice.Totals.TaxInclusiveAmount.Value < 0)
            Error("INV010", "Totals", "Invoice monetary totals cannot be negative.");
        var lineTotal = invoice.Lines.Sum(x => x.LineExtensionAmount.Value);
        var taxTotal = invoice.Taxes.Sum(x => x.Amount.Value);
        var withholdingTotal = invoice.Taxes.Sum(x => x.WithholdingAmount?.Value ?? 0m);
        if (Math.Abs(invoice.Totals.LineExtensionAmount.Value - lineTotal) > tolerance)
            Error("INV011", "Totals.LineExtensionAmount", "Line total does not match invoice lines.");
        if (Math.Abs(invoice.Totals.TaxInclusiveAmount.Value - invoice.Totals.TaxExclusiveAmount.Value - taxTotal) > tolerance)
            Error("INV012", "Totals.TaxInclusiveAmount", "Tax total is inconsistent with inclusive and exclusive totals.");
        if (Math.Abs(invoice.Totals.PayableAmount.Value - (invoice.Totals.TaxInclusiveAmount.Value - withholdingTotal)) > tolerance)
            Error("INV013", "Totals.PayableAmount", "Payable total is inconsistent with withholding.");

        return new ValidationResult(Array.AsReadOnly(issues.ToArray()));
    }
}
