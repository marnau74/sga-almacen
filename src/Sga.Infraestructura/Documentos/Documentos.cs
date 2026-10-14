using System.Globalization;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Sga.Aplicacion.Consultas;

using ZXing;
using ZXing.OneD;
using ZXing.Rendering;

namespace Sga.Infraestructura.Documentos;

/// <summary>Los papeles del almacén: albarán, etiqueta de bulto (SSCC) y etiquetas de ubicación.</summary>
public static class Documentos
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly string[] ColumnasDelAlbaran = ["Artículo", "Lote", "Caducidad", "Unidades"];

    static Documentos() => QuestPDF.Settings.License = LicenseType.Community;

    /// <summary>Código de barras Code 128 en SVG. Con <paramref name="gs1"/>, GS1-128 (empieza por FNC1).</summary>
    public static string CodigoDeBarras(string contenido, bool gs1, int alto = 80)
    {
        var escritor = new BarcodeWriterSvg
        {
            Format = BarcodeFormat.CODE_128,
            Options = new Code128EncodingOptions { Height = alto, Margin = 0, PureBarcode = true, GS1Format = gs1 },
        };
        return escritor.Write(contenido).Content;
    }

    public static byte[] Albaran(PedidoDetalle pedido, string almacen) => Document.Create(documento =>
    {
        documento.Page(pagina =>
        {
            pagina.Size(PageSizes.A4);
            pagina.Margin(36);
            pagina.DefaultTextStyle(t => t.FontSize(10));
            pagina.Header().Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.RelativeItem().Text(almacen).SemiBold().FontSize(14);
                    r.ConstantItem(220).AlignRight().Text($"Albarán {pedido.Resumen.Numero}").SemiBold().FontSize(14);
                });
                c.Item().PaddingTop(6).Text($"Cliente: {pedido.Resumen.Cliente}");
                c.Item().Text($"Fecha de servicio: {pedido.Resumen.FechaDeServicio.ToString("d", Es)}");
            });
            pagina.Content().PaddingTop(16).Column(c =>
            {
                c.Item().Table(tabla =>
                {
                    tabla.ColumnsDefinition(d =>
                    {
                        d.RelativeColumn(5);
                        d.RelativeColumn(2);
                        d.RelativeColumn(2);
                        d.RelativeColumn(1);
                    });
                    tabla.Header(h =>
                    {
                        foreach (var titulo in ColumnasDelAlbaran)
                        {
                            h.Cell().BorderBottom(1).PaddingBottom(4).Text(titulo).SemiBold();
                        }
                    });
                    foreach (var p in pedido.Preparados)
                    {
                        tabla.Cell().PaddingVertical(3).Text(p.Articulo);
                        tabla.Cell().PaddingVertical(3).Text(p.Lote.Length == 0 ? "—" : p.Lote);
                        tabla.Cell().PaddingVertical(3).Text(p.Caducidad?.ToString("d", Es) ?? "—");
                        tabla.Cell().PaddingVertical(3).AlignRight().Text(p.Cantidad.ToString("N0", Es));
                    }
                });
                var roturas = pedido.Lineas.Where(l => l.Rotura > 0).ToList();
                if (roturas.Count > 0)
                {
                    c.Item().PaddingTop(16).Text("No servido por falta de stock").SemiBold();
                    foreach (var l in roturas)
                    {
                        c.Item().Text($"{l.Articulo}: {l.Rotura.ToString("N0", Es)} unidades");
                    }
                }
                c.Item().PaddingTop(16).Text($"Bultos: {pedido.Bultos.Count}  ·  SSCC: {string.Join(", ", pedido.Bultos.Select(b => b.Sscc))}")
                    .FontSize(8);
            });
            pagina.Footer().AlignRight().Text(t =>
            {
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
    }).GeneratePdf();

    /// <summary>Etiqueta A6 del bulto: cliente, pedido, contenido y el SSCC en GS1-128.</summary>
    public static byte[] EtiquetaDeBulto(PedidoDetalle pedido, BultoDetalle bulto) => Document.Create(documento =>
    {
        documento.Page(pagina =>
        {
            pagina.Size(PageSizes.A6);
            pagina.Margin(14);
            pagina.DefaultTextStyle(t => t.FontSize(9));
            pagina.Content().Column(c =>
            {
                c.Item().Text(pedido.Resumen.Cliente).SemiBold().FontSize(13);
                c.Item().Text($"Pedido {pedido.Resumen.Numero} · Bulto {bulto.Numero} de {pedido.Bultos.Count}");
                c.Item().PaddingVertical(6).LineHorizontal(0.5f);
                foreach (var linea in bulto.Contenido.Take(8))
                {
                    c.Item().Text($"{linea.Cantidad,4} × {linea.Articulo}{(linea.Lote.Length > 0 ? $" (lote {linea.Lote})" : "")}");
                }
                c.Item().PaddingTop(10).Height(70).Svg(CodigoDeBarras("00" + bulto.Sscc, gs1: true));
                c.Item().AlignCenter().Text($"(00) {bulto.Sscc}").FontSize(10);
            });
        });
    }).GeneratePdf();

    /// <summary>Etiquetas de ubicación para pegar en la estantería: el código grande y su código de barras.</summary>
    public static byte[] EtiquetasDeUbicacion(IEnumerable<string> codigos) => Document.Create(documento =>
    {
        documento.Page(pagina =>
        {
            pagina.Size(PageSizes.A4);
            pagina.Margin(20);
            pagina.Content().Table(tabla =>
            {
                tabla.ColumnsDefinition(d =>
                {
                    d.RelativeColumn();
                    d.RelativeColumn();
                });
                foreach (var codigo in codigos)
                {
                    tabla.Cell().Border(0.5f).Padding(10).Column(c =>
                    {
                        c.Item().AlignCenter().Text(codigo).SemiBold().FontSize(22);
                        c.Item().PaddingTop(4).Height(45).Svg(CodigoDeBarras(codigo, gs1: false, alto: 50));
                    });
                }
            });
        });
    }).GeneratePdf();
}
