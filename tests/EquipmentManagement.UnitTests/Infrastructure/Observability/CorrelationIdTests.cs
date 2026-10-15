using EquipmentManagement.Infrastructure.Observability;

namespace EquipmentManagement.UnitTests.Infrastructure.Observability;

/// <summary>The X-Correlation-ID acceptance policy (ADR-076). Invariants only — generated GUIDs are never asserted exactly.</summary>
public sealed class CorrelationIdTests
{
    public static TheoryData<string> AcceptedValues => new()
    {
        "a",
        "0f8fad5b-d9cb-469f-a165-70867728950e",
        "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
        "gateway:req_42.retry-1",
        new string('x', CorrelationId.MaxLength),
    };

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void ValidIdentifiers_ArePreserved(string value)
    {
        Assert.True(CorrelationId.IsValid(value));
        Assert.Equal(value, CorrelationId.Resolve(value));
    }

    public static TheoryData<string?> RejectedValues => new()
    {
        null,
        "",
        " ",
        "\t",
        new string('x', CorrelationId.MaxLength + 1),
        new string('x', 101),
        "line\r\nInjected-Header: 1",
        "line\nbreak",
        "tab\tinside",
        "nul\0byte",
        "esc\u001b[31m",
        "space inside",
        "comma,joined",
        "semi;colon",
        "quote\"d",
        "<script>",
        "slash/ed",
        "café",
        "full－width",
        "zero​width",
        "rtl‮override",
        "emoji\U0001F600",
    };

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void UnsafeValues_AreReplacedByAFreshServerId(string? value)
    {
        Assert.False(CorrelationId.IsValid(value));

        var resolved = CorrelationId.Resolve(value);

        Assert.NotEqual(value, resolved);
        Assert.True(CorrelationId.IsValid(resolved));
    }

    [Fact]
    public void GeneratedIds_AreValidAndDistinct()
    {
        var first = CorrelationId.Generate();
        var second = CorrelationId.Generate();

        Assert.True(CorrelationId.IsValid(first));
        Assert.True(first.Length <= CorrelationId.MaxLength);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void MaxLength_StaysBelowTheAuditLogColumn()
    {
        // AuditLog.CorrelationId is varchar(100) (ApplicationDbContext); the
        // integration suite checks the live EF model too.
        Assert.InRange(CorrelationId.MaxLength, 36, 100);
    }
}
