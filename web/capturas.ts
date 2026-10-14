import { chromium, devices } from "@playwright/test";

/** Las capturas del README, contra la demo en marcha (http://localhost:5080). */
const base = process.argv[2] ?? "http://localhost:5080";
const salida = "../docs/capturas";
const navegador = await chromium.launch();

async function sesion(usuario: string, opciones: Parameters<typeof navegador.newContext>[0]) {
  const contexto = await navegador.newContext({ ...opciones, locale: "es-ES", timezoneId: "Europe/Madrid" });
  const pagina = await contexto.newPage();
  await pagina.goto(base);
  await pagina.getByLabel("Usuario").fill(usuario);
  await pagina.getByLabel("Contraseña").fill("almacen-demo");
  await pagina.getByRole("button", { name: "Entrar" }).click();
  await pagina.getByRole("button", { name: "Salir" }).waitFor();
  return pagina;
}

const oficina = await sesion("jefa", { viewport: { width: 1360, height: 900 } });
for (const [ruta, nombre] of [
  ["/oficina", "panel"],
  ["/oficina/pedidos", "pedidos"],
  ["/oficina/ubicaciones", "ubicaciones"],
] as const) {
  await oficina.goto(base + ruta);
  await oficina.waitForTimeout(1200);
  await oficina.screenshot({ path: `${salida}/${nombre}.png`, fullPage: true });
}
const pedidos = await (await oficina.request.get(`${base}/api/pedidos?estado=Expedido`)).json();
await oficina.goto(`${base}/oficina/pedidos/${pedidos[0].id}`);
await oficina.waitForTimeout(1000);
await oficina.screenshot({ path: `${salida}/pedido.png`, fullPage: true });
const preparado = pedidos[0].id;
const detalle = await (await oficina.request.get(`${base}/api/pedidos/${preparado}`)).json();
const conLote = detalle.preparados.find((p: { lote: string }) => p.lote);
await oficina.goto(`${base}/oficina/stock/${conLote.articuloId}/lotes/${encodeURIComponent(conLote.lote)}`);
await oficina.waitForTimeout(1000);
await oficina.screenshot({ path: `${salida}/trazabilidad.png`, fullPage: true });

const terminal = await sesion("operario1", { ...devices["Pixel 7"] });
await terminal.goto(`${base}/terminal`);
await terminal.waitForTimeout(1000);
await terminal.screenshot({ path: `${salida}/terminal-inicio.png` });
await terminal.getByRole("link", { name: /Preparar pedidos/ }).click();
await terminal.getByRole("button", { name: "Empezar por la primera" }).click();
await terminal.waitForTimeout(1200);
await terminal.screenshot({ path: `${salida}/terminal-tarea.png` });
await terminal.getByLabel(/Escanea el hueco/).fill("R-99-99-9");
await terminal.keyboard.press("Enter");
await terminal.waitForTimeout(500);
await terminal.screenshot({ path: `${salida}/terminal-error.png` });
// La deja para otro, para no cambiar la demo.
await terminal.getByRole("button", { name: "Dejarla para otro" }).click();
await navegador.close();
console.log("capturas hechas");
