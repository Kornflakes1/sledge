using System.Collections.Generic;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Every mesh's edges as world space line pairs, kept until the mesh is rebuilt or moved, so
	/// the views don't rebuild tens of thousands of lines on every repaint.
	/// </summary>
	public static class MeshEdgeCache
	{
		sealed class Entry
		{
			public Matrix4x4 Matrix;
			public Vector3[] Lines;
		}

		static readonly Dictionary<HammerMesh, Entry> _cache = new();

		static MeshEdgeCache()
		{
			HammerMesh.AnyRebuilt += c => _cache.Remove( c );
			UnityEditor.Undo.undoRedoPerformed += () => { _cache.Clear(); _meshes = null; };
			UnityEditor.EditorApplication.hierarchyChanged += () => _meshes = null;
		}

		/// <summary>
		/// The mesh's edges in world space, two points per edge.
		/// </summary>
		public static Vector3[] WorldEdges( HammerMesh c )
		{
			var matrix = c.transform.localToWorldMatrix;
			if ( _cache.TryGetValue( c, out var entry ) && entry.Matrix == matrix )
				return entry.Lines;

			var mesh = c.Mesh;
			var lines = new List<Vector3>();
			foreach ( var he in mesh.HalfEdgeHandles )
			{
				var opposite = mesh.GetOppositeHalfEdge( he );
				if ( opposite.IsValid && he.Index > opposite.Index ) continue;
				var line = mesh.GetEdgeLine( he );
				lines.Add( c.SourceToWorld( line.Start ) );
				lines.Add( c.SourceToWorld( line.End ) );
			}

			entry = new Entry { Matrix = matrix, Lines = lines.ToArray() };
			_cache[c] = entry;
			return entry.Lines;
		}

		/// <summary>
		/// Enabled Hammer meshes, looked up again only when the scene's objects change.
		/// </summary>
		public static List<HammerMesh> Meshes()
		{
			if ( _meshes == null || _meshes.Exists( x => x == null || !x.isActiveAndEnabled ) )
			{
				_meshes = new List<HammerMesh>();
				foreach ( var c in Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ) )
					if ( c.isActiveAndEnabled ) _meshes.Add( c );
			}

			return _meshes;
		}

		static List<HammerMesh> _meshes;
	}
}
