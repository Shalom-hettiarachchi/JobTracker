using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace JobTracker.Components;

/// <summary>Base class for components that work with the signed-in user's data.</summary>
public abstract class AuthedComponent : ComponentBase
{
    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    /// <summary>The signed-in user's id, or an empty string when nobody is signed in.</summary>
    protected string UserId { get; private set; } = "";

    protected override async Task OnInitializedAsync()
    {
        if (AuthState is not null)
        {
            var state = await AuthState;
            UserId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        }

        if (UserId.Length > 0)
        {
            await OnReadyAsync();
        }
    }

    /// <summary>Runs once, after <see cref="UserId"/> is known.</summary>
    protected virtual Task OnReadyAsync() => Task.CompletedTask;
}
