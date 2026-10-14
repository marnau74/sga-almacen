using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Sga.Api.Infraestructura;
using Sga.Aplicacion.Abstracciones;
using Sga.Aplicacion.Consultas;
using Sga.Aplicacion.Maestros;
using Sga.Dominio.Almacen;
using Sga.Infraestructura.Documentos;

namespace Sga.Api.Endpoints;

internal sealed record NuevoTercero(string Codigo, string Nombre, int DiasMinimosDeVidaUtil = 0);

internal sealed record Bloqueo(string Motivo);

internal sealed record AsignacionDePicking(Guid ArticuloId, int Minimo, int Maximo);

internal sealed class ValidadorDeArticulo : AbstractValidator<NuevoArticulo>
{
    public ValidadorDeArticulo()
    {
        RuleFor(a => a.Ean).NotEmpty().WithMessage("Falta el código EAN.");
        RuleFor(a => a.Nombre).NotEmpty().WithMessage("Falta el nombre.").MaximumLength(200).WithMessage("El nombre tiene 200 caracteres como mucho.");
        RuleFor(a => a.UnidadesPorCaja).InclusiveBetween(1, 10_000).WithMessage("Las unidades por caja van de 1 a 10.000.");
    }
}

internal sealed class ValidadorDeTercero : AbstractValidator<NuevoTercero>
{
    public ValidadorDeTercero()
    {
        RuleFor(t => t.Codigo).NotEmpty().WithMessage("Falta el código.").MaximumLength(20).WithMessage("El código tiene 20 caracteres como mucho.");
        RuleFor(t => t.Nombre).NotEmpty().WithMessage("Falta el nombre.").MaximumLength(200).WithMessage("El nombre tiene 200 caracteres como mucho.");
    }
}

internal sealed class ValidadorDeEstanteria : AbstractValidator<NuevaEstanteria>
{
    public ValidadorDeEstanteria()
    {
        RuleFor(e => e.Zona).Must(z => z is TipoDeZona.Picking or TipoDeZona.Reserva).WithMessage("Las estanterías son de picking o de reserva.");
        RuleFor(e => e.Pasillo).InclusiveBetween(1, 99).WithMessage("El pasillo va de 1 a 99.");
    }
}

internal static class CatalogoEndpoints
{
    public static void MapCatalogo(this IEndpointRouteBuilder app)
    {
        var articulos = app.MapGroup("/api/articulos").WithTags("Catálogo");
        articulos.MapGet("/", (string? buscar, Consultas consultas, CancellationToken ct) =>
            consultas.Articulos(buscar, 100, ct));
        articulos.MapPost("/", async (NuevoArticulo nuevo, ServicioDeMaestros maestros, CancellationToken ct) =>
            {
                var articulo = await maestros.CrearArticulo(nuevo, ct);
                return Results.Created($"/api/articulos/{articulo.Id}", new { articulo.Id });
            })
            .Validado<NuevoArticulo>()
            .RequireAuthorization(Politicas.Jefe);
        articulos.MapGet("/{id:guid}/stock", (Guid id, Consultas consultas, CancellationToken ct) => consultas.StockDe(id, ct));
        articulos.MapGet("/{id:guid}/lotes/{lote}", (Guid id, string lote, Consultas consultas, CancellationToken ct) =>
                consultas.Trazabilidad(id, lote, ct))
            .RequireAuthorization(Politicas.Oficina);
        articulos.MapPut("/{id:guid}/lotes/{lote}/bloqueo", async (Guid id, string lote, Bloqueo bloqueo, ServicioDeMaestros maestros, CancellationToken ct) =>
            {
                await maestros.BloquearLote(id, lote, bloqueo.Motivo, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Politicas.Jefe);
        articulos.MapDelete("/{id:guid}/lotes/{lote}/bloqueo", async (Guid id, string lote, ServicioDeMaestros maestros, CancellationToken ct) =>
            {
                await maestros.DesbloquearLote(id, lote, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Politicas.Jefe);

        var clientes = app.MapGroup("/api/clientes").WithTags("Catálogo").RequireAuthorization(Politicas.Oficina);
        clientes.MapGet("/", async (IContexto contexto, CancellationToken ct) =>
            await contexto.Clientes.AsNoTracking().OrderBy(c => c.Codigo)
                .Select(c => new { c.Id, c.Codigo, c.Nombre, c.DiasMinimosDeVidaUtil }).ToListAsync(ct));
        clientes.MapPost("/", async (NuevoTercero nuevo, ServicioDeMaestros maestros, CancellationToken ct) =>
            {
                var cliente = await maestros.CrearCliente(nuevo.Codigo, nuevo.Nombre, nuevo.DiasMinimosDeVidaUtil, ct);
                return Results.Created($"/api/clientes/{cliente.Id}", new { cliente.Id });
            })
            .Validado<NuevoTercero>();

        var proveedores = app.MapGroup("/api/proveedores").WithTags("Catálogo").RequireAuthorization(Politicas.Oficina);
        proveedores.MapGet("/", async (IContexto contexto, CancellationToken ct) =>
            await contexto.Proveedores.AsNoTracking().OrderBy(p => p.Codigo).Select(p => new { p.Id, p.Codigo, p.Nombre }).ToListAsync(ct));
        proveedores.MapPost("/", async (NuevoTercero nuevo, ServicioDeMaestros maestros, CancellationToken ct) =>
            {
                var proveedor = await maestros.CrearProveedor(nuevo.Codigo, nuevo.Nombre, ct);
                return Results.Created($"/api/proveedores/{proveedor.Id}", new { proveedor.Id });
            })
            .Validado<NuevoTercero>();

        var ubicaciones = app.MapGroup("/api/ubicaciones").WithTags("Almacén");
        ubicaciones.MapGet("/", (TipoDeZona? zona, Consultas consultas, CancellationToken ct) => consultas.Ubicaciones(zona, ct));
        ubicaciones.MapPost("/estanterias", async (NuevaEstanteria nueva, ServicioDeMaestros maestros, CancellationToken ct) =>
            {
                var huecos = await maestros.CrearEstanteria(nueva, ct);
                return Results.Ok(huecos.Select(h => h.Codigo));
            })
            .Validado<NuevaEstanteria>()
            .RequireAuthorization(Politicas.Jefe);
        ubicaciones.MapPut("/{codigo}/picking", async (string codigo, AsignacionDePicking asignacion, ServicioDeMaestros maestros, CancellationToken ct) =>
            {
                await maestros.AsignarPicking(codigo, asignacion.ArticuloId, asignacion.Minimo, asignacion.Maximo, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Politicas.Jefe);
        ubicaciones.MapGet("/etiquetas.pdf", async (TipoDeZona? zona, Consultas consultas, CancellationToken ct) =>
            {
                var lista = await consultas.Ubicaciones(zona, ct);
                return Results.File(Documentos.EtiquetasDeUbicacion(lista.Select(u => u.Codigo)), "application/pdf", "etiquetas.pdf");
            })
            .RequireAuthorization(Politicas.Jefe);
    }
}
