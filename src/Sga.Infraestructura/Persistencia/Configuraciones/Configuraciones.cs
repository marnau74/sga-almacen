using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sga.Dominio.Almacen;
using Sga.Dominio.Catalogo;
using Sga.Dominio.Codigos;
using Sga.Dominio.Entradas;
using Sga.Dominio.Inventario;
using Sga.Dominio.Salidas;
using Sga.Dominio.Stock;

namespace Sga.Infraestructura.Persistencia.Configuraciones;

internal static class ExtensionesDeConfiguracion
{
    /// <summary>Concurrencia optimista con la columna de sistema <c>xmin</c> de PostgreSQL.</summary>
    public static void ConVersion<T>(this EntityTypeBuilder<T> b) where T : class =>
        b.Property<uint>("Version").IsRowVersion();
}

internal sealed class ArticuloConfiguracion : IEntityTypeConfiguration<Articulo>
{
    public void Configure(EntityTypeBuilder<Articulo> b)
    {
        b.ToTable("articulo", "catalogo");
        b.Property(a => a.Ean).HasConversion(g => g.Valor, v => Gtin.Crear(v)).HasMaxLength(13);
        b.HasIndex(a => a.Ean).IsUnique();
        b.Property(a => a.Nombre).HasMaxLength(200);
        b.Property(a => a.Marca).HasMaxLength(200);
        b.Property(a => a.Formato).HasMaxLength(100);
    }
}

internal sealed class ProveedorConfiguracion : IEntityTypeConfiguration<Proveedor>
{
    public void Configure(EntityTypeBuilder<Proveedor> b)
    {
        b.ToTable("proveedor", "catalogo");
        b.HasIndex(p => p.Codigo).IsUnique();
        b.Property(p => p.Codigo).HasMaxLength(20);
        b.Property(p => p.Nombre).HasMaxLength(200);
    }
}

internal sealed class ClienteConfiguracion : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> b)
    {
        b.ToTable("cliente", "catalogo");
        b.HasIndex(c => c.Codigo).IsUnique();
        b.Property(c => c.Codigo).HasMaxLength(20);
        b.Property(c => c.Nombre).HasMaxLength(200);
    }
}

internal sealed class UbicacionConfiguracion : IEntityTypeConfiguration<Ubicacion>
{
    public void Configure(EntityTypeBuilder<Ubicacion> b)
    {
        b.ToTable("ubicacion", "almacen", t => t.HasCheckConstraint("ck_ubicacion_minimo_maximo",
            "minimo IS NULL OR (minimo >= 0 AND maximo > minimo)"));
        b.HasIndex(u => u.Codigo).IsUnique();
        b.HasIndex(u => u.ArticuloFijoId);
        b.Property(u => u.Codigo).HasMaxLength(20);
        b.Property(u => u.Zona).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Articulo>().WithMany().HasForeignKey(u => u.ArticuloFijoId).OnDelete(DeleteBehavior.Restrict);
        b.ConVersion();
    }
}

