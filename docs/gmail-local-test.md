# Prueba local de Gmail

> Función retirada: el proyecto ya no conecta con Gmail ni admite esta prueba.
> Consulta el [flujo actual de BBVA y el historial web](vinculacion-qr.md).
> Las instrucciones siguientes se conservan solo como referencia histórica.

Esta prueba manual no es el lector permanente ni valida pagos. No expone una ruta
HTTP. Abre Inbox en modo de solo lectura y muestra exclusivamente el numero de
mensajes, sin descargar cuerpos, enviar, borrar ni cambiar marcas de lectura.

## Requisitos

- Gmail personal propio con verificacion en dos pasos.
- Contrasena de aplicacion de Google, si las politicas de la cuenta lo permiten.
  No usar la contrasena habitual ni desactivar protecciones. Si esta opcion no
  esta disponible, configurar OAuth en una siguiente etapa.
- Acceso de red a `imap.gmail.com:993` y certificados TLS validos.

## Ejecutar desde PowerShell

Desde la carpeta Listener:

```powershell
dotnet run --project src/Listener.Api/Listener.Api.csproj --no-launch-profile -- --test-gmail
```

Confirma la direccion propuesta o escribe otra cuenta Gmail propia. Introduce la
contrasena de aplicacion en el campo oculto de la terminal y pulsa Enter. Se
aceptan los espacios con los que Google la presenta. Escape cancela la entrada.
No pongas la contrasena en el comando, archivos, capturas, chats ni logs.

La direccion propuesta ahora es `Victorrmq35@gmail.com`. La contrasena de
aplicacion debe pertenecer a esa cuenta. Para extraer datos de hasta 20 avisos BCP
recientes, consulta [lectura-bcp.md](lectura-bcp.md) y usa `--read-gmail-bcp`.

La credencial permanece en memoria durante el proceso; no se persiste. Al salir,
el servicio no sigue conectado y la siguiente prueba vuelve a pedirla. La memoria
administrada no garantiza borrado inmediato de todas sus copias. No usar volcados
de memoria ni capturas de terminal durante la prueba. Una contrasena de aplicacion
otorga mas acceso que solo lectura, aunque esta prueba solo abra Inbox en lectura.
Revoca la credencial en Google cuando termine su uso.

Resultados: 0 = conexion y lectura correctas; 1 = fallo de conexion; 2 = entrada
no valida o prueba cancelada. No se imprime la excepcion completa para no filtrar
datos. No se omite nunca la validacion de certificados. Limite de red: 45 segundos.

El comando habitual `dotnet run --project src/Listener.Api` sigue levantando la
API como antes. Esta prueba no requiere SQL Server.

## Pendiente para produccion

OAuth, almacenamiento protegido de autorizaciones, lector en segundo plano,
reconexion, seguimiento de mensajes procesados y verificacion de evidencias.

Referencias:

- https://support.google.com/accounts/answer/185833?hl=es
- https://github.com/jstedfast/MailKit/blob/master/FAQ.md
