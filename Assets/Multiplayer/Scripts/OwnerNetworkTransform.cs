using Unity.Netcode.Components;

namespace Eindwerk.Multiplayer
{
    /// <summary>
    /// Laat de eigenaar direct bewegen en deelt de positie met de andere spelers.
    /// Dit prototype controleert de beweging nog niet op de server.
    /// </summary>
    public sealed class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
