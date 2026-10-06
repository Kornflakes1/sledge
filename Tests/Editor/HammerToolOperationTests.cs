using System.Collections.Generic;
using System.Linq;
using HammerUnity.EditorTools;
using NUnit.Framework;
using Sandbox.Primitives;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.Tests
{
	/// <summary>
	/// Runs the tool's mesh operations against real components to make sure the Unity glue
	/// (selection, undo scopes, rebaking) holds together.
	/// </summary>
	public class HammerToolOperationTests
	{
		readonly List<GameObject> _objects = new();
		HammerMeshTool _tool;

		float _grid;
		int _segments;

		[SetUp]
		public void SetUp()
		{
			_tool = ScriptableObject.CreateInstance<HammerMeshTool>();

			// Grid size is a user preference; pin it so results don't depend on it
			_grid = HammerSettings.GridSize;
			HammerSettings.GridSize = 8;
			_segments = HammerSettings.BevelSegments;
			HammerSettings.BevelSegments = 1;
		}

		[TearDown]
		public void TearDown()
		{
			foreach ( var go in _objects.Where( x => x != null ) )
				Object.DestroyImmediate( go );

			_objects.Clear();
			Object.DestroyImmediate( _tool );
			Selection.objects = new Object[0];
			HammerSettings.GridSize = _grid;
			HammerSettings.BevelSegments = _segments;
		}

		HammerMesh Box( Vector3 position = default, float size = 64 )
		{
			var go = new GameObject( "Box" );
			_objects.Add( go );
			go.transform.position = position;
			var c = go.AddComponent<HammerMesh>();
			c.Mesh = new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -size / 2 ), new S.Vector3( size / 2 ) ) );
			return c;
		}

		static MeshFace TopFace( HammerMesh c )
		{
			var f = c.Mesh.FaceHandles.First( h => { c.Mesh.ComputeFaceNormal( h, out var n ); return n.z > 0.9f; } );
			return new MeshFace( c, f );
		}

		void SetMode( EditMode mode )
		{
			_tool.Mode = mode;
		}

		[Test]
		public void FastTextureDragMovesUVsTheSameWay()
		{
			var c = Box();
			SetMode( EditMode.Face );
			var face = TopFace( c );
			_tool.Selection.Add( face );

			var before = c.Mesh.GetFaceTextureCoords( face.Handle );
			_tool.MoveTextureUV( new Vector2( 32, 16 ) );
			var after = c.Mesh.GetFaceTextureCoords( face.Handle );

			var size = face.Material?.TextureSize ?? new S.Vector2( 512, 512 );
			Assert.That( after[0].x - before[0].x, Is.EqualTo( 32 / size.x ).Within( 1e-3f ) );
			Assert.That( after[0].y - before[0].y, Is.EqualTo( 16 / size.y ).Within( 1e-3f ) );
		}

		[Test]
		public void PaintMaterialChangesOnlyThatFace()
		{
			var c = Box();
			var face = TopFace( c );
			var material = S.Material.Load( "test/paint.vmat" );
			c.Mesh.SetFaceMaterial( face.Handle, material );
			Assert.That( c.Mesh.FaceHandles.Count( f => c.Mesh.GetFaceMaterial( f )?.Name == "test/paint.vmat" ), Is.EqualTo( 1 ) );
		}

		[Test]
		public void ShiftSwitchToEdgesSelectsBoundary()
		{
			var c = Box();
			SetMode( EditMode.Face );
			// Top face plus one side face: 7 distinct edges, minus the one they share
			var top = TopFace( c );
			var side = c.Mesh.FaceHandles.First( h => { c.Mesh.ComputeFaceNormal( h, out var n ); return n.x > 0.9f; } );
			_tool.Selection.Add( top );
			_tool.Selection.Add( new MeshFace( c, side ) );

			_tool.SwitchMode( EditMode.Edge, SelectionConversion.Boundary );

			Assert.That( _tool.Mode, Is.EqualTo( EditMode.Edge ) );
			Assert.That( _tool.Selection.OfType<MeshEdge>().Count(), Is.EqualTo( 6 ) );
		}

		[Test]
		public void AltSwitchToFacesSelectsTouching()
		{
			var c = Box();
			SetMode( EditMode.Vertex );
			_tool.Selection.Add( new MeshVertex( c, c.Mesh.VertexHandles.First() ) );

			_tool.SwitchMode( EditMode.Face, SelectionConversion.Convert );

			// A box corner touches three faces
			Assert.That( _tool.Selection.OfType<MeshFace>().Count(), Is.EqualTo( 3 ) );
		}

		[Test]
		public void CtrlSwitchToEdgesConnectsVertices()
		{
			var c = Box();
			SetMode( EditMode.Face );
			_tool.Selection.Add( TopFace( c ) );
			SetMode( EditMode.Vertex );
			Assert.That( _tool.Selection.Count, Is.EqualTo( 4 ) );

			_tool.SwitchMode( EditMode.Edge, SelectionConversion.Connect );

			Assert.That( _tool.Selection.OfType<MeshEdge>().Count(), Is.EqualTo( 4 ) );
		}

		[Test]
		public void InsetTopFace()
		{
			var c = Box();
			SetMode( EditMode.Face );
			_tool.Selection.Add( TopFace( c ) );

			_tool.InsetFaces();

			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 10 ) );
			Assert.That( _tool.Selection.Count, Is.EqualTo( 1 ) );
		}

		[Test]
		public void ExtrudeTopFaceByGrid()
		{
			var c = Box();
			SetMode( EditMode.Face );
			_tool.Selection.Add( TopFace( c ) );

			_tool.ExtrudeFaces();

			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 10 ) );
			var top = c.Mesh.CalculateBounds().Maxs.z;
			Assert.That( top, Is.EqualTo( 32 + HammerSettings.GridSize ).Within( 1e-3f ) );
		}

		[Test]
		public void BevelEdge()
		{
			var c = Box();
			SetMode( EditMode.Edge );
			_tool.Selection.Add( new MeshEdge( c, c.Mesh.HalfEdgeHandles.First() ) );

			_tool.QuickBevelEdges();

			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 7 ) );
			Assert.That( c.Mesh.BadFaces.Count, Is.EqualTo( 0 ) );
		}

		[Test]
		public void LoopCutViaRingAndConnect()
		{
			var c = Box();
			SetMode( EditMode.Edge );
			_tool.Selection.Add( new MeshEdge( c, c.Mesh.HalfEdgeHandles.First() ) );

			_tool.SelectRing();
			Assert.That( _tool.Selection.Count, Is.EqualTo( 4 ) );

			_tool.ConnectEdges();
			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 10 ) );
		}

		[Test]
		public void MergeVerticesCollapsesToOne()
		{
			var c = Box();
			SetMode( EditMode.Vertex );
			var top = TopFace( c );
			foreach ( var v in c.Mesh.GetFaceVertices( top.Handle ) )
				_tool.Selection.Add( new MeshVertex( c, v ) );

			_tool.MergeVertices();

			Assert.That( c.Mesh.VertexHandles.Count(), Is.EqualTo( 5 ) );
		}

		[Test]
		public void DeleteFaceThenFillHole()
		{
			var c = Box();
			SetMode( EditMode.Face );
			_tool.Selection.Add( TopFace( c ) );
			_tool.Delete();
			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 5 ) );

			SetMode( EditMode.Edge );
			var open = c.Mesh.HalfEdgeHandles.First( e => c.Mesh.IsEdgeOpen( e ) );
			_tool.Selection.Add( new MeshEdge( c, open ) );
			_tool.FillHole();

			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 6 ) );
		}

		[Test]
		public void ExtractFacesMakesNewObject()
		{
			var c = Box();
			SetMode( EditMode.Face );
			_tool.Selection.Add( TopFace( c ) );

			_tool.ExtractFaces();

			var created = Selection.gameObjects.Select( x => x.GetComponent<HammerMesh>() ).Where( x => x != null && x != c ).ToList();
			_objects.AddRange( created.Select( x => x.gameObject ) );

			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 5 ) );
			Assert.That( created.Count, Is.EqualTo( 1 ) );
			Assert.That( created[0].Mesh.FaceHandles.Count(), Is.EqualTo( 1 ) );

			// The extracted face stays where it was in the world
			var worldTop = created[0].RenderMesh.bounds.center + created[0].transform.position;
			Assert.That( worldTop.y, Is.EqualTo( 32 * SourceSpace.UnitScale ).Within( 1e-4f ) );
		}

		[Test]
		public void MergeAndSeparateObjects()
		{
			var a = Box( Vector3.zero );
			var b = Box( new Vector3( 5, 0, 0 ) );
			SetMode( EditMode.Object );

			Selection.objects = new Object[] { a.gameObject, b.gameObject };
			Selection.activeGameObject = a.gameObject;
			_tool.MergeMeshes();

			Assert.That( a.Mesh.FaceHandles.Count(), Is.EqualTo( 12 ) );
			Assert.That( b == null, Is.True );

			Selection.objects = new Object[] { a.gameObject };
			_tool.SeparateComponents();

			var pieces = Selection.gameObjects.Select( x => x.GetComponent<HammerMesh>() ).Where( x => x != null ).ToList();
			_objects.AddRange( pieces.Select( x => x.gameObject ) );
			Assert.That( pieces.Count, Is.EqualTo( 2 ) );
			Assert.That( pieces.All( x => x.Mesh.FaceHandles.Count() == 6 ), Is.True );
		}

		[Test]
		public void ThickenQuad()
		{
			var go = new GameObject( "Quad" );
			_objects.Add( go );
			var c = go.AddComponent<HammerMesh>();
			c.Mesh = new QuadPrimitive().CreateMesh( new S.BBox( new S.Vector3( -32, -32, 0 ), new S.Vector3( 32, 32, 0 ) ) );

			SetMode( EditMode.Face );
			_tool.Selection.Add( new MeshFace( c, c.Mesh.FaceHandles.First() ) );
			_tool.ThickenFaces();

			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 6 ) );
		}

		[Test]
		public void ModeSwitchConvertsSelection()
		{
			var c = Box();
			SetMode( EditMode.Face );
			_tool.Selection.Add( TopFace( c ) );

			SetMode( EditMode.Vertex );
			Assert.That( _tool.SelectedVertices.Count(), Is.EqualTo( 4 ) );

			SetMode( EditMode.Face );
			Assert.That( _tool.SelectedFaces.Count(), Is.EqualTo( 1 ) );

			SetMode( EditMode.Edge );
			Assert.That( _tool.SelectedEdges.Count(), Is.EqualTo( 4 ) );
		}
	}
}

