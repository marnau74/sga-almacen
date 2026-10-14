import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";

type TipoDeAviso = "TareasCambiadas" | "PedidosCambiados" | "RecepcionesCambiadas" | "StockCambiado" | "ConteosCambiados";

// Qué consultas hay que volver a pedir con cada aviso.
const AFECTA: Record<TipoDeAviso, string[]> = {
  TareasCambiadas: ["tareas", "panel"],
  PedidosCambiados: ["pedidos", "pedido", "panel"],
  RecepcionesCambiadas: ["recepciones", "recepcion", "panel"],
  StockCambiado: ["articulos", "stock", "panel", "ubicaciones"],
  ConteosCambiados: ["conteos", "conteo", "ubicaciones"],
};

/**
 * Escucha los avisos de la API (SignalR) y marca como viejas las consultas afectadas: lo que se ve se actualiza
 * solo, sin recargar, cuando otro operario o la oficina cambian algo.
 */
export function useAvisos(activo: boolean) {
  const clientes = useQueryClient();
  useEffect(() => {
    if (!activo) return;
    const conexion = new HubConnectionBuilder()
      .withUrl("/avisos")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    conexion.on("aviso", (aviso: { tipo: TipoDeAviso }) => {
      for (const clave of AFECTA[aviso.tipo] ?? []) {
        void clientes.invalidateQueries({ queryKey: [clave] });
      }
    });
    void conexion.start().catch(() => {
      // Sin tiempo real se sigue pudiendo trabajar: las consultas se refrescan al volver a la pantalla.
    });
    return () => void conexion.stop();
  }, [activo, clientes]);
}
