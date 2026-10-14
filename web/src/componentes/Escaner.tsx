import { useEffect, useRef, useState } from "react";
import { Boton } from "./ui";

interface Props {
  etiqueta: string;
  /** Lo que se espera escanear, para la instrucción («el hueco P-01-04-1»). */
  pista?: string;
  onLeido: (codigo: string) => void;
  desactivado?: boolean;
}

/**
 * Lo que el operario escanea. Funciona con el lector de una PDA (que escribe el código y pulsa Intro), a mano y con
 * la cámara del móvil. El campo vuelve a tener el foco después de cada lectura para encadenar escaneos sin tocar.
 */
export function Escaner({ etiqueta, pista, onLeido, desactivado }: Props) {
  const [texto, setTexto] = useState("");
  const [camara, setCamara] = useState(false);
  const campo = useRef<HTMLInputElement>(null);
  const id = `escaner-${etiqueta.replace(/\W+/g, "-").toLowerCase()}`;

  useEffect(() => {
    if (!desactivado && !camara) campo.current?.focus();
  }, [desactivado, camara, etiqueta]);

  function leer(codigo: string) {
    const limpio = codigo.trim();
    if (!limpio) return;
    setTexto("");
    onLeido(limpio);
  }

  return (
    <div className="flex flex-col gap-2">
      <label htmlFor={id} className="text-base font-medium">
        {etiqueta}
        {pista && <span className="codigo ml-2 text-suave">{pista}</span>}
      </label>
      <form
        className="flex gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          leer(texto);
        }}
      >
        <input
          ref={campo}
          id={id}
          value={texto}
          onChange={(e) => setTexto(e.target.value)}
          disabled={desactivado}
          autoComplete="off"
          autoCapitalize="characters"
          spellCheck={false}
          enterKeyHint="go"
          placeholder="Código"
          className="codigo min-h-14 min-w-0 flex-1 rounded-md border-2 border-borde bg-superficie px-3 text-xl text-texto placeholder:text-suave focus:border-acento"
        />
        <Boton grande type="submit" disabled={desactivado || !texto.trim()} variante="principal">
          OK
        </Boton>
        <Boton grande onClick={() => setCamara(true)} disabled={desactivado} aria-label="Escanear con la cámara">
          Cámara
        </Boton>
      </form>
      {camara && (
        <LectorDeCamara
          onLeido={(codigo) => {
            setCamara(false);
            leer(codigo);
          }}
          onCerrar={() => setCamara(false)}
        />
      )}
    </div>
  );
}

interface Detector {
  detect(fuente: HTMLVideoElement): Promise<{ rawValue: string }[]>;
}

/**
 * Lee códigos con la cámara: el BarcodeDetector del navegador si lo tiene (Chrome en Android) y, si no,
 * zxing-wasm. Las etiquetas GS1-128 llegan con sus identificadores, que la API sabe leer.
 */
function LectorDeCamara({ onLeido, onCerrar }: { onLeido: (codigo: string) => void; onCerrar: () => void }) {
  const video = useRef<HTMLVideoElement>(null);
  const [error, setError] = useState<string | null>(null);
  // La función cambia en cada render; la cámara no tiene que reiniciarse por eso.
  const alLeer = useRef(onLeido);
  useEffect(() => {
    alLeer.current = onLeido;
  }, [onLeido]);

  useEffect(() => {
    let parado = false;
    let flujo: MediaStream | null = null;
    let temporizador = 0;

    async function empezar() {
      try {
        flujo = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" } });
      } catch {
        setError("No se puede usar la cámara. Revisa el permiso del navegador o usa el lector.");
        return;
      }
      if (parado || !video.current) return;
      video.current.srcObject = flujo;
      await video.current.play().catch(() => undefined);
      const leer = await crearLector();
      const intentar = async () => {
        if (parado || !video.current) return;
        const codigo = await leer(video.current).catch(() => null);
        if (codigo) {
          alLeer.current(codigo);
          return;
        }
        temporizador = window.setTimeout(intentar, 200);
      };
      void intentar();
    }

    void empezar();
    return () => {
      parado = true;
      window.clearTimeout(temporizador);
      flujo?.getTracks().forEach((t) => t.stop());
    };
  }, []);

  return (
    <div className="fixed inset-0 z-50 flex flex-col bg-black/90 p-4" role="dialog" aria-label="Escanear con la cámara">
      <video ref={video} className="mx-auto max-h-[70vh] w-full max-w-lg rounded-md object-cover" muted playsInline />
      {error ? <p className="mt-4 text-center text-mal">{error}</p> : <p className="mt-4 text-center text-white">Apunta al código de barras</p>}
      <Boton grande className="mx-auto mt-4" onClick={onCerrar}>
        Cerrar
      </Boton>
    </div>
  );
}

async function crearLector(): Promise<(video: HTMLVideoElement) => Promise<string | null>> {
  const Nativo = (window as unknown as { BarcodeDetector?: new (o: { formats: string[] }) => Detector }).BarcodeDetector;
  if (Nativo) {
    const detector = new Nativo({ formats: ["ean_13", "code_128"] });
    return async (video) => (await detector.detect(video))[0]?.rawValue ?? null;
  }
  // zxing-wasm, con su .wasm servido desde aquí mismo (la política de seguridad no deja cargarlo de fuera).
  const [{ prepareZXingModule, readBarcodes }, { default: urlDelWasm }] = await Promise.all([
    import("zxing-wasm/reader"),
    import("zxing-wasm/reader/zxing_reader.wasm?url"),
  ]);
  prepareZXingModule({ overrides: { locateFile: (ruta: string, prefijo: string) => (ruta.endsWith(".wasm") ? urlDelWasm : prefijo + ruta) } });
  const lienzo = document.createElement("canvas");
  return async (video) => {
    if (!video.videoWidth) return null;
    lienzo.width = video.videoWidth;
    lienzo.height = video.videoHeight;
    const dibujo = lienzo.getContext("2d", { willReadFrequently: true });
    if (!dibujo) return null;
    dibujo.drawImage(video, 0, 0);
    const imagen = dibujo.getImageData(0, 0, lienzo.width, lienzo.height);
    const [resultado] = await readBarcodes(imagen, { formats: ["EAN13", "Code128"], maxNumberOfSymbols: 1, textMode: "HRI" });
    return resultado?.text || null;
  };
}
