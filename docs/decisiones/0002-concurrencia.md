# 2. Concurrencia: bloqueo por fila en orden fijo y existencias que se crean sin carreras

## Contexto

Varios operarios trabajan a la vez sobre los mismos huecos, la oficina asigna pedidos mientras se recibe mercancía y
dos personas pueden intentar quedarse la misma tarea. Lo que no puede pasar:

- que dos pedidos se lleven la misma unidad (comprobar el disponible y descontarlo en dos pasos sin bloquear);
- que el stock quede negativo;
- que dos transacciones se esperen la una a la otra para siempre (interbloqueo).

## Decisión

- **Bloqueo pesimista de las existencias que se tocan** (`SELECT … FOR UPDATE`) antes de comprobar y cambiar nada.
  Siempre **en el mismo orden** (artículo, ubicación, lote): dos transacciones que bloquean las mismas filas lo hacen
  en el mismo orden y no se cruzan.
- **El reparto de stock entre pedidos bloquea todas las existencias de los artículos implicados**. Diez repartos a
  la vez sobre el mismo artículo se esperan uno a otro y cada uno ve lo que el anterior ya reservó.
- **La existencia del destino se crea antes de bloquear**, con `INSERT … ON CONFLICT DO NOTHING`. Un `FOR UPDATE`
  sobre una fila que aún no existe no bloquea nada, y dos movimientos a la vez hacia un sitio vacío (el muelle, por
  ejemplo) intentaban crearla los dos y uno fallaba por clave duplicada. Con la inserción previa, la segunda
  transacción espera a la primera y luego encuentra la fila. Lo encontró la prueba de concurrencia.
- **Concurrencia optimista (`xmin` de PostgreSQL) en lo que no es stock**: tareas, pedidos, recepciones, conteos y
  ubicaciones. Si dos operarios cogen la misma tarea a la vez, el segundo recibe un conflicto (409) y la tarea queda
  del primero.
- **Pruebas de concurrencia de verdad**, contra PostgreSQL: diez asignaciones a la vez sobre 30 unidades (se asignan
  exactamente 30), diez operarios cogiendo del mismo hueco (el stock final es exacto) y cuatro operarios tomando la
  misma tarea (solo uno se la queda). Se han repetido varias veces seguidas para descartar que pasen por suerte.

## Alternativas descartadas

- **Aislamiento `SERIALIZABLE`**: también evita las carreras, pero obliga a reintentar las transacciones que
  PostgreSQL aborta, y con muchos operarios sobre los mismos huecos serían muchas.
- **Solo concurrencia optimista en las existencias**: con muchos operarios sobre un mismo hueco de picking, casi
  todas las confirmaciones chocarían y habría que reintentarlas; bloquear y esperar unos milisegundos es mejor.
