using floQ.Domain.Billing;

namespace floQ.Tests.Billing;

/// <summary>Kanonisches Nummern-Format YYYY{Sep}CC{Sep}NNNN — Single Source
/// of Truth für alle vergebenen Belegnummern (Zug UND Vorschau).</summary>
public class DocumentNumberConfigTests
{
    [Fact]
    public void Format_Standard_JahrTypCodeSequenz()
    {
        var config = new DocumentNumberConfig { Year = 2026, TypeCode = 11 };
        Assert.Equal("2026-11-0042", config.Format(42));
    }

    [Fact]
    public void Format_OhneTrenner()
    {
        var config = new DocumentNumberConfig { Year = 2026, TypeCode = 10, Separator = "" };
        Assert.Equal("2026100001", config.Format(1));
    }

    [Fact]
    public void Format_EigenesPadding()
    {
        var config = new DocumentNumberConfig { Year = 2026, TypeCode = 14, SequencePadding = 6 };
        Assert.Equal("2026-14-000007", config.Format(7));
    }

    [Fact]
    public void Format_UngueltigesPadding_FaelltAufVierZurueck()
    {
        var config = new DocumentNumberConfig { Year = 2026, TypeCode = 12, SequencePadding = 0 };
        Assert.Equal("2026-12-0001", config.Format(1));
    }

    [Fact]
    public void Format_SequenzLaengerAlsPadding_WirdNichtGekappt()
    {
        var config = new DocumentNumberConfig { Year = 2026, TypeCode = 11, SequencePadding = 2 };
        Assert.Equal("2026-11-123", config.Format(123));
    }

    [Theory]
    [InlineData(DocumentType.Quote, 10)]
    [InlineData(DocumentType.Invoice, 11)]
    [InlineData(DocumentType.CreditNote, 12)]
    [InlineData(DocumentType.CancellationInvoice, 13)]
    [InlineData(DocumentType.PaymentReminder, 14)]
    public void DefaultTypeCode_JeBelegtyp(DocumentType type, int expected)
    {
        Assert.Equal(expected, DocumentNumberConfig.DefaultTypeCode(type));
    }
}

/// <summary>Geld-Rundung: kaufmännisch, 2 Stellen, weg von Null.</summary>
public class MoneyTests
{
    [Theory]
    [InlineData("1.005", "1.01")]
    [InlineData("1.004", "1.00")]
    [InlineData("-1.005", "-1.01")]
    [InlineData("2.675", "2.68")]
    public void Round_KaufmaennischWegVonNull(string input, string expected)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal(decimal.Parse(expected, invariant), Money.Round(decimal.Parse(input, invariant)));
    }
}
