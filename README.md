
![chiaki-ng Logo](gui/res/chiaking-logo.svg)

# [chiaki-ng](https://streetpea.github.io/chiaki-ng/)

An open source PlayStation remote play project serving as the next-generation of Chiaki with improvements and ongoing support now that the original Chiaki project is in maintenance mode only. [Click here to see the accompanying site for documentation, updates and more](https://streetpea.github.io/chiaki-ng/).

## Discord
[chiaki-ng community Discord](https://discord.gg/tAMbRuwXDH)

## Disclaimer
This project is not endorsed or certified by Sony Interactive Entertainment LLC.

Chiaki is a Free and Open Source Software Client for PlayStation 4 and PlayStation 5 Remote Play.
This fork targets **Windows only**; the Linux, FreeBSD, OpenBSD, Android, macOS, Nintendo Switch and
Steam Deck ports have been removed.

## Hardware

The tuning goes to NVIDIA first, and first is not only: the decode, renderer and present paths that
must keep working with no NVIDIA card are stated in
[docs/HARDWARE-CONTRACT.md](docs/HARDWARE-CONTRACT.md).

## Building

Windows builds are done in MSYS2/MinGW64; see [doc/platform-build.md](doc/platform-build.md).
