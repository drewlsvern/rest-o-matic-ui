using System.Text.Json;

namespace RestOMatic.Web.Features.Hosts.Contract;

/// <summary>The contract's error codes, and replies that carry them.</summary>
public static class ContractErrors
{
    public const string TokenRejected = "token_rejected";
    public const string CredentialRejected = "credential_rejected";
    public const string UnsupportedFormat = "unsupported_format";
    public const string InvalidRequest = "invalid_request";

    // Codes of this app's own. The contract allows any code with another status.
    public const string RequestTooLarge = "request_too_large";
    public const string RateLimited = "rate_limited";

    public static IResult Reply(int status, string code, string message) =>
        Results.Json(new ErrorBody(new ErrorDetail(code, message)), ContractJson.Options, statusCode: status);

    public static Task WriteAsync(HttpResponse response, int status, string code, string message)
    {
        response.StatusCode = status;
        return response.WriteAsJsonAsync(new ErrorBody(new ErrorDetail(code, message)), ContractJson.Options);
    }
}

/// <summary>A request as read: the message, or the error reply to send instead and its status.</summary>
public readonly record struct Read<T>(T? Message, IResult? Error, int ErrorStatus = 0) where T : class;

/// <summary>
/// Reads a host's request so that every failure gets the contract's error
/// body: an oversized or broken body, a format version this app does not
/// speak, or a message missing a field the schema requires.
/// </summary>
public static class ContractRequests
{
    /// <param name="maxBytes">The largest body accepted, after decompression; null for no limit of its own.</param>
    public static async Task<Read<T>> ReadAsync<T>(HttpRequest request, long? maxBytes, CancellationToken cancellationToken)
        where T : class
    {
        using var body = new MemoryStream();
        try
        {
            // The request decompression middleware has already unwrapped
            // gzip. The size is counted here as well as by the server, so the
            // limit holds whatever server runs the app, and stops reading as
            // soon as it is passed.
            var buffer = new byte[81920];
            int read;
            while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                body.Write(buffer, 0, read);
                if (body.Length > maxBytes)
                {
                    return TooLarge<T>();
                }
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge<T>();
        }
        catch (InvalidDataException)
        {
            return Fail<T>(StatusCodes.Status400BadRequest, ContractErrors.InvalidRequest, "The gzip-compressed body cannot be read.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body.ToArray());
        }
        catch (JsonException)
        {
            return Fail<T>(StatusCodes.Status400BadRequest, ContractErrors.InvalidRequest, "The body is not valid JSON.");
        }

        using (document)
        {
            // The format version is checked first: a message in another
            // version need not match anything else this version expects.
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("format_version", out var version))
            {
                return Fail<T>(StatusCodes.Status400BadRequest, ContractErrors.InvalidRequest, "The message has no format_version.");
            }
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != ContractJson.FormatVersion)
            {
                return Fail<T>(StatusCodes.Status400BadRequest, ContractErrors.UnsupportedFormat,
                    $"This app accepts format_version {ContractJson.FormatVersion}, not {version.GetRawText()}.");
            }

            try
            {
                var message = document.Deserialize<T>(ContractJson.Options)
                    ?? throw new JsonException("The message is null.");
                return new Read<T>(message, null);
            }
            catch (JsonException ex)
            {
                return Fail<T>(StatusCodes.Status400BadRequest, ContractErrors.InvalidRequest, Describe(ex));
            }
        }
    }

    /// <summary>The serializer's message names the missing property and where it was expected.</summary>
    private static string Describe(JsonException ex) =>
        ex.Path is { Length: > 0 } path && !ex.Message.Contains(path, StringComparison.Ordinal)
            ? $"{ex.Message} Path: {path}."
            : ex.Message;

    private static Read<T> TooLarge<T>() where T : class =>
        Fail<T>(StatusCodes.Status413PayloadTooLarge, ContractErrors.RequestTooLarge,
            "The request is larger than this app accepts (CheckIn:MaxRequestBytes).");

    private static Read<T> Fail<T>(int status, string code, string message) where T : class =>
        new(null, ContractErrors.Reply(status, code, message), status);
}
