# Probar Listener sin TonysGo

## Que reemplaza a la aplicacion

Editas un JSON como si TonysGo solicitara comprobar una recarga. El otro archivo,
`.eml`, representa un correo FICTICIO. El comando lee ambos, compara sus campos,
guarda la solicitud, el correo, la evidencia y la auditoria en una transaccion,
y devuelve JSON. Puedes consultar nuevamente el resultado desde la base.

Esta etapa usa la terminal. No hay que iniciar el servidor HTTP ni tener TonysGo
abierto. NO se ha implementado un endpoint de produccion ni autenticacion entre
servicios. Tampoco es necesario introducir la contrasena de Gmail.

## Importante

- `coincidenciaDatos: true` significa que coinciden los datos ficticios.
- `pagoVerificado` SIEMPRE es `false`; `estado` es `RequiereRevision`.
- Todas las filas de solicitud/correo/evidencia llevan `EsSimulacion = 1`.
- SQL impide marcar esas solicitudes como Verificada, sus correos como autenticos
  o sus evidencias como Validada/asignada. No se invoca a TonysGo ni se recarga saldo.
- El formato `X-Listener-Demo: v1` es nuestro, NO es el formato de BCP ni Yape.
  Las referencias de conductor/solicitud del correo son ficticias. Los avisos
  bancarios reales pueden no incluirlas. No prueban propiedad del pago.
- NO verifica SPF, DKIM, DMARC, vigencia temporal, reversos reales ni identidad.
  Las fechas fijas del ejemplo permiten repetirlo cualquier dia y no se usan
  para autorizar pagos. El texto From tampoco demuestra autenticidad.
- No se descarga correo de Gmail, no se recorren mensajes personales y no se
  persisten cuerpos ni adjuntos. El archivo fuente permanece donde tu lo guardaste.
- Un futuro lector/verificador real debe excluir `EsSimulacion = 1` siempre.

## Preparacion

Desde la carpeta Listener, con la instancia local `.\SQLEXPRESS` iniciada y la
conexion `ListenerDb` configurada en User Secrets:

```powershell
dotnet tool restore
dotnet ef database update --project src/Listener.Api --configuration Release -- --environment Development
```

La migracion `AddSimulationIsolation` agrega las marcas sin borrar datos previos.
No hay migracion automatica al iniciar el simulador.

## Caso que coincide

```powershell
dotnet run --project src/Listener.Api --configuration Release --no-launch-profile -- --simulate-payment --solicitud examples/simulacion/solicitud.json --correo examples/simulacion/correo.eml --environment Development
```

Resultado esperado: `referenciaExterna: DEMO-001`, `coincidenciaDatos: true`,
`diferencias: []`, `pagoVerificado: false`, `estado: RequiereRevision`.

## Caso con monto distinto

```powershell
dotnet run --project src/Listener.Api --configuration Release --no-launch-profile -- --simulate-payment --solicitud examples/simulacion/solicitud-monto-distinto.json --correo examples/simulacion/correo-monto-distinto.eml --environment Development
```

El JSON declara 30 soles y el correo informa 25: `coincidenciaDatos: false`,
`diferencias: ["MONTO"]`. La discrepancia NO rechaza automaticamente un pago real.

## Consultar el resultado persistido

```powershell
dotnet run --project src/Listener.Api --configuration Release --no-launch-profile -- --simulation-result DEMO-001 --environment Development
```

No requiere volver a pasar los archivos. Devuelve solo simulaciones.

## Crear tus entradas

1. Copia los archivos de ejemplo con otro nombre.
2. Usa una referencia nueva, por ejemplo `DEMO-003`, y cambia tanto
   `referenciaExterna` del JSON como `REFERENCIA_SOLICITUD` del correo si deben coincidir.
3. Cambia conductor, importe, moneda, operacion o cuenta receptora. Si quieres
   coincidencia, mantenlos iguales en ambos archivos.
4. En el correo puedes usar `DIRECCION: SALIDA` o `ESTADO: PENDIENTE`, `FALLIDO`
   o `REVERSADO` para probar casos que no representan un ingreso completado.
5. Ejecuta el comando con las rutas de tus nuevos archivos.

El JSON solo admite los siete campos del ejemplo; no acepta que el cliente mande
`estado`, `pagoVerificado` ni instrucciones. Limite JSON: 16 KiB. EML: 128 KiB,
texto plano sin adjuntos/HTML. Monto positivo hasta 1000000, con maximo dos decimales
y punto decimal. Monedas demo: PEN/USD. Proveedores demo: BCP/YAPE. Referencias:
letras ASCII, numeros, puntos, guiones o guiones bajos, hasta 100 caracteres.

Misma referencia + mismos datos + mismo archivo: devuelve el mismo resultado sin
duplicar filas. Misma referencia con datos o archivo diferentes: error de conflicto,
sin sobrescribir. Para un nuevo caso usa una referencia nueva. El simulador no
implementa deduplicacion bancaria entre referencias diferentes; esto NO es prueba
de seguridad suficiente para produccion.

El comando esta limitado a `--environment Development` y la base `ListenerDb` de
SQLEXPRESS local con autenticacion de Windows. No crea endpoints accesibles por red.
Las referencias DEMO son ayudas de organizacion; la separacion real es EsSimulacion.
El cursor ficticio usa `simulacion@listener.invalid` / `SIMULACION`, no el cursor IMAP
real. SQL `Uid` en estos correos es un contador local, no un identificador de Gmail.

Los codigos de salida: 0 = simulacion o consulta completada (incluso si no coincide),
1 = error. No se imprimen excepciones de SQL ni credenciales. Consulta el resultado
por referencia si una desconexion hizo incierto el resultado de la operacion.

## Siguiente etapa real

Analizar muestras autorizadas de avisos de ingresos de BCP/Yape, definir extractores
reales y comprobaciones de autenticidad/propiedad, luego exponer solicitudes y
consulta de resultados mediante API autenticada. El panel y el lector permanente
siguen pendientes.
