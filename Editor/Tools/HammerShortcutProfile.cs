using UnityEditor;
using UnityEditor.ShortcutManagement;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Earlier versions swapped to a "Hammer" shortcut profile while the scene view tool was
	/// active. Hammer's keys now live in the Hammer window, so put back whatever profile was in
	/// use before and remove the old one.
	/// </summary>
	[InitializeOnLoad]
	static class HammerShortcutProfile
	{
		const string ProfileId = "Hammer";
		const string PreviousProfileKey = "HammerUnity.PreviousShortcutProfile";

		static HammerShortcutProfile()
		{
			EditorApplication.delayCall += Cleanup;
		}

		static void Cleanup()
		{
			try
			{
				var manager = ShortcutManager.instance;

				if ( manager.activeProfileId == ProfileId )
				{
					var previous = EditorPrefs.GetString( PreviousProfileKey, ShortcutManager.defaultProfileId );
					manager.activeProfileId = manager.IsProfileIdValid( previous ) && previous != ProfileId ? previous : ShortcutManager.defaultProfileId;
				}

				foreach ( var id in manager.GetAvailableProfileIds() )
				{
					if ( id == ProfileId )
					{
						manager.DeleteProfile( ProfileId );
						break;
					}
				}
			}
			catch ( System.Exception )
			{
				// Shortcut manager not ready; try again next reload
			}
		}
	}
}
