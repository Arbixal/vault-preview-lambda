using SeasonConfigCli.Request;
using SeasonConfigCli.Response;

namespace SeasonConfigCli;

public interface ILambdaInvoker
{
    Task<SeasonActivationResponse> InvokeAsync(
        string functionName,
        SeasonActivationRequest request,
        CancellationToken cancellationToken = default);
}
