using System.Text;
using TrEInvoice.Core;
using TrEInvoice.Ubl;

namespace TrEInvoice.Ubl.Tests;

public class UblTests
{
    private static InvoiceDocument Invoice(int lineCount = 1) => new("SYN-INV-01", Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), new DateOnly(2026, 1, 2), "EUR",
        new("Fictional Seller", "SYNTHETIC-TAX-SELLER", "1 Example Street"), new("Fictional Buyer", "SYNTHETIC-TAX-BUYER", "2 Sample Street"),
        Enumerable.Range(1, lineCount).Select(i => new InvoiceLine(i.ToString(), $"Synthetic item {i}", i, new(10m, "EUR"), new(i * 10m, "EUR"), [], new(i == 1 ? 1m : 0m, "EUR"), new(i == 1 ? 2m : 0m, "EUR"))).ToArray(),
        [new(new("X-CUSTOM-77", "Fictional custom tax", "Custom"), 20m, new(22.2m, "EUR"), 5m, new(5.55m, "EUR"))],
        new(new(lineCount * 10m, "EUR"), new(lineCount * 10m + 1m, "EUR"), new(lineCount * 10m + 23.2m, "EUR"), new(lineCount * 10m + 17.65m, "EUR"), new(1m, "EUR"), new(2m, "EUR")));

    private static InvoiceDocument RoundTrip(InvoiceDocument invoice)
    {
        using var stream = new MemoryStream();
        new UblInvoiceWriter().Write(invoice, stream);
        Assert.True(stream.CanRead);
        stream.Position = 0;
        var result = new UblInvoiceReader().Read(stream);
        Assert.True(stream.CanRead);
        return result;
    }

    [Fact] public void One_line_invoice_round_trips() { var result = RoundTrip(Invoice()); Assert.Single(result.Lines); Assert.Equal("SYN-INV-01", result.Id); }
    [Fact] public void Multiple_lines_round_trip() => Assert.Equal(3, RoundTrip(Invoice(3)).Lines.Count);
    [Fact] public void Foreign_currency_is_preserved() => Assert.Equal("EUR", RoundTrip(Invoice()).Currency);
    [Fact] public void Unknown_tax_code_is_preserved() => Assert.Equal("X-CUSTOM-77", RoundTrip(Invoice()).Taxes[0].TaxCode.Code);
    [Fact] public void Allowance_and_charge_round_trip() { var line = Assert.Single(RoundTrip(Invoice()).Lines); Assert.Equal(1m, line.AllowanceAmount.Value); Assert.Equal(2m, line.ChargeAmount.Value); }
    [Fact] public void Withholding_round_trips() { var tax = Assert.Single(RoundTrip(Invoice()).Taxes); Assert.Equal(5m, tax.WithholdingRate); Assert.Equal(5.55m, tax.WithholdingAmount?.Value); }
    [Fact] public void Read_write_read_preserves_mapped_values() { var first = RoundTrip(Invoice(2)); var second = RoundTrip(first); Assert.Equal(first.Id, second.Id); Assert.Equal(first.Uuid, second.Uuid); Assert.Equal(first.Lines.Select(x => x.LineExtensionAmount), second.Lines.Select(x => x.LineExtensionAmount)); Assert.Equal(first.Taxes, second.Taxes); Assert.Equal(first.Totals.PayableAmount, second.Totals.PayableAmount); }
    [Fact] public void Malformed_xml_is_rejected() => Assert.ThrowsAny<Exception>(() => new UblInvoiceReader().Read(Bytes("<Invoice>")));
    [Fact] public void Dtd_and_external_entity_payload_is_rejected() => Assert.ThrowsAny<Exception>(() => new UblInvoiceReader().Read(Bytes("<!DOCTYPE x [<!ENTITY x SYSTEM 'file:///not-read'>]><Invoice>&x;</Invoice>")));
    [Fact] public void Oversized_xml_is_rejected() => Assert.Throws<InvalidDataException>(() => new SecureXmlReader(10).Read(Bytes("<Invoice>abcdefghijklmno</Invoice>")));
    [Fact] public void Missing_required_elements_are_not_fabricated() { var invoice = new UblInvoiceReader().Read(Bytes("<Invoice xmlns='urn:test'><ID>SYN-1</ID><DocumentCurrencyCode>EUR</DocumentCurrencyCode></Invoice>")); Assert.Null(invoice.Uuid); Assert.Null(invoice.IssueDate); Assert.Null(invoice.Supplier); Assert.Empty(invoice.Lines); }
    [Fact] public void Non_seekable_stream_is_bounded_and_left_open() { using var input = new NonSeekableStream(Encoding.UTF8.GetBytes("<Invoice>abcdefghijklmno</Invoice>")); Assert.Throws<InvalidDataException>(() => new SecureXmlReader(12).Read(input)); Assert.True(input.CanRead); }
    [Fact] public void Invalid_size_configuration_is_rejected() => Assert.Throws<ArgumentOutOfRangeException>(() => new SecureXmlReader(0));

    private static MemoryStream Bytes(string xml) => new(Encoding.UTF8.GetBytes(xml));
    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes) { public override bool CanSeek => false; public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException(); }
}
