using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;
using SeasonConfigCli.Request;
using SeasonConfigCli.Response;
using VaultPreview.SeasonConfigurationInfrastructure;
using VaultShared.Seasons;

namespace SeasonConfigCli;

public sealed class SeasonConfigurationCli
{
    private const int _MAX_SCHEDULE_NAME_LENGTH = 64;
    private const int _SCHEDULER_MAXIMUM_EVENT_AGE_SECONDS = 86400;
    private const int _SCHEDULER_MAXIMUM_RETRY_ATTEMPTS = 3;
    private const string _SCHEDULE_NAME_PREFIX = "vault-preview-activate-";

    private readonly ISeasonRevisionStore? _store;
    private readonly ILambdaInvoker? _lambdaInvoker;
    private readonly IScheduler? _scheduler;
    private readonly SeasonConfigurationCliOptions _options;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly Regex _seasonIdPattern = new(
        @"^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex _scheduleNamePattern = new(
        @"^[A-Za-z0-9_.-]{1,64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public SeasonConfigurationCli(
        ISeasonRevisionStore? store,
        ILambdaInvoker? lambdaInvoker = null,
        IScheduler? scheduler = null,
        SeasonConfigurationCliOptions? options = null)
    {
        _store = store;
        _lambdaInvoker = lambdaInvoker;
        _scheduler = scheduler;
        _options = options ?? new SeasonConfigurationCliOptions();
    }

    public async Task<int> ValidateAsync(
        string sourceFilePath,
        CancellationToken cancellationToken = default)
    {
        SeasonConfiguration? configuration = await _loadConfigurationAsync(
            sourceFilePath,
            cancellationToken);
        if (configuration == null)
            return 1;

        try
        {
            SeasonConfigurationValidator.ValidateOrThrow(configuration);
            string hash = SeasonRevisionHasher.Compute(configuration);
            Console.WriteLine($"Valid configuration '{configuration.Id}'. Revision hash: {hash}");
            return 0;
        }
        catch (SeasonConfigurationValidationException exception)
        {
            _printErrors("Validation failed", exception.Errors);
            return 1;
        }
    }

    public async Task<int> PublishAsync(
        string sourceFilePath,
        string seasonId,
        string revisionId,
        CancellationToken cancellationToken = default)
    {
        SeasonConfiguration? configuration = await _loadConfigurationAsync(
            sourceFilePath,
            cancellationToken);
        if (configuration == null)
            return 1;

        if (_store == null)
        {
            Console.Error.WriteLine("AWS-backed configuration storage is not configured.");
            return 1;
        }

        try
        {
            SeasonConfigurationValidator.ValidateOrThrow(configuration);
        }
        catch (SeasonConfigurationValidationException exception)
        {
            _printErrors("Validation failed", exception.Errors);
            return 1;
        }

        if (!string.Equals(configuration.Id, seasonId, StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                $"Season ID mismatch: source configuration has ID '{configuration.Id}' but argument is '{seasonId}'.");
            return 1;
        }

        if (!_isValidRevisionId(seasonId, revisionId))
        {
            Console.Error.WriteLine(
                $"Revision ID '{revisionId}' must match the season ID and use the form '{seasonId}-r1'.");
            return 1;
        }

        SeasonRevision revision = SeasonRevision.Create(revisionId, configuration);
        try
        {
            await _store.SaveRevision(revision, cancellationToken);
            Console.WriteLine(
                $"Published revision '{revisionId}' for season '{seasonId}' with hash {revision.RevisionHash}");
            return 0;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            SeasonRevision? existing = await _store.GetRevision(
                seasonId,
                revisionId,
                cancellationToken);
            if (existing != null && existing.RevisionHash == revision.RevisionHash)
            {
                Console.WriteLine(
                    $"Revision '{revisionId}' for season '{seasonId}' is already published with the same content.");
                return 0;
            }

            Console.Error.WriteLine(
                $"Revision '{revisionId}' already exists with different content. Publish rejected (immutable).");
            return 1;
        }
        catch (SeasonConfigurationValidationException exception)
        {
            _printErrors("Publish failed", exception.Errors);
            return 1;
        }
    }

