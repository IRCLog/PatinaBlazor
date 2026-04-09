using PatinaBlazor.Data;

namespace PatinaBlazor.Services;

public class IrcChatNotifier
{
    public event Action<IrcEvent>? OnEventLogged;

    public void Notify(IrcEvent evt) => OnEventLogged?.Invoke(evt);
}
