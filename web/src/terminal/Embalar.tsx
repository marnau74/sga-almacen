import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { useState } from "react";
import { Escaner } from "../componentes/Escaner";
import { Aviso, Boton, Cargando, Vacio } from "../componentes/ui";
import { api, ErrorDeApi } from "../lib/api";
import { leer } from "../lib/gs1";
import { bien, mal } from "../lib/sonido";
import type { PedidoDetalle, PedidoResumen } from "../lib/tipos";

/** Pedidos preparados (por embalar) y embalados (por cargar). */
export function Embalar() {
  const { data, isPending } = useQuery({
    queryKey: ["pedidos", "embalar"],
    queryFn: async () => [
      ...(await api.get<PedidoResumen[]>("/api/pedidos?estado=Preparado")),
      ...(await api.get<PedidoResumen[]>("/api/pedidos?estado=Embalado")),
    ],
  });
  if (isPending) return <Cargando />;
  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Embalar y expedir</h1>
        <Link to="/terminal" className="text-acento">
          Menú
        </Link>
      </div>
      {(data ?? []).length === 0 && <Vacio>No hay pedidos preparados.</Vacio>}
      {(data ?? []).map((p) => (
        <Link
          key={p.id}
          to="/terminal/embalar/$id"
          params={{ id: p.id }}
          className="block rounded-md border border-borde bg-superficie px-4 py-3 hover:border-acento"
        >
          <span className="codigo text-lg font-semibold">{p.numero}</span>
          <span className="ml-2 text-suave">{p.estado === "Embalado" ? "listo para cargar" : "por embalar"}</span>
          <span className="block text-suave">
            {p.cliente} · {p.preparadas} unidades
          </span>
        </Link>
      ))}
    </div>
  );
}

/**
 * Embalar: abrir un bulto (sale su SSCC), escanear lo que se mete y cerrarlo (sale su etiqueta). Cuando todo lo
 * preparado está embalado, se expide.
 */
export function EmbalarPedido() {
  const { id } = useParams({ from: "/terminal/embalar/$id" });
  const clientes = useQueryClient();
  const { data: pedido } = useQuery({ queryKey: ["pedido", id], queryFn: () => api.get<PedidoDetalle>(`/api/pedidos/${id}`) });
  const [error, setError] = useState<string | null>(null);
  const [lote, setLote] = useState<string | null>(null);
  const [pendiente, setPendiente] = useState<string | null>(null);
  const recargar = () => clientes.invalidateQueries({ queryKey: ["pedido", id] });

  const alTerminar = { ok: () => void recargar(), error: setError };
  const abrir = useAccion(() => api.post(`/api/pedidos/${id}/bultos`), alTerminar);
  const cerrar = useAccion(() => api.post(`/api/pedidos/${id}/bultos/abierto/cierre`), alTerminar);
  const expedir = useAccion(() => api.post(`/api/pedidos/${id}/expedicion`), alTerminar);
  const meter = useMutation({
    mutationFn: (datos: { codigo: string; lote: string | null }) =>
      api.post(`/api/pedidos/${id}/bultos/abierto/contenido`, { ...datos, cantidad: leer(datos.codigo).cantidad ?? 1 }),
    onSuccess: () => {
      bien();
      setError(null);
      setPendiente(null);
      setLote(null);
      void recargar();
    },
    onError: (e, datos) => {
      mal();
      // Si el artículo va de varios lotes, se pregunta cuál.
      if (e instanceof ErrorDeApi && e.codigo === "indica-el-lote") setPendiente(datos.codigo);
      setError(e.message);
    },
  });

  if (!pedido) return <Cargando />;
  const abierto = pedido.bultos.find((b) => !b.cerrado);
  const lotesDe = (codigo: string) => {
    const gtin = leer(codigo).gtin;
    const articulo = pedido.lineas.find((l) => l.ean === gtin)?.articuloId;
    return pedido.preparados.filter((p) => p.articuloId === articulo && p.embalado < p.cantidad).map((p) => p.lote);
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="codigo text-2xl font-semibold">{pedido.resumen.numero}</h1>
        <Link to="/terminal/embalar" className="text-acento">
          Pedidos
        </Link>
      </div>
      <p className="text-suave">{pedido.resumen.cliente}</p>
      {error && <Aviso>{error}</Aviso>}

      {pedido.resumen.estado === "Preparado" && !abierto && (
        <Boton grande variante="principal" onClick={() => abrir.mutate()} disabled={abrir.isPending}>
          Abrir un bulto
        </Boton>
      )}
      {abierto && (
        <section className="flex flex-col gap-3 rounded-md border border-acento/50 bg-superficie p-4" aria-label="Bulto abierto">
          <p>
            Bulto {abierto.numero} · <span className="codigo">{abierto.sscc}</span>
          </p>
          {pendiente ? (
            <div className="flex flex-col gap-2">
              <p>¿De qué lote?</p>
              <div className="flex flex-wrap gap-2">
                {lotesDe(pendiente).map((l) => (
                  <Boton key={l} grande className="codigo" onClick={() => meter.mutate({ codigo: pendiente, lote: l })}>
                    {l || "sin lote"}
                  </Boton>
                ))}
              </div>
            </div>
          ) : (
            <Escaner etiqueta="Escanea lo que metes" onLeido={(codigo) => meter.mutate({ codigo, lote })} desactivado={meter.isPending} />
          )}
          <Boton grande onClick={() => cerrar.mutate()} disabled={cerrar.isPending || abierto.contenido.length === 0}>
            Cerrar el bulto
          </Boton>
        </section>
      )}

      <ul className="flex flex-col divide-y divide-borde rounded-md border border-borde bg-superficie" aria-label="Preparado">
        {pedido.preparados.map((p) => (
          <li key={`${p.articuloId}-${p.lote}`} className="flex justify-between gap-3 px-4 py-2">
            <span>
              {p.articulo}
              {p.lote && <span className="codigo block text-xs text-suave">lote {p.lote}</span>}
            </span>
            <span className={`cifras ${p.embalado === p.cantidad ? "text-bien" : ""}`}>
              {p.embalado} / {p.cantidad}
            </span>
          </li>
        ))}
      </ul>

      {pedido.bultos.filter((b) => b.cerrado).length > 0 && (
        <ul className="flex flex-col gap-1 text-sm" aria-label="Bultos cerrados">
          {pedido.bultos
            .filter((b) => b.cerrado)
            .map((b) => (
              <li key={b.numero}>
                Bulto {b.numero} ·{" "}
                <a className="text-acento underline" href={`/api/pedidos/${id}/bultos/${b.numero}/etiqueta.pdf`} target="_blank" rel="noreferrer">
                  etiqueta <span className="codigo">{b.sscc}</span>
                </a>
              </li>
            ))}
        </ul>
      )}

      {pedido.resumen.estado === "Embalado" && (
        <Boton grande variante="principal" onClick={() => expedir.mutate()} disabled={expedir.isPending}>
          Cargado: expedir
        </Boton>
      )}
      {pedido.resumen.estado === "Expedido" && <p className="text-bien">Expedido.</p>}
    </div>
  );
}

/** Una acción del embalaje con su respuesta: sonido y recarga si va bien, sonido y mensaje si no. */
function useAccion(fn: () => Promise<unknown>, alTerminar: { ok: () => void; error: (mensaje: string | null) => void }) {
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      bien();
      alTerminar.error(null);
      alTerminar.ok();
    },
    onError: (e) => {
      mal();
      alTerminar.error(e.message);
    },
  });
}
