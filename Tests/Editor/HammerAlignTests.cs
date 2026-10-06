using System.Collections.Generic;
using System.Linq;
using HammerUnity.EditorTools;
using NUnit.Framework;
using Sandbox.Primitives;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using S = Sandbox;

namespace HammerUnity.Tests
{
	/// <summary>
	/// Move to Furthest, Radial Align, Move Path Trace Down, Subdivide and the displacement brush.
	/// </summary>
	public class HammerAlignTests
	{
		HammerMeshTool _tool;
		readonly List<GameObject> _objects = new();

		[SetUp]
		public void SetUp()
		{
			Undo.ClearAll();
			_tool = ScriptableObject.CreateInstance<HammerMeshTool>();
		}

		[TearDown]
		public void TearDown()
		{
			foreach ( var go in _objects ) if ( go != null ) Object.DestroyImmediate( go );
			_objects.Clear();
			Object.DestroyImmediate( _tool );
			UnityEditor.Selection.objects = new Object[0];
			Undo.ClearAll();
		}

		HammerMesh Make( PrimitiveBuilder builder, S.Vector3 center, S.Vector3 size )
		{
			var go = new GameObject( "Align Test" );
			_objects.Add( go );
			go.transform.position = SourceSpace.ToUnityPosition( center );
			var c = go.AddComponent<HammerMesh>();
			c.Mesh = builder.CreateMesh( new S.BBox( -size / 2, size / 2 ) );
			return c;
		}

		HammerMesh Box( S.Vector3 center, S.Vector3 size ) => Make( new BlockPrimitive(), center, size );

		static MeshFace Facing( HammerMesh c, S.Vector3 dir ) => new( c, c.Mesh.FaceHandles.OrderByDescending( f => { c.Mesh.ComputeFaceNormal( f, out var n ); return S.Vector3.Dot( n, dir ); } ).First() );

		static float Top( HammerMesh c ) => c.Mesh.VertexHandles.Max( v => SourceSpace.ToSourcePosition( c.SourceToWorld( c.Mesh.GetVertexPosition( v ) ) ).z );
		static float Bottom( HammerMesh c ) => c.Mesh.VertexHandles.Min( v => SourceSpace.ToSourcePosition( c.SourceToWorld( c.Mesh.GetVertexPosition( v ) ) ).z );

		void Select( EditMode mode, params IMeshElement[] elements )
		{
			_tool.Mode = mode;
			_tool.Selection.Clear();
			foreach ( var e in elements ) _tool.Selection.Add( e );
		}

		[Test]
		public void MoveToFurthestMovesFacesFlush()
		{
			var tall = Box( new S.Vector3( 0, 0, 64 ), new S.Vector3( 64, 64, 128 ) );
			var low = Box( new S.Vector3( 128, 0, 32 ), new S.Vector3( 64, 64, 64 ) );
			Select( EditMode.Face, Facing( tall, new S.Vector3( 0, 0, 1 ) ), Facing( low, new S.Vector3( 0, 0, 1 ) ) );

			_tool.MoveToFurthest( 2, 1 );

			Assert.That( Top( low ), Is.EqualTo( 128 ).Within( 0.01f ), "the low box's top rose to the tall one's" );
			Assert.That( Top( tall ), Is.EqualTo( 128 ).Within( 0.01f ) );

			Undo.PerformUndo();
			low.RebuildIfReloaded();
			Assert.That( Top( low ), Is.EqualTo( 64 ).Within( 0.01f ), "undo puts it back" );
		}

		[Test]
		public void MoveToFurthestFlattensVertices()
		{
			var box = Box( new S.Vector3( 0, 0, 64 ), new S.Vector3( 64, 64, 128 ) );
			var mesh = box.Mesh;
			var side = mesh.VertexHandles.Where( v => mesh.GetVertexPosition( v ).x > 0 ).Select( v => (IMeshElement)new MeshVertex( box, v ) ).ToArray();
			Select( EditMode.Vertex, side );

			_tool.MoveToFurthest( 2, -1 );

			foreach ( MeshVertex v in side )
				Assert.That( SourceSpace.ToSourcePosition( v.PositionWorld ).z, Is.EqualTo( 0 ).Within( 0.01f ) );
		}

