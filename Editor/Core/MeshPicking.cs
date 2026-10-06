using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Scene view picking of mesh elements under the cursor.
	/// </summary>
	public static class MeshPicking
	{
		public const float VertexPickRadius = 10.0f;
		public const float EdgePickRadius = 8.0f;

		public struct FaceHit
		{
			public MeshFace Face;
			public float Distance;
			public Vector3 Point;
			public Vector3 Normal;
		}

		/// <summary>
		/// All Hammer meshes that could be on screen in the current scene view.
		/// </summary>
		// The visible list is asked for over and over while drawing one frame (once per selected
		// face, to see if it's hidden): keep it for the rest of the editor tick while the camera
		// and the set of meshes stay the same
		static List<HammerMesh> _visible;
		static int _visibleTick = -1, _visibleCamera, _visibleMeshes;
		static Matrix4x4 _visibleMatrix;
		static int _tick;

		[InitializeOnLoadMethod]
		static void HookTick() => EditorApplication.update += () => _tick++;

		public static List<HammerMesh> VisibleMeshes()
		{
			var camera = HammerGUI.Camera;
			var cameraId = camera != null ? camera.GetInstanceID() : 0;
			var matrix = camera != null ? camera.projectionMatrix * camera.worldToCameraMatrix : Matrix4x4.identity;
			if ( _visible != null && _visibleTick == _tick && _visibleCamera == cameraId && _visibleMeshes == HammerMesh.EnabledVersion && _visibleMatrix == matrix )
				return new List<HammerMesh>( _visible );

			var result = new List<HammerMesh>();
			var planes = camera != null ? GeometryUtility.CalculateFrustumPlanes( camera ) : null;

			foreach ( var mesh in HammerMesh.Enabled )
			{
				if ( mesh == null ) continue;
				if ( !mesh.isActiveAndEnabled )
					continue;

				if ( SceneVisibilityManager.instance.IsHidden( mesh.gameObject ) )
					continue;

				if ( SceneVisibilityManager.instance.IsPickingDisabled( mesh.gameObject ) )
					continue;

				var renderer = mesh.GetComponent<MeshRenderer>();
				if ( planes != null && renderer != null && !GeometryUtility.TestPlanesAABB( planes, renderer.bounds ) )
					continue;

				result.Add( mesh );
			}

			(_visible, _visibleTick, _visibleCamera, _visibleMeshes, _visibleMatrix) = (result, _tick, cameraId, HammerMesh.EnabledVersion, matrix);
			return new List<HammerMesh>( result );
		}

		/// <summary>
		/// Closest front-facing face hit by a world space ray.
		/// </summary>
		public static bool RaycastFace( Ray ray, IEnumerable<HammerMesh> meshes, out FaceHit hit )
		{
			hit = default;
			var best = float.MaxValue;

			foreach ( var component in meshes )
			{
				if ( component == null ) continue;

				if ( RaycastFace( ray, component, out var h ) && h.Distance < best )
				{
					best = h.Distance;
					hit = h;
				}
			}

			return best < float.MaxValue;
		}

		public static bool RaycastFace( Ray ray, HammerMesh component, out FaceHit hit )
		{
			hit = default;

			// The cheap test first: on a big map most meshes are nowhere near the ray, and
			// reading their polygon data would unpack every one of them
			var renderer = component.GetComponent<MeshRenderer>();
			if ( renderer != null && !renderer.bounds.IntersectRay( ray ) )
				return false;

			var world = World( component );
			var points = world.Triangles;
			var normals = world.Normals;
			var faces = component.Mesh.TriangleFaces;

			if ( points.Length < 3 )
				return false;

			var best = float.MaxValue;
			int bestTri = -1;
			Vector3 bestNormal = default;

			for ( int tri = 0; tri < normals.Length; tri++ )
			{
				var n = normals[tri];
				if ( Vector3.Dot( n, ray.direction ) >= 0 )
					continue; // back face

				if ( IntersectTriangle( ray, points[tri * 3], points[tri * 3 + 1], points[tri * 3 + 2], out var t ) && t < best )
				{
					best = t;
					bestTri = tri;
					bestNormal = n.normalized;
				}
			}

			if ( bestTri < 0 || bestTri >= faces.Count )
				return false;

			hit = new FaceHit
			{
				Face = new MeshFace( component, faces[bestTri] ),
				Distance = best,
				Point = ray.GetPoint( best ),
				Normal = bestNormal,
			};

			return true;
		}

		// Möller–Trumbore, double sided
		static bool IntersectTriangle( Ray ray, Vector3 a, Vector3 b, Vector3 c, out float t )
		{
			t = 0;
			var e1 = b - a;
			var e2 = c - a;
			var p = Vector3.Cross( ray.direction, e2 );
			var det = Vector3.Dot( e1, p );
			if ( Mathf.Abs( det ) < 1e-10f ) return false;

			var inv = 1.0f / det;
			var s = ray.origin - a;
			var u = Vector3.Dot( s, p ) * inv;
			if ( u < 0 || u > 1 ) return false;

			var q = Vector3.Cross( s, e1 );
			var v = Vector3.Dot( ray.direction, q ) * inv;
			if ( v < 0 || u + v > 1 ) return false;

			t = Vector3.Dot( e2, q ) * inv;
			return t > 0;
		}

		/// <summary>
		/// Is a world point hidden behind any mesh, seen from the scene camera?
		/// </summary>
		public static bool IsOccluded( Vector3 world, IEnumerable<HammerMesh> meshes )
		{
			var camera = HammerGUI.Camera;
			if ( camera == null ) return false;

			var origin = camera.transform.position;
			var dir = world - origin;
			var distance = dir.magnitude;
			if ( distance < 1e-5f ) return false;

			if ( camera.orthographic )
			{
				dir = camera.transform.forward;
				origin = world - dir * (camera.farClipPlane * 0.5f);
				distance = camera.farClipPlane * 0.5f;
			}

			// Any hit in front of the point will do (no need for the nearest), and meshes whose
			// bounds start beyond it can't be in the way
			var ray = new Ray( origin, dir.normalized );
			var epsilon = Mathf.Max( 0.002f, distance * 0.001f );
			foreach ( var component in meshes )
			{
				if ( component == null ) continue;
				var renderer = component.GetComponent<MeshRenderer>();
				if ( renderer != null && (!renderer.bounds.IntersectRay( ray, out var enter ) || enter > distance) ) continue;
				if ( RaycastFace( ray, component, out var hit ) && hit.Distance < distance - epsilon ) return true;
			}
			return false;
		}

		public static MeshVertex PickVertex( Vector2 mouse, IEnumerable<HammerMesh> meshes, bool through )
		{
			var list = meshes as IList<HammerMesh> ?? meshes.ToList();
			var candidates = new List<(float distance, MeshVertex vertex, Vector3 world)>();

			var projector = HammerGUI.Projector.Current();

			foreach ( var component in list )
			{
				var data = World( component );
				data.FillVertices( component );
				for ( int i = 0; i < data.Vertices.Length; i++ )
				{
					var world = data.VertexPositions[i];
					var gui = projector.Project( world );
					if ( gui.z < 0 ) continue;

					var d = Vector2.Distance( gui, mouse );
					if ( d < VertexPickRadius )
						candidates.Add( (d, new MeshVertex( component, data.Vertices[i] ), world) );
				}
			}

			// Occlusion tests are the expensive part, so only run them nearest first until one passes
			foreach ( var c in candidates.OrderBy( x => x.distance ) )
			{
				if ( through || !IsOccluded( c.world, list ) )
					return c.vertex;
			}

			return default;
		}

		public static MeshEdge PickEdge( Vector2 mouse, IEnumerable<HammerMesh> meshes, bool through )
		{
			var list = meshes as IList<HammerMesh> ?? meshes.ToList();
			var candidates = new List<(float distance, MeshEdge edge, Vector3 world)>();

			var projector = HammerGUI.Projector.Current();

			foreach ( var component in list )
			{
				var data = World( component );
				data.FillEdges( component );
				for ( int i = 0; i < data.Edges.Length; i++ )
				{
					var a = data.EdgePoints[i * 2];
					var b = data.EdgePoints[i * 2 + 1];

					var ga = projector.Project( a );
					var gb = projector.Project( b );
					if ( ga.z < 0 && gb.z < 0 ) continue;

					var d = DistancePointSegment( mouse, ga, gb, out var t );
					if ( d < EdgePickRadius )
						candidates.Add( (d, new MeshEdge( component, data.Edges[i] ), Vector3.Lerp( a, b, t )) );
				}
			}

			foreach ( var c in candidates.OrderBy( x => x.distance ) )
			{
				if ( through || !IsOccluded( c.world, list ) )
					return c.edge;
			}

			return default;
		}

		public static bool PickFace( Vector2 mouse, IEnumerable<HammerMesh> meshes, out FaceHit hit )
		{
			var ray = HammerGUI.GUIToRay( mouse );
			if ( HammerTrace.Sink != null )
				HammerTrace.Log( $"PickFace mouse={mouse} ray={ray} cam={(HammerGUI.Camera ? HammerGUI.Camera.name : "null")} meshes={meshes.Count()}" );
			return RaycastFace( ray, meshes, out hit );
		}

		/// <summary>
		/// A mesh's triangles, edges and vertices in world space, kept until it's rebuilt or moved:
		/// working each point out again on every mouse move made picking on big meshes choppy.
		/// </summary>
		sealed class WorldData
		{
			public object Source;
			public int Triangulation;
			public Matrix4x4 Matrix;
			public Vector3[] Triangles;
			public Vector3[] Normals;
			public HalfEdgeMesh.HalfEdgeHandle[] Edges;
			public Vector3[] EdgePoints;
			public HalfEdgeMesh.VertexHandle[] Vertices;
			public Vector3[] VertexPositions;

			public void FillEdges( HammerMesh component )
			{
				if ( Edges != null ) return;
				var mesh = component.Mesh;
				var edges = new List<HalfEdgeMesh.HalfEdgeHandle>();
				var points = new List<Vector3>();
				foreach ( var e in mesh.HalfEdgeHandles )
				{
					var opposite = mesh.GetOppositeHalfEdge( e );
					if ( opposite.IsValid && e.Index > opposite.Index ) continue;
					if ( mesh.IsEdgeHidden( e ) ) continue;
					var line = mesh.GetEdgeLine( e );
					edges.Add( e );
					points.Add( Matrix.MultiplyPoint3x4( SourceSpace.ToUnityPosition( line.Start ) ) );
					points.Add( Matrix.MultiplyPoint3x4( SourceSpace.ToUnityPosition( line.End ) ) );
				}
				(Edges, EdgePoints) = (edges.ToArray(), points.ToArray());
			}

			public void FillVertices( HammerMesh component )
			{
				if ( Vertices != null ) return;
				var mesh = component.Mesh;
				var handles = new List<HalfEdgeMesh.VertexHandle>();
				var points = new List<Vector3>();
				foreach ( var v in mesh.VertexHandles )
				{
					if ( mesh.IsVertexHidden( v ) ) continue;
					handles.Add( v );
					points.Add( Matrix.MultiplyPoint3x4( SourceSpace.ToUnityPosition( mesh.GetVertexPosition( v ) ) ) );
				}
				(Vertices, VertexPositions) = (handles.ToArray(), points.ToArray());
			}
		}

		static readonly Dictionary<HammerMesh, WorldData> _world = new();

		static WorldData World( HammerMesh component )
		{
			var mesh = component.Mesh;
			var matrix = component.transform.localToWorldMatrix;
			if ( !mesh.IsDirty && _world.TryGetValue( component, out var cached ) && ReferenceEquals( cached.Source, mesh )
				&& cached.Triangulation == mesh.TriangulationVersion && cached.Matrix == matrix )
				return cached;

			var vertices = mesh.CollisionVertices;
			var indices = mesh.CollisionIndices;
			var triangles = indices.Count / 3;
			var data = new WorldData
			{
				Source = mesh,
				Triangulation = mesh.TriangulationVersion,
				Matrix = matrix,
				Triangles = new Vector3[triangles * 3],
				Normals = new Vector3[triangles],
			};

			for ( int tri = 0; tri < triangles; tri++ )
			{
				var i = tri * 3;
				data.Triangles[i] = matrix.MultiplyPoint3x4( SourceSpace.ToUnityPosition( vertices[indices[i]] ) );
				data.Triangles[i + 1] = matrix.MultiplyPoint3x4( SourceSpace.ToUnityPosition( vertices[indices[i + 1]] ) );
				data.Triangles[i + 2] = matrix.MultiplyPoint3x4( SourceSpace.ToUnityPosition( vertices[indices[i + 2]] ) );

				// s&box triangles are CCW around their normal in source space; the world normal is
				// the source normal carried through the mirror into Unity space
				var sa = vertices[indices[i]];
				var sn = S.Vector3.Cross( vertices[indices[i + 1]] - sa, vertices[indices[i + 2]] - sa );
				data.Normals[tri] = component.SourceDirectionToWorld( sn );
			}

			// (one being edited right now changes again next time: not worth keeping)
			if ( !mesh.IsDirty ) _world[component] = data;
			return data;
		}

		static float DistancePointSegment( Vector2 p, Vector2 a, Vector2 b, out float t )
		{
			var ab = b - a;
			var len = ab.sqrMagnitude;
			t = len > 1e-6f ? Mathf.Clamp01( Vector2.Dot( p - a, ab ) / len ) : 0;
			return Vector2.Distance( p, a + ab * t );
		}
	}
}
