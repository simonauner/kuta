using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// The current certificate encrypts new Data Protection keys in the database; the previous one
// only decrypts keys from before a certificate rotation.
public sealed record DataProtectionCertificates(X509Certificate2? Current, X509Certificate2? Previous)
{
    private const string CurrentKey = "DataProtection:Certificate";
    private const string PreviousKey = "DataProtection:PreviousCertificate";

    // Set as environment variables, where the section separator is a double underscore.
    private static string Name(string key) => $"{key.Replace(":", "__")} ({key})";

    public static DataProtectionCertificates Load(IConfiguration configuration, bool requireCurrent)
    {
        var current = LoadCertificate(configuration, CurrentKey);
        if (current is null && requireCurrent)
            throw new InvalidOperationException(
                $"{Name(CurrentKey)} must be set outside Development: a base64-encoded PKCS #12 certificate (.pfx) " +
                "that encrypts the Data Protection keys in the database.");
        return new DataProtectionCertificates(current, LoadCertificate(configuration, PreviousKey));
    }

    private static X509Certificate2? LoadCertificate(IConfiguration configuration, string key)
    {
        var encoded = configuration[key];
        if (string.IsNullOrWhiteSpace(encoded))
            return null;

        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12(
                Convert.FromBase64String(encoded), configuration[$"{key}Password"]);
            return certificate.HasPrivateKey
                ? certificate
                : throw new CryptographicException("The certificate has no private key.");
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            throw new InvalidOperationException(
                $"{Name(key)} must be a base64-encoded PKCS #12 certificate (.pfx) with a private key; " +
                $"set {Name($"{key}Password")} if the file has a password.", exception);
        }
    }
}
