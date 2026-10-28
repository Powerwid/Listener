using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using TonysGo.Listener.Bbva;

namespace Listener.Api.Bbva;

public sealed record BbvaValidationResult(Guid EventoId, Guid AcumulacionId, long Secuencia, string NombreCompleto,
    bool Correcto, decimal MontoRecibido, decimal MontoAcumulado, decimal MontoMinimo, decimal MontoFaltante,
    string Codigo, string Mensaje, DateTimeOffset FechaNotificacion, string Moneda = "PEN", string Origen = "BBVA_EMPRESAS")
{
    // A failed validation must never look like a creditable abono to an older receiver.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Monto => Correcto ? MontoAcumulado : null;
}

public static partial class BbvaPaymentLedger
{
    public const decimal Minimum = 10m;
    public const string InsufficientCode = "MONTO_INSUFICIENTE";
    public const string CorrectCode = "MONTO_MINIMO_CUMPLIDO";

    // Caller must own a serializable transaction. Event identity and the running
    // balance are committed together; an Android retry cannot add the money twice.
    public static async Task<BbvaPaymentValidation?> ApplyAsync(ListenerDbContext db, CorreoProcesado row,
        BbvaPaymentData payment, CancellationToken ct)
    {
        if (row.EsSimulacion || !payment.EsAbono || string.IsNullOrWhiteSpace(payment.NombreCompleto) || payment.Monto is not > 0) return null;
        var existing = await db.BbvaPaymentValidations.FindAsync([row.Id], ct);
        if (existing is not null) return existing;
        var normalized = Spaces().Replace(payment.NombreCompleto.Normalize(NormalizationForm.FormKC).Trim(), " ").ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        // The range lock also serializes creation of a previously unseen payer.
        var balance = db.Database.IsSqlServer()
            ? await db.BbvaPaymentBalances.FromSqlInterpolated($"SELECT * FROM [BbvaPaymentBalances] WITH (UPDLOCK, HOLDLOCK) WHERE [PayerKey] = {key}").SingleOrDefaultAsync(ct)
            : await db.BbvaPaymentBalances.SingleOrDefaultAsync(x => x.PayerKey == key, ct);
        if (balance is null)
        {
            balance = new() { PayerKey = key };
            db.BbvaPaymentBalances.Add(balance);
        }
        var total = balance.PendingAmount + payment.Monto.Value;
        var correct = total >= Minimum;
        var validation = new BbvaPaymentValidation
        {
            EventId = row.Id, PayerKey = key, FullName = payment.NombreCompleto, GroupId = balance.GroupId,
            Sequence = checked(++balance.LastSequence), ReceivedAmount = payment.Monto.Value,
            AccumulatedAmount = total, Correct = correct, NotificationAt = row.FechaRecepcion
        };
        balance.PendingAmount = correct ? 0 : total;
        if (correct) balance.GroupId = Guid.NewGuid();
        row.CodigoMotivo = correct ? "ABONO_BBVA_EXTRAIDO" : InsufficientCode;
        db.BbvaPaymentValidations.Add(validation);
        return validation;
    }

    public static BbvaValidationResult Result(BbvaPaymentValidation validation)
    {
        var missing = Math.Max(0m, Minimum - validation.AccumulatedAmount);
        var total = Money(validation.AccumulatedAmount);
        var message = validation.Correct ? $"El total acumulado es S/ {total}. Cumple el monto mínimo de S/ 10.00."
            : $"Recibimos S/ {Money(validation.ReceivedAmount)}. El total acumulado es S/ {total}; faltan S/ {Money(missing)} para alcanzar el mínimo de S/ 10.00.";
        return new(validation.EventId, validation.GroupId, validation.Sequence, validation.FullName, validation.Correct,
            validation.ReceivedAmount, validation.AccumulatedAmount, Minimum, missing,
            validation.Correct ? CorrectCode : InsufficientCode, message, validation.NotificationAt);
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
