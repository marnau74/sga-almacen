// Respuesta al escanear en el terminal: en una nave con ruido y guantes, el operario no mira la pantalla para saber
// si ha ido bien. Un pitido agudo y corto si sí; dos graves y vibración si no.

let contexto: AudioContext | null = null;

function pitar(frecuencia: number, duracion: number, retraso = 0) {
  try {
    contexto ??= new AudioContext();
    const oscilador = contexto.createOscillator();
    const volumen = contexto.createGain();
    oscilador.type = "square";
    oscilador.frequency.value = frecuencia;
    volumen.gain.value = 0.08;
    oscilador.connect(volumen).connect(contexto.destination);
    const inicio = contexto.currentTime + retraso;
    oscilador.start(inicio);
    oscilador.stop(inicio + duracion);
  } catch {
    // Sin audio (navegador sin permiso): queda la respuesta en pantalla.
  }
}

export function bien() {
  pitar(1760, 0.08);
}

export function mal() {
  pitar(220, 0.15);
  pitar(220, 0.15, 0.2);
  navigator.vibrate?.([120, 80, 120]);
}
