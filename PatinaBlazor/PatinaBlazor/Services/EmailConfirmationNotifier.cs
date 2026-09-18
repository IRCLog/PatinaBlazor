namespace PatinaBlazor.Services;

// A new, standalone singleton - deliberately not a reuse or refactor of IrcChatNotifier,
// per explicit direction. Shares that class's simple shape (a plain C# event any Blazor
// Server component can subscribe to) because that shape is already proven in this app for
// pushing server-side state into an interactive circuit in real time - not because this
// shares any code or instance with it.
//
// ConfirmEmail.razor calls Notify(userId) once a confirmation succeeds; the storage
// signup wizard's email-verification step subscribes and advances itself immediately,
// without the customer needing to manually refresh or click "I've verified."
//
// In-process only - correct for this app's current single-instance deployment. Wouldn't
// reach a subscriber connected to a different server instance without a backplane
// (Redis/SQL) - not a real concern here, worth remembering if that ever changes.
public class EmailConfirmationNotifier
{
    public event Action<string>? OnConfirmed;

    public void Notify(string userId) => OnConfirmed?.Invoke(userId);
}
