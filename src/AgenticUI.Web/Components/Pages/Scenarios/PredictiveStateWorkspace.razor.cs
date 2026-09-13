using AgenticUI.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.AI;

namespace AgenticUI.Web.Components.Pages.Scenarios;

public partial class PredictiveStateWorkspace
{
    private IDisposable? _stateChangedRegistration;
    private IDisposable? _statusChangedRegistration;
    private string _runStartDocument = string.Empty;
    private ConversationStatus _lastStatus;

    [CascadingParameter]
    public AgentContext AgentContext { get; set; } = default!;

    [Parameter, EditorRequired]
    public UIAgent<DocumentState> Agent { get; set; } = default!;

    [Parameter, EditorRequired]
    public EventCallback ResetRequested { get; set; }

    private bool IsRunning => IsRunningStatus(AgentContext.Status);

    protected override void OnInitialized()
    {
        _runStartDocument = Agent.State.Value.Document;
        _lastStatus = AgentContext.Status;
        _stateChangedRegistration = Agent.State.OnChanged(HandleStateChanged);
        _statusChangedRegistration =
            AgentContext.RegisterOnStatusChanged(HandleConversationStatusChanged);
    }

    private void UpdateDocument(string document)
    {
        if (!IsRunning)
        {
            Agent.State.Value = new DocumentState { Document = document };
        }
    }

    private Task ResetAsync() => ResetRequested.InvokeAsync();

    private async void HandleStateChanged()
    {
        try
        {
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception exception)
        {
            await DispatchExceptionAsync(exception);
        }
    }

    private async void HandleConversationStatusChanged(ConversationStatus status)
    {
        try
        {
            await InvokeAsync(() =>
            {
                if (status == ConversationStatus.Streaming &&
                    !IsRunningStatus(_lastStatus))
                {
                    _runStartDocument = Agent.State.Value.Document;
                }

                if (status == ConversationStatus.Error &&
                    Agent.State.HasPendingPredictiveState)
                {
                    Agent.State.RejectPredictiveState();
                }

                if (status is ConversationStatus.Idle or ConversationStatus.Error)
                {
                    _runStartDocument = Agent.State.Value.Document;
                }

                _lastStatus = status;
                StateHasChanged();
            });
        }
        catch (Exception exception)
        {
            await DispatchExceptionAsync(exception);
        }
    }

    private static bool IsRunningStatus(ConversationStatus status) =>
        status is ConversationStatus.Streaming or ConversationStatus.AwaitingInput;

    public ValueTask DisposeAsync()
    {
        _stateChangedRegistration?.Dispose();
        _statusChangedRegistration?.Dispose();
        return ValueTask.CompletedTask;
    }
}
