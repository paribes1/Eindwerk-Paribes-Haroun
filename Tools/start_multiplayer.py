"""Start de gebouwde Unity-game als LAN-host, aparte server of client."""

import argparse
from datetime import datetime
import ipaddress
from pathlib import Path
import subprocess


def ipv4(value):
    """Unity gebruikt hier alleen IPv4-adressen."""
    try:
        return str(ipaddress.IPv4Address(value))
    except ipaddress.AddressValueError as error:
        raise argparse.ArgumentTypeError("Gebruik een geldig IPv4-adres.") from error


def udp_port(value):
    try:
        port = int(value)
    except ValueError as error:
        raise argparse.ArgumentTypeError("De poort moet een getal zijn.") from error
    if not 1 <= port <= 65535:
        raise argparse.ArgumentTypeError("Kies een poort tussen 1 en 65535.")
    return port


def build_command(build, role, address, port, log):
    """Geef losse argumenten door. Er wordt geen shell gebruikt."""
    mode = "host" if role == "lan-host" else role
    layout = "lan" if role == "lan-host" else "client-server"
    command = [str(build), "-connectionMode", layout, "-networkMode", mode,
               "-networkAddress", address, "-networkPort", str(port), "-logFile", str(log)]
    if role == "server":
        command += ["-batchmode", "-nographics"]
    return command


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("role", choices=("server", "client", "lan-host"))
    parser.add_argument("--build", type=Path,
                        default=root / "Builds/MechanicsSmoke/MechanicsSmoke.exe")
    parser.add_argument("--address", type=ipv4, default="127.0.0.1")
    parser.add_argument("--port", type=udp_port, default=7777)
    parser.add_argument("--log", type=Path, help="Eigen pad voor het logbestand.")
    parser.add_argument("--preview", action="store_true", help="Toon de opdracht zonder te starten.")
    args = parser.parse_args()
    build = args.build.resolve()
    if not build.is_file():
        parser.error("Gamebuild ontbreekt. Maak eerst de multiplayer-testbuild in Unity.")
    log = args.log.resolve() if args.log else root / "Logs" / f"{args.role}-{datetime.now():%Y%m%d-%H%M%S-%f}.log"
    command = build_command(build, args.role, args.address, args.port, log)
    if args.preview:
        print(subprocess.list2cmdline(command))
        return 0
    log.parent.mkdir(parents=True, exist_ok=True)
    try:
        return subprocess.run(command, cwd=build.parent, check=False).returncode
    except KeyboardInterrupt:
        return 130
    except OSError as error:
        parser.error(str(error))


if __name__ == "__main__":
    raise SystemExit(main())
