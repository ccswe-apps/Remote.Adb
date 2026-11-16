using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;
using Remote.Adb.Core.Adb.Wireless;

namespace Remote.Adb.Core.UnitTests.Adb.Wireless;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class QrPairingPayloadTests
{
    public class When_Create_Is_Called : QrPairingPayloadTests
    {
        [Test]
        public void It_uses_only_letters_and_digits()
        {
            var payload = QrPairingPayload.Create();

            Assert.Multiple(() =>
            {
                Assert.That(payload.ServiceName, Does.Match("^[A-Za-z0-9]+$"));
                Assert.That(payload.Password, Does.Match("^[A-Za-z0-9]{12}$"));
            });
        }

        [Test]
        public void It_makes_a_new_name_and_password_each_time()
        {
            var first = QrPairingPayload.Create();
            var second = QrPairingPayload.Create();

            Assert.Multiple(() =>
            {
                Assert.That(second.ServiceName, Is.Not.EqualTo(first.ServiceName));
                Assert.That(second.Password, Is.Not.EqualTo(first.Password));
            });
        }
    }

    public class When_Text_Is_Read : QrPairingPayloadTests
    {
        [Test]
        public void It_is_the_adb_wifi_pairing_format()
        {
            var payload = new QrPairingPayload("RemoteAdbAbc123", "Secret789xyz");

            Assert.That(payload.Text, Is.EqualTo("WIFI:T:ADB;S:RemoteAdbAbc123;P:Secret789xyz;;"));
        }
    }
}
