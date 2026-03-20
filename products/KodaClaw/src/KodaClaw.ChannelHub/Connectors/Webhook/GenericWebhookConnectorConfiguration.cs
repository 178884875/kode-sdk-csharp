using System.Text.Json;
using KodaClaw.Contracts;

namespace KodaClaw.ChannelHub.Connectors.Webhook;

internal sealed record GenericWebhookConnectorConfiguration(
    string? SharedSecret,
    ChannelThreadType DefaultThreadType)
{
    public static GenericWebhookConnectorConfiguration FromAccount(
        ChannelAccount account,
        ChannelSecretResolver? secretResolver = null)
    {
        ArgumentNullException.ThrowIfNull(account);

        var sharedSecretFromConfig = default(string);
        var credentialReferenceFromConfig = default(string);
        var defaultThreadType = ChannelThreadType.DirectMessage;

        if (!string.IsNullOrWhiteSpace(account.ConfigurationJson))
        {
            using var document = JsonDocument.Parse(account.ConfigurationJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Channel account configuration must be a JSON object.", nameof(account));
            }

            var root = document.RootElement;
            sharedSecretFromConfig = GetOptionalString(root, "sharedSecret")
                ?? GetOptionalString(root, "secret");
            credentialReferenceFromConfig = GetOptionalString(root, "credentialReference");

            var configuredDefaultThreadType = GetOptionalString(root, "defaultThreadType");
            if (!string.IsNullOrWhiteSpace(configuredDefaultThreadType))
            {
                defaultThreadType = GenericWebhookPayloadParser.ParseThreadType(
                    configuredDefaultThreadType,
                    defaultThreadType);
            }
        }

        var resolvedSecret = (secretResolver ?? new ChannelSecretResolver()).Resolve(
            sharedSecretFromConfig,
            credentialReferenceFromConfig,
            account.CredentialReference);

        return new GenericWebhookConnectorConfiguration(
            SharedSecret: resolvedSecret,
            DefaultThreadType: defaultThreadType);
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

            return null;
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
