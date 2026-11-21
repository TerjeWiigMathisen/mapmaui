# Portable Map

A minimal .NET MAUI map viewer for Android, iOS, Mac Catalyst, and Windows.

## Open a map

- **Open KMZ** reads a Google Earth KMZ containing a KML `GroundOverlay`, a `LatLonBox`, and an image stored in the archive. Non-zero overlay rotation and `LatLonQuad` overlays are not supported.
- **Open image + world file** selects exactly one PNG, JPEG, TIFF, or BMP image and its matching `.wld`, `.pgw`, `.jgw`, `.tfw`, `.bpw`, or image-extension-plus-`w` world file at the same time. World files with coordinates outside longitude/latitude ranges are interpreted as ETRS89 / UTM zone 32N (EPSG:25832); otherwise, coordinates are treated as WGS 84 longitude and latitude.

The map requests location access while it is open and updates the marker from the device's live location. The marker is a semi-transparent blue ring with a 2 mm outer diameter; it appears when the current location falls within the loaded map. Drag to pan and pinch to zoom around the pinch point on touch devices. On Windows, use the left mouse button to drag and the mouse wheel over the map viewport to zoom around the pointer. Windows input uses the map control's DPI-scaled pointer coordinates, so interactions stay aligned on scaled displays. Over the surrounding empty canvas, zoom anchors to the nearest image edge. Zoom ranges from 0.05x to 100x.

Build with the .NET 11 SDK and the MAUI workload for the target platform. Android builds require the Android SDK and JDK 25; for a default Android Studio installation:

```powershell
$env:JAVA_HOME = "$env:ProgramFiles\Android\Android Studio\jbr"
dotnet build -f net11.0-android -p:TargetFrameworks=net11.0-android
```

On Windows, build the desktop target with:

```powershell
dotnet build -f net11.0-windows10.0.19041.0 -p:TargetFrameworks=net11.0-windows10.0.19041.0
```
