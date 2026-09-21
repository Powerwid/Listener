# Base de datos de Listener

## Alcance

SQL Server, base independiente `ListenerDb`, cliente unico TonysGo. No hay tablas
de conductores, saldos, viajes ni acceso cruzado a AppTaxi. Cinco tablas de negocio:

- SolicitudesVerificacion: datos declarados y referencia externa unica de TonysGo.
- EstadoSincronizacionCorreo: cursor por buzon/carpeta, sin credenciales.
- CorreosProcesados: identidad IMAP, autenticidad y estado de procesamiento.
- EvidenciasPago: datos extraidos y asignacion opcional a una solicitud.
- EventosAuditoria: decisiones y acciones, sin mensajes completos ni secretos.

EF agrega `__EFMigrationsHistory` para registrar migraciones. No es una tabla de
negocio. No hay seeds con correos, pagos o usuarios reales.

La migracion `AddSimulationIsolation` incorpora `EsSimulacion` en solicitudes,
correos y evidencias, y `ReferenciaSolicitudExtraida` en evidencias. El simulador
manual puede crear casos ficticios marcados, nunca pagos verificados. Consulta
`docs/simulacion.md` para crear entradas sin conectar TonysGo. Los lectores y
verificadores reales deberan excluir esos registros.

## Configuracion local

Instancia: `.\SQLEXPRESS`, autenticacion de Windows. La conexion esta en User
Secrets del proyecto, no en appsettings ni en el repositorio. User Secrets no
cifra la informacion: es solo una ayuda de desarrollo, no un vault de produccion.

```powershell
dotnet tool restore
dotnet user-secrets set "ConnectionStrings:ListenerDb" "Server=.\SQLEXPRESS;Database=ListenerDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15" --project src/Listener.Api
```

El certificado se acepta sin validar su confianza SOLO en esta conexion local,
como en la configuracion de SSMS del desarrollador. Produccion requiere certificado
valido y `TrustServerCertificate=False`. No copiar credenciales de TonysGo.

Para ejecutar la API y cargar User Secrets:

```powershell
dotnet run --project src/Listener.Api -- --environment Development
```

La prueba `--test-gmail` sigue siendo independiente y no necesita SQL Server.

## Migraciones

EF Core y dotnet-ef estan fijados a 10.0.11. La migracion inicial ya forma parte del
proyecto. No volver a crear otra InitialCreate.

```powershell
dotnet ef migrations list --project src/Listener.Api --configuration Release -- --environment Development
dotnet ef database update --project src/Listener.Api --configuration Release -- --environment Development
dotnet ef migrations has-pending-model-changes --project src/Listener.Api --configuration Release -- --environment Development
```

Para un cambio futuro, crear una migracion con nombre descriptivo, revisar el SQL
y entonces aplicarla. Se usa Release para evitar bloquear la compilacion Debug si
la API esta abierta; no cambia la base destino. El entorno si controla de donde
se obtiene la conexion. No hay `EnsureCreated`, migraciones automaticas al iniciar
ni endpoints que permitan modificar el esquema.

En produccion, revisar scripts/bundles y usar una identidad de despliegue separada
con permisos de esquema. La identidad habitual debe tener permisos limitados de
datos; para auditoria, limitar modificaciones/borrado segun la politica acordada.

## Garantias y limites

- `decimal(18,2)` para importes y `datetimeoffset` para instantes (usar UTC).
- `rowversion` evita sobreescrituras silenciosas de entidades mutables.
- Unica referencia externa por solicitud; no cambiar silenciosamente sus datos
  si TonysGo reenvia una referencia con otro monto o conductor.
- Unico correo por cursor/UIDVALIDITY/UID. Ante cambio de UIDVALIDITY el lector
  debe reiniciar el cursor, conservando los correos historicos; nunca asumir que
  el UID anterior sigue siendo valido.
- Una evidencia puede asignarse a una solicitud y una solicitud a una evidencia.
  No se implementan pagos parciales ni pagos agrupados.
- Solo las evidencias `Validada` reservan la identidad de operacion mediante un
  indice unico (proveedor + cuenta receptora + numero). Un correo no autentico
  no debe bloquear una operacion real. La identidad exacta y su normalizacion
  deben confirmarse con avisos reales; distintos avisos BCP/Yape del MISMO pago
  necesitan una identidad canonica para evitar acreditar dos veces.
- Una evidencia asignada no se puede degradar de estado sin tratamiento explicito
  de su asignacion. Reversos y correcciones necesitaran un flujo auditado.
- Las restricciones NO comprueban por si solas la autenticidad del correo,
  propiedad del pago, coincidencia de conductor/monto ni transiciones de negocio.
  El futuro verificador debe hacerlo y guardar decision, asignacion y auditoria
  en una transaccion. No hay todavia endpoints de aprobacion ni saldo.
- La API usa reintentos SQL: las transacciones explicitas de negocio deben
  ejecutarse mediante la estrategia de ejecucion de EF, con idempotencia ante
  resultados de commit inciertos. No reintentar acreditaciones ciegamente.
- No se incluye outbox: primero debe elegirse consulta de resultado por API o
  webhook. Si se elige webhook se agregara una migracion para notificaciones.
- Retencion y almacenamiento protegido de MIME estan pendientes. No guardar
  cuerpos, adjuntos, contrasenas o tokens en auditoria. No mostrar MIME como HTML
  sin sanitizar en el futuro panel.

## Pruebas

```powershell
dotnet test Listener.sln --configuration Release
```

Las pruebas locales de SQL estan desactivadas por defecto. Para ejecutarlas contra
la instancia local indicada, DESPUES de aplicar la migracion:

```powershell
$env:LISTENER_RUN_SQL_TESTS = "1"
dotnet test Listener.sln --configuration Release
Remove-Item Env:LISTENER_RUN_SQL_TESTS
```

Todas sus escrituras usan datos sinteticos y se revierten mediante transacciones.
No crean, borran ni migran bases. La conexion esta limitada en el codigo de pruebas
a `.\SQLEXPRESS` / `ListenerDb`; no sustituirla por produccion.
