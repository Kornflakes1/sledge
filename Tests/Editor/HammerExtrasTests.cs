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
	/// Hammer's per-mode options added after the first pass: merge within a distance, edge
	/// extrude, UV peel, subdivision levels, filtered selection, find/replace materials, and the
	/// Meshes mode origin, pivot, freeze and align tools.
	/// </summary>
	public class HammerExtrasTests
	{
		HammerMeshTool _tool;
		readonly List<GameObject> _objects = new();

		[SetUp]
		public void SetUp()
		{
			Undo.ClearAll();
			_tool = ScriptableObject.CreateInstance<HammerMeshTool>();
			HammerSettings.GridSize = 8;
		}

		[TearDown]
		public void TearDown()
		{
			foreach ( var go in _objects ) if ( go != null ) Object.DestroyImmediate( go );
			_objects.Clear();
			Object.DestroyImmediate( _tool );
			UnityEditor.Selection.objects = new Object[0];
			Undo.ClearAll();
			Workplane.Reset();
		}

		HammerMesh Make( PrimitiveBuilder builder, S.Vector3 size, Vector3 position = default )
		{
			var go = new GameObject( "Extras" );
			_objects.Add( go );
			go.transform.position = position;
			var c = go.AddComponent<HammerMesh>();
			c.Mesh = builder.CreateMesh( new S.BBox( -size / 2, size / 2 ) );
			return c;
		}

		HammerMesh Box( float size = 128, Vector3 position = default ) => Make( new BlockPrimitive(), new S.Vector3( size ), position );

		void Select( EditMode mode, IEnumerable<IMeshElement> elements )
		{
			_tool.Mode = mode;
			_tool.Selection.Clear();
			foreach ( var e in elements ) _tool.Selection.Add( e );
		}

		void SelectObjects( params HammerMesh[] meshes )
		{
			_tool.Mode = EditMode.Object;
			UnityEditor.Selection.objects = meshes.Select( m => (Object)m.gameObject ).ToArray();
			UnityEditor.Selection.activeGameObject = meshes[0].gameObject;
		}

		static IMeshElement Facing( HammerMesh c, S.Vector3 dir ) =>
			new MeshFace( c, c.Mesh.FaceHandles.OrderByDescending( f => { c.Mesh.ComputeFaceNormal( f, out var n ); return S.Vector3.Dot( n, dir ); } ).First() );

		static float Top( HammerMesh c ) => c.Mesh.VertexHandles.Max( v => c.Mesh.GetVertexPosition( v ).z );

		// ── Vertices ──

		[Test]
		public void MergeWithinDistanceOnlyJoinsCloseVertices()
		{
			var c = Box();
			var mesh = c.Mesh;
			var top = mesh.VertexHandles.Where( v => mesh.GetVertexPosition( v ).z > 63 ).ToList();

			// Nudge one top corner right next to another
			var a = top[0];
			var b = top.First( v => v != a && mesh.FindEdgeConnectingVertices( a, v ).IsValid );
			mesh.SetVertexPosition( a, mesh.GetVertexPosition( b ) + new S.Vector3( 0.05f, 0, 0 ) );
			c.Commit();

			HammerSettings.MergeInfinite = false;
			HammerSettings.MergeDistance = 0.1f;
			Select( EditMode.Vertex, top.Select( v => (IMeshElement)new MeshVertex( c, v ) ) );
			_tool.MergeVertices();

			Assert.That( c.Mesh.VertexHandles.Count(), Is.EqualTo( 7 ), "only the close pair joined" );
			HammerSettings.MergeInfinite = true;
		}

		// ── Edges ──

		[Test]
		public void ExtrudeEdgeButtonExtrudesOneGridStep()
		{
			var c = Make( new QuadPrimitive(), new S.Vector3( 128, 128, 0 ) );
			var mesh = c.Mesh;
			var far = mesh.HalfEdgeHandles.Where( h => mesh.IsEdgeOpen( h ) ).OrderByDescending( h => mesh.GetEdgeLine( h ).Center.x ).First();
			Select( EditMode.Edge, new IMeshElement[] { new MeshEdge( c, far ) } );
			HammerSettings.GridSize = 16;
			_tool.ExtrudeEdges();

			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 2 ), "a new face off the edge" );
			Assert.That( c.Mesh.VertexHandles.Max( v => c.Mesh.GetVertexPosition( v ).x ), Is.EqualTo( 64 + 16 ).Within( 0.01f ), "out by one grid step, in the quad's plane" );
			Assert.That( _tool.LastActionName, Is.EqualTo( "Extrude" ), "Shift+G repeats it" );
		}

		[Test]
		public void UVPeelRunsTheTextureAlongAStrip()
		{
			// A long wall's front cut into 4 columns: peel along its bottom edges
			var c = Make( new BlockPrimitive(), new S.Vector3( 16, 512, 128 ) );
			var mesh = c.Mesh;
			var front = mesh.FaceHandles.First( f => { mesh.ComputeFaceNormal( f, out var n ); return n.x < -0.99f; } );
			mesh.QuadSliceFaces( new List<HalfEdgeMesh.FaceHandle> { front }, 3, 0, 60, new List<HalfEdgeMesh.FaceHandle>() );
			c.Commit();

			var bottom = mesh.HalfEdgeHandles.Where( h =>
			{
				var l = mesh.GetEdgeLine( h );
				return l.Start.x < -7.9f && l.End.x < -7.9f && l.Start.z < -63 && l.End.z < -63;
			} ).ToList();
			Select( EditMode.Edge, bottom.Select( h => (IMeshElement)new MeshEdge( c, h ) ) );

			HammerSettings.PeelAlongV = false;
			HammerSettings.PeelWorldSpace = false;
			HammerSettings.PeelURepeats = 2;
			HammerSettings.PeelVRepeats = 1;
			HammerSettings.PeelUOffset = 0;
			HammerSettings.PeelVOffset = 0;
			_tool.PeelUVs();

			// The front's corners: u runs 0..2 along the wall, v 0..1 up it
			var us = new List<float>();
			var vs = new List<float>();
			foreach ( var f in mesh.FaceHandles.Where( f => { mesh.ComputeFaceNormal( f, out var n ); return n.x < -0.99f; } ) )
			{
				var uv = mesh.GetFaceTextureCoords( f );
				us.AddRange( uv.Select( x => x.x ) );
				vs.AddRange( uv.Select( x => x.y ) );
			}

			Assert.That( us.Min(), Is.EqualTo( 0 ).Within( 0.01f ) );
			Assert.That( us.Max(), Is.EqualTo( 2 ).Within( 0.01f ), "two repeats along the strip" );
			Assert.That( vs.Min(), Is.EqualTo( 0 ).Within( 0.01f ) );
			Assert.That( vs.Max(), Is.EqualTo( 1 ).Within( 0.01f ) );
			Assert.That( us.Select( u => Mathf.Round( u * 100 ) ).Distinct().Count(), Is.EqualTo( 5 ), "continuous: 5 columns of corners shared by the 4 faces" );
		}

		// ── Faces ──

		[Test]
		public void SubdivisionLevelsSmoothWithoutChangingTheMesh()
		{
			var c = Box();
			var faces = c.Mesh.FaceHandles.Count();
			SelectObjects( c );

			_tool.SetSubdivision( 2 );
			Assert.That( c.SubdivisionLevel, Is.EqualTo( 2 ) );
			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( faces ), "the editable mesh is unchanged" );
			var rendered = c.GetComponent<MeshFilter>().sharedMesh;
			Assert.That( rendered.triangles.Length / 3, Is.EqualTo( 6 * 16 * 2 ), "drawn as 16 quads a side" );

			// Smoothed: the corners pull in, so it's smaller than the box
			Assert.That( rendered.bounds.size.x, Is.LessThan( 128 * SourceSpace.UnitScale - 0.01f ) );

			Undo.IncrementCurrentGroup();
			_tool.IncreaseSubdivision();
			Assert.That( c.SubdivisionLevel, Is.EqualTo( 3 ) );
			Undo.IncrementCurrentGroup();
			_tool.DecreaseSubdivision();
			Undo.PerformUndo();
			Assert.That( c.SubdivisionLevel, Is.EqualTo( 3 ), "undo" );
			_tool.SetSubdivision( 1 );

			_tool.BakeSubdivision();
			Assert.That( c.SubdivisionLevel, Is.Zero );
			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 24 ), "baked into the mesh" );
			Assert.That( c.Mesh.FindBadFaces( includeNonPlanar: false ), Is.Empty );
			Assert.That( c.Mesh.HalfEdgeHandles.Count( h => c.Mesh.IsEdgeOpen( h ) ), Is.Zero, "still closed" );
		}

		[Test]
		public void SubdivisionKeepsHardEdgesSharp()
		{
			var c = Box();
			foreach ( var h in c.Mesh.HalfEdgeHandles ) c.Mesh.SetEdgeSmoothing( h, S.PolygonMesh.EdgeSmoothMode.Hard );
			var smooth = c.Mesh.Subdivided( 2 );
			var max = smooth.VertexHandles.Max( v => smooth.GetVertexPosition( v ).x );
			var corners = smooth.VertexHandles.Count( v => { var p = smooth.GetVertexPosition( v ); return Mathf.Abs( Mathf.Abs( p.x ) - 64 ) < 0.01f && Mathf.Abs( Mathf.Abs( p.y ) - 64 ) < 0.01f && Mathf.Abs( Mathf.Abs( p.z ) - 64 ) < 0.01f; } );
			Assert.That( max, Is.EqualTo( 64 ).Within( 0.01f ), "all edges hard: the box keeps its shape" );
			Assert.That( corners, Is.EqualTo( 8 ) );
		}

		[Test]
		public void SelectContiguousStopsAtTheNormalFilter()
		{
			var c = Make( new CylinderPrimitive { NumberOfSides = 16 }, new S.Vector3( 128, 128, 128 ) );
			var side = c.Mesh.FaceHandles.First( f => { c.Mesh.ComputeFaceNormal( f, out var n ); return Mathf.Abs( n.z ) < 0.1f; } );
			Select( EditMode.Face, new IMeshElement[] { new MeshFace( c, side ) } );

			HammerSettings.FilterMaterial = false;
			HammerSettings.FilterNormal = true;
			HammerSettings.FilterNormalAngle = 30;
			_tool.SelectContiguous();
			Assert.That( _tool.SelectedFaces.Count(), Is.EqualTo( 16 ), "all the sides (22.5 degrees apart), not the caps" );

			HammerSettings.FilterNormal = false;
			_tool.SelectContiguous();
			Assert.That( _tool.SelectedFaces.Count(), Is.EqualTo( 18 ), "no filter: everything connected" );
			HammerSettings.FilterNormal = true;
			HammerSettings.FilterNormalAngle = 15;
		}

		[Test]
		public void FindReplaceMaterial()
		{
			var c = Box();
			var red = AssetDatabase.LoadAssetAtPath<Material>( "Packages/com.hammerunity.meshtools/Runtime/DevTextures/Dev Measure Red.mat" );
			var blue = AssetDatabase.LoadAssetAtPath<Material>( "Packages/com.hammerunity.meshtools/Runtime/DevTextures/Dev Measure Blue.mat" );
			Assert.That( red != null && blue != null );

			var faces = c.Mesh.FaceHandles.ToList();
			c.Mesh.AssignMaterialToFaces( faces.Take( 2 ).ToList(), HammerMaterials.Get( red ) );
			c.Commit();

			var changed = HammerMeshTool.ReplaceMaterial( new[] { (c, (IEnumerable<HalfEdgeMesh.FaceHandle>)c.Mesh.FaceHandles.ToList()) }, red, blue );
			Assert.That( changed, Is.EqualTo( 2 ) );
			Assert.That( c.Mesh.FaceHandles.Count( f => c.Mesh.GetFaceMaterial( f )?.Name == HammerMaterials.Get( blue ).Name ), Is.EqualTo( 2 ) );

			// From the default grid to red: the other four
			Assert.That( HammerMeshTool.ReplaceMaterial( new[] { (c, (IEnumerable<HalfEdgeMesh.FaceHandle>)c.Mesh.FaceHandles.ToList()) }, null, red ), Is.EqualTo( 4 ) );
		}

		// ── Block tool ──

		[Test]
		public void BlockWaitsWithHandlesThenSelectsAllOnModeSwitch()
		{
			var any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
			_tool.Mode = EditMode.Primitive;
			HammerSettings.PrimitiveType = "Box";

			// A drawn box, waiting with its handles (as after releasing the drag)
			var stage = typeof( HammerMeshTool ).GetField( "_primitiveStage", any );
			stage.SetValue( _tool, System.Enum.Parse( stage.FieldType, "Editing" ) );
			typeof( HammerMeshTool ).GetField( "_primitiveNormal", any ).SetValue( _tool, Vector3.up );
			typeof( HammerMeshTool ).GetField( "_editBounds", any ).SetValue( _tool, new Bounds( new Vector3( 0, 0.5f, 0 ), new Vector3( 2, 1, 3 ) ) );

			var before = Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ).Length;
			Assert.That( _tool.OnSpace(), "Space confirms the shape" );
			var made = Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ).Except( _objects.Select( o => o != null ? o.GetComponent<HammerMesh>() : null ) ).ToList();
			Assert.That( Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ).Length, Is.EqualTo( before + 1 ) );
			var box = UnityEditor.Selection.activeGameObject.GetComponent<HammerMesh>();
			_objects.Add( box.gameObject );
			Assert.That( box.GetComponent<MeshRenderer>().bounds.size.z, Is.EqualTo( 3 ).Within( 0.01f ) );
			Assert.That( _tool.Mode, Is.EqualTo( EditMode.Object ) );

			_tool.Mode = EditMode.Face;
			Assert.That( _tool.SelectedFaces.Count(), Is.EqualTo( 6 ), "switching to Faces selects all of the new box's" );

			// Space then cycles modes
			Assert.That( _tool.OnSpace(), Is.False );
		}

		[Test]
		public void ShiftDragInMeshesModeCopies()
		{
			var c = Box();
			SelectObjects( c );
			var any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
			typeof( HammerMeshTool ).GetMethod( "BeginObjectDrag", any ).Invoke( _tool, new object[] { "Move", true } );

			var copy = UnityEditor.Selection.activeGameObject;
			Assert.That( copy != c.gameObject, "the copy is what's being moved" );
			_objects.Add( copy );
			copy.transform.position += Vector3.right * 3;
			Assert.That( Vector3.Distance( c.transform.position, Vector3.zero ), Is.LessThan( 0.001f ), "the original stays" );
			Assert.That( copy.GetComponent<HammerMesh>().Mesh.FaceHandles.Count(), Is.EqualTo( 6 ) );
		}

		[Test]
		public void MirrorFacesStayInTheSameMesh()
		{
			var c = Box();
			_tool.Mode = EditMode.Face;
			_tool.Selection.Clear();
			_tool.Selection.Add( Facing( c, new S.Vector3( 0, 0, 1 ) ) );
			MirrorTool.Open( _tool );
			var mirror = (MirrorTool)_tool.SubTool;
			Assert.That( mirror != null );

			// A mirror plane along the floor, below the box: the top face reflects to under it
			var any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
			var t = typeof( MirrorTool );
			t.GetField( "_hasHitPlane", any ).SetValue( mirror, true );
			t.GetField( "_hitNormal", any ).SetValue( mirror, Vector3.forward );
			t.GetField( "_point1", any ).SetValue( mirror, new Vector3( -1, -2, 0 ) );
			t.GetField( "_point2", any ).SetValue( mirror, new Vector3( 1, -2, 0 ) );
			t.GetMethod( "UpdateCopies", any ).Invoke( mirror, null );
			mirror.Apply();

			Assert.That( Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ).Length, Is.EqualTo( 1 ), "no new object" );
			Assert.That( c.Mesh.FaceHandles.Count(), Is.EqualTo( 7 ), "the mirrored face is in the box's mesh" );
			Assert.That( c.GetComponent<MeshRenderer>().bounds.min.y, Is.LessThan( -3 ), "and it's below, mirrored" );
			Assert.That( _tool.SelectedFaces.Count(), Is.EqualTo( 1 ) );
		}

		// ── Workplane ──

		[Test]
		public void WorkplaneFromAFaceTiltsSnappingAndNewBlocks()
		{
			// A ramp: a box turned 30 degrees about the X axis; pick its top face
			var ramp = Box();
			ramp.transform.rotation = Quaternion.Euler( 30, 0, 0 );
			ramp.Commit();
			var top = ramp.Mesh.FaceHandles.First( f => { ramp.Mesh.ComputeFaceNormal( f, out var n ); return n.z > 0.9f; } );
			Workplane.SetFromFace( new MeshFace( ramp, top ) );

			Assert.That( Workplane.Active );
			Assert.That( Vector3.Angle( Workplane.Up, ramp.transform.up ), Is.LessThan( 0.1f ), "up is the face's normal" );

			// Snapping happens on the workplane's own grid
			var step = HammerSettings.GridSize * SourceSpace.UnitScale;
			var snapped = Workplane.ToLocal( HammerSettings.SnapWorld( Workplane.ToWorld( new Vector3( step * 2.3f, step * 0.7f, step * -1.4f ) ) ) );
			Assert.That( snapped.x, Is.EqualTo( step * 2 ).Within( 1e-4f ) );
			Assert.That( snapped.y, Is.EqualTo( step * 1 ).Within( 1e-4f ) );
			Assert.That( snapped.z, Is.EqualTo( step * -1 ).Within( 1e-4f ) );

			// A block drawn now comes out square to the ramp
			var any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
			_tool.Mode = EditMode.Primitive;
			HammerSettings.PrimitiveType = "Box";
			var stage = typeof( HammerMeshTool ).GetField( "_primitiveStage", any );
			stage.SetValue( _tool, System.Enum.Parse( stage.FieldType, "Editing" ) );
			typeof( HammerMeshTool ).GetField( "_primitiveNormal", any ).SetValue( _tool, Vector3.up );
			typeof( HammerMeshTool ).GetField( "_editBounds", any ).SetValue( _tool, new Bounds( new Vector3( 0, 0.5f, 0 ), new Vector3( 1, 1, 1 ) ) );
			_tool.Confirm();

			var block = UnityEditor.Selection.activeGameObject;
			_objects.Add( block );
			Assert.That( Quaternion.Angle( block.transform.rotation, Workplane.Rotation ), Is.LessThan( 0.1f ), "turned with the workplane" );
			Assert.That( Vector3.Distance( block.transform.position, Workplane.ToWorld( new Vector3( 0, 0.5f, 0 ) ) ), Is.LessThan( 0.001f ), "sitting on it" );

			Workplane.Reset();
			Assert.That( HammerSettings.SnapWorld( new Vector3( step * 0.4f, 0, 0 ) ).x, Is.EqualTo( 0 ).Within( 1e-5f ), "back to the world grid" );
		}

		// ── History and sets ──

		[Test]
		public void CommandHistoryRepeatsPickedCommandsNTimes()
		{
			var c = Box();
			_tool.ClearHistory();
			var count = 0;
			_tool.Remember( "One", () => count += 1 );
			_tool.Remember( "Ten", () => count += 10 );
			Assert.That( _tool.History.Select( h => h.Name ), Is.EqualTo( new[] { "One", "Ten" } ) );

			_tool.RepeatCommands( new[] { 0, 1 }, 3 );
			Assert.That( count, Is.EqualTo( 33 ) );
			_ = c;
		}

		[Test]
		public void SelectionSetsSelectAndHide()
		{
			var a = Box();
			var b = Box( 64, Vector3.right * 3 );
			UnityEditor.Selection.objects = new Object[] { a.gameObject, b.gameObject };
			HammerSelectionSetsWindow.CreateFromSelection();
			var sets = HammerSelectionSets.Get( false );
			_objects.Add( sets.gameObject );
			Assert.That( sets.Sets.Count, Is.EqualTo( 1 ) );
			Assert.That( sets.Sets[0].Objects, Is.EquivalentTo( new[] { a.gameObject, b.gameObject } ) );
			Assert.That( sets.gameObject.CompareTag( "EditorOnly" ), "never in a build" );
			EditorWindow.GetWindow<HammerSelectionSetsWindow>().Close();
		}

		// ── Instances, projection, paths ──

		[Test]
		public void InstancesStayTheSame()
		{
			var a = Box();
			var b = Box( 64, Vector3.right * 4 );
			b.transform.rotation = Quaternion.Euler( 0, 45, 0 );
			HammerInstances.MakeInstances( new[] { a, b }, a );
			Assert.That( b.Mesh.FaceHandles.Count(), Is.EqualTo( 6 ) );
			Assert.That( b.GetComponent<MeshRenderer>().bounds.size.y, Is.EqualTo( 128 * SourceSpace.UnitScale ).Within( 0.01f ), "took the master's shape" );

			// Edit one: the other follows, keeping its own place and turn
			SelectObjects( a );
			_tool.Mode = EditMode.Face;
			_tool.Selection.Clear();
			_tool.Selection.Add( Facing( a, new S.Vector3( 0, 0, 1 ) ) );
			HammerSettings.GridSize = 16;
			_tool.ExtrudeFaces();
			Assert.That( b.Mesh.FaceHandles.Count(), Is.EqualTo( 10 ), "the copy got the extrude" );
			Assert.That( Quaternion.Angle( b.transform.rotation, Quaternion.Euler( 0, 45, 0 ) ), Is.LessThan( 0.01f ) );

			HammerInstances.Collapse( new[] { b } );
			_tool.ExtrudeFaces();
			Assert.That( b.Mesh.FaceHandles.Count(), Is.EqualTo( 10 ), "collapsed: no longer follows" );
		}

		[Test]
		public void MeshProjectionDropsOntoTheGround()
		{
			// A flat quad over a bumpy floor (two blocks of different heights)
			var low = Make( new BlockPrimitive(), new S.Vector3( 128, 256, 16 ), new Vector3( 0, -8 * SourceSpace.UnitScale, -64 * SourceSpace.UnitScale ) );
			var high = Make( new BlockPrimitive(), new S.Vector3( 128, 256, 48 ), new Vector3( 0, 8 * SourceSpace.UnitScale, 64 * SourceSpace.UnitScale ) );
			var sheet = Make( new QuadPrimitive(), new S.Vector3( 256, 256, 0 ), new Vector3( 0, 200 * SourceSpace.UnitScale, 0 ) );
			for ( int i = 0; i < 2; i++ ) { SelectObjects( sheet ); _tool.Subdivide(); }

			SelectObjects( sheet );
			MeshProjectionTool.ProjectDirection = MeshProjectionTool.Direction.Down;
			MeshProjectionTool.Offset = 0;
			MeshProjectionTool.Open( _tool );
			_tool.SubTool.Apply();

			var heights = sheet.Mesh.VertexHandles.Select( v => Mathf.Round( SourceSpace.ToSourcePosition( sheet.SourceToWorld( sheet.Mesh.GetVertexPosition( v ) ) ).z ) ).Distinct().OrderBy( h => h ).ToList();
			Assert.That( heights, Is.SupersetOf( new[] { 0f, 32f } ), "points landed on both blocks' tops" );
			Assert.That( heights.Max(), Is.LessThan( 100 ), "nothing left floating" );
			_ = low; _ = high;
		}

		[Test]
		public void PathToolBuildsARibbonAndAPipe()
		{
			foreach ( var shape in new[] { PathTool.Profile.Ribbon, PathTool.Profile.Rail, PathTool.Profile.Pipe } )
			{
				PathTool.Open( _tool );
				var path = (PathTool)_tool.SubTool;
				PathTool.Shape = shape;
				PathTool.Smoothness = 4;
				path.Points.AddRange( new[] { Vector3.zero, new Vector3( 20, 0, 0 ), new Vector3( 20, 0, 20 ) } );
				path.Apply();

				var made = UnityEditor.Selection.activeGameObject.GetComponent<HammerMesh>();
				_objects.Add( made.gameObject );
				var steps = 2 * 4 + 1;
				var expected = shape switch
				{
					PathTool.Profile.Ribbon => (steps - 1),
					PathTool.Profile.Rail => (steps - 1) * 4 + 2,
					_ => (steps - 1) * PathTool.Sides + 2,
				};
				Assert.That( made.Mesh.FaceHandles.Count(), Is.EqualTo( expected ), $"{shape} faces" );
				Assert.That( made.Mesh.FindBadFaces( includeNonPlanar: false ), Is.Empty, $"{shape} sound" );
				if ( shape != PathTool.Profile.Ribbon )
					Assert.That( made.Mesh.HalfEdgeHandles.Count( h => made.Mesh.IsEdgeOpen( h ) ), Is.Zero, $"{shape} closed" );
				else
				{
					// Faces the sky
					made.Mesh.ComputeFaceNormal( made.Mesh.FaceHandles.First(), out var n );
					Assert.That( Vector3.Dot( made.SourceDirectionToWorld( n ), Vector3.up ), Is.GreaterThan( 0.9f ) );
				}
			}
		}

		// ── Meshes ──

		[Test]
		public void PivotStepsAndSetOriginToPivot()
		{
			var c = Box( 128, new Vector3( 2, 3, 4 ) );
			SelectObjects( c );
			var before = c.Mesh.VertexHandles.Select( v => c.SourceToWorld( c.Mesh.GetVertexPosition( v ) ) ).ToList();

			_tool.StepPivot( 1 ); // the middle of the bottom
			_tool.SetOriginToPivot();

			var bottom = new Vector3( 2, 3 - 64 * SourceSpace.UnitScale, 4 );
			Assert.That( Vector3.Distance( c.transform.position, bottom ), Is.LessThan( 0.001f ), "origin at the bottom middle" );
			var after = c.Mesh.VertexHandles.Select( v => c.SourceToWorld( c.Mesh.GetVertexPosition( v ) ) ).ToList();
			for ( int i = 0; i < before.Count; i++ )
				Assert.That( Vector3.Distance( before[i], after[i] ), Is.LessThan( 0.001f ), "the geometry didn't move" );

			_tool.PivotToWorldOrigin();
			_tool.SetOriginToPivot();
			Assert.That( c.transform.position.magnitude, Is.LessThan( 0.001f ) );
		}

		[Test]
		public void FreezeTransformKeepsTheShape()
		{
			var c = Box();
			c.transform.rotation = Quaternion.Euler( 0, 30, 10 );
			c.transform.localScale = new Vector3( 1, 2, 1 );
			c.Commit();
			var before = c.GetComponent<MeshRenderer>().bounds;

			SelectObjects( c );
			_tool.FreezeTransform();

			Assert.That( c.transform.rotation, Is.EqualTo( Quaternion.identity ) );
			Assert.That( c.transform.localScale, Is.EqualTo( Vector3.one ) );
			var after = c.GetComponent<MeshRenderer>().bounds;
			Assert.That( Vector3.Distance( before.center, after.center ), Is.LessThan( 0.001f ) );
			Assert.That( Vector3.Distance( before.size, after.size ), Is.LessThan( 0.001f ), "looks the same" );

			c.transform.rotation = Quaternion.Euler( 10, 20, 30 );
			_tool.ClearRotationAndScale();
			Assert.That( c.transform.rotation, Is.EqualTo( Quaternion.identity ) );
		}

		[Test]
		public void MergeByEdgeWeldsWhereTheyTouch()
		{
			// Two open-topped... two quads side by side sharing an edge position
			var a = Make( new QuadPrimitive(), new S.Vector3( 64, 64, 0 ) );
			var b = Make( new QuadPrimitive(), new S.Vector3( 64, 64, 0 ), new Vector3( -64 * SourceSpace.UnitScale, 0, 0 ) );
			SelectObjects( a, b );
			_tool.MergeMeshesByEdge();

			Assert.That( b == null );
			Assert.That( a.Mesh.VertexHandles.Count(), Is.EqualTo( 6 ), "the shared edge welded" );
			Assert.That( a.Mesh.HalfEdgeHandles.Count( h => a.Mesh.IsEdgeOpen( h ) ), Is.EqualTo( 6 * 2 ), "only the outside is open (both halves of its 6 edges)" );
		}
	}
}
