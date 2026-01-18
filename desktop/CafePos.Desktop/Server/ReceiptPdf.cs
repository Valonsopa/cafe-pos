using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CafePos.Desktop.Server;

public static class ReceiptPdf
{
    public static byte[] Build(
        long orderNumber,
        string? tableLabel,
        List<(string name, int unitPriceCents, int qty)> items,
        int totalCents,
        int paidCents,
        int balanceCents)
    {
        // QuestPDF requires a license setting (free for most small business use, check their license)
        QuestPDF.Settings.License = LicenseType.Community;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A6);
                page.Margin(10);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(col =>
                {
                    col.Item().Text("Cafe POS").Bold().FontSize(14);
                    col.Item().Text($"Receipt for Order #{orderNumber}");
                    if (!string.IsNullOrWhiteSpace(tableLabel))
                        col.Item().Text($"Table: {tableLabel}");

                    col.Item().LineHorizontal(1);

                    foreach (var it in items)
                    {
                        var lineTotal = (it.unitPriceCents * it.qty) / 100.0;
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text($"{it.qty} x {it.name}");
                            r.ConstantItem(60).AlignRight().Text($"{lineTotal:0.00}");
                        });
                    }

                    col.Item().LineHorizontal(1);

                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("TOTAL").Bold();
                        r.ConstantItem(60).AlignRight().Text($"{totalCents / 100.0:0.00}").Bold();
                    });

                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("PAID");
                        r.ConstantItem(60).AlignRight().Text($"{paidCents / 100.0:0.00}");
                    });

                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("BALANCE");
                        r.ConstantItem(60).AlignRight().Text($"{balanceCents / 100.0:0.00}");
                    });

                    col.Item().PaddingTop(6).Text("Thank you!").Italic();
                });
            });
        });

        return doc.GeneratePdf();
    }
}