namespace HammerUnity.Tests
{
	public class HammerSubToolTests
	{
		GameObject _go;
		HammerMeshTool _tool;

		[SetUp]
		public void SetUp()
		{
			_tool = ScriptableObject.CreateInstance<HammerMeshTool>();
			_go = new GameObject( "Box" );
			var c = _go.AddComponent<HammerMesh>();
			c.Mesh = new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -32 ), new S.Vector3( 32 ) ) );
		}

		[TearDown]
		public void TearDown()
		{
			Object.DestroyImmediate( _go );
			Object.DestroyImmediate( _tool );
		}

		[Test]
		public void ClipBoxInHalf()
		{
			var c = _go.GetComponent<HammerMesh>();
			ClipTool.Clip( c, c.Mesh, Vector3.zero, Vector3.right, ClipTool.KeepMode.Back, null, null );
			c.Commit();

			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 6 ) );
			var bounds = c.RenderMesh.bounds;
			Assert.That( bounds.size.x, Is.EqualTo( 32 * SourceSpace.UnitScale ).Within( 1e-4f ) );
		}

		[Test]
		public void EdgeCutAcrossTopFace()
		{
			var c = _go.GetComponent<HammerMesh>();
			var mesh = c.Mesh;
			var top = mesh.FaceHandles.First( h => { mesh.ComputeFaceNormal( h, out var n ); return n.z > 0.9f; } );
			var face = new MeshFace( c, top );

			// Two opposite edges of the top face, cut through their midpoints
			var edges = mesh.GetFaceEdges( top );
			var e0 = new MeshEdge( c, edges[0] );
			var e2 = new MeshEdge( c, edges[2] );
			e0.GetWorldPoints( out var a0, out var b0 );
			e2.GetWorldPoints( out var a2, out var b2 );

			var cut = new EdgeCutTool();
			_tool.BeginSubTool( cut );
			cut.AddEdgePoint( face, e0, (a0 + b0) * 0.5f );
			cut.AddEdgePoint( face, e2, (a2 + b2) * 0.5f );
			cut.Apply();

			Assert.That( mesh.FaceHandles.Count(), Is.EqualTo( 7 ) );
			Assert.That( _tool.SubTool, Is.Null );
		}
	}
}

