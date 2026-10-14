using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sga.Infraestructura.Persistencia;

/// <summary>Para <c>dotnet ef migrations add</c>: no se conecta a ninguna base.</summary>
internal sealed class FabricaDeDiseno : IDesignTimeDbContextFactory<SgaContexto>
{
    public SgaContexto CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<SgaContexto>()
        .UseNpgsql("Host=localhost;Database=sga", npgsql => npgsql.MigrationsHistoryTable("historial_migraciones", "almacen"))
        .UseSnakeCaseNamingConvention()
        .Options);
}
