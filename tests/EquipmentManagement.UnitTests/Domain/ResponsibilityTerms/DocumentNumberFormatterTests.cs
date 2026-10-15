using EquipmentManagement.Domain.ResponsibilityTerms;

namespace EquipmentManagement.UnitTests.Domain.ResponsibilityTerms;

public class DocumentNumberFormatterTests
{
    [Fact]
    public void Format_PadsYearAndSequence()
    {
        Assert.Equal("TR-2026-000123", DocumentNumberFormatter.Format(2026, 123));
    }

    [Fact]
    public void Format_DoesNotTruncateASequenceLargerThanSixDigits()
    {
        Assert.Equal("TR-2026-1234567", DocumentNumberFormatter.Format(2026, 1_234_567));
    }

    [Fact]
    public void Format_NeverEncodesConsultantData()
    {
        var documentNumber = DocumentNumberFormatter.Format(2026, 1);

        Assert.Matches("^TR-[0-9]{4}-[0-9]{6,}$", documentNumber);
    }
}
