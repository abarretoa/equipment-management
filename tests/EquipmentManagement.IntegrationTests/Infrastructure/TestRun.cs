namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Identity of this integration-test process (ADR-074): one random value shared
/// by its <see cref="TestDatabase"/> and its <see cref="TestStorage"/> root, so
/// both resources of one run carry the same suffix. Contains nothing about the
/// machine or user.
/// </summary>
public static class TestRun
{
    public static string Id { get; } = Guid.NewGuid().ToString("N");
}
