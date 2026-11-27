"""Controleert de startargumenten zonder de game te openen."""

import argparse
from pathlib import Path
import unittest
from start_multiplayer import build_command, ipv4, udp_port


class LauncherTests(unittest.TestCase):
    def test_server_without_local_player(self):
        command = build_command(Path("game met spaties.exe"), "server", "127.0.0.1", 7777, Path("server.log"))
        self.assertEqual(command[0], "game met spaties.exe")
        self.assertEqual(command[command.index("-networkMode") + 1], "server")
        self.assertIn("-batchmode", command)
        self.assertIn("-nographics", command)

    def test_client_and_lan(self):
        for role, mode, layout in (("client", "client", "client-server"), ("lan-host", "host", "lan")):
            command = build_command(Path("game.exe"), role, "192.168.1.20", 8888, Path("test.log"))
            self.assertEqual(command[command.index("-networkMode") + 1], mode)
            self.assertEqual(command[command.index("-connectionMode") + 1], layout)
            self.assertNotIn("-batchmode", command)

    def test_address_and_port(self):
        self.assertEqual(ipv4("127.0.0.1"), "127.0.0.1")
        self.assertEqual(udp_port("65535"), 65535)
        for value in ("::1", "geen-adres", "999.1.1.1"):
            with self.assertRaises(argparse.ArgumentTypeError):
                ipv4(value)
        for value in ("0", "65536", "tekst"):
            with self.assertRaises(argparse.ArgumentTypeError):
                udp_port(value)


if __name__ == "__main__":
    unittest.main()
