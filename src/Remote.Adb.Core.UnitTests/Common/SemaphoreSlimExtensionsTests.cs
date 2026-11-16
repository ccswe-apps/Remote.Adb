using System.Diagnostics.CodeAnalysis;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.UnitTests.Common;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class SemaphoreSlimExtensionsTests
{
    public class When_LockAsync_Is_Called : SemaphoreSlimExtensionsTests
    {
        [Test]
        public async Task It_holds_the_semaphore_until_the_lease_is_disposed()
        {
            using var semaphore = new SemaphoreSlim(1, 1);

            var lease = await semaphore.LockAsync();
            var heldCount = semaphore.CurrentCount;
            lease.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(heldCount, Is.Zero);
                Assert.That(semaphore.CurrentCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task It_holds_nothing_when_the_wait_is_cancelled()
        {
            using var semaphore = new SemaphoreSlim(0, 1);
            using var cancel = new CancellationTokenSource();
            await cancel.CancelAsync();

            await Assert.ThatAsync(() => semaphore.LockAsync(cancel.Token), Throws.InstanceOf<OperationCanceledException>());

            Assert.That(semaphore.CurrentCount, Is.Zero);
        }
    }
}
