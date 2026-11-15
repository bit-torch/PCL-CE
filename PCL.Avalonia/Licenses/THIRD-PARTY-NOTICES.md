# Third-party notices

The cross-platform desktop links the following media components dynamically:

- **LibVLCSharp / LibVLCSharp.Avalonia 3.10.2**, VideoLAN contributors,
  LGPL-2.1-or-later. Source: https://code.videolan.org/videolan/LibVLCSharp
  (NuGet source revision `3082c09ca955131c705fc4767fde03e9c4a14999`).
- **LibVLC**, when installed by the user: https://www.videolan.org/vlc/libvlc.html.
  LibVLC is not included in these desktop packages. The system installation's license
  and codec/plugin notices apply to that installation.

The LGPL 2.1 text is included in `LGPL-2.1.txt`. The managed binding assemblies remain
separate, replaceable DLLs. The source URLs above provide their corresponding source.

Other dependencies retain their own license information in their upstream projects
and NuGet packages. PCL.Core remains under the root Apache-2.0 license; the launcher
business code retains `LICENCE.Launcher`, copied alongside the application.
