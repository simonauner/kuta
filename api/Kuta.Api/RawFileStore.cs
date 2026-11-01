using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

// Raw FIT files in S3-compatible object storage: locally Supabase Storage, in production R2 or Supabase Storage.
// activities.raw_file_key points to the key, so the import can be rerun when the logic changes.
// The raw file contains heart rate (health data), so it is stored only with consent and deleted when consent is withdrawn.
public sealed class RawFileStore
{
    private readonly string bucket;
    private readonly AmazonS3Client client;

    public RawFileStore(IConfiguration configuration)
    {
        string Required(string key) => configuration[$"Storage:{key}"]
            ?? throw new InvalidOperationException($"Storage:{key} is missing (Storage__{key})");
        bucket = Required("Bucket");
        client = new AmazonS3Client(
            new BasicAWSCredentials(Required("AccessKeyId"), Required("SecretAccessKey")),
            new AmazonS3Config
            {
                ServiceURL = Required("ServiceUrl"),
                AuthenticationRegion = Required("Region"),
                ForcePathStyle = true,
                // R2 and Supabase Storage don't support the SDK's default checksums (CRC32 in trailer)
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
            // Streaming signing (aws-chunked) isn't supported by R2/Supabase; the files are small
            UseChunkEncoding = false,
        }, cancellationToken);
    }

    public async Task CopyToAsync(string key, Stream destination, CancellationToken cancellationToken)
    {
        using var response = await client.GetObjectAsync(bucket, key, cancellationToken);
        await response.ResponseStream.CopyToAsync(destination, cancellationToken);
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
