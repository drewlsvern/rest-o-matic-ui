using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace RestOMatic.Web.Features.Accounts.SignIn;

/// <summary>
/// For pages that sign someone in (sign-in and setup). The browser binding
/// and the client's address are only known from the HTTP request, which
/// exists while the page is prerendered. They are carried over to the
/// interactive page as persisted component state, which the server protects
/// from tampering.
/// </summary>
public abstract class SignInPageBase : ComponentBase, IDisposable
{
    private const string StateKey = "sign-in-context";

    private PersistingComponentStateSubscription _subscription;

    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    [Inject]
    private PersistentComponentState State { get; set; } = default!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery]
    public string? ReturnUrl { get; set; }

    /// <summary>Null only when the page was reached without a full page load; it then reloads itself.</summary>
    protected SignInContext? Context { get; private set; }

    protected override void OnInitialized()
    {
        if (HttpContext is not null)
        {
            Context = new SignInContext(
                BrowserBinding.For(HttpContext) ?? "",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                ReturnUrls.Local(ReturnUrl));
            _subscription = State.RegisterOnPersisting(() =>
            {
                State.PersistAsJson(StateKey, Context);
                return Task.CompletedTask;
            }, RenderMode.InteractiveServer);
        }
        else if (State.TryTakeFromJson<SignInContext>(StateKey, out var context))
        {
            Context = context;
        }
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender && Context is null && HttpContext is null)
        {
            Navigation.Refresh(forceReload: true);
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
        GC.SuppressFinalize(this);
    }
}
