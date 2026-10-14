import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { useState } from "react";
import { Aviso, Boton, Cargando, Estado, Tabla, Titulo, Vacio } from "../componentes/ui";
import { api, conConsulta } from "../lib/api";
import { dia, diasHasta, entero, momento, MOVIMIENTOS, ZONAS } from "../lib/formato";
import { esJefe, useSesion } from "../lib/sesion";
import type { Articulo, StockEnUbicacion, Trazabilidad } from "../lib/tipos";

export function Stock() {
  const [buscar, setBuscar] = useState("");
  const { data, isPending } = useQuery({
    queryKey: ["articulos", buscar],
    queryFn: () => api.get<Articulo[]>(conConsulta("/api/articulos", { buscar })),
  });
  return (
    <div className="flex flex-col gap-4">
      <Titulo>Stock</Titulo>
      <div>
        <label htmlFor="buscar-stock" className="text-sm font-medium">
          Buscar
        </label>
        <input
          id="buscar-stock"
          value={buscar}
          onChange={(e) => setBuscar(e.target.value)}
          placeholder="Nombre, marca o EAN"
          className="mt-1 min-h-10 w-full max-w-md rounded-md border border-borde bg-superficie px-3"
        />
      </div>
      {isPending ? (
        <Cargando />
      ) : (data ?? []).length === 0 ? (
        <Vacio>No hay artículos que coincidan.</Vacio>
      ) : (
        <Tabla titulo="Artículos y su stock">
          <thead>
            <tr>
              <th>Artículo</th>
              <th>Marca</th>
              <th>Formato</th>
              <th>EAN</th>
              <th className="!text-right">Stock</th>
              <th className="!text-right">Disponible</th>
            </tr>
          </thead>
          <tbody>
            {(data ?? []).map((a) => (
              <tr key={a.id}>
                <td>
                  <Link to="/oficina/stock/$id" params={{ id: a.id }} className="text-acento hover:underline">
                    {a.nombre}
                  </Link>
                </td>
                <td>{a.marca ?? "—"}</td>
                <td>{a.formato ?? "—"}</td>
                <td className="codigo">{a.ean}</td>
                <td className="text-right">{entero(a.stock)}</td>
                <td className={`text-right ${a.disponible === 0 ? "text-suave" : ""}`}>{entero(a.disponible)}</td>
              </tr>
            ))}
          </tbody>
        </Tabla>
      )}
      <p className="text-xs text-suave">
        Artículos de{" "}
        <a href="https://world.openfoodfacts.org" className="underline" target="_blank" rel="noreferrer">
          Open Food Facts
        </a>{" "}
        (ODbL). El stock es de demostración.
      </p>
    </div>
  );
}

export function ArticuloDetalle() {
  const { id } = useParams({ from: "/oficina/stock/$id" });
  const { data: stock, isPending } = useQuery({ queryKey: ["stock", id], queryFn: () => api.get<StockEnUbicacion[]>(`/api/articulos/${id}/stock`) });
  if (isPending) return <Cargando />;
  const lista = stock ?? [];
  const total = lista.reduce((a, s) => a + s.cantidad, 0);
  const reservado = lista.reduce((a, s) => a + s.reservada, 0);

  return (
    <div className="flex flex-col gap-4">
      <Titulo>Stock del artículo</Titulo>
      <p>
        <span className="cifras font-semibold">{entero(total)}</span> unidades, <span className="cifras">{entero(reservado)}</span> reservadas para
        pedidos.
      </p>
      {lista.length === 0 ? (
        <Vacio>No hay stock de este artículo.</Vacio>
      ) : (
        <Tabla titulo="Stock por ubicación y lote">
          <thead>
            <tr>
              <th>Ubicación</th>
              <th>Zona</th>
              <th>Lote</th>
              <th>Caducidad</th>
              <th className="!text-right">Cantidad</th>
              <th className="!text-right">Reservada</th>
              <th>
                <span className="sr-only">Avisos</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {lista.map((s) => (
              <tr key={`${s.ubicacion}-${s.lote}`}>
                <td className="codigo">{s.ubicacion}</td>
                <td>{ZONAS[s.zona]}</td>
                <td className="codigo">
                  {s.lote ? (
                    <Link to="/oficina/stock/$id/lotes/$lote" params={{ id, lote: s.lote }} className="text-acento hover:underline">
                      {s.lote}
                    </Link>
                  ) : (
                    "—"
                  )}
                </td>
                <td className={s.caducidad && diasHasta(s.caducidad) < 15 ? "text-aviso" : ""}>{dia(s.caducidad)}</td>
                <td className="text-right">{entero(s.cantidad)}</td>
                <td className="text-right">{entero(s.reservada)}</td>
                <td className="flex flex-wrap gap-1">
                  {s.loteBloqueado && <Estado tono="mal">lote bloqueado</Estado>}
                  {s.ubicacionBloqueada && <Estado tono="aviso">contándose</Estado>}
                  {s.zona === "Cuarentena" && <Estado tono="mal">cuarentena</Estado>}
                </td>
              </tr>
            ))}
          </tbody>
        </Tabla>
      )}
    </div>
  );
}

