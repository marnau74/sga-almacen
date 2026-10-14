using Microsoft.EntityFrameworkCore;
using Sga.Dominio.Almacen;
using Sga.Dominio.Catalogo;
using Sga.Dominio.Entradas;
using Sga.Dominio.Inventario;
using Sga.Dominio.Salidas;
using Sga.Dominio.Stock;

namespace Sga.Aplicacion.Abstracciones;

/// <summary>
/// Los datos del almacén. Es el <c>DbContext</c> de EF Core (unidad de trabajo y repositorio a la vez) visto desde la
/// aplicación, más lo que EF no sabe hacer solo: bloquear filas y numerar documentos.
/// </summary>
public interface IContexto
{
    DbSet<Articulo> Articulos { get; }
    DbSet<Proveedor> Proveedores { get; }
    DbSet<Cliente> Clientes { get; }
    DbSet<Ubicacion> Ubicaciones { get; }
    DbSet<Existencia> Existencias { get; }
    DbSet<Movimiento> Movimientos { get; }
    DbSet<Reserva> Reservas { get; }
    DbSet<LoteBloqueado> LotesBloqueados { get; }
    DbSet<Tarea> Tareas { get; }
    DbSet<Recepcion> Recepciones { get; }
    DbSet<Pedido> Pedidos { get; }
    DbSet<Oleada> Oleadas { get; }
    DbSet<Conteo> Conteos { get; }

    /// <summary>
    /// Carga y bloquea (<c>SELECT … FOR UPDATE</c>) las existencias de estas claves, siempre en el mismo orden para que
    /// dos operaciones que se cruzan no se bloqueen la una a la otra. Las que no existen no vuelven.
    /// </summary>
    Task<IReadOnlyList<Existencia>> BloquearExistencias(IEnumerable<ClaveDeExistencia> claves, CancellationToken ct);

    /// <summary>
    /// Crea la existencia (vacía) si no está, de forma segura con otras transacciones a la vez: si otra la está creando,
    /// espera a que termine. Así, bloquearla después siempre encuentra la fila.
    /// </summary>
    Task AsegurarExistencia(ClaveDeExistencia clave, DateOnly? caducidad, CancellationToken ct);

    /// <summary>Carga y bloquea todas las existencias de unos artículos (para repartir stock entre pedidos).</summary>
    Task<IReadOnlyList<Existencia>> BloquearExistenciasDe(IEnumerable<Guid> articulos, CancellationToken ct);

    /// <summary>Artículos cuyo nombre o marca contienen el texto, sin distinguir mayúsculas.</summary>
    IQueryable<Articulo> ArticulosQueContienen(string texto);

    /// <summary>El siguiente número de una serie de documentos: «REC» → REC-000013.</summary>
    Task<string> Siguiente(string serie, CancellationToken ct);

    /// <summary>El siguiente número de serie para los SSCC.</summary>
    Task<long> SiguienteSscc(CancellationToken ct);

    /// <summary>
    /// Ejecuta <paramref name="trabajo"/> en una transacción y guarda los cambios al final. Si falla, no queda nada.
    /// </summary>
    Task<T> EnTransaccion<T>(Func<Task<T>> trabajo, CancellationToken ct);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Quién hace la petición.</summary>
public interface IUsuarioActual
{
    string Nombre { get; }
}

/// <summary>Avisos en directo para el panel y los terminales (SignalR en la API).</summary>
public interface IAvisos
{
    Task Publicar(Aviso aviso, CancellationToken ct);
}

public enum TipoDeAviso
{
    TareasCambiadas,
    PedidosCambiados,
    RecepcionesCambiadas,
    StockCambiado,
    ConteosCambiados,
}

public sealed record Aviso(TipoDeAviso Tipo, string? Documento = null);

/// <summary>Ajustes del almacén.</summary>
public sealed class OpcionesDelAlmacen
{
    /// <summary>Prefijo de empresa GS1 para los SSCC. Por defecto, uno de circulación restringida (solo uso interno).</summary>
    public string PrefijoGs1 { get; set; } = "0200000";

    /// <summary>Días antes de la caducidad en que un lote sale en el panel como «caduca pronto».</summary>
    public int DiasDeAvisoDeCaducidad { get; set; } = 15;
}
