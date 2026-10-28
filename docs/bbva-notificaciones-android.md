# BBVA Empresas: captura, historial y entrega de abonos

Android captura el aviso y lo envía a Listener.Api. Listener valida el monto y
acumula los pagos del mismo nombre sin límite de tiempo. Por cada notificación
reconocida envía a la API de Tony’s Go un resultado: correcto o monto insuficiente.
Tony’s Go recibe y guarda esos datos; Listener no acredita una cartera ni identifica
al conductor. No se solicita código de Yape.

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
datos del aviso, acumula por nombre completo y valida el mínimo de S/10. No
compara conductores o solicitudes de recarga. El Android existente continúa enviando el mismo JSON.

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

## Validación, acumulación y envío de resultados

Se conserva la conexión existente: `TonysGo:AbonosUrl` y `TonysGo:CallbackApiKey`.
No se añade otra URL de avisos ni una consulta desde Tony’s Go. El worker entrega
un único resultado lógico por notificación al mismo endpoint configurado.

Reglas:

1. Un pago nuevo se suma al pendiente del mismo nombre completo. Se normalizan
   mayúsculas y espacios; nombres diferentes conservan acumulados separados.
2. Si el total es menor de S/10, el resultado tiene `correcto: false`, el recibido,
   el acumulado y el faltante. El pendiente queda guardado sin vencimiento.
3. Al alcanzar o superar S/10, el resultado tiene `correcto: true` por el total.
   Ese total se cierra y los pagos posteriores empiezan una nueva acumulación.
4. Una notificación reenviada devuelve el mismo resultado guardado y no suma otra vez.
5. Los reintentos de entrega usan el mismo `eventoId` y los mismos datos. Una
   respuesta posterior del mismo nombre espera la confirmación de las anteriores;
   un fallo de un nombre no impide entregar resultados de otros nombres.

Ejemplo: S/5 y después S/7, incluso en otro día, producen primero un resultado
insuficiente de S/5 y luego uno correcto de S/12. Un siguiente pago de S/4 comienza
otro acumulado de S/4. Si existen S/5 pendientes y el siguiente pago es S/20, el
resultado correcto es S/25. La regla también se aplica a los cobros QR pagados
mediante Plin: el mensaje de BBVA no distingue la billetera de origen.

Primer resultado, por S/5:

```json
{
  "eventoId": "8a53bfbc-a37d-4935-b14e-c529fe827b96",
  "nombreCompleto": "PERSONA PRUEBA",
  "monto": 5.00,
  "moneda": "PEN",
  "fechaNotificacion": "2026-10-28T10:00:00-05:00",
  "origen": "BBVA_EMPRESAS",
  "acumulacionId": "ff793327-34b8-429c-92eb-ce42ca3792cb",
  "secuencia": 1,
  "correcto": false,
  "montoAcumulado": 5.00,
  "montoFaltante": 5.00
}
```

Segundo resultado, al recibir otros S/7:

```json
{
  "eventoId": "91b348ff-759c-45fd-a9b5-3097d7e117b1",
  "nombreCompleto": "PERSONA PRUEBA",
  "monto": 7.00,
  "moneda": "PEN",
  "fechaNotificacion": "2026-10-28T10:05:00-05:00",
  "origen": "BBVA_EMPRESAS",
  "acumulacionId": "ff793327-34b8-429c-92eb-ce42ca3792cb",
  "secuencia": 2,
  "correcto": true,
  "montoAcumulado": 12.00,
  "montoFaltante": 0.00
}
```

