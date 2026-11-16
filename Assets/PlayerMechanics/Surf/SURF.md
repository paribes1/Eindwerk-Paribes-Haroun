# Kitsune-inspired surf

Open `Assets/Scenes/Map.unity` and press Play. Solo practice starts automatically.
The course has nine stages, 24 collidable 55-degree ramps, checkpoint fall resets,
a run timer, completion state and a horizontal speedometer in Source units/second.

## Controls

- WASD / arrows: move; A/D and smooth mouse turns: surf. Release W on ramps.
- Hold Space: automatic bunnyhop; Ctrl/C: crouch; Shift: walk.
- R: retry the current checkpoint; T: restart the entire run.
- Escape: free the cursor and show the LAN menu; click outside the menu to resume.
- Gamepad: left stick moves, right stick looks, A jumps, right-stick click crouches.

For LAN, open the menu and start a host or connect to the host IPv4 and UDP port.
Solo practice is suspended during a network session and resumes after disconnecting.
Surf checkpoints are local to each player. Network movement retains the project's
existing owner-authoritative controller and transform replication.

## Authoring and checks

- `Tools > Surf > Build Kitsune Course`: rebuild the saved Map geometry and assets.
- `Tools > Surf > Validate movement`: real capsule collision/momentum checks.
- `Tools > Surf > Validate Course Geometry`: saved ramp meshes, normals and spawns.
- `Tools > Surf > Validate Play Mode Integration`: solo, fall reset, all goals,
  restart, loopback LAN host, ownership, camera/audio, and solo restoration.

Reports are saved in `Logs/SurfMovementValidation.txt`, `Logs/SurfEditorReport.txt`
and `Logs/SurfPlayValidation.txt`. The ordinary Player Mechanica scene retains its
original movement settings. Surf is enabled by the Map course at player spawn.

## References and limits

This is an original Kitsune-inspired Unity course with dark tiled ramps and orange
trim, not an import of the original map's geometry or assets.
Reference: [surf_kitsune official CS2 port](https://steamcommunity.com/sharedfiles/filedetails/?id=3076153623).
Movement follows Source-style projected wish-speed acceleration and collision-plane
clipping, with gravity 800 u/s², air acceleration 150, a 30 u/s air wish-speed cap,
and a 3500 u/s safety speed limit (1 Source unit = 0.0254 m).
Reference: [Valve Source movement code](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/shared/gamemovement.cpp).
Unity CharacterController contacts and render-frame substeps are an approximation
of CS surf; this does not reproduce CS2 subtick simulation exactly.
