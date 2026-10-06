using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// F1: every Hammer command and its key, searchable (Hammer's command list). Keys are rebound
	/// under Edit > Shortcuts > Hammer.
	/// </summary>
	public sealed class HammerCommandListWindow : EditorWindow
	{
		string _filter = "";
		Vector2 _scroll;
		List<(string Name, string Keys)> _commands;

		public static void Open()
		{
			var w = GetWindow<HammerCommandListWindow>( true, "Hammer Commands" );
			w.minSize = new Vector2( 420, 360 );
			w._commands = null;
			w.Show();
		}

		void OnGUI()
		{
			_commands ??= ShortcutManager.instance.GetAvailableShortcutIds()
				.Where( id => id.StartsWith( "Hammer/" ) )
				.Select( id => (id.Substring( "Hammer/".Length ), ShortcutManager.instance.GetShortcutBinding( id ).ToString()) )
				.OrderBy( x => x.Item1 )
				.ToList();

			using ( new GUILayout.HorizontalScope( EditorStyles.toolbar ) )
			{
				GUILayout.Label( "Filter", GUILayout.Width( 36 ) );
				_filter = GUILayout.TextField( _filter, EditorStyles.toolbarSearchField );
				if ( GUILayout.Button( "Edit Keys…", EditorStyles.toolbarButton, GUILayout.Width( 70 ) ) )
					EditorApplication.ExecuteMenuItem( "Edit/Shortcuts..." );
			}

			_scroll = EditorGUILayout.BeginScrollView( _scroll );
			foreach ( var (name, keys) in _commands )
			{
				if ( _filter.Length > 0 && name.IndexOf( _filter, System.StringComparison.OrdinalIgnoreCase ) < 0 && keys.IndexOf( _filter, System.StringComparison.OrdinalIgnoreCase ) < 0 )
					continue;

				using ( new GUILayout.HorizontalScope() )
				{
					GUILayout.Label( string.IsNullOrEmpty( keys ) ? "—" : keys, EditorStyles.boldLabel, GUILayout.Width( 130 ) );
					GUILayout.Label( name );
				}
			}
			EditorGUILayout.EndScrollView();
		}
	}

	/// <summary>
	/// Tools > Hammer > Options: Hammer's View 3D options.
	/// </summary>
	public sealed class HammerOptionsWindow : EditorWindow
	{
		[MenuItem( "Tools/Hammer/Options", false, 50 )]
		public static void Open()
		{
			var w = GetWindow<HammerOptionsWindow>( true, "Hammer Options" );
			w.minSize = new Vector2( 360, 120 );
			w.Show();
		}

		void OnGUI()
		{
			EditorGUILayout.LabelField( "View 3D", EditorStyles.boldLabel );
			EditorGUI.BeginChangeCheck();
			HammerSettings.BackplaneDistance = EditorGUILayout.FloatField( new GUIContent( "Backplane Distance", "How far the 3D views draw, in units. Lower it for speed on big maps." ), HammerSettings.BackplaneDistance );
			HammerSettings.ForwardSpeedMax = EditorGUILayout.FloatField( new GUIContent( "Forward Speed Max", "The fastest the camera flies, in units per second." ), HammerSettings.ForwardSpeedMax );
			if ( EditorGUI.EndChangeCheck() ) HammerViews.RepaintAll();
		}
	}
}