    public async Task<int> ActivateAsync(
        string seasonId,
        string revisionId,
        DateTimeOffset? activationAt,
        CancellationToken cancellationToken = default)
    {
        if (!_hasActivationLambda())
            return 1;

        if (_store == null)
        {
            Console.Error.WriteLine("AWS-backed configuration storage is not configured.");
            return 1;
        }

        if (_isFuture(activationAt))
        {
            Console.Error.WriteLine("Use 'schedule' for future activations, not 'activate'.");
            return 1;
        }

        SeasonRevision? revision = await _getRevisionAsync(
            seasonId,
            revisionId,
            cancellationToken);
        if (revision == null)
        {
            Console.Error.WriteLine(
                $"Revision '{revisionId}' for season '{seasonId}' is not available.");
            return 1;
        }

        SeasonActivationRequest request = new()
        {
            Operation = "activate",
            SeasonId = seasonId,
            RevisionId = revisionId,
            RevisionHash = revision.RevisionHash,
            ActivationAt = _toUtc(activationAt)
        };

        try
        {
            SeasonActivationResponse response = await _lambdaInvoker!.InvokeAsync(
                _options.ActivationFunctionName!,
                request,
                cancellationToken);
            Console.WriteLine(
                $"Activated revision '{revisionId}' for season '{seasonId}' at {response.ActivatedAt}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Activation request failed: {exception.Message}");
            return 1;
        }
    }

    public async Task<int> ScheduleAsync(
        string seasonId,
        string revisionId,
        DateTimeOffset activationAt,
        CancellationToken cancellationToken = default)
    {
        if (!_hasSchedulerConfiguration())
            return 1;

        DateTimeOffset activationAtUtc = activationAt.ToUniversalTime();
        if (activationAtUtc <= DateTimeOffset.UtcNow)
        {
            Console.Error.WriteLine("Scheduled activation must be in the future.");
            return 1;
        }

        if (_store == null)
        {
            Console.Error.WriteLine("AWS-backed configuration storage is not configured.");
            return 1;
        }

        SeasonRevision? revision = await _getRevisionAsync(
            seasonId,
            revisionId,
            cancellationToken);
        if (revision == null)
        {
            Console.Error.WriteLine(
                $"Revision '{revisionId}' for season '{seasonId}' is not available.");
            return 1;
        }

        string? scheduleName = _tryGetScheduleName(seasonId, revisionId);
        if (scheduleName == null)
            return 1;

        SeasonSchedule? pending = await _store.GetScheduled(cancellationToken);
        string? pendingScheduleName = pending == null
            ? null
            : _tryGetScheduleName(pending.SeasonId, pending.RevisionId);
        if (pending != null && pendingScheduleName == null)
        {
            Console.Error.WriteLine("The existing pending schedule has an invalid identity and cannot be replaced safely.");
            return 1;
        }

        if (pendingScheduleName != null && !string.Equals(
                pendingScheduleName,
                scheduleName,
                StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                $"A different pending schedule already exists ({pendingScheduleName}). Cancel it before scheduling another revision.");
            return 1;
        }

        SchedulerScheduleRequest scheduleRequest = _createScheduleRequest(
            scheduleName,
            seasonId,
            revisionId,
            revision.RevisionHash,
            activationAtUtc);

        try
        {
            await _scheduler!.CreateOrUpdateScheduleAsync(scheduleRequest, cancellationToken);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Scheduler creation failed: {exception.Message}");
            return 1;
        }

        try
        {
            await _store.Schedule(
                seasonId,
                revisionId,
                activationAtUtc,
                cancellationToken);
        }
        catch (Exception exception)
        {
            await _compensateScheduleFailureAsync(
                pending,
                scheduleName,
                cancellationToken);
            Console.Error.WriteLine($"Schedule state write failed: {exception.Message}");
            return 1;
        }

        Console.WriteLine(
            $"Scheduled activation of revision '{revisionId}' for season '{seasonId}' at {activationAtUtc:O}");
        return 0;
    }

