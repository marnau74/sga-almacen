import { describe, expect, it } from "vitest";
import { aHoraLocal, dia, diasHasta } from "./formato";

describe("formato", () => {
  it("una fecha sin hora no se mueve de día por la zona horaria", () => {
    expect(dia("2026-12-01")).toBe("01/12/2026");
  });

  it("los días que faltan hasta una fecha", () => {
    expect(diasHasta("2026-10-20", new Date(2026, 9, 14, 23, 30))).toBe(6);
    expect(diasHasta("2026-10-13", new Date(2026, 9, 14))).toBe(-1);
  });

  it("las líneas por hora UTC se pasan a la hora local (Madrid en verano, UTC+2)", () => {
    const utc = Array.from({ length: 24 }, (_, h) => (h === 10 ? 7 : 0));
    const local = aHoraLocal(utc, -120);
    expect(local[12]).toBe(7);
    expect(local.reduce((a, b) => a + b, 0)).toBe(7);
  });
});
