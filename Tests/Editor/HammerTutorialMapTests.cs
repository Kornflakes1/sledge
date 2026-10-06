using System.Collections.Generic;
using System.Linq;
using HammerUnity.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.Tests
{
	/// <summary>
	/// Builds the tutorial map through the tool's own operations and checks every piece came out
	/// right: a broad "does everything still work" test.
	/// </summary>
	public class HammerTutorialMapTests
	{
		GameObject _root;
		List<HammerTutorialMap.Station> _stations;

		[OneTimeSetUp]
		public void Build()
		{
			_root = HammerTutorialMap.Build( out _stations );
		}

		[OneTimeTearDown]
		public void TearDown()
		{
			if ( _root != null ) Object.DestroyImmediate( _root );
			Selection.objects = new Object[0];
		}

		HammerMesh Mesh( string station ) => _stations.First( s => s.Name == station ).Mesh;

		int Faces( string station ) => Mesh( station ).Mesh.FaceHandles.Count();

		[Test]
		public void EveryStationIsBuilt()
		{
			string[] expected =
			{
				"Box", "Cylinder", "Sphere", "Stairs", "Spike", "Arch", "Quad", "Polygon",
				"Union", "Intersect", "Subtract (Bowl)", "Round Hole", "Doorway", "Arched Doorway", "Window (Boolean)", "Window (Inset)",
				"Inset", "Extrude", "Thicken", "Quad Slice", "Collapse Face", "Delete Faces", "Flip Faces", "Hidden Faces",
				"Bevel", "Chamfer", "Connect Edges", "Collapse Edges", "Bevel Vertex", "Merge Vertices", "Bridge", "Edge Arch",
				"Align to Grid", "Align to Face", "Rotate Texture", "Scale Texture", "Fit Texture", "Paint Faces", "Reflectivity", "Measure Colours",
				"Clip", "Mirror", "Move Vertices", "Extract Faces", "Merge Meshes", "Separate", "Move to Furthest", "Radial Align",
				"Hut", "Staircase", "Colonnade", "Arched Corridor", "Crates", "Window Wall", "Sculpted Ground",

				// The chapters that follow the docs
				"Look Around", "Fly Mode", "Orbit", "Frame Selection", "Alt Zoom and Pan", "Active View", "2D Views", "Maximise View", "Lighting Modes",
				"Selection Tool", "Translate", "Rotate", "Scale", "Pivot Tool", "Block Tool", "Clipping Tool", "Mirror Tool", "Displacement Tool", "Path Tool", "Mesh Projection Tool", "Command History",
				"Vertex", "Edge", "Face", "Mesh", "Faces Can Go", "Concave Is Fine", "Bent Faces", "Shift Extrude", "Ring and Connect", "Rotate and Scale",
				"Bridge: Block Out", "Bridge: Arch Sewn In", "Bridge: Walls", "Select Between", "Fill Hole", "Cut Tool",
				"Tunnel: Template", "Tunnel: Walls and Floor", "Tunnel: Curve (Shift+G)", "Tunnel: Pinch", "Tunnel: Copies",
				"Local Axes", "Workplane", "Clip: Keep One Side", "Clip: Keep Both", "Clip: Faces", "Mirror: Same Mesh", "Bevel: Rounded", "Bevel: Chamfer",
				"Texture Lock", "Texture Scale Lock", "UV Peel",
				"Room: Block", "Room: Flipped", "Room: Light and Spawn", "Room: Doorway (Clip)", "Room: Doorway (Cut Tool)",
				"Instances", "Prefabs", "Selection Sets", "Walls Block Sight", "Seen Through", "Hide While Working",
			};

			foreach ( var name in expected )
				Assert.That( _stations.Any( s => s.Name == name && s.Mesh != null ), $"{name} is missing" );
		}

		[Test]
		public void EveryMeshIsSound()
		{
			foreach ( var component in _root.GetComponentsInChildren<HammerMesh>() )
			{
				var mesh = component.Mesh;
				Assert.That( mesh.FaceHandles.Count(), Is.GreaterThan( 0 ), $"{component.name} has no faces" );

				foreach ( var f in mesh.FaceHandles )
					Assert.That( mesh.GetFaceVertices( f ).Length, Is.GreaterThanOrEqualTo( 3 ), $"{component.name} has a face with fewer than 3 corners" );

				foreach ( var v in mesh.VertexHandles )
				{
					var p = mesh.GetVertexPosition( v );
					Assert.That( float.IsFinite( p.x ) && float.IsFinite( p.y ) && float.IsFinite( p.z ), $"{component.name} has a broken vertex" );
				}

				var bad = mesh.FindBadFaces();
				Assert.That( bad, Is.Empty, $"{component.name} has broken faces: {string.Join( ", ", bad.Values )}" );

				var unity = component.GetComponent<MeshFilter>().sharedMesh;
				Assert.That( unity != null && unity.triangles.Length > 0, $"{component.name} didn't bake" );
			}
		}

		[Test]
		public void ShapesHaveTheirFaces()
		{
			Assert.That( Faces( "Box" ), Is.EqualTo( 6 ) );
			Assert.That( Faces( "Cylinder" ), Is.EqualTo( 16 + 2 ) );
			Assert.That( Faces( "Quad" ), Is.EqualTo( 1 ) );
			Assert.That( Faces( "Polygon" ), Is.EqualTo( 6 + 2 ) );
			Assert.That( Faces( "Stairs" ), Is.GreaterThan( 8 ) );
		}

		[Test]
		public void OperationsChangedTheirMeshes()
		{
			Assert.That( Faces( "Window (Boolean)" ), Is.GreaterThan( 6 ), "boolean subtract cut a hole" );
			Assert.That( Faces( "Window (Inset)" ), Is.GreaterThan( 6 + 4 * 2 ), "inset + extrude + inset" );
			Assert.That( Faces( "Bevel" ), Is.GreaterThan( 6 + 4 ), "bevel added faces" );
			Assert.That( Faces( "Extrude" ), Is.EqualTo( 6 + 4 + 4 + 4 ), "inset then two extrudes" );
			Assert.That( Faces( "Thicken" ), Is.EqualTo( 6 ), "a quad thickened into a slab" );
			Assert.That( Faces( "Quad Slice" ), Is.EqualTo( 5 + 9 ), "one face cut 3 x 3" );
			Assert.That( Mesh( "Edge Arch" ).Mesh.VertexHandles.Count(), Is.GreaterThan( 4 ), "the arched edge became a curve" );

			var bridge = Mesh( "Bridge" ).Mesh;
			Assert.That( bridge.HalfEdgeHandles.Count( he => bridge.IsEdgeOpen( he ) ), Is.EqualTo( 0 ), "bridge closed both holes" );

			var hidden = Mesh( "Hidden Faces" ).Mesh;
			Assert.That( hidden.FaceHandles.Count( f => hidden.IsFaceHidden( f ) ), Is.EqualTo( 1 ) );

			Assert.That( Faces( "Union" ), Is.GreaterThan( 6 ), "union joined the post" );
			Assert.That( Faces( "Intersect" ), Is.GreaterThan( 6 ), "intersect rounded the box" );
			Assert.That( Faces( "Subtract (Bowl)" ), Is.GreaterThan( 6 ), "subtract sank a bowl" );
			Assert.That( Faces( "Inset" ), Is.EqualTo( 6 + 4 ) );
			Assert.That( Faces( "Collapse Face" ), Is.EqualTo( 5 ), "a pyramid" );
			Assert.That( Faces( "Delete Faces" ), Is.EqualTo( 5 ) );
			Assert.That( Faces( "Flip Faces" ), Is.EqualTo( 5 ) );
			Assert.That( Faces( "Chamfer" ), Is.EqualTo( 6 + 4 ) );
			Assert.That( Faces( "Connect Edges" ), Is.EqualTo( 6 + 4 ), "a loop round the middle" );
			Assert.That( Mesh( "Collapse Edges" ).Mesh.VertexHandles.Count(), Is.EqualTo( 6 ), "a ridge" );
			Assert.That( Faces( "Bevel Vertex" ), Is.EqualTo( 7 ) );
			Assert.That( Mesh( "Merge Vertices" ).Mesh.VertexHandles.Count(), Is.EqualTo( 3 ), "a triangle" );
			Assert.That( Faces( "Reflectivity" ), Is.EqualTo( 5 + 9 ) );
			Assert.That( Faces( "Measure Colours" ), Is.EqualTo( 5 + 8 ) );
			Assert.That( Faces( "Merge Meshes" ), Is.EqualTo( 6 * 5 ), "a table in one mesh" );
			Assert.That( Faces( "Extract Faces" ), Is.EqualTo( 5 ) );
			Assert.That( Mesh( "Clip" ).Mesh.VertexHandles.Count(), Is.Not.EqualTo( 8 ), "clipped" );

			// Every face a different material
			var paint = Mesh( "Paint Faces" ).Mesh;
			Assert.That( paint.FaceHandles.Select( f => paint.GetFaceMaterial( f )?.Name ).Distinct().Count(), Is.EqualTo( 6 ) );
			var strips = Mesh( "Reflectivity" ).Mesh;
			Assert.That( strips.FaceHandles.Select( f => strips.GetFaceMaterial( f )?.Name ).Distinct().Count(), Is.EqualTo( 10 ), "9 strips and the plain wall" );
		}

		[Test]
		public void ChaptersDidTheirJob()
		{
			// The tunnel: walls and floor closed off underneath, the curve went round
			var walls = Mesh( "Tunnel: Walls and Floor" ).Mesh;
			Assert.That( walls.FaceHandles.Count(), Is.EqualTo( 16 + 2 + 1 ), "arch, two walls and a floor" );
			Assert.That( Faces( "Tunnel: Curve (Shift+G)" ), Is.EqualTo( 19 + 6 * 19 ), "six more segments" );

			// The rooms: flipped boxes, and two with a doorway
			Assert.That( Faces( "Room: Flipped" ), Is.EqualTo( 6 ) );
			Assert.That( Faces( "Room: Doorway (Clip)" ), Is.GreaterThan( 6 ), "the wall was cut" );
			Assert.That( Faces( "Room: Doorway (Cut Tool)" ), Is.EqualTo( 6 ), "the wall wraps round the doorway in one face" );
			Assert.That( Mesh( "Room: Doorway (Clip)" ).Mesh.HalfEdgeHandles.Any( h => Mesh( "Room: Doorway (Clip)" ).Mesh.IsEdgeOpen( h ) ), "a hole for the door" );
			Assert.That( Mesh( "Room: Doorway (Cut Tool)" ).Mesh.HalfEdgeHandles.Any( h => Mesh( "Room: Doorway (Cut Tool)" ).Mesh.IsEdgeOpen( h ) ), "a hole for the door" );

			Assert.That( Faces( "Command History" ), Is.EqualTo( 6 + 4 * 2 * 4 ), "four steps of two extrudes" );
			Assert.That( Faces( "Bridge: Walls" ), Is.GreaterThan( Faces( "Bridge: Arch Sewn In" ) ) );

			var pillars = _root.GetComponentsInChildren<HammerMesh>().Where( m => m.name is "Instances" or "Instance" ).ToList();
			Assert.That( pillars.Count, Is.EqualTo( 3 ) );
			Assert.That( pillars.Select( p => p.Mesh.FaceHandles.Count() ).Distinct().Single(), Is.GreaterThan( 6 ), "the bevel reached every instance" );
		}

		[Test]
		public void NewerToolsDidTheirJob()
		{
			var posts = _root.GetComponentsInChildren<HammerMesh>().Where( m => m.name.StartsWith( "Move to Furthest" ) ).ToList();
			Assert.That( posts.Count, Is.EqualTo( 3 ) );
			var tops = posts.Select( p => p.GetComponent<MeshRenderer>().bounds.max.y ).ToList();
			Assert.That( tops.Max() - tops.Min(), Is.LessThan( 0.001f ), "post tops level" );

			Assert.That( Faces( "Radial Align" ), Is.EqualTo( 8 + 2 ), "an octagon slab" );

			var ground = Mesh( "Sculpted Ground" );
			Assert.That( ground.Mesh.FaceHandles.Count(), Is.EqualTo( 256 ), "subdivided 4 times" );
			var hill = ground.GetComponent<MeshRenderer>().bounds;
			Assert.That( hill.size.y, Is.GreaterThan( 20 * SourceSpace.UnitScale ), "sculpted into a hill" );

			var crate = _root.GetComponentsInChildren<HammerMesh>().First( m => m.name == "Dropped Crate" ).GetComponent<MeshRenderer>().bounds;
			Assert.That( crate.min.y, Is.GreaterThan( hill.min.y + 0.05f ).And.LessThan( hill.max.y + 0.01f ), "the crate landed on the hill" );
		}

		[Test]
		public void UtilitiesMadeTheirObjects()
		{
			var names = _root.GetComponentsInChildren<HammerMesh>().Select( m => m.name ).ToList();
			Assert.That( names, Has.Member( "Mirror (Copy)" ) );
			Assert.That( names, Has.Member( "Extracted Face" ) );
			Assert.That( names, Has.Member( "Separate (Piece)" ) );
			Assert.That( names.Count( n => n == "Table Leg" ), Is.Zero, "legs merged into the table" );
			Assert.That( names.Count( n => n == "Column Base" || n == "Column Cap" ), Is.Zero, "columns merged" );
		}

		[Test]
		public void StationsDontOverlap()
		{
			for ( int i = 0; i < _stations.Count; i++ )
			{
				for ( int j = i + 1; j < _stations.Count; j++ )
				{
					var a = _stations[i].Mesh.GetComponent<MeshRenderer>().bounds;
					var b = _stations[j].Mesh.GetComponent<MeshRenderer>().bounds;
					a.Expand( -0.05f );
					b.Expand( -0.05f );
					Assert.That( a.Intersects( b ), Is.False, $"{_stations[i].Name} overlaps {_stations[j].Name}" );
				}
			}
		}

		[Test]
		public void StationsAreInsideTheBuilding()
		{
			var floors = _root.GetComponentsInChildren<HammerMesh>().Where( m => m.name is "Floor" or "Yard Floor" ).Select( m => m.GetComponent<MeshRenderer>().bounds ).ToList();
			Assert.That( floors.Count, Is.EqualTo( 2 ) );
			foreach ( var s in _stations )
			{
				var c = s.Mesh.GetComponent<MeshRenderer>().bounds.center;
				Assert.That( floors.Any( f => c.x > f.min.x && c.x < f.max.x && c.z > f.min.z && c.z < f.max.z ), $"{s.Name} is off the floor" );
			}
		}

		/// <summary>
		/// Shoot a ray through the middle of an opening (along the wall's thickness) at a height:
		/// it must go through without hitting the wall.
		/// </summary>
		bool SeeThrough( string station, float heightAboveCenter )
		{
			var mesh = Mesh( station );
			var b = mesh.GetComponent<MeshRenderer>().bounds;
			// Walls are thin along their own forward axis (s&box X = Unity Z)
			var thin = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
			var origin = b.center + Vector3.up * heightAboveCenter - thin * 5;
			var hitFront = MeshPicking.RaycastFace( new Ray( origin, thin ), mesh, out _ );
			var hitBack = MeshPicking.RaycastFace( new Ray( b.center + Vector3.up * heightAboveCenter + thin * 5, -thin ), mesh, out _ );
			return !hitFront && !hitBack;
		}

		[Test]
		public void OpeningsAreOpen()
		{
			Assert.That( SeeThrough( "Doorway", -0.6f ), "doorway has a hole" );
			Assert.That( SeeThrough( "Arched Doorway", -0.6f ), "arched doorway has a hole" );
			Assert.That( SeeThrough( "Window (Boolean)", 0.4f ), "boolean window has a hole" );
			Assert.That( SeeThrough( "Round Hole", 0 ), "round hole goes through" );
		}

		[Test]
		public void SignsReadFromBothSides()
		{
			var signs = _root.GetComponentsInChildren<TextMesh>().Where( t => t.name != "Back" ).ToList();
			Assert.That( signs.Count, Is.GreaterThan( _stations.Count ) );
			foreach ( var sign in signs )
			{
				var back = sign.transform.Find( "Back" );
				Assert.That( back != null && Vector3.Dot( back.forward, sign.transform.forward ) < -0.99f, $"{sign.name} has no back" );
			}
		}

		[Test]
		public void TheCutterWasUsedUp()
		{
			foreach ( var cutter in new[] { "Window Cutter", "Union Post", "Intersect Sphere", "Bowl Cutter", "Round Hole Cutter", "Window Wall Cutter" } )
				Assert.That( _root.GetComponentsInChildren<HammerMesh>().Any( m => m.name == cutter ), Is.False, $"{cutter} left behind" );
			Assert.That( _root.GetComponentsInChildren<HammerMesh>().Any( m => m.name == "Bridge B" ), Is.False );
		}
	}
}
