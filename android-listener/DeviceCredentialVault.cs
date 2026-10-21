using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Text;

namespace TonysGo.YapeListener.Android;

internal static class DeviceCredentialVault
{
    private const string Alias = "tonysgo-listener-collector-v2";
    private const string Preference = "device_credential_v2";
    private static readonly object Gate = new();
    private static string? _cachedCiphertext;
    private static string _cachedSecret = "";

    public static string Read(Context context)
    {
        lock (Gate)
        {
            var encrypted = Preferences(context).GetString(Preference, "") ?? "";
            if (encrypted.Length == 0) return "";
            if (encrypted == _cachedCiphertext) return _cachedSecret;
            try
            {
                var bytes = Convert.FromBase64String(encrypted);
                if (bytes.Length < 29) return "";
                using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
                cipher.Init(CipherMode.DecryptMode, Key(), new GCMParameterSpec(128, bytes[..12]));
                _cachedSecret = Encoding.UTF8.GetString(cipher.DoFinal(bytes[12..])!);
                _cachedCiphertext = encrypted;
                return _cachedSecret;
            }
            catch (Exception)
            {
                // A missing/invalidated Keystore key requires scanning a fresh QR. Never fall back to plaintext.
                return "";
            }
        }
    }

    public static void Save(Context context, string credential)
    {
        lock (Gate)
        {
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.EncryptMode, Key());
            var encrypted = Convert.ToBase64String(cipher.GetIV()!.Concat(cipher.DoFinal(Encoding.UTF8.GetBytes(credential))!).ToArray());
            if (!Preferences(context).Edit()!.PutString(Preference, encrypted)!.Remove("api_key")!.Commit())
                throw new IOException("No se pudo guardar la vinculacion en el telefono.");
            _cachedCiphertext = encrypted;
            _cachedSecret = credential;
        }
    }

    public static void Clear(Context context)
    {
        lock (Gate)
        {
            Preferences(context).Edit()!.Remove(Preference)!.Remove("api_key")!.Commit();
            _cachedCiphertext = null;
            _cachedSecret = "";
        }
    }

    private static ISharedPreferences Preferences(Context context) => context.GetSharedPreferences("listener_config", FileCreationMode.Private)!;
    private static IKey Key()
    {
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        if (store.ContainsAlias(Alias)) return store.GetKey(Alias, null)!;
        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes!, "AndroidKeyStore")!;
        using var specification = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm!).SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone!)
            .SetRandomizedEncryptionRequired(true).Build()!;
        generator.Init(specification);
        return generator.GenerateKey()!;
    }
}