namespace HammerUnity.Tests
{
	public class HammerClipboardAndBooleanTests
	{
		readonly List<GameObject> _objects = new();
		HammerMeshTool _tool;

		[SetUp]
		public void SetUp() => _tool = ScriptableObject.CreateInstance<HammerMeshTool>();

		[TearDown]
		public void TearDown()
		{
			foreach ( var go in _objects.Where( x => x != null ) ) Object.DestroyImmediate( go );
			_objects.Clear();
			Object.DestroyImmediate( _tool );
			Selection.objects = new Object[0];
		}

		HammerMesh Box( Vector3 position, float size )
		{
			var go = new GameObject( "Box" );
			_objects.Add( go );
			go.transform.position = position;
			var c = go.AddComponent<HammerMesh>();
			c.Mesh = new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -size / 2 ), new S.Vector3( size / 2 ) ) );
			return c;
		}

		[Test]
		public void CopyPasteFacesIntoAnotherMesh()
		{
			var a = Box( Vector3.zero, 64 );
			var b = Box( new Vector3( 3, 0, 0 ), 32 );
			_tool.Mode = EditMode.Face;

			foreach ( var f in a.Mesh.FaceHandles.Take( 2 ) )
				_tool.Selection.Add( new MeshFace( a, f ) );

			_tool.CopyFaces();

			_tool.Selection.Clear();
			_tool.Selection.Add( new MeshFace( b, b.Mesh.FaceHandles.First() ) );
			_tool.PasteFaces();

			Assert.That( b.Mesh.FaceHandles.Count(), Is.EqualTo( 8 ) );

			// Pasted in place: b's bounds now reach a's extent in world space
			var worldBounds = b.GetComponent<MeshRenderer>().bounds;
			Assert.That( worldBounds.min.x, Is.LessThan( 0 ) );
		}

		// Cutters people actually draw: through the wall, exactly as thick as the wall (coplanar
		// faces), only part way in, touching the top edge, moved/rotated objects
		[TestCase( 64f, 0f, 0f, 0f, TestName = "Subtract: cutter through the wall" )]
		[TestCase( 16f, 0f, 0f, 0f, TestName = "Subtract: cutter exactly as thick as the wall" )]
		[TestCase( 64f, 0f, 64f, 0f, TestName = "Subtract: notch out of the top edge" )]
		[TestCase( 64f, 0f, 0f, 30f, TestName = "Subtract: rotated wall" )]
		public void SubtractCases( float cutterThickness, float offsetY, float offsetZ, float wallYaw )
		{
			var wallGo = new GameObject( "Wall" );
			wallGo.transform.position = new Vector3( 3, 1, -2 );
			wallGo.transform.rotation = Quaternion.Euler( 0, wallYaw, 0 );
			var wall = wallGo.AddComponent<HammerMesh>();
			wall.Mesh = new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -8, -128, -96 ), new S.Vector3( 8, 128, 96 ) ) );

			var cutGo = new GameObject( "Cutter" );
			cutGo.transform.SetPositionAndRotation( wallGo.transform.TransformPoint( SourceSpace.ToUnityPosition( new S.Vector3( 0, offsetY, offsetZ ) ) ), wallGo.transform.rotation );
			var cut = cutGo.AddComponent<HammerMesh>();
			cut.Mesh = new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -cutterThickness / 2, -48, -32 ), new S.Vector3( cutterThickness / 2, 48, 32 ) ) );
			var holeCenter = cutGo.transform.position + wallGo.transform.up * (offsetZ > 0 ? -0.2f : 0);

			try
			{
				_tool.Mode = EditMode.Object;
				Selection.objects = new Object[] { wallGo, cutGo };
				Selection.activeGameObject = wallGo;
				_tool.Boolean( S.PolygonMesh.BooleanOperation.Subtract );

				var through = wallGo.transform.forward;
				Assert.That( MeshPicking.RaycastFace( new Ray( holeCenter - through * 2, through ), wall, out var h ), Is.False, $"front blocked at {h.Point}" );
				Assert.That( MeshPicking.RaycastFace( new Ray( holeCenter + through * 2, -through ), wall, out h ), Is.False, $"back blocked at {h.Point}" );
				Assert.That( wall.Mesh.FindBadFaces( includeNonPlanar: false ), Is.Empty, "no broken faces" );
			}
			finally
			{
				Object.DestroyImmediate( wallGo );
				if ( cutGo != null ) Object.DestroyImmediate( cutGo );
			}
		}

		[Test]
		public void SubtractThroughAWallMakesAHole()
		{
			var wallGo = new GameObject( "Wall" );
			var wall = wallGo.AddComponent<HammerMesh>();
			wall.Mesh = new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -8, -128, -96 ), new S.Vector3( 8, 128, 96 ) ) );
			var cutGo = new GameObject( "Cutter" );
			var cut = cutGo.AddComponent<HammerMesh>();
			cut.Mesh = new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -32, -48, -32 ), new S.Vector3( 32, 48, 32 ) ) );
			try
			{
				_tool.Mode = EditMode.Object;
				Selection.objects = new Object[] { wallGo, cutGo };
				Selection.activeGameObject = wallGo;
				_tool.Boolean( S.PolygonMesh.BooleanOperation.Subtract );

				var mesh = wall.Mesh;
				// A ray straight through the middle (along s&box X = Unity Z) must not hit the wall
				var hit = MeshPicking.RaycastFace( new Ray( new Vector3( 0, 0, -2 ), Vector3.forward ), wall, out var h );
				Assert.That( hit, Is.False, $"hit at {(hit ? h.Point.ToString() : "")}" );
				hit = MeshPicking.RaycastFace( new Ray( new Vector3( 0, 0, 2 ), Vector3.back ), wall, out h );
				Assert.That( hit, Is.False, "from the back" );
				// And the wall is still there beside the hole
				Assert.That( MeshPicking.RaycastFace( new Ray( new Vector3( 2, 0, -2 ), Vector3.forward ), wall, out _ ), Is.True );
			}
			finally
			{
				Object.DestroyImmediate( wallGo );
				if ( cutGo != null ) Object.DestroyImmediate( cutGo );
			}
		}

		[Test]
		public void SubtractSmallBoxFromBigBox()
		{
			var big = Box( Vector3.zero, 64 );
			var small = Box( new Vector3( 32 * SourceSpace.UnitScale, 0, 0 ), 32 );
			_tool.Mode = EditMode.Object;

			Selection.objects = new Object[] { big.gameObject, small.gameObject };
			Selection.activeGameObject = big.gameObject;
			_tool.Boolean( S.PolygonMesh.BooleanOperation.Subtract );

			Assert.That( small == null, Is.True );
			Assert.That( big.Mesh.FaceHandles.Count(), Is.GreaterThan( 6 ) );
			big.Mesh.Rebuild();
			Assert.That( big.Mesh.BadFaces.Count, Is.EqualTo( 0 ) );
		}
	}
}

