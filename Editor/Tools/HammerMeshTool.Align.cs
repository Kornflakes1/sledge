using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Hammer's alignment operations: move to furthest, radial align, move path trace down, and
	/// subdivide.
	/// </summary>
	partial class HammerMeshTool
	{
		/// <summary>
		/// A Hammer axis (0 = X, 1 = Y, 2 = Z; s&amp;box space, Z up) as a Unity direction.
		/// </summary>
		static Vector3 HammerAxis( int axis, int sign )
		{
			var v = axis switch { 0 => new S.Vector3( 1, 0, 0 ), 1 => new S.Vector3( 0, 1, 0 ), _ => new S.Vector3( 0, 0, 1 ) };
			return SourceSpace.ToUnityDirection( v ) * sign;
		}

		/// <summary>
		/// The selected elements as groups of vertices that move together: each vertex on its own
		/// in Vertices mode, otherwise each connected piece of the selection.
		/// </summary>
		List<List<MeshVertex>> SelectionIslands()
		{
			var islands = new List<List<MeshVertex>>();

			if ( _mode == EditMode.Vertex )
			{
				foreach ( var v in SelectedVertices ) islands.Add( new List<MeshVertex> { v } );
				return islands;
			}

			// Union-find over the elements, joined where they share a vertex
			var elements = new List<List<MeshVertex>>();
			foreach ( var e in SelectedEdges )
			{
				e.Component.Mesh.GetEdgeVertices( e.Handle, out var a, out var b );
				elements.Add( new List<MeshVertex> { new( e.Component, a ), new( e.Component, b ) } );
			}
			foreach ( var f in SelectedFaces )
				elements.Add( f.Component.Mesh.GetFaceVertices( f.Handle ).Select( v => new MeshVertex( f.Component, v ) ).ToList() );

			var parent = Enumerable.Range( 0, elements.Count ).ToArray();
			int Find( int i ) => parent[i] == i ? i : parent[i] = Find( parent[i] );

			var owner = new Dictionary<MeshVertex, int>();
			for ( int i = 0; i < elements.Count; i++ )
			{
				foreach ( var v in elements[i] )
				{
					if ( owner.TryGetValue( v, out var other ) ) parent[Find( i )] = Find( other );
					else owner[v] = i;
				}
			}

			foreach ( var group in Enumerable.Range( 0, elements.Count ).GroupBy( Find ) )
				islands.Add( group.SelectMany( i => elements[i] ).Distinct().ToList() );

			return islands;
		}

		/// <summary>
		/// Move to Furthest: line the selection up with whichever part of it is furthest along a
		/// direction. Vertices are flattened onto that plane; edges and faces move whole, each
		/// connected piece until it's flush.
		/// </summary>
		/// <param name="axis">Hammer axis: 0 = X, 1 = Y, 2 = Z.</param>
		/// <param name="sign">+1 for the positive end, -1 for the negative.</param>
		public void MoveToFurthest( int axis, int sign )
		{
			var islands = SelectionIslands();
			if ( islands.Count < 2 && _mode != EditMode.Vertex ) return;
			if ( islands.Sum( x => x.Count ) < 2 ) return;

			var dir = HammerAxis( axis, sign );
			var furthest = islands.SelectMany( x => x ).Max( v => Vector3.Dot( v.PositionWorld, dir ) );

			using ( Scope( "Move to Furthest" ) )
			{
				foreach ( var island in islands )
				{
					var delta = dir * (furthest - island.Max( v => Vector3.Dot( v.PositionWorld, dir ) ));
					var targets = island.Select( v => (v, v.PositionWorld + delta) ).ToList();
					foreach ( var (v, world) in targets )
						v.Component.Mesh.SetVertexPosition( v.Handle, v.Component.WorldToSource( world ) );
				}

				foreach ( var c in Selection.Components )
					c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			Done();
		}

		/// <summary>
		/// Radial Align: put the selected vertices on a circle round their middle, in the plane
		/// that fits them best, evenly spaced in the order they go round.
		/// </summary>
		public void RadialAlign()
		{
			var vertices = SelectionVertices().ToList();
			if ( vertices.Count < 3 ) return;
			if ( vertices.Select( v => v.Component ).Distinct().Count() > 1 )
			{
				MeshHealth.Report( "⚠ Radial Align works on one mesh at a time." );
				return;
			}

			var points = vertices.Select( v => v.PositionWorld ).ToList();
			var center = points.Aggregate( Vector3.zero, ( a, p ) => a + p ) / points.Count;
			var normal = BestFitNormal( points, center );
			if ( normal == Vector3.zero ) return;

			var offsets = points.Select( p => Vector3.ProjectOnPlane( p - center, normal ) ).ToList();
			var radius = offsets.Average( o => o.magnitude );
			if ( radius < 1e-5f ) return;

			// A ring of points is what it's for. Points spread out in depth too (all of a box,
			// several objects) would be squashed flat into folded faces: say so instead
			var depth = points.Max( p => Mathf.Abs( Vector3.Dot( p - center, normal ) ) );
			if ( depth > radius * 0.25f )
			{
				MeshHealth.Report( "⚠ Radial Align needs points that lie roughly flat, like a ring of vertices or one face." );
				return;
			}

			// Keep their order round the middle, and the first one's direction; space them evenly
			var first = offsets.First( o => o.sqrMagnitude > 1e-10f ).normalized;
			var side = Vector3.Cross( normal, first );
			float Angle( Vector3 o ) => Mathf.Repeat( Mathf.Atan2( Vector3.Dot( o, side ), Vector3.Dot( o, first ) ), Mathf.PI * 2 );
			var order = Enumerable.Range( 0, vertices.Count ).OrderBy( i => Angle( offsets[i] ) ).ToList();

			using ( Scope( "Radial Align" ) )
			{
				for ( int k = 0; k < order.Count; k++ )
				{
					var i = order[k];
					var angle = Angle( offsets[order[0]] ) + k * Mathf.PI * 2 / order.Count;
					var world = center + (first * Mathf.Cos( angle ) + side * Mathf.Sin( angle )) * radius;
					vertices[i].Component.Mesh.SetVertexPosition( vertices[i].Handle, vertices[i].Component.WorldToSource( world ) );
				}

				foreach ( var c in Selection.Components )
					c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			Done();
		}

		/// <summary>
		/// The normal of the plane that best fits some points: the direction they spread least in.
		/// </summary>
		static Vector3 BestFitNormal( List<Vector3> points, Vector3 center )
		{
			// From the covariance: solve with each axis fixed in turn and keep the best conditioned
			// answer (the largest determinant)
			double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
			foreach ( var p in points )
			{
				var r = p - center;
				xx += r.x * r.x; xy += r.x * r.y; xz += r.x * r.z;
				yy += r.y * r.y; yz += r.y * r.z; zz += r.z * r.z;
			}

			var detX = yy * zz - yz * yz;
			var detY = xx * zz - xz * xz;
			var detZ = xx * yy - xy * xy;
			var max = System.Math.Max( detX, System.Math.Max( detY, detZ ) );
			if ( max <= 1e-12 ) return Vector3.zero;

			Vector3 n;
			if ( max == detX ) n = new Vector3( (float)detX, (float)(xz * yz - xy * zz), (float)(xy * yz - xz * yy) );
			else if ( max == detY ) n = new Vector3( (float)(xz * yz - xy * zz), (float)detY, (float)(xy * xz - yz * xx) );
			else n = new Vector3( (float)(xy * yz - xz * yy), (float)(xy * xz - yz * xx), (float)detZ );
			return n.normalized;
		}

		/// <summary>
		/// Move Path Trace Down: drop the selection straight down until it lands on other
		/// geometry. Works on elements and, in Meshes mode, on whole objects.
		/// </summary>
		public void MovePathTraceDown()
		{
			var objects = _mode == EditMode.Object;
			var moving = objects ? SelectedObjectMeshes().ToList() : Selection.Components.ToList();
			if ( moving.Count == 0 ) return;

			var points = objects
				? moving.SelectMany( c => c.Mesh.VertexHandles.Select( v => c.SourceToWorld( c.Mesh.GetVertexPosition( v ) ) ) ).ToList()
				: SelectionVertices().Select( v => v.PositionWorld ).ToList();
			if ( points.Count == 0 ) return;

			var others = Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None )
				.Where( c => c.isActiveAndEnabled && !moving.Contains( c ) ).ToList();

			if ( !TraceDown( points, others, out var drop ) )
				return;

			var delta = Vector3.down * drop;

			if ( objects )
			{
				Undo.RecordObjects( moving.Select( c => (Object)c.transform ).ToArray(), "Move Path Trace Down" );
				foreach ( var c in moving ) c.transform.position += delta;
			}
			else
			{
				using ( Scope( "Move Path Trace Down" ) )
				{
					foreach ( var v in SelectionVertices() )
						v.Component.Mesh.SetVertexPosition( v.Handle, v.Component.WorldToSource( v.PositionWorld + delta ) );
					foreach ( var c in Selection.Components )
						c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}

			Done();
		}

		/// <summary>
		/// How far the points can fall before the first one lands on something.
		/// </summary>
		internal static bool TraceDown( IEnumerable<Vector3> points, IList<HammerMesh> others, out float distance )
		{
			const float lift = 0.001f;
			distance = float.MaxValue;

			foreach ( var p in points )
			{
				if ( MeshPicking.RaycastFace( new Ray( p + Vector3.up * lift, Vector3.down ), others, out var hit ) )
					distance = Mathf.Min( distance, hit.Distance - lift );
			}

			if ( distance == float.MaxValue || distance <= 1e-5f )
				return false;

			return true;
		}

		/// <summary>
		/// Split each selected face (every face, in Meshes mode) into four, for more detail to
		/// shape, sculpt or paint.
		/// </summary>
		public void Subdivide()
		{
			var groups = _mode == EditMode.Object
				? SelectedObjectMeshes().Select( c => (c, c.Mesh.FaceHandles.ToList()) ).ToList()
				: SelectedFaces.GroupBy( x => x.Component ).Select( g => (g.Key, g.Select( x => x.Handle ).ToList()) ).ToList();

			if ( groups.Count == 0 ) return;

			using ( Scope( "Subdivide", groups.Select( x => x.Item1 ) ) )
			{
				var keep = _mode == EditMode.Face;
				Selection.Clear();

				foreach ( var (component, faces) in groups )
				{
					var created = new List<FaceHandle>();
					component.Mesh.QuadSliceFaces( faces, 1, 1, 60.0f, created );
					component.Mesh.ComputeFaceTextureCoordinatesFromParameters();

					if ( keep )
						foreach ( var f in created ) Selection.Add( new MeshFace( component, f ) );
				}
			}

			Done();
		}
	}
}
