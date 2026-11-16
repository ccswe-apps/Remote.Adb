using Moq;
using Remote.Adb.Core.Adb.Wireless;

namespace Remote.Adb.Testing;

/// <summary>
/// Wraps a <see cref="Mock{T}"/> of <see cref="IWirelessAdbService"/> with the setups the wireless view-model tests keep
/// repeating. Starts with discovery available and nothing discovered; every other call returns Moq's default until set up.
/// </summary>
internal sealed class WirelessMock
{
    public WirelessMock()
    {
        DiscoveryAvailable(true);
        Discovers();
    }

    /// <summary>The underlying mock, for setups (argument-specific ones) and verifications the helpers don't cover.</summary>
    public Mock<IWirelessAdbService> Inner { get; } = new();

    /// <summary>The mocked service to hand to the class under test.</summary>
    public IWirelessAdbService Object => Inner.Object;

    /// <summary>Makes every connect attempt end with <paramref name="outcome"/> and <paramref name="message"/>.</summary>
    public void ConnectReturns(WirelessConnectOutcome outcome, string message) =>
        Inner
            .Setup(wireless => wireless.ConnectAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WirelessConnectResult(outcome, message));

    /// <summary>Makes every connect attempt throw <paramref name="exception"/>.</summary>
    public void ConnectThrows(Exception exception) =>
        Inner
            .Setup(wireless => wireless.ConnectAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

    /// <summary>Makes discovery report <paramref name="services"/>.</summary>
    public void Discovers(params MdnsService[] services) =>
        Inner
            .Setup(wireless => wireless.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(services);

    /// <summary>Makes the discovery check report <paramref name="available"/>.</summary>
    public void DiscoveryAvailable(bool available) =>
        Inner
            .Setup(wireless => wireless.IsDiscoveryAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(available);

    /// <summary>Makes every pairing attempt return <paramref name="result"/>.</summary>
    public void PairReturns(WirelessPairResult result) =>
        Inner
            .Setup(wireless => wireless.PairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
}
