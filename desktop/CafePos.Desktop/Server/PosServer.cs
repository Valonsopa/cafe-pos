using CafePos.Desktop.Data;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CafePos.Desktop.Server;

public class PosServer
{
    private WebApplication? _app;
    private readonly WsHub _wsHub;

    public int Port { get; }

    public PosServer(int port, WsHub wsHub)
    {
        Port = port;
        _wsHub = wsHub;
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();

        // Listen on LAN so phones can connect later
        builder.WebHost.UseKestrel().UseUrls($"http://0.0.0.0:{Port}");

        _app = builder.Build();

        _app.UseWebSockets();

        // ---- ROUTES ----
        _app.MapGet("/api/health", () => Results.Ok(new { ok = true }));

        _app.MapGet("/api/orders/open", GetOpenOrdersEndpoint);
        _app.MapPost("/api/orders/add-items", AddItemsEndpoint);

        _app.MapGet("/api/orders/{orderId}", GetOrderEndpoint);
        _app.MapPost("/api/orders/{orderId}/payments", AddPaymentEndpoint);
        _app.MapPost("/api/orders/{orderId}/close", CloseOrderEndpoint);
        _app.MapGet("/api/orders/{orderId}/receipt.pdf", GetReceiptPdfEndpoint);


        _app.Map("/ws", WsEndpoint);

        await _app.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_app != null) await _app.StopAsync();
    }

    // WebSocket endpoint (we’ll use more later)
    private async Task WsEndpoint(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = 400;
            return;
        }

        var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        await _wsHub.AddClient(ws);
        await _wsHub.ListenUntilClosed(ws);
    }

    // ---------------------------
    // STEP 2: LIST OPEN ORDERS
    // ---------------------------
    private static async Task<IResult> GetOpenOrdersEndpoint()
    {
        using var conn = Db.Open();

        var rows = await conn.QueryAsync(
            @"SELECT o.id,
                     o.order_number AS orderNumber,
                     o.source,
                     t.label AS tableLabel,
                     o.ticket_label AS ticketLabel,
                     o.total_cents AS totalCents,
                     o.balance_cents AS balanceCents,
                     o.opened_at AS openedAt
              FROM orders o
              LEFT JOIN tables t ON t.id = o.table_id
              WHERE o.status='OPEN'
              ORDER BY o.opened_at DESC
              LIMIT 200");

        return Results.Ok(rows);
    }

    // ---------------------------
    // STEP 3A: GET ORDER DETAILS
    // ---------------------------
    private static async Task<IResult> GetOrderEndpoint(string orderId)
    {
        using var conn = Db.Open();

        var order = await conn.QuerySingleOrDefaultAsync<dynamic>(
            @"SELECT o.id,
                     o.order_number AS orderNumber,
                     o.status,
                     o.source,
                     t.label AS tableLabel,
                     o.ticket_label AS ticketLabel,
                     o.subtotal_cents AS subtotalCents,
                     o.tax_cents AS taxCents,
                     o.discount_cents AS discountCents,
                     o.total_cents AS totalCents,
                     o.paid_cents AS paidCents,
                     o.balance_cents AS balanceCents
              FROM orders o
              LEFT JOIN tables t ON t.id = o.table_id
              WHERE o.id=@id",
            new { id = orderId });

        if (order == null)
            return Results.NotFound(new { error = "order_not_found" });

        var items = await conn.QueryAsync<dynamic>(
            @"SELECT id,
                     menu_item_name AS name,
                     unit_price_cents AS unitPriceCents,
                     qty,
                     tax_rate_bps AS taxRateBps,
                     note,
                     status,
                     created_at AS createdAt
              FROM order_items
              WHERE order_id=@id
              ORDER BY created_at",
            new { id = orderId });

        var payments = await conn.QueryAsync<dynamic>(
            @"SELECT id,
                     method,
                     amount_cents AS amountCents,
                     tip_cents AS tipCents,
                     status,
                     created_at AS createdAt
              FROM payments
              WHERE order_id=@id
              ORDER BY created_at",
            new { id = orderId });

        return Results.Ok(new { order, items, payments });
    }

    // ---------------------------
    // STEP 3B: ADD PAYMENT
    // ---------------------------
    public record AddPaymentRequest(
        string Method,     // CASH or CARD
        int AmountCents,
        int TipCents,
        string UserId,
        string Reason
    );

    private static async Task<IResult> AddPaymentEndpoint(string orderId, AddPaymentRequest req)
    {
        if (req.AmountCents <= 0)
            return Results.BadRequest(new { error = "amount_must_be_positive" });

        using var conn = Db.Open();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var tx = conn.BeginTransaction();

        var order = await conn.QuerySingleOrDefaultAsync<dynamic>(
            @"SELECT id, status, total_cents AS totalCents, paid_cents AS paidCents
              FROM orders WHERE id=@id LIMIT 1",
            new { id = orderId }, tx);

        if (order == null)
            return Results.NotFound(new { error = "order_not_found" });

        if ((string)order.status != "OPEN")
            return Results.BadRequest(new { error = "order_not_open" });

        var paymentId = Guid.NewGuid().ToString("N");

        await conn.ExecuteAsync(
            @"INSERT INTO payments(
                id, order_id, method, amount_cents, tip_cents, status,
                created_by_user_id, created_at, reason
              ) VALUES(
                @id, @oid, @m, @amt, @tip, 'CAPTURED',
                @uid, @t, @reason
              )",
            new
            {
                id = paymentId,
                oid = orderId,
                m = req.Method,
                amt = req.AmountCents,
                tip = req.TipCents,
                uid = req.UserId,
                t = now,
                reason = req.Reason
            }, tx);

        // update order paid/balance
        var newPaid = (int)order.paidCents + req.AmountCents + req.TipCents;
        var total = (int)order.totalCents;
        var newBalance = Math.Max(0, total - newPaid);

        await conn.ExecuteAsync(
            @"UPDATE orders
              SET paid_cents=@paid, balance_cents=@bal
              WHERE id=@id",
            new { paid = newPaid, bal = newBalance, id = orderId }, tx);

        // audit entry (simple)
        await conn.ExecuteAsync(
            @"INSERT INTO audit_log(id, entity_type, entity_id, action, actor_user_id, created_at, details_json)
              VALUES(@id,'PAYMENT',@eid,'ADD_PAYMENT',@actor,@t,@json)",
            new
            {
                id = Guid.NewGuid().ToString("N"),
                eid = paymentId,
                actor = req.UserId,
                t = now,
                json = JsonSerializer.Serialize(new { orderId, req.Method, req.AmountCents, req.TipCents })
            }, tx);

        tx.Commit();

        return Results.Ok(new { paymentId, paidCents = newPaid, balanceCents = newBalance });
    }

    // ---------------------------
    // STEP 3C: CLOSE ORDER (PAID)
    // ---------------------------
    private static async Task<IResult> CloseOrderEndpoint(string orderId)
    {
        using var conn = Db.Open();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var tx = conn.BeginTransaction();

        var order = await conn.QuerySingleOrDefaultAsync<dynamic>(
            @"SELECT id, status, balance_cents AS balanceCents
              FROM orders WHERE id=@id LIMIT 1",
            new { id = orderId }, tx);

        if (order == null)
            return Results.NotFound(new { error = "order_not_found" });

        if ((string)order.status != "OPEN")
            return Results.BadRequest(new { error = "order_not_open" });

        if ((int)order.balanceCents != 0)
            return Results.BadRequest(new { error = "order_not_paid", balanceCents = (int)order.balanceCents });

        await conn.ExecuteAsync(
            @"UPDATE orders
              SET status='PAID', closed_at=@t
              WHERE id=@id",
            new { t = now, id = orderId }, tx);

        tx.Commit();

        return Results.Ok(new { ok = true });
    }

    // ---------------------------
    // STEP 1/2: ADD ITEMS (CREATE/UPDATE ORDER)
    // ---------------------------
    private async Task<IResult> AddItemsEndpoint(AddItemsRequest req)
    {
        using var conn = Db.Open();

        // Idempotency: return cached response if key exists
        var existing = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT response_json FROM idempotency_keys WHERE key = @k",
            new { k = req.IdempotencyKey });

        if (existing != null)
        {
            var cached = JsonSerializer.Deserialize<AddItemsResponse>(existing)!;
            return Results.Ok(cached);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Find/create table
        string? tableId = null;
        if (req.Source == "TABLE" && !string.IsNullOrWhiteSpace(req.TableLabel))
        {
            tableId = await conn.QuerySingleOrDefaultAsync<string>(
                "SELECT id FROM tables WHERE label = @l",
                new { l = req.TableLabel });

            if (tableId == null)
            {
                tableId = Guid.NewGuid().ToString("N");
                await conn.ExecuteAsync(
                    "INSERT INTO tables(id,label,is_active) VALUES (@id,@label,1)",
                    new { id = tableId, label = req.TableLabel });
            }
        }

        // Find open order for this table/ticket
        var order = await conn.QuerySingleOrDefaultAsync<dynamic>(
            @"SELECT id, order_number
              FROM orders
              WHERE status='OPEN'
                AND (
                  (@src='TABLE' AND table_id=@tableId)
                  OR (@src='TICKET' AND ticket_label=@ticketLabel)
                )
              LIMIT 1",
            new { src = req.Source, tableId, ticketLabel = req.TicketLabel });

        string orderId;
        long orderNumber;

        using var tx = conn.BeginTransaction();

        if (order == null)
        {
            var maxNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(order_number), 0) FROM orders",
                transaction: tx);

            orderNumber = maxNo + 1;
            orderId = Guid.NewGuid().ToString("N");

            await conn.ExecuteAsync(
                @"INSERT INTO orders(
                    id, order_number, status, source, table_id, ticket_label,
                    opened_by_user_id, opened_at,
                    subtotal_cents, tax_cents, discount_cents, total_cents, paid_cents, balance_cents
                  ) VALUES (
                    @id, @no, 'OPEN', @src, @tableId, @ticketLabel,
                    @userId, @openedAt,
                    0,0,0,0,0,0
                  )",
                new
                {
                    id = orderId,
                    no = orderNumber,
                    src = req.Source,
                    tableId,
                    ticketLabel = req.TicketLabel,
                    userId = req.UserId,
                    openedAt = now
                },
                tx);
        }
        else
        {
            orderId = (string)order.id;
            orderNumber = (long)order.order_number;
        }

        // Insert items
        foreach (var line in req.Items)
        {
            await conn.ExecuteAsync(
                @"INSERT INTO order_items(
                    id, order_id, menu_item_name, unit_price_cents, qty, tax_rate_bps, note,
                    status, created_by_user_id, created_at
                  ) VALUES (
                    @id, @orderId, @name, @price, @qty, @tax, @note,
                    'ACTIVE', @userId, @createdAt
                  )",
                new
                {
                    id = Guid.NewGuid().ToString("N"),
                    orderId,
                    name = line.Name,
                    price = line.UnitPriceCents,
                    qty = line.Qty,
                    tax = line.TaxRateBps,
                    note = line.Note,
                    userId = req.UserId,
                    createdAt = now
                },
                tx);
        }
        

        // Recalculate totals (TAX INCLUDED): prices already include tax