El JSON enviado a Tony’s Go conserva los seis campos originales: `eventoId`,
`nombreCompleto`, `monto`, `moneda`, `fechaNotificacion` y `origen`. Añade
`acumulacionId`, `secuencia`, `correcto`, `montoAcumulado` y `montoFaltante`:
once campos tanto para el resultado correcto como para el insuficiente.
`monto` mantiene su significado original: el importe recibido en esa notificación.
No se añade `montoRecibido`, que duplicaría ese dato.
El mínimo de S/10 se valida dentro de Listener. Los códigos y los textos para
su historial quedan internos; no se envían `montoMinimo`, `codigo` ni `mensaje`.
La API receptora debe entender ambas respuestas, guardar antes de devolver 2xx
y aceptar repetidos por `eventoId` devolviendo 2xx sin duplicar efectos.
`acumulacionId` relaciona los pagos de un mismo grupo. `secuencia` es creciente
por nombre normalizado, no se reinicia al cerrar un grupo y conserva su valor en
los reintentos. Permite reconocer un resultado antiguo al actualizar el estado
mostrado, sin reemplazar uno posterior; cada evento único se guarda por separado.
Si `correcto` es verdadero,
el importe total validado es `montoAcumulado`. `correcto` significa que el monto cumple el mínimo; la comparación
con la cuenta del conductor y cualquier acreditación siguen perteneciendo a Tony’s Go.
La implementación receptora de AppTaxi no se modifica en este repositorio.

El resultado se guarda de forma inmutable junto con el evento. El saldo pendiente
se mantiene en `BbvaPaymentBalances`, con una búsqueda por nombre normalizado;
no se vuelve a sumar todo el historial en cada pago. `BbvaPaymentValidations`
reserva una sola validación por evento y una secuencia por nombre. La captura,
la suma y la reserva se confirman en la misma transacción serializable. Los saldos
sobreviven a reinicios del servidor. Las entregas antiguas ya confirmadas no se
vuelven a acumular; las que seguían sin entregar se incorporan una sola vez.

La recepción Android mantiene el contrato anterior y añade `validacion` con el
resultado. `esAbono=true` sigue confirmando que se recibió un pago reconocido,
aunque su validación sea insuficiente. El colector actual ignora los campos
adicionales; no requiere un nuevo APK. La web conserva el monto de cada pago y
muestra por separado el acumulado, el faltante y la entrega de su respuesta.

Antes de desplegar Listener.Api, aplica la migración de las dos tablas nuevas:

```powershell
dotnet ef database update --project src/Listener.Api/Listener.Api.csproj --context ListenerDbContext
```

No se cambia el esquema de vinculación QR. La URL debe seguir usando HTTPS,
salvo loopback HTTP en Development. Una URL sin configurar conserva las
respuestas pendientes; los errores de red se reintentan con espera.

Las rutas antiguas Yape siguen disponibles para compatibilidad; no intervienen
en la captura, persistencia o entrega de BBVA. AppTaxi y su pantalla móvil no se
modifican en este cambio.

## Reportes y exportación a Excel

La web incluye **Reportes** en `/reportes`, con filtros Desde, Hasta y Estado.
El historial visible y los reportes trabajan con BBVA Empresas por defecto, sin
selector de origen. Los registros históricos de otras fuentes se conservan en
la base de datos; el JSON enviado a Tony’s Go mantiene `origen: "BBVA_EMPRESAS"`.

`GET /api/v1/admin/reports` devuelve la vista previa paginada y
`GET /api/v1/admin/reports/excel` descarga el `.xlsx`. Ambas rutas requieren la
sesión administrativa y aplican los mismos filtros. El reporte siempre fuerza
BBVA, incluso si se intenta enviar otro origen por la URL. Las fechas se aplican
al aviso en hora de Perú; Hasta incluye el día completo.

El Excel contiene fecha, nombre, monto recibido, acumulado, faltante, validación
y estado de entrega. Las fechas y los importes son celdas numéricas, con encabezados
inmovilizados y filtros de Excel. El total recibido suma pagos individuales y
excluye los acumulados para no contar dinero dos veces. Los nombres se escriben
como texto, sin fórmulas. La exportación no modifica pagos ni envíos.

Se exportan todos los resultados filtrados, no solo los 25 de la página visible.
El máximo es de 50 000 registros por archivo: si se supera, se solicita reducir
las fechas y no se entrega un archivo incompleto. Este módulo no requiere una
migración nueva ni actualizar el APK.

## Verificación local

```powershell
dotnet test tests/Listener.Tests/Listener.Tests.csproj
dotnet build android-listener/TonysGo.YapeListener.Android.csproj
```

Para comprobar recepción real se deben actualizar tanto Listener.Api como el APK,
vincular el teléfono por QR y habilitar acceso a notificaciones en el
teléfono que tiene BBVA Empresas. Un servidor con la versión anterior devolverá
404 en las rutas BBVA; el historial mostrará el rechazo.