namespace HammerUnity.Tests
{
	public class HammerPaintTests
	{
		[Test]
		public void FloodFillColoursEveryVertex()
		{
			var tool = ScriptableObject.CreateInstance<HammerMeshTool>();
			var go = new GameObject( "Box" );
			try
			{
				var c = go.AddComponent<HammerMesh>();
				c.Mesh = new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -32 ), new S.Vector3( 32 ) ) );
				Selection.activeGameObject = go;

				HammerMeshTool.PaintMode = PaintMode.Color;
				HammerMeshTool.PaintColor = Color.red;
				tool.FloodPaint( false );

				var colors = c.RenderMesh.colors32;
				Assert.That( colors.Length, Is.EqualTo( 24 ) );
				Assert.That( colors.All( x => x.r == 255 && x.g == 0 && x.b == 0 ), Is.True );

				// Survives serialization
				var copy = S.PolygonMesh.FromData( c.Mesh.ToData() );
				Assert.That( copy.GetVertexColor( copy.HalfEdgeHandles.First( h => copy.GetHalfEdgeFace( h ).IsValid ) ).g, Is.EqualTo( 0 ) );
			}
			finally
			{
				Object.DestroyImmediate( go );
				Object.DestroyImmediate( tool );
				Selection.objects = new Object[0];
			}
		}

		[Test]
		public void VertexBlendShaderCompiles()
		{
			var shader = Shader.Find( "Hammer/Vertex Blend" );
			Assert.That( shader, Is.Not.Null );
			Assert.That( ShaderUtil.ShaderHasError( shader ), Is.False );
		}
	}
}

