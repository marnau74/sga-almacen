import { createRootRoute, createRoute, createRouter, Navigate, Outlet } from "@tanstack/react-router";
import { Carcasa } from "./Carcasa";
import { Entrar } from "./Entrar";
import { useSesionDeLaApi, veOficina, ProveedorDeSesion } from "./lib/sesion";
import { Cargando } from "./componentes/ui";
import { ESTADOS_DE_PEDIDO } from "./lib/formato";
import type { EstadoDePedido } from "./lib/tipos";
import { Inventario, ConteoDeOficina } from "./oficina/Inventario";
import { PanelDeOficina } from "./oficina/Panel";
import { Pedido, Pedidos } from "./oficina/Pedidos";
import { Recepcion, Recepciones } from "./oficina/Recepciones";
import { ArticuloDetalle, Stock, TrazabilidadDeLote } from "./oficina/Stock";
import { Ubicaciones } from "./oficina/Ubicaciones";
import { InicioDelTerminal } from "./terminal/Inicio";
import { ListaDeTareas, HacerTarea } from "./terminal/Tareas";
import { Recibir, RecibirRecepcion } from "./terminal/Recibir";
import { Embalar, EmbalarPedido } from "./terminal/Embalar";
import { Contar, ContarConteo } from "./terminal/Contar";

/** La raíz: sin sesión, la pantalla de entrar; con ella, la aplicación. */
function Raiz() {
  const { data: sesion, isPending } = useSesionDeLaApi();
  if (isPending) return <Cargando />;
  if (!sesion) return <Entrar />;
  return (
    <ProveedorDeSesion sesion={sesion}>
      <Carcasa>
        <Outlet />
      </Carcasa>
    </ProveedorDeSesion>
  );
}

const raiz = createRootRoute({ component: Raiz });

function Inicio() {
  const { data: sesion } = useSesionDeLaApi();
  return <Navigate to={sesion && veOficina(sesion) ? "/oficina" : "/terminal"} replace />;
}

const r = <P extends string>(path: P, component: () => React.ReactNode) =>
  createRoute({ getParentRoute: () => raiz, path, component });

const rutas = [
  r("/", Inicio),
  r("/oficina", PanelDeOficina),
  createRoute({
    getParentRoute: () => raiz,
    path: "/oficina/pedidos",
    component: Pedidos,
    validateSearch: (s: Record<string, unknown>): { estado?: EstadoDePedido } =>
      typeof s.estado === "string" && s.estado in ESTADOS_DE_PEDIDO ? { estado: s.estado as EstadoDePedido } : {},
  }),
  r("/oficina/pedidos/$id", Pedido),
  r("/oficina/recepciones", Recepciones),
  r("/oficina/recepciones/$id", Recepcion),
  r("/oficina/stock", Stock),
  r("/oficina/stock/$id", ArticuloDetalle),
  r("/oficina/stock/$id/lotes/$lote", TrazabilidadDeLote),
  r("/oficina/ubicaciones", Ubicaciones),
  r("/oficina/inventario", Inventario),
  r("/oficina/inventario/$id", ConteoDeOficina),
  r("/terminal", InicioDelTerminal),
  r("/terminal/tareas/$tipo", ListaDeTareas),
  r("/terminal/tarea/$id", HacerTarea),
  r("/terminal/recibir", Recibir),
  r("/terminal/recibir/$id", RecibirRecepcion),
  r("/terminal/embalar", Embalar),
  r("/terminal/embalar/$id", EmbalarPedido),
  r("/terminal/contar", Contar),
  r("/terminal/contar/$id", ContarConteo),
];

export const router = createRouter({
  routeTree: raiz.addChildren(rutas),
  defaultNotFoundComponent: () => <p className="p-6">Esta página no existe.</p>,
});

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