internal sealed class ExistenciaConfiguracion : IEntityTypeConfiguration<Existencia>
{
    public void Configure(EntityTypeBuilder<Existencia> b)
    {
        // Las reglas del stock también en la base: si un error de programación se las saltara, la transacción falla.
        b.ToTable("existencia", "stock", t =>
        {
            t.HasCheckConstraint("ck_existencia_cantidad", "cantidad >= 0");
            t.HasCheckConstraint("ck_existencia_reservada", "reservada >= 0 AND reservada <= cantidad");
        });
        b.HasKey(e => new { e.ArticuloId, e.UbicacionId, e.Lote });
        b.Ignore(e => e.Clave);
        b.Ignore(e => e.Disponible);
        b.Ignore(e => e.EstaVacia);
        b.Property(e => e.Lote).HasMaxLength(20);
        b.HasIndex(e => e.UbicacionId);
        b.HasOne<Articulo>().WithMany().HasForeignKey(e => e.ArticuloId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Ubicacion>().WithMany().HasForeignKey(e => e.UbicacionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MovimientoConfiguracion : IEntityTypeConfiguration<Movimiento>
{
    public void Configure(EntityTypeBuilder<Movimiento> b)
    {
        b.ToTable("movimiento", "stock", t =>
        {
            t.HasCheckConstraint("ck_movimiento_cantidad", "cantidad > 0");
            t.HasCheckConstraint("ck_movimiento_sitio", "origen_id IS NOT NULL OR destino_id IS NOT NULL");
        });
        b.Property(m => m.Id).UseIdentityAlwaysColumn();
        b.Property(m => m.Tipo).HasConversion<string>().HasMaxLength(20);
        b.Property(m => m.Lote).HasMaxLength(20);
        b.Property(m => m.Usuario).HasMaxLength(100);
        b.Property(m => m.Documento).HasMaxLength(30);
        b.HasIndex(m => new { m.ArticuloId, m.Lote });
        b.HasIndex(m => m.OrigenId);
        b.HasIndex(m => m.DestinoId);
        b.HasIndex(m => m.Momento);
        b.HasIndex(m => m.Documento);
        b.HasOne<Articulo>().WithMany().HasForeignKey(m => m.ArticuloId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Ubicacion>().WithMany().HasForeignKey(m => m.OrigenId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Ubicacion>().WithMany().HasForeignKey(m => m.DestinoId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReservaConfiguracion : IEntityTypeConfiguration<Reserva>
{
    public void Configure(EntityTypeBuilder<Reserva> b)
    {
        b.ToTable("reserva", "stock", t => t.HasCheckConstraint("ck_reserva_cantidad",
            "cantidad > 0 AND consumida >= 0 AND consumida <= cantidad"));
        b.Ignore(r => r.Clave);
        b.Ignore(r => r.Pendiente);
        b.Property(r => r.Lote).HasMaxLength(20);
        b.Property(r => r.Estado).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(r => r.PedidoId);
        b.HasIndex(r => new { r.ArticuloId, r.UbicacionId, r.Lote }).HasFilter("estado = 'Abierta'");
    }
}

internal sealed class LoteBloqueadoConfiguracion : IEntityTypeConfiguration<LoteBloqueado>
{
    public void Configure(EntityTypeBuilder<LoteBloqueado> b)
    {
        b.ToTable("lote_bloqueado", "stock");
        b.HasKey(l => new { l.ArticuloId, l.Lote });
        b.Property(l => l.Lote).HasMaxLength(20);
        b.Property(l => l.Motivo).HasMaxLength(500);
    }
}

internal sealed class TareaConfiguracion : IEntityTypeConfiguration<Tarea>
{
    public void Configure(EntityTypeBuilder<Tarea> b)
    {
        b.ToTable("tarea", "almacen");
        b.Ignore(t => t.Abierta);
        b.Property(t => t.Tipo).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.Estado).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.Lote).HasMaxLength(20);
        b.Property(t => t.Documento).HasMaxLength(30);
        b.Property(t => t.Operario).HasMaxLength(100);
        b.HasIndex(t => new { t.Estado, t.Tipo, t.Orden });
        b.HasIndex(t => t.PedidoId);
        b.HasIndex(t => t.OleadaId);
        // Dos operarios que cogen la misma tarea a la vez: el segundo recibe un conflicto.
        b.ConVersion();
    }
}

internal sealed class RecepcionConfiguracion : IEntityTypeConfiguration<Recepcion>
{
    public void Configure(EntityTypeBuilder<Recepcion> b)
    {
        b.ToTable("recepcion", "entradas");
        b.HasIndex(r => r.Numero).IsUnique();
        b.Property(r => r.Numero).HasMaxLength(20);
        b.Property(r => r.Estado).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Observaciones).HasMaxLength(1000);
        b.Ignore(r => r.TieneDiferencias);
        b.HasOne<Proveedor>().WithMany().HasForeignKey(r => r.ProveedorId).OnDelete(DeleteBehavior.Restrict);
        b.OwnsMany(r => r.Lineas, l =>
        {
            l.ToTable("linea_de_recepcion", "entradas");
            l.WithOwner().HasForeignKey("RecepcionId");
            l.HasKey("RecepcionId", nameof(LineaDeRecepcion.Numero));
            l.Property(x => x.Numero).ValueGeneratedNever();
        });
        b.OwnsMany(r => r.Descargas, d =>
        {
            d.ToTable("descarga", "entradas");
            d.WithOwner().HasForeignKey("RecepcionId");
            d.HasKey("RecepcionId", nameof(Descarga.Numero));
            d.Property(x => x.Numero).ValueGeneratedNever();
            d.Property(x => x.Lote).HasMaxLength(20);
            d.Property(x => x.Operario).HasMaxLength(100);
        });
        b.ConVersion();
    }
}

internal sealed class PedidoConfiguracion : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> b)
    {
        b.ToTable("pedido", "salidas");
        b.HasIndex(p => p.Numero).IsUnique();
        b.HasIndex(p => p.Estado);
        b.Property(p => p.Numero).HasMaxLength(20);
        b.Property(p => p.Estado).HasConversion<string>().HasMaxLength(20);
        b.Ignore(p => p.SePuedeCancelar);
        b.HasOne<Cliente>().WithMany().HasForeignKey(p => p.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.OwnsMany(p => p.Lineas, l =>
        {
            l.ToTable("linea_de_pedido", "salidas");
            l.WithOwner().HasForeignKey("PedidoId");
            l.HasKey("PedidoId", nameof(LineaDePedido.Numero));
            l.Property(x => x.Numero).ValueGeneratedNever();
            l.Ignore(x => x.Rotura);
        });
        b.OwnsMany(p => p.Preparados, l =>
        {
            l.ToTable("preparado", "salidas");
            l.WithOwner().HasForeignKey("PedidoId");
            l.Property<int>("Id");
            l.HasKey("PedidoId", "Id");
            l.Property(x => x.Lote).HasMaxLength(20);
        });
        b.OwnsMany(p => p.Bultos, bulto =>
        {
            bulto.ToTable("bulto", "salidas");
            bulto.WithOwner().HasForeignKey("PedidoId");
            bulto.HasKey("PedidoId", nameof(Bulto.Numero));
            bulto.Property(x => x.Numero).ValueGeneratedNever();
            bulto.Property(x => x.Sscc).HasMaxLength(18);
            bulto.HasIndex(x => x.Sscc).IsUnique();
            bulto.OwnsMany(x => x.Contenido, c =>
            {
                c.ToTable("contenido_de_bulto", "salidas");
                c.WithOwner().HasForeignKey("PedidoId", "BultoNumero");
                c.Property<int>("Id");
                c.HasKey("PedidoId", "BultoNumero", "Id");
                c.Property(x => x.Lote).HasMaxLength(20);
            });
        });
        // Dos operarios embalando o un pedido que se cancela mientras se asigna: el segundo recibe un conflicto.
        b.ConVersion();
    }
}

internal sealed class OleadaConfiguracion : IEntityTypeConfiguration<Oleada>
{
    public void Configure(EntityTypeBuilder<Oleada> b)
    {
        b.ToTable("oleada", "salidas");
        b.HasIndex(o => o.Numero).IsUnique();
        b.Property(o => o.Numero).HasMaxLength(20);
        b.Property(o => o.Estado).HasConversion<string>().HasMaxLength(20);
    }
}

internal sealed class ConteoConfiguracion : IEntityTypeConfiguration<Conteo>
{
    public void Configure(EntityTypeBuilder<Conteo> b)
    {
        b.ToTable("conteo", "inventario");
        b.HasIndex(c => c.Numero).IsUnique();
        b.Property(c => c.Numero).HasMaxLength(20);
        b.Property(c => c.Estado).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.AprobadoPor).HasMaxLength(100);
        b.Ignore(c => c.PorRecontar);
        b.Ignore(c => c.Terminado);
        b.PrimitiveCollection<List<Guid>>("_ubicaciones").HasColumnName("ubicaciones");
        b.Ignore(c => c.Ubicaciones);
        b.OwnsMany(c => c.Lineas, l =>
        {
            l.ToTable("linea_de_conteo", "inventario");
            l.WithOwner().HasForeignKey("ConteoId");
            l.Property<int>("Id");
            l.HasKey("ConteoId", "Id");
            l.Property(x => x.Lote).HasMaxLength(20);
            l.Ignore(x => x.Contada);
            l.Ignore(x => x.Diferencia);
        });
        b.ConVersion();
    }
}