namespace HammerUnity.Tests
{
	public class HammerBridgeTests
	{
		[Test]
		public void BridgeTopAndBottomFacesMakesTunnel()
		{
			var tool = ScriptableObject.CreateInstance<HammerMeshTool>();
			var go = new GameObject( "Box" );
			try
			{
				var c = go.AddComponent<HammerMesh>();
				c.Mesh = new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -32 ), new S.Vector3( 32 ) ) );
				tool.Mode = EditMode.Face;

				foreach ( var f in c.Mesh.FaceHandles )
				{
					c.Mesh.ComputeFaceNormal( f, out var n );
					if ( Mathf.Abs( n.z ) > 0.9f ) tool.Selection.Add( new MeshFace( c, f ) );
				}

				BridgeTool.Open( tool );
				Assert.That( tool.SubTool, Is.InstanceOf<BridgeTool>() );
				tool.SubTool.Apply();

				Assert.That( tool.SubTool, Is.Null );
				Assert.That( c.Mesh.FaceHandles.Count(), Is.GreaterThan( 4 ) );
				c.Mesh.Rebuild();
				Assert.That( c.Mesh.BadFaces.Count, Is.EqualTo( 0 ) );
				Assert.That( c.Mesh.HalfEdgeHandles.Any( h => c.Mesh.IsEdgeOpen( h ) ), Is.False, "tunnel is closed" );
			}
			finally
			{
				Object.DestroyImmediate( go );
				Object.DestroyImmediate( tool );
			}
		}
	}
}

