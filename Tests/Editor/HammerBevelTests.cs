using System.Collections.Generic;
using System.Linq;
using HammerUnity.EditorTools;
using NUnit.Framework;
using Sandbox.Primitives;
using UnityEngine;
using Object = UnityEngine.Object;
using S = Sandbox;

namespace HammerUnity.Tests
{
	/// <summary>
	/// Bevel (F) in every common situation, checking the actual geometry: where the new edges are,
	/// how many there are, and that rounded bevels really are round.
	/// </summary>
	public class HammerBevelTests
	{
		HammerMeshTool _tool;
		readonly List<GameObject> _objects = new();
		float _grid;
		int _segments;

		[SetUp]
		public void SetUp()
		{
			_grid = HammerSettings.GridSize;
			_segments = HammerSettings.BevelSegments;
			_tool = ScriptableObject.CreateInstance<HammerMeshTool>();
		}

		[TearDown]
		public void TearDown()
		{
			foreach ( var go in _objects ) if ( go != null ) Object.DestroyImmediate( go );
			Object.DestroyImmediate( _tool );
			HammerSettings.GridSize = _grid;
			HammerSettings.BevelSegments = _segments;
		}

		HammerMesh Box( S.Vector3 size )
		{
			var go = new GameObject( "Bevel" );
			_objects.Add( go );
			var c = go.AddComponent<HammerMesh>();
			c.Mesh = new BlockPrimitive().CreateMesh( new S.BBox( -size / 2, size / 2 ) );
			return c;
		}

		void Bevel( HammerMesh c, IEnumerable<HalfEdgeMesh.HalfEdgeHandle> edges, float width, int segments )
		{
			_tool.Mode = EditMode.Edge;
			_tool.Selection.Clear();
			foreach ( var e in edges ) _tool.Selection.Add( new MeshEdge( c, e ) );
			HammerSettings.GridSize = width;
			HammerSettings.BevelSegments = segments;
			_tool.QuickBevelEdges();
		}

		static void AssertSound( HammerMesh c )
		{
			Assert.That( c.Mesh.FindBadFaces( includeNonPlanar: true ), Is.Empty, "no broken or bent faces" );
			Assert.That( c.Mesh.HalfEdgeHandles.Count( h => c.Mesh.IsEdgeOpen( h ) ), Is.Zero, "still closed" );
		}

		static HalfEdgeMesh.HalfEdgeHandle Edge( HammerMesh c, System.Func<S.Vector3, S.Vector3, bool> where ) =>
			c.Mesh.HalfEdgeHandles.First( h => { var l = c.Mesh.GetEdgeLine( h ); return where( l.Start, l.End ); } );

		static List<S.Vector3> Points( HammerMesh c ) => c.Mesh.VertexHandles.Select( v => c.Mesh.GetVertexPosition( v ) ).ToList();

		/// <summary>
		/// One handle per edge (not both halves).
		/// </summary>
		static List<HalfEdgeMesh.HalfEdgeHandle> Unique( S.PolygonMesh mesh, IEnumerable<HalfEdgeMesh.HalfEdgeHandle> edges )
		{
			var unique = new List<HalfEdgeMesh.HalfEdgeHandle>();
			foreach ( var h in edges )
				if ( !unique.Any( u => u == h || mesh.GetOppositeHalfEdge( u ) == h ) ) unique.Add( h );
			return unique;
		}

		// ── A corner ──

