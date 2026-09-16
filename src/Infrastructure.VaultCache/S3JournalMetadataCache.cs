using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using VaultPreview.Blizzard;

namespace VaultPreview.VaultCache;

public sealed class S3JournalMetadataCache(IAmazonS3 s3Client) : IJournalMetadataCache
{
    private const string _BUCKET_NAME = "vault-preview-data";
    private const string _KEY_FORMAT = "journal-metadata/v1/{0}/{1}/{2}.json";

    public async Task<JournalMetadataCacheEntry?> Get(
        string region,
        string staticNamespace,
        long instanceId)
    {
        try
        {
            using GetObjectResponse response = await s3Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _BUCKET_NAME,
                Key = _getKey(region, staticNamespace, instanceId)
            });

            JournalMetadataCacheEntry? entry = await Deserialize(response.ResponseStream);
            if (IsValid(entry))
                return entry;

            Console.WriteLine("Ignoring semantically invalid Journal metadata cache content.");
            return null;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonS3Exception exception)
        {
            Console.WriteLine($"Unable to read Journal metadata cache: {exception.Message}");
            return null;
        }
        catch (JsonException exception)
        {
            Console.WriteLine($"Ignoring invalid Journal metadata cache content: {exception.Message}");
            return null;
        }
        catch (NotSupportedException exception)
        {
            Console.WriteLine($"Ignoring unsupported Journal metadata cache content: {exception.Message}");
            return null;
        }
    }

    public async Task Put(
        string region,
        string staticNamespace,
        long instanceId,
        JournalMetadataCacheEntry entry)
    {
        try
        {
            using MemoryStream memoryStream = new();
            await JsonSerializer.SerializeAsync(memoryStream, entry);
            memoryStream.Position = 0;

            await s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _BUCKET_NAME,
                Key = _getKey(region, staticNamespace, instanceId),
                ContentType = "application/json",
                AutoCloseStream = true,
                InputStream = memoryStream
            });
        }
        catch (AmazonS3Exception exception)
        {
            Console.WriteLine($"Unable to write Journal metadata cache: {exception.Message}");
        }
    }

    private static string _getKey(string region, string staticNamespace, long instanceId)
    {
        return string.Format(
            _KEY_FORMAT,
            Uri.EscapeDataString(region.Trim().ToLowerInvariant()),
            Uri.EscapeDataString(staticNamespace.Trim().ToLowerInvariant()),
            instanceId);
    }

    internal static async Task<JournalMetadataCacheEntry?> Deserialize(Stream stream)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<JournalMetadataCacheEntry>(stream);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    internal static bool IsValid(JournalMetadataCacheEntry? entry)
    {
        return entry?.Instance != null &&
               entry.Instance.Id > 0 &&
               !string.IsNullOrWhiteSpace(entry.Instance.Name) &&
               entry.FetchedAt <= entry.ExpiresAt &&
               entry.ExpiresAt <= entry.StaleUntil;
    }
}
