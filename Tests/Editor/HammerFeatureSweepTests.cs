using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
	/// Every major feature, one after another, the way a user would chain them: after each step the
	/// mesh must be sound (no folded, zero-size or bent many-sided faces, nothing non-finite), a
	/// closed shape must stay closed where the operation should keep it closed, the rendered mesh
	/// must match, and undo then redo must give exactly the shapes before and after.
	/// </summary>
	public class HammerFeatureSweepTests
	{
		HammerMeshTool _tool;
		readonly List<GameObject> _objects = new();
		float _grid;
		int _segments;
		Vector2Int _cuts;

		[SetUp]
		public void SetUp()
		{
			_grid = HammerSettings.GridSize;
			_segments = HammerSettings.BevelSegments;
			_cuts = HammerSettings.QuadSliceCuts;
			HammerSettings.GridSize = 8;
			HammerSettings.BevelSegments = 1;
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
			HammerSettings.GridSize = _grid;
			HammerSettings.BevelSegments = _segments;
			HammerSettings.QuadSliceCuts = _cuts;
		}

		// ── Building blocks ──

		HammerMesh Make( PrimitiveBuilder builder, S.Vector3 size, Vector3 position = default, string name = "Sweep" )
		{
			var go = new GameObject( name );
			_objects.Add( go );
			go.transform.position = position;
			var c = go.AddComponent<HammerMesh>();
			c.Mesh = builder.CreateMesh( new S.BBox( -size / 2, size / 2 ) );
			Undo.RegisterCreatedObjectUndo( go, "Create" );
			EndFrame();
			return c;
		}

		HammerMesh Box( float size = 128, Vector3 position = default ) => Make( new BlockPrimitive(), new S.Vector3( size ), position );

		static void EndFrame()
		{
			Undo.FlushUndoRecordObjects();
			Undo.IncrementCurrentGroup();
		}

		static string Shape( HammerMesh c )
		{
			c.RebuildIfReloaded();
			var mesh = c.Mesh;
			var verts = mesh.VertexHandles.Select( v => mesh.GetVertexPosition( v ) ).Select( p => $"{p.x:0.##},{p.y:0.##},{p.z:0.##}" ).OrderBy( x => x );
			// Corner count and facing, so flips count as changes
			var faces = mesh.FaceHandles.Select( f => { mesh.ComputeFaceNormal( f, out var n ); var m = mesh.GetFaceCenter( f ); return $"{mesh.GetFaceVertices( f ).Length}:{n.x:0.#},{n.y:0.#},{n.z:0.#}@{m.x:0},{m.y:0},{m.z:0}{(mesh.IsFaceHidden( f ) ? "h" : "")}"; } ).OrderBy( x => x );
			return $"{mesh.FaceHandles.Count()}f [{string.Join( " ", faces )}] {string.Join( " ", verts )}";
		}

		static string Shapes( IEnumerable<HammerMesh> components ) => string.Join( " | ", components.Where( c => c != null ).OrderBy( c => c.GetInstanceID() ).Select( Shape ) );

		static int OpenEdges( HammerMesh c ) => c.Mesh.HalfEdgeHandles.Count( h => c.Mesh.IsEdgeOpen( h ) );

		static void AssertSound( HammerMesh c, string step )
		{
			var mesh = c.Mesh;
			Assert.That( mesh.FaceHandles.Count(), Is.GreaterThan( 0 ), $"{step}: no faces left" );

			foreach ( var v in mesh.VertexHandles )
			{
				var p = mesh.GetVertexPosition( v );
				Assert.That( float.IsFinite( p.x ) && float.IsFinite( p.y ) && float.IsFinite( p.z ), $"{step}: a vertex went non-finite" );
			}

			// Folded, zero-size or untriangulatable faces are broken. Bent faces aren't (the user
			// can bend them on purpose by dragging): the health panel offers to flatten those
			var bad = mesh.FindBadFaces( includeNonPlanar: false );
			Assert.That( bad, Is.Empty, $"{step}: broken faces ({string.Join( ", ", bad.Values )})" );

			var unity = c.GetComponent<MeshFilter>().sharedMesh;
			Assert.That( unity != null && unity.triangles.Length >= 3, $"{step}: the rendered mesh is empty" );
			Assert.That( unity.vertices.All( v => float.IsFinite( v.x ) && float.IsFinite( v.y ) && float.IsFinite( v.z ) ), $"{step}: rendered vertices non-finite" );
			AssertRenderMatches( c, step );
		}

		/// <summary>
		/// The rendered triangles cover the same area as the visible faces.
		/// </summary>
		static void AssertRenderMatches( HammerMesh c, string step )
		{
			c.RebuildIfReloaded();
			var mesh = c.Mesh;

			// Bent faces triangulate to a slightly different area than their outline: only flat
			// meshes can be compared exactly
			if ( mesh.FaceHandles.Any( f => (mesh.CheckFace( f ) & S.PolygonMesh.FaceProblem.NonPlanar) != 0 ) )
			{
				Assert.That( c.GetComponent<MeshFilter>().sharedMesh.triangles.Length, Is.GreaterThan( 0 ), $"{step}: nothing rendered" );
				return;
			}

			var faceArea = 0.0;
			foreach ( var f in mesh.FaceHandles )
			{
				if ( mesh.IsFaceHidden( f ) ) continue;
				var p = mesh.GetFaceVertices( f ).Select( v => mesh.GetVertexPosition( v ) ).ToArray();
				var n = S.Vector3.Zero;
				for ( int i = 0; i < p.Length; i++ ) n += S.Vector3.Cross( p[i], p[(i + 1) % p.Length] );
				faceArea += n.Length * 0.5;
			}

			var unity = c.GetComponent<MeshFilter>().sharedMesh;
			var v3 = unity.vertices;
			var t = unity.triangles;
			var triArea = 0.0;
			for ( int i = 0; i + 2 < t.Length; i += 3 )
				triArea += Vector3.Cross( v3[t[i + 1]] - v3[t[i]], v3[t[i + 2]] - v3[t[i]] ).magnitude * 0.5;
			triArea /= SourceSpace.UnitScale * SourceSpace.UnitScale;

			Assert.That( triArea, Is.EqualTo( faceArea ).Within( Math.Max( 1, faceArea * 0.01 ) ), $"{step}: the rendered mesh doesn't cover the faces" );
		}

		/// <summary>
		/// Run one step and check it: it changed something, everything is sound, closed shapes
		/// stayed closed (if <paramref name="keepsClosed"/>), and undo/redo are exact.
		/// </summary>
		/// <param name="mayRefuse">The operation may decline (a bevel too wide to fit): then nothing
		/// may change, and the user must have been told why.</param>
		void Step( string name, IList<HammerMesh> meshes, Action action, bool keepsClosed = true, bool mayRefuse = false )
		{
			var before = Shapes( meshes );
			var wasClosed = meshes.ToDictionary( c => c, c => OpenEdges( c ) == 0 );

			action();
			EndFrame();

			var alive = meshes.Where( c => c != null ).ToList();
			var after = Shapes( alive );
			if ( mayRefuse && after == before )
			{
				Assert.That( MeshHealth.Warning, Does.Contain( "doesn't fit" ), $"{name}: refused without saying why" );
				_log.Add( $"refused {name}: {MeshHealth.Warning}" );
				return;
			}
			Assert.That( after, Is.Not.EqualTo( before ), $"{name}: changed nothing" );

			foreach ( var c in alive )
			{
				AssertSound( c, name );
				if ( keepsClosed && wasClosed.TryGetValue( c, out var closed ) && closed )
					Assert.That( OpenEdges( c ), Is.EqualTo( 0 ), $"{name}: left holes in a closed mesh" );
			}

			// Undo (selection changes are steps of their own, so keep going until the mesh changes)
			for ( int i = 0; i < 6 && Shapes( alive ) == after; i++ ) Undo.PerformUndo();
			Assert.That( Shapes( alive ), Is.EqualTo( before ), $"{name}: undo didn't give back the shape before" );
			foreach ( var c in alive ) AssertRenderMatches( c, $"{name} (undone)" );

			for ( int i = 0; i < 6 && Shapes( alive ) == before; i++ ) Undo.PerformRedo();
			Assert.That( Shapes( alive ), Is.EqualTo( after ), $"{name}: redo didn't give back the shape after" );
			foreach ( var c in alive ) AssertRenderMatches( c, $"{name} (redone)" );

			_log.Add( $"ok  {name}: {after.Split( ' ' )[0]}" );
		}

		readonly List<string> _log = new();

		void Select( EditMode mode, IEnumerable<IMeshElement> elements )
		{
			_tool.Mode = mode;
			_tool.Selection.Clear();
			foreach ( var e in elements ) _tool.Selection.Add( e );
			EndFrame();
		}

		void SelectObjects( params HammerMesh[] meshes )
		{
			_tool.Mode = EditMode.Object;
			UnityEditor.Selection.objects = meshes.Select( m => (Object)m.gameObject ).ToArray();
			UnityEditor.Selection.activeGameObject = meshes[0].gameObject;
			EndFrame();
		}

		static IEnumerable<IMeshElement> Faces( HammerMesh c, Func<S.Vector3, S.Vector3, bool> where ) =>
			c.Mesh.FaceHandles.Where( f => { c.Mesh.ComputeFaceNormal( f, out var n ); return where( n, c.Mesh.GetFaceCenter( f ) ); } ).Select( f => (IMeshElement)new MeshFace( c, f ) ).ToList();

		static IEnumerable<IMeshElement> FacingFaces( HammerMesh c, S.Vector3 dir ) => Faces( c, ( n, _ ) => S.Vector3.Dot( n, dir ) > 0.99f );

		static IMeshElement TopFace( HammerMesh c ) => c.Mesh.FaceHandles.OrderByDescending( f => c.Mesh.GetFaceCenter( f ).z ).Select( f => (IMeshElement)new MeshFace( c, f ) ).First();

		static IEnumerable<IMeshElement> Edges( HammerMesh c, Func<S.Vector3, S.Vector3, bool> where ) =>
			c.Mesh.HalfEdgeHandles.Where( h => { var l = c.Mesh.GetEdgeLine( h ); return where( l.Start, l.End ); } ).Select( h => (IMeshElement)new MeshEdge( c, h ) ).ToList();

		static float Top( HammerMesh c ) => c.Mesh.VertexHandles.Max( v => c.Mesh.GetVertexPosition( v ).z );

		static IEnumerable<IMeshElement> TopEdges( HammerMesh c )
		{
			var top = Top( c );
			return Edges( c, ( a, b ) => Mathf.Abs( a.z - top ) < 0.01f && Mathf.Abs( b.z - top ) < 0.01f );
		}

		static IEnumerable<IMeshElement> UprightEdges( HammerMesh c ) =>
			Edges( c, ( a, b ) => Mathf.Abs( a.x - b.x ) < 0.01f && Mathf.Abs( a.y - b.y ) < 0.01f );

		static IMeshElement TopCorner( HammerMesh c, int x = 1, int y = 1 ) =>
			new MeshVertex( c, c.Mesh.VertexHandles.OrderByDescending( v => { var p = c.Mesh.GetVertexPosition( v ); return p.z * 1000 + p.x * x + p.y * y; } ).First() );

		static readonly BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
		void Call( string method, params object[] args ) => typeof( HammerMeshTool ).GetMethod( method, Any ).Invoke( _tool, args );

		/// <summary>
		/// A gizmo drag as the tool does it, over several editor frames.
		/// </summary>
		void Drag( bool extrude, Vector3 delta )
		{
			typeof( HammerMeshTool ).GetField( "_handlePosition", Any ).SetValue( _tool, _tool.SelectionCenter() );
			Call( "BeginTransform", extrude );
			Undo.FlushUndoRecordObjects();
			var comps = _tool.Selection.Components.ToList();
			for ( int i = 1; i <= 4; i++ )
			{
				Call( "ApplyTranslate", delta * (i / 4.0f) );
				HammerMeshTool.RebuildNow( comps );
				Undo.FlushUndoRecordObjects();
			}
			Call( "EndTransform" );
		}

		void Rotate( float degrees, bool extrude = false, Vector3? around = null, Vector3? axis = null )
		{
			var pivot = around ?? _tool.SelectionCenter();
			typeof( HammerMeshTool ).GetField( "_handlePosition", Any ).SetValue( _tool, pivot );
			Call( "BeginTransform", extrude );
			Undo.FlushUndoRecordObjects();
			var comps = _tool.Selection.Components.ToList();
			for ( int i = 1; i <= 3; i++ )
			{
				Call( "ApplyRotate", pivot, Quaternion.AngleAxis( degrees * i / 3, axis ?? Vector3.up ) );
				HammerMeshTool.RebuildNow( comps );
				Undo.FlushUndoRecordObjects();
			}
			Call( "EndTransform" );
		}

		void Scale( Vector3 scale, bool extrude = false )
		{
			var pivot = _tool.SelectionCenter();
			typeof( HammerMeshTool ).GetField( "_handlePosition", Any ).SetValue( _tool, pivot );
			Call( "BeginTransform", extrude );
			Undo.FlushUndoRecordObjects();
			var comps = _tool.Selection.Components.ToList();
			Call( "ApplyScale", pivot, Quaternion.identity, scale );
			HammerMeshTool.RebuildNow( comps );
			Call( "EndTransform" );
		}

		static float Unit( float inches ) => inches * SourceSpace.UnitScale;

		// ── Faces ──

		[Test]
		public void FaceFeatures()
		{
			var c = Box();
			var m = new[] { c };

			Step( "inset top", m, () => { Select( EditMode.Face, new[] { TopFace( c ) } ); HammerSettings.GridSize = 16; _tool.InsetFaces(); HammerSettings.GridSize = 8; } );
			Step( "extrude top", m, () => _tool.ExtrudeFaces() );
			Step( "extrude again", m, () => _tool.ExtrudeFaces() );
			Step( "drag extrude (Shift+drag)", m, () => Drag( true, Vector3.up * Unit( 24 ) ), keepsClosed: true );
			Step( "drag face sideways", m, () => Drag( false, Vector3.right * Unit( 16 ) ) );
			Step( "rotate face", m, () => Rotate( 20 ) );
			Step( "scale face", m, () => Scale( new Vector3( 0.6f, 1, 0.6f ) ) );
			Step( "inset twice", m, () => { _tool.InsetFaces(); } );
			Step( "extrude inward", m, () => Drag( true, Vector3.down * Unit( 16 ) ) );

			Step( "quad slice side 3x3", m, () => { Select( EditMode.Face, FacingFaces( c, new S.Vector3( 1, 0, 0 ) ) ); HammerSettings.QuadSliceCuts = new Vector2Int( 2, 2 ); _tool.QuadSlice(); } );
			Step( "extrude a sliced face", m, () =>
			{
				var middle = FacingFaces( c, new S.Vector3( 1, 0, 0 ) ).Cast<MeshFace>().OrderBy( f => (c.Mesh.GetFaceCenter( f.Handle ) - c.Mesh.CalculateBounds().Center).Length ).First();
				Select( EditMode.Face, new IMeshElement[] { middle } );
				Drag( true, Vector3.forward * Unit( 32 ) );
			} );
			Step( "combine sliced faces", m, () =>
			{
				var row = FacingFaces( c, new S.Vector3( 1, 0, 0 ) ).Cast<MeshFace>().Where( f => c.Mesh.GetFaceCenter( f.Handle ).z < -20 ).Cast<IMeshElement>().ToList();
				Select( EditMode.Face, row );
				_tool.CombineFaces();
			} );
			Step( "bevel around the extruded face", m, () =>
			{
				Select( EditMode.Face, FacingFaces( c, new S.Vector3( 0, 0, 1 ) ) );
				_tool.SwitchMode( EditMode.Edge, SelectionConversion.Boundary );
				HammerSettings.GridSize = 2; _tool.QuickBevelEdges(); HammerSettings.GridSize = 8;
			}, mayRefuse: true );
			Step( "collapse a face", m, () => { Select( EditMode.Face, FacingFaces( c, new S.Vector3( -1, 0, 0 ) ).Take( 1 ) ); _tool.Collapse(); } );
			Step( "flip all", m, () => { Select( EditMode.Face, c.Mesh.FaceHandles.Select( f => (IMeshElement)new MeshFace( c, f ) ) ); _tool.FlipFaces(); } );
			Step( "flip back", m, () => _tool.FlipFaces() );
			Step( "subdivide a face", m, () => { Select( EditMode.Face, FacingFaces( c, new S.Vector3( 0, -1, 0 ) ).Take( 1 ) ); _tool.Subdivide(); } );
			Step( "snap to grid", m, () => { Select( EditMode.Face, c.Mesh.FaceHandles.Select( f => (IMeshElement)new MeshFace( c, f ) ) ); HammerSettings.GridSize = 1; _tool.SnapToGrid(); HammerSettings.GridSize = 8; } );
			Step( "delete a face", m, () => { Select( EditMode.Face, FacingFaces( c, new S.Vector3( 0, 0, -1 ) ).Take( 1 ) ); _tool.Delete(); }, keepsClosed: false );
			Step( "fill the hole", m, () =>
			{
				Select( EditMode.Edge, c.Mesh.HalfEdgeHandles.Where( h => c.Mesh.IsEdgeOpen( h ) ).Take( 1 ).Select( h => (IMeshElement)new MeshEdge( c, h ) ) );
				_tool.FillHole();
				Assert.That( OpenEdges( c ), Is.Zero, "filled" );
			}, keepsClosed: false );
			// Hiding is a view setting (not saved, not an undo step, like s&box): it only changes
			// what's drawn
			var tris = c.GetComponent<MeshFilter>().sharedMesh.triangles.Length;
			Select( EditMode.Face, new[] { TopFace( c ) } );
			_tool.HideFaces();
			Assert.That( c.GetComponent<MeshFilter>().sharedMesh.triangles.Length, Is.LessThan( tris ), "hidden face not drawn" );
			AssertSound( c, "hide" );
			_tool.UnhideFaces();
			Assert.That( c.GetComponent<MeshFilter>().sharedMesh.triangles.Length, Is.EqualTo( tris ), "unhide brings it back" );
		}

		[Test]
		public void FaceThickenDetachExtract()
		{
			var quad = Make( new QuadPrimitive(), new S.Vector3( 128, 128, 0 ) );
			Step( "thicken quad", new[] { quad }, () => { Select( EditMode.Face, quad.Mesh.FaceHandles.Select( f => (IMeshElement)new MeshFace( quad, f ) ) ); HammerSettings.GridSize = 16; _tool.ThickenFaces(); HammerSettings.GridSize = 8; }, keepsClosed: false );
			Assert.That( OpenEdges( quad ), Is.Zero, "thickened quad is closed" );

			var box = Box( 128, Vector3.right * 5 );
			Step( "detach top", new[] { box }, () => { Select( EditMode.Face, new[] { TopFace( box ) } ); _tool.DetachFaces(); }, keepsClosed: false );

			var before = _objects.Count( o => o != null );
			var box2 = Box( 128, Vector3.right * 10 );
			Select( EditMode.Face, new[] { TopFace( box2 ) } );
			_tool.ExtractFaces();
			EndFrame();
			Assert.That( box2.Mesh.FaceHandles.Count(), Is.EqualTo( 5 ) );
			Assert.That( UnityEditor.Selection.gameObjects.Length, Is.EqualTo( 1 ) );
			var extracted = UnityEditor.Selection.activeGameObject.GetComponent<HammerMesh>();
			_objects.Add( extracted.gameObject );
			AssertSound( extracted, "extract" );
			Assert.That( extracted.Mesh.FaceHandles.Count(), Is.EqualTo( 1 ) );
		}

		// ── Edges ──

		[Test]
		public void EdgeFeatures()
		{
			var c = Box();
			var m = new[] { c };

			Step( "bevel top edges (1 segment)", m, () => { Select( EditMode.Edge, TopEdges( c ) ); HammerSettings.GridSize = 16; _tool.QuickBevelEdges(); HammerSettings.GridSize = 8; } );
			Step( "bevel upright edges (4 segments)", m, () => { Select( EditMode.Edge, UprightEdges( c ) ); HammerSettings.BevelSegments = 4; HammerSettings.GridSize = 8; _tool.QuickBevelEdges(); HammerSettings.BevelSegments = 1; } );
			Step( "connect a ring", m, () =>
			{
				var b = Box( 128, Vector3.left * 5 );
				_ = b;
				Select( EditMode.Edge, UprightEdges( c ).Take( 1 ) );
				_tool.SelectRing();
				_tool.ConnectEdges();
			} );
			Step( "select loop and drag it up", m, () =>
			{
				_tool.SelectLoop();
				Drag( false, Vector3.up * Unit( 8 ) );
			} );
			Step( "dissolve the loop", m, () => _tool.DissolveEdges() );
			Step( "collapse an edge", m, () => { Select( EditMode.Edge, TopEdges( c ).Take( 1 ) ); _tool.Collapse(); } );
			Step( "drag an edge", m, () => { Select( EditMode.Edge, TopEdges( c ).Take( 1 ) ); Drag( false, Vector3.up * Unit( 16 ) ); } );
		}

		[Test]
		public void BevelTwiceAndThreeTimes()
		{
			foreach ( var segments in new[] { 1, 2 } )
			{
				var c = Box( 128, Vector3.right * segments * 5 );
				var m = new[] { c };
				HammerSettings.BevelSegments = segments;
				Step( $"bevel top edges x{segments}", m, () => { Select( EditMode.Edge, TopEdges( c ) ); HammerSettings.GridSize = 16; _tool.QuickBevelEdges(); } );
				Step( $"bevel the same selection again x{segments}", m, () => { HammerSettings.GridSize = 4; _tool.QuickBevelEdges(); }, mayRefuse: true );
				Step( $"bevel the new top edges x{segments}", m, () => { Select( EditMode.Edge, TopEdges( c ) ); HammerSettings.GridSize = 2; _tool.QuickBevelEdges(); }, mayRefuse: true );
				HammerSettings.GridSize = 8;
			}
			HammerSettings.BevelSegments = 1;
		}

		[Test]
		public void BevelACutEdge()
		{
			// A wall; select the top and bottom edges of its front, Connect (V) to cut down the
			// middle, then Bevel (F) that new edge: two edges either side of the cut
			var c = Make( new BlockPrimitive(), new S.Vector3( 16, 512, 512 ) );
			var front = FacingFaces( c, new S.Vector3( -1, 0, 0 ) ).Cast<MeshFace>().First();
			var edges = c.Mesh.GetFaceEdges( front.Handle ).Where( h => { var l = c.Mesh.GetEdgeLine( h ); return Mathf.Abs( l.Start.z - l.End.z ) < 0.01f; } ).Select( h => (IMeshElement)new MeshEdge( c, h ) ).ToList();
			Assert.That( edges.Count, Is.EqualTo( 2 ) );
			Select( EditMode.Edge, edges );
			_tool.ConnectEdges();
			EndFrame();

			var cut = _tool.SelectedEdges.ToList();
			Assert.That( cut.Count, Is.EqualTo( 1 ), "connect selects the new edge" );
			var line = c.Mesh.GetEdgeLine( cut[0].Handle );
			Debug.Log( $"cut edge {line.Start} -> {line.End}" );

			HammerSettings.GridSize = 32;
			Step( "bevel the cut edge", new[] { c }, () => _tool.QuickBevelEdges() );

			var xs = c.Mesh.VertexHandles.Select( v => c.Mesh.GetVertexPosition( v ) ).Where( p => p.x < -7 ).Select( p => Mathf.Round( p.y ) ).Distinct().OrderBy( y => y ).ToList();
			Debug.Log( "front vertex y: " + string.Join( ", ", xs ) );
			// Like Hammer: the bevel's outline stays selected
			var selected = _tool.SelectedEdges.Select( e => c.Mesh.GetEdgeLine( e.Handle ) ).ToList();
			Assert.That( selected.Count, Is.EqualTo( 4 ) );
			Assert.That( selected.Count( l => Mathf.Abs( l.Start.z - l.End.z ) > 500 ), Is.EqualTo( 2 ), "two run the height of the wall" );
			Assert.That( xs, Is.EquivalentTo( new[] { -256f, -32f, 32f, 256f } ), "the bevel sits either side of the cut" );
		}

		[TestCase( false, 1 ), TestCase( true, 1 ), TestCase( false, 3 ), TestCase( true, 3 )]
		public void BevelCutEdgesInARoom( bool inside, int cuts )
		{
			// A room (a box seen from inside, faces flipped) or a solid wall; a wall face cut into
			// columns, each cut bevelled: every cut becomes two edges, evenly either side of it
			var c = Make( new BlockPrimitive(), new S.Vector3( 512, 512, 512 ) );
			if ( inside ) { SelectObjects( c ); _tool.FlipFaces(); EndFrame(); }

			var wallNormal = new S.Vector3( inside ? 1 : -1, 0, 0 );
			var wall = c.Mesh.FaceHandles.First( f => { c.Mesh.ComputeFaceNormal( f, out var n ); return S.Vector3.Dot( n, wallNormal ) > 0.99f; } );
			var x = c.Mesh.GetFaceCenter( wall ).x;

			Select( EditMode.Face, new IMeshElement[] { new MeshFace( c, wall ) } );
			HammerSettings.QuadSliceCuts = new Vector2Int( cuts, 1 );
			c.Mesh.QuadSliceFaces( new List<HalfEdgeMesh.FaceHandle> { wall }, cuts, 0, 60, new List<HalfEdgeMesh.FaceHandle>() );
			c.Commit();
			EndFrame();

			// The cut edges: upright, inside the wall face (not its border)
			var cutEdges = c.Mesh.HalfEdgeHandles.Where( h =>
			{
				var l = c.Mesh.GetEdgeLine( h );
				return Mathf.Abs( l.Start.x - x ) < 0.1f && Mathf.Abs( l.End.x - x ) < 0.1f && Mathf.Abs( l.Start.y - l.End.y ) < 0.1f && Mathf.Abs( l.Start.y ) < 255;
			} ).Select( h => (IMeshElement)new MeshEdge( c, h ) ).ToList();
			var cutYs = cutEdges.Cast<MeshEdge>().Select( e => Mathf.Round( c.Mesh.GetEdgeLine( e.Handle ).Start.y ) ).Distinct().OrderBy( y => y ).ToList();
			Assert.That( cutYs.Count, Is.EqualTo( cuts ), "cuts made" );

			Select( EditMode.Edge, cutEdges );
			HammerSettings.GridSize = 16;
			HammerSettings.BevelSegments = 3; // ignored on flat cuts: still two edges per cut
			Step( $"bevel {cuts} cut(s), inside={inside}", new[] { c }, () => _tool.QuickBevelEdges(), keepsClosed: true );

			var ys = c.Mesh.VertexHandles.Select( v => c.Mesh.GetVertexPosition( v ) ).Where( p => Mathf.Abs( p.x - x ) < 0.1f ).Select( p => Mathf.Round( p.y ) ).Distinct().OrderBy( y => y ).ToList();
			var expected = new List<float> { -256, 256 };
			foreach ( var y in cutYs ) { expected.Add( y - 16 ); expected.Add( y + 16 ); }
			Assert.That( ys, Is.EquivalentTo( expected ), $"wall vertices at {string.Join( ", ", ys )}" );

			Assert.That( _tool.SelectedEdges.Count(), Is.EqualTo( cuts * 4 ), "each bevel's outline stays selected" );
		}

		[Test]
		public void OpenEdgeFeatures()
		{
			var c = Make( new QuadPrimitive(), new S.Vector3( 128, 128, 0 ) );
			var m = new[] { c };
			IEnumerable<IMeshElement> Open() => c.Mesh.HalfEdgeHandles.Where( h => c.Mesh.IsEdgeOpen( h ) ).Select( h => (IMeshElement)new MeshEdge( c, h ) ).ToList();
			IMeshElement FarEdge() => Open().Cast<MeshEdge>().OrderByDescending( e => c.Mesh.GetEdgeLine( e.Handle ).Center.x ).First();

			Step( "extend open edge", m, () => { Select( EditMode.Edge, new[] { FarEdge() } ); _tool.ExtendEdges(); }, keepsClosed: false );
			Step( "extrude open edge (Shift+drag)", m, () => { Select( EditMode.Edge, new[] { FarEdge() } ); Drag( true, Vector3.up * Unit( 32 ) ); }, keepsClosed: false );
			Step( "edge arch", m, () => { Select( EditMode.Edge, new[] { FarEdge() } ); EdgeArchTool.Open( _tool ); _tool.SubTool?.Apply(); }, keepsClosed: false );
			Step( "split edge", m, () => { Select( EditMode.Edge, c.Mesh.HalfEdgeHandles.Where( h => !c.Mesh.IsEdgeOpen( h ) ).Take( 1 ).Select( h => (IMeshElement)new MeshEdge( c, h ) ) ); _tool.SplitEdges(); }, keepsClosed: false );

			// Two separate quads bridged
			var a = Make( new QuadPrimitive(), new S.Vector3( 64, 64, 0 ), Vector3.right * 5 );
			var b = Make( new QuadPrimitive(), new S.Vector3( 64, 64, 0 ), Vector3.right * 5 + Vector3.up * Unit( 64 ) );
			SelectObjects( a, b );
			_tool.MergeMeshes();
			EndFrame();
			var mm = new[] { a };
			Step( "bridge two loops", mm, () =>
			{
				Select( EditMode.Edge, a.Mesh.HalfEdgeHandles.Where( h => a.Mesh.IsEdgeOpen( h ) ).Select( h => (IMeshElement)new MeshEdge( a, h ) ) );
				_tool.BridgeEdges();
			}, keepsClosed: false );
		}

		[Test]
		public void RepeatLastExtrudeAndBevel()
		{
			// Shift+G after an extrude drag extrudes again by the same amount
			var c = Box();
			Select( EditMode.Face, new[] { TopFace( c ) } );
			Drag( true, Vector3.up * Unit( 32 ) );
			EndFrame();
			var top1 = Top( c );
			Assert.That( _tool.LastActionName, Is.EqualTo( "Extrude" ) );

			_tool.RepeatLast();
			EndFrame();
			Assert.That( Top( c ), Is.EqualTo( top1 + 32 ).Within( 0.01f ), "extruded another 32" );
			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 6 + 4 + 4 ), "with new side faces" );
			AssertSound( c, "repeat extrude" );

			Undo.PerformUndo();
			Assert.That( Top( c ), Is.EqualTo( top1 ).Within( 0.01f ), "the repeat undoes on its own" );

			// A remembered operation repeats on the new selection
			var d = Box( 128, Vector3.right * 5 );
			_tool.Remember( "Bevel", () => _tool.QuickBevelEdges() );
			Select( EditMode.Edge, TopEdges( d ).Take( 1 ) );
			HammerSettings.GridSize = 8;
			_tool.RepeatLast();
			Assert.That( d.Mesh.FaceHandles.Count(), Is.EqualTo( 7 ) );
		}

		[Test]
		public void ShiftRotateAndScaleExtrude()
		{
			var c = Box();
			var m = new[] { c };
			// Turned about the face's bottom edge, like bending a pipe or starting an arch
			Step( "Shift+rotate extrudes", m, () =>
			{
				Select( EditMode.Face, FacingFaces( c, new S.Vector3( 1, 0, 0 ) ) );
				var bottomEdge = c.SourceToWorld( new S.Vector3( 64, 0, -64 ) );
				// The bottom edge runs along s&box Y: Unity's X axis
				Rotate( 30, extrude: true, around: bottomEdge, axis: Vector3.right );
			} );
			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 6 + 3 ), "three new sides: the one along the hinge had no width and went" );
			Step( "Shift+scale extrudes", m, () => Scale( new Vector3( 0.5f, 0.5f, 0.5f ), extrude: true ) );
			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 6 + 3 + 4 ), "a tapered extrude" );
		}

		[Test]
		public void CurvedCorridorWithRepeat()
		{
			// Hammer's curved corridor: a face, the pivot moved off to the side (the middle of the
			// curve), Shift+rotate 15 degrees, then Shift+G five more times
			var c = Make( new BlockPrimitive(), new S.Vector3( 64, 128, 128 ) );
			var m = new[] { c };
			var center = c.SourceToWorld( new S.Vector3( 32, 256, 0 ) ); // 256 to the side of the front face

			Select( EditMode.Face, FacingFaces( c, new S.Vector3( 1, 0, 0 ) ) );
			var start = _tool.SelectedFaces.First().CenterWorld;
			typeof( HammerMeshTool ).GetProperty( "_customPivot", Any ).SetValue( _tool, (Vector3?)center );
			typeof( HammerMeshTool ).GetField( "_pivotSelectionVersion", Any ).SetValue( _tool, _tool.Selection.Version );
			Step( "Shift+rotate round the pivot", m, () => Rotate( 15, extrude: true, around: center ) );

			for ( int i = 0; i < 5; i++ )
				Step( $"Shift+G #{i + 1}", m, () => _tool.RepeatLast() );

			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 6 + 6 * 4 ), "six segments" );

			// The end face carried on round the curve: the same distance from the centre
			Assert.That( _tool.SelectedFaces.Count(), Is.EqualTo( 1 ), "the end face is still selected" );
			var cap = _tool.SelectedFaces.First();
			var radius0 = Vector3.Distance( Flat( start ), Flat( center ) );
			var radius6 = Vector3.Distance( Flat( cap.CenterWorld ), Flat( center ) );
			Assert.That( radius6, Is.EqualTo( radius0 ).Within( 0.01f ), "the last face is as far from the centre as the first" );

			// 6 x 15 = 90 degrees round
			var a0 = Flat( start ) - Flat( center );
			var a6 = Flat( cap.CenterWorld ) - Flat( center );
			Assert.That( Vector3.Angle( a0, a6 ), Is.EqualTo( 90 ).Within( 0.5f ) );
			Assert.That( _tool.HasCustomPivot, "the pivot survived all of it" );

			static Vector3 Flat( Vector3 v ) => new( v.x, 0, v.z );
		}

		[Test]
		public void UndoStepsThePivotBack()
		{
			// Place the pivot, move the face (the pivot goes along), place it again: each undo
			// takes one of those back, rather than throwing the pivot away
			_tool.MoveMode = MoveMode.Position;
			var c = Box();
			Select( EditMode.Face, new[] { TopFace( c ) } );
			Vector3? Pivot() => (Vector3?)typeof( HammerMeshTool ).GetProperty( "_customPivot", Any ).GetValue( _tool );
			var top = Top( c );

			var p1 = new Vector3( 1, 2, 3 );
			Undo.IncrementCurrentGroup();
			_tool.ScriptedPivot( p1 );
			Undo.IncrementCurrentGroup();
			EndFrame();
			_tool.ScriptedMove( Vector3.up );
			EndFrame();
			Assert.That( Pivot(), Is.EqualTo( p1 + Vector3.up ), "the move took the pivot along" );
			var p2 = new Vector3( -4, 0, 2 );
			_tool.ScriptedPivot( p2 );
			Undo.IncrementCurrentGroup();
			EndFrame();

			Undo.PerformUndo();
			EndFrame();
			Assert.That( Pivot(), Is.EqualTo( p1 + Vector3.up ), "undo put the pivot back where the move left it" );
			Assert.That( Top( c ), Is.Not.EqualTo( top ), "and left the move alone" );

			Undo.PerformUndo();
			EndFrame();
			Assert.That( Pivot(), Is.EqualTo( p1 ), "undoing the move took the pivot back too" );
			Assert.That( Top( c ), Is.EqualTo( top ).Within( 1e-4f ) );

			Undo.PerformUndo();
			EndFrame();
			Assert.That( Pivot(), Is.Null, "then the first placement" );

			for ( int i = 0; i < 3; i++ ) { Undo.PerformRedo(); EndFrame(); }
			Assert.That( Pivot(), Is.EqualTo( p2 ), "redo brings it all back" );
		}

		[Test]
		public void TypedAmountAfterDrag()
		{
			// Drag the top face up roughly, then type 32 on the numpad and Enter: exactly 32
			var c = Box();
			Select( EditMode.Face, new[] { TopFace( c ) } );
			Drag( false, Vector3.up * Unit( 13.7f ) );
			EndFrame();

			Assert.That( _tool.TypeKey( KeyCode.Keypad3, '3' ) );
			Assert.That( _tool.TypeKey( KeyCode.Keypad2, '2' ) );
			Assert.That( _tool.TypedValue, Is.EqualTo( "32" ) );
			Assert.That( _tool.TypeKey( KeyCode.KeypadEnter, ' ' ) );
			EndFrame();
			Assert.That( Top( c ), Is.EqualTo( 64 + 32 ).Within( 0.01f ), "moved exactly 32" );

			// A rotation, typed as 45 degrees
			Select( EditMode.Face, new[] { TopFace( c ) } );
			Rotate( 10, axis: Vector3.right );
			EndFrame();
			foreach ( var k in new[] { KeyCode.Keypad4, KeyCode.Keypad5 } ) _tool.TypeKey( k, ' ' );
			_tool.TypeKey( KeyCode.KeypadEnter, ' ' );
			var f = (MeshFace)TopFace( c );
			c.Mesh.ComputeFaceNormal( f.Handle, out var n );
			Assert.That( Mathf.Acos( Mathf.Clamp( n.z, -1, 1 ) ) * Mathf.Rad2Deg, Is.EqualTo( 45 ).Within( 0.5f ), "turned exactly 45 degrees" );
			AssertSound( c, "typed" );
		}

		[Test]
		public void SolidEdgeExtrude()
		{
			var c = Box();
			Step( "extrude solid edge", new[] { c }, () => { Select( EditMode.Edge, TopEdges( c ).Take( 1 ) ); Drag( true, Vector3.up * Unit( 32 ) ); }, keepsClosed: false );
			Step( "extrude it again", new[] { c }, () => Drag( true, Vector3.up * Unit( 32 ) ), keepsClosed: false );
		}

		// ── Vertices ──

		[Test]
		public void VertexFeatures()
		{
			var c = Box();
			var m = new[] { c };

			Step( "drag a vertex", m, () => { Select( EditMode.Vertex, new[] { TopCorner( c ) } ); Drag( false, Vector3.up * Unit( 16 ) ); } );
			Step( "bevel a vertex", m, () => { Select( EditMode.Vertex, new[] { TopCorner( c, -1, -1 ) } ); HammerSettings.GridSize = 32; _tool.BevelVertices(); HammerSettings.GridSize = 8; } );
			Step( "connect two vertices across a face", m, () =>
			{
				var bottom = c.Mesh.FaceHandles.OrderBy( f => c.Mesh.GetFaceCenter( f ).z ).First();
				var corners = c.Mesh.GetFaceVertices( bottom );
				Select( EditMode.Vertex, new IMeshElement[] { new MeshVertex( c, corners[0] ), new MeshVertex( c, corners[2] ) } );
				_tool.ConnectVertices();
			} );
			Step( "merge two vertices", m, () =>
			{
				var bottom = c.Mesh.VertexHandles.OrderBy( v => c.Mesh.GetVertexPosition( v ).z ).Take( 4 ).ToList();
				var a = bottom[0];
				var b = bottom.Skip( 1 ).First( v => c.Mesh.FindEdgeConnectingVertices( a, v ).IsValid );
				Select( EditMode.Vertex, new IMeshElement[] { new MeshVertex( c, a ), new MeshVertex( c, b ) } );
				_tool.MergeVertices();
			} );
			// Snap to last isn't checked for soundness: snapping corners of one face together
			// squashes it by design (the health check then offers the fix)
			Step( "extrude a vertex", m, () => { Select( EditMode.Vertex, new[] { TopCorner( c, 1, -1 ) } ); Drag( true, Vector3.up * Unit( 24 ) ); }, keepsClosed: false );
		}

		[Test]
		public void AlignFeatures()
		{
			var c = Make( new CylinderPrimitive { NumberOfSides = 10 }, new S.Vector3( 128, 128, 64 ) );
			var m = new[] { c };
			Select( EditMode.Vertex, new[] { TopCorner( c ) } );
			Drag( false, Vector3.right * Unit( 20 ) );
			EndFrame();
			Step( "radial align after a wonky drag", m, () =>
			{
				Select( EditMode.Face, new[] { TopFace( c ) } );
				_tool.RadialAlign();
			}, keepsClosed: true );

			var low = Box( 64, Vector3.right * 5 );
			var high = Box( 64, Vector3.right * 5 + Vector3.up * Unit( 40 ) );
			Step( "move to furthest Z+", new[] { low, high }, () =>
			{
				Select( EditMode.Face, new[] { TopFace( low ), TopFace( high ) } );
				_tool.MoveToFurthest( 2, 1 );
			} );
		}

		// ── Objects ──

		[Test]
		public void ObjectFeatures()
		{
			var a = Box( 128 );
			var b = Box( 64, new Vector3( Unit( 80 ), 0, 0 ) );

			SelectObjects( a, b );
			_tool.MergeMeshes();
			EndFrame();
			Assert.That( b == null, "merged away" );
			AssertSound( a, "merge" );
			var merged = Shape( a );
			Undo.PerformUndo();
			Assert.That( Shape( a ), Is.Not.EqualTo( merged ), "undoing a merge restores the mesh" );
			Assert.That( Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ).Length, Is.EqualTo( 2 ), "and brings the other object back" );
			Undo.PerformRedo();
			Assert.That( Shape( a ), Is.EqualTo( merged ), "redo merges again" );

			SelectObjects( a );
			_tool.SeparateComponents();
			EndFrame();
			var pieces = UnityEditor.Selection.gameObjects.Select( g => g.GetComponent<HammerMesh>() ).ToList();
			_objects.AddRange( pieces.Select( p => p.gameObject ) );
			Assert.That( pieces.Count, Is.EqualTo( 2 ) );
			foreach ( var p in pieces ) AssertSound( p, "separate" );

			var c = Box( 128, Vector3.right * 6 );
			Step( "clip on a slant", new[] { c }, () =>
			{
				Undo.RecordObject( c, "Clip" );
				c.Mesh.ClipFacesByPlaneAndCap( c.Mesh.FaceHandles.ToList(), new S.Plane( S.Vector3.Zero, new S.Vector3( 1, 0, 1 ).Normal ), true, true );
				c.Commit();
			} );

			var d = Box( 64, Vector3.right * 9 );
			Step( "subdivide whole object", new[] { d }, () => { SelectObjects( d ); _tool.Subdivide(); } );
			Step( "flip whole object", new[] { d }, () => { SelectObjects( d ); _tool.FlipFaces(); } );
		}

		[Test]
		public void BooleanUndoStepByStep()
		{
			var target = Box( 128 );
			var other = Make( new CylinderPrimitive { NumberOfSides = 12 }, new S.Vector3( 64, 64, 192 ), Vector3.forward * Unit( 32 ) );
			var before = Shape( target );
			SelectObjects( target, other );
			_tool.Boolean( S.PolygonMesh.BooleanOperation.Subtract );
			EndFrame();
			var after = Shape( target );
			Assert.That( other == null, "cutter used up" );

			var log = new List<string>();
			for ( int i = 0; i < 4; i++ )
			{
				Undo.PerformUndo();
				var cutter = Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ).Any( x => x != target );
				log.Add( $"undo {i}: target {(target == null ? "gone" : Shape( target ) == before ? "BEFORE" : Shape( target ) == after ? "AFTER" : "other")}, cutter {(cutter ? "back" : "gone")}" );
			}
			Debug.Log( string.Join( " / ", log ) );
			Assert.That( log[0], Does.Contain( "BEFORE" ).And.Contain( "back" ), string.Join( " / ", log ) );
		}

		[Test]
		public void Booleans()
		{
			foreach ( var op in new[] { S.PolygonMesh.BooleanOperation.Subtract, S.PolygonMesh.BooleanOperation.Union, S.PolygonMesh.BooleanOperation.Intersect } )
			{
				var x = (int)op * 6;
				var target = Box( 128, Vector3.right * x );
				var other = Make( new CylinderPrimitive { NumberOfSides = 12 }, new S.Vector3( 64, 64, 192 ), Vector3.right * x + Vector3.forward * Unit( 32 ) );
				Step( $"boolean {op}", new[] { target }, () => { SelectObjects( target, other ); _tool.Boolean( op ); }, keepsClosed: false );
				Assert.That( OpenEdges( target ), Is.Zero, $"{op} left holes" );
			}
		}

		// ── Random chains ──

		/// <summary>
		/// Seeded random chains of operations on one box, checking everything after each step.
		/// Catches combinations nobody thought to test.
		/// </summary>
		[TestCase( 1 ), TestCase( 2 ), TestCase( 3 ), TestCase( 4 ), TestCase( 5 ), TestCase( 6 ), TestCase( 7 ), TestCase( 8 )]
		public void RandomChains( int seed )
		{
			var rng = new System.Random( seed );
			var c = Box();
			var m = new[] { c };
			T Pick<T>( IList<T> list ) => list[rng.Next( list.Count )];

			List<IMeshElement> AllFaces() => c.Mesh.FaceHandles.Where( f => !c.Mesh.IsFaceHidden( f ) ).Select( f => (IMeshElement)new MeshFace( c, f ) ).ToList();
			List<IMeshElement> AllEdges() => c.Mesh.HalfEdgeHandles.Select( h => (IMeshElement)new MeshEdge( c, h ) ).ToList();
			List<IMeshElement> AllVerts() => c.Mesh.VertexHandles.Select( v => (IMeshElement)new MeshVertex( c, v ) ).ToList();

			var ops = new (string name, Func<bool> run, bool closed)[]
			{
				("inset", () => { Select( EditMode.Face, new[] { Pick( AllFaces() ) } ); HammerSettings.GridSize = 4; _tool.InsetFaces(); return true; }, true),
				("extrude", () => { Select( EditMode.Face, new[] { Pick( AllFaces() ) } ); _tool.ExtrudeFaces(); return true; }, true),
				("drag-extrude", () => { var f = Pick( AllFaces() ); Select( EditMode.Face, new[] { f } ); Drag( true, Outward( f ) * Unit( 16 ) ); return true; }, true),
				("bevel edge", () => { Select( EditMode.Edge, new[] { Pick( AllEdges() ) } ); HammerSettings.GridSize = 2; _tool.QuickBevelEdges(); return true; }, true),
				("bevel vertex", () => { Select( EditMode.Vertex, new[] { Pick( AllVerts() ) } ); HammerSettings.GridSize = 2; _tool.BevelVertices(); return true; }, true),
				("quad slice", () => { Select( EditMode.Face, new[] { Pick( AllFaces() ) } ); HammerSettings.QuadSliceCuts = new Vector2Int( 1, 1 ); _tool.QuadSlice(); return true; }, true),
				("move face out", () => { var f = Pick( AllFaces() ); Select( EditMode.Face, new[] { f } ); Drag( false, Outward( f ) * Unit( 8 ) ); return true; }, true),
				("ring connect", () => { Select( EditMode.Edge, new[] { Pick( AllEdges() ) } ); _tool.SelectRing(); if ( _tool.SelectedEdges.Count() < 2 ) return false; _tool.ConnectEdges(); return true; }, true),
			};

			Vector3 Outward( IMeshElement e )
			{
				var f = (MeshFace)e;
				f.Component.Mesh.ComputeFaceNormal( f.Handle, out var n );
				return f.Component.SourceDirectionToWorld( n ).normalized;
			}

			var done = 0;
			for ( int i = 0; i < 40 && done < 12; i++ )
			{
				var (name, run, closed) = ops[rng.Next( ops.Length )];
				var before = Shape( c );
				var ran = false;

				try
				{
					Step( $"#{seed}.{done} {name}", m, () => { ran = run(); HammerSettings.GridSize = 8; }, closed );
					done++;
				}
				catch ( AssertionException e ) when ( !ran || e.Message.Contains( "changed nothing" ) )
				{
					// Not applicable to what got picked (nothing to do): try something else
					Assert.That( Shape( c ), Is.EqualTo( before ) );
				}
			}

			Assert.That( done, Is.GreaterThan( 6 ), "enough steps ran" );
			Debug.Log( $"Sweep seed {seed}:\n{string.Join( "\n", _log )}" );
		}
	
		// ── One Ctrl+Z per panel button ──

		string SelectionText() => string.Join( " ", _tool.Selection.Where( x => x.IsValid ).Select( x => $"{x.GetType().Name[4]}{x.Index}" ).OrderBy( x => x ) );

		[Test]
		public void EveryPanelButtonUndoesInOneStep()
		{
			// Each button as a click does it: its own undo step, the selection it leaves included.
			// One Ctrl+Z must give back both the shape and the selection, one redo both again
			(EditMode Mode, string Op, Func<HammerMesh, IEnumerable<IMeshElement>> Pick)[] cases =
			{
				(EditMode.Face, "ExtrudeFaces", c => new[] { TopFace( c ) }),
				(EditMode.Face, "InsetFaces", c => new[] { TopFace( c ) }),
				(EditMode.Face, "DetachFaces", c => new[] { TopFace( c ) }),
				(EditMode.Face, "FlipFaces", c => new[] { TopFace( c ) }),
				(EditMode.Face, "ThickenFaces", c => new[] { TopFace( c ) }),
				(EditMode.Face, "QuadSlice", c => new[] { TopFace( c ) }),
				(EditMode.Face, "HideFaces", c => new[] { TopFace( c ) }),
				(EditMode.Face, "ExtractFaces", c => new[] { TopFace( c ) }),
				(EditMode.Face, "Delete", c => new[] { TopFace( c ) }),
				(EditMode.Face, "Collapse", c => new[] { TopFace( c ) }),
				(EditMode.Face, "GrowSelection", c => new[] { TopFace( c ) }),
				(EditMode.Face, "ShrinkSelection", c => new[] { TopFace( c ) }),
				(EditMode.Face, "InvertSelection", c => new[] { TopFace( c ) }),
				(EditMode.Face, "SelectAll", c => new[] { TopFace( c ) }),
				(EditMode.Face, "Subdivide", c => new[] { TopFace( c ) }),
				(EditMode.Edge, "QuickBevelEdges", c => TopEdges( c ).Take( 1 )),
				(EditMode.Edge, "ConnectEdges", c => UprightEdges( c ).Take( 2 )),
				(EditMode.Edge, "SplitEdges", c => TopEdges( c ).Take( 1 )),
				(EditMode.Edge, "DissolveEdges", c => TopEdges( c ).Take( 1 )),
				(EditMode.Edge, "ExtrudeEdges", c => TopEdges( c ).Take( 1 )),
				(EditMode.Edge, "Collapse", c => TopEdges( c ).Take( 1 )),
				(EditMode.Edge, "SelectLoop", c => TopEdges( c ).Take( 1 )),
				(EditMode.Edge, "SelectRing", c => TopEdges( c ).Take( 1 )),
				(EditMode.Vertex, "BevelVertices", c => new[] { TopCorner( c ) }),
				(EditMode.Vertex, "MergeVertices", c => new[] { TopCorner( c ), TopCorner( c, -1, 1 ) }),
				(EditMode.Vertex, "Delete", c => new[] { TopCorner( c ) }),
				(EditMode.Vertex, "GrowSelection", c => new[] { TopCorner( c ) }),
			};

			var failures = new List<string>();
			_tool.Attach();
			try
			{
				foreach ( var (mode, op, pick) in cases )
				{
					var c = Box();
					var m = new[] { c };
					Select( mode, pick( c ) );

					var shapeBefore = Shapes( m );
					var selBefore = SelectionText();

					Call( op );
					EndFrame();

					var shapeAfter = Shapes( m );
					var selAfter = SelectionText();
					if ( shapeAfter == shapeBefore && selAfter == selBefore ) { failures.Add( $"{mode} {op}: did nothing" ); goto next; }

					Undo.PerformUndo();
					if ( Shapes( m ) != shapeBefore ) failures.Add( $"{mode} {op}: one undo didn't restore the shape" );
					if ( SelectionText() != selBefore ) failures.Add( $"{mode} {op}: one undo didn't restore the selection ({SelectionText()} vs {selBefore})" );

					Undo.PerformRedo();
					if ( Shapes( m ) != shapeAfter ) failures.Add( $"{mode} {op}: one redo didn't restore the shape" );
					if ( SelectionText() != selAfter ) failures.Add( $"{mode} {op}: one redo didn't restore the selection ({SelectionText()} vs {selAfter})" );

					next:
					// Everything made along the way (an extract makes a new object) goes too
					foreach ( var left in Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ) )
						Object.DestroyImmediate( left.gameObject );
					Undo.ClearAll();
				}
			}
			finally
			{
				_tool.Detach();
			}

			Assert.That( failures, Is.Empty, string.Join( "\n", failures ) );
		}

		[Test]
		public void UVLockKeepsTexturesOnMovedFaces()
		{
			// Texture Lock Component Manipulations: with it on, moving the top face keeps the side
			// faces' texture coordinates (the texture stretches); off, they're worked out again
			// from the world (the texture stays put and the face slides over it)
			foreach ( var locked in new[] { true, false } )
			{
				var c = Box();
				var side = FacingFaces( c, new S.Vector3( 1, 0, 0 ) ).Cast<MeshFace>().First();
				var before = c.Mesh.GetFaceTextureCoords( side.Handle ).Select( t => $"{t.x:0.###},{t.y:0.###}" ).ToList();

				var old = HammerSettings.TextureLockComponents;
				HammerSettings.TextureLockComponents = locked;
				Select( EditMode.Face, new[] { TopFace( c ) } );
				Drag( false, Vector3.up * Unit( 32 ) );
				EndFrame();
				HammerSettings.TextureLockComponents = old;

				var after = c.Mesh.GetFaceTextureCoords( side.Handle ).Select( t => $"{t.x:0.###},{t.y:0.###}" ).ToList();
				if ( locked ) Assert.That( after, Is.EqualTo( before ), "UV lock: the side's texture coordinates didn't change" );
				else Assert.That( after, Is.Not.EqualTo( before ), "no UV lock: the side's texture follows the world" );
			}
		}

		[Test]
		public void HiddenFacesAreKept()
		{
			// Hidden faces are saved with the object (a copy, like a scene reload, still has them)
			var c = Box();
			Select( EditMode.Face, new[] { TopFace( c ) } );
			_tool.HideFaces();
			EndFrame();

			var copy = Object.Instantiate( c.gameObject ).GetComponent<HammerMesh>();
			_objects.Add( copy.gameObject );
			Assert.That( copy.Mesh.FaceHandles.Count( f => copy.Mesh.IsFaceHidden( f ) ), Is.EqualTo( 1 ), "still hidden after a reload" );

			_tool.UnhideFaces();
			EndFrame();
			var again = Object.Instantiate( c.gameObject ).GetComponent<HammerMesh>();
			_objects.Add( again.gameObject );
			Assert.That( again.Mesh.HasHiddenFaces, Is.False, "unhide is saved too" );
		}

		[Test]
		public void OpenToolSettingsUndoFirst()
		{
			// Inside an open tool, undo steps back through its setting changes before anything else
			var c = Box();
			Select( EditMode.Edge, TopEdges( c ).Take( 1 ) );
			BevelTool.Open( _tool );
			var bevel = _tool.SubTool;
			Assert.That( bevel, Is.Not.Null );

			var steps = typeof( BevelTool ).GetField( "_steps", BindingFlags.NonPublic | BindingFlags.Static );
			var start = (int)steps.GetValue( null );
			bevel.TrackSettings( () => steps.SetValue( null, start + 2 ) );
			bevel.TrackSettings( () => steps.SetValue( null, start + 5 ) );

			Assert.That( bevel.UndoSettings(), Is.True );
			Assert.That( steps.GetValue( null ), Is.EqualTo( start + 2 ) );
			Assert.That( bevel.UndoSettings(), Is.True );
			Assert.That( steps.GetValue( null ), Is.EqualTo( start ) );
			Assert.That( bevel.UndoSettings(), Is.False, "nothing left: the next Ctrl+Z cancels the tool" );
			Assert.That( bevel.RedoSettings(), Is.True );
			Assert.That( steps.GetValue( null ), Is.EqualTo( start + 2 ) );

			bevel.Cancel();
			steps.SetValue( null, start );
		}

		[Test]
		public void SettingsUndo()
		{
			// Panel settings are part of the undo history, like Hammer's tool properties
			var before = HammerSettings.BevelSegments;
			EndFrame();
			HammerSettings.BevelSegments = before + 3;
			EndFrame();
			Undo.PerformUndo();
			Assert.That( HammerSettings.BevelSegments, Is.EqualTo( before ), "undo puts a setting back" );
			Undo.PerformRedo();
			Assert.That( HammerSettings.BevelSegments, Is.EqualTo( before + 3 ), "redo sets it again" );
		}
	}
}
