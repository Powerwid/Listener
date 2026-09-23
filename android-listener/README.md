# Tony's Go Yape Listener para Android

Aplicacion Android nativa escrita en C# y .NET. Se instala en el mismo telefono
que recibe los pagos de Yape, escucha exclusivamente las notificaciones del
paquete oficial `com.bcp.innovacxion.yapeapp` y las envia al microservicio
Listener con WorkManager y reintentos persistentes.

## Ejecutar desde Visual Studio

1. Abre `Listener.sln` o `TonysGo.YapeListener.Android.csproj`.
2. Selecciona `TonysGo.YapeListener.Android` como proyecto de inicio.
3. Activa depuracion USB en el telefono y conectalo con un cable de datos.
4. Acepta la huella RSA en el telefono.
5. Selecciona el telefono Android en la barra de Visual Studio y pulsa `F5`.
6. Configura la URL HTTPS de Listener, el identificador del equipo y la misma
   clave definida en `ListenerSecurity:CollectorApiKey`.
7. Pulsa **Abrir acceso a notificaciones** y habilita el listener.

El proyecto requiere Android 8.0 (API 26) o posterior y .NET 10 con la carga de
trabajo Android. No requiere Android Studio ni Flutter.

Consulta `../docs/yape-notificaciones-android.md` para el flujo completo.

## Prueba local por USB

La compilacion Debug permite exclusivamente HTTP local para usar el puente de
ADB. Con el telefono conectado ejecuta:

```powershell
adb reverse tcp:5057 tcp:5057
```

Inicia `Listener.Api` con el perfil `http` y escribe estos datos en la app:

- URL: `http://127.0.0.1:5057`
- Dispositivo: `yape-caja-01`
- Clave: el valor local de `ListenerSecurity:CollectorApiKey`

`127.0.0.1` funciona en este caso porque ADB reenvia el puerto del telefono al
puerto 5057 de la computadora. La compilacion Release sigue exigiendo HTTPS.

El proyecto se abre directamente en Android Studio. No incluye claves ni URL de
produccion dentro del APK; se configuran en la pantalla local del equipo dedicado.
