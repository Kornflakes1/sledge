using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Step-by-step help inside the Hammer window: a tour of the window the first time it opens,
	/// and "How do I..." guides for making things. Each step is a short popup; steps that can see
	/// you do them tick themselves off.
	/// </summary>
	public static class HammerGuides
	{
		public sealed class Step
		{
			public string Text;

			/// <summary>Ticks the step off by itself when it returns true (null: press Next).</summary>
			public Func<Context, bool> Done;

			/// <summary>A part of the window to outline (see <see cref="HammerWindow"/>'s guide areas).</summary>
			public string Highlight;
		}

		public sealed class Guide
		{
			public string Title;
			public string Keywords = "";
			public List<Step> Steps = new();
		}

		/// <summary>What a step can check, and what things were like when the guide started.</summary>
		public sealed class Context
		{
			public HammerMeshTool Tool;
			public int MeshesAtStart;
			public int FacesAtStart;

			public EditMode Mode => Tool.Mode;
			public SubTool SubTool => Tool.SubTool;
			public int Meshes => UnityEngine.Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ).Length;
			// Counted at most a few times a second: steps are checked on every repaint
			int _faces;
			double _facesAt = -1;
			public int Faces
			{
				get
				{
					var now = EditorApplication.timeSinceStartup;
					if ( _facesAt < 0 || now - _facesAt > 0.25 )
					{
						_faces = UnityEngine.Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ).Sum( c => c.Mesh?.FaceHandles.Count() ?? 0 );
						_facesAt = now;
					}
					return _faces;
				}
			}
			public void Recount() => _facesAt = -1;
			public bool MadeAMesh => Meshes > MeshesAtStart;
			public bool FacesChanged => Faces != FacesAtStart;
			public int SelectedFaces => Tool.SelectedFaces.Count();
			public int SelectedEdges => Tool.SelectedEdges.Count();
			public int SelectedObjects => UnityEditor.Selection.gameObjects.Count( g => g.GetComponent<HammerMesh>() != null );
		}

		static Guide _current;
		static int _step;
		static Context _context;
		static double _tickedAt;

		public static bool Running => _current != null;

		const string TourSeenKey = "HammerUnity.TourSeen";

		/// <summary>Start the tour the first time the window opens.</summary>
		public static void FirstRun( HammerMeshTool tool )
		{
			if ( HammerSettings.Isolated || EditorPrefs.GetBool( TourSeenKey, false ) ) return;
			EditorPrefs.SetBool( TourSeenKey, true );
			Start( Tour, tool );
		}

		public static void Start( Guide guide, HammerMeshTool tool )
		{
			_picking = false;
			_current = guide;
			_step = 0;
			_context = new Context { Tool = tool };
			Rebase();
			HammerViews.RepaintAll();
		}

		public static void Stop()
		{
			_current = null;
			_picking = false;
			HammerViews.RepaintAll();
		}

		// What "made a mesh" or "changed the faces" is measured against: the start of each step
		static void Rebase()
		{
			if ( _context?.Tool == null ) return;
			_context.MeshesAtStart = _context.Meshes;
			_context.Recount();
			_context.FacesAtStart = _context.Faces;
		}

		static void Go( int step )
		{
			_step = Mathf.Clamp( step, 0, _current.Steps.Count - 1 );
			Rebase();
			HammerViews.RepaintAll();
		}

		// ── The popup ──

		const float Width = 340;
		static GUIStyle _text, _title, _small;

		static Rect PanelRect( Rect area, float height ) => new( area.xMax - Width - 12, area.yMax - height - 12, Width, height );

		static float Height()
		{
			Styles();
			var step = _current.Steps[_step];
			var text = step.Text + (step.Done != null ? "\nDo it and this moves on by itself." : "");
			return 30 + _text.CalcHeight( new GUIContent( text ), Width - 24 ) + 44;
		}

		static void Styles()
		{
			_text ??= new GUIStyle( EditorStyles.wordWrappedLabel ) { richText = true, fontSize = 12, normal = { textColor = new Color( 0.9f, 0.9f, 0.9f ) } };
			_title ??= new GUIStyle( EditorStyles.boldLabel ) { normal = { textColor = Color.white } };
			_small ??= new GUIStyle( EditorStyles.miniLabel ) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color( 0.65f, 0.65f, 0.65f ) } };
		}

		// Help > How do I...: the list of guides, in the same corner as the steps
		static bool _picking;
		static HammerMeshTool _pickTool;
		static Vector2 _pickScroll;
		const float PickerHeight = 380;

		public static void OpenPicker( HammerMeshTool tool )
		{
			_picking = true;
			_pickTool = tool;
			_current = null;
			HammerViews.RepaintAll();
		}

		static bool Showing => _current != null || _picking;

		static Rect CurrentRect( Rect area ) => PanelRect( area, _picking ? PickerHeight : Height() );

		/// <summary>
		/// Before the views: the popup's buttons take their clicks, and clicks on the popup don't
		/// fall through to the view under it.
		/// </summary>
		public static void Input( Rect area )
		{
			var e = Event.current;
			if ( !Showing || e.type == EventType.Repaint || e.type == EventType.Layout ) return;
			var rect = CurrentRect( area );
			if ( !rect.Contains( e.mousePosition ) ) return;
			// Hit tested by hand: GUI controls here would use up control ids on some events and
			// not others, and the views' own controls (placing a block...) would stop working
			if ( e.type == EventType.ScrollWheel && _picking ) _pickScroll.y = Mathf.Max( 0, _pickScroll.y + e.delta.y * 12 );
			if ( e.type == EventType.MouseUp && e.button == 0 ) Contents( rect, false );
			if ( e.isMouse || e.type == EventType.ScrollWheel ) e.Use();
		}

		/// <summary>After the views: check the step, outline what it's about, draw the popup.</summary>
		public static void Draw( Rect area, Func<string, Rect?> areaOf )
		{
			if ( !Showing || Event.current.type != EventType.Repaint ) return;

			// The views leave Handles' camera set up: back to plain window drawing, as the view
			// labels do
			Handles.BeginGUI();
			try
			{
				if ( _current != null )
				{
					// A step that sees itself done moves on (after a moment, so the tick shows)
					var step = _current.Steps[_step];
					if ( step.Done != null && _tickedAt <= 0 && SafeDone( step ) )
						_tickedAt = EditorApplication.timeSinceStartup;
					if ( _tickedAt > 0 && EditorApplication.timeSinceStartup - _tickedAt > 0.6 )
					{
						_tickedAt = 0;
						if ( _step < _current.Steps.Count - 1 ) Go( _step + 1 );
						step = _current.Steps[_step];
					}
					if ( _tickedAt > 0 ) HammerViews.RepaintAll();

					if ( step.Highlight != null && areaOf( step.Highlight ) is Rect h )
						Outline( h, new Color( 1.0f, 0.6f, 0.15f ), 2 );
				}

				var rect = CurrentRect( area );
				EditorGUI.DrawRect( new Rect( rect.x + 3, rect.y + 3, rect.width, rect.height ), new Color( 0, 0, 0, 0.35f ) );
				EditorGUI.DrawRect( rect, new Color( 0.13f, 0.13f, 0.14f, 0.97f ) );
				EditorGUI.DrawRect( new Rect( rect.x, rect.y, rect.width, 3 ), new Color( 1.0f, 0.6f, 0.15f ) );
				Contents( rect, true );
			}
			finally
			{
				Handles.EndGUI();
			}
		}

		/// <summary>
		/// What's in the popup: the current step, or the list of guides. Drawn on repaint; on a
		/// click (drawing false) it only works out what was clicked.
		/// </summary>
		static void Contents( Rect rect, bool drawing )
		{
			Styles();
			var mouse = Event.current.mousePosition;
			bool Hit( Rect r ) => !drawing && r.Contains( mouse );

			var close = new Rect( rect.xMax - 28, rect.y + 6, 20, 18 );
			if ( drawing ) GUI.Button( close, "✕", EditorStyles.miniButton );
			if ( Hit( close ) ) { Stop(); return; }

			if ( _picking )
			{
				var list = new Rect( rect.x + 8, rect.y + 30, rect.width - 16, rect.height - 38 );
				var inner = new Rect( 0, 0, list.width - 16, All.Count * 24 );
				_pickScroll.y = Mathf.Clamp( _pickScroll.y, 0, Mathf.Max( 0, inner.height - list.height ) );

				if ( !drawing )
				{
					if ( !list.Contains( mouse ) ) return;
					var index = Mathf.FloorToInt( (mouse.y - list.y + _pickScroll.y) / 24 );
					if ( index >= 0 && index < All.Count )
						Start( All[index], _pickTool ?? HammerMeshTool.Focused );
					return;
				}

				GUI.Label( new Rect( rect.x + 12, rect.y + 6, rect.width - 50, 20 ), "How do I...", _title );
				_pickScroll = GUI.BeginScrollView( list, _pickScroll, inner );
				for ( int i = 0; i < All.Count; i++ )
				{
					var row = new Rect( 0, i * 24, inner.width, 22 );
					if ( row.Contains( Event.current.mousePosition ) )
						EditorGUI.DrawRect( row, new Color( 0.24f, 0.37f, 0.6f ) );
					GUI.Label( row, "  " + All[i].Title, _text );
				}
				GUI.EndScrollView();
				return;
			}

			var step = _current.Steps[_step];
			var back = new Rect( rect.x + 12, rect.yMax - 32, 70, 22 );
			var next = new Rect( rect.xMax - 92, rect.yMax - 32, 80, 22 );
			var last = _step == _current.Steps.Count - 1;

			if ( !drawing )
			{
				if ( Hit( back ) && _step > 0 ) Go( _step - 1 );
				else if ( Hit( next ) )
				{
					if ( last ) Stop();
					else Go( _step + 1 );
				}
				return;
			}

			GUI.Label( new Rect( rect.x + 12, rect.y + 6, rect.width - 110, 20 ), _current.Title, _title );
			GUI.Label( new Rect( rect.xMax - 110, rect.y + 6, 80, 20 ), $"Step {_step + 1} of {_current.Steps.Count}", _small );

			var text = step.Text;
			if ( _tickedAt > 0 ) text = "<color=#5f5>✓</color> " + text;
			else if ( step.Done != null ) text += "\n<color=#999><i>Do it and this moves on by itself.</i></color>";
			GUI.Label( new Rect( rect.x + 12, rect.y + 28, rect.width - 24, rect.height - 70 ), text, _text );

			GUI.enabled = _step > 0;
			GUI.Button( back, "Back" );
			GUI.enabled = true;
			GUI.Button( next, last ? "Finish" : "Next" );
		}

		static bool SafeDone( Step step )
		{
			try { return _context.Tool != null && step.Done( _context ); }
			catch ( Exception ) { return false; }
		}

		static void Outline( Rect r, Color color, float width )
		{
			EditorGUI.DrawRect( new Rect( r.x, r.y, r.width, width ), color );
			EditorGUI.DrawRect( new Rect( r.x, r.yMax - width, r.width, width ), color );
			EditorGUI.DrawRect( new Rect( r.x, r.y, width, r.height ), color );
			EditorGUI.DrawRect( new Rect( r.xMax - width, r.y, width, r.height ), color );
		}

		// ── The guides ──

		static Step S( string text, Func<Context, bool> done = null, string highlight = null ) => new() { Text = text, Done = done, Highlight = highlight };

		public static readonly Guide Tour = new()
		{
			Title = "Welcome to Hammer Mesh Tools",
			Steps =
			{
				S( "This window works like Source 2 Hammer. This quick tour points out where everything is. You can take it again from <b>Help > Take the Tour</b>." ),
				S( "The <b>menu bar</b>: Edit, View, Tools and Help, with every command and its key.", null, "menu" ),
				S( "The <b>selection modes</b>: Vertices (1), Edges (2), Faces (3) and Meshes (4). Most tools change with the mode.", null, "modes" ),
				S( "<b>World or local axes</b> (Tab), texture lock, and select-through.", null, "toggles" ),
				S( "<b>Grid and snapping</b>: grid size ([ and ]), the snap switches, and the angle snap for rotating.", null, "snap" ),
				S( "The <b>tool strip</b>: Select, Move (T), Rotate (R), Scale (E), Pivot (Insert), then the Block tool (Shift+B), Polygon (Shift+P), vertex paint, and the cutting tools.", null, "strip" ),
				S( "<b>Tool Properties</b> changes with the mode and tool: every operation is a button here, with its key beside it.", null, "panel" ),
				S( "The <b>views</b>: a 3D view and Top, Front and Side. In 3D hold the right mouse and use WASD to fly; Alt+drag orbits; the wheel zooms. Shift+Z maximises the view under the mouse.", null, "views" ),
				S( "The <b>status bar</b> says what's selected and how big it is, and warns if an edit breaks a face.", null, "status" ),
				S( "That's it. Whenever you forget how to make something, open <b>Help > How do I...</b> for a step-by-step guide. F1 lists every key." ),
			},
		};

		public static readonly List<Guide> All = new()
		{
			new Guide
			{
				Title = "Make a box", Keywords = "block cube brush wall floor",
				Steps =
				{
					S( "Press <b>Shift+B</b> (or the cube on the tool strip) for the Block tool.", c => c.Mode == EditMode.Primitive, "strip" ),
					S( "In Tool Properties set the <b>Shape</b> to Box.", c => HammerSettings.PrimitiveType == "Box", "panel" ),
					S( "In a view, <b>drag out the base</b>, let go, then move the mouse for the <b>height</b> and click. Drag the handles to adjust.", null, "views" ),
					S( "Press <b>Enter</b> to make it.", c => c.MadeAMesh ),
				},
			},
			new Guide
			{
				Title = "Make stairs", Keywords = "steps staircase",
				Steps =
				{
					S( "Press <b>Shift+B</b> for the Block tool.", c => c.Mode == EditMode.Primitive, "strip" ),
					S( "Set the <b>Shape</b> to Stairs in Tool Properties. The number of steps is there too.", c => HammerSettings.PrimitiveType == "Stairs", "panel" ),
					S( "Drag out the base, then the height, and press <b>Enter</b>.", c => c.MadeAMesh, "views" ),
				},
			},
			new Guide
			{
				Title = "Make an arch or doorway", Keywords = "door arch opening",
				Steps =
				{
					S( "Press <b>Shift+B</b> for the Block tool.", c => c.Mode == EditMode.Primitive, "strip" ),
					S( "Set the <b>Shape</b> to Doorway. Give it an <b>Arch Height</b> for a round top.", c => HammerSettings.PrimitiveType == "Doorway", "panel" ),
					S( "Drag it out and press <b>Enter</b>.", c => c.MadeAMesh, "views" ),
					S( "For an arch along existing edges instead: in Edges mode (2) select two edges and press <b>Y</b>." ),
				},
			},
			new Guide
			{
				Title = "Draw any shape (polygon)", Keywords = "outline floor plan custom",
				Steps =
				{
					S( "Press <b>Shift+P</b> for the Polygon tool.", c => c.SubTool is PolygonTool, "strip" ),
					S( "Click out the corners in a view, then click the first point (or press Enter) to close it.", null, "views" ),
					S( "Move the mouse for the height and click.", c => c.MadeAMesh ),
				},
			},
			new Guide
			{
				Title = "Extrude a face", Keywords = "pull push grow out",
				Steps =
				{
					S( "Press <b>3</b> for Faces mode.", c => c.Mode == EditMode.Face, "modes" ),
					S( "Click a face to select it.", c => c.SelectedFaces > 0, "views" ),
					S( "Press <b>T</b> for Move, then hold <b>Shift</b> and drag an arrow. Shift+drag always extrudes.", c => c.FacesChanged ),
				},
			},
			new Guide
			{
				Title = "Cut a doorway or window (Boolean)", Keywords = "hole subtract boolean opening window door",
				Steps =
				{
					S( "Make the wall, then a box where the hole goes (see <i>Make a box</i>)." ),
					S( "Press <b>4</b> for Meshes mode.", c => c.Mode == EditMode.Object, "modes" ),
					S( "Click the <b>cutting box</b>, then Ctrl+click the <b>wall</b> so the wall is selected last.", c => c.SelectedObjects >= 2, "views" ),
					S( "In Tool Properties press <b>Subtract</b> under Boolean.", c => c.FacesChanged || c.Meshes < c.MeshesAtStart, "panel" ),
				},
			},
			new Guide
			{
				Title = "Bevel an edge", Keywords = "round chamfer smooth corner",
				Steps =
				{
					S( "Press <b>2</b> for Edges mode.", c => c.Mode == EditMode.Edge, "modes" ),
					S( "Click the edge (double-click for the whole loop).", c => c.SelectedEdges > 0, "views" ),
					S( "Press <b>Alt+F</b> for the Bevel tool. Set the width and steps in Tool Properties (or [ and ] for steps).", c => c.SubTool is BevelTool, "panel" ),
					S( "Press <b>Enter</b> to keep it. (Plain <b>F</b> does a quick bevel without the tool.)", c => c.SubTool == null && c.FacesChanged ),
				},
			},
			new Guide
			{
				Title = "Cut new edges (knife)", Keywords = "edge cut split knife loop",
				Steps =
				{
					S( "Press <b>C</b> for Edge Cut (in Vertices, Edges or Faces mode).", c => c.SubTool is EdgeCutTool ),
					S( "Click points on edges and faces to draw the cut. Hold Shift to line them up.", null, "views" ),
					S( "Press <b>Enter</b> to cut. For a cut right round a strip of faces, press <b>V</b> first and click one edge.", c => c.FacesChanged ),
				},
			},
			new Guide
			{
				Title = "Slice a mesh (clip)", Keywords = "clip slice split half",
				Steps =
				{
					S( "Select the mesh in Meshes mode (4) or some faces in Faces mode (3).", c => c.SelectedObjects > 0 || c.SelectedFaces > 0 ),
					S( "Press <b>Shift+X</b> for the Clipping tool.", c => c.SubTool is ClipTool ),
					S( "Drag a line across the mesh. <b>Shift+X</b> again picks which side to keep; G and F turn the plane.", null, "views" ),
					S( "Press <b>Enter</b> to clip.", c => c.SubTool == null && c.FacesChanged ),
				},
			},
			new Guide
			{
				Title = "Texture a face", Keywords = "material paint texture apply",
				Steps =
				{
					S( "Press <b>3</b> for Faces mode.", c => c.Mode == EditMode.Face, "modes" ),
					S( "Pick a material in Tool Properties (or Shift+right-click a face to pick one up).", null, "panel" ),
					S( "Select faces and press <b>Shift+T</b>, or Ctrl+right-click a face to paint it." ),
				},
			},
			new Guide
			{
				Title = "Move the pivot", Keywords = "pivot origin rotate around centre",
				Steps =
				{
					S( "Select what you want to turn or scale." ),
					S( "Press <b>Insert</b> (or the pivot button) and drag the diamond. Or hold <b>Tab</b> and click where the pivot should go.", null, "strip" ),
					S( "Rotate (R) or scale (E) now turns round that point. In Meshes mode, Ctrl+D makes it the object's origin." ),
				},
			},
			new Guide
			{
				Title = "Make a curved corridor", Keywords = "curve bend corridor ring repeat",
				Steps =
				{
					S( "In Faces mode select the end face of a box.", c => c.SelectedFaces > 0 ),
					S( "Hold <b>Tab</b> and click off to the side: the middle of the curve.", null, "views" ),
					S( "Press <b>R</b>, then hold <b>Shift</b> and drag a ring 15 degrees: it extrudes round the pivot.", c => c.FacesChanged ),
					S( "Press <b>Shift+G</b> a few times to repeat it round the curve." ),
				},
			},
			new Guide
			{
				Title = "Mirror a mesh", Keywords = "mirror symmetry flip copy",
				Steps =
				{
					S( "Select the mesh (Meshes mode) or faces (Faces mode).", c => c.SelectedObjects > 0 || c.SelectedFaces > 0 ),
					S( "Press <b>Shift+F</b> for the Mirror tool and pick the axis and plane in Tool Properties.", c => c.SubTool is MirrorTool, "panel" ),
					S( "Press <b>Enter</b>.", c => c.SubTool == null ),
				},
			},
			new Guide
			{
				Title = "Sculpt terrain", Keywords = "displacement terrain ground hill sculpt",
				Steps =
				{
					S( "Make a flat box or quad for the ground, then subdivide it a few times (Subdivision in Tool Properties) so there's something to shape." ),
					S( "Press <b>Shift+D</b> for the Displacement tool.", c => c.SubTool is DisplacementTool ),
					S( "Pick a brush and drag on the ground. Ctrl reverses, Shift smooths, Ctrl+wheel resizes.", null, "panel" ),
					S( "Press <b>Enter</b> to keep the strokes (Esc throws them all away)." ),
				},
			},
			new Guide
			{
				Title = "Fill a hole", Keywords = "cap close fill hole open",
				Steps =
				{
					S( "Press <b>2</b> for Edges mode.", c => c.Mode == EditMode.Edge, "modes" ),
					S( "Double-click an edge round the hole to select the whole loop.", c => c.SelectedEdges > 2 ),
					S( "Press <b>P</b> to fill it.", c => c.FacesChanged ),
				},
			},
			new Guide
			{
				Title = "Join two faces (bridge)", Keywords = "bridge connect join tunnel",
				Steps =
				{
					S( "Press <b>3</b> for Faces mode and select two faces that face each other.", c => c.SelectedFaces == 2, "views" ),
					S( "Press <b>B</b> to bridge them (Alt+B for the Bridge tool with more options).", c => c.FacesChanged ),
				},
			},
		};
	}
}
