namespace EquipmentManagement.Infrastructure.Handovers;

public sealed record EquipmentDispatchRecord(
    Guid Id,
    Guid EquipmentHandoverId,
    DateOnly ExpectedDeliveryFromDate,
    DateOnly? ExpectedDeliveryToDate,
    DateTime DispatchedAtUtc,
    string DispatchedByDisplayNameSnapshot);

public sealed record RegisterDispatchData(DateOnly ExpectedDeliveryFromDate, DateOnly? ExpectedDeliveryToDate);

/// <summary>A dispatch attempt cancelled by "Anular envio" — kept as history on the handover, newest first.</summary>
public sealed record CancelledDispatchRecord(
    Guid Id,
    DateTime DispatchedAtUtc,
    string DispatchedByDisplayNameSnapshot,
    DateTime CancelledAtUtc,
    string CancelledByDisplayNameSnapshot,
    string CancellationReason);
