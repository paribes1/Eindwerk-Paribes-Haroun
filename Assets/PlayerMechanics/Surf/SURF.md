# Surfmap en beweging

## Starten en besturen

Open `Assets/Scenes/Map.unity`, druk op Play en gebruik het bestaande Host/Client-menu.
WASD beweegt, de muis kijkt rond en Shift sprint. Ctrl/C hurkt.
Houd Spatie vast om na elke landing opnieuw te springen.
Surf met A/D en rustige muisbewegingen; laat W los op de ramp.
Escape geeft de muis vrij. De bestaande spelinterface blijft behouden.

## Snelheid en sprint

In de gewone testscène zijn lopen en sprinten standaard 4,5 en 7,5 m/s.
De surfmap loopt standaard met 8 m/s. Sprint gebruikt dezelfde verhouding:
`8 × (7,5 / 4,5) = 13,33 m/s`. Aanpassen kan bij `walkSpeed`, `sprintSpeed`
en `surfWishSpeed` in de spelerinstellingen. Sprint wordt nooit trager dan lopen.

Shift verhoogt de gewenste snelheid op de grond. Het vermenigvuldigt de bestaande
snelheid niet direct: in de lucht en op rampen blijft de opgebouwde snelheid behouden.
Hurken blijft trager, ook met Shift. De oude fout was dat Shift bij surf juist
een factor 0,52 gebruikte en daardoor vertraagde.

## Rampen en checkpoints

Rampen zijn ongeveer 55° steil. Grond tot 46° is beloopbaar; steilere vlakken laten
de speler glijden. Bij een botsing verdwijnt alleen de snelheid het oppervlak in.
Zwaartekracht is 20,32 m/s². De veilige snelheidsgrens is 88,9 m/s.

Elk bereikt checkpoint wordt de nieuwe startplek voor die speler.
R of een val onder de grens van het huidige deel zet de speler daar terug en wist
de snelheid. Het checkpoint maakt geen spelers, camera's of extra interface aan.

## Waar staat de code?

- `CapsulePlayerController.cs`: invoer, lopen, sprinten, surf, hurken en camera.
- `KitsuneSurfCourse.cs`: doelen, checkpoints en terugzetten na een val.
- `NetworkPlayer.cs`: zet lokale besturing aan voor de eigenaar van de speler.
- `MultiplayerSession.cs`: het bestaande Host/Client-menu en verbindingen.
- `KitsuneSurfSceneBuilder.cs`: bouwt de opgeslagen surfmap opnieuw.

## Controleren

Gebruik `Tools > Surf > Validate movement` voor beweging en botsingen.
De sprinttests vergelijken lopen met sprinten en controleren snelheid in de lucht
en tijdens hurken. `Tools > Surf > Validate Play Mode Integration` controleert
de sessie en de checkpoints. Resultaten staan in `Logs/SurfMovementValidation.txt`
en `Logs/SurfPlayValidation.txt`.
