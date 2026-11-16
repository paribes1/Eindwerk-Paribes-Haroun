# Surf map and movement

Open `Assets/Scenes/Map.unity` and use the original Host/Client menu.
The original interface and multiplayer session flow are unchanged.

The map uses Kitsune-inspired ramps. Player movement adds air strafing, momentum
preservation, steep-ramp sliding and held-Space bunnyhopping. Use A/D and smooth
mouse turns on ramps; release W while surfing. Ctrl/C crouches and Shift walks.

Reaching the next checkpoint updates the player's respawn. R and falling below
the map return the player to the current checkpoint with momentum cleared.

The checkpoint component draws no UI and creates no players or cameras.
There is no added timer, speed display, progress overlay, or solo-practice mode.

Editor checks remain under `Tools > Surf`; test reports are written to `Logs`.
