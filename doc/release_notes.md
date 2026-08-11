## System Pre-requisites

- Windows 10 or newer (x64 or arm64)
- GPU or integrated graphics supporting Vulkan 1.2 or higher

## Where to Download

- winget
  ```
  winget install --id=StreetPea.chiaki-ng -e
  ```
- chocolatey
  ```
  choco install chiaki-ng
  ```
- Attached releases (installer and portable)

## Updates

### v1.10.0

This release provides the following improvements:

- improve streaming quality and stability with better queueing, reset handling, congestion control, and streaming metrics
- add an OpenGL renderer backend plus major renderer and libplacebo fixes, adaptive queue depth, and custom upscalers
- add VSync support plus spatial upscaler presets under settings->video->Render
- improve PSN remote play reliability with configurable NAT port guessing and holepunching fixes
- rework audio, microphone, and haptics paths for better recovery and fewer failure cases
- fix macOS ARM rendering, crash-on-exit, and controller input issues, and add iOS/tvOS compilation support
