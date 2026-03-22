using System.Text.Json;
using KodaClaw.Contracts;

namespace KodaClaw.ChannelHub.Connectors.Feishu;

internal sealed record FeishuConnectorConfiguration(
    string AppId,
    string AppSecret,
    DeliveryMode? DefaultDeliveryMode = null)
{
    public static FeishuConnectorConfiguration FromAccount(
        ChannelAccount account,
        ChannelSecretResolver? secretResolver = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.ConnectorKind != ChannelConnectorKind.Feishu)
        {
            throw new ArgumentException(
                $"Feishu connector cannot start account with connector kind '{account.ConnectorKind}'.",
                nameof(account));
        }

        var appIdFromConfig = default(string);
        var appSecretFromConfig = default(string);
        var credentialReferenceFromConfig = default(string);
        DeliveryMode? defaultDeliveryMode = null;

        if (!string.IsNullOrWhiteSpace(account.ConfigurationJson))
        {
            using var document = JsonDocument.Parse(account.ConfigurationJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Feishu account configuration must be a JSON object.", nameof(account));
            }

            var root = document.RootElement;
            appIdFromConfig = GetOptionalString(root, "appId");
            appSecretFromConfig = GetOptionalString(root, "appSecret");
            credentialReferenceFromConfig = GetOptionalString(root, "credentialReference");

            var configuredDeliveryMode = GetOptionalString(root, "defaultDeliveryMode");
            if (!string.IsNullOrWhiteSpace(configuredDeliveryMode)
                && Enum.TryParse<DeliveryMode>(configuredDeliveryMode, ignoreCase: true, out var parsed))
            {
                defaultDeliveryMode = parsed;
            }
        }

        var appId = NormalizeNullable(appIdFromConfig ?? account.ExternalAccountId);
        if (string.IsNullOrWhiteSpace(appId))
        {
            throw new ArgumentException(
                "Feishu app_id is required (set 'appId' in ConfigurationJson or ExternalAccountId).",
                nameof(account));
        }

        var resolver = secretResolver ?? new ChannelSecretResolver();
        var appSecret = resolver.Resolve(appSecretFromConfig, credentialReferenceFromConfig, account.CredentialReference);
        if (string.IsNullOrWhiteSpace(appSecret))
        {
            throw new ArgumentException(
                "Feishu app_secret is required and must resolve from configuration or credential reference.",
                nameof(account));
        }

        return new FeishuConnectorConfiguration(
            AppId: appId,
            AppSecret: appSecret,
            DefaultDeliveryMode: defaultDeliveryMode);
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                return NormalizeNullable(property.Value.GetString());
            }
        }

        return null;
    }

    private static string? NormalizeNullable(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
