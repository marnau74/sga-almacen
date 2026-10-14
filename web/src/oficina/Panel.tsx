import { useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { Cargando, Cifra, Tabla, Titulo, Vacio } from "../componentes/ui";
import { api } from "../lib/api";
import { aHoraLocal, dia, diasHasta, entero, ESTADOS_DE_PEDIDO, momento } from "../lib/formato";
import type { EstadoDePedido, Panel } from "../lib/tipos";

const ORDEN: EstadoDePedido[] = ["Pendiente", "AsignadoParcial", "Asignado", "EnPreparacion", "Preparado", "Embalado"];

/** El estado del almacén de un vistazo. Se actualiza solo con los avisos de la API. */
export function PanelDeOficina() {
  const { data: panel, isPending } = useQuery({ queryKey: ["panel"], queryFn: () => api.get<Panel>("/api/panel") });
  if (isPending || !panel) return <Cargando />;
  const ocupacion = panel.huecosDeReserva ? Math.round((panel.huecosDeReservaOcupados / panel.huecosDeReserva) * 100) : 0;
  const pendientes = Object.values(panel.tareasPendientes).reduce((a, b) => a + (b ?? 0), 0);
  const porHora = aHoraLocal(panel.lineasPorHora);
  const maximo = Math.max(1, ...porHora);
  const horas = porHora.map((n, h) => ({ n, h })).filter(({ h }) => h >= 6 && h <= 22);

  return (
    <div className="flex flex-col gap-6">
      <Titulo>Panel</Titulo>
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Cifra valor={entero(panel.lineasPreparadasHoy)} texto="líneas preparadas hoy" />
        <Cifra valor={entero(pendientes)} texto="tareas pendientes en el terminal" />
        <Cifra valor={`${ocupacion} %`} texto={`de la reserva ocupada (${panel.huecosDeReservaOcupados} de ${panel.huecosDeReserva} huecos)`} />
        <Cifra
          valor={entero(panel.recepcionesConDiferencias)}
          texto="recepciones con diferencias"
          tono={panel.recepcionesConDiferencias > 0 ? "aviso" : undefined}
        />
      </div>

      <section aria-labelledby="pedidos-por-estado">
        <h2 id="pedidos-por-estado" className="mb-2 font-semibold">
          Pedidos
        </h2>
        <ul className="flex flex-wrap gap-2">
          {ORDEN.map((estado) => (
            <li key={estado}>
              <Link
                to="/oficina/pedidos"
                search={{ estado }}
                className="block rounded-md border border-borde bg-superficie px-3 py-2 hover:border-acento"
              >
                <span className="cifras text-xl font-semibold">{entero(panel.pedidosPorEstado[estado] ?? 0)}</span>
                <span className="ml-2 text-sm text-suave">{ESTADOS_DE_PEDIDO[estado]}</span>
              </Link>
            </li>
          ))}
        </ul>
      </section>

      <section aria-labelledby="ritmo">
        <h2 id="ritmo" className="mb-2 font-semibold">
          Líneas preparadas hoy, por hora
        </h2>
        <div className="flex h-32 items-end gap-1 rounded-md border border-borde bg-superficie p-3" role="img" aria-label={`Líneas por hora: ${horas.map(({ h, n }) => `${h} h, ${n}`).join("; ")}`}>
          {horas.map(({ h, n }) => (
            <div key={h} className="flex h-full flex-1 flex-col items-center justify-end gap-1">
              <div className="w-full rounded-sm bg-acento" style={{ height: `${(n / maximo) * 85}%`, minHeight: n ? 2 : 0 }} title={`${n} líneas`} />
              <span className="cifras text-[10px] text-suave">{h}</span>
            </div>
          ))}
        </div>
      </section>

      <div className="grid gap-6 lg:grid-cols-2">
        <section aria-labelledby="faltas">
          <h2 id="faltas" className="mb-2 font-semibold">
            Faltas en el picking (24 h)
          </h2>
          {panel.faltas.length === 0 ? (
            <Vacio>Ninguna.</Vacio>
          ) : (
            <Tabla titulo="Faltas en el picking">
              <thead>
                <tr>
                  <th>Hueco</th>
                  <th>Artículo</th>
                  <th className="!text-right">Faltaban</th>
                  <th>Cuándo</th>
                </tr>
              </thead>
              <tbody>
                {panel.faltas.map((f, i) => (
                  <tr key={i}>
                    <td className="codigo">{f.ubicacion}</td>
                    <td>{f.articulo}</td>
                    <td className="text-right text-mal">{entero(f.unidades)}</td>
                    <td>{momento(f.momento)}</td>
                  </tr>
                ))}
              </tbody>
            </Tabla>
          )}
          <p className="mt-2 text-xs text-suave">Un hueco con falta merece un conteo: el sistema creía que había más.</p>
        </section>

        <section aria-labelledby="caducan">
          <h2 id="caducan" className="mb-2 font-semibold">
            Caducan pronto
          </h2>
          {panel.caducanPronto.length === 0 ? (
            <Vacio>Nada en los próximos días.</Vacio>
          ) : (
            <Tabla titulo="Lotes que caducan pronto">
              <thead>
                <tr>
                  <th>Artículo</th>
                  <th>Lote</th>
                  <th>Caducidad</th>
                  <th className="!text-right">Unidades</th>
                </tr>
              </thead>
              <tbody>
                {panel.caducanPronto.map((c) => (
                  <tr key={`${c.articulo}-${c.lote}`}>
                    <td>{c.articulo}</td>
                    <td className="codigo">{c.lote}</td>
                    <td className={diasHasta(c.caducidad) < 0 ? "text-mal" : "text-aviso"}>{dia(c.caducidad)}</td>
                    <td className="text-right">{entero(c.cantidad)}</td>
                  </tr>
                ))}
              </tbody>
            </Tabla>
          )}
        </section>
      </div>

      <section aria-labelledby="roturas">
        <h2 id="roturas" className="mb-2 font-semibold">
          Pedidos con rotura
        </h2>
        {panel.roturas.length === 0 ? (
          <Vacio>Ninguno: todo lo preparado ha salido completo.</Vacio>
        ) : (
          <Tabla titulo="Pedidos con rotura">
            <thead>
              <tr>
                <th>Pedido</th>
                <th>Cliente</th>
                <th className="!text-right">Unidades sin servir</th>
              </tr>
            </thead>
            <tbody>
              {panel.roturas.map((r) => (
                <tr key={r.pedido}>
                  <td className="codigo">{r.pedido}</td>
                  <td>{r.cliente}</td>
                  <td className="text-right text-mal">{entero(r.unidades)}</td>
                </tr>
              ))}
            </tbody>
          </Tabla>
        )}
      </section>
    </div>
  );
}
