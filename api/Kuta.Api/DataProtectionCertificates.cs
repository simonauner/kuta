using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// Portad från Kredovis. Det aktuella certifikatet krypterar nya Data Protection-nycklar i databasen; det förra
// dekrypterar bara nycklar från före ett certifikatbyte.
public sealed record DataProtectionCertificates(X509Certificate2? Current, X509Certificate2? Previous)
{
    private const string CurrentKey = "DataProtection:Certificate";
    private const string PreviousKey = "DataProtection:PreviousCertificate";

    // Sätts som miljövariabler, där sektionsavgränsaren är dubbla understreck.
    private static string Name(string key) => $"{key.Replace(":", "__")} ({key})";

    public static DataProtectionCertificates Load(IConfiguration configuration, bool requireCurrent)
    {
        var current = LoadCertificate(configuration, CurrentKey);
        if (current is null && requireCurrent)
            throw new InvalidOperationException(
                $"{Name(CurrentKey)} måste sättas utanför Development: ett base64-kodat PKCS #12-certifikat (.pfx) " +
                "som krypterar Data Protection-nycklarna i databasen.");
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
                : throw new CryptographicException("Certifikatet saknar privat nyckel.");
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            throw new InvalidOperationException(
                $"{Name(key)} måste vara ett base64-kodat PKCS #12-certifikat (.pfx) med privat nyckel; " +
                $"sätt {Name($"{key}Password")} om filen har lösenord.", exception);
        }
    }
}
