# Portable Map

A minimal .NET MAUI map viewer for Android, iOS, Mac Catalyst, and Windows.

## Open a map

- **Open KMZ** reads a Google Earth KMZ containing a KML `GroundOverlay`, a `LatLonBox`, and an image stored in the archive. Non-zero overlay rotation and `LatLonQuad` overlays are not supported.
- **Open image + world file** selects exactly one PNG, JPEG, TIFF, or BMP image and its matching `.wld`, `.pgw`, `.jgw`, `.tfw`, `.bpw`, or image-extension-plus-`w` world file at the same time. World-file coordinates must be WGS 84 longitude and latitude; projected coordinates are not inferred.

The device location is shown as a semi-transparent blue ring with a 2 mm outer diameter. Drag to pan and pinch to zoom. Location access is requested while the app is in use.

Build with the .NET 11 SDK and the MAUI workload for the target platform. Android builds require the Android SDK and JDK 25; for a default Android Studio installation:

```powershell
$env:JAVA_HOME = "$env:ProgramFiles\Android\Android Studio\jbr"
dotnet build -f net11.0-android -p:TargetFrameworks=net11.0-android
```

On Windows, build the desktop target with:

```powershell
dotnet build -f net11.0-windows10.0.19041.0 -p:TargetFrameworks=net11.0-windows10.0.19041.0
```
