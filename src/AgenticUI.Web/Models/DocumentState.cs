using System.Text.Json.Serialization;

namespace AgenticUI.Web.Models;

public sealed class DocumentState
{
    [JsonPropertyName("document")]
    public string Document { get; set; } = string.Empty;
}
