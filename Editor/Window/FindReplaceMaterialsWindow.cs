using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Hammer's Find / Replace Materials: swap one material for another on the selected faces,
	/// the selected objects, or everything in the scene.
	/// </summary>
	public sealed class FindReplaceMaterialsWindow : EditorWindow
	{
		enum Scope { SelectedFaces, SelectedObjects, WholeScene }

		HammerMeshTool _tool;
		Material _find;
		Material _replace;
		Scope _scope = Scope.SelectedObjects;
		string _result;

		public static void Open( HammerMeshTool tool )
		{
			var w = GetWindow<FindReplaceMaterialsWindow>( true, "Find / Replace Materials" );
			w._tool = tool;
			w.minSize = new Vector2( 320, 170 );

			// Start from the material on the first selected face
			var face = tool.SelectedFaces.FirstOrDefault();
			if ( face.IsValid )
			{
				var key = face.Component.Mesh.GetFaceMaterial( face.Handle )?.Name;
				w._find = key == null || key == HammerMaterials.DefaultKey ? null : HammerMaterials.Resolve( key );
				w._scope = Scope.SelectedFaces;
			}

			w._replace = HammerSettings.ActiveMaterial;
			w.Show();
		}

		void OnGUI()
		{
			EditorGUILayout.Space();
			_find = (Material)EditorGUILayout.ObjectField( new GUIContent( "Find", "Empty means the default dev grid" ), _find, typeof( Material ), false );
			_replace = (Material)EditorGUILayout.ObjectField( new GUIContent( "Replace with", "Empty means the default dev grid" ), _replace, typeof( Material ), false );
			_scope = (Scope)EditorGUILayout.EnumPopup( "In", _scope );

			EditorGUILayout.Space();
			using ( new EditorGUI.DisabledScope( _tool == null || _find == _replace ) )
			{
				if ( GUILayout.Button( "Replace", GUILayout.Height( 26 ) ) )
				{
					var count = HammerMeshTool.ReplaceMaterial( Targets(), _find, _replace );
					_result = $"Replaced on {count} face{(count == 1 ? "" : "s")}.";
				}
			}

			if ( _result != null )
				EditorGUILayout.HelpBox( _result, MessageType.None );
		}

		IEnumerable<(HammerMesh, IEnumerable<FaceHandle>)> Targets()
		{
			switch ( _scope )
			{
				case Scope.SelectedFaces:
					return _tool.SelectedFaces.GroupBy( f => f.Component ).Select( g => (g.Key, g.Select( f => f.Handle )) ).ToList();

				case Scope.SelectedObjects:
					return UnityEditor.Selection.gameObjects.SelectMany( g => g.GetComponentsInChildren<HammerMesh>() ).Distinct()
						.Select( c => (c, (IEnumerable<FaceHandle>)c.Mesh.FaceHandles.ToList()) ).ToList();

				default:
					return Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None )
						.Select( c => (c, (IEnumerable<FaceHandle>)c.Mesh.FaceHandles.ToList()) ).ToList();
			}
		}
	}
}
