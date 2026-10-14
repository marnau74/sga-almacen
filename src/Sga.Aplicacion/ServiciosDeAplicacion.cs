using Microsoft.Extensions.DependencyInjection;
using Sga.Aplicacion.Entradas;
using Sga.Aplicacion.Inventario;
using Sga.Aplicacion.Maestros;
using Sga.Aplicacion.Salidas;
using Sga.Aplicacion.Stock;
using Sga.Aplicacion.Tareas;

namespace Sga.Aplicacion;

public static class ServiciosDeAplicacion
{
    public static IServiceCollection AddAplicacion(this IServiceCollection servicios)
    {
        servicios.AddScoped<ServicioDeStock>();
        servicios.AddScoped<Huecos>();
        servicios.AddScoped<Reposicion>();
        servicios.AddScoped<Reparto>();
        servicios.AddScoped<ServicioDeMaestros>();
        servicios.AddScoped<ServicioDeEntradas>();
        servicios.AddScoped<ServicioDeSalidas>();
        servicios.AddScoped<ServicioDeTareas>();
        servicios.AddScoped<ServicioDeInventario>();
        servicios.AddScoped<Consultas.Consultas>();
        servicios.AddSingleton(TimeProvider.System);
        return servicios;
    }
}
