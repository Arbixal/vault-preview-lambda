using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.S3;
using Amazon.S3.Model;
using VaultShared.Seasons;

namespace VaultPreview.SeasonConfigurationInfrastructure;

public sealed class S3SeasonRevisionProvider(IAmazonS3 s3Client)
    : ISeasonRevisionStore, IActiveSeasonRevisionProvider
{
    private const string _DEFAULT_BUCKET_NAME = "vault-preview-data";
    private const string _ACTIVE_KEY = "season-config/v1/active.json";
    private const string _SCHEDULED_KEY = "season-config/v1/scheduled.json";
    private const string _REVISION_KEY_FORMAT = "season-config/v1/revisions/{0}/{1}.json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private sealed record DocumentRead<T>(T? Value, string? ETag) where T : class;

    public async Task<SeasonRevision?> GetActiveRevision(CancellationToken cancellationToken = default)
    {
        ActiveSeasonPointer? pointer = await _getDocument<ActiveSeasonPointer>(
            _ACTIVE_KEY,
            cancellationToken);
        pointer = SelectActivePointer(pointer, DateTimeOffset.UtcNow);
        if (pointer == null)
            return null;

        SeasonRevisionDocument? document = await _getRevisionDocument(pointer, cancellationToken);
        if (!IsValid(document))
            return null;

        if (!string.Equals(document!.Id, pointer.Revision, StringComparison.Ordinal) ||
            !string.Equals(document.Configuration.Id, pointer.SeasonId, StringComparison.Ordinal) ||
            !string.Equals(document.RevisionHash, pointer.RevisionHash, StringComparison.Ordinal))
        {
            Console.WriteLine("Ignoring an active season pointer that does not match its revision document.");
            return null;
        }

        SeasonConfiguration snapshot = SeasonConfigurationSnapshot.Clone(document.Configuration);
        return new SeasonRevision(
            document.Id,
            snapshot,
            SeasonRevisionStatus.Active,
            pointer.ActivationAt,
            document.RevisionHash);
    }

    public async Task<SeasonRevision?> GetRevision(
        string seasonId,
        string revisionId,
        CancellationToken cancellationToken = default)
    {
        if (!_hasSafeKeyPart(seasonId) || !_hasSafeKeyPart(revisionId))
            return null;

        SeasonRevisionDocument? document = await _getDocument<SeasonRevisionDocument>(
            GetRevisionKey(seasonId, revisionId),
            cancellationToken);
        if (!IsValid(document) ||
            !string.Equals(document!.Id, revisionId, StringComparison.Ordinal) ||
            !string.Equals(document.Configuration.Id, seasonId, StringComparison.Ordinal))
        {
            return null;
        }

        return new SeasonRevision(
            document.Id,
            SeasonConfigurationSnapshot.Clone(document.Configuration),
            SeasonRevisionStatus.Draft,
            null,
            document.RevisionHash);
    }

    public async Task<ActiveSeasonRevision?> GetActive(CancellationToken cancellationToken = default)
    {
        SeasonRevision? revision = await GetActiveRevision(cancellationToken);
        if (revision == null)
            return null;

        return new ActiveSeasonRevision(
            revision.Configuration.Id,
            revision.Id,
            revision.RevisionHash,
            revision.Configuration.SourceSeasonId);
    }

    public async Task SaveRevision(
        SeasonRevision revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        SeasonConfigurationValidator.ValidateOrThrow(revision.Configuration);

        if (revision.Status != SeasonRevisionStatus.Draft)
        {
            throw new SeasonConfigurationValidationException(
                [$"Revision '{revision.Id}' must be saved as a draft."]);
        }

        if (!_hasSafeKeyPart(revision.Configuration.Id) || !_hasSafeKeyPart(revision.Id))
            throw new SeasonConfigurationValidationException(["Season and revision IDs must not contain path separators."]);

        SeasonConfiguration snapshot = SeasonConfigurationSnapshot.Clone(revision.Configuration);
        string expectedHash = SeasonRevisionHasher.Compute(snapshot);
        if (!string.Equals(expectedHash, revision.RevisionHash, StringComparison.Ordinal))
        {
            throw new SeasonConfigurationValidationException(
                [$"Revision '{revision.Id}' has a content hash that does not match its configuration."]);
        }

        SeasonRevisionDocument document = new()
        {
            Id = revision.Id,
            Configuration = snapshot,
            RevisionHash = revision.RevisionHash
        };

        try
        {
            await _putDocument(
                GetRevisionKey(snapshot.Id, revision.Id),
                document,
                ifNoneMatch: "*",
                cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            SeasonRevisionDocument? existingDocument = await _getDocument<SeasonRevisionDocument>(
                GetRevisionKey(snapshot.Id, revision.Id),
                cancellationToken);
            if (!IsValid(existingDocument) ||
                !string.Equals(existingDocument!.RevisionHash, revision.RevisionHash, StringComparison.Ordinal))
            {
                throw;
            }
        }
    }

    public async Task Activate(
        string seasonId,
        string revisionId,
        DateTimeOffset? activatedAt = null,
        CancellationToken cancellationToken = default)
    {
        if (activatedAt > DateTimeOffset.UtcNow)
        {
            await Schedule(seasonId, revisionId, activatedAt.Value, cancellationToken);
            return;
        }

        if (!_hasSafeKeyPart(seasonId) || !_hasSafeKeyPart(revisionId))
            throw new ArgumentException("Season and revision IDs must not contain path separators.");

        DocumentRead<ActiveSeasonPointer>? currentActive = await _getDocumentWithEtag<ActiveSeasonPointer>(
            _ACTIVE_KEY,
            cancellationToken);
        SeasonRevisionDocument? document = await _getDocument<SeasonRevisionDocument>(
            GetRevisionKey(seasonId, revisionId),
            cancellationToken);
        if (!IsValid(document))
            throw new InvalidOperationException($"Revision '{revisionId}' is missing or invalid.");

        if (!string.Equals(document!.Id, revisionId, StringComparison.Ordinal) ||
            !string.Equals(document.Configuration.Id, seasonId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Revision '{revisionId}' does not match season '{seasonId}'.");
        }

        ActiveSeasonPointer pointer = new()
        {
            SeasonId = seasonId,
            Revision = revisionId,
            RevisionHash = document.RevisionHash,
            ActivationAt = activatedAt ?? DateTimeOffset.UtcNow
        };
        await _putDocument(
            _ACTIVE_KEY,
            pointer,
            currentActive == null ? "*" : null,
            cancellationToken,
            currentActive?.ETag);
        await _deleteDocument(_SCHEDULED_KEY, cancellationToken);
    }

    public async Task Schedule(
        string seasonId,
        string revisionId,
        DateTimeOffset activationAt,
        CancellationToken cancellationToken = default)
    {
        if (activationAt <= DateTimeOffset.UtcNow)
            throw new ArgumentOutOfRangeException(nameof(activationAt), "Scheduled activation must be in the future.");
        if (!_hasSafeKeyPart(seasonId) || !_hasSafeKeyPart(revisionId))
            throw new ArgumentException("Season and revision IDs must not contain path separators.");

        SeasonRevisionDocument? document = await _getDocument<SeasonRevisionDocument>(
            GetRevisionKey(seasonId, revisionId),
            cancellationToken);
        if (!IsValid(document))
            throw new InvalidOperationException($"Revision '{revisionId}' is missing or invalid.");

        if (!string.Equals(document!.Id, revisionId, StringComparison.Ordinal) ||
            !string.Equals(document.Configuration.Id, seasonId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Revision '{revisionId}' does not match season '{seasonId}'.");
        }

        await _putDocument(
            _SCHEDULED_KEY,
            new ActiveSeasonPointer
            {
                SeasonId = seasonId,
                Revision = revisionId,
                RevisionHash = document.RevisionHash,
                ActivationAt = activationAt
            },
            null,
            cancellationToken);
    }

    public async Task<SeasonSchedule?> GetScheduled(CancellationToken cancellationToken = default)
    {
        ActiveSeasonPointer? pointer = await _getControlPlaneDocument<ActiveSeasonPointer>(
            _SCHEDULED_KEY,
            cancellationToken);
        if (pointer == null)
            return null;

        if (!_hasValidPointer(pointer))
        {
            throw new InvalidDataException(
                "The pending season activation pointer is invalid.");
        }

        return new SeasonSchedule(
            pointer!.SeasonId,
            pointer.Revision,
            pointer.RevisionHash,
            pointer.ActivationAt);
    }

    public Task Rollback(
        string seasonId,
        string revisionId,
        DateTimeOffset? activatedAt = null,
        CancellationToken cancellationToken = default) =>
        Activate(seasonId, revisionId, activatedAt, cancellationToken);

    public Task CancelSchedule(CancellationToken cancellationToken = default) =>
        _deleteDocument(_SCHEDULED_KEY, cancellationToken);

    internal static bool IsValid(SeasonRevisionDocument? document)
    {
        if (document == null ||
            string.IsNullOrWhiteSpace(document.Id) ||
            string.IsNullOrWhiteSpace(document.RevisionHash) ||
            document.Configuration == null)
        {
            return false;
        }

        try
        {
            SeasonConfigurationValidator.ValidateOrThrow(document.Configuration);
        }
        catch (SeasonConfigurationValidationException)
        {
            return false;
        }

        return string.Equals(
            SeasonRevisionHasher.Compute(document.Configuration),
            document.RevisionHash,
            StringComparison.Ordinal);
    }

    internal static string GetRevisionKey(string seasonId, string revisionId) =>
        string.Format(
            _REVISION_KEY_FORMAT,
            Uri.EscapeDataString(seasonId),
            Uri.EscapeDataString(revisionId));

    internal static ActiveSeasonPointer? SelectActivePointer(
        ActiveSeasonPointer? pointer,
        DateTimeOffset now) =>
        _hasValidPointer(pointer) && pointer!.ActivationAt <= now
            ? pointer
            : null;

    private async Task<SeasonRevisionDocument?> _getRevisionDocument(
        ActiveSeasonPointer pointer,
        CancellationToken cancellationToken)
    {
        return await _getDocument<SeasonRevisionDocument>(
            GetRevisionKey(pointer.SeasonId, pointer.Revision),
            cancellationToken);
    }

    private async Task<T?> _getDocument<T>(string key, CancellationToken cancellationToken)
        where T : class
    {
        DocumentRead<T>? document = await _getDocumentWithEtag<T>(key, cancellationToken);
        return document?.Value;
    }

    private async Task<T?> _getControlPlaneDocument<T>(
        string key,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using GetObjectResponse response = await s3Client.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = _getBucketName(),
                    Key = key
                },
                cancellationToken);

            T? document = await JsonSerializer.DeserializeAsync<T>(
                response.ResponseStream,
                _jsonOptions,
                cancellationToken);
            if (document == null)
            {
                throw new InvalidDataException(
                    $"Season control-plane object '{key}' is empty or null.");
            }

            return document;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonS3Exception exception)
        {
            throw new IOException(
                $"Unable to read season control-plane object '{key}'.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Season control-plane object '{key}' contains invalid JSON.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidDataException(
                $"Season control-plane object '{key}' has an unsupported shape.",
                exception);
        }
    }

    private async Task<DocumentRead<T>?> _getDocumentWithEtag<T>(
        string key,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using GetObjectResponse response = await s3Client.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = _getBucketName(),
                    Key = key
                },
                cancellationToken);

            T? document = await JsonSerializer.DeserializeAsync<T>(
                response.ResponseStream,
                _jsonOptions,
                cancellationToken);
            return new DocumentRead<T>(document, response.ETag);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return default;
        }
        catch (AmazonS3Exception exception)
        {
            Console.WriteLine($"Unable to read season configuration: {exception.GetType().Name}");
            return default;
        }
        catch (JsonException exception)
        {
            Console.WriteLine($"Ignoring invalid season configuration content: {exception.GetType().Name}");
            return default;
        }
        catch (NotSupportedException exception)
        {
            Console.WriteLine($"Ignoring unsupported season configuration content: {exception.GetType().Name}");
            return default;
        }
    }

    private async Task _putDocument<T>(
        string key,
        T document,
        string? ifNoneMatch,
        CancellationToken cancellationToken,
        string? ifMatch = null)
    {
        using MemoryStream memoryStream = new();
        await JsonSerializer.SerializeAsync(memoryStream, document, _jsonOptions, cancellationToken);
        memoryStream.Position = 0;

        await s3Client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _getBucketName(),
                Key = key,
                ContentType = "application/json",
                InputStream = memoryStream,
                AutoCloseStream = true,
                IfNoneMatch = ifNoneMatch,
                IfMatch = ifMatch
            },
            cancellationToken);
    }

    private async Task _deleteDocument(string key, CancellationToken cancellationToken)
    {
        try
        {
            await s3Client.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = _getBucketName(),
                    Key = key
                },
                cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // A missing scheduled pointer is already the desired state.
        }
    }

    private static bool _hasValidPointer(ActiveSeasonPointer? pointer) =>
        pointer != null &&
        _hasSafeKeyPart(pointer.SeasonId) &&
        _hasSafeKeyPart(pointer.Revision) &&
        _isValidRevisionHash(pointer.RevisionHash) &&
        pointer.ActivationAt > DateTimeOffset.UnixEpoch;

    private static bool _isValidRevisionHash(string? revisionHash) =>
        !string.IsNullOrWhiteSpace(revisionHash) &&
        revisionHash.StartsWith("sha256:", StringComparison.Ordinal) &&
        revisionHash.Length == "sha256:".Length + 64 &&
        revisionHash[7..].All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool _hasSafeKeyPart(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.IndexOfAny(['/','\\']) < 0;

    private static string _getBucketName() =>
        Environment.GetEnvironmentVariable("VAULT_PREVIEW_DATA_BUCKET") ?? _DEFAULT_BUCKET_NAME;
}
