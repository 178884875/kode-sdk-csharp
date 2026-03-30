using KodaClaw.Contracts;

public static partial class GatewayApp
{
    private sealed record ValidatedModelEndpointRequest(
        string DisplayName,
        ModelProviderKind Provider,
        string ModelId,
        string? BaseUrl,
        string? ApiKeyEnvironmentVariable,
        string? ApiKeySecretRef,
        bool Enabled,
        ModelCapabilitySet Capabilities,
        int ContextWindowSize = 128_000,
        int MaxOutputTokens = 8192,
        bool IsReasoning = false,
        IReadOnlyDictionary<string, string>? CustomHeaders = null);

    private static bool TryValidateModelEndpointRequest(
        CreateModelEndpointRequest request,
        out ValidatedModelEndpointRequest validated,
        out ErrorResponse? error)
    {
        return TryValidateModelEndpointRequest(
            request.DisplayName,
            request.Provider,
            request.ModelId,
            request.BaseUrl,
            request.ApiKeyEnvironmentVariable,
            request.ApiKeySecretRef,
            request.Enabled,
            request.Capabilities,
            request.ContextWindowSize,
            request.MaxOutputTokens,
            request.IsReasoning,
            request.CustomHeaders,
            out validated,
            out error);
    }

    private static bool TryValidateModelEndpointRequest(
        UpdateModelEndpointRequest request,
        out ValidatedModelEndpointRequest validated,
        out ErrorResponse? error)
    {
        return TryValidateModelEndpointRequest(
            request.DisplayName,
            request.Provider,
            request.ModelId,
            request.BaseUrl,
            request.ApiKeyEnvironmentVariable,
            request.ApiKeySecretRef,
            request.Enabled,
            request.Capabilities,
            request.ContextWindowSize,
            request.MaxOutputTokens,
            request.IsReasoning,
            request.CustomHeaders,
            out validated,
            out error);
    }

    private static bool TryValidateModelEndpointRequest(
        string displayName,
        ModelProviderKind provider,
        string modelId,
        string? baseUrl,
        string? apiKeyEnvironmentVariable,
        string? apiKeySecretRef,
        bool enabled,
        ModelCapabilitySet capabilities,
        int contextWindowSize,
        int maxOutputTokens,
        bool isReasoning,
        IReadOnlyDictionary<string, string>? customHeaders,
        out ValidatedModelEndpointRequest validated,
        out ErrorResponse? error)
    {
        validated = default!;
        error = null;

        var normalizedDisplayName = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            error = new ErrorResponse(
                Code: "validation.model_display_name_required",
                Message: "Model display name is required.");
            return false;
        }

        var normalizedModelId = modelId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedModelId))
        {
            error = new ErrorResponse(
                Code: "validation.model_id_required",
                Message: "Model id is required.");
            return false;
        }

        var normalizedBaseUrl = NormalizeOptionalString(baseUrl);
        if (IsCompatibleProvider(provider) && normalizedBaseUrl is null)
        {
            error = new ErrorResponse(
                Code: "validation.model_base_url_required",
                Message: "Compatible providers require a base URL.");
            return false;
        }

        if (normalizedBaseUrl is not null && !TryNormalizeModelBaseUrl(normalizedBaseUrl, out normalizedBaseUrl, out error))
        {
            return false;
        }

        var normalizedApiKeyVariable = NormalizeOptionalString(apiKeyEnvironmentVariable);
        if (normalizedApiKeyVariable is not null && !TryNormalizeApiKeyEnvironmentVariable(normalizedApiKeyVariable, out normalizedApiKeyVariable, out error))
        {
            return false;
        }

        var normalizedApiKeySecretRef = NormalizeOptionalString(apiKeySecretRef);
        if (normalizedApiKeySecretRef is not null && !TryNormalizeApiKeySecretRef(normalizedApiKeySecretRef, out normalizedApiKeySecretRef, out error))
        {
            return false;
        }

        if (customHeaders is { Count: > 0 })
        {
            if (customHeaders.Count > 10)
            {
                error = new ErrorResponse(
                    Code: "validation.model_custom_headers_too_many",
                    Message: "Custom headers must not exceed 10 entries.");
                return false;
            }

            foreach (var (key, value) in customHeaders)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    error = new ErrorResponse(
                        Code: "validation.model_custom_headers_invalid_key",
                        Message: "Custom header keys must not be empty or whitespace.");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    error = new ErrorResponse(
                        Code: "validation.model_custom_headers_invalid_value",
                        Message: "Custom header values must not be empty or whitespace.");
                    return false;
                }
            }
        }

        var normalizedCustomHeaders = customHeaders is { Count: > 0 } ? customHeaders : null;

        validated = new ValidatedModelEndpointRequest(
            DisplayName: normalizedDisplayName,
            Provider: provider,
            ModelId: normalizedModelId,
            BaseUrl: normalizedBaseUrl,
            ApiKeyEnvironmentVariable: normalizedApiKeyVariable,
            ApiKeySecretRef: normalizedApiKeySecretRef,
            Enabled: enabled,
            Capabilities: capabilities,
            ContextWindowSize: contextWindowSize,
            MaxOutputTokens: maxOutputTokens > 0 ? maxOutputTokens : 8192,
            IsReasoning: isReasoning,
            CustomHeaders: normalizedCustomHeaders);
        return true;
    }

    private static bool TryNormalizeModelBaseUrl(
        string rawValue,
        out string normalized,
        out ErrorResponse? error)
    {
        error = null;
        normalized = rawValue;
        if (!Uri.TryCreate(rawValue, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = new ErrorResponse(
                Code: "validation.model_base_url_invalid",
                Message: "Model base URL must be an absolute http or https URL.");
            return false;
        }

        normalized = uri.ToString().TrimEnd('/');
        return true;
    }

    private static bool TryNormalizeApiKeyEnvironmentVariable(
        string rawValue,
        out string normalized,
        out ErrorResponse? error)
    {
        error = null;
        normalized = rawValue;

        if (rawValue.Any(char.IsWhiteSpace))
        {
            error = new ErrorResponse(
                Code: "validation.model_api_key_environment_variable_invalid",
                Message: "API key environment variable must not contain whitespace.");
            return false;
        }

        if (!rawValue.All(static ch => char.IsLetterOrDigit(ch) || ch == '_') ||
            !(char.IsLetter(rawValue[0]) || rawValue[0] == '_'))
        {
            error = new ErrorResponse(
                Code: "validation.model_api_key_environment_variable_invalid",
                Message: "API key environment variable must use letters, digits, and underscores, and cannot start with a digit.");
            return false;
        }

        return true;
    }

    private static bool TryNormalizeApiKeySecretRef(
        string rawValue,
        out string normalized,
        out ErrorResponse? error)
    {
        error = null;
        normalized = rawValue;

        if (!SecretRef.TryParse(rawValue, out var secretRef))
        {
            error = new ErrorResponse(
                Code: "validation.model_api_key_secret_ref_invalid",
                Message: "API key secret ref must use the '<provider>:<scope>:<key>' format.");
            return false;
        }

        normalized = secretRef.ToReferenceString();
        return true;
    }

    private static string? NormalizeOptionalString(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool IsCompatibleProvider(ModelProviderKind provider)
    {
        return provider is ModelProviderKind.OpenAICompatible or ModelProviderKind.AnthropicCompatible;
    }
}