		[Test]
		public void RadialAlignMakesACircle()
		{
			var cylinder = Make( new CylinderPrimitive { NumberOfSides = 12 }, S.Vector3.Zero, new S.Vector3( 128, 128, 64 ) );
			var mesh = cylinder.Mesh;
			var top = Facing( cylinder, new S.Vector3( 0, 0, 1 ) );

			// Knock one corner out of the circle
			var corner = mesh.GetFaceVertices( top.Handle )[0];
			mesh.SetVertexPosition( corner, mesh.GetVertexPosition( corner ) * 1.4f + new S.Vector3( 0, 0, 0 ) );
			var p = mesh.GetVertexPosition( corner );
			mesh.SetVertexPosition( corner, new S.Vector3( p.x, p.y, 32 ) );
			cylinder.Commit();

			Select( EditMode.Face, top );
			_tool.RadialAlign();

			var points = mesh.GetFaceVertices( top.Handle ).Select( v => mesh.GetVertexPosition( v ) ).ToList();
			var center = points.Aggregate( S.Vector3.Zero, ( a, b ) => a + b ) / points.Count;
			var radii = points.Select( x => (x - center).Length ).ToList();
			Assert.That( radii.Max() - radii.Min(), Is.LessThan( 0.05f ), "every corner the same distance from the middle" );
		}

		[Test]
		public void TraceDownDropsAnObjectOntoTheFloor()
		{
			Box( new S.Vector3( 0, 0, -8 ), new S.Vector3( 512, 512, 16 ) );
			var crate = Box( new S.Vector3( 0, 0, 200 ), new S.Vector3( 32, 32, 32 ) );
			_tool.Mode = EditMode.Object;
			UnityEditor.Selection.objects = new Object[] { crate.gameObject };

			_tool.MovePathTraceDown();

			Assert.That( Bottom( crate ), Is.EqualTo( 0 ).Within( 0.05f ), "resting on the floor" );
		}

		[Test]
		public void TraceDownDropsAFace()
		{
			Box( new S.Vector3( 0, 0, -8 ), new S.Vector3( 512, 512, 16 ) );
			var box = Box( new S.Vector3( 0, 0, 100 ), new S.Vector3( 64, 64, 64 ) );
			Select( EditMode.Face, Facing( box, new S.Vector3( 0, 0, -1 ) ) );

			_tool.MovePathTraceDown();

			Assert.That( Bottom( box ), Is.EqualTo( 0 ).Within( 0.05f ), "bottom face stretched down to the floor" );
			Assert.That( Top( box ), Is.EqualTo( 132 ).Within( 0.05f ), "top stayed" );
		}

		[Test]
		public void SubdivideSplitsFacesInFour()
		{
			var box = Box( S.Vector3.Zero, new S.Vector3( 64, 64, 64 ) );
			Select( EditMode.Face, Facing( box, new S.Vector3( 0, 0, 1 ) ) );
			_tool.Subdivide();
			Assert.That( box.Mesh.FaceHandles.Count(), Is.EqualTo( 5 + 4 ) );
			Assert.That( _tool.SelectedFaces.Count(), Is.EqualTo( 4 ), "the new faces stay selected" );

			_tool.Mode = EditMode.Object;
			UnityEditor.Selection.objects = new Object[] { box.gameObject };
			_tool.Subdivide();
			Assert.That( box.Mesh.FaceHandles.Count(), Is.EqualTo( 9 * 4 ) );
			Assert.That( box.Mesh.FindBadFaces(), Is.Empty );
		}

