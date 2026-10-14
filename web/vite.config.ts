import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// En desarrollo, la API (dotnet run) está en el 5080: el front le pasa /api y /avisos para que todo sea el mismo
// sitio (la cookie de sesión es SameSite=Strict). Al construir, el front acaba en wwwroot de la API.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      "/api": "http://localhost:5080",
      "/avisos": { target: "http://localhost:5080", ws: true },
      "/health": "http://localhost:5080",
    },
  },
  build: { outDir: "../src/Sga.Api/wwwroot", emptyOutDir: true },
  test: { environment: "jsdom", include: ["src/**/*.test.{ts,tsx}"] },
});
