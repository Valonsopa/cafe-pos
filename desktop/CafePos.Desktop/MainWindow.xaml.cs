using CafePos.Desktop.Data;
using CafePos.Desktop.Server;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using CafePos.Desktop.Printing;


namespace CafePos.Desktop;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<dynamic> _openOrders = new();
    private readonly HttpClient _http = new();
    private CancellationTokenSource? _pollCts;

    private PosServer? _server;

    private string? _selectedOrderId;
    private int _selectedBalanceCents;

    public MainWindow()
    {
        InitializeComponent();

        OpenOrdersList.ItemsSource = _openOrders;

        Loaded += OnLoaded;
        Closing += OnClosing;
        
    }
  private async Task<string> BuildReceiptText(string orderId)
{
    var json = await _http.GetStringAsync($"http://localhost:5055/api/orders/{orderId}");
    using var doc = JsonDocument.Parse(json);

    var order = doc.RootElement.GetProperty("order");

    var orderNumber = order.GetProperty("orderNumber").GetInt64();
    var tableLabel = order.TryGetProperty("tableLabel", out var tl) ? tl.GetString() : null;
    var ticketLabel = order.TryGetProperty("ticketLabel", out var tk) ? tk.GetString() : null;

    var totalCents = order.GetProperty("totalCents").GetInt32();
    var paidCents = order.GetProperty("paidCents").GetInt32();

    var sb = new StringBuilder();
    sb.AppendLine("CAFE POS");
    sb.AppendLine($"Order #{orderNumber}");
    if (!string.IsNullOrWhiteSpace(tableLabel)) sb.AppendLine($"Table: {tableLabel}");
    if (!string.IsNullOrWhiteSpace(ticketLabel)) sb.AppendLine($"Ticket: {ticketLabel}");
    sb.AppendLine("--------------------------------");

    foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
    {
        var name = it.GetProperty("name").GetString() ?? "";
        var qty = it.GetProperty("qty").GetInt32();
        var unit = it.GetProperty("unitPriceCents").GetInt32();
        var lineTotal = unit * qty;

        sb.AppendLine($"{qty} x {name}".PadRight(24) + $"{lineTotal / 100.0:0.00}".PadLeft(8));
    }

    sb.AppendLine("--------------------------------");
    sb.AppendLine(("TOTAL").PadRight(24) + $"{totalCents / 100.0:0.00}".PadLeft(8));
    sb.AppendLine(("PAID").PadRight(24) + $"{paidCents / 100.0:0.00}".PadLeft(8));
    sb.AppendLine();
    sb.AppendLine("Thank you!");

    return sb.ToString();
}



    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureSchema();

        var wsHub = new WsHub();
        _server = new PosServer(port: 5055, wsHub);
        await _server.StartAsync();

        ServerStatus.Text = "Running on http://localhost:5055 (LAN: use your PC IP)";

        _pollCts = new CancellationTokenSource();
        _ = Task.Run(() => PollOpenOrders(_pollCts.Token));
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _pollCts?.Cancel();
        if (_server != null) await _server.StopAsync();
    }

    private void EnsureSchema()
    {
        var schemaPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Schema.sql");
        var sql = File.ReadAllText(schemaPath, Encoding.UTF8);

        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public record OpenOrderRow(
        string id,
        long orderNumber,
        string source,
        string? tableLabel,
        string? ticketLabel,
        int totalCents,
        int balanceCents,
        long openedAt
    );

    private async Task PollOpenOrders(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var json = await _http.GetStringAsync("http://localhost:5055/api/orders/open", ct);
                var orders = JsonSerializer.Deserialize<List<OpenOrderRow>>(json) ?? new();

                await Dispatcher.InvokeAsync(() =>
                {
                    _openOrders.Clear();

                    foreach (var o in orders)
                    {
                        var display = o.source == "TABLE"
                            ? (o.tableLabel ?? "?")
                            : (o.ticketLabel ?? "?");

                        _openOrders.Add(new
                        {
                            id = o.id,
                            o.orderNumber,
                            o.source,
                            display,
                            balanceCents = o.balanceCents,
                            balanceDisplay = $"{o.balanceCents / 100.0:0.00}"
                        });
                    }
                });
            }
            catch
            {
                // ignore temporary errors
            }

            await Task.Delay(1000, ct);
        }
    }

    private async void OpenOrdersList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        dynamic sel = OpenOrdersList.SelectedItem;
        if (sel == null) return;

        _selectedOrderId = (string)sel.id;
        _selectedBalanceCents = (int)sel.balanceCents;

        await LoadCheckout(_selectedOrderId);
    }

    private async Task LoadCheckout(string orderId)
    {
        try
        {
            var json = await _http.GetStringAsync($"http://localhost:5055/api/orders/{orderId}");
            using var doc = JsonDocument.Parse(json);

            var order = doc.RootElement.GetProperty("order");

            var orderNumber = order.GetProperty("orderNumber").GetInt64();
            var status = order.GetProperty("status").GetString();

            var tableLabel = order.TryGetProperty("tableLabel", out var tl) ? tl.GetString() : null;
            var ticketLabel = order.TryGetProperty("ticketLabel", out var tk) ? tk.GetString() : null;

            var totalCents = order.GetProperty("totalCents").GetInt32();
            var paidCents = order.GetProperty("paidCents").GetInt32();
            var balanceCents = order.GetProperty("balanceCents").GetInt32();
            _selectedBalanceCents = balanceCents;

            CheckoutTitle.Text = $"Order #{orderNumber}";
            CheckoutSubtitle.Text = tableLabel != null ? $"Table: {tableLabel}" : $"Ticket: {ticketLabel}";
            TotalText.Text = $"Total: {totalCents / 100.0:0.00}";
            PaidText.Text = $"Paid: {paidCents / 100.0:0.00}";
            BalanceText.Text = $"Balance: {balanceCents / 100.0:0.00}";

            var items = new List<dynamic>();
            foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                var name = it.GetProperty("name").GetString() ?? "";
                var qty = it.GetProperty("qty").GetInt32();
                var unit = it.GetProperty("unitPriceCents").GetInt32();

                var note = it.TryGetProperty("note", out var n)
                    ? (n.ValueKind == JsonValueKind.Null ? "" : (n.GetString() ?? ""))
                    : "";

                items.Add(new
                {
                    name,
                    qty,
                    priceDisplay = $"{(unit * qty) / 100.0:0.00}",
                    note
                });
            }

            ItemsList.ItemsSource = items;

            var isOpen = (status == "OPEN");
            PayCashBtn.IsEnabled = isOpen && balanceCents > 0;
            PayCardBtn.IsEnabled = isOpen && balanceCents > 0;
            CloseBtn.IsEnabled = isOpen && balanceCents == 0;
        }
        catch
        {
            CheckoutTitle.Text = "Failed to load order";
            CheckoutSubtitle.Text = "";
            ItemsList.ItemsSource = null;

            PayCashBtn.IsEnabled = false;
            PayCardBtn.IsEnabled = false;
            CloseBtn.IsEnabled = false;
        }
    }

    private async void PayCashBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOrderId == null) return;
        await PayAndRefresh(_selectedOrderId, "CASH");
    }

    private async void PayCardBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOrderId == null) return;
        await PayAndRefresh(_selectedOrderId, "CARD");
    }

   private async Task PayAndRefresh(string orderId, string method)
{
    if (_selectedBalanceCents <= 0) return;

    // MVP: pay full balance
    var body = $@"{{
  ""Method"": ""{method}"",
  ""AmountCents"": {_selectedBalanceCents},
  ""TipCents"": 0,
  ""UserId"": ""cashier-01"",
  ""Reason"": ""POS {method} payment""
}}";

    await _http.PostAsync(
        $"http://localhost:5055/api/orders/{orderId}/payments",
        new StringContent(body, Encoding.UTF8, "application/json"));

    // Reload checkout to get updated balance
    await LoadCheckout(orderId);

    // If balance is now 0 -> close + print automatically
    if (_selectedBalanceCents == 0)
    {
        await _http.PostAsync(
            $"http://localhost:5055/api/orders/{orderId}/close",
            new StringContent("", Encoding.UTF8, "application/json"));

        // Build receipt text and print silently
        var receipt = await BuildReceiptText(orderId);
        ReceiptPrinter.PrintText(receipt);

        // Reload again (optional)
        await LoadCheckout(orderId);
    }
}


    private async void CloseBtn_Click(object sender, RoutedEventArgs e)
{
    if (_selectedOrderId == null) return;

    await _http.PostAsync(
        $"http://localhost:5055/api/orders/{_selectedOrderId}/close",
        new StringContent("", Encoding.UTF8, "application/json"));

    // Open receipt PDF in default viewer (you can print from there)
    var url = $"http://localhost:5055/api/orders/{_selectedOrderId}/receipt.pdf";
    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    await LoadCheckout(_selectedOrderId);
    }
}   