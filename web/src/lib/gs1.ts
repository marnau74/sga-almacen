/**
 * Lo mínimo de GS1 que necesita el terminal para rellenar formularios: de qué artículo es un escaneo y qué trae ya
 * la etiqueta. La validación de verdad (dígito de control, fechas) la hace la API.
 */
export interface Lectura {
  gtin: string | null;
  lote: string | null;
  caducidad: string | null;
  cantidad: number | null;
}

const SEPARADOR = "\u001d";
const FIJOS: Record<string, number> = { "00": 18, "01": 14, "02": 14, "11": 6, "15": 6, "17": 6 };
const VARIABLES = new Set(["10", "37"]);

function gtin13(valor: string) {
  return valor.length === 14 && valor.startsWith("0") ? valor.slice(1) : valor;
}

/** AAMMDD → AAAA-MM-DD (día 00: el último del mes). */
function fecha(valor: string): string | null {
  if (!/^\d{6}$/.test(valor)) return null;
  const anio = 2000 + Number(valor.slice(0, 2));
  const mes = Number(valor.slice(2, 4));
  let dia = Number(valor.slice(4, 6));
  if (mes < 1 || mes > 12) return null;
  if (dia === 0) dia = new Date(Date.UTC(anio, mes, 0)).getUTCDate();
  return `${anio}-${String(mes).padStart(2, "0")}-${String(dia).padStart(2, "0")}`;
}

export function leer(texto: string): Lectura {
  let limpio = texto.trim();
  if (limpio.startsWith("]") && limpio.length > 3) limpio = limpio.slice(3);
  const lectura: Lectura = { gtin: null, lote: null, caducidad: null, cantidad: null };
  if (/^\d{13,14}$/.test(limpio)) return { ...lectura, gtin: gtin13(limpio) };

  const campos: [string, string][] = [];
  if (limpio.startsWith("(")) {
    for (const m of limpio.matchAll(/\((\d{2})\)([^(]*)/g)) campos.push([m[1]!, m[2]!]);
  } else if (/^0[012]\d/.test(limpio)) {
    let i = 0;
    while (i < limpio.length) {
      if (limpio[i] === SEPARADOR) {
        i++;
        continue;
      }
      const ia = limpio.slice(i, i + 2);
      i += 2;
      let fin: number;
      if (FIJOS[ia]) fin = i + FIJOS[ia];
      else if (VARIABLES.has(ia)) {
        const sep = limpio.indexOf(SEPARADOR, i);
        fin = sep < 0 ? limpio.length : sep;
      } else break;
      campos.push([ia, limpio.slice(i, fin)]);
      i = fin;
    }
  }
  for (const [ia, valor] of campos) {
    if (ia === "01" || ia === "02") lectura.gtin = gtin13(valor);
    if (ia === "10") lectura.lote = valor;
    if (ia === "17") lectura.caducidad = fecha(valor);
    if (ia === "15") lectura.caducidad ??= fecha(valor);
    if (ia === "37" && /^\d+$/.test(valor)) lectura.cantidad = Number(valor);
  }
  return lectura;
}

/** Si lo escaneado es el artículo con este EAN (suelto o dentro de una etiqueta GS1). */
export function esElArticulo(escaneado: string, ean: string) {
  return leer(escaneado).gtin === ean;
}
