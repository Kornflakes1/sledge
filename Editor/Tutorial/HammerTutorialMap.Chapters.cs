using System.Collections.Generic;
using System.Linq;
using Sandbox.Primitives;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// The tutorial's chapters that follow Valve's docs page by page (the older themed rooms,
	/// Shapes, Faces, Edges, Openings, Materials and Utilities, slot in between them).
	/// </summary>
	public static partial class HammerTutorialMap
	{
		static float Inch => SourceSpace.UnitScale;

		/// <summary>A direction in the room's frame (x forward, y left, z up) as a Unity direction.</summary>
		static Vector3 Dir( Context c, S.Vector3 frame ) => SourceSpace.ToUnityDirection( c.Forward * frame.x + c.Left * frame.y + new S.Vector3( 0, 0, frame.z ) );

		static S.Vector3 At( int slot, float z ) => Slot( slot ) + new S.Vector3( 0, 0, z );

		static IEnumerable<IMeshElement> AllFaces( HammerMesh m ) => m.Mesh.FaceHandles.Select( f => (IMeshElement)new MeshFace( m, f ) ).ToList();

		/// <summary>Faces of a mesh picked by their centre (in the mesh's own frame: x forward, y left, z up).</summary>
		static List<IMeshElement> FacesWhere( HammerMesh m, System.Func<S.Vector3, bool> where ) =>
			m.Mesh.FaceHandles.Where( f => where( m.Mesh.GetFaceCenter( f ) ) ).Select( f => (IMeshElement)new MeshFace( m, f ) ).ToList();

		/// <summary>Edges of a mesh picked by their two ends (in the mesh's own frame).</summary>
		static List<IMeshElement> EdgesWhere( HammerMesh m, System.Func<S.Vector3, S.Vector3, bool> where )
		{
			var result = new List<IMeshElement>();
			var seen = new HashSet<int>();
			foreach ( var he in m.Mesh.HalfEdgeHandles )
			{
				var opposite = m.Mesh.GetOppositeHalfEdge( he );
				if ( opposite.IsValid && seen.Contains( opposite.Index ) ) continue;
				seen.Add( he.Index );
				var line = m.Mesh.GetEdgeLine( he );
				if ( where( line.Start, line.End ) ) result.Add( new MeshEdge( m, he ) );
			}
			return result;
		}

		static bool Near( float a, float b ) => System.Math.Abs( a - b ) < 0.5f;

		/// <summary>Faces mode with these faces selected.</summary>
		static void SelectFaces( Context c, IEnumerable<IMeshElement> faces )
		{
			c.Tool.Mode = EditMode.Face;
			Select( c, faces );
		}

		static void SelectEdges( Context c, IEnumerable<IMeshElement> edges )
		{
			c.Tool.Mode = EditMode.Edge;
			Select( c, edges );
		}

		static void SelectObjects( Context c, params HammerMesh[] meshes )
		{
			c.Tool.Selection.Clear();
			c.Tool.Mode = EditMode.Object;
			UnityEditor.Selection.objects = meshes.Select( m => (Object)m.gameObject ).ToArray();
			UnityEditor.Selection.activeGameObject = meshes[0].gameObject;
		}

		/// <summary>After the tool has moved things: textures back on the grid, labelled.</summary>
		static void Done( Context c, HammerMesh m, string name, string how )
		{
			c.Tool.ScriptedPivot( null );
			c.Tool.Selection.Clear();
			Refresh( m );
			AddStation( c, name, how, m );
		}

		// ─────────────────────────────── 1. Navigation ───────────────────────────────

		static void Navigation( Context c )
		{
			const string m = "Dev Measure Blue";

			AddStation( c, "Look Around", "Hold the right mouse button and move the mouse to look. W A S D move, Z / X (or E / Q) go up and down. Shift is faster, Ctrl slower, the wheel changes speed.",
				Primitive( c, "Look Around", new CylinderPrimitive { NumberOfSides = 12 }, At( 0, 80 ), new S.Vector3( 64, 64, 160 ), m ) );
			AddStation( c, "Fly Mode", "Press Z to fly without holding the mouse button. Z again, Esc or a click stops.",
				Primitive( c, "Fly Mode", new SpikePrimitive(), At( 1, 64 ), new S.Vector3( 96, 96, 128 ), m ) );
			AddStation( c, "Orbit", "Alt + left drag turns the camera round the middle of the view.",
				Primitive( c, "Orbit", new SpherePrimitive(), At( 2, 64 ), new S.Vector3( 128, 128, 128 ), m ) );
			AddStation( c, "Frame Selection", "Shift+A centres the view on what's selected, so Alt + drag then turns round it.",
				Block( c, "Frame Selection", At( 3, 48 ), new S.Vector3( 96, 96, 96 ), m ) );
			AddStation( c, "Alt Zoom and Pan", "Alt + right drag zooms in and out, Alt + middle drag slides the view. The wheel moves forward and back.",
				Primitive( c, "Alt Zoom and Pan", new CylinderPrimitive { NumberOfSides = 6 }, At( 4, 40 ), new S.Vector3( 128, 128, 80 ), m ) );
			AddStation( c, "Active View", "The view with the red outline is the active one. Press a mouse button in another to switch (right or middle won't change the selection).",
				Block( c, "Active View", At( 5, 64 ), new S.Vector3( 128, 32, 128 ), "Dev Measure Red" ) );
			AddStation( c, "2D Views", "2D views: right drag pans, the wheel zooms. Ctrl+Space cycles Top / Front / Side; F2, F3, F4 pick one.",
				Primitive( c, "2D Views", new QuadPrimitive(), At( 6, 1 ), new S.Vector3( 160, 160, 0 ), m ) );
			AddStation( c, "Maximise View", "Shift+Z fills the window with the view under the mouse. Shift+Z again for all four.",
				Primitive( c, "Maximise View", new StairsPrimitive { NumberOfSteps = 4, AlignToCamera = false }, At( 7, 48 ), new S.Vector3( 128, 96, 96 ), m ) );
			AddStation( c, "Lighting Modes", "In the 3D view F5 shows Fullbright (no lighting), F6 shows the lighting. The view's menu also has Wire.",
				Primitive( c, "Lighting Modes", new SpherePrimitive(), At( 8, 48 ), new S.Vector3( 96, 96, 96 ), "Dev Reflectivity 90" ) );
		}

		// ─────────────────────────────── 2. Hammer Overview ───────────────────────────────

		static void Overview( Context c )
		{
			const string m = "Dev Measure Yellow";
			var cube = new S.Vector3( 96, 96, 96 );

			AddStation( c, "Selection Tool", "Shift+S. Click to select. Space cycles Vertices, Edges, Faces and Meshes; 1 to 4 pick one.",
				Block( c, "Selection Tool", At( 0, 48 ), cube, m ) );

			// Translate: lifted on the grid
			{
				var box = Block( c, "Translate", At( 1, 32 ), new S.Vector3( 96, 96, 64 ), m );
				SelectFaces( c, FaceFacing( box, new S.Vector3( 0, 0, 1 ) ) );
				c.Tool.ScriptedMove( Vector3.up * 32 * Inch );
				Done( c, box, "Translate", "T, then drag an arrow (or a square for two axes). Ctrl moves off the grid, [ and ] change the grid. The purple middle drops it onto other surfaces." );
			}

			AddStation( c, "Rotate", "R, then drag a ring. Hover the middle for the yellow ball, which turns freely. The angle snap is bottom right; Ctrl turns it off.",
				Block( c, "Rotate", At( 2, 48 ), cube, m, yaw: 30 ) );

			// Scale: stretched tall and thin
			{
				var box = Block( c, "Scale", At( 3, 48 ), cube, m );
				SelectFaces( c, AllFaces( box ) );
				c.Tool.ScriptedScale( new Vector3( 0.5f, 1.5f, 0.5f ) );
				Done( c, box, "Scale", "E, then drag a cube (one axis), a square (two) or the middle (all). After a drag, type a number on the numpad and Enter for an exact amount." );
			}

			// Pivot: turned round one corner instead of its middle
			{
				var box = Block( c, "Pivot Tool", At( 4, 32 ), new S.Vector3( 128, 48, 64 ), m );
				SelectFaces( c, AllFaces( box ) );
				var corner = box.SourceToWorld( new S.Vector3( -64, -24, 0 ) );
				c.Tool.ScriptedPivot( corner );
				c.Tool.ScriptedRotate( corner, Quaternion.AngleAxis( 30, Vector3.up ) );
				Done( c, box, "Pivot Tool", "Insert, then drag the pivot: rotate and scale turn round it. End puts it back. Set Origin To Pivot (Meshes mode) keeps it." );
			}

			AddStation( c, "Block Tool", "Shift+B. Drag the base, then the height; drag the balls to resize; Enter or Space makes it, Esc cancels. Hold Shift to draw on the surface under the mouse.",
				Block( c, "Block Tool", At( 5, 48 ), new S.Vector3( 128, 96, 96 ), m ) );

			// Clipping tool: a ramp
			{
				var box = Block( c, "Clipping Tool", At( 6, 48 ), cube, m );
				ClipTool.Clip( box, box.Mesh, box.SourceToWorld( S.Vector3.Zero ), Dir( c, new S.Vector3( 1, 0, 1 ) ).normalized, ClipTool.KeepMode.Back, null, new List<(Vector3, Vector3)>() );
				Refresh( box );
				AddStation( c, "Clipping Tool", "Shift+X. Drag a line in a 2D view; Shift+X again picks the side kept (or both). Enter cuts.", box );
			}

			// Mirror tool: a wedge and its mirror image
			{
				var profile = new[] { new S.Vector3( 0, -40, -40 ), new S.Vector3( 0, 40, -40 ), new S.Vector3( 0, 40, 40 ) };
				var wedge = Prism( c, "Mirror Tool", At( 7, 40 ) + new S.Vector3( 0, -40, 0 ), profile, new S.Vector3( 96, 0, 0 ), m );
				var copyMesh = S.PolygonMesh.FromData( wedge.Mesh.ToData() );
				copyMesh.FlipAllFaces();
				copyMesh.Scale( new S.Vector3( 1, -1, 1 ) );
				Place( c, "Mirror Tool (Copy)", copyMesh, At( 7, 40 ) + new S.Vector3( 0, 40, 0 ), m );
				AddStation( c, "Mirror Tool", "Shift+F. Drag a line for the mirror; Enter keeps the copy. In Faces mode the copy stays in the same mesh.", wedge );
			}

			// Displacement: a bumpy patch
			{
				var ground = Primitive( c, "Displacement Tool", new QuadPrimitive(), At( 8, 2 ), new S.Vector3( 160, 160, 0 ), "Dev Measure Green" );
				SelectObjects( c, ground );
				for ( int i = 0; i < 3; i++ ) c.Tool.Subdivide();
				var p = ground.GetComponent<MeshRenderer>().bounds.center;
				for ( int i = 0; i < 5; i++ )
					DisplacementTool.Displace( ground, p, Vector3.up, p, DisplaceMode.PushPull, false, 60, 1, 0.3f );
				Refresh( ground );
				AddStation( c, "Displacement Tool", "Shift+D on a subdivided face: push, pull and smooth it like clay.", ground );
			}

			// Path tool: a ribbon road
			{
				var road = MakePath( c, "Path Tool", PathTool.Profile.Ribbon, new[] { At( 9, 2 ) + new S.Vector3( -80, 60, 0 ), At( 9, 2 ) + new S.Vector3( 0, -40, 0 ), At( 9, 2 ) + new S.Vector3( 80, 60, 0 ) }, "Dev Measure Grey" );
				AddStation( c, "Path Tool", "Click points along the way, Enter builds a ribbon (a road), rail or pipe through them. Backspace drops the last point.", road );
			}

			// Mesh projection: a plank laid over a lump
			{
				Primitive( c, "Projection Lump", new SpherePrimitive(), At( 10, 0 ), new S.Vector3( 128, 128, 80 ), "Dev Measure Green" );
				var plank = Primitive( c, "Mesh Projection Tool", new QuadPrimitive(), At( 10, 120 ), new S.Vector3( 160, 48, 0 ), "Dev Measure Orange" );
				SelectObjects( c, plank );
				for ( int i = 0; i < 3; i++ ) c.Tool.Subdivide();
				MeshProjectionTool.ProjectDirection = MeshProjectionTool.Direction.Down;
				MeshProjectionTool.Offset = 1;
				MeshProjectionTool.Open( c.Tool );
				c.Tool.SubTool?.Apply();
				MeshProjectionTool.Offset = 0;
				Refresh( plank );
				AddStation( c, "Mesh Projection Tool", "Drops the selection onto what's below (or along the view). Space projects again, Enter keeps it.", plank );
			}

			// Command history: two commands repeated three more times make a stepped pyramid
			{
				var box = Block( c, "Command History", At( 11, 8 ), new S.Vector3( 128, 128, 16 ), m );
				SelectFaces( c, FaceFacing( box, new S.Vector3( 0, 0, 1 ) ) );
				c.Tool.ScriptedScale( new Vector3( 0.75f, 1, 0.75f ), extrude: true );
				c.Tool.ScriptedMove( Vector3.up * 16 * Inch, extrude: true );
				var n = c.Tool.History.Count;
				c.Tool.RepeatCommands( new[] { n - 2, n - 1 }, 3 );
				Done( c, box, "Command History", "Shift+G repeats the last command. Window > Hammer > Command History repeats several: here a Shift+scale and a Shift+move, three more times." );
			}
		}

		static HammerMesh MakePath( Context c, string name, PathTool.Profile shape, S.Vector3[] points, string material )
		{
			var (oldShape, oldWidth, oldSmooth) = (PathTool.Shape, PathTool.Width, PathTool.Smoothness);
			PathTool.Open( c.Tool );
			var path = (PathTool)c.Tool.SubTool;
			PathTool.Shape = shape;
			PathTool.Width = 40;
			PathTool.Smoothness = 6;
			path.Points.AddRange( points.Select( p => SourceSpace.ToUnityPosition( c.ToWorld( p ) ) ) );
			path.Apply();
			(PathTool.Shape, PathTool.Width, PathTool.Smoothness) = (oldShape, oldWidth, oldSmooth);

			var made = UnityEditor.Selection.activeGameObject.GetComponent<HammerMesh>();
			made.name = name;
			Adopt( c, made, material );
			return made;
		}

		// ─────────────────────────────── 3. Mesh Editing 1 ───────────────────────────────

		static void MeshEditing1( Context c )
		{
			const string m = "Dev Measure Orange";
			var cube = new S.Vector3( 96, 96, 96 );

			// The building blocks, one changed on each
			{
				var box = Block( c, "Vertex", At( 0, 48 ), cube, m );
				c.Tool.Mode = EditMode.Vertex;
				Select( c, new IMeshElement[] { TopCorner( box, 1, 1 ) } );
				c.Tool.ScriptedMove( Vector3.up * 48 * Inch );
				Done( c, box, "Vertex", "Vertices mode (1). The smallest part: a corner. Drag one with T and the faces round it follow." );
			}
			{
				var box = Block( c, "Edge", At( 1, 48 ), cube, m );
				SelectEdges( c, EdgesWhere( box, ( a, b ) => Near( a.z, 48 ) && Near( b.z, 48 ) && Near( a.x, 48 ) && Near( b.x, 48 ) ) );
				c.Tool.ScriptedMove( Vector3.down * 64 * Inch );
				Done( c, box, "Edge", "Edges mode (2). An edge borders faces. Move one down for a slope." );
			}
			{
				var box = Block( c, "Face", At( 2, 48 ), cube, m );
				SelectFaces( c, FaceFacing( box, new S.Vector3( 0, 0, 1 ) ) );
				c.Tool.ScriptedRotate( box.SourceToWorld( new S.Vector3( 0, 0, 48 ) ), Quaternion.AngleAxis( 45, Vector3.up ) );
				c.Tool.ScriptedScale( new Vector3( 0.6f, 1, 0.6f ) );
				Done( c, box, "Face", "Faces mode (3). A face joins edges. Here the top was turned with R and shrunk with E." );
			}
			AddStation( c, "Mesh", "Meshes mode (4). The whole object: the closest thing to an old Hammer brush.",
				Block( c, "Mesh", At( 3, 48 ), cube, m ) );

			// Not brushes
			{
				var box = Block( c, "Faces Can Go", At( 4, 48 ), cube, m );
				SelectFaces( c, FaceFacing( box, new S.Vector3( 0, 0, 1 ) ).Concat( FaceFacing( box, new S.Vector3( -1, 0, 0 ) ) ) );
				c.Tool.Delete();
				Done( c, box, "Faces Can Go", "Not brushes: faces can be removed and leave the mesh open." );
			}
			{
				var l = new[] { new S.Vector3( -48, -48, 0 ), new S.Vector3( 48, -48, 0 ), new S.Vector3( 48, -8, 0 ), new S.Vector3( -8, -8, 0 ), new S.Vector3( -8, 48, 0 ), new S.Vector3( -48, 48, 0 ) };
				AddStation( c, "Concave Is Fine", "Not brushes: concave shapes are fine meshes.",
					Prism( c, "Concave Is Fine", At( 5, 40 ), l, new S.Vector3( 0, 0, 80 ), m ) );
			}
			{
				var box = Block( c, "Bent Faces", At( 6, 48 ), cube, m );
				c.Tool.Mode = EditMode.Vertex;
				Select( c, new IMeshElement[] { TopCorner( box, 1, 1 ) } );
				c.Tool.ScriptedMove( Vector3.up * 24 * Inch );
				Done( c, box, "Bent Faces", "Not brushes: a face needn't be flat; it's split into triangles when built." );
			}

			// Extrude, two ways
			{
				var box = Block( c, "Shift Extrude", At( 7, 32 ), new S.Vector3( 96, 96, 64 ), m );
				SelectFaces( c, FaceFacing( box, new S.Vector3( 0, 0, 1 ) ) );
				c.Tool.ScriptedMove( Vector3.up * 48 * Inch, extrude: true );
				SelectFaces( c, FaceFacing( box, new S.Vector3( -1, 0, 0 ) ).Take( 1 ) );
				c.Tool.ScriptedMove( Dir( c, new S.Vector3( -1, 0, 0 ) ) * 48 * Inch, extrude: true );
				Done( c, box, "Shift Extrude", "Select a face, T, and hold Shift while dragging: new faces grow out of it. Do it again on others." );
			}

			// Ring and connect, the new loop pushed out
			{
				var box = Block( c, "Ring and Connect", At( 8, 48 ), cube, m );
				SelectEdges( c, EdgesWhere( box, ( a, b ) => Near( a.x, b.x ) && Near( a.y, b.y ) ).Take( 1 ) );
				c.Tool.SelectRing();
				c.Tool.ConnectEdges();
				c.Tool.ScriptedScale( new Vector3( 1.4f, 1, 1.4f ) );
				Done( c, box, "Ring and Connect", "Edges mode: click an edge, G selects its ring, Connect adds a loop through it. Here the loop was then scaled out." );
			}

			// Rotate and scale a face as you extrude
			{
				var box = Block( c, "Rotate and Scale", At( 9, 24 ), new S.Vector3( 96, 96, 48 ), m );
				SelectFaces( c, FaceFacing( box, new S.Vector3( 0, 0, 1 ) ) );
				c.Tool.ScriptedMove( Vector3.up * 64 * Inch, extrude: true );
				var top = ((MeshFace)c.Tool.SelectedFaces.First()).CenterWorld;
				c.Tool.ScriptedRotate( top, Quaternion.AngleAxis( 30, Vector3.up ) );
				c.Tool.ScriptedScale( new Vector3( 0.5f, 1, 0.5f ) );
				Done( c, box, "Rotate and Scale", "R and E work on faces, edges and vertices too: the top was extruded, then turned and shrunk." );
			}
		}

		// ─────────────────────────────── 4. Mesh Editing 2: the arched bridge ───────────────────────────────

		static void MeshEditing2( Context c )
		{
			const string m = "Dev Measure Light";
			var deckSize = new S.Vector3( 96, 320, 64 );

			// Three stages along the left wall, the deck running alongside the path
			S.Vector3 Stage( int i ) => new( -300 + i * 300, 260, 0 );

			// The cylinder lies across the deck (turned on its side, then round to face along the path)
			HammerMesh Deck( int i ) => Block( c, $"Bridge Deck {i + 1}", Stage( i ) + new S.Vector3( 0, 0, 96 ), deckSize, m );
			HammerMesh Arch( int i ) => Primitive( c, "Bridge Arch", new CylinderPrimitive { NumberOfSides = 32 }, Stage( i ), new S.Vector3( 224, 224, 96 ), "Dev Measure Orange", yaw: 90, roll: 90 );

			{
				var deck = Deck( 0 );
				Arch( 0 );
				AddStation( c, "Bridge: Block Out", "Step 1. A block for the deck and a 32-sided cylinder for the arch, lined up with T, R and E in Meshes mode.", deck );
			}
			{
				var deck = Deck( 1 );
				Boolean( c, "Subtract", S.PolygonMesh.BooleanOperation.Subtract, deck, Arch( 1 ) );
				AddStation( c, "Bridge: Arch Sewn In", "Step 2. Docs way: delete the deck's covered faces, keep the arch's inside faces (Shift+double-click a range, Ctrl+I invert, Delete), flip them (F), Bridge (B) the gaps, fill the ends (P). Or here: Boolean > Subtract.", deck );
			}
			{
				var deck = Deck( 2 );
				Boolean( c, "Subtract", S.PolygonMesh.BooleanOperation.Subtract, deck, Arch( 2 ) );
				SelectFaces( c, FaceFacing( deck, new S.Vector3( 0, 0, 1 ) ) );
				var grid = HammerSettings.GridSize;
				HammerSettings.GridSize = 12;
				c.Tool.InsetFaces();
				HammerSettings.GridSize = grid;
				var middle = c.Tool.SelectedFaces.Select( f => f.Index ).ToHashSet();
				SelectFaces( c, FacesWhere( deck, p => Near( p.z, 32 ) ).Where( f => !middle.Contains( f.Index ) ) );
				c.Tool.ScriptedMove( Vector3.up * 24 * Inch, extrude: true );
				Done( c, deck, "Bridge: Walls", "Step 3. Docs: Cut tool (C) lines along the deck, then extrude the strips up. Here: Inset the top, extrude the border." );
			}

			// The selection and sewing tools, along the right wall
			{
				var drum = Primitive( c, "Select Between", new CylinderPrimitive { NumberOfSides = 16 }, new S.Vector3( -300, -260, 64 ), new S.Vector3( 128, 128, 128 ), m );
				SelectFaces( c, FacesWhere( drum, p => !Near( p.z, 64 ) && !Near( p.z, -64 ) && p.y > 8 ) );
				var grid = HammerSettings.GridSize;
				HammerSettings.GridSize = 16;
				c.Tool.ExtrudeFaces();
				HammerSettings.GridSize = grid;
				Done( c, drum, "Select Between", "Faces mode: double-click selects every face, Alt+double-click the ones in line, Shift+double-click everything between. Ctrl+I inverts." );
			}
			{
				var box = Block( c, "Fill Hole", new S.Vector3( 0, -260, 48 ), new S.Vector3( 96, 96, 96 ), m );
				SelectFaces( c, FaceFacing( box, new S.Vector3( 0, 0, 1 ) ) );
				c.Tool.Delete();
				SelectEdges( c, EdgesWhere( box, ( a, b ) => Near( a.z, 48 ) && Near( b.z, 48 ) ) );
				c.Tool.FillHole();
				Done( c, box, "Fill Hole", "Edges mode: double-click an open edge for its loop, P fills it with a face. B bridges two open edges." );
			}
			{
				var box = Block( c, "Cut Tool", new S.Vector3( 300, -260, 48 ), new S.Vector3( 96, 96, 96 ), m );
				var top = (MeshFace)FaceFacing( box, new S.Vector3( 0, 0, 1 ) ).First();
				var edges = box.Mesh.GetFaceEdges( top.Handle );
				var e0 = new MeshEdge( box, edges[0] );
				var e2 = new MeshEdge( box, edges[2] );
				e0.GetWorldPoints( out var a0, out var b0 );
				e2.GetWorldPoints( out var a2, out var b2 );
				var cut = new EdgeCutTool();
				c.Tool.BeginSubTool( cut );
				cut.AddEdgePoint( top, e0, (a0 + b0) * 0.5f );
				cut.AddEdgePoint( top, e2, (a2 + b2) * 0.5f );
				cut.Apply();

				// One half of the cut top raised
				var halves = FacesWhere( box, p => Near( p.z, 48 ) ).OrderBy( f => box.Mesh.GetFaceCenter( ((MeshFace)f).Handle ).x + box.Mesh.GetFaceCenter( ((MeshFace)f).Handle ).y ).ToList();
				SelectFaces( c, halves.Take( 1 ) );
				c.Tool.ScriptedMove( Vector3.up * 32 * Inch, extrude: true );
				Done( c, box, "Cut Tool", "Faces mode, C: click points on edges and faces to draw new edges, Enter cuts. Here one half was then extruded." );
			}
		}

		// ─────────────────────────────── 5. Mesh Editing 3: the tunnel ───────────────────────────────

		const float TunnelRadius = 64;
		const float TunnelLength = 96;
		const float TunnelWalls = 48;

		/// <summary>
		/// The tunnel template: the top half of a cylinder lying along the room, faces turned in
		/// (step 1), with its sides extruded down and the floor bridged across (step 2).
		/// </summary>
		static HammerMesh Tunnel( Context c, string name, S.Vector3 at, bool walls )
		{
			var drum = Primitive( c, name, new CylinderPrimitive { NumberOfSides = 32 }, at + new S.Vector3( 0, 0, TunnelWalls + 2 ), new S.Vector3( TunnelRadius * 2, TunnelRadius * 2, TunnelLength ), "Dev Measure Grey", yaw: 90, roll: 90 );

			// Keep the arch: the curved faces above the middle (not the end caps)
			var mesh = drum.Mesh;
			var keep = mesh.FaceHandles.Where( f =>
			{
				mesh.ComputeFaceNormal( f, out var n );
				var p = drum.SourceToWorld( mesh.GetFaceCenter( f ) );
				var middle = drum.transform.position;
				var along = Mathf.Abs( Vector3.Dot( drum.SourceDirectionToWorld( n ), Dir( c, new S.Vector3( 1, 0, 0 ) ) ) ) > 0.9f;
				return !along && p.y > middle.y + 0.01f;
			} ).Select( f => f.Index ).ToHashSet();
			SelectFaces( c, AllFaces( drum ).Where( f => !keep.Contains( f.Index ) ) );
			c.Tool.Delete();

			// Turned inside out: an arch to stand under
			SelectFaces( c, AllFaces( drum ) );
			c.Tool.FlipFaces();

			if ( walls )
			{
				// The two long open edges at the foot of the arch, extruded down, then the floor
				var foot = drum.transform.position.y;
				SelectEdges( c, OpenEdges( drum ).Where( e =>
				{
					((MeshEdge)e).GetWorldPoints( out var a, out var b );
					return Mathf.Abs( a.y - foot ) < 0.01f && Mathf.Abs( b.y - foot ) < 0.01f;
				} ) );
				c.Tool.ScriptedMove( Vector3.down * TunnelWalls * Inch, extrude: true );
				c.Tool.BridgeEdges();
			}

			c.Tool.Selection.Clear();
			Refresh( drum );
			return drum;
		}

		static IEnumerable<IMeshElement> OpenEdges( HammerMesh m ) =>
			m.Mesh.HalfEdgeHandles.Where( he => m.Mesh.IsEdgeOpen( he ) ).Select( he => (IMeshElement)new MeshEdge( m, he ) ).ToList();

		/// <summary>The open edges round the far end of a tunnel.</summary>
		static List<IMeshElement> FarEnd( Context c, HammerMesh tunnel )
		{
			var forward = Dir( c, new S.Vector3( 1, 0, 0 ) );
			var open = OpenEdges( tunnel ).ToList();
			var far = open.Max( e => { ((MeshEdge)e).GetWorldPoints( out var a, out var b ); return Vector3.Dot( (a + b) * 0.5f, forward ); } );
			return open.Where( e =>
			{
				((MeshEdge)e).GetWorldPoints( out var a, out var b );
				return Mathf.Abs( Vector3.Dot( a, forward ) - far ) < 0.01f && Mathf.Abs( Vector3.Dot( b, forward ) - far ) < 0.01f;
			} ).ToList();
		}

		static void MeshEditing3( Context c )
		{
			{
				var t = Tunnel( c, "Tunnel: Template", new S.Vector3( -300, 300, 0 ), false );
				AddStation( c, "Tunnel: Template", "Step 1. A 32-sided cylinder along the grid; delete all but the top half, double-click it and Flip (F) so it faces in.", t );
			}
			{
				var t = Tunnel( c, "Tunnel: Walls and Floor", new S.Vector3( 0, 300, 0 ), true );
				AddStation( c, "Tunnel: Walls and Floor", "Step 2. Edges mode: the two bottom edges, Shift+drag down for walls, then Bridge (B) for the floor.", t );
			}

			// The curve: the end loop turned round a pivot off to the side, 15 degrees at a time
			{
				var t = Tunnel( c, "Tunnel: Curve", new S.Vector3( 0, -240, 0 ), true );
				SelectEdges( c, FarEnd( c, t ) );
				var end = c.Tool.Selection.OfType<MeshEdge>().Aggregate( Vector3.zero, ( sum, e ) => { e.GetWorldPoints( out var a, out var b ); return sum + a + b; } ) / (c.Tool.Selection.Count * 2);
				var right = Dir( c, new S.Vector3( 0, -1, 0 ) );
				var pivot = new Vector3( end.x, t.transform.position.y - (TunnelWalls + 2) * Inch, end.z ) + right * (TunnelRadius + 64) * Inch;
				// Whichever way round carries the end on forwards
				var turn = Quaternion.AngleAxis( 15, Vector3.up );
				if ( Vector3.Dot( pivot + turn * (end - pivot) - end, Dir( c, new S.Vector3( 1, 0, 0 ) ) ) < 0 )
					turn = Quaternion.Inverse( turn );
				c.Tool.ScriptedPivot( pivot );
				c.Tool.ScriptedRotate( pivot, turn, extrude: true );

				for ( int i = 0; i < 5; i++ ) c.Tool.RepeatLast();
				Done( c, t, "Tunnel: Curve (Shift+G)", "Step 3. Double-click the end loop, Insert and move the pivot out to the side, R and Shift+drag 15 degrees, then Shift+G five times." );
			}

			// The pinch: extended, scaled round a pivot on the floor, extended, plugged
			{
				var t = Tunnel( c, "Tunnel: Pinch", new S.Vector3( -300, -240, 0 ), true );
				var forward = Dir( c, new S.Vector3( 1, 0, 0 ) );
				SelectEdges( c, FarEnd( c, t ) );
				c.Tool.ScriptedMove( forward * 48 * Inch, extrude: true );
				var loop = c.Tool.Selection.OfType<MeshEdge>().ToList();
				var middle = loop.Aggregate( Vector3.zero, ( sum, e ) => { e.GetWorldPoints( out var a, out var b ); return sum + a + b; } ) / (loop.Count * 2);
				var floor = new Vector3( middle.x, t.transform.position.y - (TunnelWalls + 2) * Inch, middle.z );
				c.Tool.ScriptedPivot( floor );
				c.Tool.ScriptedMove( forward * 32 * Inch, extrude: true );
				var scale = Vector3.one * 0.6f + new Vector3( Mathf.Abs( forward.x ), 0, Mathf.Abs( forward.z ) ) * 0.4f;
				c.Tool.ScriptedScale( scale );
				c.Tool.FillHole();
				Done( c, t, "Tunnel: Pinch", "Step 4. Extend the end, move the pivot to the floor, Scale (E) the loop in: the floor stays put. P plugs the end." );
			}

			// Copies: Shift+drag in Meshes mode copies instead of extruding
			{
				var a = Tunnel( c, "Tunnel: Copies", new S.Vector3( 300, -240, 0 ), true );
				var b = Tunnel( c, "Tunnel: Copy", new S.Vector3( 300, -376, 0 ), true );
				_ = b;
				AddStation( c, "Tunnel: Copies", "Step 5. Meshes mode, Shift+drag a whole piece to copy it (in Faces mode it would extrude). It turns round its moved pivot.", a );
			}
		}

		// ─────────────────────────────── 6. Mesh Editing 4 ───────────────────────────────

		static void MeshEditing4( Context c )
		{
			const string m = "Dev Measure Green";
			var cube = new S.Vector3( 96, 96, 96 );

			// Local axes: a turned beam extended along itself
			{
				var beam = Block( c, "Local Axes", At( 0, 24 ), new S.Vector3( 64, 48, 48 ), m, yaw: 30 );
				SelectFaces( c, FaceFacing( beam, new S.Vector3( 1, 0, 0 ) ) );
				var face = (MeshFace)c.Tool.SelectedFaces.First();
				c.Tool.ScriptedMove( face.NormalWorld * 64 * Inch, extrude: true );
				Done( c, beam, "Local Axes", "Tab turns the gizmo to the selection's own axes, so Shift+drag carries on along a turned piece. Tab again for the world." );
			}

			// Workplane: a block standing square on a tilted one
			{
				var slab = Block( c, "Workplane", At( 1, 24 ), new S.Vector3( 128, 128, 16 ), m, roll: 20 );
				var post = Block( c, "Workplane Post", At( 1, 24 ), new S.Vector3( 32, 32, 64 ), "Dev Measure Orange", roll: 20 );
				post.transform.position = slab.transform.position + slab.transform.up * (8 + 32) * Inch;
				Refresh( post );
				AddStation( c, "Workplane", "Shift+Q, click a surface: the grid lies on it, so new pieces sit square to it. Shift+Q then Esc puts it back.", slab );
			}

			// The clipping tool's three ways
			foreach ( var (slot, keep, name) in new[] { (2, ClipTool.KeepMode.Front, "Clip: Keep One Side"), (3, ClipTool.KeepMode.Both, "Clip: Keep Both") } )
			{
				var box = Block( c, name, At( slot, 48 ), cube, m );
				ClipTool.Clip( box, box.Mesh, box.SourceToWorld( S.Vector3.Zero ), Dir( c, new S.Vector3( 1, 1, 0 ) ).normalized, keep, null, new List<(Vector3, Vector3)>() );
				Refresh( box );
				AddStation( c, name, "Shift+X in Meshes mode slices the mesh; the tool's buttons (or Shift+X again) keep one side or both.", box );
			}
			{
				var box = Block( c, "Clip: Faces", At( 4, 48 ), cube, m );
				var top = new HashSet<HalfEdgeMesh.FaceHandle>( FaceFacing( box, new S.Vector3( 0, 0, 1 ) ).Select( f => ((MeshFace)f).Handle ) );
				ClipTool.Clip( box, box.Mesh, box.SourceToWorld( S.Vector3.Zero ), Dir( c, new S.Vector3( 1, 0, 0 ) ), ClipTool.KeepMode.Both, top, new List<(Vector3, Vector3)>() );
				Refresh( box );
				SelectFaces( c, FacesWhere( box, p => Near( p.z, 48 ) && p.x > 0 ) );
				c.Tool.ScriptedMove( Vector3.up * 24 * Inch, extrude: true );
				Done( c, box, "Clip: Faces", "In Faces mode the clipping tool only splits the selected faces: a quick way to add a straight edge. One half was then extruded." );
			}

			// Mirror: a new mesh, or faces kept in the same mesh
			{
				var profile = new[] { new S.Vector3( 0, -40, -40 ), new S.Vector3( 0, 40, -40 ), new S.Vector3( 0, 40, 40 ) };
				var wedge = Prism( c, "Mirror: Same Mesh", At( 5, 40 ) + new S.Vector3( 0, -40, 0 ), profile, new S.Vector3( 96, 0, 0 ), m );
				var copyMesh = S.PolygonMesh.FromData( wedge.Mesh.ToData() );
				copyMesh.FlipAllFaces();
				copyMesh.Scale( new S.Vector3( 1, -1, 1 ) );
				var copy = Place( c, "Mirror: Same Mesh (Copy)", copyMesh, At( 5, 40 ) + new S.Vector3( 0, 40, 0 ), m );
				SelectObjects( c, wedge, copy );
				c.Tool.MergeMeshes();
				AddStation( c, "Mirror: Same Mesh", "Shift+F in Faces mode: the mirrored faces join the same mesh (they needn't touch).", wedge );
			}

			// Bevel: rounded and chamfered, the size from the grid
			{
				var box = Block( c, "Bevel: Rounded", At( 6, 48 ), cube, m );
				var (grid, segments) = (HammerSettings.GridSize, HammerSettings.BevelSegments);
				HammerSettings.GridSize = 24;
				HammerSettings.BevelSegments = 4;
				SelectEdges( c, EdgesWhere( box, ( a, b ) => Near( a.x, b.x ) && Near( a.y, b.y ) ) );
				c.Tool.QuickBevelEdges();
				(HammerSettings.GridSize, HammerSettings.BevelSegments) = (grid, segments);
				Done( c, box, "Bevel: Rounded", "Edges mode, F: rounds the edges. The width comes from the grid size, Segments from Tool Properties." );
			}
			{
				var box = Block( c, "Bevel: Chamfer", At( 7, 48 ), cube, m );
				var (grid, segments) = (HammerSettings.GridSize, HammerSettings.BevelSegments);
				HammerSettings.GridSize = 16;
				HammerSettings.BevelSegments = 1;
				SelectEdges( c, EdgesWhere( box, ( a, b ) => Near( a.z, 48 ) && Near( b.z, 48 ) ) );
				c.Tool.QuickBevelEdges();
				(HammerSettings.GridSize, HammerSettings.BevelSegments) = (grid, segments);
				Done( c, box, "Bevel: Chamfer", "Bevel with one segment cuts the corners flat. A bigger grid makes a bigger bevel." );
			}
		}

		// ─────────────────────────────── 7. Mesh Texturing (after the Materials stations) ───────────────────────────────

		static void TexturingMore( Context c )
		{
			AddStation( c, "Texture Lock", "Toolbar: with Texture Lock on, moving a mesh carries its texture along; off, the texture stays put in the world.",
				Block( c, "Texture Lock", At( 8, 48 ), new S.Vector3( 96, 96, 96 ), "Dev Measure Orange" ) );
			AddStation( c, "Texture Scale Lock", "Toolbar: with Texture Scale Lock on, scaling a mesh stretches its texture too.",
				Block( c, "Texture Scale Lock", At( 9, 48 ), new S.Vector3( 160, 64, 96 ), "Dev Measure Blue" ) );

			// UV peel: the texture made to flow round an arch
			{
				var arch = Tunnel( c, "UV Peel", At( 10, 0 ) + new S.Vector3( 0, -40, 0 ), false );
				SelectEdges( c, FarEnd( c, arch ) );
				c.Tool.PeelUVs();
				c.Tool.Selection.Clear();
				AddStation( c, "UV Peel", "Edges mode: select an edge loop running round the curve, UV Peel (bottom of Tool Properties). World Space keeps the scale.", arch );
			}
		}

		// ─────────────────────────────── 8. Creating your first room ───────────────────────────────

		static void FirstRoom( Context c )
		{
			const string m = "Dev Measure Light";
			var size = new S.Vector3( 256, 256, 128 );
			S.Vector3 Spot( int i ) => new( -300 + (i % 3) * 300, i < 3 ? 260 : -260, 64 );

			HammerMesh MakeRoom( int i, bool flipped )
			{
				var box = Block( c, $"Room {i + 1}", Spot( i ), size, m );
				if ( flipped )
				{
					SelectFaces( c, AllFaces( box ) );
					c.Tool.FlipFaces();
					c.Tool.Selection.Clear();
				}
				return box;
			}

			AddStation( c, "Room: Block", "Step 1. Block tool (Shift+B): drag out 256 x 256, drag the top ball up to 128, Enter.", MakeRoom( 0, false ) );
			AddStation( c, "Room: Flipped", "Step 2. Faces mode, double-click it, Flip (F): four walls, a floor and a ceiling, seen from inside.", MakeRoom( 1, true ) );

			// A light and a spawn point
			{
				var room = MakeRoom( 2, true );
				var light = new GameObject( "Room Light" );
				Undo.RegisterCreatedObjectUndo( light, "Room Light" );
				light.transform.SetParent( c.Root.transform, false );
				light.transform.position = SourceSpace.ToUnityPosition( c.ToWorld( Spot( 2 ) + new S.Vector3( 0, 0, 40 ) ) );
				var l = light.AddComponent<Light>();
				l.type = LightType.Point;
				l.range = 300 * Inch;
				l.intensity = 2;
				Block( c, "Player Start", Spot( 2 ) + new S.Vector3( -64, 0, -28 ), new S.Vector3( 32, 32, 72 ), "Dev Measure Green" );
				AddStation( c, "Room: Light and Spawn", "Step 3. Hammer's Entity tool places info_player_start and light_omni. In Unity: GameObject > Light > Point Light, and your player prefab.", room );
			}

			// A doorway with the clipping tool: two upright cuts and one across, keeping both sides
			{
				var room = MakeRoom( 3, true );
				HashSet<HalfEdgeMesh.FaceHandle> Wall() => new( room.Mesh.FaceHandles.Where( f => Near( room.Mesh.GetFaceCenter( f ).x, -128 ) ) );
				var left = Dir( c, new S.Vector3( 0, 1, 0 ) );
				foreach ( var y in new[] { -24f, 24f } )
					ClipTool.Clip( room, room.Mesh, room.SourceToWorld( new S.Vector3( -128, y, 0 ) ), left, ClipTool.KeepMode.Both, Wall(), new List<(Vector3, Vector3)>() );
				var between = new HashSet<HalfEdgeMesh.FaceHandle>( Wall().Where( f => System.Math.Abs( room.Mesh.GetFaceCenter( f ).y ) < 1 ) );
				ClipTool.Clip( room, room.Mesh, room.SourceToWorld( new S.Vector3( -128, 0, 96 - 64 ) ), Vector3.up, ClipTool.KeepMode.Both, between, new List<(Vector3, Vector3)>() );
				Refresh( room );
				SelectFaces( c, FacesWhere( room, p => Near( p.x, -128 ) && System.Math.Abs( p.y ) < 1 && p.z < 32 ) );
				c.Tool.Delete();
				Done( c, room, "Room: Doorway (Clip)", "Step 4a. Faces mode, select the wall, Shift+X: an upright cut keeping both sides, another 48 along, one across at 96 up. Delete the face in the gap." );
			}

			// A doorway with the cut tool: traced round in one go
			{
				var room = MakeRoom( 4, true );
				var wall = (MeshFace)FacesWhere( room, p => Near( p.x, -128 ) ).First();
				var bottom = (MeshEdge)EdgesWhere( room, ( a, b ) => Near( a.x, -128 ) && Near( b.x, -128 ) && Near( a.z, -64 ) && Near( b.z, -64 ) ).First();
				var cut = new EdgeCutTool();
				c.Tool.BeginSubTool( cut );
				cut.AddEdgePoint( wall, bottom, room.SourceToWorld( new S.Vector3( -128, -24, -64 ) ) );
				cut.AddFacePoint( wall, room.SourceToWorld( new S.Vector3( -128, -24, 32 ) ) );
				cut.AddFacePoint( wall, room.SourceToWorld( new S.Vector3( -128, 24, 32 ) ) );
				cut.AddEdgePoint( wall, bottom, room.SourceToWorld( new S.Vector3( -128, 24, -64 ) ) );
				cut.Apply();
				// The face inside the traced outline (the rest of the wall wraps round it)
				SelectFaces( c, room.Mesh.FaceHandles.Where( f => Near( room.Mesh.GetFaceCenter( f ).x, -128 ) &&
					room.Mesh.GetFaceVertices( f ).All( v => System.Math.Abs( room.Mesh.GetVertexPosition( v ).y ) < 24.5f ) )
					.Select( f => (IMeshElement)new MeshFace( room, f ) ) );
				c.Tool.Delete();
				Done( c, room, "Room: Doorway (Cut Tool)", "Step 4b. Faces mode, C: click the floor edge, up 96, across 48, back down to the edge, Enter. Delete the face inside." );
			}
		}

		// ─────────────────────────────── 9. Prefabs and instances, 10. Visibility ───────────────────────────────

		static void PrefabsAndVisibility( Context c )
		{
			// Instances: three pillars kept the same; the first was bevelled and the others followed
			{
				var pillars = new List<HammerMesh>();
				foreach ( var y in new[] { 96f, 0f, -96f } )
					pillars.Add( Block( c, y == 96 ? "Instances" : "Instance", Slot( 0 ) + new S.Vector3( 0, y, 80 ), new S.Vector3( 48, 48, 160 ), "Dev Measure Orange" ) );
				HammerInstances.MakeInstances( pillars, pillars[0] );

				var (grid, segments) = (HammerSettings.GridSize, HammerSettings.BevelSegments);
				HammerSettings.GridSize = 8;
				HammerSettings.BevelSegments = 1;
				SelectEdges( c, EdgesWhere( pillars[0], ( a, b ) => Near( a.x, b.x ) && Near( a.y, b.y ) ) );
				c.Tool.QuickBevelEdges();
				(HammerSettings.GridSize, HammerSettings.BevelSegments) = (grid, segments);
				c.Tool.Selection.Clear();
				foreach ( var p in pillars ) Refresh( p );
				AddStation( c, "Instances", "Meshes mode, select copies then Make Instance (Tool Properties): edit one and the rest follow. Collapse makes them separate again.", pillars[0] );
			}

			AddStation( c, "Prefabs", "Hammer's prefabs are Unity prefabs: drag objects into the Project window, then place copies. Double-click one to edit it.",
				Block( c, "Prefabs", At( 2, 40 ), new S.Vector3( 96, 96, 80 ), "Dev Measure Blue" ) );

			AddStation( c, "Selection Sets", "Ctrl+R makes a named set from the selection (Window > Hammer > Selection Sets): select, hide or show it in one click.",
				Block( c, "Selection Sets", At( 3, 40 ), new S.Vector3( 96, 96, 80 ), "Dev Measure Yellow" ) );

			// Visibility
			AddStation( c, "Walls Block Sight", "Big solid meshes hide what's behind them. In Unity: mark them Occluder Static and bake Window > Rendering > Occlusion Culling.",
				Block( c, "Walls Block Sight", At( 8, 96 ), new S.Vector3( 16, 192, 192 ), "Dev Measure Dark" ) );
			AddStation( c, "Seen Through", "Windows, fences and small props shouldn't hide things: leave them Occludee only.",
				Block( c, "Seen Through", At( 9, 64 ), new S.Vector3( 16, 128, 128 ), "Dev Reflectivity 30" ) );
			AddStation( c, "Hide While Working", "H hides the selection, U shows everything again; the eye in the Outliner does the same for whole objects.",
				Block( c, "Hide While Working", At( 10, 48 ), new S.Vector3( 96, 96, 96 ), "Dev Measure Grey" ) );
		}
	}
}
