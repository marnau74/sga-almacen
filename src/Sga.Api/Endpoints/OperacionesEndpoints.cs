using System.Security.Claims;

using FluentValidation;

using Sga.Api.Infraestructura;
using Sga.Aplicacion.Consultas;
using Sga.Aplicacion.Entradas;
using Sga.Aplicacion.Inventario;
using Sga.Aplicacion.Salidas;
using Sga.Aplicacion.Tareas;
using Sga.Dominio.Almacen;
using Sga.Dominio.Entradas;
using Sga.Dominio.Salidas;
using Sga.Infraestructura.Documentos;
using Sga.Infraestructura.Identidad;

namespace Sga.Api.Endpoints;

internal sealed record NuevaRecepcion(Guid ProveedorId, DateOnly FechaPrevista, IReadOnlyList<LineaPrevista> Lineas);

internal sealed record NuevoPedido(Guid ClienteId, DateOnly FechaDeServicio, IReadOnlyList<LineaPedida> Lineas);

internal sealed record SeleccionDePedidos(IReadOnlyList<Guid> Pedidos);

internal sealed record Cierre(string? Observaciones);

internal sealed record Escaneado(string Codigo, string? Lote, int? Cantidad);

internal sealed record NuevoConteo(IReadOnlyList<string> Ubicaciones);

internal sealed class ValidadorDeRecepcion : AbstractValidator<NuevaRecepcion>
{
    public ValidadorDeRecepcion()
    {
        RuleFor(r => r.Lineas).NotEmpty().WithMessage("La recepción necesita al menos una línea.");
        RuleForEach(r => r.Lineas).Must(l => l.Cantidad is > 0 and <= 1_000_000).WithMessage("Cada línea, de 1 a 1.000.000 unidades.");
    }
}

internal sealed class ValidadorDePedido : AbstractValidator<NuevoPedido>
{
    public ValidadorDePedido()
    {
        RuleFor(p => p.Lineas).NotEmpty().WithMessage("El pedido necesita al menos una línea.");
        RuleFor(p => p.Lineas.Count).LessThanOrEqualTo(200).WithMessage("Como mucho 200 líneas por pedido.");
        RuleForEach(p => p.Lineas).Must(l => l.Cantidad is > 0 and <= 100_000).WithMessage("Cada línea, de 1 a 100.000 unidades.");
    }
}

internal sealed class ValidadorDeDescarga : AbstractValidator<Descargado>
{
    public ValidadorDeDescarga()
    {
        RuleFor(d => d.Codigo).NotEmpty().WithMessage("Escanea el artículo.").MaximumLength(100).WithMessage("Ese código es demasiado largo.");
        RuleFor(d => d.Cantidad).InclusiveBetween(1, 1_000_000).When(d => d.Cantidad is not null).WithMessage("La cantidad va de 1 a 1.000.000.");
    }
}

internal sealed class ValidadorDeConfirmacion : AbstractValidator<Confirmacion>
{
    public ValidadorDeConfirmacion()
    {
        RuleFor(c => c.CodigoDeArticulo).MaximumLength(100).WithMessage("Ese código es demasiado largo.");
        RuleFor(c => c.Cantidad).GreaterThanOrEqualTo(0).WithMessage("La cantidad no puede ser negativa.");
    }
}

internal sealed class ValidadorDeConteo : AbstractValidator<NuevoConteo>
{
    public ValidadorDeConteo()
    {
        RuleFor(c => c.Ubicaciones).NotEmpty().WithMessage("Elige al menos una ubicación.");
        RuleFor(c => c.Ubicaciones.Count).LessThanOrEqualTo(100).WithMessage("Como mucho 100 ubicaciones por conteo.");
    }
}

internal static class OperacionesEndpoints
{
    public static void MapOperaciones(this IEndpointRouteBuilder app)
    {
        MapEntradas(app);
        MapSalidas(app);
        MapTareas(app);
        MapInventario(app);
        app.MapGet("/api/panel", (Consultas consultas, CancellationToken ct) => consultas.Panel(ct))
            .WithTags("Panel").RequireAuthorization(Politicas.Oficina);
    }

