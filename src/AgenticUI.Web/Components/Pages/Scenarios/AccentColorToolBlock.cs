using Microsoft.AspNetCore.Components.AI;

namespace AgenticUI.Web.Components.Pages.Scenarios;

[ToolBlock("set_accent_color")]
public partial class AccentColorToolBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "color")]
    public string? Color { get; set; }

    [ToolResult]
    public string? Confirmation { get; set; }
}