namespace HammerUnity.Tests
{
	public class HammerArchAndPathTests
	{
		readonly List<GameObject> _objects = new();
		HammerMeshTool _tool;

		[SetUp]
		public void SetUp() => _tool = ScriptableObject.CreateInstance<HammerMeshTool>();

		[TearDown]
		public void TearDown()
		{
			foreach ( var go in _objects.Where( x => x != null ) ) Object.DestroyImmediate( go );
			foreach ( var go in Selection.gameObjects.Where( x => x != null && x.GetComponent<HammerMesh>() != null ) ) Object.DestroyImmediate( go );
			_objects.Clear();
			Object.DestroyImmediate( _tool );
		}

		HammerMesh Make( PrimitiveBuilder builder, S.BBox box )
		{
			var go = new GameObject( builder.GetType().Name );
			_objects.Add( go );
			var c = go.AddComponent<HammerMesh>();
			c.Mesh = builder.CreateMesh( box );
			return c;
		}

		[Test]
		public void ArchOpenEdge()
		{
			var quad = Make( new QuadPrimitive(), new S.BBox( new S.Vector3( -32, -32, 0 ), new S.Vector3( 32, 32, 0 ) ) );
			_tool.Mode = EditMode.Edge;
			_tool.Selection.Add( new MeshEdge( quad, quad.Mesh.HalfEdgeHandles.First( h => quad.Mesh.IsEdgeOpen( h ) ) ) );

			EdgeArchTool.Open( _tool );
			Assert.That( _tool.SubTool, Is.InstanceOf<EdgeArchTool>() );
			_tool.SubTool.Apply();

			Assert.That( quad.Mesh.VertexHandles.Count(), Is.GreaterThan( 4 ) );
			Assert.That( _tool.Selection.Count, Is.GreaterThan( 1 ) );
		}

