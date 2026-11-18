using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Storage;
using GraphicsImage = Microsoft.Maui.Graphics.IImage;

namespace PortableMap;

public sealed partial class MainPage : ContentPage
{
    private static readonly FilePickerFileType KmzFileType = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = new[] { "application/vnd.google-earth.kmz", "application/zip", "application/octet-stream" },
            [DevicePlatform.iOS] = new[] { "com.google.earth.kmz", "public.zip-archive" },
            [DevicePlatform.MacCatalyst] = new[] { "com.google.earth.kmz", "public.zip-archive" },
            [DevicePlatform.WinUI] = new[] { ".kmz" }
        });

    private static readonly FilePickerFileType ImageAndWorldFileTypes = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = new[] { "image/*", "text/plain", "application/octet-stream" },
            [DevicePlatform.iOS] = new[] { "public.image", "public.data" },
            [DevicePlatform.MacCatalyst] = new[] { "public.image", "public.data" },
            [DevicePlatform.WinUI] = new[] { ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".pgw", ".jgw", ".tfw", ".bpw", ".pngw", ".jpgw", ".jpegw", ".tifw", ".bmpw", ".wld" }
        });

    private readonly MapDrawable _map = new();
    private readonly GraphicsView _mapView;
    private readonly Label _status;
    private double _panStartX;
    private double _panStartY;
    private double _pinchStartZoom = 1;

    public MainPage()
    {
        Title = "Portable Map";
        BackgroundColor = Colors.White;

        _mapView = new GraphicsView
        {
            Drawable = _map,
            BackgroundColor = Color.FromArgb("#E9EDF0"),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinchUpdated;
        _mapView.GestureRecognizers.Add(pan);
        _mapView.GestureRecognizers.Add(pinch);

        var openKmz = new Button { Text = "Open KMZ" };
        openKmz.Clicked += OpenKmzClicked;
        var openImage = new Button { Text = "Open image + world file" };
        openImage.Clicked += OpenImageClicked;

        _status = new Label
        {
            Text = "Open a KMZ, or select an image and its world file.",
            Padding = new Thickness(12, 8),
            FontSize = 13,
            TextColor = Color.FromArgb("#303840")
        };

        var topControls = new HorizontalStackLayout
        {
            Padding = new Thickness(8, 4, 8, 0),
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Fill,
            Children = { openKmz }
        };
        var controls = new VerticalStackLayout
        {
            Spacing = 0,
            Children = { topControls, openImage }
        };
        openKmz.HorizontalOptions = LayoutOptions.Fill;
        openImage.HorizontalOptions = LayoutOptions.Fill;

        var pageLayout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
        pageLayout.Add(controls, 0, 0);
        pageLayout.Add(_mapView, 0, 1);
        pageLayout.Add(_status, 0, 2);
        Content = pageLayout;
#if WINDOWS
        AttachWindowsMapInput();
#endif
    }

    private async void OpenKmzClicked(object? sender, EventArgs e)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose a Google Earth KMZ file",
                FileTypes = KmzFileType
            });
            if (file is null)
                return;

            SetStatus($"Opening {file.FileName}...");
            var loaded = await ReadKmzAsync(file);
            _map.SetLayer(loaded);
            _mapView.Invalidate();
            SetStatus($"{loaded.Name}  |  KMZ GroundOverlay");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                           exception is XmlException || exception is ArgumentException ||
                                           exception is UnauthorizedAccessException || exception is FeatureNotSupportedException)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private async void OpenImageClicked(object? sender, EventArgs e)
    {
        try
        {
            var pickedFiles = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Select one image and its world file",
                FileTypes = ImageAndWorldFileTypes
            });
            if (pickedFiles is null)
                return;
            var files = pickedFiles.ToArray();
            if (files.Length == 0)
                return;

            var images = files.Where(file => IsImageFile(file.FileName)).ToArray();
            if (images.Length != 1)
                throw new InvalidDataException("Select exactly one supported image and its matching world file.");
            var imageFile = images[0];

            var worldFiles = files.Where(file =>
                IsWorldFileFor(imageFile.FileName, file.FileName)).ToArray();
            if (worldFiles.Length != 1 || files.Length != 2)
                throw new InvalidDataException("Select exactly one supported image and its matching world file.");
            var worldFile = worldFiles[0];

            SetStatus($"Opening {imageFile.FileName}...");
            var loaded = await ReadWorldFileImageAsync(imageFile, worldFile);
            _map.SetLayer(loaded);
            _mapView.Invalidate();
            var coordinateSystem = loaded.Reference.UsesUtm32N
                ? "EPSG:25832 coordinates"
                : "WGS 84 longitude/latitude";
            SetStatus($"{loaded.Name}  |  World-file coordinates: {coordinateSystem}.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                           exception is ArgumentException || exception is UnauthorizedAccessException ||
                                           exception is FeatureNotSupportedException)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
