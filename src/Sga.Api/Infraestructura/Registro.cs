namespace Sga.Api.Infraestructura;

/// <summary>Los mensajes del registro, generados en compilación (sin coste si el nivel está desactivado).</summary>
internal static partial class Registro
{
    [LoggerMessage(Level = LogLevel.Information, Message = "{Codigo}: {Detalle}")]
    public static partial void ReglaIncumplida(ILogger registro, string codigo, string detalle);

    [LoggerMessage(Level = LogLevel.Information, Message = "Base de demostración preparada.")]
    public static partial void DemoPreparada(ILogger registro);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo: {Articulos} artículos de Open Food Facts.")]
    public static partial void ArticulosDeDemo(ILogger registro, int articulos);
}
