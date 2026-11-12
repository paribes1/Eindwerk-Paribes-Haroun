using Unity.Netcode.Components;

namespace Eindwerk.Multiplayer
{
    /// <summary>
    /// Immediate local movement, replicated to the server and other clients.
    /// Intended for a mechanics prototype; competitive games need server movement validation.
    /// </summary>
    public sealed class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
