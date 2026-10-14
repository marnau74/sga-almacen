import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { api, conConsulta } from "../lib/api";
import { entero } from "../lib/formato";
import type { Articulo } from "../lib/tipos";
import { Boton } from "./ui";

export interface Linea {
  articulo: Articulo;
  cantidad: number;
}

/** Las líneas de un pedido o una recepción: buscar un artículo (por nombre, marca o EAN), cantidad y añadir. */
export function EditorDeLineas({ lineas, onCambiar }: { lineas: Linea[]; onCambiar: (lineas: Linea[]) => void }) {
  const [buscar, setBuscar] = useState("");
  const { data: resultados = [] } = useQuery({
    queryKey: ["articulos", buscar],
    queryFn: () => api.get<Articulo[]>(conConsulta("/api/articulos", { buscar })),
    enabled: buscar.trim().length >= 2,
  });

  function anadir(articulo: Articulo) {
    if (lineas.some((l) => l.articulo.id === articulo.id)) return;
    onCambiar([...lineas, { articulo, cantidad: articulo.unidadesPorCaja }]);
    setBuscar("");
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="relative">
        <label htmlFor="buscar-articulo" className="text-sm font-medium">
          Añadir artículo
        </label>
        <input
          id="buscar-articulo"
          value={buscar}
          onChange={(e) => setBuscar(e.target.value)}
          placeholder="Nombre, marca o EAN"
          autoComplete="off"
          className="mt-1 min-h-10 w-full rounded-md border border-borde bg-superficie px-3"
        />
        {buscar.trim().length >= 2 && resultados.length > 0 && (
          <ul className="absolute z-10 mt-1 max-h-72 w-full overflow-auto rounded-md border border-borde bg-superficie shadow-sm" role="listbox">
            {resultados.slice(0, 12).map((a) => (
              <li key={a.id} role="option" aria-selected={false}>
                <button type="button" className="w-full px-3 py-2 text-left hover:bg-acento-suave" onClick={() => anadir(a)}>
                  {a.nombre}
                  {a.marca && <span className="text-suave"> · {a.marca}</span>}
                  <span className="codigo block text-xs text-suave">
                    {a.ean} · {entero(a.disponible)} disponibles
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
      {lineas.length > 0 && (
        <ul className="flex flex-col divide-y divide-borde rounded-md border border-borde bg-superficie" aria-label="Líneas">
          {lineas.map((l) => (
            <li key={l.articulo.id} className="flex items-center justify-between gap-3 px-3 py-2">
              <span className="min-w-0 truncate">
                {l.articulo.nombre}
                <span className="codigo block text-xs text-suave">
                  {l.articulo.ean} · caja de {l.articulo.unidadesPorCaja}
                </span>
              </span>
              <span className="flex items-center gap-2">
                <input
                  type="number"
                  min={1}
                  aria-label={`Unidades de ${l.articulo.nombre}`}
                  value={l.cantidad}
                  onChange={(e) =>
                    onCambiar(lineas.map((x) => (x.articulo.id === l.articulo.id ? { ...x, cantidad: Number(e.target.value) } : x)))
                  }
                  className="cifras min-h-9 w-24 rounded-md border border-borde bg-superficie px-2 text-right"
                />
                <Boton variante="discreto" onClick={() => onCambiar(lineas.filter((x) => x.articulo.id !== l.articulo.id))}>
                  Quitar
                </Boton>
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