		[TestCase( 1 ), TestCase( 2 ), TestCase( 3 ), TestCase( 6 )]
		public void CornerEdgeIsRounded( int segments )
		{
			// Top edge along X at y = +64, z = +64 of a 128 box, bevelled 16
			var c = Box( new S.Vector3( 128 ) );
			Bevel( c, new[] { Edge( c, ( a, b ) => a.y > 63 && b.y > 63 && a.z > 63 && b.z > 63 ) }, 16, segments );
			AssertSound( c );

			// The new points at one end (x = +64): on a quarter circle round (y 48, z 48), radius 16
			var end = Points( c ).Where( p => p.x > 63 && p.y > 47.9f && p.z > 47.9f ).ToList();
			Assert.That( end.Count, Is.EqualTo( segments + 1 ), "segments + 1 points across the bevel" );

			if ( segments > 1 )
			{
				// Round (Hammer's curve, a touch fuller than a circle at the middle), and the same
				// both ways round
				foreach ( var p in end )
				{
					var r = new S.Vector3( 0, p.y - 48, p.z - 48 ).Length;
					Assert.That( r, Is.InRange( 15.9f, 17.2f ), $"point {p} is on the rounded profile" );
					Assert.That( end.Any( q => Mathf.Abs( q.y - p.z ) < 0.01f && Mathf.Abs( q.z - p.y ) < 0.01f ), $"{p} has its mirror across the corner" );
				}
			}

			Assert.That( end.Any( p => Mathf.Abs( p.y - 64 ) < 0.01f && Mathf.Abs( p.z - 48 ) < 0.01f ), "meets the side 16 down" );
			Assert.That( end.Any( p => Mathf.Abs( p.z - 64 ) < 0.01f && Mathf.Abs( p.y - 48 ) < 0.01f ), "meets the top 16 in" );
			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 6 + segments ) );
		}

		[TestCase( 1 ), TestCase( 3 )]
		public void TopLoopIsRoundedAllRound( int segments )
		{
			var c = Box( new S.Vector3( 128 ) );
			var top = c.Mesh.HalfEdgeHandles.Where( h => { var l = c.Mesh.GetEdgeLine( h ); return l.Start.z > 63 && l.End.z > 63; } ).ToList();
			Bevel( c, Unique( c.Mesh, top ), 16, segments );
			AssertSound( c );

			// Every side gets the same profile: symmetric in x and y
			var pts = Points( c );
			foreach ( var p in pts )
			{
				Assert.That( pts.Any( q => (q - new S.Vector3( -p.x, p.y, p.z )).Length < 0.01f ), $"{p} mirrored in x" );
				Assert.That( pts.Any( q => (q - new S.Vector3( p.x, -p.y, p.z )).Length < 0.01f ), $"{p} mirrored in y" );
			}

			// The top face is a 96 square
			var topFace = c.Mesh.FaceHandles.OrderByDescending( f => c.Mesh.GetFaceCenter( f ).z ).First();
			var tv = c.Mesh.GetFaceVertices( topFace ).Select( v => c.Mesh.GetVertexPosition( v ) ).ToList();
			Assert.That( tv.All( p => Mathf.Abs( p.z - 64 ) < 0.01f && Mathf.Abs( Mathf.Abs( p.x ) - 48 ) < 0.01f && Mathf.Abs( Mathf.Abs( p.y ) - 48 ) < 0.01f ), "top face shrunk by the bevel" );
		}

		[TestCase( 1 ), TestCase( 3 )]
		public void InsideCornerOfARoom( int segments )
		{
			// A room (box with its faces turned inward); bevel an upright inside corner by 64:
			// one segment makes a single flat face across the corner (like Hammer)
			var c = Box( new S.Vector3( 512 ) );
			c.Mesh.FlipAllFaces();
			c.Commit();
			var corner = Edge( c, ( a, b ) => a.x > 255 && b.x > 255 && a.y > 255 && b.y > 255 );
			Bevel( c, new[] { corner }, 64, segments );
			AssertSound( c );

			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 6 + segments ) );
			var cornerFaces = c.Mesh.FaceHandles.Where( f => { c.Mesh.ComputeFaceNormal( f, out var n ); return n.x < -0.1f && n.y < -0.1f; } ).ToList();
			Assert.That( cornerFaces.Count, Is.EqualTo( segments ), "faces across the corner" );
			Assert.That( Points( c ).Any( p => Mathf.Abs( p.x - 256 ) < 0.01f && Mathf.Abs( p.y - 192 ) < 0.01f ), "64 along one wall" );
			Assert.That( Points( c ).Any( p => Mathf.Abs( p.y - 256 ) < 0.01f && Mathf.Abs( p.x - 192 ) < 0.01f ), "64 along the other" );
			Assert.That( _tool.SelectedEdges.Count(), Is.EqualTo( 2 + 2 * segments ), "the bevel's outline is selected: two along it, and its ends" );
		}

		// ── Flat cuts ──

		/// <summary>
		/// Cut a face of a 512 wall into columns with Quad Slice: the cut edges are flat (both
		/// faces in one plane).
		/// </summary>
		HammerMesh SlicedWall( int cuts, out List<HalfEdgeMesh.HalfEdgeHandle> cutEdges, out List<float> cutYs )
		{
			var c = Box( new S.Vector3( 16, 512, 512 ) );
			var front = c.Mesh.FaceHandles.First( f => { c.Mesh.ComputeFaceNormal( f, out var n ); return n.x < -0.99f; } );
			c.Mesh.QuadSliceFaces( new List<HalfEdgeMesh.FaceHandle> { front }, cuts, 0, 60, new List<HalfEdgeMesh.FaceHandle>() );
			c.Commit();

			var mesh = c.Mesh;
			cutEdges = Unique( mesh, mesh.HalfEdgeHandles.Where( h =>
			{
				var l = mesh.GetEdgeLine( h );
				return l.Start.x < -7.9f && l.End.x < -7.9f && Mathf.Abs( l.Start.y - l.End.y ) < 0.01f && Mathf.Abs( l.Start.y ) < 255;
			} ) );
			cutYs = cutEdges.Select( h => mesh.GetEdgeLine( h ).Start.y ).OrderBy( y => y ).ToList();
			return c;
		}

		static List<float> FrontColumns( HammerMesh c ) =>
			c.Mesh.VertexHandles.Select( v => c.Mesh.GetVertexPosition( v ) ).Where( p => p.x < -7.9f ).Select( p => Mathf.Round( p.y * 100 ) / 100 ).Distinct().OrderBy( y => y ).ToList();

		[TestCase( 1, 1 ), TestCase( 1, 3 ), TestCase( 3, 1 ), TestCase( 3, 4 )]
		public void FlatCutsSplitInTwo( int cuts, int segments )
		{
			var c = SlicedWall( cuts, out var edges, out var ys );
			Assert.That( edges.Count, Is.EqualTo( cuts ) );

			Bevel( c, edges, 16, segments );
			AssertSound( c );

			var expected = new List<float> { -256, 256 };
			foreach ( var y in ys ) { expected.Add( y - 16 ); expected.Add( y + 16 ); }
			Assert.That( FrontColumns( c ), Is.EquivalentTo( expected.Select( x => Mathf.Round( x * 100 ) / 100 ) ), "two edges per cut, 16 either side, whatever the segments" );
			Assert.That( _tool.SelectedEdges.Count(), Is.EqualTo( cuts * 4 ), "each bevel's outline is selected" );
		}

		[Test]
		public void FlatCutsCloseTogetherStillFit()
		{
			// 3 cuts 128 apart, bevel 64: the new edges would meet; the bevel narrows to fit
			var c = SlicedWall( 3, out var edges, out _ );
			Bevel( c, edges, 64, 1 );
			AssertSound( c );
			Assert.That( FrontColumns( c ).Count, Is.EqualTo( 2 + 3 * 2 ) );
		}

		[Test]
		public void SubdividedFaceEdges()
		{
			// A face split 2 x 2: bevel the middle upright line (two edges, end to end)
			var c = Box( new S.Vector3( 16, 512, 512 ) );
			var front = c.Mesh.FaceHandles.First( f => { c.Mesh.ComputeFaceNormal( f, out var n ); return n.x < -0.99f; } );
			c.Mesh.QuadSliceFaces( new List<HalfEdgeMesh.FaceHandle> { front }, 1, 1, 60, new List<HalfEdgeMesh.FaceHandle>() );
			c.Commit();

			var mesh = c.Mesh;
			var middle = Unique( mesh, mesh.HalfEdgeHandles.Where( h => { var l = mesh.GetEdgeLine( h ); return l.Start.x < -7.9f && l.End.x < -7.9f && Mathf.Abs( l.Start.y ) < 0.01f && Mathf.Abs( l.End.y ) < 0.01f; } ) );
			Assert.That( middle.Count, Is.EqualTo( 2 ), "the line down the middle is two edges" );

			Bevel( c, middle, 16, 3 );
			AssertSound( c );
			Assert.That( FrontColumns( c ), Is.EquivalentTo( new[] { -256f, -16f, 16f, 256f } ), "the line becomes two, 16 either side" );
		}
	}
}
