using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HammerUnity.EditorTools;
using NUnit.Framework;
using UnityEditor.ShortcutManagement;

namespace HammerUnity.Tests
{
	/// <summary>
	/// The menu bar runs the same handlers as the keys: every shortcut it names has to exist.
	/// </summary>
	public class HammerMenuTests
	{
		[Test]
		public void EveryMenuItemFindsItsCommand()
		{
			var source = File.ReadAllText( Path.GetFullPath( "Packages/com.hammerunity.meshtools/Editor/Window/HammerWindow.Menus.cs" ) );
			var ids = Regex.Matches( source, @"Item\( [^,]+, ""(Hammer/[^""]+)""" ).Select( m => m.Groups[1].Value ).Distinct().ToList();
			Assert.That( ids.Count, Is.GreaterThan( 30 ) );

			var shortcut = typeof( HammerWindow ).GetMethod( "Shortcut", BindingFlags.NonPublic | BindingFlags.Static );
			var missing = ids.Where( id => shortcut.Invoke( null, new object[] { id } ) == null ).ToList();
			Assert.That( missing, Is.Empty, "menu items with no command behind them" );

			var unbound = ids.Where( id => { try { ShortcutManager.instance.GetShortcutBinding( id ); return false; } catch { return true; } } ).ToList();
			Assert.That( unbound, Is.Empty, "menu items whose shortcut the Shortcut Manager doesn't know" );
		}
	}
}
