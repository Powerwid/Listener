using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using TonysGo.Listener.Pairing;

namespace Listener.Api.Devices;

public sealed class DevicePairingException(string message) : Exception(message);
public sealed record CollectorDeviceInfo(Guid Id, string Model, DateTimeOffset LinkedAt, DateTimeOffset? LastSeenAt, bool Revoked);

public sealed class DevicePairingService(PairingDbContext db, TimeProvider clock)
{
    public async Task<DevicePairingQr> CreateAsync(string serverUrl, string administrator, CancellationToken ct)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var now = clock.GetUtcNow();
            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            // A new QR replaces earlier unused tickets, also when two administrators generate concurrently.
            await db.Tickets.Where(x => x.ConsumedAt == null).ExecuteUpdateAsync(
                updates => updates.SetProperty(x => x.ConsumedAt, now), ct);
            db.Tickets.Add(new DevicePairingTicket
            {
                TokenHash = Hash(token), CreatedBy = administrator[..Math.Min(100, administrator.Length)],
                CreatedAt = now, ExpiresAt = now.AddMinutes(5)
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new DevicePairingQr(DevicePairingProtocol.QrType, 1, serverUrl.TrimEnd('/'), token, now.AddMinutes(5));
        });
    }

    public async Task<DevicePairingResponse> RedeemAsync(DevicePairingRequest request, CancellationToken ct)
    {
        if (request.Token is not { Length: 43 } || !Guid.TryParseExact(request.InstallationId, "N", out _)
            || string.IsNullOrWhiteSpace(request.NotificationDeviceId) || request.NotificationDeviceId.Length > 100
            || string.IsNullOrWhiteSpace(request.DeviceModel) || request.DeviceModel.Length > 120)
            throw new DevicePairingException("La solicitud de vinculacion no es valida.");
        var hash = Hash(request.Token);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var now = clock.GetUtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            // The conditional write consumes the QR atomically, including simultaneous scans.
            var consumed = await db.Tickets.Where(x => x.TokenHash == hash && x.ConsumedAt == null && x.ExpiresAt > now)
                .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.ConsumedAt, now), ct);
            if (consumed != 1) throw new DevicePairingException("El QR vencio o ya fue utilizado. Genera uno nuevo en la web.");
            var device = await db.Devices.SingleOrDefaultAsync(x => x.InstallationId == request.InstallationId, ct);
            if (device is null)
            {
                device = new CollectorDevice
                {
                    InstallationId = request.InstallationId, NotificationDeviceId = request.NotificationDeviceId,
                    Model = request.DeviceModel.Trim(), CredentialHash = "", CreatedAt = now
                };
                db.Devices.Add(device);
            }
            var credential = $"dvc_{device.Id:N}.{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}";
            device.NotificationDeviceId = request.NotificationDeviceId;
            device.Model = request.DeviceModel.Trim();
            device.CredentialHash = Hash(credential);
            device.LinkedAt = now;
            device.RevokedAt = null;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new DevicePairingResponse(credential, device.NotificationDeviceId);
        });
    }

    public async Task<CollectorDevice?> AuthenticateAsync(string? credential, CancellationToken ct)
    {
        if (credential is not { Length: 80 } || !credential.StartsWith("dvc_", StringComparison.Ordinal)
            || credential[36] != '.' || !Guid.TryParseExact(credential.AsSpan(4, 32), "N", out var id)) return null;
        var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.RevokedAt == null, ct);
        if (device is null || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(device.CredentialHash), Encoding.ASCII.GetBytes(Hash(credential)))) return null;
        var now = clock.GetUtcNow();
        if (device.LastSeenAt is null || device.LastSeenAt < now.AddMinutes(-1))
            await db.Devices.Where(x => x.Id == id && x.RevokedAt == null)
                .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.LastSeenAt, now), ct);
        return device;
    }

    public Task<CollectorDeviceInfo[]> ListAsync(CancellationToken ct) => db.Devices.AsNoTracking()
        .OrderByDescending(x => x.LinkedAt)
        .Select(x => new CollectorDeviceInfo(x.Id, x.Model, x.LinkedAt, x.LastSeenAt, x.RevokedAt != null)).ToArrayAsync(ct);

    public async Task<bool> RevokeAsync(Guid id, CancellationToken ct) => await db.Devices.Where(x => x.Id == id && x.RevokedAt == null)
        .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.RevokedAt, clock.GetUtcNow()), ct) == 1;

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
