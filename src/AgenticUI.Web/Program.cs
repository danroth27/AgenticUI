using AgenticUI.Web;
using AgenticUI.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Named HttpClient pointed at the AG-UI agent server (resolved via Aspire service discovery).
// AG-UI responses are Server-Sent Event streams that stay open for the whole agent run, so the
// standard resilience handler from ServiceDefaults is removed here: its 30 second total request
// timeout cuts long runs short, and its retries would silently re-run the agent.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental.
builder.Services.AddHttpClient("agentserver", client =>
{
    client.BaseAddress = new Uri("https+http://agentserver");
    client.Timeout = Timeout.InfiniteTimeSpan;
})
.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

// Helper that turns an AG-UI endpoint on the agent server into an IChatClient / UIAgent.
builder.Services.AddScoped<AgentServerConnection>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
