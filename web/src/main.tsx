import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "@tanstack/react-router";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { ErrorDeApi } from "./lib/api";
import { router } from "./rutas";
import "./estilos.css";

const clientes = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 15_000,
      // Un 4xx no se arregla reintentando.
      retry: (intentos, error) => !(error instanceof ErrorDeApi && error.estado < 500) && intentos < 2,
    },
  },
});

createRoot(document.getElementById("raiz")!).render(
  <StrictMode>
    <QueryClientProvider client={clientes}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
);
