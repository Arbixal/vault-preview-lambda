using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using VaultShared.Seasons;
using VaultPreview.SeasonConfigurationInfrastructure;
using SeasonConfigCli.Request;
using SeasonConfigCli.Response;

namespace SeasonConfigCli;

public sealed class SeasonConfigurationCli
{
    private readonly ISeasonRevisionStore _store;
    private readonly ILambdaInvoker? _lambdaInvoker;
    private readonly string? _activationFunctionName;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public SeasonConfigurationCli(
        ISeasonRevisionStore store,
        ILambdaInvoker? lambdaInvoker = null,
        string? activationFunctionName = null)
    {
        _store = store;
        _lambdaInvoker = lambdaInvoker;
        _activationFunctionName = activationFunctionName;
    }

    public async Task<int> ValidateAsync(string sourceFilePath, CancellationToken cancellationToken = default)
    {
        SeasonConfiguration configuration = await LoadConfigurationAsync(sourceFilePath, cancellationToken);
        try
        {
            SeasonConfigurationValidator.ValidateOrThrow(configuration);
            string hash = SeasonRevisionHasher.Compute(configuration);
            Console.WriteLine($"Valid configuration '{configuration.Id}'. Revision hash: {hash}");
            return 0;
        }
        catch (SeasonConfigurationValidationException ex)
        {
            PrintErrors("Validation failed", ex.Errors);
            return 1;
        }
    }

    public async Task<int> PublishAsync(
        string sourceFilePath,
        string seasonId,
        string revisionId,
        CancellationToken cancellationToken = default)
    {
        SeasonConfiguration configuration = await LoadConfigurationAsync(sourceFilePath, cancellationToken);
        try
        {
            SeasonConfigurationValidator.ValidateOrThrow(configuration);
        }
        catch (SeasonConfigurationValidationException ex)
        {
            PrintErrors("Validation failed", ex.Errors);
            return 1;
        }

        if (configuration.Id != seasonId)
        {
            Console.Error.WriteLine(
                $"Season ID mismatch: source configuration has ID '{configuration.Id}' but argument is '{seasonId}'.");
            return 1;
        }

        SeasonRevision revision = SeasonRevision.Create(revisionId, configuration);
        try
        {
            await _store.SaveRevision(revision, cancellationToken);
            Console.WriteLine($"Published revision '{revisionId}' for season '{seasonId}' with hash {revision.RevisionHash}");
            return 0;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            Console.Error.WriteLine(
                $"Revision '{revisionId}' already exists with different content. Publish rejected (immutable).");
            return 1;
        }
        catch (SeasonConfigurationValidationException ex)
        {
            PrintErrors("Publish failed", ex.Errors);
            return 1;
        }
    }

    public async Task<int> ActivateAsync(
        string seasonId,
        string revisionId,
        DateTimeOffset? activationAt,
        CancellationToken cancellationToken = default)
    {
        SeasonRevision? revision = await GetRevisionAsync(seasonId, revisionId, cancellationToken);
        if (revision == null)
        {
            Console.Error.WriteLine(
                $"Revision '{revisionId}' for season '{seasonId}' is not available.");
            return 1;
        }

        if (activationAt.HasValue && activationAt.Value > DateTimeOffset.UtcNow)
        {
            Console.Error.WriteLine(
                "Use 'schedule' for future activations, not 'activate'.");
            return 1;
        }

        if (_lambdaInvoker != null && !string.IsNullOrEmpty(_activationFunctionName))
        {
            SeasonActivationRequest request = new()
            {
                Operation = "activate",
                SeasonId = seasonId,
                RevisionId = revisionId,
                RevisionHash = revision.RevisionHash
            };
            try
            {
                SeasonActivationResponse response = await _lambdaInvoker.InvokeAsync(
                    _activationFunctionName, request, cancellationToken);
                Console.WriteLine(
                    $"Activated revision '{revisionId}' for season '{seasonId}' at {response.ActivatedAt}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Activation request failed: {ex.Message}");
                return 1;
            }
        }

        try
        {
            await _store.Activate(seasonId, revisionId, activationAt, cancellationToken);
            Console.WriteLine(
                $"Activated revision '{revisionId}' for season '{seasonId}'{(activationAt.HasValue ? $" at {activationAt.Value}" : "")}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Activation failed: {ex.Message}");
            return 1;
        }
    }

