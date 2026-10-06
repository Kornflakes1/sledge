using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using FaceProblem = Sandbox.PolygonMesh.FaceProblem;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Keeps track of broken faces (folded, bent, zero-size...) so the views can draw them in red,
	/// and warns after an edit that leaves some behind.
	/// </summary>
	public static class MeshHealth
	{
		static readonly Dictionary<HammerMesh, Dictionary<FaceHandle, FaceProblem>> _cache = new();

		static MeshHealth()
		{
			HammerMesh.AnyRebuilt += c => _cache.Remove( c );
			Undo.undoRedoPerformed += () => { _cache.Clear(); Warning = null; };
		}

		/// <summary>
		/// The last edit's warning, for the status bar (null when fine).
		/// </summary>
		public static string Warning { get; private set; }

		/// <summary>
		/// Problem faces of a mesh (cached until it's rebuilt).
		/// </summary>
		public static Dictionary<FaceHandle, FaceProblem> BadFaces( HammerMesh component )
		{
			if ( component == null ) return new Dictionary<FaceHandle, FaceProblem>();

			if ( !_cache.TryGetValue( component, out var bad ) )
			{
				bad = component.Mesh.FindBadFaces();
				_cache[component] = bad;
			}

			return bad;
		}

		public static void Forget( HammerMesh component ) => _cache.Remove( component );

		/// <summary>
		/// After an edit: warn if the edited meshes now have broken faces.
		/// </summary>
		public static void AfterEdit( IEnumerable<HammerMesh> components )
		{
			var parts = new List<string>();
			foreach ( var c in components.Where( x => x != null ).Distinct() )
			{
				_cache.Remove( c );
				var bad = BadFaces( c );
				if ( bad.Count == 0 ) continue;
				parts.Add( $"{c.name}: {Describe( bad.Values )}" );
			}

			Warning = parts.Count == 0 ? null : $"⚠ {string.Join( "; ", parts )}. Ctrl+Z to undo, or fix it under Mesh Health in Tool Properties.";
		}

		public static void ClearWarning() => Warning = null;

		/// <summary>
		/// Show a message in the status bar's warning slot (cleared by the next edit).
		/// </summary>
		public static void Report( string message ) => Warning = message;

		/// <summary>
		/// "2 folded, 1 bent" style summary.
		/// </summary>
		public static string Describe( IEnumerable<FaceProblem> problems )
		{
			var list = problems.ToList();
			int Count( FaceProblem p ) => list.Count( x => (x & p) != 0 );

			var bits = new List<string>();
			var folded = Count( FaceProblem.SelfIntersecting | FaceProblem.BadTriangulation );
			var degenerate = list.Count( x => (x & FaceProblem.Degenerate) != 0 && (x & (FaceProblem.SelfIntersecting | FaceProblem.BadTriangulation)) == 0 );
			var bent = list.Count( x => x == FaceProblem.NonPlanar );

			if ( folded > 0 ) bits.Add( $"{folded} folded" );
			if ( degenerate > 0 ) bits.Add( $"{degenerate} zero-size" );
			if ( bent > 0 ) bits.Add( $"{bent} bent" );
			return $"{string.Join( ", ", bits )} face{(list.Count == 1 ? "" : "s")}";
		}

		public static readonly Color BrokenColor = new( 1.0f, 0.15f, 0.12f, 1.0f );
		public static readonly Color BentColor = new( 1.0f, 0.45f, 0.2f, 0.9f );

		/// <summary>
		/// Red outlines on a mesh's problem faces (broken ones solid red, merely bent ones orange-red).
		/// </summary>
		public static void Draw( HammerMesh component, System.Func<Vector3, Vector3> lift )
		{
			var bad = BadFaces( component );
			if ( bad.Count == 0 ) return;

			var mesh = component.Mesh;
			var broken = new List<Vector3>();
			var bent = new List<Vector3>();

			foreach ( var (face, problem) in bad )
			{
				if ( !face.IsValid ) continue;
				var list = problem == FaceProblem.NonPlanar ? bent : broken;
				var vertices = mesh.GetFaceVertices( face );
				for ( int i = 0; i < vertices.Length; i++ )
				{
					list.Add( lift( component.SourceToWorld( mesh.GetVertexPosition( vertices[i] ) ) ) );
					list.Add( lift( component.SourceToWorld( mesh.GetVertexPosition( vertices[(i + 1) % vertices.Length] ) ) ) );
				}
			}

			var zTest = Handles.zTest;
			Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
			Handles.color = BentColor;
			if ( bent.Count > 0 ) Handles.DrawLines( bent.ToArray() );
			Handles.color = BrokenColor;
			for ( int i = 0; i + 1 < broken.Count; i += 2 )
				Handles.DrawAAPolyLine( 3, broken[i], broken[i + 1] );
			Handles.zTest = zTest;
		}
	}
}
