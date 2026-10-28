using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;

namespace TonysGo.YapeListener.Android;

public sealed partial class MainActivity
{
    private void ReactivateReading()
    {
        ListenerRuntimeService.EnsureStarted(this);
        ListenerRuntimeService.RequestRebind(this);
        if (ListenerRuntimeService.NeedsHelp) ShowReadingRepair();
        UpdateStatus();
    }

    private void ShowReadingRepair()
    {
        new AlertDialog.Builder(this)!.SetTitle("Reparar lectura de BBVA")!
            .SetMessage("Si Android dejó el permiso activado pero no entrega las notificaciones:\n\n1. Abre el acceso a notificaciones.\n2. Desactiva y vuelve a activar solo Tony’s Go BBVA Listener.\n3. Regresa aquí y comprueba que Lectura de BBVA aparezca en verde.\n\nRevisa también el inicio automático para que pueda volver a funcionar después de reiniciar el celular.")!
            .SetPositiveButton("Revisar acceso", (_, _) => StartActivity(new Intent(Settings.ActionNotificationListenerSettings)))!
            .SetNeutralButton("Inicio automático", (_, _) => ShowStartupGuide())!
            .SetNegativeButton("Cerrar", (_, _) => { })!.Show();
    }

    private void ShowStartupGuide()
    {
        var huawei = (Build.Manufacturer ?? "").Contains("huawei", StringComparison.OrdinalIgnoreCase)
            || (Build.Manufacturer ?? "").Contains("honor", StringComparison.OrdinalIgnoreCase);
        var message = huawei
            ? "En Ajustes del teléfono, busca Inicio de aplicaciones (puede estar dentro de Batería).\n\nBusca Tony’s Go BBVA Listener, desactiva Gestionar automáticamente y permite las tres opciones:\n\n• Inicio automático\n• Inicio secundario\n• Ejecutar en segundo plano\n\nGuarda los cambios y regresa a Listener."
            : "En los ajustes de este teléfono, abre las opciones de Listener. Permite Inicio automático o Ejecutar en segundo plano si están disponibles, y evita las restricciones de batería.";
        new AlertDialog.Builder(this)!.SetTitle(huawei ? "Inicio automático en Huawei" : "Funcionamiento en segundo plano")!
            .SetMessage(message + "\n\nDespués de reiniciar el equipo, desbloquéalo una vez para habilitar los datos de las aplicaciones.")!
            // Some Huawei system-manager screens require a signature permission.
            // Public Settings always remains available; never bypass that restriction.
            .SetPositiveButton("Abrir ajustes", (_, _) => StartActivity(new Intent(Settings.ActionSettings)))!
            .SetNegativeButton("Volver", (_, _) => { })!.Show();
    }
}
