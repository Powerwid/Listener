using System.Net.Http.Json;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Gms.Tasks;
using Android.Gms.Common.ModuleInstall;
using TonysGo.Listener.Pairing;
using Xamarin.Google.MLKit.Vision.CodeScanner;
using Xamarin.Google.MLKit.Vision.Barcode.Common;

namespace TonysGo.YapeListener.Android;

internal static class DevicePairingClient
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };

    public static async System.Threading.Tasks.Task<string?> ScanAsync(Activity activity)
    {
        var options = new GmsBarcodeScannerOptions.Builder().SetBarcodeFormats(Barcode.FormatQrCode).EnableAutoZoom().Build()!;
        var scanner = GmsBarcodeScanning.GetClient(activity, options);
        // Sideloaded APKs do not get Play Store's install-time barcode_ui download.
        var modules = ModuleInstall.GetClient(activity);
        var availability = await GoogleTaskAsync(modules.AreModulesAvailable(scanner)!);
        if ((availability as ModuleAvailabilityResponse)?.AreModulesAvailable() != true)
        {
            var request = ModuleInstallRequest.NewBuilder()!.AddApi(scanner)!.Build()!;
            await GoogleTaskAsync(modules.InstallModules(request)!);
            var available = false;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await System.Threading.Tasks.Task.Delay(1000);
                availability = await GoogleTaskAsync(modules.AreModulesAvailable(scanner)!);
                if ((availability as ModuleAvailabilityResponse)?.AreModulesAvailable() == true) { available = true; break; }
            }
            if (!available) throw new InvalidOperationException("El lector QR se esta descargando. Mantén la conexion a Internet e intenta escanear de nuevo en unos momentos.");
        }
        if (activity.IsFinishing || activity.IsDestroyed) return null;
        return (await GoogleTaskAsync(scanner.StartScan()!) as Barcode)?.RawValue;
    }

    public static async System.Threading.Tasks.Task PairAsync(Context context, DevicePairingQr qr)
    {
        var settings = ListenerConfig.Load(context);
        var model = $"{Build.Manufacturer} {Build.Model}".Trim();
        if (model.Length == 0) model = "Telefono Android";
        var input = new DevicePairingRequest(qr.Token, ListenerConfig.InstallationId(context), settings.DeviceId, model[..Math.Min(120, model.Length)]);
        using var response = await Client.PostAsJsonAsync($"{qr.ServerUrl}/api/v1/devices/pair", input,
            DevicePairingJsonContext.Default.DevicePairingRequest);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync(DevicePairingJsonContext.Default.DevicePairingError);
            throw new InvalidOperationException(error?.Error ?? $"No se pudo vincular (HTTP {(int)response.StatusCode}).");
        }
        var result = await response.Content.ReadFromJsonAsync(DevicePairingJsonContext.Default.DevicePairingResponse);
        if (result is null || result.Credential is not { Length: 80 } || !result.Credential.StartsWith("dvc_", StringComparison.Ordinal)
            || result.NotificationDeviceId != settings.DeviceId) throw new InvalidOperationException("El servidor devolvio una vinculacion invalida.");
        ListenerConfig.SavePairing(context, qr.ServerUrl, result.Credential);
        BbvaHistory.RetryPending(context);
    }

    private static System.Threading.Tasks.Task<Java.Lang.Object?> GoogleTaskAsync(global::Android.Gms.Tasks.Task task)
    {
        var completion = new TaskCompletionSource<Java.Lang.Object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new CompletionListener(completion);
        task.AddOnSuccessListener(listener)!.AddOnFailureListener(listener)!.AddOnCanceledListener(listener);
        return completion.Task;
    }
    private sealed class CompletionListener(TaskCompletionSource<Java.Lang.Object?> completion) : Java.Lang.Object,
        IOnSuccessListener, IOnFailureListener, IOnCanceledListener
    {
        public void OnSuccess(Java.Lang.Object? result) => completion.TrySetResult(result);
        public void OnFailure(Java.Lang.Exception exception) => completion.TrySetException(new InvalidOperationException(
            "No se pudo abrir el lector QR. Comprueba la conexion y que Servicios de Google Play este actualizado.", exception));
        public void OnCanceled() => completion.TrySetResult(null);
    }
}
