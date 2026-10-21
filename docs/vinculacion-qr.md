# Vincular Listener mediante QR

La aplicación Android ya no pide nombre/identificador de dispositivo ni clave.
El administrador entra en la web de Listener, genera un QR y lo escanea desde
**Conexión → Escanear QR de vinculación** en el teléfono con BBVA Empresas.

## Preparar el servidor una sola vez

Desde la carpeta `src/Listener.Api` del proyecto, configura al administrador:

```powershell
dotnet run --configuration Release -- --configure-admin
```

El comando pide una contraseña de entre 8 y 256 caracteres dos veces, sin
mostrarla. El usuario inicial es `admin`. Se guarda solo su hash de ASP.NET
Identity en `App_Data/admin-login.json`, fuera de wwwroot y excluido de Git y
de la publicación. No existe una contraseña predeterminada ni un endpoint
público de creación de administradores.

Aplica las dos tablas nuevas de vinculación a la conexión `ListenerDb`:

```powershell
dotnet run --configuration Release -- --migrate-devices
```

Este comando aplica solamente las migraciones de `PairingDbContext`, con su
historial `__PairingMigrationsHistory`. No modifica el modelo de pagos ni las
tablas de AppTaxi. Las migraciones del Listener anterior siguen siendo necesarias
para almacenar notificaciones. En un servidor sin SDK también funcionan:

```powershell
dotnet Listener.Api.dll --configure-admin
dotnet Listener.Api.dll --migrate-devices
```

Ejecuta los comandos desde la carpeta de la API publicada para que usen el mismo
`App_Data`. La identidad del proceso debe poder escribir allí. Conserva esa carpeta
y sus claves de Data Protection entre publicaciones para mantener las sesiones.
Si el hosting no ofrece terminal, se puede generar un script SQL idempotente con:

```powershell
dotnet ef migrations script --context PairingDbContext --configuration Release --idempotent --output vinculacion.sql
```

Como alternativa al archivo de administración, el hosting puede proporcionar
`ListenerAdmin__Username` y `ListenerAdmin__PasswordHash` mediante su almacén de
secretos. El hash debe tener el formato de `PasswordHasher` de ASP.NET Identity;
no coloques una contraseña en texto plano en `PasswordHash`.

En producción configura la dirección que podrá abrir el teléfono:

```text
DevicePairing__PublicBaseUrl=https://tonysgo-001-site4.ftempurl.com
```

Se exige HTTPS y una URL raíz, sin usuario, contraseña, query ni fragmento.
En Development, si este valor está vacío, se usa la dirección local de la petición.
El APK Debug admite HTTP exclusivamente para localhost/127.0.0.1; Release exige HTTPS.

## Conectar el teléfono

1. Reinicia la API con los cambios y abre la raíz del sitio.
2. Inicia sesión como administrador, entra en **Teléfonos y QR** y pulsa **Generar QR de vinculación**.
3. Abre Android → **Conexión → Escanear QR de vinculación**.
4. Comprueba el servidor mostrado y pulsa **Vincular**.
5. Habilita el acceso a notificaciones y pulsa **Probar conexión con Listener**.

El QR vence en cinco minutos, se consume una sola vez y no incluye la credencial
permanente. Generar otro invalida el anterior. Un QR consumido cuya respuesta se
pierda requiere generar otro; no se recupera una credencial permanente desde la web.

El lector utiliza [Google Code Scanner](https://developers.google.com/ml-kit/vision/barcode-scanning/code-scanner):
requiere Servicios de Google Play y puede descargar su módulo la primera vez.
La aplicación no solicita permiso propio de cámara. El permiso de acceso a
notificaciones sigue requiriendo habilitación explícita desde Android.

Para una prueba local por USB, inicia la API con el perfil `http`, conecta Android
Debug y ejecuta `adb reverse tcp:5057 tcp:5057`. El QR local contiene localhost:5057;
la conexión funciona a través de ese puente. No edites identificadores o claves.

## Administración y credenciales

- La web muestra modelo del teléfono, fecha de vinculación, última conexión y
  estado. **Revocar acceso** bloquea sus siguientes peticiones; el teléfono
  conserva el historial y debe volver a escanear un QR.
- Cada instalación genera automáticamente un identificador estable. La identidad
  de las notificaciones existentes se conserva para los reintentos de una actualización.
- El servidor entrega una credencial aleatoria exclusiva para ese equipo y guarda
  solo su SHA-256. Volver a vincular la misma instalación rota la credencial.
- Android guarda la credencial cifrada con AES-GCM y una clave de Android Keystore;
  no aparece en formularios ni en el diagnóstico. Se mantiene `allowBackup=false`.
- El acceso web usa cookies HttpOnly/SameSite Strict, sesión de 30 minutos, tokens
  antiforgery y límite de intentos. Cambiar el hash administrativo invalida las
  sesiones previas. En producción las cookies exigen HTTPS.
- Las rutas BBVA ya no aceptan la clave global `CollectorApiKey`. Las rutas antiguas
  Yape la conservan únicamente para compatibilidad.

## Endpoints

| Método | Ruta | Autorización |
|---|---|---|
| POST | `/acceso` | Usuario y contraseña + antiforgery |
| GET | `/api/v1/admin/devices` | Sesión administrativa |
| POST | `/api/v1/admin/devices/pairings` | Sesión administrativa + antiforgery |
| DELETE | `/api/v1/admin/devices/{id}` | Sesión administrativa + antiforgery |
| POST | `/api/v1/devices/pair` | Token del QR válido + límite de intentos |
| GET | `/api/v1/bbva/connection` | Credencial del teléfono |
| POST | `/api/v1/bbva/notifications` | Credencial del teléfono y dispositivo correspondiente |

La emisión del QR devuelve una imagen PNG y su caducidad. El canje recibe token,
identificador de instalación, identidad interna de notificaciones y modelo del
teléfono; devuelve una credencial solo al teléfono. Ninguna respuesta de lista
administrativa contiene hashes, tokens o credenciales.

La vinculación no cambia la comparación de recargas: Listener continúa enviando
nombre completo y monto; la asignación y acreditación corresponden a AppTaxi.

## Historial web

Al entrar se abre `/historial`, la vista principal de los avisos guardados en
ListenerDb. Muestra nombre, monto, fecha en Perú, origen y estado de entrega.
El detalle permite leer el mensaje original. Hay búsqueda, filtros por origen,
estado y fechas, paginación de 25 registros y actualización automática cada 15 segundos.
La consulta no se limita a los últimos 200 avisos del teléfono ni a los últimos
100 del panel anterior; permite recorrer todo el historial guardado en el servidor.

Se incluyen BBVA Empresas y los registros anteriores de Yape y correo BCP.
El lector de correo fue retirado: ya no hay pantalla `/panel`, conexión Gmail,
consulta IMAP ni lectura automática en segundo plano. Los registros anteriores
permanecen disponibles para consulta. La navegación ofrece **Historial de avisos**
y **Teléfonos y QR**. Las páginas y consultas de administración requieren la misma
sesión, tanto localmente como en producción.
No se necesitan nuevas tablas o migraciones para esta vista.

Android conserva su historial local de apoyo; la web muestra los avisos que ya
recibió el servidor. Un aviso que siga únicamente en el teléfono no aparece aún
en la web. La consulta web no acredita recargas ni modifica los abonos.
