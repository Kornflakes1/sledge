using System.Linq;
using UnityEditor;
using UnityEngine;
using Sandbox.Primitives;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	[CustomEditor( typeof( HammerMesh ) )]
	[CanEditMultipleObjects]
	sealed class HammerMeshEditor : UnityEditor.Editor
	{
		SerializedProperty _collision;
		SerializedProperty _smoothing;

		void OnEnable()
		{
			_collision = serializedObject.FindProperty( "_collision" );
			_smoothing = serializedObject.FindProperty( "_smoothingAngle" );
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField( _collision );
			EditorGUILayout.PropertyField( _smoothing, new GUIContent( "Smoothing Angle" ) );
			if ( EditorGUI.EndChangeCheck() )
			{
				serializedObject.ApplyModifiedProperties();
				foreach ( HammerMesh m in targets )
				{
					m.Mesh.SetSmoothingAngle( m.SmoothingAngle );
					m.Commit();
				}
			}

			if ( targets.Length == 1 )
			{
				var mesh = ((HammerMesh)target).Mesh;
				EditorGUILayout.LabelField( "Geometry", $"{mesh.VertexHandles.Count()} vertices, {mesh.FaceHandles.Count()} faces" );
			}

			EditorGUILayout.Space();

			using ( new GUILayout.HorizontalScope() )
			{
				if ( GUILayout.Button( "Edit in Hammer Window" ) ) HammerWindow.Open();
			}

			if ( GUILayout.Button( "Bake To Mesh Asset…" ) )
				BakeToAsset( (HammerMesh)target );
		}

		/// <summary>
		/// Save the current triangulated mesh as a regular .asset, for use outside Hammer meshes.
		/// </summary>
		static void BakeToAsset( HammerMesh component )
		{
			var path = EditorUtility.SaveFilePanelInProject( "Bake Mesh", component.name, "asset", "Save the baked mesh" );
			if ( string.IsNullOrEmpty( path ) ) return;

			component.RebuildUnityMesh();
			var copy = UnityEngine.Object.Instantiate( component.RenderMesh );
			copy.name = component.name;
			AssetDatabase.CreateAsset( copy, path );
			AssetDatabase.SaveAssets();
		}
	}

	static class HammerMenu
	{
		[MenuItem( "GameObject/Hammer/Box", false, 10 )]
		static void CreateBox( MenuCommand command ) => Create<BlockPrimitive>( "Box", command, new S.Vector3( 64, 64, 64 ) );

		[MenuItem( "GameObject/Hammer/Cylinder", false, 11 )]
		static void CreateCylinder( MenuCommand command ) => Create<CylinderPrimitive>( "Cylinder", command, new S.Vector3( 64, 64, 64 ) );

		[MenuItem( "GameObject/Hammer/Sphere", false, 12 )]
		static void CreateSphere( MenuCommand command ) => Create<SpherePrimitive>( "Sphere", command, new S.Vector3( 64, 64, 64 ) );

		[MenuItem( "GameObject/Hammer/Stairs", false, 13 )]
		static void CreateStairs( MenuCommand command ) => Create<StairsPrimitive>( "Stairs", command, new S.Vector3( 128, 64, 96 ) );

		[MenuItem( "GameObject/Hammer/Doorway", false, 14 )]
		static void CreateDoorway( MenuCommand command ) => Create<DoorwayPrimitive>( "Doorway", command, new S.Vector3( 16, 128, 128 ) );

		[MenuItem( "GameObject/Hammer/Quad", false, 15 )]
		static void CreateQuad( MenuCommand command ) => Create<QuadPrimitive>( "Quad", command, new S.Vector3( 128, 128, 0 ) );

		[MenuItem( "Tools/Hammer/Scene View Mesh Tool", false, 1 )]
		static void ActivateTool() => HammerMeshTool.Activate();

		static void Create<T>( string name, MenuCommand command, S.Vector3 size ) where T : PrimitiveBuilder, new()
		{
			var builder = new T
			{
				Material = HammerMaterials.Get( HammerSettings.ActiveMaterial ) ?? S.Material.Load( HammerMaterials.DefaultKey )
			};

			// Build sitting on the ground at the scene view pivot, snapped to the grid
			var pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
			pivot = HammerSettings.SnapWorld( pivot );
			var origin = SourceSpace.ToSourcePosition( pivot );
			var box = new S.BBox( origin - new S.Vector3( size.x, size.y, 0 ) * 0.5f, origin + new S.Vector3( size.x * 0.5f, size.y * 0.5f, size.z ) );

			var mesh = builder.CreateMesh( box );
			var center = mesh.CalculateBounds().Center;
			mesh.ApplyTransform( new S.Transform( -center ) );

			var go = new GameObject( name );
			GameObjectUtility.SetParentAndAlign( go, command?.context as GameObject );
			go.transform.position = SourceSpace.ToUnityPosition( center );
			go.isStatic = true;

			var component = go.AddComponent<HammerMesh>();
			component.SmoothingAngle = 40.0f;
			component.Mesh = mesh;

			Undo.RegisterCreatedObjectUndo( go, $"Create {name}" );
			Selection.activeGameObject = go;
		}
	}
}
