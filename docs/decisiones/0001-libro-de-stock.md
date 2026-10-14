# 1. El stock: un libro de movimientos que no se borra y existencias que se actualizan con él

## Contexto

En un almacén, el stock que dice el sistema tiene que coincidir con lo que hay en la estantería, y cuando no coincide
hay que poder explicar por qué: quién movió qué, cuándo y con qué documento. Hay dos formas habituales de guardarlo:

- solo las existencias (cuánto hay de cada cosa en cada sitio), que se leen rápido pero no explican nada;
- solo los movimientos, y calcular el stock sumándolos, que lo explica todo pero se vuelve lento con el tiempo.

## Decisión

- **Las dos cosas, en la misma transacción.** Cada cambio de stock es un `Movimiento` (recepción, ubicación,
  traslado, reposición, picking, expedición, ajuste o cuarentena) que no se modifica ni se borra nunca; y en la misma
  transacción se actualiza la `Existencia` de su origen y su destino (artículo, ubicación y lote). Un error se
  corrige con otro movimiento que lo explica, no tocando el anterior.
- **Un solo sitio cambia cantidades**: `Libro.Aplicar` en el dominio. La aplicación carga y bloquea las existencias,
  llama al libro y guarda el movimiento y las existencias juntos.
- **Las reglas, también en la base**: `CHECK (cantidad >= 0)` y `CHECK (reservada BETWEEN 0 AND cantidad)`. Si un
  error de programación se las saltara, la transacción falla en lugar de dejar el stock mal.
- **Lo reservado está en la existencia y en las reservas**: al asignar un pedido se crea una `Reserva` y se suma a
  `Existencia.Reservada`. Lo disponible es `cantidad − reservada`. Coger del hueco para el pedido consume la reserva;
  cancelarlo la libera.
- **Se comprueba**: una prueba de propiedades (FsCheck) aplica secuencias aleatorias de entradas, traslados,
  reservas, picking, liberaciones y salidas (con las que fallan incluidas) y comprueba después de cada paso que
  ninguna existencia es negativa, que cada una es igual a la suma de sus movimientos y que lo reservado es igual a la
  suma de las reservas abiertas. Las pruebas de integración hacen la misma comprobación en SQL contra PostgreSQL al
  final de cada flujo.

## Alternativas descartadas

- **Event sourcing completo** (reconstruir el estado desde los eventos): es lo que el libro ya da para el stock, sin
  obligar a todo lo demás (pedidos, recepciones) a funcionar así.
- **Cantidades con decimales**: el stock va en unidades enteras; las cajas se convierten en el borde. Así no hay
  errores de redondeo ni medias unidades.
