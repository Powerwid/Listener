using System.Net;
using System.Net.Http.Json;
using Android.Content;
using AndroidX.Work;
using TonysGo.Listener.Bbva;
using WorkResult = AndroidX.Work.ListenableWorker.Result;

namespace TonysGo.YapeListener.Android;

public sealed class UploadNotificationWorker : Worker
{
    public const string EventIdKey = "eventId";
    public const string PackageKey = "packageName";
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };

    public UploadNotificationWorker(Context context, WorkerParameters parameters) : base(context, parameters) { }

    public override WorkResult DoWork()
    {
        var context = ApplicationContext!;
        var eventId = InputData!.GetString(EventIdKey) ?? "";
        // Old Yape jobs must never be relabelled as BBVA after an app update.
        if (InputData.GetString(PackageKey) != BbvaNotificationParser.OfficialPackage) return WorkResult.InvokeFailure()!;
        try { return UploadAsync(eventId).GetAwaiter().GetResult(); }
        catch (Exception exception)
        {
            BbvaHistory.Store(context).SetStatus(eventId, BbvaDeliveryStatus.Retry,
                $"Sin confirmacion del servidor: {exception.GetType().Name}. Se reintentara.");
            ListenerDebugLog.Error(context, $"Error enviando BBVA {Short(eventId)}: {exception.GetType().Name}.");
            return WorkResult.InvokeRetry()!;
        }
    }

    private async Task<WorkResult> UploadAsync(string eventId)
    {
        var context = ApplicationContext!;
        var history = BbvaHistory.Store(context);
        if (history.Read().Any(x => x.Notification.NotificationKey == eventId && x.Status == BbvaDeliveryStatus.StoredByListener))
        {
            BbvaHistory.Uploads(context).Remove(eventId);
            return WorkResult.InvokeSuccess()!;
        }
        var settings = ListenerConfig.Load(context);
        if (!settings.IsValid)
        {
            history.SetStatus(eventId, BbvaDeliveryStatus.ConfigurationRequired, "Escanea el QR generado en la web de Listener para vincular este telefono.");
            return WorkResult.InvokeFailure()!;
        }

        var queued = BbvaHistory.Uploads(context).BindDevice(eventId, settings.DeviceId);
        var input = queued.Notification with { DeviceId = queued.UploadDeviceId! };
        history.SetStatus(eventId, BbvaDeliveryStatus.Sending, "Enviando al servidor Listener...");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.Endpoint}/api/v1/bbva/notifications");
        request.Headers.Add("X-Listener-Api-Key", settings.ApiKey);
        request.Content = JsonContent.Create(input, BbvaDeviceJsonContext.Default.BbvaNotificationRequest);
        using var response = await Client.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var cleared = ListenerConfig.ClearPairing(context, settings.ApiKey);
            history.SetStatus(eventId, cleared ? BbvaDeliveryStatus.ConfigurationRequired : BbvaDeliveryStatus.Retry,
                cleared ? "El acceso ya no esta activo. Escanea un nuevo QR desde Conexion."
                    : "La vinculacion se actualizo durante el envio. Se reintentara con la conexion nueva.");
            return cleared ? WorkResult.InvokeFailure()! : WorkResult.InvokeRetry()!;
        }
        if (response.IsSuccessStatusCode)
        {
            var receipt = await response.Content.ReadFromJsonAsync(BbvaDeviceJsonContext.Default.BbvaIngestResult).ConfigureAwait(false);
            if (receipt is null || receipt.Id == Guid.Empty || !receipt.EsAbono)
            {
                history.SetStatus(eventId, BbvaDeliveryStatus.Rejected,
                    $"Listener no reconocio el abono: {receipt?.CodigoMotivo ?? "respuesta invalida"}.");
                return WorkResult.InvokeFailure()!;
            }
            history.SetStatus(eventId, BbvaDeliveryStatus.StoredByListener,
                $"Listener recibio el abono (HTTP {(int)response.StatusCode}). La recarga se valida en Tony's Go.");
            BbvaHistory.Uploads(context).Remove(eventId);
            ListenerConfig.SetLastStatus(context, $"Abono BBVA recibido por Listener a las {DateTimeOffset.Now:HH:mm:ss}.");
            return WorkResult.InvokeSuccess()!;
        }

        var retry = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)response.StatusCode >= 500;
        history.SetStatus(eventId, retry ? BbvaDeliveryStatus.Retry : BbvaDeliveryStatus.Rejected,
            $"Listener respondio HTTP {(int)response.StatusCode}. " + (retry ? "Se reintentara automaticamente." : "Comprueba la vinculacion y pulsa Reintentar envios."));
        ListenerDebugLog.Warning(context, $"Envio BBVA {Short(eventId)}: HTTP {(int)response.StatusCode}.");
        return retry ? WorkResult.InvokeRetry()! : WorkResult.InvokeFailure()!;
    }

    private static string Short(string value) => value[..Math.Min(value.Length, 12)];
}
