using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using Npgsql;

using Sga.Aplicacion.Abstracciones;
using Sga.Dominio.Almacen;
using Sga.Dominio.Catalogo;
using Sga.Dominio.Entradas;
using Sga.Dominio.Inventario;
using Sga.Dominio.Salidas;
using Sga.Dominio.Stock;
using Sga.Infraestructura.Identidad;

namespace Sga.Infraestructura.Persistencia;

public sealed class SgaContexto(DbContextOptions<SgaContexto> opciones)
    : IdentityDbContext<Usuario, IdentityRole, string>(opciones), IContexto
{
    public DbSet<Articulo> Articulos => Set<Articulo>();
    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Ubicacion> Ubicaciones => Set<Ubicacion>();
    public DbSet<Existencia> Existencias => Set<Existencia>();
    public DbSet<Movimiento> Movimientos => Set<Movimiento>();
    public DbSet<Reserva> Reservas => Set<Reserva>();
    public DbSet<LoteBloqueado> LotesBloqueados => Set<LoteBloqueado>();
    public DbSet<Tarea> Tareas => Set<Tarea>();
    public DbSet<Recepcion> Recepciones => Set<Recepcion>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<Oleada> Oleadas => Set<Oleada>();
    public DbSet<Conteo> Conteos => Set<Conteo>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Lo que no dice su esquema (las tablas de ASP.NET Identity) va a «identidad»; cada módulo tiene el suyo.
        builder.HasDefaultSchema("identidad");
        base.OnModelCreating(builder);
        builder.Entity<Usuario>().ToTable("usuario");
        builder.Entity<IdentityRole>().ToTable("rol");
        builder.Entity<IdentityUserRole<string>>().ToTable("usuario_rol");
        builder.Entity<IdentityUserClaim<string>>().ToTable("usuario_claim");
        builder.Entity<IdentityUserLogin<string>>().ToTable("usuario_login");
        builder.Entity<IdentityUserToken<string>>().ToTable("usuario_token");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("rol_claim");
        builder.ApplyConfigurationsFromAssembly(typeof(SgaContexto).Assembly);
        builder.Entity<Numerador>(b =>
        {
            b.ToTable("numerador", "almacen");
            b.HasKey(n => n.Serie);
        });
    }

    public async Task<IReadOnlyList<Existencia>> BloquearExistencias(IEnumerable<ClaveDeExistencia> claves, CancellationToken ct)
    {
        var lista = claves.Distinct().ToList();
        if (lista.Count == 0)
        {
            return [];
        }
        // El orden fijo (por clave) evita interbloqueos entre dos transacciones que bloquean las mismas filas.
        return await Existencias.FromSqlRaw(
                """
                SELECT * FROM stock.existencia
                WHERE (articulo_id, ubicacion_id, lote) IN (SELECT * FROM unnest(@articulos, @ubicaciones, @lotes))
                ORDER BY articulo_id, ubicacion_id, lote
                FOR UPDATE
                """,
                new NpgsqlParameter("articulos", lista.Select(c => c.ArticuloId).ToArray()),
                new NpgsqlParameter("ubicaciones", lista.Select(c => c.UbicacionId).ToArray()),
                new NpgsqlParameter("lotes", lista.Select(c => c.Lote).ToArray()))
            .ToListAsync(ct);
    }

    public async Task AsegurarExistencia(ClaveDeExistencia clave, DateOnly? caducidad, CancellationToken ct) =>
        await Database.ExecuteSqlRawAsync(
            """
            INSERT INTO stock.existencia (articulo_id, ubicacion_id, lote, caducidad, cantidad, reservada)
            VALUES (@articulo, @ubicacion, @lote, @caducidad, 0, 0)
            ON CONFLICT DO NOTHING
            """,
            [
                new NpgsqlParameter("articulo", clave.ArticuloId),
                new NpgsqlParameter("ubicacion", clave.UbicacionId),
                new NpgsqlParameter("lote", clave.Lote),
                new NpgsqlParameter("caducidad", (object?)caducidad ?? DBNull.Value) { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Date },
            ],
            ct);

    public async Task<IReadOnlyList<Existencia>> BloquearExistenciasDe(IEnumerable<Guid> articulos, CancellationToken ct)
    {
        var lista = articulos.Distinct().ToArray();
        if (lista.Length == 0)
        {
            return [];
        }
        return await Existencias.FromSqlRaw(
                """
                SELECT * FROM stock.existencia
                WHERE articulo_id = ANY(@articulos)
                ORDER BY articulo_id, ubicacion_id, lote
                FOR UPDATE
                """,
                new NpgsqlParameter("articulos", lista))
            .ToListAsync(ct);
    }

    public async Task BloquearPedidos(IEnumerable<Guid> pedidos, CancellationToken ct)
    {
        var lista = pedidos.Distinct().ToArray();
        if (lista.Length > 0)
        {
            await Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM salidas.pedido WHERE id = ANY(@pedidos) ORDER BY id FOR UPDATE",
                [new NpgsqlParameter("pedidos", lista)], ct);
        }
    }

    public async Task BloquearOleada(Guid oleada, CancellationToken ct) =>
        await Database.ExecuteSqlRawAsync("SELECT 1 FROM salidas.oleada WHERE id = @oleada FOR UPDATE",
            [new NpgsqlParameter("oleada", oleada)], ct);

    /// <summary>
    /// Las líneas, los preparados y los bultos de un pedido (y lo que cuelga de la recepción y del conteo) están en sus
    /// propias tablas: cambiarlos no toca la fila del dueño y su versión no se comprobaría. Se marca el dueño como
    /// cambiado para que dos cambios a la vez sobre el mismo documento acaben en un conflicto y no en uno pisando al otro.
    /// </summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        foreach (var dueno in DuenosDeLoCambiado())
        {
            dueno.State = EntityState.Modified;
        }
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry> DuenosDeLoCambiado()
    {
        var duenos = new List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry>();
        var pendientes = new Queue<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry>(ChangeTracker.Entries()
            .Where(e => e.Metadata.IsOwned() && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted));
        while (pendientes.TryDequeue(out var poseido))
        {
            var propiedad = poseido.Metadata.FindOwnership()!;
            var clave = propiedad.Properties.Select(p => poseido.Property(p.Name).CurrentValue).ToArray();
            var dueno = ChangeTracker.Entries().FirstOrDefault(e => e.Metadata == propiedad.PrincipalEntityType &&
                propiedad.PrincipalKey.Properties.Select(p => e.Property(p.Name).CurrentValue).SequenceEqual(clave));
            if (dueno is null || dueno.State != EntityState.Unchanged)
            {
                continue;
            }
            if (dueno.Metadata.IsOwned())
            {
                pendientes.Enqueue(dueno);
            }
            else if (dueno.Metadata.FindProperty("Version") is not null)
            {
                duenos.Add(dueno);
            }
        }
        return duenos;
    }

    public IQueryable<Articulo> ArticulosQueContienen(string texto)
    {
        // Lo que escribe el usuario es texto, no un patrón: % y _ se escapan.
        var patron = "%" + texto.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal) + "%";
        return Articulos.Where(a => EF.Functions.ILike(a.Nombre, patron) || (a.Marca != null && EF.Functions.ILike(a.Marca, patron)));
    }

    public async Task<string> Siguiente(string serie, CancellationToken ct)
    {
        var numero = await SiguienteDe(serie, ct);
        return $"{serie}-{numero:000000}";
    }

    public Task<long> SiguienteSscc(CancellationToken ct) => SiguienteDe("SSCC", ct);

    private async Task<long> SiguienteDe(string serie, CancellationToken ct) =>
        (await Database.SqlQueryRaw<long>(
                """
                INSERT INTO almacen.numerador (serie, ultimo) VALUES (@serie, 1)
                ON CONFLICT (serie) DO UPDATE SET ultimo = numerador.ultimo + 1
                RETURNING ultimo AS "Value"
                """,
                new NpgsqlParameter("serie", serie))
            .ToListAsync(ct))
        .Single();

    public async Task<T> EnTransaccion<T>(Func<Task<T>> trabajo, CancellationToken ct)
    {
        if (Database.CurrentTransaction is not null)
        {
            var dentro = await trabajo();
            await SaveChangesAsync(ct);
            return dentro;
        }
        await using var transaccion = await Database.BeginTransactionAsync(ct);
        var resultado = await trabajo();
        await SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return resultado;
    }
}

/// <summary>Último número usado de cada serie de documentos.</summary>
internal sealed class Numerador
{
    public string Serie { get; set; } = "";
    public long Ultimo { get; set; }
}