/** Todo lo de un lote: de dónde vino, por dónde ha pasado, a quién se ha servido y dónde queda. Para una retirada. */
export function TrazabilidadDeLote() {
  const { id, lote } = useParams({ from: "/oficina/stock/$id/lotes/$lote" });
  const sesion = useSesion();
  const clientes = useQueryClient();
  const [motivo, setMotivo] = useState("");
  const { data: t } = useQuery({ queryKey: ["stock", id, lote], queryFn: () => api.get<Trazabilidad>(`/api/articulos/${id}/lotes/${encodeURIComponent(lote)}`) });
  const recargar = () => clientes.invalidateQueries({ queryKey: ["stock", id] });
  const bloquear = useMutation({ mutationFn: () => api.put(`/api/articulos/${id}/lotes/${encodeURIComponent(lote)}/bloqueo`, { motivo }), onSuccess: recargar });
  const desbloquear = useMutation({ mutationFn: () => api.delete(`/api/articulos/${id}/lotes/${encodeURIComponent(lote)}/bloqueo`), onSuccess: recargar });
  if (!t) return <Cargando />;

  return (
    <div className="flex flex-col gap-5">
      <Titulo>
        Lote <span className="codigo">{t.lote}</span> {t.bloqueado && <Estado tono="mal">bloqueado</Estado>}
      </Titulo>
      <p>
        {t.articulo} · <span className="codigo">{t.ean}</span>
        {t.caducidad && <> · caduca el {dia(t.caducidad)}</>}
      </p>

      {esJefe(sesion) &&
        (t.bloqueado ? (
          <Boton className="self-start" onClick={() => desbloquear.mutate()}>
            Desbloquear el lote
          </Boton>
        ) : (
          <form
            className="flex flex-wrap items-end gap-2"
            onSubmit={(e) => {
              e.preventDefault();
              bloquear.mutate();
            }}
          >
            <div className="flex flex-col gap-1">
              <label htmlFor="motivo" className="text-sm font-medium">
                Motivo del bloqueo
              </label>
              <input id="motivo" required value={motivo} onChange={(e) => setMotivo(e.target.value)} className="min-h-9 w-72 rounded-md border border-borde bg-superficie px-3" />
            </div>
            <Boton type="submit" variante="peligro">
              Bloquear: no se sirve más
            </Boton>
          </form>
        ))}
      <Aviso error={bloquear.error ?? desbloquear.error} />

      <section aria-labelledby="salidas">
        <h2 id="salidas" className="mb-2 font-semibold">
          A quién se ha servido
        </h2>
        {t.salidas.length === 0 ? (
          <Vacio>A nadie todavía.</Vacio>
        ) : (
          <Tabla titulo="Pedidos con este lote">
            <thead>
              <tr>
                <th>Pedido</th>
                <th>Cliente</th>
                <th className="!text-right">Unidades</th>
                <th>Expedido</th>
              </tr>
            </thead>
            <tbody>
              {t.salidas.map((s) => (
                <tr key={s.pedido}>
                  <td className="codigo">{s.pedido}</td>
                  <td>{s.cliente}</td>
                  <td className="text-right">{entero(s.cantidad)}</td>
                  <td>{s.expedido ? momento(s.expedido) : "todavía en el almacén"}</td>
                </tr>
              ))}
            </tbody>
          </Tabla>
        )}
      </section>

      <section aria-labelledby="donde">
        <h2 id="donde" className="mb-2 font-semibold">
          Dónde queda
        </h2>
        {t.dondeEsta.length === 0 ? (
          <Vacio>No queda nada en el almacén.</Vacio>
        ) : (
          <ul className="flex flex-wrap gap-2">
            {t.dondeEsta.map((d) => (
              <li key={d.ubicacion} className="rounded-md border border-borde bg-superficie px-3 py-1">
                <span className="codigo">{d.ubicacion}</span> · <span className="cifras">{entero(d.cantidad)}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section aria-labelledby="historia">
        <h2 id="historia" className="mb-2 font-semibold">
          Historia (recepciones: {t.recepciones.join(", ") || "—"})
        </h2>
        <Tabla titulo="Movimientos del lote">
          <thead>
            <tr>
              <th>Cuándo</th>
              <th>Movimiento</th>
              <th>Desde</th>
              <th>Hasta</th>
              <th className="!text-right">Unidades</th>
              <th>Documento</th>
              <th>Quién</th>
            </tr>
          </thead>
          <tbody>
            {t.movimientos.map((m, i) => (
              <tr key={i}>
                <td>{momento(m.momento)}</td>
                <td>{MOVIMIENTOS[m.tipo] ?? m.tipo}</td>
                <td className="codigo">{m.origen ?? "—"}</td>
                <td className="codigo">{m.destino ?? "—"}</td>
                <td className="text-right">{entero(m.cantidad)}</td>
                <td className="codigo">{m.documento}</td>
                <td>{m.usuario}</td>
              </tr>
            ))}
          </tbody>
        </Tabla>
      </section>
    </div>
  );
}
