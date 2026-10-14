using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sga.Aplicacion.Abstracciones;
using Sga.Infraestructura.Persistencia;

namespace Sga.Infraestructura;

public static class ServiciosDeInfraestructura
{
    public static IServiceCollection AddInfraestructura(this IServiceCollection servicios, string cadenaDeConexion)
    {
        servicios.AddDbContext<SgaContexto>(o => o
            .UseNpgsql(cadenaDeConexion, npgsql => npgsql.MigrationsHistoryTable("historial_migraciones", "almacen"))
            .UseSnakeCaseNamingConvention());
        servicios.AddScoped<IContexto>(sp => sp.GetRequiredService<SgaContexto>());
        return servicios;
    }
}
