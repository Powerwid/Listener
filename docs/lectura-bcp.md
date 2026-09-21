# Lectura de correos BCP

El lector extrae datos reales de un EML o de Gmail mediante MailKit/MimeKit.
Es una lectura manual de una sola ejecucion: todavia no es un worker permanente,
no escribe en SQL Server ni responde a TonysGo. `pagoVerificado` siempre es false.
La simulacion existente sigue usando sus propios archivos y tablas de prueba.

## Analizar un EML local

Desde Listener, indicar la ruta absoluta del archivo:

```powershell
dotnet run --project src/Listener.Api --configuration Release --no-launch-profile -- --inspect-bcp-eml "C:\ruta\notificacion.eml"
```

No requiere Gmail ni SQL Server. Limite de 1 MiB; devuelve JSON en la consola.
El archivo no se copia al repositorio ni se guarda su contenido en la base.
El JSON puede contener datos personales; compartir solo versiones anonimizadas.

Plantillas basadas en los ejemplos proporcionados:

- Consumo Tarjeta de Debito: extrae importe, fecha, empresa y numero de operacion;
  estado `Descartado`, motivo `OPERACION_NO_CORRESPONDE_A_RECARGA`.
- Constancia de recepcion de Yapeo a celular BCP: extrae importe, ordenante y fecha;
  estado `RequiereRevision`. La muestra no contiene numero de operacion ni cuenta
  receptora; ambos se dejan en null, sin inventarlos ni usar el Message-ID como
  numero bancario.
- Pago de servicios: descartado como recarga. Otros formatos requieren revision.
- Transferencia mencionada en el asunto: `PLANTILLA_TRANSFERENCIA_PENDIENTE`.
  Todavia falta un EML real de transferencia recibida para implementar su extraccion.
  No se deduce que sea ingreso solo por contener la palabra transferencia.

Las fechas de estas plantillas se interpretan como hora de Peru (UTC-05).
Se admite septiembre/setiembre, AM/PM y soles con dos decimales. Si cambia la
plantilla o falta un campo, se requiere revision; nunca se aprueba por aproximacion.

## Leer Gmail una vez

```powershell
dotnet run --project src/Listener.Api --configuration Release --no-launch-profile -- --read-gmail-bcp
```

La cuenta propuesta es `Victorrmq35@gmail.com`; se puede introducir otra.
Ingresar en la terminal interactiva una **contrasena de aplicacion de esa cuenta**.
La de una cuenta anterior no sirve. No enviarla por chat ni agregarla al comando.
El proceso no la persiste. Para comprobar solo la conexion sigue disponible
`--test-gmail`.

Consulta INBOX, ultimos 30 dias, hasta 20 mensajes recientes cuyo From contenga
la direccion BCP. Despues el parser exige un unico remitente con direccion exacta
`notificaciones@notificacionesbcp.com.pe`. Ese filtro no demuestra autenticidad.
No consulta archivados, Spam o Papelera. Lee tanto vistos como no vistos sin cambiar
sus marcas. Abre la carpeta ReadOnly y usa TLS con validacion de certificados.
Limite total de conexion/lectura: 45 segundos. Al finalizar se desconecta.

El resultado incluye UID y UIDValidity para identificar el mensaje en esa carpeta,
pero esta etapa no persiste el cursor: repetir el comando vuelve a mostrar mensajes.
Un mensaje de mas de 1 MiB se omite. No descarga imagenes externas ni ejecuta HTML.

## Probar el filtrado de varios correos y obtener un JSON

El modo por lotes y Gmail entregan el mismo informe, separado en:

- `ingresosExtraidos`: yapeos de la plantilla conocida con campos consistentes;
  siguen en `RequiereRevision`, `pagoVerificado: false`.
- `descartados`: egresos (consumos, pagos de servicios) y remitentes no permitidos.
- `revision`: formatos desconocidos, transferencias pendientes de plantilla o datos
  incompletos/inconsistentes.
- `errores`: archivos no disponibles, mensajes demasiado grandes o ilegibles.
- `resumen`: cantidades de cada grupo; `coincidenciasFiltro` puede ser mayor que
  procesados si Gmail devuelve mas que el limite de 20 mensajes.

Se incluyen asunto, remitente, Message-ID y los campos extraidos. No se genera un
numero de operacion cuando no figura en el correo. En Gmail `referencia` es el UID;
en archivos locales es el nombre del archivo. Cada ejecucion es una nueva lectura;
este informe no identifica pagos duplicados entre mensajes ni asigna conductores.

Para probar los dos archivos entregados, ejecutar desde Listener:

```powershell
dotnet build src/Listener.Api --configuration Release
dotnet src/Listener.Api/bin/Release/net10.0/Listener.Api.dll --inspect-bcp-emls "C:\Users\Power\Downloads\Constancia de recepción de Yapeo a celular BCP - Servicio de Notificaciones BCP.eml" "C:\Users\Power\Downloads\Realizaste un consumo con tu Tarjeta de Débito BCP - Servicio de Notificaciones BCP.eml"
```

Admite de 1 a 20 rutas, con un limite de 1 MiB por archivo. Un archivo fallido queda
en errores y no impide informar los otros. Codigo de salida 1 si hay errores de
lectura; 0 si todos se analizaron, aunque sean descartados o requieran revision.

Para guardar el resultado de Gmail en un archivo local, tras compilar:

```powershell
dotnet src/Listener.Api/bin/Release/net10.0/Listener.Api.dll --read-gmail-bcp > resultado-gmail.json
```

Se pide correo y contrasena en la terminal (stderr); stdout contiene solamente el
JSON. No redirigir stdin ni combinar stderr con stdout. Usar el DLL compilado evita
que los mensajes de compilacion de `dotnet run` entren en el archivo. El archivo
contiene datos personales y solo se crea cuando tu rediriges la salida. Si falla la
conexion, el proceso termina con codigo distinto de cero y no emite un informe;
no tomar un archivo vacio como resultado correcto.

## Autenticidad y etapa siguiente

Ni el From ni un `Authentication-Results: ... pass` en un archivo demuestran por si
solos que el archivo sea autentico. Esta etapa siempre devuelve
`autenticidadCorreo: NoVerificada`, incluso al leer por IMAP: autenticar el acceso
al buzon no autentica al remitente del mensaje. Pendiente establecer la confianza
en las cabeceras del receptor y verificar alineacion del dominio, ademas de
vincular el buzon a la cuenta recaudadora y la evidencia a una solicitud unica.

Siguientes etapas: autenticacion y vinculacion, persistencia con cursor y control
de duplicados, worker con reconexion y contrato HTTP autenticado con TonysGo.
La recarga de saldo corresponde al backend TonysGo.
