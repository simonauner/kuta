using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

// Råa FIT-filer i S3-kompatibel objektlagring: lokalt Supabase Storage, i drift R2 eller Supabase Storage.
// activities.raw_file_key pekar på nyckeln, så att importen kan köras om när logiken ändras.
// Råfilen innehåller puls (hälsodata), så den sparas bara med samtycke och raderas när samtycket återkallas.
public sealed class RawFileStore
{
    private readonly string bucket;
    private readonly AmazonS3Client client;

    public RawFileStore(IConfiguration configuration)
    {
        string Required(string key) => configuration[$"Storage:{key}"]
            ?? throw new InvalidOperationException($"Storage:{key} saknas (Storage__{key})");
        bucket = Required("Bucket");
        client = new AmazonS3Client(
            new BasicAWSCredentials(Required("AccessKeyId"), Required("SecretAccessKey")),
            new AmazonS3Config
            {
                ServiceURL = Required("ServiceUrl"),
                AuthenticationRegion = Required("Region"),
                ForcePathStyle = true,
                // R2 och Supabase Storage stöder inte SDK:ns standardchecksummor (CRC32 i trailer)
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    public async Task PutAsync(string key, byte[] content, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content);
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = stream,
            ContentType = "application/octet-stream",
            // Strömmande signering (aws-chunked) stöds inte av R2/Supabase; filerna är små
            UseChunkEncoding = false,
        }, cancellationToken);
    }

    public async Task DeleteAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        foreach (var key in keys)
        {
            try { await client.DeleteObjectAsync(bucket, key, cancellationToken); }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound) { }
        }
    }
}
