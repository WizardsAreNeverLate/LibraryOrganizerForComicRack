using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public class SmokeTests
{
	[Test]
	public async Task Core_assembly_loads() => await Assert.That(typeof(Placeholder).Assembly.GetName().Name).IsEqualTo("LibraryOrganizer.Core");
}