		[Test]
		public void DisplacementBrush()
		{
			var ground = Make( new QuadPrimitive(), S.Vector3.Zero, new S.Vector3( 256, 256, 0 ) );
			_tool.Mode = EditMode.Object;
			UnityEditor.Selection.objects = new Object[] { ground.gameObject };
			for ( int i = 0; i < 3; i++ ) _tool.Subdivide();

			var mesh = ground.Mesh;
			float Height() => mesh.VertexHandles.Max( v => mesh.GetVertexPosition( v ).z );
			var center = ground.transform.position;

			Assert.That( DisplacementTool.Displace( ground, center, Vector3.up, center, DisplaceMode.PushPull, false, 64, 1, 0.3f ), Is.True );
			var raised = Height();
			Assert.That( raised, Is.GreaterThan( 1 ), "pushed up a bump" );

			var edge = mesh.VertexHandles.Count( v => mesh.GetVertexPosition( v ).z > 0.001f );
			Assert.That( edge, Is.LessThan( mesh.VertexHandles.Count() ), "only inside the brush" );

			for ( int i = 0; i < 10; i++ )
				DisplacementTool.Displace( ground, center, Vector3.up, center, DisplaceMode.Smooth, false, 128, 1, 0.3f );
			Assert.That( Height(), Is.LessThan( raised ), "smoothing lowers the bump" );

			DisplacementTool.Displace( ground, center, Vector3.up, center + Vector3.down, DisplaceMode.Flatten, false, 512, 1, 1 );
			Assert.That( Height(), Is.LessThan( raised * 0.75f ), "flatten pulls towards the stroke plane" );
		}

		[Test]
		public void DisplacementNewBrushes()
		{
			var ground = Make( new QuadPrimitive(), S.Vector3.Zero, new S.Vector3( 256, 256, 0 ) );
			_tool.Mode = EditMode.Object;
			UnityEditor.Selection.objects = new Object[] { ground.gameObject };
			for ( int i = 0; i < 3; i++ ) _tool.Subdivide();

			var mesh = ground.Mesh;
			float Height() => mesh.VertexHandles.Max( v => mesh.GetVertexPosition( v ).z );
			float Lowest() => mesh.VertexHandles.Min( v => mesh.GetVertexPosition( v ).z );
			var center = ground.transform.position;
			float Flat( float t ) => 1;

			// Raise To: up towards the level, never past it
			var level = center + Vector3.up * 16 * SourceSpace.UnitScale;
			for ( int i = 0; i < 40; i++ )
				DisplacementTool.Displace( ground, center, Vector3.up, level, DisplaceMode.RaiseTo, false, 64, 1, Flat, null );
			Assert.That( Height(), Is.GreaterThan( 12 ).And.LessThanOrEqualTo( 16.01f ), "raised up to the level" );

			// Noise: some up, some down
			var before = Lowest();
			DisplacementTool.Displace( ground, center + Vector3.right * 2, Vector3.up, center, DisplaceMode.Noise, false, 256, 1, Flat, null );
			Assert.That( Lowest(), Is.LessThan( before ), "noise digs in as well as lifting" );

			// Backfaces: a camera underneath sees none of the upward faces, so nothing moves
			var shape = string.Join( ",", mesh.VertexHandles.Select( v => mesh.GetVertexPosition( v ).z.ToString( "0.000" ) ) );
			DisplacementTool.Displace( ground, center, Vector3.up, center, DisplaceMode.PushPull, false, 64, 1, Flat, center + Vector3.down * 10 );
			Assert.That( string.Join( ",", mesh.VertexHandles.Select( v => mesh.GetVertexPosition( v ).z.ToString( "0.000" ) ) ), Is.EqualTo( shape ), "facing away: left alone" );
		}

		[Test]
		public void DisplacementFalloffPresets()
		{
			var preset = DisplacementTool.Preset;
			try
			{
				typeof( DisplacementTool ).GetMethod( "ApplyPreset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static ).Invoke( null, new object[] { DisplaceBrushPreset.Smooth } );
				Assert.That( DisplacementTool.Falloff01( 0 ), Is.EqualTo( 0 ).Within( 1e-3f ), "nothing at the rim" );
				Assert.That( DisplacementTool.Falloff01( 1 ), Is.EqualTo( 1 ).Within( 1e-3f ), "full in the middle" );
				Assert.That( DisplacementTool.Falloff01( 0.5f ), Is.EqualTo( 0.5f ).Within( 0.05f ), "half way, half strength" );

				typeof( DisplacementTool ).GetMethod( "ApplyPreset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static ).Invoke( null, new object[] { DisplaceBrushPreset.Constant } );
				Assert.That( DisplacementTool.Falloff01( 0.1f ), Is.EqualTo( 1 ), "constant: full everywhere inside" );
			}
			finally
			{
				typeof( DisplacementTool ).GetMethod( "ApplyPreset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static ).Invoke( null, new object[] { preset } );
			}
		}
	}
}