#if WINDOWS
        if (_windowsPointerPanning)
            return;
#endif

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartX = _map.PanX;
                _panStartY = _map.PanY;
                break;
            case GestureStatus.Running:
                _map.PanX = _panStartX + e.TotalX;
                _map.PanY = _panStartY + e.TotalY;
                _mapView.Invalidate();
                break;
        }
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
            _pinchStartZoom = _map.Zoom;
        else if (e.Status == GestureStatus.Running)
        {
            _map.Zoom = Math.Clamp(_pinchStartZoom * e.Scale, MapDrawable.MinimumZoom, MapDrawable.MaximumZoom);
            _mapView.Invalidate();
        }
    }

    private void SetStatus(string text, bool isError = false)
    {
        _status.Text = text;
        _status.TextColor = isError ? Colors.DarkRed : Color.FromArgb("#303840");
    }

    private static async Task<MapLayer> ReadKmzAsync(FileResult file)
    {
        await using var input = await file.OpenReadAsync();
        var archiveBytes = await ReadLimitedAsync(input, 64 * 1024 * 1024);
        using var archiveStream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var path = NormalizeArchivePath(entry.FullName);
            if (path is null || entry.FullName.EndsWith("/", StringComparison.Ordinal))
                continue;
            expandedBytes += entry.Length;
            if (expandedBytes > 128 * 1024 * 1024)
                throw new InvalidDataException("The uncompressed KMZ contents are too large to open.");
            entries[path] = entry;
        }

        var kml = entries.TryGetValue("doc.kml", out var preferredKml)
            ? preferredKml
            : entries.Values.FirstOrDefault(entry => entry.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));
        if (kml is null)
            throw new InvalidDataException("This KMZ archive does not contain a KML file.");

        await using var kmlStream = kml.Open();
        var kmlBytes = await ReadLimitedAsync(kmlStream, 4 * 1024 * 1024);
        using var xmlStream = new MemoryStream(kmlBytes, writable: false);
        using var reader = XmlReader.Create(xmlStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var document = XDocument.Load(reader);
        var overlay = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "GroundOverlay");
        var box = overlay?.Descendants().FirstOrDefault(element => element.Name.LocalName == "LatLonBox");
        var quad = overlay?.Descendants().FirstOrDefault(element => element.Name.LocalName == "LatLonQuad");
        if (overlay is null || box is null || quad is not null)
            throw new InvalidDataException("The KML must contain a GroundOverlay with a LatLonBox.");

        var rotation = ReadOptionalCoordinate(box, "rotation", 0);
        if (!double.IsFinite(rotation))
            throw new InvalidDataException("The KMZ GroundOverlay has an invalid rotation.");
        if (Math.Abs(rotation) > 1e-8)
            throw new InvalidDataException("Rotated KMZ GroundOverlays are not supported.");

        var north = ReadCoordinate(box, "north");
        var south = ReadCoordinate(box, "south");
        var east = ReadCoordinate(box, "east");
        var west = ReadCoordinate(box, "west");
        if (north <= south || east <= west || north > 90 || south < -90 || east > 180 || west < -180)
            throw new InvalidDataException("The KMZ GroundOverlay has invalid geographic bounds.");

        var href = overlay.Descendants().FirstOrDefault(element => element.Name.LocalName == "href")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(href))
            throw new InvalidDataException("The KMZ GroundOverlay is missing its image reference.");
        if (Uri.TryCreate(href, UriKind.Absolute, out _) || href.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException("The GroundOverlay image must be included in the KMZ archive.");

        var kmlPath = NormalizeArchivePath(kml.FullName)
            ?? throw new InvalidDataException("The KMZ KML path is invalid.");
        var basePath = kmlPath.Contains('/') ? kmlPath[..kmlPath.LastIndexOf('/')] : string.Empty;
        var imagePath = NormalizeArchivePath(Path.Combine(basePath, Uri.UnescapeDataString(href)));
        if (imagePath is null || !entries.TryGetValue(imagePath, out var imageEntry))
            throw new InvalidDataException($"The GroundOverlay image '{href}' was not found in the KMZ.");

        await using var imageStream = imageEntry.Open();
        var imageBytes = await ReadLimitedAsync(imageStream, 128 * 1024 * 1024);
        var image = DecodeImage(imageBytes);
        if (image.Width <= 0 || image.Height <= 0)
            throw new InvalidDataException("The selected image has invalid dimensions.");
        return new MapLayer(
            image,
            GeoReference.ForBounds(west, south, east, north),
            Path.GetFileName(imageEntry.FullName));
    }

    private static async Task<MapLayer> ReadWorldFileImageAsync(FileResult imageFile, FileResult worldFile)
    {
        await using var worldStream = await worldFile.OpenReadAsync();
        var worldBytes = await ReadLimitedAsync(worldStream, 16 * 1024);
        var values = Encoding.UTF8.GetString(worldBytes).TrimStart('\uFEFF')
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : double.NaN)
            .ToArray();
        if (values.Length != 6 || values.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("The world file must contain six finite numbers.");

        var transform = GeoReference.ForWorldFile(
            values[0], values[1], values[2], values[3], values[4], values[5]);
        await using var imageStream = await imageFile.OpenReadAsync();
        var imageBytes = await ReadLimitedAsync(imageStream, 128 * 1024 * 1024);
        var image = DecodeImage(imageBytes);
        if (image.Width <= 0 || image.Height <= 0)
            throw new InvalidDataException("The selected image has invalid dimensions.");
        return new MapLayer(image, transform, imageFile.FileName);
    }

    private static GraphicsImage DecodeImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return PlatformImage.FromStream(stream)
            ?? throw new InvalidDataException("The selected image format could not be decoded.");
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream input, int limit)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer);
            if (read == 0)
                break;
            if (output.Length + read > limit)
                throw new InvalidDataException("The selected file is too large to open.");
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        return output.ToArray();
    }

    private static string? NormalizeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal))
            return null;
        var segments = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/'))
        {
            if (segment is "" or ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                    return null;
                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }
        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    private static double ReadCoordinate(XElement element, string name)
    {
        var value = ReadOptionalCoordinate(element, name, double.NaN);
        if (!double.IsFinite(value))
            throw new InvalidDataException($"The KMZ GroundOverlay is missing a valid {name} coordinate.");
        return value;
    }

    private static double ReadOptionalCoordinate(XElement element, string name, double fallback)
    {
        var text = element.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value;
        if (text is null)
            return fallback;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            double.IsFinite(value)
            ? value
            : double.NaN;
    }

    private static bool IsImageFile(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".bmp";

    private static bool IsWorldFileFor(string imageName, string worldName)
    {
        var imageExtension = Path.GetExtension(imageName).ToLowerInvariant();
        var worldExtension = Path.GetExtension(worldName).ToLowerInvariant();
        var imageStem = Path.GetFileNameWithoutExtension(imageName);
        var worldStem = Path.GetFileNameWithoutExtension(worldName);
        var expectedExtension = imageExtension switch
        {
            ".jpg" or ".jpeg" => ".jgw",
            ".png" => ".pgw",
            ".tif" or ".tiff" => ".tfw",
            ".bmp" => ".bpw",
            _ => string.Empty
        };

        return string.Equals(worldStem, imageStem, StringComparison.OrdinalIgnoreCase) &&
            (worldExtension == ".wld" || worldExtension == expectedExtension) ||
            string.Equals(worldName, imageName + "w", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class MapLayer(GraphicsImage image, GeoReference reference, string name)
{
    public GraphicsImage Image { get; } = image;
    public GeoReference Reference { get; } = reference;
    public string Name { get; } = name;
}

internal sealed class GeoReference
{
    private readonly double _a;
    private readonly double _d;
    private readonly double _b;
    private readonly double _e;
    private readonly double _c;
    private readonly double _f;
    private readonly bool _worldFile;
    private readonly bool _utm32N;

    private GeoReference(double a, double d, double b, double e, double c, double f, bool worldFile, bool utm32N)
    {
        _a = a;
        _d = d;
        _b = b;
        _e = e;
        _c = c;
        _f = f;
        _worldFile = worldFile;
        _utm32N = utm32N;
    }

    public bool UsesUtm32N => _utm32N;

    public static GeoReference ForBounds(double west, double south, double east, double north) =>
        new(east - west, 0, 0, south - north, west, north, worldFile: false, utm32N: false);

    public static GeoReference ForWorldFile(double a, double d, double b, double e, double c, double f)
    {
        var determinant = a * e - b * d;
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-20)
            throw new InvalidDataException("The world file has a singular coordinate transform.");
        var utm32N = Math.Abs(c) > 180 || Math.Abs(f) > 90;
        return new GeoReference(a, d, b, e, c, f, worldFile: true, utm32N);
    }

    public bool TryGetPixel(double longitude, double latitude, float imageWidth, float imageHeight, out PointF point)
    {
        if (_utm32N)
            (longitude, latitude) = ToUtm32N(longitude, latitude);

        var x = longitude - _c;
        var y = latitude - _f;
        if (_worldFile)
        {
            var determinant = _a * _e - _b * _d;
            var column = (x * _e - _b * y) / determinant + 0.5;
            var row = (_a * y - x * _d) / determinant + 0.5;
            return SetPointIfInside(column, row, imageWidth, imageHeight, out point);
        }

        var boundsColumn = x / _a * imageWidth;
        var boundsRow = y / _e * imageHeight;
        return SetPointIfInside(boundsColumn, boundsRow, imageWidth, imageHeight, out point);
    }

    private static (double Easting, double Northing) ToUtm32N(double longitude, double latitude)
    {
        const double semiMajorAxis = 6_378_137;
        const double eccentricitySquared = 0.00669438002290;
        const double scale = 0.9996;
        const double centralMeridian = 9 * Math.PI / 180;

        var latitudeRadians = latitude * Math.PI / 180;
        var longitudeRadians = longitude * Math.PI / 180;
        var sinLatitude = Math.Sin(latitudeRadians);
        var cosLatitude = Math.Cos(latitudeRadians);
        var tanLatitude = Math.Tan(latitudeRadians);
        var secondEccentricitySquared = eccentricitySquared / (1 - eccentricitySquared);
        var n = semiMajorAxis / Math.Sqrt(1 - eccentricitySquared * sinLatitude * sinLatitude);
        var t = tanLatitude * tanLatitude;
        var c = secondEccentricitySquared * cosLatitude * cosLatitude;
        var a = cosLatitude * (longitudeRadians - centralMeridian);
        var e4 = eccentricitySquared * eccentricitySquared;
        var e6 = e4 * eccentricitySquared;
        var meridionalArc = semiMajorAxis *
            ((1 - eccentricitySquared / 4 - 3 * e4 / 64 - 5 * e6 / 256) * latitudeRadians
             - (3 * eccentricitySquared / 8 + 3 * e4 / 32 + 45 * e6 / 1024) * Math.Sin(2 * latitudeRadians)
             + (15 * e4 / 256 + 45 * e6 / 1024) * Math.Sin(4 * latitudeRadians)
             - 35 * e6 / 3072 * Math.Sin(6 * latitudeRadians));

        var easting = 500_000 + scale * n *
            (a + (1 - t + c) * Math.Pow(a, 3) / 6
             + (5 - 18 * t + t * t + 72 * c - 58 * secondEccentricitySquared) * Math.Pow(a, 5) / 120);
        var northing = scale *
            (meridionalArc + n * tanLatitude *
                (a * a / 2
                 + (5 - t + 9 * c + 4 * c * c) * Math.Pow(a, 4) / 24
                 + (61 - 58 * t + t * t + 600 * c - 330 * secondEccentricitySquared) * Math.Pow(a, 6) / 720));
        return (easting, northing);
    }

    private static bool SetPointIfInside(
        double column,
        double row,
        float imageWidth,
        float imageHeight,
        out PointF point)
    {
        if (!double.IsFinite(column) || !double.IsFinite(row) ||
            column < 0 || column > imageWidth || row < 0 || row > imageHeight)
        {
            point = default;
            return false;
        }

        point = new PointF((float)column, (float)row);
        return true;
    }
}

internal sealed class MapDrawable : IDrawable
{
    private MapLayer? _layer;
    private RectF _viewport;

    public const double MinimumZoom = 0.05;
    public const double MaximumZoom = 100;

    public Location? Location { get; set; } = new(59.904473, 10.668472);
    public double Zoom { get; set; } = 1;
    public double PanX { get; set; }
    public double PanY { get; set; }

    public void SetLayer(MapLayer layer)
    {
        if (_layer?.Image is IDisposable disposable)
            disposable.Dispose();
        _layer = layer;
        Zoom = 1;
        PanX = 0;
        PanY = 0;
    }

    public void ZoomAtPoint(double zoom, double x, double y)
    {
        var newZoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        var layer = _layer;
        if (layer is null || _viewport.Width <= 0 || _viewport.Height <= 0)
        {
            Zoom = newZoom;
            return;
        }

        var currentBounds = GetImageBounds(layer, _viewport, Zoom, PanX, PanY);
        var anchorX = Math.Clamp(x, currentBounds.X, currentBounds.X + currentBounds.Width);
        var anchorY = Math.Clamp(y, currentBounds.Y, currentBounds.Y + currentBounds.Height);
        var imageX = (anchorX - currentBounds.X) / currentBounds.Width;
        var imageY = (anchorY - currentBounds.Y) / currentBounds.Height;
        var fitScale = Math.Min(_viewport.Width / layer.Image.Width, _viewport.Height / layer.Image.Height);
        var nextScale = (float)(fitScale * newZoom);
        var nextWidth = layer.Image.Width * nextScale;
        var nextHeight = layer.Image.Height * nextScale;
        var centeredLeft = _viewport.X + (_viewport.Width - nextWidth) / 2f;
        var centeredTop = _viewport.Y + (_viewport.Height - nextHeight) / 2f;
        PanX = anchorX - imageX * nextWidth - centeredLeft;
        PanY = anchorY - imageY * nextHeight - centeredTop;
        Zoom = newZoom;
    }

    public bool TryMapInputPosition(
        double x,
        double y,
        out double viewportX,
        out double viewportY)
    {
        if (_viewport.Width <= 0 || _viewport.Height <= 0 ||
            !double.IsFinite(x) || !double.IsFinite(y))
        {
            viewportX = 0;
            viewportY = 0;
            return false;
        }

        viewportX = _viewport.X + x;
        viewportY = _viewport.Y + y;
        return true;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        _viewport = dirtyRect;
        var layer = _layer;
        if (layer is null)
        {
            canvas.FontColor = Color.FromArgb("#49545E");
            canvas.FontSize = 16;
            canvas.DrawString(
                "Open a KMZ or a georeferenced image",
                16, 16, dirtyRect.Width - 32, dirtyRect.Height - 32,
                HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        var imageBounds = GetImageBounds(layer, dirtyRect, Zoom, PanX, PanY);
        var scale = imageBounds.Width / layer.Image.Width;
        canvas.DrawImage(layer.Image, imageBounds.X, imageBounds.Y, imageBounds.Width, imageBounds.Height);

        var location = Location;
        if (location is null ||
            !layer.Reference.TryGetPixel(location.Longitude, location.Latitude, layer.Image.Width, layer.Image.Height, out var pixel))
            return;

        var centerX = imageBounds.X + pixel.X * scale;
        var centerY = imageBounds.Y + pixel.Y * scale;
        var outerDiameter = MillimetersToDip(2f);
        var strokeWidth = MillimetersToDip(0.4f);
        var inset = strokeWidth / 2f;
        canvas.StrokeColor = Color.FromRgba(30, 136, 229, 180);
        canvas.StrokeSize = strokeWidth;
        canvas.DrawEllipse(
            centerX - outerDiameter / 2 + inset,
            centerY - outerDiameter / 2 + inset,
            outerDiameter - strokeWidth,
            outerDiameter - strokeWidth);
    }

    private static RectF GetImageBounds(MapLayer layer, RectF viewport, double zoom, double panX, double panY)
    {
        var fitScale = Math.Min(viewport.Width / layer.Image.Width, viewport.Height / layer.Image.Height);
        var scale = (float)(fitScale * zoom);
        var width = layer.Image.Width * scale;
        var height = layer.Image.Height * scale;
        return new RectF(
            viewport.X + (viewport.Width - width) / 2f + (float)panX,
            viewport.Y + (viewport.Height - height) / 2f + (float)panY,
            width,
            height);
    }

    private static float MillimetersToDip(float millimeters)
    {
#if ANDROID
        var metrics = Android.App.Application.Context.Resources?.DisplayMetrics;
        if (metrics is not null && metrics.Density > 0 && metrics.Xdpi > 0)
            return millimeters * metrics.Xdpi / metrics.Density / 25.4f;
        return millimeters * 160f / 25.4f;
#elif IOS
        return millimeters * 163f / 25.4f;
#elif MACCATALYST
        return millimeters * 72f / 25.4f;
#else
        return millimeters * 96f / 25.4f;
#endif
    }
}
