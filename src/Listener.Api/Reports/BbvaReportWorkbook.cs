using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Listener.Api.History;

namespace Listener.Api.Reports;

public static class BbvaReportWorkbook
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly TimeSpan Peru = TimeSpan.FromHours(-5);

    public static byte[] Create(IReadOnlyList<HistoryItem> items, HistoryFilter filter, DateTimeOffset generated)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = Styles();
            var sheet = workbook.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            var view = new SheetView { WorkbookViewId = 0, ShowGridLines = false, ZoomScale = 85 };
            view.Append(new Pane { VerticalSplit = 6, TopLeftCell = "A7", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen });
            var columns = new Columns();
            double[] widths = [24, 36, 19, 19, 18, 25, 38];
            for (var i = 0; i < widths.Length; i++) columns.Append(new Column { Min = (uint)i + 1, Max = (uint)i + 1, Width = widths[i], CustomWidth = true });
            sheet.Worksheet = new Worksheet(new SheetProperties(new PageSetupProperties { FitToPage = true }),
                new SheetViews(view), new SheetFormatProperties { DefaultRowHeight = 30 }, columns, data);
            data.Append(Row(1, 38, Text("A1", "Tony’s Go · Reporte BBVA Empresas", 1)));
            data.Append(Row(2, 30, Text("A2", $"Desde: {Day(filter.From, "inicio")}   |   Hasta: {Day(filter.To, "sin límite")}   |   Estado: {Status(filter.Status)}", 2)));
            // Only individual recognized payments contribute to this total, never accumulated snapshots.
            var received = items.Where(x => x.Validation is not null || x.Classification == "Abono detectado").Sum(x => x.Amount ?? 0m);
            data.Append(Row(3, 30, Text("A3", "Avisos incluidos", 7), Number("B3", items.Count, 7),
                Text("C3", "Total recibido (S/)", 7), Number("D3", received, 8),
                Text("E3", "Generado en Perú", 2), Text("F3", generated.ToOffset(Peru).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), 2)));
            data.Append(Row(4, 28, Text("A4", "El total suma pagos individuales. Los acumulados son una referencia y no deben sumarse entre sí.", 2)));
            string[] headers = ["Fecha del aviso (Perú)", "Nombre completo", "Monto recibido (S/)", "Acumulado (S/)", "Faltante (S/)", "Validación", "Estado de entrega"];
            data.Append(Row(6, 34, headers.Select((text, index) => Text($"{(char)('A' + index)}6", text, 3)).ToArray()));
            uint row = 7;
            foreach (var item in items)
            {
                var validation = item.Validation;
                data.Append(new Row(
                    new Cell { CellReference = $"A{row}", DataType = CellValues.Number, StyleIndex = 6,
                        CellValue = new CellValue(item.ReceivedAt.ToOffset(Peru).DateTime.ToOADate().ToString("R", CultureInfo.InvariantCulture)) },
                    Text($"B{row}", item.Name ?? "Sin nombre extraído", 4), Number($"C{row}", item.Amount, 5),
                    Number($"D{row}", validation?.MontoAcumulado, 5), Number($"E{row}", validation?.MontoFaltante, 5),
                    Text($"F{row}", validation is null ? item.Classification : validation.Correcto ? "Correcta" : "Insuficiente", 4),
                    Text($"G{row}", item.StatusLabel, 4)) { RowIndex = row });
                row++;
            }
            sheet.Worksheet.Append(new AutoFilter { Reference = $"A6:G{Math.Max(6, row - 1)}" });
            sheet.Worksheet.Append(new MergeCells(new MergeCell { Reference = "A1:G1" }, new MergeCell { Reference = "A2:G2" },
                new MergeCell { Reference = "A4:G4" }, new MergeCell { Reference = "F3:G3" }));
            sheet.Worksheet.Append(new PrintOptions { HorizontalCentered = true },
                new PageMargins { Left = 0.25, Right = 0.25, Top = 0.4, Bottom = 0.4, Header = 0.2, Footer = 0.2 },
                new PageSetup { Orientation = OrientationValues.Landscape, PaperSize = 9, FitToWidth = 1, FitToHeight = 0 });
            workbook.Workbook.Append(new Sheets(new Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1, Name = "Reporte BBVA" }));
            workbook.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static string Day(DateOnly? date, string fallback) => date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? fallback;
    private static string Status(string? status) => status switch
    {
        "correct" => "Validación correcta", "insufficient" => "Validación insuficiente", "pending" => "Pendientes de entrega",
        "sent" => "Entregados a Tony’s Go", "failed" => "Envío con error", "discarded" => "Descartados", "error" => "Error de lectura", _ => "Todos los estados"
    };
    private static Row Row(uint index, double height, params Cell[] cells) => new(cells) { RowIndex = index, Height = height, CustomHeight = true };
    private static Cell Text(string reference, string value, uint style) => new()
    {
        CellReference = reference, DataType = CellValues.InlineString, StyleIndex = style,
        InlineString = new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve })
    };
    private static Cell Number(string reference, decimal? value, uint style) => new()
    {
        CellReference = reference, DataType = CellValues.Number, StyleIndex = style,
        CellValue = value is null ? null : new CellValue(value.Value.ToString(CultureInfo.InvariantCulture))
    };

    private static Stylesheet Styles()
    {
        var formats = new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0.00;[Red](#,##0.00);0.00" },
            new NumberingFormat { NumberFormatId = 165, FormatCode = "dd/mm/yyyy hh:mm:ss" }) { Count = 2 };
        var fonts = new Fonts(
            new Font(new FontSize { Val = 11 }, new Color { Rgb = "FF203247" }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11 }, new Color { Rgb = "FFFFFFFF" }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 20 }, new Color { Rgb = "FFFFFFFF" }, new FontName { Val = "Calibri" }),
            new Font(new FontSize { Val = 10 }, new Color { Rgb = "FF627186" }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11 }, new Color { Rgb = "FF203247" }, new FontName { Val = "Calibri" })) { Count = 5 };
        var fills = new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FF075C91" }, new BackgroundColor { Indexed = 64 }) { PatternType = PatternValues.Solid }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFEDF4FA" }, new BackgroundColor { Indexed = 64 }) { PatternType = PatternValues.Solid })) { Count = 4 };
        CellFormat Format(uint font = 0, uint fill = 0, uint number = 0, bool wrap = false) => new()
        {
            FontId = font, FillId = fill, BorderId = 0, NumberFormatId = number, FormatId = 0,
            ApplyFont = true, ApplyFill = true, ApplyNumberFormat = number != 0, ApplyAlignment = true,
            Alignment = new Alignment { Vertical = VerticalAlignmentValues.Center, WrapText = wrap }
        };
        var cells = new CellFormats(Format(), Format(2, 2), Format(3, wrap: true), Format(1, 2, wrap: true),
            Format(wrap: true), Format(number: 164), Format(number: 165), Format(4, 3, wrap: true), Format(4, 3, 164)) { Count = 9 };
        return new Stylesheet(formats, fonts, fills, new Borders(new Border()) { Count = 1 },
            new CellStyleFormats(new CellFormat()) { Count = 1 }, cells,
            new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
    }
}
