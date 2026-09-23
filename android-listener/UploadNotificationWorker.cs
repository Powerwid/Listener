using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Android.Content;
using Android.Util;
using AndroidX.Work;
using WorkResult = AndroidX.Work.ListenableWorker.Result;

namespace TonysGo.YapeListener.Android;

public sealed class UploadNotificationWorker : Worker
{
    public const string EventIdKey = "eventId";
    public const string PostedAtKey = "postedAt";
    public const string TitleKey = "title";
    public const string TextKey = "text";
    public const string BigTextKey = "bigText";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public UploadNotificationWorker(Context context, WorkerParameters workerParameters)
        : base(context, workerParameters)
    {
    }

    public override WorkResult DoWork()
    {
        try
        {
            return UploadAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            var message = $"Error de envio a las {DateTimeOffset.Now:HH:mm:ss}: {ex.GetType().Name}: {ex.Message}";
            ListenerConfig.SetLastStatus(ApplicationContext!, message);
            ListenerDebugLog.Error(ApplicationContext!, message);
            Log.Error("TonysGoYapeListener", ex.ToString());
            return WorkResult.InvokeRetry()!;
        }
    }

    private async Task<WorkResult> UploadAsync()
    {
        var settings = ListenerConfig.Load(ApplicationContext!);
        if (!settings.IsValid)
        {
            ListenerConfig.SetLastStatus(ApplicationContext!, "No se envio: configuracion local invalida.");
            ListenerDebugLog.Warning(ApplicationContext!, "WorkManager cancelo el envio: configuracion local invalida.");
            return WorkResult.InvokeFailure()!;
        }

        var eventId = InputData!.GetString(EventIdKey) ?? "sin-id";
        ListenerDebugLog.Info(ApplicationContext!,
            $"Intentando enviar evento={Short(eventId)} al host={new Uri(settings.Endpoint).Host}.");
        ListenerConfig.SetLastStatus(ApplicationContext!,
            $"Enviando notificacion a las {DateTimeOffset.Now:HH:mm:ss}...");

        var inputData = InputData;
        var payload = JsonSerializer.Serialize(new
        {
            notificationKey = inputData.GetString(EventIdKey),
            deviceId = settings.DeviceId,
            packageName = YapeNotificationListenerService.YapePackage,
            postedAt = DateTimeOffset.FromUnixTimeMilliseconds(inputData.GetLong(PostedAtKey, 0)),
            title = inputData.GetString(TitleKey),
            text = inputData.GetString(TextKey),
            bigText = inputData.GetString(BigTextKey)
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{settings.Endpoint}/api/v1/yape/notifications");
        request.Headers.Add("X-Listener-Api-Key", settings.ApiKey);
        request.Content = new StringContent(payload, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };

        using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            ListenerDebugLog.Info(ApplicationContext!,
                $"Servidor acepto evento={Short(eventId)} con HTTP {(int)response.StatusCode}.");
            ListenerConfig.SetLastStatus(ApplicationContext!,
                $"Notificacion enviada correctamente a las {DateTimeOffset.Now:HH:mm:ss} (HTTP {(int)response.StatusCode}).");
            return WorkResult.InvokeSuccess()!;
        }

        ListenerDebugLog.Warning(ApplicationContext!,
            $"Servidor rechazo evento={Short(eventId)} con HTTP {(int)response.StatusCode}.");
        ListenerConfig.SetLastStatus(ApplicationContext!,
            $"Servidor respondio HTTP {(int)response.StatusCode} a las {DateTimeOffset.Now:HH:mm:ss}.");
        return response.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
            ? WorkResult.InvokeFailure()!
            : WorkResult.InvokeRetry()!;
    }

    private static string Short(string value) => value.Length <= 12 ? value : value[..12];
}
