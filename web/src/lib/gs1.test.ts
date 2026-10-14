import { describe, expect, it } from "vitest";
import { esElArticulo, leer } from "./gs1";

describe("lectura de escaneos GS1", () => {
  it("un EAN suelto es solo el artículo", () => {
    expect(leer("8410297012150")).toEqual({ gtin: "8410297012150", lote: null, caducidad: null, cantidad: null });
  });

  it("entre paréntesis trae artículo, caducidad, lote y cantidad", () => {
    expect(leer("(01)08410297012150(17)261231(10)L2410A(37)12")).toEqual({
      gtin: "8410297012150",
      lote: "L2410A",
      caducidad: "2026-12-31",
      cantidad: 12,
    });
  });

  it("seguido, con el separador FNC1 tras los campos variables y el prefijo del lector", () => {
    expect(leer("]C1010841029701215017261200" + "10L2410\u001d" + "3712")).toEqual({
      gtin: "8410297012150",
      lote: "L2410",
      caducidad: "2026-12-31",
      cantidad: 12,
    });
  });

  it("el consumo preferente vale si no hay caducidad", () => {
    expect(leer("(01)08410297012150(15)270315").caducidad).toBe("2027-03-15");
  });

  it("un código de ubicación no es un artículo", () => {
    expect(leer("P-01-04-1").gtin).toBeNull();
    expect(esElArticulo("P-01-04-1", "8410297012150")).toBe(false);
    expect(esElArticulo("(01)08410297012150(10)L1", "8410297012150")).toBe(true);
  });
});
