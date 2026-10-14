import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { useState } from "react";
import { Aviso, Boton, Cargando, Estado, Tabla, Titulo, Vacio, type Tono } from "../componentes/ui";
import { api } from "../lib/api";
import { ESTADOS_DE_CONTEO, momento } from "../lib/formato";
import type { ConteoDetalle, ConteoResumen, EstadoDeConteo } from "../lib/tipos";

const TONO: Record<EstadoDeConteo, Tono> = { Abierto: "acento", Recuento: "aviso", PorAprobar: "aviso", Cerrado: "bien", Anulado: "neutro" };

export function Inventario() {
  const clientes = useQueryClient();
  const { data, isPending } = useQuery({ queryKey: ["conteos"], queryFn: () => api.get<ConteoResumen[]>("/api/conteos") });
  const [codigos, setCodigos] = useState("");
  const abrir = useMutation({
    mutationFn: () =>
      api.post("/api/conteos", {
        ubicaciones: codigos
          .split(/[\s,;]+/)
          .map((c) => c.trim())
          .filter(Boolean),
      }),
    onSuccess: () => {
      setCodigos("");
      void clientes.invalidateQueries({ queryKey: ["conteos"] });
    },
  });

  return (
    <div className="flex flex-col gap-4">
      <Titulo>Inventario</Titulo>
      <form
        className="flex flex-col gap-2 rounded-md border border-borde bg-superficie p-4"
        onSubmit={(e) => {
          e.preventDefault();
          abrir.mutate();
        }}
      >
        <label htmlFor="ubicaciones" className="text-sm font-medium">
          Ubicaciones que contar
        </label>
        <textarea
          id="ubicaciones"
          rows={2}
          value={codigos}
          onChange={(e) => setCodigos(e.target.value)}
          placeholder="P-01-01-1 P-01-02-1 R-04-01-0"
          className="codigo rounded-md border border-borde bg-superficie px-3 py-2"
        />
        <p className="text-xs text-suave">
          Mientras se cuentan, quedan bloqueadas: ni se coge ni se deja nada en ellas. Tienen que estar sin pedidos ni tareas a medias.
        </p>
        <Aviso error={abrir.error} />
        <Boton type="submit" variante="principal" className="self-start" disabled={!codigos.trim() || abrir.isPending}>
          Abrir el conteo
        </Boton>
      </form>

      {isPending ? (
        <Cargando />
      ) : (data ?? []).length === 0 ? (
        <Vacio>Todavía no hay conteos.</Vacio>
      ) : (
        <Tabla titulo="Conteos">
          <thead>
            <tr>
              <th>Conteo</th>
              <th>Estado</th>
              <th className="!text-right">Ubicaciones</th>
              <th>Abierto</th>
            </tr>
          </thead>
          <tbody>
            {(data ?? []).map((c) => (
              <tr key={c.id}>
                <td>
                  <Link to="/oficina/inventario/$id" params={{ id: c.id }} className="codigo text-acento hover:underline">
                    {c.numero}
                  </Link>
                </td>
                <td>
                  <Estado tono={TONO[c.estado]}>{ESTADOS_DE_CONTEO[c.estado]}</Estado>
                </td>
                <td className="text-right">{c.ubicaciones}</td>
                <td>{momento(c.abierto)}</td>
              </tr>
            ))}
          </tbody>
        </Tabla>
      )}
    </div>
  );
}

/** El conteo visto por el jefe de almacén: con lo esperado y las diferencias, para aprobar el ajuste. */
export function ConteoDeOficina() {
  const { id } = useParams({ from: "/oficina/inventario/$id" });
  const clientes = useQueryClient();
  const { data: c } = useQuery({ queryKey: ["conteo", id], queryFn: () => api.get<ConteoDetalle>(`/api/conteos/${id}`) });
  const recargar = () => clientes.invalidateQueries({ queryKey: ["conteo", id] });
  const aprobar = useMutation({ mutationFn: () => api.post(`/api/conteos/${id}/aprobacion`), onSuccess: recargar });
  const anular = useMutation({ mutationFn: () => api.post(`/api/conteos/${id}/anulacion`), onSuccess: recargar });
  if (!c) return <Cargando />;
  const abierto = !["Cerrado", "Anulado"].includes(c.estado);

  return (
    <div className="flex flex-col gap-4">
      <Titulo
        accion={
          abierto && (
            <div className="flex gap-2">
              {c.estado === "PorAprobar" && (
                <Boton variante="principal" onClick={() => aprobar.mutate()} disabled={aprobar.isPending}>
                  Aprobar el ajuste
                </Boton>
              )}
              <Boton variante="peligro" onClick={() => confirm("¿Anular el conteo? No se tocará el stock.") && anular.mutate()}>
                Anular
              </Boton>
            </div>
          )
        }
      >
        Conteo <span className="codigo">{c.numero}</span> <Estado tono={TONO[c.estado]}>{ESTADOS_DE_CONTEO[c.estado]}</Estado>
      </Titulo>
      <p className="codigo text-sm text-suave">{c.ubicaciones.join(" · ")}</p>
      {c.aprobadoPor && <p className="text-sm">Ajuste aprobado por {c.aprobadoPor}.</p>}
      <Aviso error={aprobar.error ?? anular.error} />
      {c.lineas.length === 0 ? (
        <Vacio>Las ubicaciones estaban vacías y no se ha contado nada.</Vacio>
      ) : (
        <Tabla titulo="Líneas del conteo">
          <thead>
            <tr>
              <th>Ubicación</th>
              <th>Artículo</th>
              <th>Lote</th>
              <th className="!text-right">Esperada</th>
              <th className="!text-right">1.ª vuelta</th>
              <th className="!text-right">Recuento</th>
              <th className="!text-right">Diferencia</th>
            </tr>
          </thead>
          <tbody>
            {c.lineas.map((l, i) => (
              <tr key={i}>
                <td className="codigo">{l.ubicacion}</td>
                <td>{l.articulo}</td>
                <td className="codigo">{l.lote || "—"}</td>
                <td className="text-right">{l.esperada ?? "—"}</td>
                <td className="text-right">{l.primera ?? "—"}</td>
                <td className="text-right">{l.segunda ?? "—"}</td>
                <td className={`text-right ${l.diferencia ? "font-semibold text-mal" : "text-suave"}`}>
                  {l.diferencia && l.diferencia > 0 ? `+${l.diferencia}` : l.diferencia}
                </td>
              </tr>
            ))}
          </tbody>
        </Tabla>
      )}
    </div>
  );
}
