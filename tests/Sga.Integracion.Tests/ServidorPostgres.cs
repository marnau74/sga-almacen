using Microsoft.EntityFrameworkCore;

using Npgsql;

using Sga.Infraestructura.Persistencia;

using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Sga.Integracion.Tests.ServidorPostgres))]

namespace Sga.Integracion.Tests;

/// <summary>
/// Un PostgreSQL real en Docker, compartido por todas las pruebas del ensamblado. Cada prueba tiene su propia base,
/// ya migrada, así que se pueden ejecutar a la vez sin pisarse. No sirve una base en memoria: lo que se prueba aquí
/// (bloqueos por fila, restricciones, concurrencia) solo existe en la de verdad.
/// </summary>
public sealed class ServidorPostgres : IAsyncDisposable
{
    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine")
        .WithCommand("-c", "max_connections=400")
        .Build();
    private readonly SemaphoreSlim _arranque = new(1, 1);
    private bool _iniciado;

    public async Task<string> CrearBaseDeDatos()
    {
        await _arranque.WaitAsync();
        try
        {
            if (!_iniciado)
            {
                await _contenedor.StartAsync();
                _iniciado = true;
            }
        }
        finally
        {
            _arranque.Release();
        }

        var nombre = $"sga_{Guid.NewGuid():N}";
        await using (var conexion = new NpgsqlConnection(_contenedor.GetConnectionString()))
        {
            await conexion.OpenAsync();
            await using var crear = new NpgsqlCommand($"CREATE DATABASE \"{nombre}\"", conexion);
            await crear.ExecuteNonQueryAsync();
        }
        var cadena = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString())
        {
            Database = nombre,
            MaxPoolSize = 30,
        }.ConnectionString;

        await using var contexto = new SgaContexto(new DbContextOptionsBuilder<SgaContexto>()
            .UseNpgsql(cadena, n => n.MigrationsHistoryTable("historial_migraciones", "almacen"))
            .UseSnakeCaseNamingConvention().Options);
        await contexto.Database.MigrateAsync();
        return cadena;
    }

    public async ValueTask DisposeAsync()
    {
        _arranque.Dispose();
        await _contenedor.DisposeAsync();
    }
}
