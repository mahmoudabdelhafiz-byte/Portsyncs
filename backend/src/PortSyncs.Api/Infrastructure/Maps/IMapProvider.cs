using System.Globalization;

namespace PortSyncs.Api.Infrastructure.Maps;

public enum NavigationApp
{
    GoogleMaps,
    Waze,
}

/// <summary>
/// Maps provider layer (T67). The MVP hands navigation off to the driver's own app,
/// which costs nothing; portals use OpenStreetMap-based tiles.
/// </summary>
public interface IMapProvider
{
    Uri BuildNavigationLink(double lat, double lon, NavigationApp app);
}

public sealed class DeepLinkMapProvider : IMapProvider
{
    public Uri BuildNavigationLink(double lat, double lon, NavigationApp app)
    {
        if (lat is < -90 or > 90) throw new ArgumentOutOfRangeException(nameof(lat));
        if (lon is < -180 or > 180) throw new ArgumentOutOfRangeException(nameof(lon));

        var ll = string.Create(CultureInfo.InvariantCulture, $"{lat:0.######},{lon:0.######}");
        return app switch
        {
            NavigationApp.Waze => new Uri($"https://waze.com/ul?ll={ll}&navigate=yes"),
            _ => new Uri($"https://www.google.com/maps/dir/?api=1&destination={ll}"),
        };
    }
}
