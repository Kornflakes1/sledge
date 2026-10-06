using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// "Hammer" button and shape dropdown in Unity's main toolbar.
	/// </summary>
	static class HammerToolbar
	{
		const string ButtonPath = "Hammer/Mesh Tool";
		const string ShapePath = "Hammer/New Shape";

		[MainToolbarElement( ButtonPath, defaultDockPosition = MainToolbarDockPosition.Middle )]
		static MainToolbarElement CreateButton()
		{
			var icon = EditorGUIUtility.IconContent( "EditCollider" ).image as Texture2D;
			var content = new MainToolbarContent( "Hammer", icon, "Open the Hammer window (Ctrl+Shift+H)" );
			return new MainToolbarButton( content, HammerWindow.Open );
		}

		[MainToolbarElement( ShapePath, defaultDockPosition = MainToolbarDockPosition.Middle )]
		static MainToolbarElement CreateShapeDropdown()
		{
			var icon = EditorGUIUtility.IconContent( "PreMatCube" ).image as Texture2D;
			var content = new MainToolbarContent( "Shape", icon, "Create a Hammer mesh" );
			return new MainToolbarDropdown( content, ShowShapeMenu );
		}

		static void ToggleTool()
		{
			if ( HammerMeshTool.Active != null )
				HammerMeshTool.Deactivate();
			else
				HammerMeshTool.Activate();
		}

		static void ShowShapeMenu( Rect rect )
		{
			var menu = new GenericMenu();

			foreach ( var name in new[] { "Box", "Cylinder", "Sphere", "Stairs", "Doorway", "Quad" } )
				menu.AddItem( new GUIContent( name ), false, () => EditorApplication.ExecuteMenuItem( $"GameObject/Hammer/{name}" ) );

			menu.AddSeparator( "" );
			menu.AddItem( new GUIContent( "Draw Shape (Shift+B)" ), false, () =>
			{
				HammerWindow.Open();
				if ( HammerWindow.FocusedTool != null ) HammerWindow.FocusedTool.Mode = EditMode.Primitive;
			} );
			menu.DropDown( rect );
		}
	}
}
