using HammerUnity.EditorTools;
using NUnit.Framework;

namespace HammerUnity.Tests
{
	/// <summary>
	/// Every test runs with in-memory settings, so changing the grid, bevel segments and so on
	/// never touches the user's own (EditorPrefs are shared by every Unity project on the machine,
	/// and a test that's stopped half way can't put them back).
	/// </summary>
	[SetUpFixture]
	public class IsolatedSettings
	{
		[OneTimeSetUp]
		public void Isolate() => HammerSettings.Isolated = true;

		[OneTimeTearDown]
		public void Restore() => HammerSettings.Isolated = false;
	}
}