    private static void MapEntradas(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/recepciones").WithTags("Entradas");
        grupo.MapGet("/", (EstadoDeRecepcion? estado, Consultas consultas, CancellationToken ct) => consultas.Recepciones(estado, ct));
        grupo.MapGet("/{id:guid}", (Guid id, Consultas consultas, CancellationToken ct) => consultas.Recepcion(id, ct));
        grupo.MapPost("/", async (NuevaRecepcion nueva, ServicioDeEntradas entradas, CancellationToken ct) =>
            {
                var recepcion = await entradas.Prever(nueva.ProveedorId, nueva.FechaPrevista, nueva.Lineas, ct);
                return Results.Created($"/api/recepciones/{recepcion.Id}", new { recepcion.Id, recepcion.Numero });
            })
            .Validado<NuevaRecepcion>()
            .RequireAuthorization(Politicas.Oficina);
        grupo.MapPost("/{id:guid}/descargas", async (Guid id, Descargado descargado, ServicioDeEntradas entradas, CancellationToken ct) =>
            {
                var resultado = await entradas.Recibir(id, descargado, ct);
                return Results.Ok(new { resultado.Descarga.Numero, Tareas = resultado.Tareas.Count });
            })
            .Validado<Descargado>()
            .RequireAuthorization(Politicas.Terminal);
        grupo.MapPost("/{id:guid}/cierre", async (Guid id, Cierre cierre, ServicioDeEntradas entradas, CancellationToken ct) =>
            {
                var recepcion = await entradas.Cerrar(id, cierre.Observaciones, ct);
                return Results.Ok(new { recepcion.Estado });
            })
            .RequireAuthorization(Politicas.Terminal);
    }

