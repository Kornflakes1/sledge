using System.Collections.Generic;
using System.Linq;
using Sandbox.Primitives;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Builds the tutorial: one path through a row of rooms that follows Valve's Hammer docs in
	/// order (Navigation, Hammer Overview, Mesh Editing 1 to 4, Mesh Texturing, Creating Your First
	/// Room, Prefabs and Instances, Visibility), each room a chapter, then a yard of example
	/// builds. Every piece is made with the tool's own operations and labelled with how it was
	/// done. The tests build the same map to check everything still works.
	/// </summary>
	public static partial class HammerTutorialMap
	{
		const string MaterialFolder = "Packages/com.hammerunity.meshtools/Runtime/DevTextures/";
		const string Folder = "Assets/Hammer Tutorial";

		/// <summary>
		/// One built piece: its mesh and the sign text explaining it.
		/// </summary>
		public sealed class Station
		{
			public string Name;
			public string How;
			public string Room;
			public HammerMesh Mesh;
		}

		[MenuItem( "Tools/Hammer/Build Tutorial Map", false, 40 )]
		static void BuildMenu()
		{
			if ( !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() )
				return;

			// Never write over a tutorial scene someone has been working in without asking
			var scenePath = $"{Folder}/Hammer Tutorial.unity";
			if ( AssetDatabase.LoadAssetAtPath<SceneAsset>( scenePath ) != null &&
				!EditorUtility.DisplayDialog( "Build Tutorial Map", $"{scenePath} already exists. Rebuild it? Any changes made in it will be lost.", "Rebuild", "Cancel" ) )
				return;

			EditorSceneManager.NewScene( NewSceneSetup.DefaultGameObjects, NewSceneMode.Single );

			if ( !AssetDatabase.IsValidFolder( Folder ) ) AssetDatabase.CreateFolder( "Assets", "Hammer Tutorial" );
			Build( out _, saveAssets: true );

			EditorSceneManager.SaveScene( EditorSceneManager.GetActiveScene(), scenePath );

			HammerWindow.Open();
		}

		/// <summary>
		/// Build the map into the open scene. Returns the root object.
		/// </summary>
		/// <param name="saveAssets">Save the sign material into the project, so a saved scene keeps it.</param>
		public static GameObject Build( out List<Station> stations, bool saveAssets = false )
		{
			stations = new List<Station>();
			var root = new GameObject( "Hammer Tutorial" );
			Undo.RegisterCreatedObjectUndo( root, "Build Tutorial Map" );

			var tool = ScriptableObject.CreateInstance<HammerMeshTool>();
			var grid = HammerSettings.GridSize;
			var segments = HammerSettings.BevelSegments;
			var cuts = HammerSettings.QuadSliceCuts;
			var selection = UnityEditor.Selection.objects;

			try
			{
				HammerSettings.GridSize = 8;
				var context = new Context { Root = root, Tool = tool, Stations = stations, SignMaterial = SignMaterial( saveAssets ) };

				Entrance( context );

				// The docs, in order: walk straight through, one chapter per room
				var chapters = new (string Title, string Blurb, System.Action<Context> Fill)[]
				{
					("1. NAVIGATION", "Getting around the 3D and 2D views.", Navigation),
					("2. HAMMER OVERVIEW", "The tools down the left of the window, and the panes.", Overview),
					("2. OVERVIEW: SHAPES", "Everything the Block tool makes (Geometry Type in Tool Properties), plus the Polygon tool.", Shapes),
					("3. MESH EDITING 1", "Building blocks: meshes are faces, edges and vertices. Move them, extrude them, add loops.", MeshEditing1),
					("3. MESH EDITING 1: FACES", "Faces mode (3): click faces, Shift to add, Ctrl to take away.", Faces),
					("3. MESH EDITING 1: EDGES", "Edges mode (2) and Vertices mode (1). Double-click an edge to select its loop, G for its ring.", Edges),
					("4. MESH EDITING 2: BRIDGE", "An arched bridge, step by step, and the selection tricks it uses.", MeshEditing2),
					("4. MESH EDITING 2: OPENINGS", "Doors, windows and holes: shapes with openings, insets, and Boolean cuts.", Openings),
					("5. MESH EDITING 3: TUNNEL", "A tunnel, step by step: pivot points, Shift+G repeats and copies.", MeshEditing3),
					("6. MESH EDITING 4", "Local axes, the workplane, and the clipping, mirror and bevel tools.", MeshEditing4),
					("6. MESH EDITING 4: MORE", "Meshes mode (4): merge, separate, extract and line things up.", Utilities),
					("7. MESH TEXTURING", "Faces mode: pick a material (Shift+RMB), paint it (Ctrl+RMB), line it up with Fast Texture (Ctrl+G).", c => { Materials( c ); TexturingMore( c ); }),
					("8. YOUR FIRST ROOM", "A room in five steps: a block, flipped inside out, lit, then a doorway two ways.", FirstRoom),
					("9. PREFABS & 10. VISIBILITY", "Repeated pieces kept in step, and what blocks the player's view.", PrefabsAndVisibility),
				};

				for ( int i = 0; i < chapters.Length; i++ )
					Room( context, i, chapters.Length, chapters[i].Title, chapters[i].Blurb, chapters[i].Fill );

				Yard( context );

				tool.Selection.Clear();
				tool.Mode = EditMode.Object;
			}
			finally
			{
				HammerSettings.GridSize = grid;
				HammerSettings.BevelSegments = segments;
				HammerSettings.QuadSliceCuts = cuts;
				UnityEditor.Selection.objects = selection;
				Object.DestroyImmediate( tool );
			}

			return root;
		}

		/// <summary>
		/// What's being built and where: pieces are placed in a frame (an origin and a forward
		/// direction), so each room can be filled the same way whichever way it faces.
		/// </summary>
		sealed class Context
		{
			public GameObject Root;
			public HammerMeshTool Tool;
			public List<Station> Stations;
			public Material SignMaterial;
			public string Room;

			public S.Vector3 Origin;
			public S.Vector3 Forward = new( 1, 0, 0 );
			public S.Vector3 Left => new( -Forward.y, Forward.x, 0 );

			public S.Vector3 ToWorld( S.Vector3 p ) => Origin + Forward * p.x + Left * p.y + new S.Vector3( 0, 0, p.z );
			public Quaternion Rotation => Quaternion.LookRotation( SourceSpace.ToUnityDirection( Forward ), Vector3.up );

			public void Frame( string room, S.Vector3 origin, S.Vector3 forward )
			{
				Room = room;
				Origin = origin;
				Forward = forward;
			}
		}

		// ── Layout (s&box units: X forward, Y left, Z up; 1 unit = 1 inch) ──
		//
		// One straight path along X from the entrance: a row of rooms, each a chapter, joined by
		// doorways in their front and back walls. The last one opens into a walled yard.

		const float Wall = 16;
		const float WallHeight = 320;
		const float RoomInner = 1024;
		const float RoomOuter = RoomInner + Wall * 2;
		const float YardInner = 2048;
		const float Porch = 384;
		const string WallMaterial = "Dev Measure Light";
		const string FloorMaterial = "Dev Measure Grey";

		/// <summary>Where the rooms end and the yard starts.</summary>
		static float PathLength;

		static void Entrance( Context c )
		{
			c.Frame( "Entrance", S.Vector3.Zero, new S.Vector3( 1, 0, 0 ) );
			Sign( c, "HAMMER TUTORIAL\nWalk straight through: each room is a chapter of Valve's Hammer docs, in order. Example builds at the end.\nOpen Window > Hammer (Ctrl+Shift+H) and click anything.",
				new S.Vector3( -Porch / 2, 0, 190 ), 1.3f );
		}

		/// <summary>
		/// A chapter room on the path: front and back walls with doorways (in from the last room,
		/// on to the next), solid sides, the chapter's name over the way in and what it covers
		/// just inside, then its stations.
		/// </summary>
		static void Room( Context c, int index, int count, string title, string blurb, System.Action<Context> fill )
		{
			PathLength = RoomOuter * count;
			if ( index == 0 )
			{
				// One floor under the porch and every room
				c.Frame( "Entrance", S.Vector3.Zero, new S.Vector3( 1, 0, 0 ) );
				Block( c, "Floor", new S.Vector3( (PathLength - Porch) / 2, 0, -8 ), new S.Vector3( PathLength + Porch, RoomOuter, 16 ), FloorMaterial );
			}

			var center = new S.Vector3( RoomOuter * (index + 0.5f), 0, 0 );
			c.Frame( title, center, new S.Vector3( 1, 0, 0 ) );

			const float half = RoomOuter / 2;
			var door = new DoorwayPrimitive { DoorWidth = 128, DoorHeight = 176, ArchHeight = 48, ArchSegments = 10, AlignToCamera = false };
			Primitive( c, $"{Pretty( title )} Wall (In)", door, new S.Vector3( -half + Wall / 2, 0, WallHeight / 2 ), new S.Vector3( Wall, RoomOuter, WallHeight ), WallMaterial );
			Primitive( c, $"{Pretty( title )} Wall (Out)", door, new S.Vector3( half - Wall / 2, 0, WallHeight / 2 ), new S.Vector3( Wall, RoomOuter, WallHeight ), WallMaterial );
			Block( c, $"{Pretty( title )} Wall (Side)", new S.Vector3( 0, half - Wall / 2, WallHeight / 2 ), new S.Vector3( RoomInner, Wall, WallHeight ), WallMaterial );
			Block( c, $"{Pretty( title )} Wall (Side)", new S.Vector3( 0, -half + Wall / 2, WallHeight / 2 ), new S.Vector3( RoomInner, Wall, WallHeight ), WallMaterial );

			// Over the way in (seen from the room before), and just inside
			Sign( c, title, c.ToWorld( new S.Vector3( -half - 12, 0, 236 ) ), 1.5f );
			Sign( c, $"{title}\n{blurb}", c.ToWorld( new S.Vector3( -half + 110, 0, 205 ) ), 0.8f );

			fill( c );
		}

		/// <summary>
		/// Where station <paramref name="i"/> (0-11) stands in a room: three rows of four, the
		/// first row nearest the way in, numbered left to right as you walk in. The middle of each
		/// row is left clear for walking through.
		/// </summary>
		static S.Vector3 Slot( int i ) => new( -300 + (i / 4) * 300, 360 - (i % 4) * 240, 0 );

		static string Pretty( string title ) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase( title.ToLowerInvariant() );

		// ── Shapes ──

		static void Shapes( Context c )
		{
			const string m = "Dev Measure Orange";
			var size = new S.Vector3( 128, 128, 128 );
			S.Vector3 At( int i, float z = 64 ) => Slot( i ) + new S.Vector3( 0, 0, z );

			AddStation( c, "Box", "Block tool (Shift+B): drag the base, release, move for the height, click.",
				Primitive( c, "Box", new BlockPrimitive(), At( 0 ), size, m ) );
			AddStation( c, "Cylinder", "Block tool (Shift+B), Geometry Type Cylinder. Sides is in Tool Properties.",
				Primitive( c, "Cylinder", new CylinderPrimitive { NumberOfSides = 16 }, At( 1 ), size, m ) );
			AddStation( c, "Sphere", "Block tool (Shift+B), Geometry Type Sphere.",
				Primitive( c, "Sphere", new SpherePrimitive(), At( 2 ), size, m ) );
			AddStation( c, "Stairs", "Block tool (Shift+B), Geometry Type Stairs. Steps is in Tool Properties.",
				Primitive( c, "Stairs", new StairsPrimitive { NumberOfSteps = 8, AlignToCamera = false }, At( 3 ), size, m ) );
			AddStation( c, "Spike", "Block tool (Shift+B), Geometry Type Spike.",
				Primitive( c, "Spike", new SpikePrimitive(), At( 4 ), size, m ) );
			AddStation( c, "Arch", "Block tool (Shift+B), Geometry Type Doorway with Arch Height set: an arch.",
				Primitive( c, "Arch", new DoorwayPrimitive { DoorWidth = 72, DoorHeight = 80, ArchHeight = 36, ArchSegments = 8, AlignToCamera = false }, At( 5, 80 ), new S.Vector3( 32, 160, 160 ), m ) );
			AddStation( c, "Quad", "Block tool (Shift+B), Geometry Type Quad: a single flat face.",
				Primitive( c, "Quad", new QuadPrimitive(), At( 6, 1 ), new S.Vector3( 128, 128, 0 ), m ) );

			// Polygon tool: an L-shaped prism, drawn with world points like the tool does
			var l = new[] { new S.Vector3( -64, -64, 0 ), new S.Vector3( 64, -64, 0 ), new S.Vector3( 64, -16, 0 ), new S.Vector3( -16, -16, 0 ), new S.Vector3( -16, 64, 0 ), new S.Vector3( -64, 64, 0 ) };
			var prism = PolygonTool.CreatePrism( l.Select( p => SourceSpace.ToUnityPosition( c.ToWorld( Slot( 7 ) + p ) ) ).ToList(), Vector3.up, 96 * SourceSpace.UnitScale, "Polygon" );
			Adopt( c, prism, m );
			AddStation( c, "Polygon", "Polygon tool (Shift+P): click the corners, click the first one again, set the height.", prism );
		}

		// ── Openings & booleans ──

		static void Openings( Context c )
		{
			const string m = "Dev Reflectivity 50";
			var wallSize = new S.Vector3( 16, 192, 192 );
			S.Vector3 At( int i, float z ) => Slot( i ) + new S.Vector3( 0, 0, z );

			// Solid pieces in the front row, walls behind them so they don't block the view
			AddStation( c, "Union", "Meshes mode: select both, Boolean > Union. One mesh, no faces inside.",
				Boolean( c, "Union", S.PolygonMesh.BooleanOperation.Union,
					Primitive( c, "Union", new BlockPrimitive(), At( 0, 48 ), new S.Vector3( 96, 96, 96 ), m ),
					Primitive( c, "Union Post", new CylinderPrimitive { NumberOfSides = 12 }, At( 0, 80 ), new S.Vector3( 48, 48, 160 ), m ) ) );

			AddStation( c, "Intersect", "Boolean > Intersect keeps only where they overlap: box and sphere make a rounded cube.",
				Boolean( c, "Intersect", S.PolygonMesh.BooleanOperation.Intersect,
					Primitive( c, "Intersect", new BlockPrimitive(), At( 1, 56 ), new S.Vector3( 112, 112, 112 ), m ),
					Primitive( c, "Intersect Sphere", new SpherePrimitive(), At( 1, 56 ), new S.Vector3( 144, 144, 144 ), m ) ) );

			AddStation( c, "Subtract (Bowl)", "Boolean > Subtract with a sphere sunk into the top of a block.",
				Boolean( c, "Subtract", S.PolygonMesh.BooleanOperation.Subtract,
					Primitive( c, "Subtract (Bowl)", new BlockPrimitive(), At( 2, 32 ), new S.Vector3( 128, 128, 64 ), m ),
					Primitive( c, "Bowl Cutter", new SpherePrimitive(), At( 2, 72 ), new S.Vector3( 96, 96, 96 ), m ) ) );

			AddStation( c, "Round Hole", "A cylinder cutter laid through a wall, Boolean > Subtract.",
				Boolean( c, "Subtract", S.PolygonMesh.BooleanOperation.Subtract,
					Primitive( c, "Round Hole", new BlockPrimitive(), At( 3, 64 ), new S.Vector3( 32, 128, 128 ), m ),
					Primitive( c, "Round Hole Cutter", new CylinderPrimitive { NumberOfSides = 16 }, At( 3, 64 ), new S.Vector3( 64, 64, 64 ), m, roll: 90 ) ) );

			AddStation( c, "Doorway", "Block tool (Shift+B), Geometry Type Doorway: a wall with a door-sized hole.",
				Primitive( c, "Doorway", new DoorwayPrimitive { DoorWidth = 64, DoorHeight = 128, AlignToCamera = false }, At( 4, 96 ), wallSize, m ) );

			AddStation( c, "Arched Doorway", "Doorway with Arch Height and Arch Segments set.",
				Primitive( c, "Arched Doorway", new DoorwayPrimitive { DoorWidth = 80, DoorHeight = 128, ArchHeight = 40, ArchSegments = 10, AlignToCamera = false }, At( 5, 96 ), wallSize, m ) );

			AddStation( c, "Window (Boolean)", "Meshes mode: select the wall, then the cutter box, Boolean > Subtract.",
				Boolean( c, "Subtract", S.PolygonMesh.BooleanOperation.Subtract,
					Primitive( c, "Window (Boolean)", new BlockPrimitive(), At( 6, 96 ), wallSize, m ),
					Primitive( c, "Window Cutter", new BlockPrimitive(), At( 6, 112 ), new S.Vector3( 64, 96, 64 ), m ) ) );

			// Inset + extrude: a window frame that sticks out of the wall
			{
				var wall = Primitive( c, "Window (Inset)", new BlockPrimitive(), At( 7, 96 ), wallSize, m );
				c.Tool.Mode = EditMode.Face;
				Select( c, FaceFacing( wall, new S.Vector3( -1, 0, 0 ) ) );
				HammerSettings.GridSize = 24;
				c.Tool.InsetFaces();
				HammerSettings.GridSize = 8;
				c.Tool.ExtrudeFaces();
				c.Tool.InsetFaces();
				AddStation( c, "Window (Inset)", "Faces mode: select the face, Inset (Shift+I), Extrude (Shift+drag), Inset again.", wall );
			}
		}

		// ── Faces ──

		static void Faces( Context c )
		{
			const string m = "Dev Measure Blue";
			S.Vector3 At( int i, float z = 64 ) => Slot( i ) + new S.Vector3( 0, 0, z );
			c.Tool.Mode = EditMode.Face;

			// Inset: a border inside the top face
			{
				var box = Primitive( c, "Inset", new BlockPrimitive(), At( 0, 32 ), new S.Vector3( 128, 128, 64 ), m );
				Select( c, FaceFacing( box, new S.Vector3( 0, 0, 1 ) ) );
				HammerSettings.GridSize = 24;
				c.Tool.InsetFaces();
				HammerSettings.GridSize = 8;
				AddStation( c, "Inset", "Select a face, Inset (Shift+I) and drag: a smaller face inside it, ready to extrude.", box );
			}

			// Extrude: pull the top face up twice
			{
				var box = Primitive( c, "Extrude", new BlockPrimitive(), At( 1, 32 ), new S.Vector3( 128, 128, 64 ), m );
				Select( c, FaceFacing( box, new S.Vector3( 0, 0, 1 ) ) );
				HammerSettings.GridSize = 32;
				c.Tool.InsetFaces();
				c.Tool.ExtrudeFaces();
				c.Tool.ExtrudeFaces();
				HammerSettings.GridSize = 8;
				AddStation( c, "Extrude", "Inset (Shift+I), then hold Shift while dragging the move handle.", box );
			}

			// Thicken: a flat quad made solid
			{
				var quad = Primitive( c, "Thicken", new QuadPrimitive(), At( 2, 1 ), new S.Vector3( 128, 128, 0 ), m );
				Select( c, quad.Mesh.FaceHandles.Select( f => (IMeshElement)new MeshFace( quad, f ) ) );
				HammerSettings.GridSize = 16;
				c.Tool.ThickenFaces();
				HammerSettings.GridSize = 8;
				AddStation( c, "Thicken", "Select a flat face, Thicken (G).", quad );
			}

			// Quad slice: a face cut into a 3 x 3 grid
			{
				var box = Primitive( c, "Quad Slice", new BlockPrimitive(), At( 3 ), new S.Vector3( 128, 128, 128 ), m );
				Select( c, FaceFacing( box, new S.Vector3( -1, 0, 0 ) ) );
				HammerSettings.QuadSliceCuts = new Vector2Int( 2, 2 );
				c.Tool.QuadSlice();
				AddStation( c, "Quad Slice", "Quad Slice (Ctrl+D). The cut counts are next to the button.", box );
			}

			// Collapse: the top face pulled to a point makes a pyramid
			{
				var box = Primitive( c, "Collapse Face", new BlockPrimitive(), At( 4 ), new S.Vector3( 128, 128, 128 ), m );
				Select( c, FaceFacing( box, new S.Vector3( 0, 0, 1 ) ) );
				c.Tool.Collapse();
				AddStation( c, "Collapse Face", "Collapse (Shift+O) shrinks a face to one point: a box becomes a pyramid.", box );
			}

			// Delete: an open-topped box
			{
				var box = Primitive( c, "Delete Faces", new BlockPrimitive(), At( 5, 48 ), new S.Vector3( 128, 128, 96 ), m );
				Select( c, FaceFacing( box, new S.Vector3( 0, 0, 1 ) ) );
				c.Tool.Delete();
				AddStation( c, "Delete Faces", "Delete removes faces and leaves a hole. Fill Hole (P) in Edges mode closes it again.", box );
			}

			// Flip: a box turned inside out with its front gone, like a little room
			{
				var box = Primitive( c, "Flip Faces", new BlockPrimitive(), At( 6 ), new S.Vector3( 128, 128, 128 ), m );
				Select( c, FaceFacing( box, new S.Vector3( -1, 0, 0 ) ) );
				c.Tool.Delete();
				Select( c, box.Mesh.FaceHandles.Select( f => (IMeshElement)new MeshFace( box, f ) ) );
				c.Tool.FlipFaces();
				AddStation( c, "Flip Faces", "Flip (F) turns faces round: the inside of a box becomes a room.", box );
			}

			// Hidden faces: see inside a box
			{
				var box = Primitive( c, "Hidden Faces", new BlockPrimitive(), At( 7 ), new S.Vector3( 128, 128, 128 ), m );
				Select( c, FaceFacing( box, new S.Vector3( -1, 0, 0 ) ) );
				c.Tool.HideFaces();
				AddStation( c, "Hidden Faces", "Hide (H) hides faces while you work, Unhide All (U) brings them back.", box );
			}

			c.Tool.Selection.Clear();
		}

		// ── Edges & vertices ──

		static void Edges( Context c )
		{
			const string m = "Dev Measure Green";
			S.Vector3 At( int i, float z = 64 ) => Slot( i ) + new S.Vector3( 0, 0, z );
			var cube = new S.Vector3( 128, 128, 128 );

			// Bevel: round over the top edges of a box
			{
				var box = Primitive( c, "Bevel", new BlockPrimitive(), At( 0 ), cube, m );
				c.Tool.Mode = EditMode.Edge;
				HammerSettings.BevelSegments = 3;
				HammerSettings.GridSize = 16;
				Select( c, box.Mesh.HalfEdgeHandles.Where( he => IsTopEdge( box, he ) ).Select( he => (IMeshElement)new MeshEdge( box, he ) ) );
				c.Tool.QuickBevelEdges();
				HammerSettings.GridSize = 8;
				AddStation( c, "Bevel", "Edges mode: select edges, Bevel (F). Segments is in Tool Properties.", box );
			}

			// Chamfer: a one-segment bevel on the upright edges
			{
				var box = Primitive( c, "Chamfer", new BlockPrimitive(), At( 1 ), cube, m );
				c.Tool.Mode = EditMode.Edge;
				HammerSettings.BevelSegments = 1;
				HammerSettings.GridSize = 24;
				Select( c, box.Mesh.HalfEdgeHandles.Where( he => IsUprightEdge( box, he ) ).Select( he => (IMeshElement)new MeshEdge( box, he ) ) );
				c.Tool.QuickBevelEdges();
				HammerSettings.GridSize = 8;
				AddStation( c, "Chamfer", "Bevel (F) with Segments at 1 cuts corners flat.", box );
			}

			// Connect: the four upright edges joined round the middle, then the new loop pushed out
			{
				var box = Primitive( c, "Connect Edges", new BlockPrimitive(), At( 2 ), cube, m );
				c.Tool.Mode = EditMode.Edge;
				Select( c, box.Mesh.HalfEdgeHandles.Where( he => IsUprightEdge( box, he ) ).Select( he => (IMeshElement)new MeshEdge( box, he ) ) );
				c.Tool.ConnectEdges();
				AddStation( c, "Connect Edges", "Select edges across faces, Connect (V): a new edge loop through their middles.", box );
			}

			// Collapse: the two top edges running front to back collapsed make a roof ridge
			{
				var box = Primitive( c, "Collapse Edges", new BlockPrimitive(), At( 3 ), cube, m );
				c.Tool.Mode = EditMode.Edge;
				var mesh = box.Mesh;
				Select( c, mesh.HalfEdgeHandles.Where( he =>
				{
					if ( !IsTopEdge( box, he ) ) return false;
					var line = mesh.GetEdgeLine( he );
					return System.Math.Abs( line.Start.x - line.End.x ) < 0.01f;
				} ).Select( he => (IMeshElement)new MeshEdge( box, he ) ) );
				c.Tool.Collapse();
				AddStation( c, "Collapse Edges", "Collapse (Shift+O) on edges merges each into a point: two top edges make a ridge.", box );
			}

			// Bevel vertex: one corner cut off
			{
				var box = Primitive( c, "Bevel Vertex", new BlockPrimitive(), At( 4 ), cube, m );
				c.Tool.Mode = EditMode.Vertex;
				HammerSettings.GridSize = 48;
				Select( c, new IMeshElement[] { TopCorner( box, -1, 1 ) } );
				c.Tool.BevelVertices();
				HammerSettings.GridSize = 8;
				AddStation( c, "Bevel Vertex", "Vertices mode: select a corner, Bevel (F) cuts it off.", box );
			}

			// Merge vertices: two corners of a flat square pulled together make a triangle (on a
			// solid, the faces around the merged point would bend)
			{
				var quad = Primitive( c, "Merge Vertices", new QuadPrimitive(), At( 5, 1 ), new S.Vector3( 128, 128, 0 ), m );
				c.Tool.Mode = EditMode.Vertex;
				Select( c, new IMeshElement[] { TopCorner( quad, 1, 1 ), TopCorner( quad, 1, -1 ) } );
				c.Tool.MergeVertices();
				AddStation( c, "Merge Vertices", "Vertices mode: select two or more, Merge (M) joins them at their middle.", quad );
			}

			// Bridge: two boxes merged, facing sides removed, the holes joined
			{
				var a = Primitive( c, "Bridge", new BlockPrimitive(), At( 6 ) + new S.Vector3( -96, 0, 0 ), new S.Vector3( 64, 64, 64 ), m );
				var b = Primitive( c, "Bridge B", new BlockPrimitive(), At( 6 ) + new S.Vector3( 96, 0, 0 ), new S.Vector3( 64, 64, 64 ), m );
				UnityEditor.Selection.objects = new Object[] { a.gameObject, b.gameObject };
				UnityEditor.Selection.activeGameObject = a.gameObject;
				c.Tool.MergeMeshes();

				c.Tool.Mode = EditMode.Face;
				var center = a.Mesh.CalculateBounds().Center;
				var facing = a.Mesh.FaceHandles.Where( f =>
				{
					a.Mesh.ComputeFaceNormal( f, out var n );
					var p = a.Mesh.GetFaceCenter( f );
					return System.Math.Abs( n.x ) > 0.9f && S.Vector3.Dot( n, center - p ) > 0;
				} ).Select( f => (IMeshElement)new MeshFace( a, f ) ).ToList();
				Select( c, facing );
				c.Tool.Delete();

				c.Tool.Mode = EditMode.Edge;
				Select( c, a.Mesh.HalfEdgeHandles.Where( he => a.Mesh.IsEdgeOpen( he ) ).Select( he => (IMeshElement)new MeshEdge( a, he ) ) );
				c.Tool.BridgeEdges();
				AddStation( c, "Bridge", "Delete facing faces, select both open loops in Edges mode, Bridge (B).", a );
			}

			// Edge arch: the edge of a flat quad bent into an arch
			{
				var wall = Primitive( c, "Edge Arch", new QuadPrimitive(), At( 7, 0 ) + new S.Vector3( 0, 0, 1 ), new S.Vector3( 160, 128, 0 ), m );
				c.Tool.Mode = EditMode.Edge;
				var open = wall.Mesh.HalfEdgeHandles.Where( he => wall.Mesh.IsEdgeOpen( he ) ).ToList();
				var far = open.OrderByDescending( he => wall.Mesh.GetEdgeLine( he ).Center.x ).First();
				Select( c, new IMeshElement[] { new MeshEdge( wall, far ) } );
				EdgeArchTool.Open( c.Tool );
				c.Tool.SubTool?.Apply();
				AddStation( c, "Edge Arch", "Select an open edge, Edge Arch (Y), drag for the height.", wall );
			}

			c.Tool.Selection.Clear();
			HammerSettings.BevelSegments = 1;
		}

		// ── Materials ──

		static readonly string[] MeasureColours = { "Blue", "Dark", "Green", "Grey", "Light", "Orange", "Red", "Yellow" };

		static void Materials( Context c )
		{
			S.Vector3 At( int i, float z = 56 ) => Slot( i ) + new S.Vector3( 0, 0, z );
			var cube = new S.Vector3( 112, 112, 112 );
			c.Tool.Mode = EditMode.Face;

			void All( HammerMesh box ) => Select( c, box.Mesh.FaceHandles.Select( f => (IMeshElement)new MeshFace( box, f ) ) );

			// Turned boxes: world-aligned textures run straight through, face-aligned ones turn with it
			{
				var box = Primitive( c, "Align to Grid", new BlockPrimitive(), At( 0 ), cube, "Dev Measure Grey", yaw: 30 );
				All( box );
				c.Tool.TextureAlignToGrid();
				AddStation( c, "Align to Grid", "Textures line up with the world grid, so seams match between objects. Fast Texture (Ctrl+G) > Grid.", box );
			}
			{
				var box = Primitive( c, "Align to Face", new BlockPrimitive(), At( 1 ), cube, "Dev Measure Grey", yaw: 30 );
				All( box );
				c.Tool.TextureAlignToFace();
				AddStation( c, "Align to Face", "Textures line up with each face's own edges instead. Fast Texture (Ctrl+G) > Face.", box );
			}
			{
				var box = Primitive( c, "Rotate Texture", new BlockPrimitive(), At( 2 ), cube, "Dev Measure Orange" );
				All( box );
				c.Tool.RotateTexture( 45 );
				AddStation( c, "Rotate Texture", "Alt+, and Alt+. turn the texture on the selected faces.", box );
			}
			{
				var box = Primitive( c, "Scale Texture", new BlockPrimitive(), At( 3 ), cube, "Dev Measure Orange" );
				All( box );
				c.Tool.ScaleTexture( 2 );
				AddStation( c, "Scale Texture", "Alt+[ and Alt+] scale the texture. Alt+arrows shift it a grid step.", box );
			}

			// Fit: one copy of the texture stretched over the whole face
			{
				var wall = Primitive( c, "Fit Texture", new BlockPrimitive(), At( 4, 80 ), new S.Vector3( 16, 192, 160 ), "Dev Measure Yellow" );
				Select( c, FaceFacing( wall, new S.Vector3( -1, 0, 0 ) ) );
				c.Tool.JustifyTexture( S.PolygonMesh.TextureJustification.Fit );
				AddStation( c, "Fit Texture", "Fast Texture (Ctrl+G) > Fit stretches one copy of the texture over the face.", wall );
			}

			// Paint: every face of a box a different colour
			{
				var box = Primitive( c, "Paint Faces", new BlockPrimitive(), At( 5 ), cube, "Dev Measure Grey" );
				var faces = box.Mesh.FaceHandles.ToList();
				for ( int i = 0; i < faces.Count; i++ )
				{
					Select( c, new IMeshElement[] { new MeshFace( box, faces[i] ) } );
					HammerSettings.ActiveMaterial = LoadMaterial( $"Dev Measure {MeasureColours[i % MeasureColours.Length]}" );
					c.Tool.ApplyMaterial();
				}
				HammerSettings.ActiveMaterial = null;
				AddStation( c, "Paint Faces", "Shift+RMB picks up a face's material, Ctrl+RMB paints it on. Shift+T applies it to the selection.", box );
			}

			// Swatch walls: a face sliced into strips, one material on each
			Swatches( c, "Reflectivity", At( 6, 64 ), Enumerable.Range( 1, 9 ).Select( i => $"Dev Reflectivity {i * 10}" ).ToArray(),
				"Dev Reflectivity 10 to 90: how much light a surface bounces, for checking lighting." );
			Swatches( c, "Measure Colours", At( 7, 64 ), MeasureColours.Select( x => $"Dev Measure {x}" ).ToArray(),
				"Dev Measure: grids marked every 16, 64 and 128 units, for judging sizes." );

			c.Tool.Selection.Clear();
		}

		/// <summary>
		/// A wall whose front is Quad Sliced into strips, each painted a different material.
		/// </summary>
		static void Swatches( Context c, string name, S.Vector3 center, string[] materials, string how )
		{
			var wall = Primitive( c, name, new BlockPrimitive(), center, new S.Vector3( 16, 216, 128 ), "Dev Measure Light" );
			c.Tool.Mode = EditMode.Face;

			// Quad Slice with no cuts the other way (the setting's minimum is 1, so straight on the mesh)
			var mesh = wall.Mesh;
			var front = ((MeshFace)FaceFacing( wall, new S.Vector3( -1, 0, 0 ) ).First()).Handle;
			mesh.QuadSliceFaces( new List<HalfEdgeMesh.FaceHandle> { front }, materials.Length - 1, 0, 60.0f, new List<HalfEdgeMesh.FaceHandle>() );
			Refresh( wall );

			// The strips, left to right as you face them
			var strips = mesh.FaceHandles.Where( f => { mesh.ComputeFaceNormal( f, out var n ); return n.x < -0.9f; } )
				.OrderByDescending( f => mesh.GetFaceCenter( f ).y ).ThenByDescending( f => mesh.GetFaceCenter( f ).z ).ToList();

			for ( int i = 0; i < strips.Count && i < materials.Length; i++ )
			{
				Select( c, new IMeshElement[] { new MeshFace( wall, strips[i] ) } );
				HammerSettings.ActiveMaterial = LoadMaterial( materials[i] );
				c.Tool.ApplyMaterial();
			}

			HammerSettings.ActiveMaterial = null;
			AddStation( c, name, how, wall );
		}

		// ── Utilities ──

		static void Utilities( Context c )
		{
			const string m = "Dev Measure Yellow";
			S.Vector3 At( int i, float z = 64 ) => Slot( i ) + new S.Vector3( 0, 0, z );
			var cube = new S.Vector3( 128, 128, 128 );

			// Clip: a box cut on a slant into a ramp
			{
				var box = Primitive( c, "Clip", new BlockPrimitive(), At( 0 ), cube, m );
				box.Mesh.ClipFacesByPlaneAndCap( box.Mesh.FaceHandles.ToList(), new S.Plane( S.Vector3.Zero, new S.Vector3( -1, 0, 1 ).Normal ), true, true );
				Refresh( box );
				AddStation( c, "Clip", "Clipping tool (Shift+X): drag a line across the shape. Shift+X again picks which side stays.", box );
			}

			// Mirror: a ramp and its mirror image make a roof
			{
				var profile = new[] { new S.Vector3( 0, -48, -48 ), new S.Vector3( 0, 48, -48 ), new S.Vector3( 0, -48, 48 ) };
				var ramp = Prism( c, "Mirror", At( 1, 48 ) + new S.Vector3( 0, 48, 0 ), profile, new S.Vector3( 128, 0, 0 ), m );

				var copyMesh = S.PolygonMesh.FromData( ramp.Mesh.ToData() );
				copyMesh.FlipAllFaces();
				copyMesh.Scale( new S.Vector3( 1, -1, 1 ) );
				var copy = Place( c, "Mirror (Copy)", copyMesh, At( 1, 48 ) + new S.Vector3( 0, -48, 0 ), m );
				AddStation( c, "Mirror", "Mirror tool (Shift+F): drag a line for the mirror plane, Enter keeps the copy.", ramp );
				_ = copy;
			}

			// Move vertices: two top corners lifted for a sloped top
			{
				var box = Primitive( c, "Move Vertices", new BlockPrimitive(), At( 2 ), cube, m );
				foreach ( var v in new[] { TopCorner( box, 1, 1 ), TopCorner( box, 1, -1 ) } )
					box.Mesh.SetVertexPosition( v.Handle, box.Mesh.GetVertexPosition( v.Handle ) + new S.Vector3( 0, 0, 48 ) );
				Refresh( box );
				AddStation( c, "Move Vertices", "Vertices mode: select corners and drag the move handle (T). Keep faces flat or they bend.", box );
			}

			// Extract: the front face taken off as its own object, pulled forward
			{
				var box = Primitive( c, "Extract Faces", new BlockPrimitive(), At( 3 ), cube, m );
				c.Tool.Mode = EditMode.Face;
				Select( c, FaceFacing( box, new S.Vector3( -1, 0, 0 ) ) );
				c.Tool.ExtractFaces();
				foreach ( var go in UnityEditor.Selection.gameObjects )
				{
					go.name = "Extracted Face";
					go.transform.position += c.Rotation * Vector3.back * (40 * SourceSpace.UnitScale);
					var extracted = go.GetComponent<HammerMesh>();
					Refresh( extracted );
				}
				c.Tool.Selection.Clear();
				AddStation( c, "Extract Faces", "Faces mode: Alt+N moves the selected faces into a new object.", box );
			}

			// Merge meshes: a table from five blocks
			{
				var top = Primitive( c, "Merge Meshes", new BlockPrimitive(), At( 4, 69 ), new S.Vector3( 128, 96, 6 ), m );
				var legs = new List<Object> { top.gameObject };
				foreach ( var (x, y) in new[] { (-56, -40), (-56, 40), (56, -40), (56, 40) } )
					legs.Add( Primitive( c, "Table Leg", new BlockPrimitive(), At( 4, 33 ) + new S.Vector3( x, y, 0 ), new S.Vector3( 8, 8, 66 ), m ).gameObject );
				UnityEditor.Selection.objects = legs.ToArray();
				UnityEditor.Selection.activeGameObject = top.gameObject;
				c.Tool.MergeMeshes();
				AddStation( c, "Merge Meshes", "Meshes mode: select several, Merge (M): a table top and four legs become one object.", top );
			}

			// Separate: one object with two pieces split into two objects
			{
				var a = Primitive( c, "Separate", new BlockPrimitive(), At( 5, 32 ) + new S.Vector3( 0, -40, 0 ), new S.Vector3( 64, 64, 64 ), m );
				var b = Primitive( c, "Separate B", new BlockPrimitive(), At( 5, 48 ) + new S.Vector3( 0, 40, 0 ), new S.Vector3( 64, 64, 96 ), m );
				UnityEditor.Selection.objects = new Object[] { a.gameObject, b.gameObject };
				UnityEditor.Selection.activeGameObject = a.gameObject;
				c.Tool.MergeMeshes();
				UnityEditor.Selection.objects = new Object[] { a.gameObject };
				c.Tool.SeparateComponents();
				var pieces = UnityEditor.Selection.gameObjects.Select( x => x.GetComponent<HammerMesh>() ).Where( x => x != null ).ToList();
				for ( int i = 0; i < pieces.Count; i++ ) pieces[i].name = i == 0 ? "Separate" : "Separate (Piece)";
				AddStation( c, "Separate", "Meshes mode: Alt+N splits an object into one per connected piece.", pieces.OrderByDescending( x => x.GetComponent<MeshRenderer>().bounds.size.y ).First() );
			}

			// Move to furthest: three posts of different heights, tops lined up with the tallest
			{
				var posts = new List<HammerMesh>();
				foreach ( var (y, h) in new[] { (64f, 64f), (0f, 112f), (-64f, 160f) } )
					posts.Add( Primitive( c, y == 0 ? "Move to Furthest" : "Move to Furthest (Post)", new BlockPrimitive(), At( 6, h / 2 ) + new S.Vector3( 0, y, 0 ), new S.Vector3( 40, 40, h ), m ) );
				Select( c, posts.SelectMany( p => FaceFacing( p, new S.Vector3( 0, 0, 1 ) ) ) );
				c.Tool.Mode = EditMode.Face;
				c.Tool.MoveToFurthest( 2, 1 );
				AddStation( c, "Move to Furthest", "Select the tops, Align > Move to furthest Z+: each moves up until it's level with the highest.", posts[1] );
			}

			// Radial align: a lopsided eight-sided face made a regular octagon, then thickened
			{
				var mesh = new S.PolygonMesh();
				var corners = Enumerable.Range( 0, 8 ).Select( i =>
				{
					var a = i * Mathf.PI / 4 + (i % 3) * 0.12f;
					var r = 56 + (i % 2) * 14 - (i % 3) * 6;
					return new S.Vector3( Mathf.Cos( a ) * r, Mathf.Sin( a ) * r, 0 );
				} ).ToArray();
				mesh.AddFace( mesh.AddVertices( corners ) );
				var plate = Place( c, "Radial Align", mesh, At( 7, 1 ), m );

				c.Tool.Mode = EditMode.Face;
				Select( c, plate.Mesh.FaceHandles.Select( f => (IMeshElement)new MeshFace( plate, f ) ) );
				c.Tool.RadialAlign();
				HammerSettings.GridSize = 16;
				c.Tool.ThickenFaces();
				HammerSettings.GridSize = 8;
				AddStation( c, "Radial Align", "Align > Radial Align puts the corners on an even circle. Then Thicken (G).", plate );
			}

			c.Tool.Selection.Clear();
		}

		// ── Example builds ──

		static void Yard( Context c )
		{
			c.Frame( "Entrance", S.Vector3.Zero, new S.Vector3( 1, 0, 0 ) );

			const float half = YardInner / 2;
			var start = PathLength;
			var center = new S.Vector3( start + Wall + half, 0, 0 );
			const float low = 160;

			Block( c, "Yard Floor", center + new S.Vector3( 0, 0, -8 ), new S.Vector3( YardInner, YardInner, 16 ), "Dev Measure Dark" );
			var door = new DoorwayPrimitive { DoorWidth = 192, DoorHeight = 208, ArchHeight = 64, ArchSegments = 12, AlignToCamera = false };
			Primitive( c, "Yard Wall (Door)", door, new S.Vector3( start + Wall / 2, 0, WallHeight / 2 ), new S.Vector3( Wall, YardInner + Wall * 2, WallHeight ), WallMaterial );
			Block( c, "Yard Wall", center + new S.Vector3( half + Wall / 2, 0, low / 2 ), new S.Vector3( Wall, YardInner + Wall * 2, low ), WallMaterial );
			Block( c, "Yard Wall", center + new S.Vector3( 0, half + Wall / 2, low / 2 ), new S.Vector3( YardInner, Wall, low ), WallMaterial );
			Block( c, "Yard Wall", center + new S.Vector3( 0, -half - Wall / 2, low / 2 ), new S.Vector3( YardInner, Wall, low ), WallMaterial );

			Sign( c, "EXAMPLE BUILDS", new S.Vector3( start - 12, 0, 270 ), 1.5f );

			c.Frame( "EXAMPLE BUILDS", center, new S.Vector3( 1, 0, 0 ) );
			Sign( c, "EXAMPLE BUILDS\nSmall pieces made only with the tools in the rooms. Select one in Meshes mode to see how it's put together.",
				c.ToWorld( new S.Vector3( half - 12, 0, 120 ) ), 1.1f );

			// Two rows of three, front row nearer the door
			S.Vector3 Spot( int i ) => new( -620 + (i / 3) * 580, 600 - (i % 3) * 600, 0 );

			Hut( c, Spot( 0 ) );
			Staircase( c, Spot( 1 ) );
			Colonnade( c, Spot( 2 ) );
			Corridor( c, Spot( 3 ) );
			Crates( c, Spot( 4 ) );
			WindowWall( c, Spot( 5 ) );
			SculptedGround( c, Spot( 7 ) );
		}

		static void Hut( Context c, S.Vector3 at )
		{
			const float w = 224, d = 256, h = 144, t = 8;
			const string walls = "Dev Measure Light";

			var door = new DoorwayPrimitive { DoorWidth = 64, DoorHeight = 112, AlignToCamera = false };
			Primitive( c, "Hut Front", door, at + new S.Vector3( -d / 2 + t / 2, 0, h / 2 ), new S.Vector3( t, w, h ), walls );
			Block( c, "Hut Back", at + new S.Vector3( d / 2 - t / 2, 0, h / 2 ), new S.Vector3( t, w, h ), walls );
			Block( c, "Hut Side", at + new S.Vector3( 0, w / 2 - t / 2, h / 2 ), new S.Vector3( d - t * 2, t, h ), walls );
			Block( c, "Hut Side", at + new S.Vector3( 0, -w / 2 + t / 2, h / 2 ), new S.Vector3( d - t * 2, t, h ), walls );

			// A gable roof from the Polygon tool: a triangle pushed along the hut, overhanging
			var profile = new[] { new S.Vector3( 0, -w / 2 - 16, -36 ), new S.Vector3( 0, w / 2 + 16, -36 ), new S.Vector3( 0, 0, 36 ) };
			var roof = Prism( c, "Hut Roof", at + new S.Vector3( 0, 0, h + 36 ), profile, new S.Vector3( d + 32, 0, 0 ), "Dev Measure Red" );
			AddStation( c, "Hut", "Four walls (one a Doorway) and a roof drawn with the Polygon tool: a triangle given a depth.", roof );
		}

		static void Staircase( Context c, S.Vector3 at )
		{
			const float run = 224, width = 96, rise = 128;
			var stairs = Primitive( c, "Staircase", new StairsPrimitive { NumberOfSteps = 8, AlignToCamera = false }, at + new S.Vector3( 0, 0, rise / 2 ), new S.Vector3( run, width, rise ), "Dev Measure Grey" );

			// A landing at the top, and a railing up each side following the slope
			Block( c, "Staircase Landing", at + new S.Vector3( run / 2 + 48, 0, rise / 2 ), new S.Vector3( 96, width, rise ), "Dev Measure Grey" );
			foreach ( var side in new[] { -1, 1 } )
			{
				var y = side * (width / 2 + 3);
				var rail = new[] { new S.Vector3( -run / 2, 0, 36 ), new S.Vector3( run / 2, 0, rise + 36 ), new S.Vector3( run / 2, 0, rise + 42 ), new S.Vector3( -run / 2, 0, 42 ) };
				Prism( c, "Staircase Rail", at + new S.Vector3( 0, y, 0 ), rail, new S.Vector3( 0, 4, 0 ), "Dev Measure Dark" );
				Block( c, "Staircase Post", at + new S.Vector3( -run / 2 + 2, y, 21 ), new S.Vector3( 4, 4, 42 ), "Dev Measure Dark" );
				Block( c, "Staircase Post", at + new S.Vector3( run / 2 - 2, y, rise + 21 ), new S.Vector3( 4, 4, 42 ), "Dev Measure Dark" );
			}

			AddStation( c, "Staircase", "Stairs shape, a block landing, and rails drawn with the Polygon tool along the slope.", stairs );
		}

		static void Colonnade( Context c, S.Vector3 at )
		{
			const string stone = "Dev Measure Light";
			foreach ( var y in new[] { -80f, 80f } )
			{
				var p = at + new S.Vector3( 0, y, 0 );
				var shaft = Primitive( c, "Column", new CylinderPrimitive { NumberOfSides = 16 }, p + new S.Vector3( 0, 0, 96 ), new S.Vector3( 32, 32, 160 ), stone );
				var foot = Primitive( c, "Column Base", new BlockPrimitive(), p + new S.Vector3( 0, 0, 8 ), new S.Vector3( 48, 48, 16 ), stone );
				var head = Primitive( c, "Column Cap", new BlockPrimitive(), p + new S.Vector3( 0, 0, 184 ), new S.Vector3( 48, 48, 16 ), stone );
				UnityEditor.Selection.objects = new Object[] { shaft.gameObject, foot.gameObject, head.gameObject };
				UnityEditor.Selection.activeGameObject = shaft.gameObject;
				c.Tool.MergeMeshes();
			}

			var beam = Primitive( c, "Colonnade Beam", new BlockPrimitive(), at + new S.Vector3( 0, 0, 204 ), new S.Vector3( 56, 224, 24 ), stone );
			c.Tool.Mode = EditMode.Edge;
			HammerSettings.BevelSegments = 1;
			HammerSettings.GridSize = 4;
			Select( c, beam.Mesh.HalfEdgeHandles.Where( he => IsBottomEdge( beam, he ) ).Select( he => (IMeshElement)new MeshEdge( beam, he ) ) );
			c.Tool.QuickBevelEdges();
			HammerSettings.GridSize = 8;
			c.Tool.Selection.Clear();

			AddStation( c, "Colonnade", "Cylinder, base and cap merged (M) into one column each; the beam's lower edges bevelled.", beam );
		}

		static void Corridor( Context c, S.Vector3 at )
		{
			HammerMesh last = null;
			for ( int i = -1; i <= 1; i++ )
			{
				var arch = new DoorwayPrimitive { DoorWidth = 112, DoorHeight = 112, ArchHeight = 48, ArchSegments = 10, AlignToCamera = false };
				last = Primitive( c, "Corridor Arch", arch, at + new S.Vector3( i * 96, 0, 96 ), new S.Vector3( 16, 176, 192 ), "Dev Measure Light" );
			}

			var roof = Block( c, "Corridor Roof", at + new S.Vector3( 0, 0, 196 ), new S.Vector3( 224, 176, 8 ), "Dev Measure Red" );
			var path = Block( c, "Corridor Path", at + new S.Vector3( 0, 0, 1 ), new S.Vector3( 288, 112, 2 ), "Dev Measure Grey" );
			_ = last;
			_ = path;
			AddStation( c, "Arched Corridor", "Three Doorway shapes with Arch Height set, lined up with grid snapping, and a slab on top.", roof );
		}

		static void Crates( Context c, S.Vector3 at )
		{
			const float s = 48;
			var spots = new (float y, float z, float yaw)[]
			{
				(-54, 0, 0), (0, 0, 6), (54, 0, -4),
				(-27, 1, 10), (27, 1, -8),
				(0, 2, 15),
			};

			HammerMesh top = null;
			c.Tool.Mode = EditMode.Edge;
			HammerSettings.BevelSegments = 1;

			foreach ( var (y, z, yaw) in spots )
			{
				var crate = Primitive( c, "Crate", new BlockPrimitive(), at + new S.Vector3( 0, y, z * s + s / 2 ), new S.Vector3( s, s, s ), "Dev Measure Orange", yaw: yaw );
				HammerSettings.GridSize = 4;
				Select( c, crate.Mesh.HalfEdgeHandles.Select( he => (IMeshElement)new MeshEdge( crate, he ) ) );
				c.Tool.QuickBevelEdges();
				HammerSettings.GridSize = 8;
				top = crate;
			}

			c.Tool.Selection.Clear();
			AddStation( c, "Crates", "Boxes with every edge bevelled (select all edges, F), stacked and turned a little.", top );
		}

		static void SculptedGround( Context c, S.Vector3 at )
		{
			// A flat square split up for detail, then sculpted into a hill
			var ground = Primitive( c, "Sculpted Ground", new QuadPrimitive(), at + new S.Vector3( 0, 0, 1 ), new S.Vector3( 448, 448, 0 ), "Dev Measure Green" );
			c.Tool.Mode = EditMode.Object;
			UnityEditor.Selection.objects = new Object[] { ground.gameObject };
			for ( int i = 0; i < 4; i++ ) c.Tool.Subdivide();

			var up = Vector3.up;
			foreach ( var (x, y, r) in new[] { (0f, 0f, 160f), (60f, -40f, 96f), (-70f, 60f, 80f) } )
			{
				var p = SourceSpace.ToUnityPosition( c.ToWorld( at + new S.Vector3( x, y, 0 ) ) );
				for ( int i = 0; i < 6; i++ )
					DisplacementTool.Displace( ground, p, up, p, DisplaceMode.PushPull, false, r, 1, 0.3f );
			}
			for ( int i = 0; i < 3; i++ )
				DisplacementTool.Displace( ground, ground.GetComponent<MeshRenderer>().bounds.center, up, Vector3.zero, DisplaceMode.Smooth, false, 300, 1, 0.5f );
			Refresh( ground );

			// A crate dropped onto it from above
			var crate = Primitive( c, "Dropped Crate", new BlockPrimitive(), at + new S.Vector3( 0, 0, 320 ), new S.Vector3( 40, 40, 40 ), "Dev Measure Orange", yaw: 20 );
			UnityEditor.Selection.objects = new Object[] { crate.gameObject };
			c.Tool.MovePathTraceDown();
			Refresh( crate );

			AddStation( c, "Sculpted Ground", "A Quad, Subdivide x4, then the Displacement Tool (Push, Smooth). The crate was dropped on with Move Path Trace Down.", ground );
		}

		static void WindowWall( Context c, S.Vector3 at )
		{
			var wall = Primitive( c, "Window Wall", new BlockPrimitive(), at + new S.Vector3( 0, 0, 88 ), new S.Vector3( 16, 320, 176 ), "Dev Measure Light" );
			var cutters = new List<HammerMesh>();
			foreach ( var y in new[] { -96f, 0f, 96f } )
				cutters.Add( Primitive( c, "Window Wall Cutter", new BlockPrimitive(), at + new S.Vector3( 0, y, 104 ), new S.Vector3( 64, 56, 72 ), "Dev Measure Light" ) );

			Boolean( c, "Subtract", S.PolygonMesh.BooleanOperation.Subtract, wall, cutters.ToArray() );

			// Sills under the windows
			foreach ( var y in new[] { -96f, 0f, 96f } )
				Block( c, "Window Sill", at + new S.Vector3( -12, y, 64 ), new S.Vector3( 24, 72, 8 ), "Dev Measure Dark" );

			AddStation( c, "Window Wall", "One wall and three cutter boxes, Boolean > Subtract, with sills underneath.", wall );
		}

		// ── Helpers ──

		static HammerMesh Block( Context c, string name, S.Vector3 center, S.Vector3 size, string material, float yaw = 0, float roll = 0 ) =>
			Primitive( c, name, new BlockPrimitive(), center, size, material, yaw, roll );

		/// <summary>
		/// A shape at <paramref name="center"/> in the current frame, turned by <paramref name="yaw"/>
		/// degrees (and rolled about its forward axis by <paramref name="roll"/>).
		/// </summary>
		static HammerMesh Primitive( Context c, string name, PrimitiveBuilder builder, S.Vector3 center, S.Vector3 size, string material, float yaw = 0, float roll = 0 )
		{
			builder.Material = S.Material.Load( HammerMaterials.DefaultKey );
			var mesh = builder.CreateMesh( new S.BBox( -size / 2, size / 2 ) );
			return Place( c, name, mesh, center, material, yaw, roll );
		}

		/// <summary>
		/// A prism: <paramref name="profile"/> (relative to <paramref name="center"/>, in the
		/// frame) pushed along <paramref name="extrude"/>, centred on the profile's plane.
		/// </summary>
		static HammerMesh Prism( Context c, string name, S.Vector3 center, IList<S.Vector3> profile, S.Vector3 extrude, string material )
		{
			var basePoints = profile.Select( p => p - extrude * 0.5f ).ToList();
			var normal = extrude.Normal;

			// Wind the base counter-clockwise around the push direction
			var mid = basePoints.Aggregate( S.Vector3.Zero, ( a, p ) => a + p ) / basePoints.Count;
			var area = S.Vector3.Zero;
			for ( int i = 0; i < basePoints.Count; i++ )
				area += S.Vector3.Cross( basePoints[i] - mid, basePoints[(i + 1) % basePoints.Count] - mid );
			if ( S.Vector3.Dot( area, normal ) < 0 ) basePoints.Reverse();

			var mesh = new S.PolygonMesh();
			var n = basePoints.Count;
			var bottom = mesh.AddVertices( basePoints.ToArray() );
			var top = mesh.AddVertices( basePoints.Select( p => p + extrude ).ToArray() );
			mesh.AddFace( top );
			mesh.AddFace( bottom.Reverse().ToArray() );
			for ( int i = 0; i < n; i++ )
			{
				var j = (i + 1) % n;
				mesh.AddFace( bottom[i], bottom[j], top[j], top[i] );
			}

			return Place( c, name, mesh, center, material );
		}

		static HammerMesh Place( Context c, string name, S.PolygonMesh mesh, S.Vector3 center, string material, float yaw = 0, float roll = 0 )
		{
			var go = new GameObject( name );
			Undo.RegisterCreatedObjectUndo( go, $"Create {name}" );
			go.transform.SetPositionAndRotation( SourceSpace.ToUnityPosition( c.ToWorld( center ) ), c.Rotation * Quaternion.Euler( 0, yaw, 0 ) * Quaternion.Euler( 0, 0, roll ) );
			go.isStatic = true;

			var component = go.AddComponent<HammerMesh>();
			component.SmoothingAngle = 40;
			component.Mesh = mesh;
			Adopt( c, component, material );
			return component;
		}

		/// <summary>
		/// Parent under the map, put the material on every face (textures world aligned).
		/// </summary>
		static void Adopt( Context c, HammerMesh component, string material )
		{
			component.transform.SetParent( c.Root.transform, true );

			var m = HammerMaterials.Get( LoadMaterial( material ) ) ?? S.Material.Load( HammerMaterials.DefaultKey );
			var mesh = component.Mesh;
			mesh.AssignMaterialToFaces( mesh.FaceHandles.ToList(), m );
			Refresh( component );
		}

		/// <summary>
		/// After changing a mesh directly: textures back on the world grid, rebuilt.
		/// </summary>
		static void Refresh( HammerMesh component )
		{
			var mesh = component.Mesh;
			mesh.SetTransform( component.WorldTransform );
			mesh.TextureAlignToGrid( mesh.Transform );
			mesh.ComputeFaceTextureCoordinatesFromParameters();
			component.Commit();
		}

		static Material LoadMaterial( string name ) => AssetDatabase.LoadAssetAtPath<Material>( $"{MaterialFolder}{name}.mat" );

		/// <summary>
		/// Boolean the others into the target as Meshes mode does (the others are used up).
		/// </summary>
		static HammerMesh Boolean( Context c, string _, S.PolygonMesh.BooleanOperation operation, HammerMesh target, params HammerMesh[] others )
		{
			UnityEditor.Selection.objects = others.Select( x => (Object)x.gameObject ).Prepend( target.gameObject ).ToArray();
			UnityEditor.Selection.activeGameObject = target.gameObject;
			c.Tool.Boolean( operation );
			return target;
		}

		static void AddStation( Context c, string name, string how, HammerMesh mesh )
		{
			c.Stations.Add( new Station { Name = name, How = how, Room = c.Room, Mesh = mesh } );

			var b = mesh.GetComponent<MeshRenderer>().bounds;
			var top = SourceSpace.ToSourcePosition( new Vector3( b.center.x, b.max.y, b.center.z ) );
			Sign( c, $"{name.ToUpperInvariant()}\n{how}", top + new S.Vector3( 0, 0, 32 ), 0.5f );
		}

		/// <summary>
		/// A floating label, readable from both sides; the front faces back along the frame's
		/// forward (towards the door you came in by).
		/// </summary>
		static void Sign( Context c, string text, S.Vector3 position, float scale )
		{
			var go = new GameObject( "Sign: " + text.Split( '\n' )[0] );
			Undo.RegisterCreatedObjectUndo( go, "Create Sign" );
			go.transform.SetParent( c.Root.transform, false );
			go.transform.SetPositionAndRotation( SourceSpace.ToUnityPosition( position ), c.Rotation );

			var mesh = go.AddComponent<TextMesh>();
			mesh.text = Wrap( text, 34 );
			mesh.font = SignFont;
			mesh.fontSize = 48;
			mesh.characterSize = 0.1f * scale;
			mesh.anchor = TextAnchor.LowerCenter;
			mesh.alignment = TextAlignment.Center;
			mesh.color = new Color( 0.95f, 0.95f, 0.95f );
			go.GetComponent<MeshRenderer>().sharedMaterial = c.SignMaterial;

			// A copy facing the other way, so the sign reads from behind too
			var back = Object.Instantiate( go, go.transform );
			back.name = "Back";
			back.transform.localPosition = Vector3.zero;
			back.transform.localRotation = Quaternion.Euler( 0, 180, 0 );
			back.transform.localScale = Vector3.one;
		}

		static Font SignFont => Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );

		/// <summary>
		/// The font's texture on a one-sided text shader that walls hide (Unity's own text shader
		/// draws both sides through everything, so front and back copies show on top of each other).
		/// </summary>
		static Material SignMaterial( bool saveAssets )
		{
			var path = $"{Folder}/Hammer Sign.mat";
			var material = saveAssets ? AssetDatabase.LoadAssetAtPath<Material>( path ) : null;
			var shader = Shader.Find( "Hammer/Sign" );

			if ( material == null )
			{
				material = new Material( shader != null ? shader : SignFont.material.shader ) { name = "Hammer Sign" };
				if ( saveAssets ) AssetDatabase.CreateAsset( material, path );
			}

			material.mainTexture = SignFont.material.mainTexture;
			if ( saveAssets ) EditorUtility.SetDirty( material );
			return material;
		}

		static string Wrap( string text, int width )
		{
			var lines = new List<string>();
			foreach ( var paragraph in text.Split( '\n' ) )
			{
				var line = "";
				foreach ( var word in paragraph.Split( ' ' ) )
				{
					if ( line.Length + word.Length + 1 > width && line.Length > 0 )
					{
						lines.Add( line );
						line = word;
					}
					else line = line.Length == 0 ? word : line + " " + word;
				}
				lines.Add( line );
			}
			return string.Join( "\n", lines );
		}

		static void Select( Context c, IEnumerable<IMeshElement> elements )
		{
			c.Tool.Selection.Clear();
			foreach ( var e in elements ) c.Tool.Selection.Add( e );
		}

		static IEnumerable<IMeshElement> FaceFacing( HammerMesh component, S.Vector3 direction )
		{
			var best = component.Mesh.FaceHandles.OrderByDescending( f =>
			{
				component.Mesh.ComputeFaceNormal( f, out var n );
				return S.Vector3.Dot( n, direction );
			} ).First();
			yield return new MeshFace( component, best );
		}

		/// <summary>
		/// The top corner of a box on the given sides (signs of local X and Y).
		/// </summary>
		static MeshVertex TopCorner( HammerMesh component, int x, int y )
		{
			var mesh = component.Mesh;
			var v = mesh.VertexHandles.OrderByDescending( h =>
			{
				var p = mesh.GetVertexPosition( h );
				return p.z * 1000 + p.x * x + p.y * y;
			} ).First();
			return new MeshVertex( component, v );
		}

		static bool IsTopEdge( HammerMesh component, HalfEdgeMesh.HalfEdgeHandle he )
		{
			var line = component.Mesh.GetEdgeLine( he );
			var top = component.Mesh.CalculateBounds().Maxs.z;
			return System.Math.Abs( line.Start.z - top ) < 0.01f && System.Math.Abs( line.End.z - top ) < 0.01f;
		}

		static bool IsBottomEdge( HammerMesh component, HalfEdgeMesh.HalfEdgeHandle he )
		{
			var line = component.Mesh.GetEdgeLine( he );
			var bottom = component.Mesh.CalculateBounds().Mins.z;
			return System.Math.Abs( line.Start.z - bottom ) < 0.01f && System.Math.Abs( line.End.z - bottom ) < 0.01f;
		}

		static bool IsUprightEdge( HammerMesh component, HalfEdgeMesh.HalfEdgeHandle he )
		{
			var line = component.Mesh.GetEdgeLine( he );
			return System.Math.Abs( line.Start.x - line.End.x ) < 0.01f && System.Math.Abs( line.Start.y - line.End.y ) < 0.01f;
		}
	}
}
