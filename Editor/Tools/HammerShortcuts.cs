using System;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using PolygonMesh = Sandbox.PolygonMesh;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Source 2 Hammer / s&amp;box mesh tool key bindings, active while the Hammer window has
	/// focus so they never clash with Unity's own. Rebind under Edit &gt; Shortcuts &gt; Hammer.
	/// </summary>
	static class HammerShortcuts
	{
		static HammerMeshTool Tool => HammerMeshTool.Focused;

		/// <summary>
		/// Run an edit, remembering it for Repeat Last (Shift+G).
		/// </summary>
		static void Run( Action<HammerMeshTool> action, [System.Runtime.CompilerServices.CallerMemberName] string name = "" )
		{
			var tool = Tool;
			if ( tool == null ) return;
			tool.Remember( Pretty( name ), () => action( Tool ?? tool ) );
			HammerPanel.StepUndo( Pretty( name ) );
			action( tool );
			HammerPanel.EndStep();
			HammerViews.RepaintAll();
		}

		/// <summary>
		/// Run something that isn't an edit (modes, tools, selection): not remembered for Repeat Last.
		/// </summary>
		static void View( Action<HammerMeshTool> action )
		{
			var tool = Tool;
			if ( tool == null ) return;
			HammerPanel.StepUndo( "Hammer" );
			action( tool );
			HammerPanel.EndStep();
			HammerViews.RepaintAll();
		}

		/// <summary>
		/// The edit for the current mode, remembered for Repeat Last.
		/// </summary>
		static void ByMode( Action vertex = null, Action edge = null, Action face = null, Action obj = null, [System.Runtime.CompilerServices.CallerMemberName] string name = "" )
		{
			var tool = Tool;
			if ( tool == null ) return;

			var action = tool.Mode switch
			{
				EditMode.Vertex => vertex,
				EditMode.Edge => edge,
				EditMode.Face => face,
				EditMode.Object => obj,
				_ => null,
			};

			if ( action == null ) return;
			tool.Remember( Pretty( name ), action );
			HammerPanel.StepUndo( Pretty( name ) );
			action();
			HammerPanel.EndStep();
			HammerViews.RepaintAll();
		}

		static string Pretty( string member ) => member.StartsWith( "Key" ) ? member.Substring( 3 ) : System.Text.RegularExpressions.Regex.Replace( member, "(?<=[a-z])(?=[A-Z])", " " );

		// ── Undo / redo ──
		// Inside the Hammer window these come first: an open tool (bevel, clip, a shape being
		// drawn) is cancelled instead of undoing whatever was done before it was opened

		[Shortcut( "Hammer/Undo", typeof( HammerWindow ), KeyCode.Z, ShortcutModifiers.Action )]
		static void HammerUndo()
		{
			if ( Tool?.SubTool != null && Tool.SubTool.UndoSettings() ) return;
			if ( Tool != null && Tool.CancelPending() ) { HammerViews.RepaintAll(); return; }
			Undo.PerformUndo();
		}

		[Shortcut( "Hammer/Redo", typeof( HammerWindow ), KeyCode.Y, ShortcutModifiers.Action )]
		static void HammerRedo()
		{
			if ( Tool?.SubTool != null ) { Tool.SubTool.RedoSettings(); return; }
			Undo.PerformRedo();
		}

		[Shortcut( "Hammer/Redo (Shift)", typeof( HammerWindow ), KeyCode.Z, ShortcutModifiers.Action | ShortcutModifiers.Shift )]
		static void HammerRedoShift() => HammerRedo();

		[Shortcut( "Hammer/Repeat Last", typeof( HammerWindow ), KeyCode.G, ShortcutModifiers.Shift )]
		static void RepeatLast() => View( t => t.RepeatLast() );

		// ── Modes ──

		[Shortcut( "Hammer/Vertex Mode", typeof( HammerWindow ), KeyCode.Alpha1 )]
		static void VertexMode() => View( t => t.Mode = EditMode.Vertex );

		[Shortcut( "Hammer/Edge Mode", typeof( HammerWindow ), KeyCode.Alpha2 )]
		static void EdgeMode() => View( t => t.Mode = EditMode.Edge );

		[Shortcut( "Hammer/Face Mode", typeof( HammerWindow ), KeyCode.Alpha3 )]
		static void FaceMode() => View( t => t.Mode = EditMode.Face );

		[Shortcut( "Hammer/Object Mode", typeof( HammerWindow ), KeyCode.Alpha4 )]
		static void ObjectMode() => View( t => t.Mode = EditMode.Object );

		[Shortcut( "Hammer/Vertex Paint", typeof( HammerWindow ), KeyCode.Alpha5 )]
		static void PaintMode() => View( t => t.Mode = EditMode.Paint );

		/// <summary>
		/// Space: cycle Vertices, Edges, Faces, Meshes like Hammer. While drawing a shape it
		/// confirms it, and in the clipping tool it applies and stays.
		/// </summary>
		[Shortcut( "Hammer/Cycle Selection Mode · Confirm (Space)", typeof( HammerWindow ), KeyCode.Space )]
		static void KeySpace() => View( t =>
		{
			if ( t.OnSpace() ) return;
			t.Mode = t.Mode switch
			{
				EditMode.Vertex => EditMode.Edge,
				EditMode.Edge => EditMode.Face,
				EditMode.Face => EditMode.Object,
				_ => EditMode.Vertex,
			};
		} );

		/// <summary>
		/// Q in Meshes mode: resize the selection with the box handles again (Hammer's re-enter
		/// mesh resizing). While flying, Q is "down".
		/// </summary>
		[Shortcut( "Hammer/Resize Mesh (Q)", typeof( HammerWindow ), KeyCode.Q )]
		static void KeyQ()
		{
			if ( HammerWindow.Flying ) { HammerWindow.FlyKeyDown( KeyCode.Q ); return; }
			ByMode( obj: () => Tool.MoveMode = MoveMode.Select );
		}

		[Shortcut( "Hammer/New Selection Set", typeof( HammerWindow ), KeyCode.R, ShortcutModifiers.Action )]
		static void NewSelectionSet() => HammerSelectionSetsWindow.CreateFromSelection();

		[Shortcut( "Hammer/Pick Workplane", typeof( HammerWindow ), KeyCode.Q, ShortcutModifiers.Shift )]
		static void PickWorkplane() => View( t => t.BeginWorkplanePick() );

		[Shortcut( "Hammer/Displacement Tool", typeof( HammerWindow ), KeyCode.D, ShortcutModifiers.Shift )]
		static void Displacement() => View( t => DisplacementTool.Open( t ) );

		[Shortcut( "Hammer/Vertex Paint (Shift+V)", typeof( HammerWindow ), KeyCode.V, ShortcutModifiers.Shift )]
		static void PaintShortcut() => View( t => t.Mode = EditMode.Paint );

		[Shortcut( "Hammer/Lift Material Under Cursor", typeof( HammerWindow ), KeyCode.M, ShortcutModifiers.Action )]
		static void LiftMaterial() => View( t => t.LiftMaterialUnderCursor() );

		// Tab (tap: world/local axes; hold and click: place the pivot) is read straight from the
		// key events in HammerWindow, since a shortcut can't tell a tap from a hold reliably

		[Shortcut( "Hammer/Object Properties", typeof( HammerWindow ), KeyCode.Return, ShortcutModifiers.Alt )]
		static void Properties()
		{
			if ( UnityEditor.Selection.activeObject != null )
				EditorUtility.OpenPropertyEditor( UnityEditor.Selection.activeObject );
		}

		[Shortcut( "Hammer/Maximize View", typeof( HammerWindow ), KeyCode.Z, ShortcutModifiers.Shift )]
		static void Maximize()
		{
			if ( EditorWindow.focusedWindow is HammerWindow window ) window.ToggleMaximize();
		}

		// Hammer: Ctrl+Space cycles the 2D view; Ctrl+A+Space (A held too) also centres it on
		// the selection. Ctrl+Shift+Space does the same for keyboards that can't take three keys
		[Shortcut( "Hammer/Cycle 2D View", typeof( HammerWindow ), KeyCode.Space, ShortcutModifiers.Action )]
		static void Cycle2D()
		{
			if ( EditorWindow.focusedWindow is HammerWindow window ) window.CycleHoveredView( KeyHeld( 'A' ) );
		}

#if UNITY_EDITOR_WIN
		[System.Runtime.InteropServices.DllImport( "user32.dll" )]
		static extern short GetAsyncKeyState( int key );

		static bool KeyHeld( char key ) => (GetAsyncKeyState( key ) & 0x8000) != 0;
#else
		static bool KeyHeld( char key ) => false;
#endif

		[Shortcut( "Hammer/Cycle 2D View and Centre on Selection", typeof( HammerWindow ), KeyCode.Space, ShortcutModifiers.Action | ShortcutModifiers.Shift )]
		static void Cycle2DCentred()
		{
			if ( EditorWindow.focusedWindow is HammerWindow window ) window.CycleHoveredView( true );
		}

		[Shortcut( "Hammer/Command List", typeof( HammerWindow ), KeyCode.F1 )]
		static void CommandList() => HammerCommandListWindow.Open();

		[Shortcut( "Hammer/Primitive Tool", typeof( HammerWindow ), KeyCode.B, ShortcutModifiers.Shift )]
		static void PrimitiveMode() => View( t => t.Mode = EditMode.Primitive );

		[Shortcut( "Hammer/Select", typeof( HammerWindow ), KeyCode.S, ShortcutModifiers.Shift )]
		static void SelectTool() => View( t => t.MoveMode = MoveMode.Select );

		static void View( ViewType type, ViewShading? shading )
		{
			if ( EditorWindow.focusedWindow is HammerWindow window ) window.SetHoveredView( type, shading );
		}

		[Shortcut( "Hammer/View Top", typeof( HammerWindow ), KeyCode.F2 )]
		static void ViewTop() => View( ViewType.Top, null );

		[Shortcut( "Hammer/View Front", typeof( HammerWindow ), KeyCode.F3 )]
		static void ViewFront() => View( ViewType.Front, null );

		[Shortcut( "Hammer/View Side", typeof( HammerWindow ), KeyCode.F4 )]
		static void ViewSide() => View( ViewType.Side, null );

		[Shortcut( "Hammer/View 3D Fullbright", typeof( HammerWindow ), KeyCode.F5 )]
		static void View3DFullbright() => View( ViewType.Perspective, ViewShading.Fullbright );

		[Shortcut( "Hammer/View 3D Lit", typeof( HammerWindow ), KeyCode.F6 )]
		static void View3DLit() => View( ViewType.Perspective, ViewShading.Lit );

		[Shortcut( "Hammer/Polygon Tool", typeof( HammerWindow ), KeyCode.P, ShortcutModifiers.Shift )]
		static void PolygonToolShortcut() => View( t => PolygonTool.Open( t ) );

		[Shortcut( "Hammer/Pivot", typeof( HammerWindow ), KeyCode.Insert )]
		static void PivotTool() => View( t => t.TogglePivotMode() );

		[Shortcut( "Hammer/Fly Mode", typeof( HammerWindow ), KeyCode.Z )]
		static void FlyMode()
		{
			var window = EditorWindow.focusedWindow as HammerWindow;
			if ( window != null ) window.ToggleFlyMode();
		}

		// Hammer's keys: T move (translate), R rotate, E scale
		[Shortcut( "Hammer/Move", typeof( HammerWindow ), KeyCode.T )]
		static void MoveTool() => View( t => t.MoveMode = MoveMode.Position );

		[Shortcut( "Hammer/Rotate", typeof( HammerWindow ), KeyCode.R )]
		static void RotateTool() => View( t => t.MoveMode = MoveMode.Rotate );

		[Shortcut( "Hammer/Scale", typeof( HammerWindow ), KeyCode.E )]
		static void ScaleTool()
		{
			// While flying in the 3D view (right mouse held), E is "up"
			if ( HammerWindow.Flying ) { HammerWindow.FlyKeyDown( KeyCode.E ); return; }
			if ( Tool?.SubTool is EdgeCutTool cut ) { cut.ToggleUniformOffset(); return; }
			View( t => t.MoveMode = MoveMode.Scale );
		}

		// ── Grid ──

		[Shortcut( "Hammer/Grid Smaller", typeof( HammerWindow ), KeyCode.LeftBracket )]
		static void GridSmaller()
		{
			var sub = Tool?.SubTool;
			var used = false;
			sub?.TrackSettings( () => used = sub.OnBracket( -1 ) );
			if ( used ) { HammerViews.RepaintAll(); return; }
			HammerSettings.GridSmaller();
		}

		[Shortcut( "Hammer/Grid Larger", typeof( HammerWindow ), KeyCode.RightBracket )]
		static void GridLarger()
		{
			var sub = Tool?.SubTool;
			var used = false;
			sub?.TrackSettings( () => used = sub.OnBracket( 1 ) );
			if ( used ) { HammerViews.RepaintAll(); return; }
			HammerSettings.GridLarger();
		}

		// ── Selection ──

		[Shortcut( "Hammer/Select All", typeof( HammerWindow ), KeyCode.A, ShortcutModifiers.Action )]
		static void SelectAll() => View( t => t.SelectAll() );

		[Shortcut( "Hammer/Invert Selection", typeof( HammerWindow ), KeyCode.I, ShortcutModifiers.Action )]
		static void InvertSelection() => View( t => t.InvertSelection() );

		[Shortcut( "Hammer/Grow Selection", typeof( HammerWindow ), KeyCode.KeypadPlus )]
		static void Grow() => View( t => t.GrowSelection() );

		[Shortcut( "Hammer/Shrink Selection", typeof( HammerWindow ), KeyCode.KeypadMinus )]
		static void Shrink() => View( t =>
		{
			// While typing an amount after a drag, numpad minus is part of the number
			if ( t.TypedValue != null ) t.TypeKey( KeyCode.KeypadMinus, '-' );
			else t.ShrinkSelection();
		} );

		[Shortcut( "Hammer/Select Loop", typeof( HammerWindow ), KeyCode.L )]
		static void Loop() => View( t => t.SelectLoop() );

		[Shortcut( "Hammer/Frame Selection", typeof( HammerWindow ), KeyCode.A, ShortcutModifiers.Shift )]
		static void Frame() => View( t => t.FrameSelection() );

		// Esc and Enter can't be shortcut bindings in Unity; HammerMeshTool handles them as key events.

		// ── Shared editing keys, dispatched by mode like s&box ──

		[Shortcut( "Hammer/Delete", typeof( HammerWindow ), KeyCode.Delete )]
		static void Delete() => Run( t => t.Delete() );

		[Shortcut( "Hammer/Snap To Grid", typeof( HammerWindow ), KeyCode.B, ShortcutModifiers.Action )]
		static void SnapToGrid() => Run( t => t.SnapToGrid() );

		[Shortcut( "Hammer/Bevel · Flip (F)", typeof( HammerWindow ), KeyCode.F )]
		static void KeyF()
		{
			if ( Tool?.SubTool is ClipTool clip ) { clip.RotatePlane( -1 ); return; }
			if ( Tool?.SubTool is EdgeCutTool cut ) { cut.ToggleFlipUniformOffset(); return; }
			KeyFByMode();
		}

		static void KeyFByMode() => ByMode(
			vertex: () => Tool.BevelVertices(),
			edge: () => Tool.QuickBevelEdges(),
			face: () => Tool.FlipFaces(),
			obj: () => Tool.FlipFaces() );

		[Shortcut( "Hammer/Merge (M)", typeof( HammerWindow ), KeyCode.M )]
		static void KeyM() => ByMode(
			vertex: () => Tool.MergeVertices(),
			edge: () => Tool.MergeEdges(),
			obj: () => Tool.MergeMeshes() );

		[Shortcut( "Hammer/Connect (V)", typeof( HammerWindow ), KeyCode.V )]
		static void KeyV()
		{
			if ( Tool?.SubTool is EdgeCutTool cut ) { cut.ToggleLoopMode(); return; }
			KeyVByMode();
		}

		static void KeyVByMode() => ByMode(
			vertex: () => Tool.ConnectVertices(),
			edge: () => Tool.ConnectEdges() );

		[Shortcut( "Hammer/Extend · Detach (N)", typeof( HammerWindow ), KeyCode.N )]
		static void KeyN() => ByMode(
			edge: () => Tool.ExtendEdges(),
			face: () => Tool.DetachFaces() );

		[Shortcut( "Hammer/Split · Extract · Separate (Alt+N)", typeof( HammerWindow ), KeyCode.N, ShortcutModifiers.Alt )]
		static void KeyAltN() => ByMode(
			edge: () => Tool.SplitEdges(),
			face: () => Tool.ExtractFaces(),
			obj: () => Tool.SeparateComponents() );

		[Shortcut( "Hammer/Snap To Vertex · Bridge (B)", typeof( HammerWindow ), KeyCode.B )]
		static void KeyB()
		{
			if ( Tool?.SubTool is EdgeCutTool cut ) { cut.PlaceCut(); return; }
			KeyBByMode();
		}

		static void KeyBByMode() => ByMode(
			vertex: () => Tool.SnapToLastVertex(),
			edge: () => Tool.BridgeEdges(),
			obj: () => Tool.SnapToLastSelected() );

		[Shortcut( "Hammer/Dissolve · Combine (Backspace)", typeof( HammerWindow ), KeyCode.Backspace )]
		static void KeyBackspace()
		{
			if ( Tool?.Mode == EditMode.Paint ) { Tool.FloodPaint( false ); return; }
			if ( Tool != null && Tool.TypedValue != null ) { Tool.TypeKey( KeyCode.Backspace, ' ' ); return; } // editing a typed amount
			if ( Tool?.SubTool is PathTool path ) { path.RemoveLastPoint(); return; }
			KeyBackspaceByMode();
		}

		[Shortcut( "Hammer/Clear Paint", typeof( HammerWindow ), KeyCode.Backspace, ShortcutModifiers.Shift )]
		static void ClearPaint()
		{
			if ( Tool?.Mode == EditMode.Paint ) Tool.FloodPaint( true );
		}

		static void KeyBackspaceByMode() => ByMode(
			edge: () => Tool.DissolveEdges(),
			face: () => Tool.CombineFaces() );

		[Shortcut( "Hammer/Collapse", typeof( HammerWindow ), KeyCode.O, ShortcutModifiers.Shift )]
		static void Collapse() => Run( t => t.Collapse() );

		[Shortcut( "Hammer/Fill Hole", typeof( HammerWindow ), KeyCode.P )]
		static void FillHole() => ByMode( edge: () => Tool.FillHole() );

		[Shortcut( "Hammer/Select Ring · Thicken (G)", typeof( HammerWindow ), KeyCode.G )]
		static void KeyG()
		{
			if ( Tool?.SubTool is ClipTool clip ) { clip.RotatePlane( 1 ); return; }
			if ( Tool?.SubTool is EdgeCutTool cut ) { cut.ToggleSelectionOnly(); return; }
			if ( Tool?.Mode == EditMode.Edge ) View( t => t.SelectRing() );
			else ByMode( face: () => Tool.ThickenFaces() );
		}

		[Shortcut( "Hammer/Select Ribs · Fast Texture Tool", typeof( HammerWindow ), KeyCode.G, ShortcutModifiers.Action )]
		static void Ribs() => View( t => { if ( t.Mode == EditMode.Edge ) t.SelectRibs(); else if ( t.Mode == EditMode.Face ) FastTextureWindow.Open( t ); } );

		// ── Texture nudges (face mode) ──

		[Shortcut( "Hammer/Texture Shift Left", typeof( HammerWindow ), KeyCode.LeftArrow, ShortcutModifiers.Alt )]
		static void TexLeft() => ByMode( face: () => Tool.NudgeTexture( Vector2.left ) );

		[Shortcut( "Hammer/Texture Shift Right", typeof( HammerWindow ), KeyCode.RightArrow, ShortcutModifiers.Alt )]
		static void TexRight() => ByMode( face: () => Tool.NudgeTexture( Vector2.right ) );

		[Shortcut( "Hammer/Texture Shift Up", typeof( HammerWindow ), KeyCode.UpArrow, ShortcutModifiers.Alt )]
		static void TexUp() => ByMode( face: () => Tool.NudgeTexture( Vector2.down ) );

		[Shortcut( "Hammer/Texture Shift Down", typeof( HammerWindow ), KeyCode.DownArrow, ShortcutModifiers.Alt )]
		static void TexDown() => ByMode( face: () => Tool.NudgeTexture( Vector2.up ) );

		[Shortcut( "Hammer/Texture Scale Down", typeof( HammerWindow ), KeyCode.LeftBracket, ShortcutModifiers.Alt )]
		static void TexSmaller() => ByMode( face: () => Tool.ScaleTexture( 0.5f ) );

		[Shortcut( "Hammer/Texture Scale Up", typeof( HammerWindow ), KeyCode.RightBracket, ShortcutModifiers.Alt )]
		static void TexLarger() => ByMode( face: () => Tool.ScaleTexture( 2.0f ) );

		[Shortcut( "Hammer/Texture Rotate Left", typeof( HammerWindow ), KeyCode.Comma, ShortcutModifiers.Alt )]
		static void TexRotLeft() => ByMode( face: () => Tool.RotateTexture( -HammerSettings.AngleSnap ) );

		[Shortcut( "Hammer/Texture Rotate Right", typeof( HammerWindow ), KeyCode.Period, ShortcutModifiers.Alt )]
		static void TexRotRight() => ByMode( face: () => Tool.RotateTexture( HammerSettings.AngleSnap ) );

		[Shortcut( "Hammer/Hard Normals · Hide Faces (H)", typeof( HammerWindow ), KeyCode.H )]
		static void KeyH() => ByMode(
			edge: () => Tool.SetEdgeNormals( PolygonMesh.EdgeSmoothMode.Hard ),
			face: () => Tool.HideFaces() );

		[Shortcut( "Hammer/Soft Normals", typeof( HammerWindow ), KeyCode.J )]
		static void KeyJ() => ByMode( edge: () => Tool.SetEdgeNormals( PolygonMesh.EdgeSmoothMode.Soft ) );

		[Shortcut( "Hammer/Default Normals", typeof( HammerWindow ), KeyCode.K )]
		static void KeyK() => ByMode( edge: () => Tool.SetEdgeNormals( PolygonMesh.EdgeSmoothMode.Default ) );

		[Shortcut( "Hammer/Unhide Faces", typeof( HammerWindow ), KeyCode.U )]
		static void Unhide() => View( t => t.UnhideFaces() );

		[Shortcut( "Hammer/Snap Edge To Edge", typeof( HammerWindow ), KeyCode.I )]
		static void SnapEdge() => ByMode( edge: () => Tool.SnapEdgeToEdge() );

		[Shortcut( "Hammer/Weld UVs", typeof( HammerWindow ), KeyCode.F, ShortcutModifiers.Action )]
		static void WeldUVs() => Run( t => t.WeldUVs() );

		[Shortcut( "Hammer/Inset", typeof( HammerWindow ), KeyCode.I, ShortcutModifiers.Shift )]
		static void Inset() => ByMode( face: () => EditorTools.InsetTool.Open( Tool ) );

		[Shortcut( "Hammer/Quad Slice", typeof( HammerWindow ), KeyCode.D, ShortcutModifiers.Action )]
		static void QuadSlice() => ByMode( face: () => Tool.QuadSlice(), obj: () => Tool.SetOriginToPivot() );

		[Shortcut( "Hammer/Clear Rotation and Scale", typeof( HammerWindow ), KeyCode.Keypad0, ShortcutModifiers.Action )]
		static void ClearRotationScale() => ByMode( obj: () => Tool.ClearRotationAndScale() );

		[Shortcut( "Hammer/Move Path Trace Down", typeof( HammerWindow ), KeyCode.Keypad2, ShortcutModifiers.Action )]
		static void TraceDown() => Run( t => t.MovePathTraceDown() );

		[Shortcut( "Hammer/Align to Target", typeof( HammerWindow ), KeyCode.T, ShortcutModifiers.Alt )]
		static void AlignToTarget() => ByMode( obj: () => Tool.AlignToTarget() );

		[Shortcut( "Hammer/Rotate to Target", typeof( HammerWindow ), KeyCode.R, ShortcutModifiers.Alt )]
		static void RotateToTarget() => ByMode( obj: () => Tool.RotateToTarget() );

		[Shortcut( "Hammer/Apply Material", typeof( HammerWindow ), KeyCode.T, ShortcutModifiers.Shift )]
		static void ApplyMaterial() => ByMode( face: () => Tool.ApplyMaterial() );

		[Shortcut( "Hammer/Bevel Tool", typeof( HammerWindow ), KeyCode.F, ShortcutModifiers.Alt )]
		static void BevelTool() => ByMode( edge: () => EditorTools.BevelTool.Open( Tool ) );

		[Shortcut( "Hammer/Clipping Tool", typeof( HammerWindow ), KeyCode.X, ShortcutModifiers.Shift )]
		static void ClipTool()
		{
			var tool = Tool;
			if ( tool == null ) return;

			if ( tool.SubTool is ClipTool clip )
				clip.CycleKeepMode();
			else if ( tool.Mode is EditMode.Face or EditMode.Object )
				EditorTools.ClipTool.Open( tool );
		}

		[Shortcut( "Hammer/Clip · Toggle Create Caps", typeof( HammerWindow ), KeyCode.X, ShortcutModifiers.Action | ShortcutModifiers.Shift )]
		static void ClipToggleCaps()
		{
			if ( Tool?.SubTool is ClipTool clip ) clip.ToggleCaps();
		}

		[Shortcut( "Hammer/Mirror Tool", typeof( HammerWindow ), KeyCode.F, ShortcutModifiers.Shift )]
		static void MirrorTool() => ByMode( face: () => EditorTools.MirrorTool.Open( Tool ), obj: () => EditorTools.MirrorTool.Open( Tool ) );

		[Shortcut( "Hammer/Edge Cut Tool", typeof( HammerWindow ), KeyCode.C )]
		static void EdgeCut() => ByMode(
			vertex: () => EditorTools.EdgeCutTool.Open( Tool ),
			edge: () => EditorTools.EdgeCutTool.Open( Tool ),
			face: () => EditorTools.EdgeCutTool.Open( Tool ) );

		[Shortcut( "Hammer/Nudge Up", typeof( HammerWindow ), KeyCode.UpArrow )]
		static void NudgeUp() => Run( t => t.Nudge( Vector2.up ) );

		[Shortcut( "Hammer/Nudge Down", typeof( HammerWindow ), KeyCode.DownArrow )]
		static void NudgeDown() => Run( t => t.Nudge( Vector2.down ) );

		[Shortcut( "Hammer/Nudge Left", typeof( HammerWindow ), KeyCode.LeftArrow )]
		static void NudgeLeft() => Run( t => t.Nudge( Vector2.left ) );

		[Shortcut( "Hammer/Nudge Right", typeof( HammerWindow ), KeyCode.RightArrow )]
		static void NudgeRight() => Run( t => t.Nudge( Vector2.right ) );

		[Shortcut( "Hammer/Bridge Tool", typeof( HammerWindow ), KeyCode.B, ShortcutModifiers.Alt )]
		static void BridgeTool() => ByMode(
			edge: () => EditorTools.BridgeTool.Open( Tool ),
			face: () => EditorTools.BridgeTool.Open( Tool ),
			obj: () => Tool.AlignToLastSelected() );

		[Shortcut( "Hammer/Edge Arch Tool", typeof( HammerWindow ), KeyCode.Y )]
		static void ArchTool() => ByMode( edge: () => EditorTools.EdgeArchTool.Open( Tool ) );

		[Shortcut( "Hammer/Path Extrude", typeof( HammerWindow ), KeyCode.X, ShortcutModifiers.Alt )]
		static void PathExtrude() => ByMode( edge: () => EditorTools.PathExtrudeTool.Open( Tool ), obj: () => Tool.PinToTarget() );

		[Shortcut( "Hammer/Set Origin To Target", typeof( HammerWindow ), KeyCode.O, ShortcutModifiers.Alt )]
		static void SetOriginToTarget() => ByMode( obj: () => Tool.SetOriginToTarget() );

		[Shortcut( "Hammer/Align Selected Objects To Workplane", typeof( HammerWindow ), KeyCode.E, ShortcutModifiers.Alt )]
		static void AlignSelectedToWorkplane() => ByMode( obj: () => Tool.AlignSelectedToWorkplane() );

		[Shortcut( "Hammer/Align Workplane To Selected Object", typeof( HammerWindow ), KeyCode.Q, ShortcutModifiers.Alt )]
		static void AlignWorkplaneToSelected() => View( t => t.AlignWorkplaneToSelected() );

		// End: Hammer's Reset Pivot; in Meshes mode, Set Origin to Object Center
		[Shortcut( "Hammer/Center Origin · Reset Pivot (End)", typeof( HammerWindow ), KeyCode.End )]
		static void CenterOrigin() => ByMode( vertex: () => Tool.ClearPivot(), edge: () => Tool.ClearPivot(), face: () => Tool.ClearPivot(), obj: () => Tool.CenterOrigin() );
	}
}