    private static void MapSalidas(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/pedidos").WithTags("Salidas");
        grupo.MapGet("/", (EstadoDePedido? estado, Consultas consultas, CancellationToken ct) => consultas.Pedidos(estado, ct));
        grupo.MapGet("/{id:guid}", (Guid id, Consultas consultas, CancellationToken ct) => consultas.Pedido(id, ct));
        grupo.MapPost("/", async (NuevoPedido nuevo, ServicioDeSalidas salidas, CancellationToken ct) =>
            {
                var pedido = await salidas.Crear(nuevo.ClienteId, nuevo.FechaDeServicio, nuevo.Lineas, ct);
                return Results.Created($"/api/pedidos/{pedido.Id}", new { pedido.Id, pedido.Numero });
            })
            .Validado<NuevoPedido>()
            .RequireAuthorization(Politicas.Oficina);
        grupo.MapPost("/asignacion", (SeleccionDePedidos seleccion, ServicioDeSalidas salidas, CancellationToken ct) =>
                salidas.Asignar([.. seleccion.Pedidos], ct))
            .RequireAuthorization(Politicas.Jefe);
        grupo.MapPost("/oleadas", async (SeleccionDePedidos seleccion, ServicioDeSalidas salidas, CancellationToken ct) =>
            {
                var oleada = await salidas.LanzarOleada([.. seleccion.Pedidos], ct);
                return Results.Ok(new { oleada.Id, oleada.Numero });
            })
            .RequireAuthorization(Politicas.Jefe);
        grupo.MapPost("/{id:guid}/cancelacion", async (Guid id, ServicioDeSalidas salidas, CancellationToken ct) =>
            {
                await salidas.Cancelar(id, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Politicas.Oficina);

        // Embalaje y expedición (terminal)
        grupo.MapPost("/{id:guid}/bultos", async (Guid id, ServicioDeSalidas salidas, CancellationToken ct) =>
            {
                var bulto = await salidas.AbrirBulto(id, ct);
                return Results.Ok(new { bulto.Numero, bulto.Sscc });
            })
            .RequireAuthorization(Politicas.Terminal);
        grupo.MapPost("/{id:guid}/bultos/abierto/contenido", async (Guid id, Escaneado escaneado, ServicioDeSalidas salidas, CancellationToken ct) =>
            {
                await salidas.Meter(id, escaneado.Codigo, escaneado.Lote, escaneado.Cantidad, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Politicas.Terminal);
        grupo.MapPost("/{id:guid}/bultos/abierto/cierre", async (Guid id, ServicioDeSalidas salidas, CancellationToken ct) =>
            {
                var bulto = await salidas.CerrarBulto(id, ct);
                return Results.Ok(new { bulto.Numero, bulto.Sscc });
            })
            .RequireAuthorization(Politicas.Terminal);
        grupo.MapPost("/{id:guid}/expedicion", async (Guid id, ServicioDeSalidas salidas, CancellationToken ct) =>
            {
                await salidas.Expedir(id, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Politicas.Terminal);

        grupo.MapGet("/{id:guid}/albaran.pdf", async (Guid id, Consultas consultas, IConfiguration configuracion, CancellationToken ct) =>
        {
            var pedido = await consultas.Pedido(id, ct);
            return Results.File(Documentos.Albaran(pedido, configuracion["Almacen:Nombre"] ?? "Almacén"), "application/pdf",
                $"albaran-{pedido.Resumen.Numero}.pdf");
        });
        grupo.MapGet("/{id:guid}/bultos/{numero:int}/etiqueta.pdf", async (Guid id, int numero, Consultas consultas, CancellationToken ct) =>
        {
            var pedido = await consultas.Pedido(id, ct);
            var bulto = pedido.Bultos.SingleOrDefault(b => b.Numero == numero);
            return bulto is null
                ? Results.NotFound()
                : Results.File(Documentos.EtiquetaDeBulto(pedido, bulto), "application/pdf", $"bulto-{bulto.Sscc}.pdf");
        });
    }

    private static void MapTareas(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/tareas").WithTags("Terminal").RequireAuthorization(Politicas.Terminal);
        grupo.MapGet("/", (TipoDeTarea? tipo, ClaimsPrincipal yo, Consultas consultas, CancellationToken ct) =>
            consultas.TareasPara(yo.Identity!.Name!, tipo, ct));
        grupo.MapPost("/{id:guid}/toma", async (Guid id, ServicioDeTareas tareas, CancellationToken ct) =>
        {
            await tareas.Tomar(id, ct);
            return Results.NoContent();
        });
        grupo.MapDelete("/{id:guid}/toma", async (Guid id, ServicioDeTareas tareas, CancellationToken ct) =>
        {
            await tareas.Soltar(id, ct);
            return Results.NoContent();
        });
        grupo.MapPost("/{id:guid}/confirmacion", async (Guid id, Confirmacion confirmacion, ServicioDeTareas tareas, CancellationToken ct) =>
            {
                var tarea = await tareas.Confirmar(id, confirmacion, ct);
                return Results.Ok(new { tarea.Estado, tarea.Hecho });
            })
            .Validado<Confirmacion>();
    }

    private static void MapInventario(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/conteos").WithTags("Inventario");
        grupo.MapGet("/", (Consultas consultas, CancellationToken ct) => consultas.Conteos(ct)).RequireAuthorization(Politicas.Terminal);
        // El conteo es ciego: lo esperado solo lo ven el jefe de almacén y el administrador.
        grupo.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal yo, Consultas consultas, CancellationToken ct) =>
                consultas.Conteo(id, yo.IsInRole(Roles.JefeDeAlmacen) || yo.IsInRole(Roles.Administrador), ct))
            .RequireAuthorization(Politicas.Terminal);
        grupo.MapPost("/", async (NuevoConteo nuevo, ServicioDeInventario inventario, CancellationToken ct) =>
            {
                var conteo = await inventario.Abrir([.. nuevo.Ubicaciones], ct);
                return Results.Created($"/api/conteos/{conteo.Id}", new { conteo.Id, conteo.Numero });
            })
            .Validado<NuevoConteo>()
            .RequireAuthorization(Politicas.Jefe);
        grupo.MapPost("/{id:guid}/cantidades", async (Guid id, Contado contado, ServicioDeInventario inventario, CancellationToken ct) =>
            {
                await inventario.Contar(id, contado, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Politicas.Terminal);
        grupo.MapPost("/{id:guid}/vuelta", async (Guid id, ServicioDeInventario inventario, CancellationToken ct) =>
            {
                var conteo = await inventario.TerminarVuelta(id, ct);
                return Results.Ok(new { conteo.Estado });
            })
            .RequireAuthorization(Politicas.Terminal);
        grupo.MapPost("/{id:guid}/aprobacion", async (Guid id, ServicioDeInventario inventario, CancellationToken ct) =>
            {
                var conteo = await inventario.Aprobar(id, ct);
                return Results.Ok(new { conteo.Estado });
            })
            .RequireAuthorization(Politicas.Jefe);
        grupo.MapPost("/{id:guid}/anulacion", async (Guid id, ServicioDeInventario inventario, CancellationToken ct) =>
            {
                await inventario.Anular(id, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Politicas.Jefe);
    }
}
