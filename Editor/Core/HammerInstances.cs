using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Keeps Hammer's linked instances the same: whenever one is rebuilt (an edit, a drag step, an
	/// undo), the others take its mesh, each keeping its own position, rotation and scale.
	/// </summary>
	[InitializeOnLoad]
	public static class HammerInstances
	{
		static bool _syncing;

		static HammerInstances()
		{
			HammerMesh.AnyRebuilt += OnRebuilt;
		}

		public static List<HammerMesh> Members( string group ) =>
			string.IsNullOrEmpty( group ) ? new List<HammerMesh>() :
			Object.FindObjectsByType<HammerMesh>( FindObjectsInactive.Include, FindObjectsSortMode.None ).Where( c => c.InstanceGroup == group ).ToList();

		static void OnRebuilt( HammerMesh source )
		{
			if ( _syncing || source == null || string.IsNullOrEmpty( source.InstanceGroup ) ) return;

			_syncing = true;
			try
			{
				var data = source.Mesh.ToData();
				foreach ( var other in Members( source.InstanceGroup ) )
				{
					if ( other == source ) continue;
					other.Mesh = Sandbox.PolygonMesh.FromData( data );
					EditorUtility.SetDirty( other );
				}
			}
			finally
			{
				_syncing = false;
			}
		}

		/// <summary>
		/// Link the selected meshes as instances of the active one (they all take its shape).
		/// </summary>
		public static void MakeInstances( IList<HammerMesh> meshes, HammerMesh master )
		{
			if ( meshes.Count == 0 || master == null ) return;
			var group = !string.IsNullOrEmpty( master.InstanceGroup ) ? master.InstanceGroup : System.Guid.NewGuid().ToString( "N" );

			Undo.RecordObjects( meshes.Cast<Object>().ToArray(), "Make Instance" );
			foreach ( var c in meshes )
			{
				c.InstanceGroup = group;
				EditorUtility.SetDirty( c );
			}
			OnRebuilt( master );
		}

		/// <summary>
		/// Collapse: these meshes become ordinary, independent meshes again.
		/// </summary>
		public static void Collapse( IList<HammerMesh> meshes )
		{
			Undo.RecordObjects( meshes.Cast<Object>().ToArray(), "Collapse Instance" );
			foreach ( var c in meshes )
			{
				c.InstanceGroup = null;
				EditorUtility.SetDirty( c );
			}
		}
	}
}
