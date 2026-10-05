namespace RestOMatic.Web.Features.Accounts.SignIn;

/// <summary>
/// One way of signing in, shown on the sign-in page in <see cref="Order"/>.
/// <see cref="Component"/> is rendered with a single <see cref="SignInContext"/>
/// parameter named <c>Context</c>. Password sign-in is the only method today;
/// an OIDC provider would register a button here and, in its callback, start
/// the session through <c>AccountSessions.SignInAsync</c>.
/// </summary>
public sealed record SignInMethod(string Id, int Order, Type Component);

/// <summary>What a sign-in method needs from the page it is on.</summary>
/// <param name="BrowserBinding">The value a sign-in ticket is bound to.</param>
/// <param name="ClientAddress">Where the attempt comes from, for throttling.</param>
/// <param name="ReturnUrl">Where to go once signed in.</param>
public sealed record SignInContext(string BrowserBinding, string ClientAddress, string? ReturnUrl);
