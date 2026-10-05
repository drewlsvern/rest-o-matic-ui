using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using RestOMatic.Web.Features.Accounts.SignIn;

namespace RestOMatic.Web.Tests.Features.Accounts;

public sealed class PasswordSignInFormTests : IDisposable
{
    private readonly AppFactory _factory = new();
    private readonly BunitContext _context = new();
    private readonly IServiceScope _scope;

    public PasswordSignInFormTests()
    {
        _scope = _factory.Services.CreateScope();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
        _context.Services.AddSingleton(_scope.ServiceProvider.GetRequiredService<SignInHandler>());
        _context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [Fact]
    public void Show_password_reveals_and_hides_the_password()
    {
        var form = Render();
        Assert.Equal("password", PasswordInput(form).GetAttribute("type"));

        form.Find("button[aria-label='Show password']").Click();
        Assert.Equal("text", PasswordInput(form).GetAttribute("type"));

        form.Find("button[aria-label='Hide password']").Click();
        Assert.Equal("password", PasswordInput(form).GetAttribute("type"));
    }

    [Fact]
    public async Task A_wrong_password_shows_the_message_and_clears_the_field()
    {
        await _factory.CreateUserAsync("alice");
        var form = Render();

        form.FindAll("input")[0].Input("alice");
        PasswordInput(form).Input("Wrong-horse7");
        await form.Find("form").SubmitAsync();

        form.WaitForAssertion(() =>
            Assert.Equal("Incorrect user name or password.", form.Find("[data-testid='sign-in-error']").TextContent.Trim()));
        Assert.Equal("", PasswordInput(form).GetAttribute("value") ?? "");
    }

    [Fact]
    public async Task The_right_password_goes_on_to_complete_the_sign_in()
    {
        await _factory.CreateUserAsync("alice");
        var form = Render();

        form.FindAll("input")[0].Input("alice");
        PasswordInput(form).Input(AppFactory.Password);
        await form.Find("form").SubmitAsync();

        var navigation = _context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        form.WaitForAssertion(() => Assert.Contains(SignInEndpoints.CompletePath + "?ticket=", navigation.Uri));
        Assert.Empty(form.FindAll("[data-testid='sign-in-error']"));
    }

    private IRenderedComponent<PasswordSignInForm> Render() =>
        _context.Render<PasswordSignInForm>(parameters => parameters
            .Add(p => p.Context, new SignInContext("binding", "10.0.0.1", "/")));

    private static AngleSharp.Dom.IElement PasswordInput(IRenderedComponent<PasswordSignInForm> form) =>
        form.FindAll("input")[1];

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
        _factory.Dispose();
    }
}
