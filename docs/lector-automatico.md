# Recepcion automatica de notificaciones

El servidor ASP.NET Core ejecuta `AutomaticReaderWorker` en segundo plano.
No se registran correos a mano. Gmail recibe el aviso del banco y Listener consulta
INBOX cada 30 segundos, filtra BCP, extrae los datos, los guarda y expone su JSON.
Cerrar el navegador no detiene el lector. Detener el servidor si lo detiene.

## Activar

Abrir `/panel`, introducir Gmail y su contraseña de aplicacion, y pulsar
**Activar lector**. Esperar a `Esperando correos nuevos` antes de enviar la primera
prueba. Ese primer ciclo establece el UID inicial; no importa los correos antiguos.
En conexiones posteriores al mismo buzon se retoma el UID guardado, incluyendo lo
que llego mientras estuvo detenido. Los campos dias/maximo son solo del diagnostico
manual. El lector automatico procesa hasta 100 avisos por ciclo, en orden de UID.

La contraseña queda solo en memoria. Al reiniciar el servidor se debe activar otra
vez desde el panel; no se guarda la contraseña en SQL, en archivos ni en el navegador.
La activacion verifica Gmail, pero si SQL falla el estado mostrara el error y
reintentara. No se adelanta el cursor cuando no se ha confirmado el guardado.

El panel actualiza el estado cada 10 segundos. `Actualizar historial` muestra los
ultimos 100 resultados guardados. Las herramientas manuales siguen disponibles
para diagnostico; no insertan datos en el historial automatico.

## Respuesta JSON

API local de desarrollo, sin conexion directa a TonysGo todavia:

- `GET /api/lector`: estado del lector, sin contraseña.
- `GET /api/notificaciones`: ultimos 100 correos procesados automaticamente.
- `GET /api/notificaciones/{id}`: resultado persistido de un correo (404 si no existe).

La respuesta incluye un ID estable, fecha de registro, `persistido: true`,
`pagoVerificado: false` y `datos` (importe, moneda, ordenante, fecha, motivo, etc.).
Los consumidores deben distinguir resultados extraidos, descartados y errores.
El listado es de consulta reciente, no una cola de entrega garantizada. No hay
webhook ni reenvio activo a TonysGo; el contrato autenticado de solicitudes y su
vinculacion con conductores sigue pendiente. Estos endpoints solo admiten loopback
y host local en Development. En Production estan bloqueados.

## Persistencia y controles

- Cursor por buzon/INBOX y UIDVALIDITY; si cambia UIDVALIDITY, se detiene el avance
  y se informa que requiere revision, sin resetearlo silenciosamente.
- Transaccion SQL para correo, evidencia, auditoria y cursor. El indice unico
  `(buzon/carpeta, UIDVALIDITY, UID)` evita reprocesar el mismo mensaje.
- Se almacena el JSON extraido y el hash del MIME; no el cuerpo MIME completo.
- Los avisos demasiado grandes o desaparecidos se omiten y auditan por UID.
- Autenticidad siempre `NoVerificada`. Yapeos quedan `RequiereRevision`; consumos
  se descartan. Transferencias requieren una muestra de su plantilla de recepcion.
- No se asignan conductores, no se acredita saldo y no se marca una evidencia como
  validada. Deduplicar mensajes no equivale a deduplicar transacciones bancarias.
- Diseñado para una instancia local. No publicar el panel/API mediante tuneles.

La migracion `AddAutomaticEmailResults` añade solamente la columna nullable
`ResultadoJson` a `CorreosProcesados`.
