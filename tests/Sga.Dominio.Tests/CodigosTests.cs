using Sga.Dominio.Codigos;
using Sga.Dominio.Comun;

namespace Sga.Dominio.Tests;

public class CodigosTests
{
    // EAN reales de Open Food Facts.
    [Theory]
    [InlineData("8410297012150")]
    [InlineData("8480000103826")]
    [InlineData("08480000103826")] // GTIN-14 con un 0 delante: el mismo artículo.
    public void Un_ean_con_su_digito_de_control_es_valido(string codigo)
    {
        Gtin.TryCrear(codigo, out var gtin).ShouldBeTrue();
        gtin.Valor.Length.ShouldBe(13);
    }

    [Theory]
    [InlineData("8410297012151")] // dígito de control equivocado
    [InlineData("841029701215")] // le falta una cifra
    [InlineData("84102970121A0")]
    [InlineData("18480000103826")] // GTIN-14 de una caja agrupada: otro artículo
    [InlineData("")]
    public void Un_ean_mal_escrito_no_vale(string codigo) => Gtin.TryCrear(codigo, out _).ShouldBeFalse();

    [Fact]
    public void Crear_un_ean_que_no_vale_lo_dice_con_su_codigo_de_error()
    {
        var error = Should.Throw<ReglaIncumplida>(() => Gtin.Crear("1234567890123"));
        error.Codigo.ShouldBe("gtin-no-valido");
    }

    [Fact]
    public void El_sscc_lleva_extension_prefijo_serie_y_digito_de_control()
    {
        var sscc = Sscc.Generar("0200000", 3, 42);

        sscc.Valor.ShouldBe("302000000000000425");
        sscc.Valor.Length.ShouldBe(18);
        DigitoDeControl.EsValido(sscc.Valor).ShouldBeTrue();
        sscc.ParaEtiqueta().ShouldBe("00302000000000000425");
    }

    [Fact]
    public void El_sscc_no_admite_una_serie_que_no_cabe()
    {
        Should.Throw<ReglaIncumplida>(() => Sscc.Generar("0200000000", 0, 1_000_000)).Codigo.ShouldBe("sscc-agotado");
    }

    // Lecturas de escáner

    [Fact]
    public void Un_ean_suelto_es_solo_el_articulo()
    {
        var lectura = Lectura.Interpretar("8410297012150");
        lectura.Gtin!.Value.Valor.ShouldBe("8410297012150");
        lectura.Lote.ShouldBeNull();
        lectura.EsGs1.ShouldBeTrue();
    }

    [Fact]
    public void Una_etiqueta_gs1_entre_parentesis_trae_articulo_caducidad_lote_y_cantidad()
    {
        var lectura = Lectura.Interpretar("(01)08410297012150(17)261231(10)L2410A(37)12");

        lectura.Gtin!.Value.Valor.ShouldBe("8410297012150");
        lectura.Caducidad.ShouldBe(new DateOnly(2026, 12, 31));
        lectura.Lote.ShouldBe("L2410A");
        lectura.Cantidad.ShouldBe(12);
    }

    [Fact]
    public void Una_etiqueta_gs1_seguida_separa_los_campos_variables_con_fnc1()
    {
        // El lote (10) es variable y acaba en el separador; la cantidad (37) va al final.
        var lectura = Lectura.Interpretar("]C1" + "01" + "08410297012150" + "17" + "261200" + "10" + "L2410" +
                                          Lectura.Separador + "37" + "12");

        lectura.Gtin!.Value.Valor.ShouldBe("8410297012150");
        lectura.Caducidad.ShouldBe(new DateOnly(2026, 12, 31)); // día 00: el último del mes
        lectura.Lote.ShouldBe("L2410");
        lectura.Cantidad.ShouldBe(12);
    }

    [Fact]
    public void Sin_caducidad_vale_el_consumo_preferente()
    {
        Lectura.Interpretar("(01)08410297012150(15)270315").Caducidad.ShouldBe(new DateOnly(2027, 3, 15));
        Lectura.Interpretar("(01)08410297012150(17)270101(15)270315").Caducidad.ShouldBe(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void La_etiqueta_de_un_bulto_es_su_sscc()
    {
        var sscc = Sscc.Generar("0200000", 0, 7);
        Lectura.Interpretar(sscc.ParaEtiqueta()).Sscc.ShouldBe(sscc);
        Lectura.Interpretar(sscc.Valor).Sscc.ShouldBe(sscc);
    }

    [Fact]
    public void El_codigo_de_una_ubicacion_no_es_gs1()
    {
        var lectura = Lectura.Interpretar(" P-01-04-1 ");
        lectura.EsGs1.ShouldBeFalse();
        lectura.Texto.ShouldBe("P-01-04-1");
    }

    [Theory]
    [InlineData("(01)08410297012151")] // dígito de control mal
    [InlineData("(01)08410297012150(17)261332")] // mes 13
    [InlineData("(01)08410297012150(17)260231")] // 31 de febrero
    [InlineData("(01)08410297012150(99)ABC")] // identificador que no se usa aquí
    [InlineData("(01)08410297012150(10)")] // lote vacío
    public void Una_etiqueta_gs1_mal_formada_se_rechaza(string texto)
    {
        Should.Throw<ReglaIncumplida>(() => Lectura.Interpretar(texto)).Codigo.ShouldBeOneOf("etiqueta-no-valida", "gtin-no-valido");
    }
}
