using System.Text.Json;
using Amazon.Lambda.Annotations;
using Amazon.Lambda.Core;
using SeasonActivationLambda.Request;
using SeasonActivationLambda.Response;
using VaultShared.Seasons;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SeasonActivationLambda;

public sealed class Function(ISeasonRevisionStore seasonRevisionStore)
{
    [LambdaFunction]
    public async Task<SeasonActivationResponse> FunctionHandler(
        SeasonActivationRequest request,
        ILambdaContext context)
    {
        try
        {
            _validateRequest(request);
            SeasonRevision revision = await _getValidatedRevision(request);
            DateTimeOffset activationAt = request.ActivationAt ?? DateTimeOffset.UtcNow;
            if (activationAt > DateTimeOffset.UtcNow)
            {
                throw new InvalidDataException(
                    "ActivationAt must be in the past or present when the activation Lambda is invoked.");
            }

            if (string.Equals(request.Operation, "rollback", StringComparison.OrdinalIgnoreCase))
            {
                await seasonRevisionStore.Rollback(
                    request.SeasonId,
                    request.RevisionId,
                    activationAt);
            }
            else
            {
                await seasonRevisionStore.Activate(
                    request.SeasonId,
                    request.RevisionId,
                    activationAt);
            }

            string operation = request.Operation.Trim().ToLowerInvariant();
            string status = operation == "rollback" ? "rolled_back" : "activated";
            _writeLog(status, request, revision.RevisionHash);
            return new SeasonActivationResponse
            {
                Status = status,
                Operation = operation,
                SeasonId = request.SeasonId,
                RevisionId = request.RevisionId,
                RevisionHash = revision.RevisionHash,
                ActivatedAt = activationAt
            };
        }
        catch (Exception exception)
        {
            _writeLog("failed", request, request?.RevisionHash ?? string.Empty, exception.GetType().Name);
            throw;
        }
    }

    private async Task<SeasonRevision> _getValidatedRevision(SeasonActivationRequest request)
    {
        SeasonRevision? revision = await seasonRevisionStore.GetRevision(
            request.SeasonId,
            request.RevisionId);
        if (revision == null)
        {
            throw new InvalidDataException(
                $"Revision '{request.RevisionId}' for season '{request.SeasonId}' is not available.");
        }

        if (!string.Equals(revision.RevisionHash, request.RevisionHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Revision '{request.RevisionId}' does not match the requested content hash.");
        }

        return revision;
    }

    private static void _validateRequest(SeasonActivationRequest? request)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool validOperation = string.Equals(request.Operation, "activate", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(request.Operation, "rollback", StringComparison.OrdinalIgnoreCase);
        if (!validOperation ||
            string.IsNullOrWhiteSpace(request.SeasonId) ||
            string.IsNullOrWhiteSpace(request.RevisionId) ||
            string.IsNullOrWhiteSpace(request.RevisionHash))
        {
            throw new InvalidDataException(
                "Operation, seasonId, revisionId, and revisionHash are required; operation must be activate or rollback.");
        }
    }

    private static void _writeLog(
        string status,
        SeasonActivationRequest? request,
        string revisionHash,
        string? errorType = null)
    {
        string log = JsonSerializer.Serialize(new
        {
            eventName = "season_activation",
            status,
            operation = request?.Operation?.Trim().ToLowerInvariant(),
            seasonId = request?.SeasonId,
            revisionId = request?.RevisionId,
            revisionHash,
            errorType
        });
        Console.WriteLine(log);
    }
}
