using App.Shared.RCL.Services;

using FluentAssertions;

namespace App.Shared.RCL.Tests;

public sealed class MobileBackHandlerTests
{
    [Fact]
    public async Task RequestBack_Returns_False_Without_Handler()
    {
        var handler = new MobileBackHandler();

        (await handler.RequestBackAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RequestBack_Delegates_To_Handler()
    {
        var handler = new MobileBackHandler();
        handler.SetHandler(() => Task.FromResult(true));

        (await handler.RequestBackAsync()).Should().BeTrue();

        handler.SetHandler(null);

        (await handler.RequestBackAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RequestBack_Consumes_Handler_Errors()
    {
        var handler = new MobileBackHandler();
        handler.SetHandler(() => Task.FromException<bool>(new InvalidOperationException("boom")));

        // An overlay may still be open, so never let the error exit the app.
        (await handler.RequestBackAsync()).Should().BeTrue();
    }

    [Fact]
    public void First_Back_Attempt_Asks_For_Confirmation()
    {
        var handler = new MobileBackHandler();
        var now = DateTimeOffset.UtcNow;

        handler.RegisterBackAttempt(now).Should().BeFalse();
    }

    [Fact]
    public void Second_Press_Inside_Window_Confirms_Exit()
    {
        var handler = new MobileBackHandler();
        var now = DateTimeOffset.UtcNow;

        handler.RegisterBackAttempt(now).Should().BeFalse();
        handler.RegisterBackAttempt(now.AddSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void Stale_First_Press_Does_Not_Confirm_Exit()
    {
        var handler = new MobileBackHandler();
        var now = DateTimeOffset.UtcNow;

        handler.RegisterBackAttempt(now).Should().BeFalse();
        handler.RegisterBackAttempt(now.Add(MobileBackHandler.ExitConfirmWindow).AddSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void Reset_Clears_Pending_Confirmation()
    {
        var handler = new MobileBackHandler();
        var now = DateTimeOffset.UtcNow;

        handler.RegisterBackAttempt(now).Should().BeFalse();
        handler.ResetExitState();
        handler.RegisterBackAttempt(now.AddSeconds(1)).Should().BeFalse();
    }
}