    public async Task<int> ScheduleAsync(
        string seasonId,
        string revisionId,
        DateTimeOffset activationAt,
        CancellationToken cancellationToken = default)
    {
        if (activationAt <= DateTimeOffset.UtcNow)
        {
            Console.Error.WriteLine("Scheduled activation must be in the future.");
            return 1;
        }

        SeasonRevision? revision = await GetRevisionAsync(seasonId, revisionId, cancellationToken);
        if (revision == null)
        {
            Console.Error.WriteLine(
                $"Revision '{revisionId}' for season '{seasonId}' is not available.");
            return 1;
        }

        try
        {
            await _store.Schedule(seasonId, revisionId, activationAt, cancellationToken);
            Console.WriteLine(
                $"Scheduled activation of revision '{revisionId}' for season '{seasonId}' at {activationAt}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Schedule failed: {ex.Message}");
            return 1;
        }
    }

    public async Task<int> RollbackAsync(
        string seasonId,
        string revisionId,
        DateTimeOffset? activationAt,
        CancellationToken cancellationToken = default)
    {
        SeasonRevision? revision = await GetRevisionAsync(seasonId, revisionId, cancellationToken);
        if (revision == null)
        {
            Console.Error.WriteLine(
                $"Revision '{revisionId}' for season '{seasonId}' is not available.");
            return 1;
        }

        if (_lambdaInvoker != null && !string.IsNullOrEmpty(_activationFunctionName))
        {
            SeasonActivationRequest request = new()
            {
                Operation = "rollback",
                SeasonId = seasonId,
                RevisionId = revisionId,
                RevisionHash = revision.RevisionHash
            };
            try
            {
                SeasonActivationResponse response = await _lambdaInvoker.InvokeAsync(
                    _activationFunctionName, request, cancellationToken);
                Console.WriteLine(
                    $"Rolled back to revision '{revisionId}' for season '{seasonId}' at {response.ActivatedAt}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Rollback request failed: {ex.Message}");
                return 1;
            }
        }

        try
        {
            await _store.Rollback(seasonId, revisionId, activationAt, cancellationToken);
            Console.WriteLine(
                $"Rolled back to revision '{revisionId}' for season '{seasonId}'{(activationAt.HasValue ? $" at {activationAt.Value}" : "")}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Rollback failed: {ex.Message}");
            return 1;
        }
    }

    public async Task<int> CancelAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _store.CancelSchedule(cancellationToken);
            Console.WriteLine("Cancelled pending schedule.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Cancel failed: {ex.Message}");
            return 1;
        }
    }

    private async Task<SeasonConfiguration> LoadConfigurationAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string json = await File.ReadAllTextAsync(path, cancellationToken);
        SeasonConfiguration? configuration = JsonSerializer.Deserialize<SeasonConfiguration>(json, _jsonOptions);
        if (configuration == null)
        {
            throw new InvalidOperationException("Failed to deserialize season configuration.");
        }
        return configuration;
    }

    private async Task<SeasonRevision?> GetRevisionAsync(
        string seasonId,
        string revisionId,
        CancellationToken cancellationToken)
    {
        SeasonRevision? revision = await _store.GetRevision(seasonId, revisionId, cancellationToken);
        if (revision == null || !string.Equals(revision.Configuration.Id, seasonId, StringComparison.Ordinal))
        {
            return null;
        }
        return revision;
    }

    private static void PrintErrors(string header, IReadOnlyList<string> errors)
    {
        Console.Error.WriteLine(header);
        foreach (string error in errors)
        {
            Console.Error.WriteLine($"  {error}");
        }
    }
}
