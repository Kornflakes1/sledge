using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// A Hammer-style editing window: four viewports (3D, Top, Front, Side) with their own
	/// cameras, grids and navigation, the Hammer tool running in each, and the tool panel on the
	/// side. Hammer's keys are bound to this window so they never clash with Unity's scene view.
	/// </summary>
	public sealed partial class HammerWindow : EditorWindow, IHasCustomMenu
	{
		const float ToolbarHeight = 44;
		const float StripWidth = 52;
		const float PanelHeaderHeight = 26;
		const float SplitterSize = 4;
		const float StatusHeight = 26;
		const float MinPanelWidth = 220;
		const float MaxPanelWidth = 640;

		[SerializeField] Viewport[] _views;
		[SerializeField] float _splitX = 0.5f;
		[SerializeField] float _splitY = 0.5f;
		[SerializeField] int _maximized = -1;
		[SerializeField] ViewLayout _layout = ViewLayout.Quad;
		[SerializeField] bool _showPanel = true;
		[SerializeField] float _panelWidth = 300;
		bool _draggingPanel;
		[SerializeField] Vector2 _panelScroll;

		HammerMeshTool _tool;
		int _hoverView = -1;
		int _captureView = -1;
		int _activeView;
		int _draggingSplitter; // 0 none, 1 vertical, 2 horizontal, 3 both

		/// <summary>
		/// The tool of the Hammer window that has keyboard focus, if any.
		/// </summary>
		public static HammerMeshTool FocusedTool => focusedWindow is HammerWindow w ? w._tool : null;

		public HammerMeshTool Tool => _tool;

		/// <summary>
		/// The tool of an open Hammer window, if there is one.
		/// </summary>
		public static HammerMeshTool OpenTool => System.Linq.Enumerable.FirstOrDefault( System.Linq.Enumerable.Select( Resources.FindObjectsOfTypeAll<HammerWindow>(), w => w._tool ), t => t != null );

		/// <summary>
		/// Viewport rects from the last OnGUI (for tests and tooling).
		/// </summary>
		internal Rect[] LastViewRects { get; private set; } = new Rect[4];

		[MenuItem( "Window/Hammer %#h", false, 2000 )]
		[MenuItem( "Tools/Hammer/Open Hammer Window", false, 0 )]
		public static void Open()
		{
			var window = GetWindow<HammerWindow>();
			window.titleContent = new GUIContent( "Hammer", EditorGUIUtility.IconContent( "EditCollider" ).image );
			window.minSize = new Vector2( 640, 400 );
			window.Show();
		}

		void OnEnable()
		{
			titleContent = new GUIContent( "Hammer", EditorGUIUtility.IconContent( "EditCollider" ).image );
			wantsMouseMove = true;

			if ( _views == null || _views.Length != 4 )
			{
				_views = new[]
				{
					new Viewport( ViewType.Perspective ),
					new Viewport( ViewType.Top ),
					new Viewport( ViewType.Front ),
					new Viewport( ViewType.Side ),
				};
			}

			_tool = CreateInstance<HammerMeshTool>();
			_tool.hideFlags = HideFlags.HideAndDontSave;
			_tool.Attach();

			HammerViews.Register( this );
			HammerMesh.AnyRebuilt += OnMeshRebuilt;
			Undo.undoRedoPerformed += Repaint;
			EditorApplication.update += OnUpdate;
			UnityEditor.Selection.selectionChanged += Repaint;
		}

		void OnDisable()
		{
			EditorApplication.update -= OnUpdate;
			HammerMesh.AnyRebuilt -= OnMeshRebuilt;
			Undo.undoRedoPerformed -= Repaint;
			UnityEditor.Selection.selectionChanged -= Repaint;
			HammerViews.Unregister( this );

			if ( _tool != null )
			{
				_tool.Detach();
				DestroyImmediate( _tool );
				_tool = null;
			}

			if ( _views != null )
			{
				foreach ( var v in _views )
					v.DestroyCamera();
			}
		}

		void OnMeshRebuilt( HammerMesh mesh ) => Repaint();

		void OnUpdate()
		{
			// Fly camera movement while the right mouse button is held in the 3D view
			if ( _views == null ) return;

			var moved = false;
			foreach ( var v in _views )
			{
				// Looking with the cursor held in place reads the real cursor every update
				moved |= v.PollLockedLook();
				moved |= v.UpdateFly( this );
			}

			if ( moved )
				Repaint();
		}

		public void AddItemsToMenu( GenericMenu menu )
		{
			menu.AddItem( new GUIContent( "Show Tool Panel" ), _showPanel, () => _showPanel = !_showPanel );
			menu.AddItem( new GUIContent( "Reset Views" ), false, () =>
			{
				for ( int i = 0; i < _views.Length; i++ )
					_views[i] = new Viewport( _views[i].Type );
				_maximized = -1;
				_splitX = _splitY = 0.5f;
			} );
		}

		void OnGUI()
		{
			if ( _tool == null ) return;

			var e = Event.current;
			var mouseUp = e.type == EventType.MouseUp;

			// The side buttons on the mouse step the grid: back (mouse 4) smaller, forward (mouse 5) bigger
			if ( e.type == EventType.MouseDown && (e.button == 3 || e.button == 4) )
			{
				if ( e.button == 3 ) HammerSettings.GridSmaller();
				else HammerSettings.GridLarger();
				e.Use();
				HammerViews.RepaintAll();
				return;
			}

			// Tab, like Hammer: a tap swaps world and local axes; held, a click or drag places the
			// pivot. (It never walks the keyboard focus through the Tool Properties fields.)
			if ( (e.type == EventType.KeyDown || e.type == EventType.KeyUp) && (e.keyCode == KeyCode.Tab || e.character == '\t') )
			{
				if ( !EditorGUIUtility.editingTextField )
				{
					GUIUtility.keyboardControl = 0;
					if ( e.type == EventType.KeyDown && e.keyCode == KeyCode.Tab )
						_tool.BeginPivotPlacing();
					else if ( e.type == EventType.KeyUp && _tool.EndPivotPlacing() )
						HammerSettings.GlobalSpace = !HammerSettings.GlobalSpace;
					HammerViews.RepaintAll();
				}
				e.Use();
				return;
			}

			// Losing focus with Tab held would leave it stuck down
			if ( _tool.PlacingPivot && focusedWindow != this ) _tool.EndPivotPlacing();

			if ( e.type == EventType.Repaint ) CountFrame();

			// Layout like Source 2 Hammer / s&box: selection modes across the top, tool strip down
			// the left, then the Tool Properties panel, then the viewports
			var top = MenuBarHeight + SceneTabHeight + ToolbarHeight;
			DrawSceneTabs( new Rect( 0, MenuBarHeight, position.width, SceneTabHeight ) );
			DrawToolbar( new Rect( 0, MenuBarHeight + SceneTabHeight, position.width, ToolbarHeight ) );

			DrawStatusBar( new Rect( 0, position.height - StatusHeight, position.width, StatusHeight ) );
			DrawMenuBar( new Rect( 0, 0, position.width, MenuBarHeight ) );

			var body = new Rect( 0, top, position.width, position.height - top - StatusHeight );
			DrawToolStrip( new Rect( 0, body.y, StripWidth, body.height ) );

			_panelWidth = Mathf.Clamp( _panelWidth <= 0 ? 300 : _panelWidth, MinPanelWidth, Mathf.Min( MaxPanelWidth, position.width * 0.6f ) );
			var panelWidth = _showPanel ? _panelWidth : 0;
			if ( _showPanel )
			{
				// Resize handle first, so the panel's scroll bar next to it doesn't take the click
				PanelResizeGUI( new Rect( StripWidth + _panelWidth - 2, body.y, 7, body.height ) );
				DrawPanel( new Rect( StripWidth, body.y, _panelWidth, body.height ) );
			}

			var area = new Rect( StripWidth + panelWidth, body.y, position.width - StripWidth - panelWidth, body.height );

			var rects = LayoutViews( area );
			LastViewRects = rects;

			if ( _maximized < 0 && e.type != EventType.Repaint )
				SplitterInput( area );

			UpdateHoverView( e, rects );

			// Draw every view's picture before any of them sets up Handles: once Handles.SetCamera has
			// run, GUI.DrawTexture goes through that camera's matrices and a later view's picture
			// ends up as a huge quad floating in the earlier view's world
			if ( e.type == EventType.Repaint )
			{
				for ( int i = 0; i < _views.Length; i++ )
				{
					if ( rects[i].width > 1 && rects[i].height > 1 )
						RenderView( i, rects[i] );
				}
			}

			for ( int i = 0; i < _views.Length; i++ )
			{
				if ( rects[i].width <= 1 || rects[i].height <= 1 )
					continue;

				// Mouse and key events only go to the view under the mouse (or the one that owns
				// the current drag); everything repaints
				var target = _captureView >= 0 ? _captureView : _hoverView >= 0 ? _hoverView : _activeView;
				if ( e.type != EventType.Repaint && i != target )
					continue;

				DrawViewport( i, rects[i] );
			}

			if ( _maximized < 0 && e.type == EventType.Repaint )
				SplitterGUI( area );

			// Checked before the views run, since they use the event
			if ( mouseUp )
				_captureView = -1;
		}

		void UpdateHoverView( Event e, Rect[] rects )
		{
			if ( !e.isMouse && e.type != EventType.ScrollWheel && e.type != EventType.Layout )
				return;

			_hoverView = -1;
			for ( int i = 0; i < rects.Length; i++ )
			{
				if ( rects[i].Contains( e.mousePosition ) )
				{
					_hoverView = i;
					break;
				}
			}

			if ( e.type == EventType.MouseDown && _hoverView >= 0 )
			{
				_captureView = _hoverView;
				_activeView = _hoverView;
			}
		}

		public enum ViewLayout
		{
			Single,
			TwoHorizontal,
			TwoVertical,
			ThreePrimaryRight,
			ThreePrimaryLeft,
			ThreePrimaryBottom,
			ThreePrimaryTop,
			Quad,
		}

		static readonly (ViewLayout layout, string name)[] LayoutNames =
		{
			(ViewLayout.Single, "Single View"),
			(ViewLayout.TwoHorizontal, "Two-View Horizontal"),
			(ViewLayout.TwoVertical, "Two-View Vertical"),
			(ViewLayout.ThreePrimaryRight, "Three-View Primary Right"),
			(ViewLayout.ThreePrimaryLeft, "Three-View Primary Left"),
			(ViewLayout.ThreePrimaryBottom, "Three-View Primary Bottom"),
			(ViewLayout.ThreePrimaryTop, "Three-View Primary Top"),
			(ViewLayout.Quad, "Four-View Quad"),
		};

		struct Splitter
		{
			public Rect Rect;
			public bool Vertical; // drags along x
		}

		readonly List<Splitter> _splitters = new();

		/// <summary>
		/// Viewport rects for the current layout (zero size = hidden). View 0 is the primary.
		/// </summary>
		Rect[] LayoutViews( Rect area )
		{
			var rects = new Rect[4];
			_splitters.Clear();

			var layout = _layout;
			int single = _activeView;

			if ( _maximized >= 0 )
			{
				layout = ViewLayout.Single;
				single = _maximized;
			}

			var w = area.width - SplitterSize;
			var h = area.height - SplitterSize;
			var lw = Mathf.Round( w * _splitX );
			var th = Mathf.Round( h * _splitY );
			var x = area.x + lw;
			var y = area.y + th;

			Rect Left() => new( area.x, area.y, lw, area.height );
			Rect Right() => new( x + SplitterSize, area.y, w - lw, area.height );
			Rect Top() => new( area.x, area.y, area.width, th );
			Rect Bottom() => new( area.x, y + SplitterSize, area.width, h - th );

			void VSplit( Rect r ) => _splitters.Add( new Splitter { Rect = new Rect( x, r.y, SplitterSize, r.height ), Vertical = true } );
			void HSplit( Rect r ) => _splitters.Add( new Splitter { Rect = new Rect( r.x, y, r.width, SplitterSize ), Vertical = false } );

			// Split a column top/bottom, or a row left/right, at the shared ratios
			void Column( Rect r, int a, int b )
			{
				rects[a] = new Rect( r.x, r.y, r.width, y - r.y );
				rects[b] = new Rect( r.x, y + SplitterSize, r.width, r.yMax - y - SplitterSize );
				HSplit( r );
			}

			void Row( Rect r, int a, int b )
			{
				rects[a] = new Rect( r.x, r.y, x - r.x, r.height );
				rects[b] = new Rect( x + SplitterSize, r.y, r.xMax - x - SplitterSize, r.height );
				VSplit( r );
			}

			switch ( layout )
			{
				case ViewLayout.Single:
					rects[Mathf.Clamp( single, 0, 3 )] = area;
					break;

				case ViewLayout.TwoHorizontal:
					Row( area, 0, 1 );
					break;

				case ViewLayout.TwoVertical:
					Column( area, 0, 1 );
					break;

				case ViewLayout.ThreePrimaryRight:
					rects[0] = Right();
					Column( Left(), 1, 2 );
					VSplit( area );
					break;

				case ViewLayout.ThreePrimaryLeft:
					rects[0] = Left();
					Column( Right(), 1, 2 );
					VSplit( area );
					break;

				case ViewLayout.ThreePrimaryBottom:
					rects[0] = Bottom();
					Row( Top(), 1, 2 );
					HSplit( area );
					break;

				case ViewLayout.ThreePrimaryTop:
					rects[0] = Top();
					Row( Bottom(), 1, 2 );
					HSplit( area );
					break;

				default:
					// Hammer's quad: 3D top-left, Top top-right, Front bottom-left, Side bottom-right
					rects[0] = new Rect( area.x, area.y, lw, th );
					rects[1] = new Rect( x + SplitterSize, area.y, w - lw, th );
					rects[2] = new Rect( area.x, y + SplitterSize, lw, h - th );
					rects[3] = new Rect( x + SplitterSize, y + SplitterSize, w - lw, h - th );
					VSplit( area );
					HSplit( area );
					break;
			}

			return rects;
		}

		void SplitterGUI( Rect area )
		{
			foreach ( var sp in _splitters )
			{
				EditorGUIUtility.AddCursorRect( Grab( sp ), sp.Vertical ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical );
				EditorGUI.DrawRect( sp.Rect, new Color( 0.06f, 0.06f, 0.06f ) );
			}
		}

		/// <summary>
		/// The area you can grab a splitter by: a bit wider than the line itself.
		/// </summary>
		static Rect Grab( Splitter sp ) => sp.Vertical
			? new Rect( sp.Rect.x - 3, sp.Rect.y, sp.Rect.width + 6, sp.Rect.height )
			: new Rect( sp.Rect.x, sp.Rect.y - 3, sp.Rect.width, sp.Rect.height + 6 );

		void SplitterInput( Rect area )
		{
			var e = Event.current;
			var w = area.width - SplitterSize;
			var h = area.height - SplitterSize;

			switch ( e.type )
			{
				case EventType.MouseDown when e.button == 0 && GUIUtility.hotControl == 0:
					_draggingSplitter = 0;
					foreach ( var sp in _splitters )
					{
						if ( Grab( sp ).Contains( e.mousePosition ) )
							_draggingSplitter |= sp.Vertical ? 1 : 2;
					}
					if ( _draggingSplitter != 0 ) e.Use();
					break;

				case EventType.MouseDrag when _draggingSplitter != 0:
					if ( (_draggingSplitter & 1) != 0 ) _splitX = Mathf.Clamp( (e.mousePosition.x - area.x) / w, 0.1f, 0.9f );
					if ( (_draggingSplitter & 2) != 0 ) _splitY = Mathf.Clamp( (e.mousePosition.y - area.y) / h, 0.1f, 0.9f );
					e.Use();
					Repaint();
					break;

				case EventType.MouseUp when _draggingSplitter != 0:
					_draggingSplitter = 0;
					e.Use();
					break;
			}
		}

		/// <summary>
		/// Drag the right edge of the Tool Properties panel to resize it.
		/// </summary>
		void PanelResizeGUI( Rect grab )
		{
			var e = Event.current;
			EditorGUIUtility.AddCursorRect( grab, MouseCursor.ResizeHorizontal );

			switch ( e.type )
			{
				case EventType.MouseDown when e.button == 0 && grab.Contains( e.mousePosition ) && GUIUtility.hotControl == 0:
					_draggingPanel = true;
					e.Use();
					break;

				case EventType.MouseDrag when _draggingPanel:
					_panelWidth = Mathf.Clamp( e.mousePosition.x - StripWidth, MinPanelWidth, Mathf.Min( MaxPanelWidth, position.width * 0.6f ) );
					e.Use();
					Repaint();
					break;

				case EventType.MouseUp when _draggingPanel:
					_draggingPanel = false;
					e.Use();
					break;
			}
		}

		/// <summary>
		/// Centre every view on these bounds; the 3D view also moves in to fit them.
		/// </summary>
		public void FrameAll( Bounds bounds )
		{
			foreach ( var v in _views )
				v.Frame( bounds );

			Repaint();
		}

		/// <summary>
		/// Give every 2D view the same zoom as this one (Ctrl+wheel).
		/// </summary>
		void MatchOrthoZoom( Viewport source )
		{
			foreach ( var v in _views )
			{
				if ( v != source && v.IsOrtho )
					v.OrthoSize = source.OrthoSize;
			}

			Repaint();
		}

		void ShowLayoutMenu( Rect rect ) => PopupWindow.Show( rect, new LayoutPopup( this ) );

		static Rect[] LayoutPanes( ViewLayout layout ) => layout switch
		{
			ViewLayout.Single => new[] { new Rect( 0, 0, 1, 1 ) },
			ViewLayout.TwoHorizontal => new[] { new Rect( 0, 0, 0.5f, 1 ), new Rect( 0.5f, 0, 0.5f, 1 ) },
			ViewLayout.TwoVertical => new[] { new Rect( 0, 0, 1, 0.5f ), new Rect( 0, 0.5f, 1, 0.5f ) },
			ViewLayout.ThreePrimaryRight => new[] { new Rect( 0, 0, 0.4f, 0.5f ), new Rect( 0, 0.5f, 0.4f, 0.5f ), new Rect( 0.4f, 0, 0.6f, 1 ) },
			ViewLayout.ThreePrimaryLeft => new[] { new Rect( 0, 0, 0.6f, 1 ), new Rect( 0.6f, 0, 0.4f, 0.5f ), new Rect( 0.6f, 0.5f, 0.4f, 0.5f ) },
			ViewLayout.ThreePrimaryBottom => new[] { new Rect( 0, 0, 0.5f, 0.4f ), new Rect( 0.5f, 0, 0.5f, 0.4f ), new Rect( 0, 0.4f, 1, 0.6f ) },
			ViewLayout.ThreePrimaryTop => new[] { new Rect( 0, 0, 1, 0.6f ), new Rect( 0, 0.6f, 0.5f, 0.4f ), new Rect( 0.5f, 0.6f, 0.5f, 0.4f ) },
			_ => new[] { new Rect( 0, 0, 0.5f, 0.5f ), new Rect( 0.5f, 0, 0.5f, 0.5f ), new Rect( 0, 0.5f, 0.5f, 0.5f ), new Rect( 0.5f, 0.5f, 0.5f, 0.5f ) },
		};

		sealed class LayoutPopup : PopupWindowContent
		{
			readonly HammerWindow _window;
			public LayoutPopup( HammerWindow window ) => _window = window;

			public override Vector2 GetWindowSize() => new( 230, LayoutNames.Length * 26 + 8 );

			public override void OnGUI( Rect rect )
			{
				var e = Event.current;
				if ( e.type == EventType.Repaint ) EditorGUI.DrawRect( rect, new Color( 0.15f, 0.15f, 0.15f ) );

				for ( int i = 0; i < LayoutNames.Length; i++ )
				{
					var (layout, name) = LayoutNames[i];
					var row = new Rect( rect.x + 4, rect.y + 4 + i * 26, rect.width - 8, 26 );
					var on = _window._maximized < 0 && _window._layout == layout;

					if ( e.type == EventType.Repaint && (on || row.Contains( e.mousePosition )) )
						EditorGUI.DrawRect( row, on ? HammerIcons.ButtonOn : HammerIcons.ButtonHover );

					HammerIcons.DrawLayoutIcon( new Rect( row.x + 8, row.y + 6, 15, 14 ), LayoutPanes( layout ) );
					GUI.Label( new Rect( row.x + 34, row.y, row.width - 34, row.height ), name, EditorStyles.label );

					if ( e.type == EventType.MouseDown && row.Contains( e.mousePosition ) )
					{
						_window._layout = layout;
						_window._maximized = -1;
						_window.Repaint();
						editorWindow.Close();
						e.Use();
					}
				}

				if ( e.type == EventType.MouseMove ) editorWindow.Repaint();
			}

			public override void OnOpen() => editorWindow.wantsMouseMove = true;
		}

		/// <summary>
		/// Clicking a view's name: choose what it shows, like Hammer's viewport menu.
		/// </summary>
		void ShowViewMenu( int index, Rect rect )
		{
			var view = _views[index];
			var menu = new GenericMenu();

			void Item( string label, bool on, Action action ) => menu.AddItem( new GUIContent( label ), on, () => { action(); Repaint(); } );

			// (∕ is a division slash: a plain / would make a submenu)
			Item( "2D Top (x∕y)    F2", view.Type == ViewType.Top, () => SetView( index, ViewType.Top, null ) );
			Item( "2D Front (y∕z)    F3", view.Type == ViewType.Front, () => SetView( index, ViewType.Front, null ) );
			Item( "2D Side (x∕z)    F4", view.Type == ViewType.Side, () => SetView( index, ViewType.Side, null ) );
			foreach ( ViewShading shading in Enum.GetValues( typeof( ViewShading ) ) )
			{
				var s = shading;
				Item( $"2D View Shading/{s}", view.IsOrtho && view.Shading == s, () => { if ( view.IsOrtho ) view.Shading = s; else SetView( index, ViewType.Top, s ); } );
			}

			menu.AddSeparator( "" );
			Item( "3D Wire", !view.IsOrtho && view.Shading == ViewShading.Wire, () => SetView( index, ViewType.Perspective, ViewShading.Wire ) );
			Item( "3D Fullbright    F5", !view.IsOrtho && view.Shading == ViewShading.Fullbright, () => SetView( index, ViewType.Perspective, ViewShading.Fullbright ) );
			Item( "3D Lit    F6", !view.IsOrtho && view.Shading == ViewShading.Lit, () => SetView( index, ViewType.Perspective, ViewShading.Lit ) );

			menu.AddSeparator( "" );
			Item( _maximized == index ? "Restore Layout" : "Maximize", false, () => _maximized = _maximized == index ? -1 : index );

			menu.DropDown( rect );
		}

		/// <summary>
		/// Change a view's type and/or shading (null keeps the shading, or picks the default
		/// when switching between 2D and 3D).
		/// </summary>
		void SetView( int index, ViewType type, ViewShading? shading )
		{
			var view = _views[index];
			var was2D = view.IsOrtho;
			view.Type = type;

			if ( shading.HasValue ) view.Shading = shading.Value;
			else if ( was2D != view.IsOrtho ) view.Shading = view.IsOrtho ? ViewShading.Wire : ViewShading.Lit;

			Repaint();
		}

		/// <summary>
		/// F2-F6: change the view under the mouse (or the active one).
		/// </summary>
		public void SetHoveredView( ViewType type, ViewShading? shading )
		{
			var index = _hoverView >= 0 ? _hoverView : _activeView;
			SetView( index, type, shading );
		}

		static readonly string[] GridNames = { "0.125", "0.25", "0.5", "1", "2", "4", "8", "16", "32", "64", "128", "256", "512" };
		static readonly float[] GridValues = { 0.125f, 0.25f, 0.5f, 1, 2, 4, 8, 16, 32, 64, 128, 256, 512 };
		static readonly string[] AngleNames = { "1°", "5°", "15°", "30°", "45°", "90°" };
		static readonly float[] AngleValues = { 1, 5, 15, 30, 45, 90 };

		void DrawToolbar( Rect rect )
		{
			var e = Event.current;
			if ( e.type == EventType.Repaint )
			{
				EditorGUI.DrawRect( rect, HammerIcons.Bar );
				EditorGUI.DrawRect( new Rect( rect.x, rect.yMax - 1, rect.width, 1 ), HammerIcons.Divider );
			}

			// Holding a modifier turns the mode buttons into selection conversions, like Hammer
			var conversion = e.alt ? SelectionConversion.Convert : e.shift ? SelectionConversion.Boundary : (e.control || e.command) ? SelectionConversion.Connect : SelectionConversion.None;
			if ( e.type == EventType.KeyDown || e.type == EventType.KeyUp ) Repaint();

			var x = rect.x + 6;
			var h = rect.height - 8;
			var y = rect.y + 4;

			ModeButton( ref x, y, h, EditMode.Vertex, "Vertices", HammerIcons.Vertex, "1", conversion );
			ModeButton( ref x, y, h, EditMode.Edge, "Edges", HammerIcons.Edge, "2", conversion );
			ModeButton( ref x, y, h, EditMode.Face, "Faces", HammerIcons.Face, "3", conversion );
			ModeButton( ref x, y, h, EditMode.Object, "Meshes", HammerIcons.Object, "4", SelectionConversion.None );

			x += 8;
			BarDivider( x, rect );
			x += 9;

			// Settings toggles
			HammerSettings.GlobalSpace = BarToggle( ref x, y, h, HammerSettings.GlobalSpace ? HammerIcons.Global : HammerIcons.Local, HammerSettings.GlobalSpace ? "World space (click for local)" : "Local space (click for world)", HammerSettings.GlobalSpace, true ) ? !HammerSettings.GlobalSpace : HammerSettings.GlobalSpace;
			if ( BarToggle( ref x, y, h, HammerIcons.TexLock, "Texture lock: textures move with the geometry", HammerSettings.TextureLock ) ) HammerSettings.TextureLock = !HammerSettings.TextureLock;
			if ( BarToggle( ref x, y, h, HammerIcons.TexScaleLock, "Texture scale lock: textures stretch when scaling", HammerSettings.TextureLockScale ) ) HammerSettings.TextureLockScale = !HammerSettings.TextureLockScale;
			if ( BarToggle( ref x, y, h, HammerIcons.XRay, "Select through geometry", HammerSettings.SelectionThrough ) ) HammerSettings.SelectionThrough = !HammerSettings.SelectionThrough;

			x += 8;
			BarDivider( x, rect );
			x += 9;

			if ( BarToggle( ref x, y, h, HammerIcons.ShowGridIcon, "Show the grid", HammerSettings.ShowGrid ) ) { HammerSettings.ShowGrid = !HammerSettings.ShowGrid; Repaint(); }
			if ( BarToggle( ref x, y, h, HammerIcons.Wires, "Show mesh edges in the 3D view", HammerSettings.ShowWires ) ) { HammerSettings.ShowWires = !HammerSettings.ShowWires; Repaint(); }

			x += 8;
			BarDivider( x, rect );
			x += 9;
			SnapControls( ref x, rect, y, h );

		}

		/// <summary>
		/// Bottom bar, like Hammer's: what's selected and its size on the left, grid and angle snap
		/// on the right.
		/// </summary>
		void DrawStatusBar( Rect rect )
		{
			if ( Event.current.type == EventType.Repaint )
			{
				EditorGUI.DrawRect( rect, HammerIcons.Bar );
				EditorGUI.DrawRect( new Rect( rect.x, rect.y, rect.width, 1 ), HammerIcons.Divider );
			}

			// Turns red when the last edit left broken faces
			var status = StatusStyle;
			status.normal.textColor = MeshHealth.Warning != null ? new Color( 1.0f, 0.45f, 0.4f ) : new Color( 0.8f, 0.8f, 0.8f );
			GUI.Label( new Rect( rect.x + StripWidth + 8, rect.y, Mathf.Max( 100, rect.width - StripWidth - 90 ), rect.height ), _tool.StatusText, status );

			var right = rect.xMax - 8;
			// Frames per second, like Hammer's status bar
			if ( _fps > 0 )
				GUI.Label( new Rect( right - 60, rect.y, 60, rect.height ), $"{_fps:0} fps", StatusStyle );
		}

		/// <summary>
		/// Grid size, the snap toggles and the angle snap, in the top bar where they're easy to
		/// find (Hammer keeps them up top too).
		/// </summary>
		void SnapControls( ref float left, Rect bar, float y, float h )
		{
			var x = left;
			var label = new GUIStyle( StatusStyle ) { alignment = TextAnchor.MiddleRight };
			var middle = new Rect( 0, bar.y, 0, bar.height );

			GUI.Label( new Rect( x, middle.y, 34, middle.height ), "Grid", label );
			x += 38;
			var grid = Array.FindIndex( GridValues, g => Mathf.Approximately( g, HammerSettings.GridSize ) );
			StatusDropdown( new Rect( x, bar.y + (bar.height - 20) / 2, 64, 20 ), Mathf.Max( grid, 0 ), GridNames, i => HammerSettings.GridSize = GridValues[i] );
			x += 72;

			// Hammer's snap buttons: magnet (all snapping), grid, vertices, surfaces
			var size = h - 4;
			bool Snap( Texture icon, string tip, bool on, bool enabled = true )
			{
				var r = new Rect( x, y + 2, size, size );
				x += size + 3;
				return HammerIcons.SnapButton( r, icon, tip, on, enabled );
			}

			var all = HammerSettings.SnapEnabled;
			if ( Snap( HammerIcons.Magnet, "Snapping on/off (the others only work while this is on)", all ) ) HammerSettings.SnapEnabled = !all;
			if ( Snap( HammerIcons.GridSnap, "Snap to grid (hold Ctrl to toggle while dragging)", HammerSettings.GridSnapSetting, all ) ) HammerSettings.GridSnapSetting = !HammerSettings.GridSnapSetting;
			if ( Snap( HammerIcons.VertexSnap, "Snap to vertices: moves jump to a vertex near the mouse", HammerSettings.VertexSnap, all ) ) HammerSettings.VertexSnap = !HammerSettings.VertexSnap;
			if ( Snap( HammerIcons.SurfaceSnap, "Snap to surfaces: moved objects stand on the surface under the mouse", HammerSettings.SurfaceSnap, all ) ) HammerSettings.SurfaceSnap = !HammerSettings.SurfaceSnap;

			x += 6;
			GUI.Label( new Rect( x, middle.y, 42, middle.height ), "Angle", label );
			x += 46;
			var angle = Array.IndexOf( AngleValues, HammerSettings.AngleSnap );
			StatusDropdown( new Rect( x, bar.y + (bar.height - 20) / 2, 60, 20 ), Mathf.Max( angle, 0 ), AngleNames, i => HammerSettings.AngleSnap = AngleValues[i] );
			x += 68;
			left = x;
		}

		// The window only draws when something changes, so this is how fast it draws while it's
		// busy (flying, dragging): the average time between frames that follow each other closely.
		// Gaps while idle are left out, and the last figure stays up
		double _lastFrame;
		float _frameTime;
		float _fps;

		void CountFrame()
		{
			var now = EditorApplication.timeSinceStartup;
			var dt = (float)(now - _lastFrame);
			_lastFrame = now;
			if ( dt <= 0 || dt > 0.25f ) return;

			_frameTime = _frameTime <= 0 ? dt : Mathf.Lerp( _frameTime, dt, 0.1f );
			_fps = 1 / _frameTime;
		}

		/// <summary>
		/// A dropdown that never takes keyboard focus: a focused popup opens on Enter or Space,
		/// which belong to the tools (confirm a shape, a cut, a bevel).
		/// </summary>
		static void StatusDropdown( Rect rect, int index, string[] names, Action<int> pick )
		{
			if ( !EditorGUI.DropdownButton( rect, new GUIContent( names[index] ), FocusType.Passive, EditorStyles.popup ) )
				return;

			var menu = new GenericMenu();
			for ( int i = 0; i < names.Length; i++ )
			{
				var k = i;
				menu.AddItem( new GUIContent( names[i] ), i == index, () => { pick( k ); HammerViews.RepaintAll(); } );
			}
			menu.DropDown( rect );
		}

		static GUIStyle _statusStyle;
		static GUIStyle StatusStyle => _statusStyle ??= new GUIStyle( EditorStyles.label ) { alignment = TextAnchor.MiddleLeft, fontSize = 11, normal = { textColor = new Color( 0.8f, 0.8f, 0.8f ) } };

		void ModeButton( ref float x, float y, float h, EditMode mode, string label, Texture icon, string key, SelectionConversion conversion )
		{
			var on = _tool.Mode == mode;
			if ( on ) conversion = SelectionConversion.None;
			var text = conversion == SelectionConversion.None ? label : conversion.ToString();

			// Size for the widest label so the bar doesn't jump around while holding a modifier
			var width = Mathf.Max( HammerIcons.ModeButtonWidth( label, h ), HammerIcons.ModeButtonWidth( "Boundary", h ) );
			var r = new Rect( x, y, width, h );
			x += width + 2;

			var tip = $"{label} ({key})\nShift: boundary of the selection · Ctrl: connect · Alt: convert";
			if ( HammerIcons.ModeButton( r, icon, text, tip, on ) && !on )
				_tool.SwitchMode( mode, conversion );
		}

		static bool BarToggle( ref float x, float y, float h, Texture icon, string tip, bool on, bool plain = false )
		{
			var r = new Rect( x, y, h, h );
			x += h + 2;
			return HammerIcons.StripButton( r, icon, tip, on && !plain );
		}

		static void BarDivider( float x, Rect bar )
		{
			if ( Event.current.type == EventType.Repaint )
				EditorGUI.DrawRect( new Rect( x, bar.y + 8, 1, bar.height - 16 ), new Color( 0.32f, 0.32f, 0.32f ) );
		}

		void DrawToolStrip( Rect rect )
		{
			if ( Event.current.type == EventType.Repaint )
			{
				EditorGUI.DrawRect( rect, HammerIcons.Bar );
				EditorGUI.DrawRect( new Rect( rect.xMax - 1, rect.y, 1, rect.height ), HammerIcons.Divider );
			}

			var size = rect.width - 8;
			var y = rect.y + 6;
			var x = rect.x + 4;
			bool Button( Texture icon, string tip, bool on, bool enabled = true )
			{
				var r = new Rect( x, y, size, size );
				y += size + 2;
				return HammerIcons.StripButton( r, icon, tip, on, enabled );
			}

			void Separator()
			{
				y += 4;
				if ( Event.current.type == EventType.Repaint )
					EditorGUI.DrawRect( new Rect( x + 4, y, size - 8, 1 ), new Color( 0.3f, 0.3f, 0.3f ) );
				y += 6;
			}

			var editing = _tool.Mode is EditMode.Vertex or EditMode.Edge or EditMode.Face or EditMode.Object;
			void Move( MoveMode mode, Texture icon, string tip )
			{
				if ( Button( icon, tip, editing && _tool.MoveMode == mode ) )
				{
					_tool.MoveMode = mode;
					if ( !editing ) _tool.Mode = EditMode.Face;
				}
			}

			Move( MoveMode.Select, HammerIcons.Select, "Select (Shift+S)" );
			Move( MoveMode.Position, HammerIcons.Move, "Move (T)" );
			Move( MoveMode.Rotate, HammerIcons.Rotate, "Rotate (R)" );
			Move( MoveMode.Scale, HammerIcons.ScaleIcon, "Scale (E)" );
			Move( MoveMode.Pivot, HammerIcons.Pivot, "Pivot: move where rotate/scale turn around, or an object's origin (Insert)" );

			Separator();

			// One Block tool for every shape, as in Hammer: the shape is Geometry Type in Tool Properties
			var block = _tool.Mode == EditMode.Primitive;
			if ( Button( HammerIcons.Block, "Block tool: box, cylinder, stairs, arch... (pick Geometry Type in Tool Properties) (Shift+B)", block ) )
				_tool.Mode = block ? EditMode.Face : EditMode.Primitive;

			if ( Button( HammerIcons.PolygonIcon, "Polygon tool: click out a shape, then set its height (Shift+P)", _tool.SubTool is PolygonTool ) )
			{
				if ( _tool.SubTool is PolygonTool ) _tool.SubTool.Cancel();
				else PolygonTool.Open( _tool );
			}

			if ( Button( HammerIcons.Paint, "Vertex paint (5)", _tool.Mode == EditMode.Paint ) )
				_tool.Mode = _tool.Mode == EditMode.Paint ? EditMode.Face : EditMode.Paint;

			Separator();

			if ( Button( HammerIcons.Clip, "Clipping tool (Shift+X)", false, _tool.Mode is EditMode.Face or EditMode.Object ) ) ClipTool.Open( _tool );
			if ( Button( HammerIcons.Knife, "Edge cut (C)", false, _tool.Mode is EditMode.Vertex or EditMode.Edge or EditMode.Face ) ) EdgeCutTool.Open( _tool );
			if ( Button( HammerIcons.Mirror, "Mirror (Shift+F)", false, _tool.Mode is EditMode.Face or EditMode.Object ) ) MirrorTool.Open( _tool );

			var toggle = new Rect( x, rect.yMax - 34, size, 28 );
			if ( GUI.Button( toggle, new GUIContent( _showPanel ? "«" : "»", _showPanel ? "Hide Tool Properties" : "Show Tool Properties" ), EditorStyles.toolbarButton ) )
				_showPanel = !_showPanel;
		}

		void DrawPanel( Rect rect )
		{
			if ( Event.current.type == EventType.Repaint )
			{
				EditorGUI.DrawRect( rect, HammerIcons.Panel );
				EditorGUI.DrawRect( new Rect( rect.xMax - 1, rect.y, 1, rect.height ), HammerIcons.Divider );
				// "Tool Properties" tab, like s&box
				var tab = new Rect( rect.x + 4, rect.y + 3, 140, PanelHeaderHeight - 3 );
				EditorGUI.DrawRect( new Rect( rect.x, rect.y, rect.width, PanelHeaderHeight ), HammerIcons.Background );
				EditorGUI.DrawRect( tab, HammerIcons.Panel );
				EditorGUI.DrawRect( new Rect( tab.x, tab.y, tab.width, 2 ), HammerIcons.Accent );
				GUI.Label( new Rect( tab.x + 10, tab.y + 3, tab.width, tab.height ), "Tool Properties", EditorStyles.boldLabel );
			}

			GUILayout.BeginArea( new Rect( rect.x + 2, rect.y + PanelHeaderHeight + 4, rect.width - 4, rect.height - PanelHeaderHeight - 6 ) );
			_panelScroll = GUILayout.BeginScrollView( _panelScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar );
			HammerPanel.Draw( _tool );
			GUILayout.EndScrollView();
			GUILayout.EndArea();
		}

		/// <summary>
		/// Mesh edges over the shaded 3D view, hidden behind geometry.
		/// </summary>
		/// <summary>
		/// The edges of the mesh under the mouse and the selected ones (every mesh's would bury
		/// the view in lines).
		/// </summary>
		void DrawMeshEdgesDepthTested( Color color )
		{
			var z = Handles.zTest;
			Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
			Handles.color = color;

			var show = new HashSet<HammerMesh>();
			if ( _tool != null && _tool.HoveredMesh != null ) show.Add( _tool.HoveredMesh );
			// (a whole selected mesh just gets its tint, like Hammer)
			if ( _tool != null && _tool.Mode != EditMode.Object )
				foreach ( var go in UnityEditor.Selection.gameObjects )
					foreach ( var m in go.GetComponentsInChildren<HammerMesh>() ) show.Add( m );
			if ( _tool != null )
				foreach ( var m in _tool.Selection.Components ) show.Add( m );

			foreach ( var c in show )
				if ( c != null && c.isActiveAndEnabled ) Handles.DrawLines( MeshEdgeCache.WorldEdges( c ) );
			Handles.zTest = z;
		}

		static void DrawMeshWires()
		{
			var selected = new HashSet<GameObject>( UnityEditor.Selection.gameObjects );
			var z = Handles.zTest;
			Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

			// Unselected first in grey, selected on top in white
			Handles.color = new Color( 0.6f, 0.6f, 0.6f, 0.9f );
			foreach ( var c in MeshEdgeCache.Meshes() )
				if ( c != null && !selected.Contains( c.gameObject ) ) Handles.DrawLines( MeshEdgeCache.WorldEdges( c ) );

			Handles.color = Color.white;
			foreach ( var c in MeshEdgeCache.Meshes() )
				if ( c != null && selected.Contains( c.gameObject ) ) Handles.DrawLines( MeshEdgeCache.WorldEdges( c ) );

			Handles.zTest = z;
		}

		/// <summary>
		/// Z: look around with the mouse and fly with WASD without holding a button, until Z, Esc
		/// or a click. Uses the 3D view under the mouse, or the first 3D view.
		/// </summary>
		public void ToggleFlyMode()
		{
			foreach ( var v in _views )
			{
				if ( v.FlyMode )
				{
					v.SetFlyMode( false );
					Repaint();
					return;
				}
			}

			var index = _hoverView >= 0 && !_views[_hoverView].IsOrtho ? _hoverView : System.Array.FindIndex( _views, v => !v.IsOrtho );
			if ( index < 0 ) return;

			_activeView = index;
			_views[index].SetFlyMode( true );
			Focus();
			Repaint();
		}

		/// <summary>
		/// Ctrl+Space: turn the 2D view under the mouse to the next of Top, Front, Side; with
		/// <paramref name="centre"/>, also centre it on the selection.
		/// </summary>
		public void CycleHoveredView( bool centre )
		{
			var index = _hoverView >= 0 ? _hoverView : _activeView;
			var view = _views[index];
			if ( !view.IsOrtho ) return;

			var next = view.Type switch { ViewType.Top => ViewType.Front, ViewType.Front => ViewType.Side, _ => ViewType.Top };
			SetView( index, next, null );
			if ( centre && _tool != null ) _tool.FrameSelection();
			Repaint();
		}

		/// <summary>
		/// Maximize the view under the mouse, or go back to four views.
		/// </summary>
		public void ToggleMaximize()
		{
			_maximized = _maximized >= 0 ? -1 : Mathf.Max( 0, _hoverView >= 0 ? _hoverView : _activeView );
			Repaint();
		}

		/// <summary>
		/// Render the camera into its own texture and draw that, rather than Handles.DrawCamera,
		/// which leaves the GUI state of a custom window in a mess.
		/// </summary>
		void RenderView( int index, Rect rect )
		{
			var view = _views[index];
			var camera = view.EnsureCamera();
			view.ApplyToCamera( rect );

			var target = view.EnsureTarget( rect.size * EditorGUIUtility.pixelsPerPoint );
			camera.targetTexture = target;
			camera.pixelRect = new Rect( 0, 0, target.width, target.height );

			// The selection overlay is drawn into the view's own picture, after the scene, so it can
			// be depth tested against the geometry (the window itself has no depth for Handles)
			_overlayCamera = camera;
			_overlayView = MakeView( index, rect );
			_overlayViewport = view;
			Camera.onPostRender += DrawOverlay;
			UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += DrawOverlaySrp;

			// Wireframe views draw Hammer meshes as polygons (DrawMeshWires) rather than triangles
			var hidden = view.Shading == ViewShading.Wire ? HideHammerMeshes() : null;

			var wire = GL.wireframe;
			GL.wireframe = view.Shading == ViewShading.Wire;
			try
			{
				// (a replacement shader on a normal Render, not RenderWithShader, which skips the
				// post-render callback the overlay and grid are drawn in)
				if ( view.Shading == ViewShading.Fullbright && FullbrightShader != null )
					camera.SetReplacementShader( FullbrightShader, "RenderType" );
				else
					camera.ResetReplacementShader();
				// (URP ignores replacement shaders: Hammer's own shader does fullbright itself there)
				Shader.SetGlobalFloat( FullbrightId, view.Shading == ViewShading.Fullbright ? 1 : 0 );

				camera.Render();
			}
			finally
			{
				if ( hidden != null )
					foreach ( var r in hidden ) r.forceRenderingOff = false;

				GL.wireframe = wire;
				Shader.SetGlobalFloat( FullbrightId, 0 );
				Camera.onPostRender -= DrawOverlay;
				UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= DrawOverlaySrp;
				_overlayCamera = null;
			}

			camera.targetTexture = null;
			GUI.DrawTexture( rect, target, ScaleMode.StretchToFill, false );
			view.LastRender = target;
		}

		/// <summary>
		/// Turn off rendering of Hammer meshes (and 3D text) for one render (not serialized, so the scene isn't dirtied).
		/// </summary>
		static List<Renderer> HideHammerMeshes()
		{
			var list = new List<Renderer>();
			foreach ( var mesh in Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ) )
			{
				var r = mesh.GetComponent<MeshRenderer>();
				if ( r == null || r.forceRenderingOff ) continue;
				r.forceRenderingOff = true;
				list.Add( r );
			}

			// 3D text turns into a scribble of letter quads in wireframe: leave it out
			foreach ( var text in Object.FindObjectsByType<TextMesh>( FindObjectsSortMode.None ) )
			{
				var r = text.GetComponent<MeshRenderer>();
				if ( r == null || r.forceRenderingOff ) continue;
				r.forceRenderingOff = true;
				list.Add( r );
			}
			return list;
		}

		Camera _overlayCamera;
		HammerView _overlayView;
		Viewport _overlayViewport;
		static Shader _fullbright;

		static Shader FullbrightShader => _fullbright != null ? _fullbright : _fullbright = Shader.Find( "Hammer/Fullbright" );

		static readonly int FullbrightId = Shader.PropertyToID( "_HammerFullbright" );

		/// <summary>
		/// Scriptable pipelines don't leave the camera's target and matrices set up when they
		/// finish a camera, as the built-in one does in OnPostRender: set them up for the overlay.
		/// </summary>
		void DrawOverlaySrp( UnityEngine.Rendering.ScriptableRenderContext context, Camera camera )
		{
			if ( camera != _overlayCamera || camera.targetTexture == null )
				return;

			var previous = RenderTexture.active;
			RenderTexture.active = camera.targetTexture;
			GL.PushMatrix();
			GL.Viewport( new Rect( 0, 0, camera.targetTexture.width, camera.targetTexture.height ) );
			GL.LoadProjectionMatrix( camera.projectionMatrix );
			GL.modelview = camera.worldToCameraMatrix;
			try
			{
				DrawOverlay( camera );
			}
			finally
			{
				GL.PopMatrix();
				RenderTexture.active = previous;
			}
		}

		void DrawOverlay( Camera camera )
		{
			if ( camera != _overlayCamera || _tool == null )
				return;

			var wire = GL.wireframe;
			GL.wireframe = false;
			try
			{
				HammerViews.Current = _overlayView;
				if ( HammerSettings.ShowGrid ) _overlayViewport?.DrawGround();
				// Fullbright shows every mesh's edges in light blue, like Hammer; Lit only on request
				if ( _overlayViewport != null && !_overlayViewport.IsOrtho )
				{
					if ( _overlayViewport.Shading == ViewShading.Fullbright )
						DrawMeshEdgesDepthTested( new Color( 0.45f, 0.78f, 1.0f, 0.9f ) );
					else if ( HammerSettings.ShowWires && _overlayViewport.Shading == ViewShading.Lit )
						DrawMeshEdgesDepthTested( new Color( 0.05f, 0.05f, 0.05f, 0.6f ) );
				}
				_tool.DrawSceneOverlay( _overlayView );
			}
			catch ( Exception e )
			{
				Debug.LogException( e );
			}
			finally
			{
				GL.wireframe = wire;
			}
		}

		HammerView MakeView( int index, Rect rect )
		{
			var view = _views[index];
			return new HammerView
			{
				Window = this,
				Camera = view.EnsureCamera(),
				Rect = rect,
				Name = view.Type.ToString(),
				Frame = b => { view.Frame( b ); Repaint(); },
			};
		}

		void DrawViewport( int index, Rect rect )
		{
			var view = _views[index];
			var camera = view.EnsureCamera();
			camera.targetTexture = null;
			view.ApplyToCamera( rect );

			var e = Event.current;

			// Navigation first so it can claim the mouse before the tool
			// (called for every event so control IDs stay in step between Layout and input events)
			if ( view.HandleNavigation( e, rect, this ) )
				return;

			// Handles draw with this camera; HammerGUI does the GUI <-> world conversions itself
			// because Unity's are offset in custom windows
			Handles.SetCamera( rect, camera );

			var hammerView = MakeView( index, rect );

			HammerViews.Current = hammerView;

			if ( e.type == EventType.Repaint )
			{
				if ( HammerSettings.ShowGrid )
					view.DrawGrid( rect );

				// Hammer's 2D views show every brush as a wireframe
				if ( view.IsOrtho || view.Shading == ViewShading.Wire )
					DrawMeshWires();
			}

			if ( e.type == EventType.MouseDown && e.button == 0 && view.LayoutRect( rect ).Contains( e.mousePosition ) )
			{
				ShowLayoutMenu( view.LayoutRect( rect ) );
				e.Use();
			}

			// Clicking the view's name opens the view menu, double-click maximizes
			if ( e.type == EventType.MouseDown && e.button == 0 && view.LabelRect( rect ).Contains( e.mousePosition ) )
			{
				if ( e.clickCount > 1 )
				{
					_maximized = _maximized == index ? -1 : index;
					Repaint();
				}
				else
				{
					ShowViewMenu( index, view.LabelRect( rect ) );
				}

				e.Use();
			}

			_tool.ViewGUI( hammerView );

			if ( e.type == EventType.Repaint )
			{
				view.DrawLabel( rect, index == _activeView );

				// Hidden cursor while looking around (the custom cursor is a blank one)
				if ( view.HidesCursor )
					EditorGUIUtility.AddCursorRect( rect, MouseCursor.CustomCursor );
			}
		}
	}
}
