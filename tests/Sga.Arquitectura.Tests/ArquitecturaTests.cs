using NetArchTest.Rules;

using Sga.Dominio.Comun;

namespace Sga.Arquitectura.Tests;

/// <summary>Los límites entre capas, comprobados: si alguien los cruza sin querer, falla la compilación de la CI.</summary>
public class ArquitecturaTests
{
    private static readonly System.Reflection.Assembly Dominio = typeof(ReglaIncumplida).Assembly;
    private static readonly System.Reflection.Assembly Aplicacion = typeof(Sga.Aplicacion.ServiciosDeAplicacion).Assembly;

    [Fact]
    public void El_dominio_no_depende_de_nada_de_fuera()
    {
        var resultado = Types.InAssembly(Dominio).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "Sga.Aplicacion",
                "Sga.Infraestructura", "Sga.Api")
            .GetResult();
        resultado.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void La_aplicacion_no_conoce_ni_la_base_de_datos_concreta_ni_la_web()
    {
        var resultado = Types.InAssembly(Aplicacion).ShouldNot()
            .HaveDependencyOnAny("Npgsql", "Microsoft.AspNetCore", "Sga.Infraestructura", "Sga.Api")
            .GetResult();
        resultado.FailingTypeNames.ShouldBeNull();
    }

    [Theory]
    [InlineData("Sga.Dominio.Catalogo", new[] { "Sga.Dominio.Entradas", "Sga.Dominio.Salidas", "Sga.Dominio.Inventario" })]
    [InlineData("Sga.Dominio.Stock", new[] { "Sga.Dominio.Entradas", "Sga.Dominio.Salidas", "Sga.Dominio.Inventario" })]
    [InlineData("Sga.Dominio.Entradas", new[] { "Sga.Dominio.Salidas", "Sga.Dominio.Inventario" })]
    [InlineData("Sga.Dominio.Salidas", new[] { "Sga.Dominio.Entradas", "Sga.Dominio.Inventario" })]
    [InlineData("Sga.Dominio.Inventario", new[] { "Sga.Dominio.Entradas", "Sga.Dominio.Salidas" })]
    public void Cada_modulo_del_dominio_solo_usa_los_de_debajo(string modulo, string[] prohibidos)
    {
        var resultado = Types.InAssembly(Dominio).That().ResideInNamespace(modulo).ShouldNot()
            .HaveDependencyOnAny(prohibidos).GetResult();
        resultado.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void Las_entidades_no_tienen_setters_publicos()
    {
        // El estado solo cambia por los métodos del dominio, que comprueban las reglas.
        var conSetterPublico = Dominio.GetTypes()
            .Where(t => t.IsClass && t.IsPublic && t.Namespace?.StartsWith("Sga.Dominio", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly).Where(p => p.SetMethod is { IsPublic: true } && p.SetMethod.ReturnParameter
                .GetRequiredCustomModifiers().All(m => m.Name != "IsExternalInit")).Select(p => $"{t.Name}.{p.Name}"))
            .ToList();
        conSetterPublico.ShouldBeEmpty();
    }
}
