using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestrator.Api.Serialization;

/// <summary>Wire-format configuration.</summary>
public static class JsonConfig
{
    /// <summary>
    /// Apply the contract's naming rules.
    /// </summary>
    /// <remarks>
    /// Two different naming policies are in play, which is easy to miss and impossible to
    /// express with one setting: <b>property names are camelCase</b> (<c>riskLevel</c>,
    /// <c>missingEvidence</c>) while <b>enum values are snake_case</b>
    /// (<c>blocked_pending_approval</c>, <c>not_requested</c>). The converter carries its own
    /// policy so both hold at once.
    /// </remarks>
    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = false;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    }
}
