import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Boton, Cargando, Estado, Tabla, Titulo } from "../componentes/ui";
import { api, conConsulta } from "../lib/api";
import { entero, ZONAS } from "../lib/formato";
import { esJefe, useSesion } from "../lib/sesion";
import type { Ubicacion, Zona } from "../lib/tipos";

const FILTROS: Zona[] = ["Picking", "Reserva", "Recepcion", "Expedicion", "Cuarentena"];

export function Ubicaciones() {
  const sesion = useSesion();
  const [zona, setZona] = useState<Zona>("Picking");
  const { data, isPending } = useQuery({
    queryKey: ["ubicaciones", zona],
    queryFn: () => api.get<Ubicacion[]>(conConsulta("/api/ubicaciones", { zona })),
  });
  const lista = data ?? [];
  const ocupados = lista.filter((u) => u.unidades > 0).length;

  return (
    <div className="flex flex-col gap-4">
      <Titulo
        accion={
          esJefe(sesion) && (
            <a className="inline-flex min-h-9 items-center rounded-md border border-borde bg-superficie px-3 text-sm" href={conConsulta("/api/ubicaciones/etiquetas.pdf", { zona })} target="_blank" rel="noreferrer">
              Etiquetas para imprimir (PDF)
            </a>
          )
        }
      >
        Ubicaciones
      </Titulo>
      <div className="flex flex-wrap gap-1" role="group" aria-label="Zona">
        {FILTROS.map((z) => (
          <Boton key={z} variante={z === zona ? "principal" : "normal"} aria-pressed={z === zona} onClick={() => setZona(z)}>
            {ZONAS[z]}
          </Boton>
        ))}
      </div>
      {isPending ? (
        <Cargando />
      ) : (
        <>
          <p className="text-sm text-suave">
            {lista.length} ubicaciones, {ocupados} con mercancía. En el orden del recorrido del operario.
          </p>
          <Tabla titulo={`Ubicaciones de ${ZONAS[zona]}`}>
            <thead>
              <tr>
                <th>Código</th>
                {zona === "Picking" && <th>Artículo fijo</th>}
                {zona === "Picking" && <th className="!text-right">Mín. / máx.</th>}
                <th className="!text-right">Unidades</th>
                <th>
                  <span className="sr-only">Estado</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {lista.map((u) => (
                <tr key={u.id}>
                  <td className="codigo">{u.codigo}</td>
                  {zona === "Picking" && <td>{u.articuloFijo ?? <span className="text-suave">libre</span>}</td>}
                  {zona === "Picking" && (
                    <td className="text-right">
                      {u.minimo ?? "—"} / {u.maximo ?? "—"}
                    </td>
                  )}
                  <td className={`text-right ${u.minimo !== null && u.unidades < u.minimo ? "text-aviso" : ""}`}>{entero(u.unidades)}</td>
                  <td>{u.bloqueada && <Estado tono="aviso">contándose</Estado>}</td>
                </tr>
              ))}
            </tbody>
          </Tabla>
        </>
      )}
    </div>
  );
}
