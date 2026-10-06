using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Scene tabs along the top of the Hammer window, like Hammer's map tabs: the scenes you've
	/// had open recently, click one to switch (asks to save first), + to open another.
	/// </summary>
	partial class HammerWindow
	{
		const float SceneTabHeight = 26;
		const string RecentScenesKey = "HammerUnity.RecentScenes";
		const int MaxRecentScenes = 10;

		static GUIStyle _tabStyle;

		static GUIStyle TabStyle => _tabStyle ??= new GUIStyle( EditorStyles.label )
		{
			alignment = TextAnchor.MiddleLeft,
			fontSize = 11,
			padding = new RectOffset( 10, 22, 0, 0 ),
			clipping = TextClipping.Clip,
		};

		/// <summary>
		/// Scene paths for the tabs, most recent first, always including the open ones.
		/// </summary>
		static List<string> RecentScenes
		{
			get
			{
				var list = EditorPrefs.GetString( RecentScenesKey, "" ).Split( '|' ).Where( p => !string.IsNullOrEmpty( p ) && File.Exists( p ) ).ToList();

				for ( int i = 0; i < SceneManager.sceneCount; i++ )
				{
					var path = SceneManager.GetSceneAt( i ).path;
					if ( !string.IsNullOrEmpty( path ) && !list.Contains( path ) ) list.Insert( 0, path );
				}

				return list.Take( MaxRecentScenes ).ToList();
			}
			set
			{
				// Tests and automation open throwaway scenes: keep them out of the user's list
				if ( HammerSettings.Isolated ) return;
				EditorPrefs.SetString( RecentScenesKey, string.Join( "|", value.Take( MaxRecentScenes ) ) );
			}
		}

		[InitializeOnLoadMethod]
		static void TrackScenes()
		{
			EditorSceneManager.sceneOpened += ( scene, mode ) => Remember( scene.path );
			EditorSceneManager.sceneSaved += scene => Remember( scene.path );
		}

		static void Remember( string path )
		{
			if ( string.IsNullOrEmpty( path ) ) return;
			var list = RecentScenes;
			list.Remove( path );
			list.Insert( 0, path );
			RecentScenes = list;
			foreach ( var w in Resources.FindObjectsOfTypeAll<HammerWindow>() ) w.Repaint();
		}

		void DrawSceneTabs( Rect rect )
		{
			var e = Event.current;
			if ( e.type == EventType.Repaint )
			{
				EditorGUI.DrawRect( rect, HammerIcons.Background );
				EditorGUI.DrawRect( new Rect( rect.x, rect.yMax - 1, rect.width, 1 ), HammerIcons.Divider );
			}

			var active = SceneManager.GetActiveScene();
			var x = rect.x + 4;

			// An unsaved new scene gets a tab too
			var tabs = RecentScenes.Select( p => (path: p, name: Path.GetFileNameWithoutExtension( p )) ).ToList();
			if ( string.IsNullOrEmpty( active.path ) )
				tabs.Insert( 0, (path: "", name: "Untitled") );

			foreach ( var (path, name) in tabs )
			{
				var isActive = path == active.path;
				var label = isActive && active.isDirty ? name + " *" : name;
				var width = Mathf.Clamp( TabStyle.CalcSize( new GUIContent( label ) ).x, 80, 200 );
				if ( x + width > rect.xMax - 34 ) break;

				var tab = new Rect( x, rect.y + 3, width, rect.height - 3 );
				var close = new Rect( tab.xMax - 18, tab.y + (tab.height - 14) * 0.5f, 14, 14 );
				var hover = tab.Contains( e.mousePosition );

				if ( e.type == EventType.Repaint )
				{
					EditorGUI.DrawRect( tab, isActive ? HammerIcons.Panel : hover ? HammerIcons.ButtonHover : HammerIcons.Bar );
					if ( isActive ) EditorGUI.DrawRect( new Rect( tab.x, tab.y, tab.width, 2 ), HammerIcons.Accent );
					TabStyle.normal.textColor = isActive ? Color.white : new Color( 0.7f, 0.7f, 0.7f );
					TabStyle.Draw( tab, label, false, false, false, false );
					if ( !isActive && !string.IsNullOrEmpty( path ) && hover )
						GUI.Label( close, "×", EditorStyles.centeredGreyMiniLabel );
				}

				GUI.Label( tab, new GUIContent( "", string.IsNullOrEmpty( path ) ? "Unsaved scene" : path ), GUIStyle.none );

				if ( e.type == EventType.MouseDown && e.button == 0 && tab.Contains( e.mousePosition ) )
				{
					if ( !isActive && !string.IsNullOrEmpty( path ) && close.Contains( e.mousePosition ) )
					{
						// Forget the tab (doesn't touch the scene file)
						RecentScenes = RecentScenes.Where( p => p != path ).ToList();
					}
					else if ( !isActive && !string.IsNullOrEmpty( path ) )
					{
						OpenSceneTab( path );
					}

					e.Use();
					Repaint();
				}

				x += width + 2;
			}

			// + opens any scene in the project
			var plus = new Rect( x + 2, rect.y + 4, 26, rect.height - 6 );
			if ( GUI.Button( plus, new GUIContent( "+", "Open a scene" ), EditorStyles.toolbarButton ) )
			{
				var menu = new GenericMenu();
				foreach ( var guid in AssetDatabase.FindAssets( "t:Scene", new[] { "Assets" } ) )
				{
					var path = AssetDatabase.GUIDToAssetPath( guid );
					var p = path;
					menu.AddItem( new GUIContent( path.Substring( "Assets/".Length ).Replace( '/', '\u2215' ) ), path == active.path, () => OpenSceneTab( p ) );
				}

				menu.AddSeparator( "" );
				menu.AddItem( new GUIContent( "New Scene" ), false, () =>
				{
					if ( EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() )
						EditorSceneManager.NewScene( NewSceneSetup.DefaultGameObjects, NewSceneMode.Single );
				} );
				menu.DropDown( plus );
			}
		}

		static void OpenSceneTab( string path )
		{
			if ( !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() )
				return;

			EditorSceneManager.OpenScene( path, OpenSceneMode.Single );
		}
	}
}
