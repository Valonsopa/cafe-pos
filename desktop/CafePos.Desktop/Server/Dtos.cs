namespace CafePos.Desktop.Server;

public record AddItemsRequest(
    string IdempotencyKey,
    string DeviceId,
    string UserId,
    string Source,          // "TABLE" or "TICKET"
    string? TableLabel,     // e.g. "T1"
    string? TicketLabel,    // e.g. "B12"
    List<AddItemLine> Items
);

public record AddItemLine(
    string Name,
    int UnitPriceCents,
    int Qty,
    int TaxRateBps,
    string? Note
);

public record AddItemsResponse(
    string OrderId,
    long OrderNumber,
    int TotalCents,
    int BalanceCents
);

public record WsEvent(string Type, object Data);