var total = await conn.ExecuteScalarAsync<long>(
    @"SELECT COALESCE(SUM(unit_price_cents * qty),0)
      FROM order_items
      WHERE order_id=@oid AND status='ACTIVE'",
    new { oid = orderId }, tx);

// Keep these fields for future reports, but they do NOT affect total
var subtotal = total;
var tax = 0L;

        var paid = await conn.ExecuteScalarAsync<long>(
            @"SELECT paid_cents FROM orders WHERE id=@oid",
            new { oid = orderId }, tx);

       var balance = (int)total - (int)paid;


        await conn.ExecuteAsync(
            @"UPDATE orders
              SET subtotal_cents=@sub, tax_cents=@tax, total_cents=@total, balance_cents=@bal
              WHERE id=@oid",
            new { sub = (int)subtotal, tax = (int)tax, total, bal = balance, oid = orderId },
            tx);

        // Store idempotency response
        var resp = new AddItemsResponse(orderId, orderNumber, (int)total, balance);
        var respJson = JsonSerializer.Serialize(resp);

        await conn.ExecuteAsync(
            @"INSERT INTO idempotency_keys(key, device_id, created_at, request_hash, response_json)
              VALUES(@k,@d,@t,@h,@r)",
            new
            {
                k = req.IdempotencyKey,
                d = req.DeviceId,
                t = now,
                h = Sha256(JsonSerializer.Serialize(req)),
                r = respJson
            },
            tx);

        tx.Commit();

        // Notify WS clients (we’ll use later)
        _ = _wsHub.Broadcast(new WsEvent("OrderUpdated", new
        {
            orderId,
            orderNumber,
            source = req.Source,
            tableLabel = req.TableLabel,
            ticketLabel = req.TicketLabel,
            totalCents = (int)total,
            balanceCents = balance
        }));

        return Results.Ok(resp);
    }

    private static string Sha256(string s)
    {
        using var sha = SHA256.Create();
        var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(b);
    }
    private static async Task<IResult> GetReceiptPdfEndpoint(string orderId)
{
    using var conn = Db.Open();

    var order = await conn.QuerySingleOrDefaultAsync<dynamic>(
        @"SELECT o.order_number AS orderNumber,
                 o.status,
                 o.total_cents AS totalCents,
                 o.paid_cents AS paidCents,
                 o.balance_cents AS balanceCents,
                 t.label AS tableLabel
          FROM orders o
          LEFT JOIN tables t ON t.id = o.table_id
          WHERE o.id=@id",
        new { id = orderId });

    if (order == null) return Results.NotFound();

    var items = (await conn.QueryAsync<dynamic>(
        @"SELECT menu_item_name AS name, unit_price_cents AS unit, qty
          FROM order_items
          WHERE order_id=@id AND status='ACTIVE'
          ORDER BY created_at",
        new { id = orderId })).ToList();

    // Generate PDF bytes
    var pdfBytes = ReceiptPdf.Build(
        orderNumber: (long)order.orderNumber,
        tableLabel: (string?)order.tableLabel,
        items: items.Select(x => ((string)x.name, (int)x.unit, (int)x.qty)).ToList(),
        totalCents: (int)order.totalCents,
        paidCents: (int)order.paidCents,
        balanceCents: (int)order.balanceCents
    );

    return Results.File(pdfBytes, "application/pdf", $"receipt-{order.orderNumber}.pdf");
}

}
