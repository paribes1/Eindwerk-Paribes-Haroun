# Surfmap en beweging

## Starten en besturen

Open `Assets/Scenes/Map.unity`, druk op Play en gebruik het bestaande Host/Client-menu.
WASD beweegt, de muis kijkt rond en Shift sprint. Ctrl/C hurkt.
Houd Spatie vast om na elke landing opnieuw te springen.
Surf met A/D en rustige muisbewegingen; laat W los op de ramp.
Escape geeft de muis vrij. De bestaande spelinterface blijft behouden.

## LAN of client-server

In het bestaande menu staan twee keuzes naast elkaar:

- **LAN:** kies Host starten op één pc. Die speler beheert de sessie en speelt mee.
  Andere spelers kiezen Client verbinden met het LAN-adres van die pc.
- **Client-server:** kies Server starten op een aparte instantie. Deze server speelt
  niet mee. Alle spelers kiezen Client verbinden met het IPv4-adres van de server.

Beide gebruiken dezelfde C#-netwerkcode en UDP-poort, standaard 7777. Gebruik op
dezelfde pc `127.0.0.1`. Stop de sessie voordat je van keuze of poort wisselt.
De server beheert verbindingen, startplaatsen en de spelerlijst. De spelerbeweging
wordt nog door de eigen client berekend; dit is dus nog geen servercontrole tegen valsspelen.

Voor verbindingen buiten het LAN moet het serveradres bereikbaar zijn en de UDP-poort
door de firewall en eventueel router worden doorgelaten. Er is geen automatische
relay of matchmaking. De lokale tests bewijzen geen werking over het internet.

### Starten met Python

Maak eerst de Windows-build via `Tools > Player Mechanics > Maak multiplayer testbuild`.
Python start die Unity-build; Python is zelf geen tweede gameserver. Er zijn geen
extra Pythonpakketten nodig. Voer dit uit vanuit de projectmap:

```text
python Tools/start_multiplayer.py server
python Tools/start_multiplayer.py client --address 127.0.0.1
python Tools/start_multiplayer.py lan-host
```

Met `--port 8888` kies je een andere poort; gebruik dezelfde poort op beide kanten.
`--build "pad/naar/game.exe"` kiest een andere build. `--preview` toont de opdracht.
De server draait zonder beeld; Ctrl+C stopt de starter en server.
Elke start schrijft een eigen bestand met datum en tijd in `Logs`.
Met `--log "pad/test.log"` kies je zelf een logbestand. Gebruik voor meerdere spelers
aparte game-instanties. In Unity kun je de clients ook via ParrelSync openen.

Dezelfde opties werken rechtstreeks op de gamebuild:
`-connectionMode client-server -networkMode server -networkPort 7777`.
Voor een client gebruik je `-networkMode client -networkAddress 127.0.0.1`.

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
- `Tools/start_multiplayer.py`: start de game als LAN-host, server of client.
- `KitsuneSurfSceneBuilder.cs`: bouwt de opgeslagen surfmap opnieuw.

## Controleren

Gebruik `Tools > Surf > Validate movement` voor beweging en botsingen.
De sprinttests vergelijken lopen met sprinten en controleren snelheid in de lucht
en tijdens hurken. `Tools > Surf > Validate Play Mode Integration` controleert
de sessie en de checkpoints. Resultaten staan in `Logs/SurfMovementValidation.txt`
en `Logs/SurfPlayValidation.txt`.
De aparte servercontrole start met
`Eindwerk.PlayerMechanics.Editor.SurfPlayValidation.BeginClientServer`.
De Pythonstarter test je met `python -m unittest discover -s Tools -p "test_*.py"`.