		[Test]
		public void SweepSquareAlongEdge()
		{
			var profile = Make( new QuadPrimitive(), new S.BBox( new S.Vector3( -8, -8, 0 ), new S.Vector3( 8, 8, 0 ) ) );
			var path = Make( new BlockPrimitive(), new S.BBox( new S.Vector3( -32 ), new S.Vector3( 32 ) ) );
			_tool.Mode = EditMode.Edge;

			foreach ( var h in profile.Mesh.HalfEdgeHandles.Where( h => profile.Mesh.GetHalfEdgeFace( h ).IsValid ) )
				_tool.Selection.Add( new MeshEdge( profile, h ) );
			_tool.Selection.Add( new MeshEdge( path, path.Mesh.HalfEdgeHandles.First() ) );

			PathExtrudeTool.Open( _tool );
			Assert.That( _tool.SubTool, Is.InstanceOf<PathExtrudeTool>() );
			_tool.SubTool.Apply();

			var created = Selection.activeGameObject?.GetComponent<HammerMesh>();
			Assert.That( created, Is.Not.Null );
			Assert.That( created, Is.Not.EqualTo( profile ) );
			Assert.That( created.Mesh.FaceHandles.Count(), Is.EqualTo( 4 ) );
		}
	}
}

namespace HammerUnity.Tests
{
	public class HammerInteractiveToolTests
	{
		[Test]
		public void InsetToolAndBevelToolApply()
		{
			var tool = ScriptableObject.CreateInstance<HammerMeshTool>();
			var go = new GameObject( "Box" );
			try
			{
				var c = go.AddComponent<HammerMesh>();
				c.Mesh = new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -32 ), new S.Vector3( 32 ) ) );

				tool.Mode = EditMode.Face;
				tool.Selection.Add( new MeshFace( c, c.Mesh.FaceHandles.First() ) );
				InsetTool.Open( tool );
				tool.SubTool.Apply();
				Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 10 ) );

				tool.Mode = EditMode.Edge;
				tool.Selection.Clear();
				tool.Selection.Add( new MeshEdge( c, c.Mesh.HalfEdgeHandles.First() ) );
				BevelTool.Open( tool );
				tool.SubTool.Apply();
				Assert.That( c.Mesh.FaceHandles.Count(), Is.GreaterThan( 10 ) );

				c.Mesh.Rebuild();
				Assert.That( c.Mesh.BadFaces.Count, Is.EqualTo( 0 ) );

				// Cancelling restores the mesh
				var before = c.Mesh.FaceHandles.Count();
				tool.Mode = EditMode.Face;
				tool.Selection.Add( new MeshFace( c, c.Mesh.FaceHandles.First() ) );
				InsetTool.Open( tool );
				tool.SubTool.Cancel();
				Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( before ) );
			}
			finally
			{
				Object.DestroyImmediate( go );
				Object.DestroyImmediate( tool );
			}
		}
	}
}

namespace HammerUnity.Tests
{
	public class HammerShortcutTests
	{
		[Test]
		public void HammerKeysAreRegisteredForTheWindow()
		{
			var manager = UnityEditor.ShortcutManagement.ShortcutManager.instance;
			Assert.That( manager.GetAvailableShortcutIds(), Does.Contain( "Hammer/Edge Mode" ) );
			Assert.That( manager.GetShortcutBinding( "Hammer/Edge Mode" ).ToString(), Does.Contain( "2" ) );
		}

	}
}
