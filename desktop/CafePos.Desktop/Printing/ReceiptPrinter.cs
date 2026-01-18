using System.Drawing;
using System.Drawing.Printing;

namespace CafePos.Desktop.Printing;

public static class ReceiptPrinter
{
    // Prints simple text receipt to the DEFAULT printer (silent).
    public static void PrintText(string text)
    {
        using var doc = new PrintDocument();
        doc.DocumentName = "Cafe POS Receipt";

        doc.PrintPage += (_, e) =>
        {
            if (e == null || e.Graphics == null)
                return;

            using var font = new Font("Consolas", 10);

            var rect = e.MarginBounds;
            e.Graphics.DrawString(text ?? "", font, Brushes.Black, rect);

            e.HasMorePages = false;
        };

        doc.Print(); // silent print
    }
}
