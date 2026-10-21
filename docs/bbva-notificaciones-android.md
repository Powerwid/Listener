# BBVA Empresas: captura, historial y entrega de abonos

El flujo BBVA no solicita códigos de Yape ni verifica recargas. Android captura
el aviso; Listener.Api almacena y reenvía nombre completo e importe; AppTaxi.Api
será responsable de identificar al usuario y acreditar su cartera.

## Origen y formato admitido

Solo se escucha `com.bbva.GEMA.global`, identificador de la aplicación
[BBVA Empresas publicada por BBVA](https://play.google.com/store/apps/details?id=com.bbva.GEMA.global).
El nombre visible de una aplicación no se utiliza para permitirla.

Muestra utilizada:

```text
Cobro con QR
VICTOR RAUL MAMANI QUEA te hizo un pago de S/ 20.0
```

Se extraen `VICTOR RAUL MAMANI QUEA` y el decimal `20.00`. Se admiten importes
enteros, uno o dos decimales y miles con coma. No se inventa un código de
seguridad ni un número de operación bancaria. Textos incompletos, contradictorios
o de otras operaciones no se reenvían como abonos. La muestra no identifica si
el emisor pagó desde Yape o Plin.

## Aplicación Android

Se conserva el identificador `com.tonysgo.yapelistener` y el componente de acceso
a notificaciones para actualizar la instalación existente. El nombre visible
pasa a **Tony's Go BBVA Listener**. Esta versión captura únicamente BBVA Empresas.

- **Historial BBVA** muestra los últimos 200 avisos BBVA, incluidos los que no
  tienen un abono reconocible. Cada fila muestra nombre, importe, fecha y estado.
- Al tocar una fila se abre el texto original, el texto ampliado y los detalles
  de captura y envío. Los avisos que no son cobros permanecen solo en el teléfono.
- **Conexión** permite escanear el QR de la web para guardar automáticamente el
  servidor y la credencial, abrir el permiso de notificaciones y probar conexión.
  Consulta [la preparación de la vinculación QR](vinculacion-qr.md).
- Limpiar el diagnóstico no borra el historial. Este persiste al cerrar o reiniciar
  la aplicación, dentro de su almacenamiento privado; `allowBackup` permanece desactivado.
- **Reintentar envíos pendientes** vuelve a programar los abonos no recibidos por
  Listener. Completar la vinculación y reconectar el servicio también recupera la cola.

Estados: pendiente, enviando, recibido en Listener, esperando reintento, rechazado,
falta configurar y solo historial. **Recibido en Listener no significa recarga
acreditada ni entrega ya confirmada por AppTaxi.**

La cola conserva su propia copia de cada mensaje hasta recibir confirmación del
servidor. No depende del límite de 200 filas del historial ni del límite de datos
de WorkManager. HTTP 408/429/5xx y errores de conexión se reintentan; otros 4xx
quedan visibles para corregir configuración y reintentar manualmente.

## API y configuración

| Método | Ruta | Uso |
|---|---|---|
| GET | `/api/v1/bbva/connection` | Comprobar conexión y clave del colector |
| POST | `/api/v1/bbva/notifications` | Recibir y almacenar el aviso Android |

Ambas rutas requieren `X-Listener-Api-Key` con la credencial exclusiva entregada
al teléfono al escanear el QR. El POST recibe `notificationKey`, `deviceId`,
`packageName`, `postedAt`, `title`, `text` y `bigText`. La API vuelve a extraer los
datos del aviso y no compara conductores, nombres de usuarios o solicitudes.

Se reutilizan `CorreosProcesados` y `EventosAuditoria` con extractor
`android-bbva-v1`; el almacenamiento de notificaciones no requiere cambiar esas
tablas. La vinculación añade dos tablas independientes mediante `PairingDbContext`.
Los reenvíos del mismo
evento y dispositivo devuelven el mismo ID persistido. La identidad Android usa
la clave del aviso y su fecha, no el texto que puede cambiar al expandirse. Dos
avisos distintos con el mismo nombre e importe se conservan como eventos distintos.
El identificador del dispositivo se fija en el primer intento de envío para
que cambiar la configuración no transforme un reintento en otro evento.

Para habilitar el reenvío a TonysGo:

```text
TonysGo__AbonosUrl=https://api.tonysgo.pe/api/internal/bbva/abonos
TonysGo__CallbackApiKey=<clave compartida de al menos 32 caracteres>
```

La URL es un ejemplo del contrato pendiente de implementar en AppTaxi.Api;
este cambio solo modifica Listener. Se exige HTTPS, salvo HTTP de loopback en
Development. **AbonosUrl es independiente del callback antiguo de aprobaciones
Yape y no usa ese callback como alternativa.** Mientras esté vacía, los abonos
se conservan pendientes en ListenerDb.

Listener entrega un POST con `X-Listener-Api-Key` y este formato:

```json
{
  "eventoId": "8a53bfbc-a37d-4935-b14e-c529fe827b96",
  "nombreCompleto": "VICTOR RAUL MAMANI QUEA",
  "monto": 20.00,
  "moneda": "PEN",
  "fechaNotificacion": "2026-10-19T11:29:00-05:00",
  "origen": "BBVA_EMPRESAS"
}
```

El evento es un **abono recibido**, sin conductor asignado, solicitud de recarga
ni aprobación. El receptor debe guardar antes de responder 2xx y deduplicar por
`eventoId`: una respuesta perdida puede causar reentrega del mismo evento.
Listener marca entrega solo tras un 2xx; ante errores espera al menos un minuto
antes de reintentar ese abono. La API de TonysGo decidirá las reglas de coincidencia,
vigencia, homónimos y consumo único del abono.

Las rutas antiguas Yape siguen disponibles para compatibilidad; no intervienen
en la captura, persistencia o entrega de BBVA. AppTaxi y su pantalla móvil no se
modifican en este cambio.

## Verificación local

```powershell
dotnet test tests/Listener.Tests/Listener.Tests.csproj
dotnet build android-listener/TonysGo.YapeListener.Android.csproj
```

Para comprobar recepción real se deben actualizar tanto Listener.Api como el APK,
vincular el teléfono por QR y habilitar acceso a notificaciones en el
teléfono que tiene BBVA Empresas. Un servidor con la versión anterior devolverá
404 en las rutas BBVA; el historial mostrará el rechazo.
