using System.Net;
using System.Net.Http.Json;
using Android.Content;
using TonysGo.Listener.Bbva;

namespace TonysGo.YapeListener.Android;

internal enum BbvaUploadOutcome { Complete, Retry, ConfigurationRequired, Rejected }

internal static class BbvaUploadSender
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<BbvaUploadOutcome> SendAsync(Context context, string id, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var store = BbvaHistory.Store(context);
            var entry = store.Find(id);
            if (entry is null || entry.Status == BbvaDeliveryStatus.StoredByListener) return BbvaUploadOutcome.Complete;
            if (entry.Status == BbvaDeliveryStatus.Rejected) return BbvaUploadOutcome.Rejected;
            if (entry.Status == BbvaDeliveryStatus.Retry && !store.IsDue(id)) return BbvaUploadOutcome.Retry;
            var settings = ListenerConfig.Load(context);
            if (!settings.IsValid)
            {
                store.SetStatus(id, BbvaDeliveryStatus.ConfigurationRequired, "El aviso está guardado. Vincula el teléfono para enviarlo.");
                return BbvaUploadOutcome.ConfigurationRequired;
            }
            var input = store.BindDevice(id, settings.DeviceId);
            store.SetStatus(id, BbvaDeliveryStatus.Sending, "Enviando a la web de Listener…");
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.Endpoint}/api/v1/bbva/notifications");
                request.Headers.Add("X-Listener-Api-Key", settings.ApiKey);
                request.Content = JsonContent.Create(input, BbvaDeviceJsonContext.Default.BbvaNotificationRequest);
                using var response = await Client.SendAsync(request, ct).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    var cleared = ListenerConfig.ClearPairing(context, settings.ApiKey);
                    store.SetStatus(id, cleared ? BbvaDeliveryStatus.ConfigurationRequired : BbvaDeliveryStatus.Retry,
                        cleared ? "Acceso revocado. Aviso guardado; vuelve a vincular el teléfono."
                            : "La vinculación cambió durante el envío. Se reintentará.");
                    return cleared ? BbvaUploadOutcome.ConfigurationRequired : BbvaUploadOutcome.Retry;
                }
                if (response.IsSuccessStatusCode)
                {
                    var receipt = await response.Content.ReadFromJsonAsync(BbvaDeviceJsonContext.Default.BbvaIngestResult, ct).ConfigureAwait(false);
                    if (receipt is null || receipt.Id == Guid.Empty || !receipt.EsAbono)
                    {
                        store.SetStatus(id, BbvaDeliveryStatus.Rejected, $"Respuesta sin abono reconocido: {receipt?.CodigoMotivo ?? "respuesta inválida"}.");
                        return BbvaUploadOutcome.Rejected;
                    }
                    store.SetStatus(id, BbvaDeliveryStatus.StoredByListener, $"Recibido en la web de Listener (HTTP {(int)response.StatusCode}). La recarga se valida en Tony’s Go.");
                    ListenerConfig.SetLastStatus(context, $"Abono recibido en la web a las {DateTimeOffset.Now:HH:mm:ss}.");
                    try { ListenerNotifications.Delivered(context, store.Find(id)!); }
                    catch (Exception e) { ListenerDebugLog.Warning(context, $"Actualización del aviso local: {e.GetType().Name}."); }
                    return BbvaUploadOutcome.Complete;
                }
                var retry = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                store.SetStatus(id, retry ? BbvaDeliveryStatus.Retry : BbvaDeliveryStatus.Rejected,
                    $"HTTP {(int)response.StatusCode}. " + (retry ? "Aviso guardado; se reintentará automáticamente." : "Aviso guardado; revisa la conexión con el servidor."));
                return retry ? BbvaUploadOutcome.Retry : BbvaUploadOutcome.Rejected;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            {
                store.SetStatus(id, BbvaDeliveryStatus.Retry, $"Sin confirmación: {exception.GetType().Name}. Aviso guardado para reintento.");
                ListenerDebugLog.Warning(context, $"Envío BBVA pendiente: {exception.GetType().Name}.");
                return BbvaUploadOutcome.Retry;
            }
        }
        finally { Gate.Release(); ListenerRuntimeService.RefreshNotice(); }
    }
}