    public async Task<int> RollbackAsync(
        string seasonId,
        string revisionId,
        DateTimeOffset? activationAt,
        CancellationToken cancellationToken = default)
    {
        if (!_hasActivationLambda())
            return 1;

        if (_store == null)
        {
            Console.Error.WriteLine("AWS-backed configuration storage is not configured.");
            return 1;
        }

        if (_isFuture(activationAt))
        {
            Console.Error.WriteLine("Future rollback timestamps are not supported; use a validated prior revision with 'rollback' immediately.");
            return 1;
        }

        SeasonRevision? revision = await _getRevisionAsync(
            seasonId,
            revisionId,
            cancellationToken);
        if (revision == null)
        {
            Console.Error.WriteLine(
                $"Revision '{revisionId}' for season '{seasonId}' is not available.");
            return 1;
        }

        SeasonActivationRequest request = new()
        {
            Operation = "rollback",
            SeasonId = seasonId,
            RevisionId = revisionId,
            RevisionHash = revision.RevisionHash,
            ActivationAt = _toUtc(activationAt)
        };

        try
        {
            SeasonActivationResponse response = await _lambdaInvoker!.InvokeAsync(
                _options.ActivationFunctionName!,
                request,
                cancellationToken);
            Console.WriteLine(
                $"Rolled back to revision '{revisionId}' for season '{seasonId}' at {response.ActivatedAt}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Rollback request failed: {exception.Message}");
            return 1;
        }
    }

    public async Task<int> CancelAsync(CancellationToken cancellationToken = default)
    {
        if (!_hasSchedulerConfiguration())
            return 1;

        if (_store == null)
        {
            Console.Error.WriteLine("AWS-backed configuration storage is not configured.");
            return 1;
        }

        SeasonSchedule? pending = await _store.GetScheduled(cancellationToken);
        if (pending == null)
        {
            await _store.CancelSchedule(cancellationToken);
            Console.WriteLine("Cancelled pending schedule.");
            return 0;
        }

        string? scheduleName = _tryGetScheduleName(pending.SeasonId, pending.RevisionId);
        if (scheduleName == null)
            return 1;

        try
        {
            await _scheduler!.DeleteScheduleAsync(
                _options.SchedulerGroupName!,
                scheduleName,
                cancellationToken);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Scheduler deletion failed: {exception.Message}");
            return 1;
        }

        try
        {
            await _store.CancelSchedule(cancellationToken);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Cancel state write failed after scheduler deletion: {exception.Message}");
            return 1;
        }

        Console.WriteLine("Cancelled pending schedule.");
        return 0;
    }

    private async Task _compensateScheduleFailureAsync(
        SeasonSchedule? previousSchedule,
        string scheduleName,
        CancellationToken cancellationToken)
    {
        try
        {
            if (previousSchedule == null)
            {
                await _scheduler!.DeleteScheduleAsync(
                    _options.SchedulerGroupName!,
                    scheduleName,
                    cancellationToken);
                return;
            }

            string? previousScheduleName = _tryGetScheduleName(
                previousSchedule.SeasonId,
                previousSchedule.RevisionId);
            if (previousScheduleName == null)
                return;

            SchedulerScheduleRequest previousRequest = _createScheduleRequest(
                previousScheduleName,
                previousSchedule.SeasonId,
                previousSchedule.RevisionId,
                previousSchedule.RevisionHash,
                previousSchedule.ActivationAt.ToUniversalTime());
            await _scheduler!.CreateOrUpdateScheduleAsync(previousRequest, cancellationToken);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unable to restore scheduler state after a failed state write: {exception.Message}");
        }
    }

    private SchedulerScheduleRequest _createScheduleRequest(
        string scheduleName,
        string seasonId,
        string revisionId,
        string revisionHash,
        DateTimeOffset activationAt)
    {
        SeasonActivationRequest activationRequest = new()
        {
            Operation = "activate",
            SeasonId = seasonId,
            RevisionId = revisionId,
            RevisionHash = revisionHash,
            ActivationAt = activationAt.ToUniversalTime()
        };

        return new SchedulerScheduleRequest(
            _options.SchedulerGroupName!,
            scheduleName,
            activationAt.ToUniversalTime(),
            _options.ActivationFunctionArn!,
            _options.SchedulerRoleArn!,
            _options.SchedulerDeadLetterQueueArn!,
            JsonSerializer.Serialize(activationRequest, _jsonOptions),
            _SCHEDULER_MAXIMUM_EVENT_AGE_SECONDS,
            _SCHEDULER_MAXIMUM_RETRY_ATTEMPTS);
    }

