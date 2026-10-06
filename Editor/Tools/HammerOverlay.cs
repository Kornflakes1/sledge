using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using PolygonMesh = Sandbox.PolygonMesh;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Scene view panel for the Hammer tool: modes, grid, material and the operations for the
	/// current mode, with their Hammer keys.
	/// </summary>
	[Overlay( typeof( SceneView ), "hammer-mesh-tool", "Hammer", true )]
	public sealed class HammerOverlay : IMGUIOverlay, ITransientOverlay
	{
		public bool visible => HammerMeshTool.Active != null;

		public override void OnGUI()
		{
			var tool = HammerMeshTool.Active;
			if ( tool == null ) return;

			if ( GUILayout.Button( "Exit Hammer Tool  (Esc)", EditorStyles.miniButton ) )
			{
				HammerMeshTool.Deactivate();
				return;
			}

			using ( new GUILayout.VerticalScope( GUILayout.Width( 236 ) ) )
				HammerPanel.Draw( tool, compact: true );
		}
	}
}