    private async Task<SeasonRevision?> _getRevisionAsync(
        string seasonId,
        string revisionId,
        CancellationToken cancellationToken)
    {
        SeasonRevision? revision = await _store!.GetRevision(
            seasonId,
            revisionId,
            cancellationToken);
        if (revision == null || !string.Equals(
                revision.Configuration.Id,
                seasonId,
                StringComparison.Ordinal))
        {
            return null;
        }

        return revision;
    }

    private async Task<SeasonConfiguration?> _loadConfigurationAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            SeasonConfiguration? configuration = JsonSerializer.Deserialize<SeasonConfiguration>(
                json,
                _jsonOptions);
            if (configuration == null)
            {
                Console.Error.WriteLine($"Configuration file '{path}' does not contain a JSON object.");
                return null;
            }

            return configuration;
        }
        catch (FileNotFoundException)
        {
            Console.Error.WriteLine($"Configuration file '{path}' was not found.");
        }
        catch (DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"Configuration directory for '{path}' was not found.");
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Configuration file '{path}' could not be accessed.");
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Configuration file '{path}' could not be read: {exception.Message}");
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine($"Configuration file '{path}' contains invalid JSON: {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            Console.Error.WriteLine($"Configuration file '{path}' has an unsupported shape: {exception.Message}");
        }

        return null;
    }

    private bool _hasActivationLambda()
    {
        if (_lambdaInvoker != null && !string.IsNullOrWhiteSpace(_options.ActivationFunctionName))
            return true;

        Console.Error.WriteLine(
            "Activation Lambda is not configured. Cannot perform activation or rollback in production mode.");
        return false;
    }

    private bool _hasSchedulerConfiguration()
    {
        if (_scheduler != null &&
            !string.IsNullOrWhiteSpace(_options.ActivationFunctionArn) &&
            !string.IsNullOrWhiteSpace(_options.SchedulerRoleArn) &&
            !string.IsNullOrWhiteSpace(_options.SchedulerGroupName) &&
            !string.IsNullOrWhiteSpace(_options.SchedulerDeadLetterQueueArn))
        {
            return true;
        }

        Console.Error.WriteLine(
            "Scheduler configuration is incomplete. Activation ARN, scheduler role ARN, scheduler group, dead-letter queue ARN, and AWS region are required.");
        return false;
    }

    private static string? _tryGetScheduleName(string seasonId, string revisionId)
    {
        string scheduleName = $"{_SCHEDULE_NAME_PREFIX}{seasonId}-{revisionId}";
        if (!_scheduleNamePattern.IsMatch(scheduleName) || scheduleName.Length > _MAX_SCHEDULE_NAME_LENGTH)
        {
            Console.Error.WriteLine(
                $"Schedule name '{scheduleName}' is invalid or exceeds {_MAX_SCHEDULE_NAME_LENGTH} characters.");
            return null;
        }

        return scheduleName;
    }

    private static bool _isValidRevisionId(string seasonId, string revisionId) =>
        _seasonIdPattern.IsMatch(seasonId) &&
        Regex.IsMatch(
            revisionId,
            $"^{Regex.Escape(seasonId)}-r[0-9]+$",
            RegexOptions.CultureInvariant);

    private static bool _isFuture(DateTimeOffset? activationAt) =>
        activationAt.HasValue && activationAt.Value > DateTimeOffset.UtcNow;

    private static DateTimeOffset? _toUtc(DateTimeOffset? value) =>
        value?.ToUniversalTime();

    private static void _printErrors(string header, IReadOnlyList<string> errors)
    {
        Console.Error.WriteLine(header);
        foreach (string error in errors)
        {
            Console.Error.WriteLine($"  {error}");
        }
    }
}
