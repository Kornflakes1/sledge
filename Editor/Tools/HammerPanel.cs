using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using PolygonMesh = Sandbox.PolygonMesh;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// The "Tool Properties" sidebar, laid out like the s&amp;box mesh tool: a header for the mode,
	/// then grouped sections of operations, then the material palette. Shared by the Hammer
	/// window and the scene view overlay (which also shows the mode and grid controls).
	/// </summary>
	public static class HammerPanel
	{
		static readonly string[] ModeNames = { "Vertex", "Edge", "Face", "Object", "Shape", "Paint" };
		static readonly string[] MoveNames = { "Select", "Move", "Rotate", "Scale", "Pivot" };

		static GUIStyle _header;
		static GUIStyle _count;
		static GUIStyle _sectionTitle;
		static GUIStyle _subLabel;
		static GUIStyle _button;
		static GUIStyle _buttonText;
		static GUIStyle _buttonKey;
		static GUIStyle _section;

		static readonly Color HeaderBack = new( 0.045f, 0.045f, 0.045f );
		static readonly Color SectionBack = new( 0.105f, 0.105f, 0.105f );
		static readonly Color Accent = new( 0.3f, 0.56f, 0.95f );

		static void InitStyles()
		{
			if ( _header != null ) return;

			_header = new GUIStyle( EditorStyles.boldLabel ) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
			_count = new GUIStyle( EditorStyles.miniLabel ) { alignment = TextAnchor.MiddleRight };
			_count.normal.textColor = new Color( 0.65f, 0.65f, 0.65f );
			_sectionTitle = new GUIStyle( EditorStyles.boldLabel ) { fontSize = 12, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
			_subLabel = new GUIStyle( EditorStyles.miniLabel ) { padding = new RectOffset( 2, 2, 4, 0 ) };
			_subLabel.normal.textColor = new Color( 0.6f, 0.6f, 0.6f );
			_button = new GUIStyle( EditorStyles.miniButton ) { fixedHeight = 24, margin = new RectOffset( 2, 2, 2, 2 ) };
			_buttonText = new GUIStyle( EditorStyles.label ) { fontSize = 11, alignment = TextAnchor.MiddleCenter, padding = new RectOffset( 4, 4, 0, 0 ), clipping = TextClipping.Clip, normal = { textColor = new Color( 0.88f, 0.88f, 0.88f ) } };
			_buttonKey = new GUIStyle( EditorStyles.miniLabel ) { alignment = TextAnchor.MiddleRight, padding = new RectOffset( 2, 6, 0, 0 ) };
			_buttonKey.normal.textColor = new Color( 0.55f, 0.55f, 0.55f );
			_section = new GUIStyle { padding = new RectOffset( 6, 6, 4, 6 ), margin = new RectOffset( 0, 0, 0, 0 ) };
		}

		/// <param name="compact">Also show the mode, move mode and grid controls (scene view overlay).</param>
		public static void Draw( HammerMeshTool tool, bool compact = false )
		{
			InitStyles();
			_tool = tool;

			if ( tool.SubTool != null )
			{
				SubToolGUI( tool.SubTool );
				return;
			}

			if ( compact )
			{
				var mode = (EditMode)GUILayout.Toolbar( (int)tool.Mode, ModeNames, EditorStyles.miniButton );
				if ( mode != tool.Mode ) tool.Mode = mode;

				var move = (MoveMode)GUILayout.Toolbar( (int)tool.MoveMode, MoveNames, EditorStyles.miniButton );
				if ( move != tool.MoveMode ) tool.MoveMode = move;

				GridGUI();
			}

			HeaderGUI( tool );

			if ( tool.LastActionName != null && tool.Mode is not (EditMode.Primitive or EditMode.Paint) )
				Button( $"Repeat: {tool.LastActionName}", "Shift+G", tool.RepeatLast );

			switch ( tool.Mode )
			{
				case EditMode.Vertex: VertexGUI( tool ); break;
				case EditMode.Edge: EdgeGUI( tool ); break;
				case EditMode.Face: FaceGUI( tool ); break;
				case EditMode.Object: ObjectGUI( tool ); break;
				case EditMode.Primitive: PrimitiveGUI(); break;
				case EditMode.Paint: PaintGUI( tool ); break;
			}

			HealthGUI( tool );

			// The window has these on its toolbar; the scene view overlay needs them here
			if ( compact && tool.Mode != EditMode.Paint )
			{
				Section( "Settings", () =>
				{
					using ( new GUILayout.HorizontalScope() )
					{
						HammerSettings.TextureLock = GUILayout.Toggle( HammerSettings.TextureLock, new GUIContent( "Tex Lock", "Move textures with the geometry" ), EditorStyles.miniButtonLeft );
						HammerSettings.SelectionThrough = GUILayout.Toggle( HammerSettings.SelectionThrough, new GUIContent( "X-Ray", "Select through geometry" ), EditorStyles.miniButtonMid );
						HammerSettings.LassoSelect = GUILayout.Toggle( HammerSettings.LassoSelect, new GUIContent( "Lasso", "Drag a free lasso to select instead of a box" ), EditorStyles.miniButtonMid );
						HammerSettings.TextureLockComponents = GUILayout.Toggle( HammerSettings.TextureLockComponents, new GUIContent( "UV Lock", "Texture Lock Component Manipulations: textures stretch with moved vertices, edges and faces" ), EditorStyles.miniButtonMid );
						HammerSettings.GlobalSpace = GUILayout.Toggle( HammerSettings.GlobalSpace, new GUIContent( "Global", "Transform in world space" ), EditorStyles.miniButtonRight );
					}
				} );
			}

			MaterialGUI();
		}

		/// <summary>
		/// Shown when the meshes being edited have broken faces: what's wrong and how to fix it.
		/// </summary>
		static void HealthGUI( HammerMeshTool tool )
		{
			if ( tool.Mode is EditMode.Primitive or EditMode.Paint ) return;

			var problems = tool.HealthMeshes().SelectMany( c => MeshHealth.BadFaces( c ).Values ).ToList();
			if ( problems.Count == 0 ) return;

			Section( "Mesh Health", () =>
			{
				var style = new GUIStyle( EditorStyles.wordWrappedMiniLabel ) { normal = { textColor = new Color( 1.0f, 0.5f, 0.45f ) } };
				GUILayout.Label( $"{MeshHealth.Describe( problems )} (outlined in red). Folded faces break rendering and collision; bent ones render with a crease.", style );

				var broken = problems.Count( p => p != Sandbox.PolygonMesh.FaceProblem.NonPlanar );
				var bent = problems.Count( p => (p & Sandbox.PolygonMesh.FaceProblem.NonPlanar) != 0 );

				Button( "Clean Up (all of the below)", "", tool.CleanUp );
				Button( "Remove Broken Faces", "", tool.RemoveBrokenFaces, broken > 0 );
				Button( "Make Faces Planar", "", tool.MakePlanar, bent > 0 );
				Button( "Weld Doubled Vertices", "", tool.WeldVertices );
			} );
		}

		static void HeaderGUI( HammerMeshTool tool )
		{
			var (title, icon, count) = tool.Mode switch
			{
				EditMode.Vertex => ("Vertex", HammerIcons.Vertex, $"{tool.SelectedVertices.Count()} selected"),
				EditMode.Edge => ("Edge", HammerIcons.Edge, $"{tool.SelectedEdges.Count()} selected"),
				EditMode.Face => ("Face", HammerIcons.Face, $"{tool.SelectedFaces.Count()} selected"),
				EditMode.Object => ("Object", HammerIcons.Object, $"{UnityEditor.Selection.gameObjects.Length} selected"),
				EditMode.Primitive => ("Block Tool", HammerIcons.Block, ""),
				_ => ("Vertex Paint", HammerIcons.Paint, ""),
			};

			Header( title, icon, count );
		}

		static void Header( string title, Texture icon, string right )
		{
			var rect = GUILayoutUtility.GetRect( 0, 34, GUILayout.ExpandWidth( true ) );
			if ( Event.current.type == EventType.Repaint )
			{
				EditorGUI.DrawRect( rect, HeaderBack );
				EditorGUI.DrawRect( new Rect( rect.x, rect.yMax - 2, rect.width, 2 ), Accent );
			}

			var x = rect.x + 6;
			if ( icon != null )
			{
				GUI.DrawTexture( new Rect( x, rect.y + 5, 22, 22 ), icon, ScaleMode.ScaleToFit );
				x += 28;
			}

			GUI.Label( new Rect( x, rect.y, rect.width - (x - rect.x), rect.height - 2 ), title, _header );
			if ( !string.IsNullOrEmpty( right ) )
				GUI.Label( new Rect( rect.x, rect.y, rect.width - 8, rect.height - 2 ), right, _count );

			GUILayout.Space( 2 );
		}

		// ── Layout helpers ──

		/// <summary>
		/// A collapsible section with a title bar; open/closed is remembered per title.
		/// </summary>
		static void Section( string title, Action body )
		{
			var key = "Hammer.Panel." + title;
			var open = EditorPrefs.GetBool( key, true );

			GUILayout.Space( 2 );
			var rect = GUILayoutUtility.GetRect( 0, 24, GUILayout.ExpandWidth( true ) );
			var e = Event.current;

			if ( e.type == EventType.Repaint )
			{
				// Hammer style: centred title on a black bar, [+]/[-] box on the right
				EditorGUI.DrawRect( rect, HeaderBack );
				_sectionTitle.Draw( rect, title, false, false, false, false );

				var box = new Rect( rect.xMax - 18, rect.y + 6, 12, 12 );
				var c = rect.Contains( e.mousePosition ) ? HammerIcons.Accent : new Color( 0.55f, 0.55f, 0.55f );
				EditorGUI.DrawRect( new Rect( box.x, box.y, box.width, 1 ), c );
				EditorGUI.DrawRect( new Rect( box.x, box.yMax - 1, box.width, 1 ), c );
				EditorGUI.DrawRect( new Rect( box.x, box.y, 1, box.height ), c );
				EditorGUI.DrawRect( new Rect( box.xMax - 1, box.y, 1, box.height ), c );
				EditorGUI.DrawRect( new Rect( box.x + 3, box.y + 5.5f, 6, 1 ), c );
				if ( !open ) EditorGUI.DrawRect( new Rect( box.x + 5.5f, box.y + 3, 1, 6 ), c );
			}

			if ( e.type == EventType.MouseDown && e.button == 0 && rect.Contains( e.mousePosition ) )
			{
				open = !open;
				EditorPrefs.SetBool( key, open );
				e.Use();
			}

			if ( !open )
				return;

			var area = EditorGUILayout.BeginVertical( _section );
			if ( e.type == EventType.Repaint )
				EditorGUI.DrawRect( area, SectionBack );
			body();
			EditorGUILayout.EndVertical();
		}

		/// <summary>
		/// A small grey label splitting a section into groups.
		/// </summary>
		static void Sub( string label ) => GUILayout.Label( label, _subLabel );

		/// <summary>
		/// A column of full width buttons, like Hammer's tool properties.
		/// </summary>
		static void Grid( params (string label, string key, Action action, bool enabled)[] buttons )
		{
			foreach ( var b in buttons )
				Button( b.label, b.key, b.action, b.enabled );
		}

		static HammerMeshTool _tool;

		/// <summary>
		/// Buttons that select, show or open things rather than edit: not remembered for Repeat Last.
		/// </summary>
		static readonly System.Collections.Generic.HashSet<string> NotEdits = new()
		{
			"Select All", "Invert", "Grow", "Shrink", "Frame", "Loop", "Ring", "Ribs", "Select Loop",
			"Clear Pivot", "Unhide All", "Fast Texture Tool", "Apply", "Cancel", "Select Contiguous",
			"Previous", "Next", "View Center", "World Origin", "Clear", "Find / Replace Materials", "Displacement Tool",
			"Select All Instances", "Mesh Projection", "Path Tool",
		};

		static void Button( string label, string key, Action action, bool enabled = true )
		{
			// Edits are remembered for Repeat Last (Shift+G)
			if ( _tool != null && !NotEdits.Contains( label ) && !label.StartsWith( "Repeat:" ) )
			{
				var edit = action;
				var tool = _tool;
				action = () => { tool.Remember( label, edit ); edit(); };
			}

			var tip = string.IsNullOrEmpty( key ) ? label : $"{label}  ({key})";

			using ( new EditorGUI.DisabledScope( !enabled ) )
			{
				var rect = GUILayoutUtility.GetRect( GUIContent.none, _button, GUILayout.MinWidth( 60 ), GUILayout.ExpandWidth( true ) );
				var e = Event.current;

				if ( e.type == EventType.Repaint )
				{
					// Flat dark button, lighter under the mouse
					var hover = enabled && rect.Contains( e.mousePosition );
					EditorGUI.DrawRect( rect, hover ? HammerIcons.ButtonHover : HammerIcons.Button );
					EditorGUI.DrawRect( new Rect( rect.x, rect.yMax - 1, rect.width, 1 ), HammerIcons.Divider );

					var old = GUI.color;
					if ( !enabled ) GUI.color = new Color( 1, 1, 1, 0.4f );
					_buttonText.Draw( rect, label, false, false, false, false );
					if ( !string.IsNullOrEmpty( key ) )
						_buttonKey.Draw( rect, key, false, false, false, false );
					GUI.color = old;
				}

				GUI.Label( rect, new GUIContent( "", tip ), GUIStyle.none );
				if ( enabled && e.type == EventType.MouseDown && e.button == 0 && rect.Contains( e.mousePosition ) )
				{
					e.Use();
					StepUndo( label );
					action();
					EndStep();
				}

				if ( e.type == EventType.MouseMove && rect.Contains( e.mousePosition ) )
					HammerViews.RepaintAll();
			}
		}

		// An open tool (bevel, projection...) records its changes when it opens and writes them
		// when it's applied: closing the undo step in between would lose them
		static bool SubToolOpen => (HammerMeshTool.Focused ?? HammerWindow.OpenTool)?.SubTool != null;

		/// <summary>
		/// Every button is its own undo step: one Ctrl+Z takes back exactly that button.
		/// </summary>
		internal static void StepUndo( string name )
		{
			if ( SubToolOpen ) return;
			Undo.FlushUndoRecordObjects();
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName( name );
		}

		internal static void EndStep()
		{
			if ( SubToolOpen ) return;
			Undo.FlushUndoRecordObjects();
			Undo.IncrementCurrentGroup();
		}

		static void SubToolGUI( SubTool sub )
		{
			Header( sub.Title, null, "" );
			if ( sub.Keys != null ) KeyTable( sub.Keys );
			else EditorGUILayout.LabelField( sub.Help, EditorStyles.wordWrappedMiniLabel );

			Section( "Settings", () => sub.TrackSettings( sub.OnOverlayGUI ) );

			using ( new GUILayout.HorizontalScope() )
			{
				Button( "Apply", "Enter", sub.Apply );
				Button( "Cancel", "Esc", sub.Cancel );
			}
		}

		static GUIStyle _keyStyle;

		/// <summary>
		/// Hammer's Key / Operation table for a tool.
		/// </summary>
		static void KeyTable( (string Key, string Operation)[] keys )
		{
			_keyStyle ??= new GUIStyle( EditorStyles.miniLabel ) { normal = { textColor = new Color( 0.95f, 0.62f, 0.2f ) } };
			GUILayout.Space( 4 );
			var head = GUILayoutUtility.GetRect( 0, 20, GUILayout.ExpandWidth( true ) );
			var keyWidth = Mathf.Min( 110, head.width * 0.4f );
			if ( Event.current.type == EventType.Repaint )
			{
				EditorGUI.DrawRect( head, HeaderBack );
				EditorGUI.DrawRect( new Rect( head.x + keyWidth, head.y, 1, head.height ), SectionBack );
			}
			GUI.Label( new Rect( head.x, head.y, keyWidth, head.height ), "Key", EditorStyles.centeredGreyMiniLabel );
			GUI.Label( new Rect( head.x + keyWidth, head.y, head.width - keyWidth, head.height ), "Operation", EditorStyles.centeredGreyMiniLabel );

			for ( int i = 0; i < keys.Length; i++ )
			{
				var row = GUILayoutUtility.GetRect( 0, 18, GUILayout.ExpandWidth( true ) );
				if ( Event.current.type == EventType.Repaint )
					EditorGUI.DrawRect( row, i % 2 == 0 ? SectionBack : new Color( 0.13f, 0.13f, 0.13f ) );
				GUI.Label( new Rect( row.x + 6, row.y, keyWidth - 6, row.height ), $"[{keys[i].Key}]", _keyStyle );
				GUI.Label( new Rect( row.x + keyWidth + 6, row.y, row.width - keyWidth - 6, row.height ), keys[i].Operation, EditorStyles.miniLabel );
			}
			GUILayout.Space( 4 );
		}

		public static void GridGUI()
		{
			using ( new GUILayout.HorizontalScope() )
			{
				GUILayout.Label( "Grid", GUILayout.Width( 30 ) );
				if ( GUILayout.Button( "[", EditorStyles.miniButtonLeft, GUILayout.Width( 20 ) ) ) HammerSettings.GridSmaller();
				GUILayout.Label( HammerSettings.GridSize.ToString( "0.###" ), EditorStyles.centeredGreyMiniLabel, GUILayout.Width( 36 ) );
				if ( GUILayout.Button( "]", EditorStyles.miniButtonRight, GUILayout.Width( 20 ) ) ) HammerSettings.GridLarger();
				HammerSettings.GridSnap = GUILayout.Toggle( HammerSettings.GridSnap, "Snap", EditorStyles.miniButton );
			}
		}

		// ── Material palette (bottom of the panel, like s&box) ──

		static void MaterialGUI()
		{
			Section( "Material", () =>
			{
				var active = HammerSettings.ActiveMaterial;

				using ( new GUILayout.HorizontalScope() )
				{
					// Big swatch of the active material
					var rect = GUILayoutUtility.GetRect( 96, 96, GUILayout.Width( 96 ), GUILayout.Height( 96 ) );
					DrawSwatch( rect, active );

					using ( new GUILayout.VerticalScope() )
					{
						GUILayout.Label( active != null ? active.name : "Dev grid (default)", EditorStyles.wordWrappedMiniLabel );
						var picked = (Material)EditorGUILayout.ObjectField( active, typeof( Material ), false );
						if ( picked != active ) HammerSettings.ActiveMaterial = picked;
						GUILayout.Label( "Shift+T applies to faces", EditorStyles.centeredGreyMiniLabel );
					}
				}

				// Hammer-style dev textures that ship with the package, wrapped to the panel width
				var dev = DevMaterials;
				SwatchRows( "Dev: measure", dev.Where( m => m.name.StartsWith( "Dev Measure" ) ).ToList(), active );
				SwatchRows( "Dev: reflectivity", dev.Where( m => m.name.StartsWith( "Dev Reflectivity" ) ).ToList(), active );

				// Palette of recently used materials
				var recent = HammerSettings.RecentMaterials;
				if ( recent.Count > 0 ) Sub( "Recent" );
				if ( recent.Count > 0 )
				{
					GUILayout.Space( 4 );
					const int perRow = 6;
					for ( int i = 0; i < recent.Count; i += perRow )
					{
						using ( new GUILayout.HorizontalScope() )
						{
							for ( int j = i; j < Math.Min( i + perRow, recent.Count ); j++ )
							{
								var r = GUILayoutUtility.GetRect( 32, 32, GUILayout.Width( 32 ), GUILayout.Height( 32 ) );
								DrawSwatch( r, recent[j] );

								if ( recent[j] == active )
									DrawOutline( r, new Color( 0.3f, 0.6f, 1.0f ) );

								if ( Event.current.type == EventType.MouseDown && r.Contains( Event.current.mousePosition ) )
								{
									HammerSettings.ActiveMaterial = recent[j];
									Event.current.Use();
								}
							}

							GUILayout.FlexibleSpace();
						}
					}
				}
			} );
		}

		static readonly System.Collections.Generic.Dictionary<string, float> _swatchAreaWidth = new();

		static void SwatchRows( string title, System.Collections.Generic.List<Material> materials, Material active )
		{
			if ( materials.Count == 0 ) return;
			Sub( title );

			// How many fit: measured on the last repaint (layout events don't know the width)
			// (worked out before updating it, so layout and repaint agree within a frame)
			var width = _swatchAreaWidth.TryGetValue( title, out var w ) ? w : 260;
			var perRow = Mathf.Max( 1, Mathf.FloorToInt( (width + 2) / 30 ) );
			var area = GUILayoutUtility.GetRect( 0, 0, GUILayout.ExpandWidth( true ) );
			if ( Event.current.type == EventType.Repaint && area.width > 1 && Mathf.Abs( area.width - width ) > 0.5f )
			{
				_swatchAreaWidth[title] = area.width;
				HammerViews.RepaintAll();
			}

			for ( int i = 0; i < materials.Count; i += perRow )
			{
				using ( new GUILayout.HorizontalScope() )
				{
					for ( int j = i; j < Math.Min( i + perRow, materials.Count ); j++ )
					{
						var m = materials[j];
						var r = GUILayoutUtility.GetRect( 28, 28, GUILayout.Width( 28 ), GUILayout.Height( 28 ) );
						DrawSwatch( r, m );
						if ( m == active ) DrawOutline( r, HammerIcons.Accent );
						GUI.Label( r, new GUIContent( "", m.name ), GUIStyle.none );

						if ( Event.current.type == EventType.MouseDown && r.Contains( Event.current.mousePosition ) )
						{
							HammerSettings.ActiveMaterial = m;
							Event.current.Use();
						}
					}
					GUILayout.FlexibleSpace();
				}
			}
		}

		static System.Collections.Generic.List<Material> _devMaterials;

		static System.Collections.Generic.List<Material> DevMaterials => _devMaterials ??= AssetDatabase
			.FindAssets( "t:Material", new[] { "Packages/com.hammerunity.meshtools/Runtime/DevTextures" } )
			.Select( g => AssetDatabase.LoadAssetAtPath<Material>( AssetDatabase.GUIDToAssetPath( g ) ) )
			.Where( m => m != null )
			.OrderBy( m => m.name )
			.ToList();

		static void DrawSwatch( Rect rect, Material material )
		{
			if ( Event.current.type != EventType.Repaint ) return;

			EditorGUI.DrawRect( rect, new Color( 0.12f, 0.12f, 0.12f ) );

			// The flat texture reads better than Unity's sphere preview for dev textures
			Texture preview = material != null && material.HasProperty( "_MainTex" ) ? material.mainTexture : null;
			if ( preview == null && material != null ) preview = AssetPreview.GetAssetPreview( material );
			if ( preview == null && material == null ) preview = HammerMaterials.Default.mainTexture;

			if ( preview != null )
				GUI.DrawTexture( new Rect( rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2 ), preview, ScaleMode.ScaleToFit );
		}

		static void DrawOutline( Rect r, Color c )
		{
			EditorGUI.DrawRect( new Rect( r.x, r.y, r.width, 2 ), c );
			EditorGUI.DrawRect( new Rect( r.x, r.yMax - 2, r.width, 2 ), c );
			EditorGUI.DrawRect( new Rect( r.x, r.y, 2, r.height ), c );
			EditorGUI.DrawRect( new Rect( r.xMax - 2, r.y, 2, r.height ), c );
		}

		// ── Modes ──

		static void VertexGUI( HammerMeshTool tool )
		{
			var count = tool.SelectedVertices.Count();

			Section( "Edit", () =>
			{
				Grid(
					("Connect", "V", tool.ConnectVertices, count > 1),
					("Bevel", "F", tool.BevelVertices, count > 0),
					("Edge Cut", "C", () => EdgeCutTool.Open( tool ), true) );

				Sub( "Merge" );
				using ( new GUILayout.HorizontalScope() )
				{
					GUILayout.Label( "Range", EditorStyles.miniLabel, GUILayout.Width( 44 ) );
					var range = GUILayout.Toolbar( HammerSettings.MergeInfinite ? 0 : 1, new[] { "Infinite", "Distance" }, EditorStyles.miniButton );
					HammerSettings.MergeInfinite = range == 0;
				}
				if ( !HammerSettings.MergeInfinite )
					HammerSettings.MergeDistance = EditorGUILayout.FloatField( "Distance", HammerSettings.MergeDistance );
				Button( "Merge", "M", tool.MergeVertices, count > 1 );

				Sub( "Snap" );
				Grid(
					("To Last", "B", tool.SnapToLastVertex, count > 1),
					("To Grid", "Ctrl+B", tool.SnapToGrid, count > 0) );

				Sub( "Other" );
				Grid(
					("Weld UVs", "Ctrl+F", tool.WeldUVs, count > 0),
					("Delete", "Del", tool.Delete, count > 0) );
			} );

			AlignSection( tool, count );
			SelectionSection( tool, count > 0, loop: false );
			DisplaySection( EditMode.Vertex );
		}

		static void EdgeGUI( HammerMeshTool tool )
		{
			var edges = tool.SelectedEdges.ToList();
			var count = edges.Count;
			var open = edges.Any( x => x.IsOpen );

			Section( "Edit", () =>
			{
				Grid(
					("Extrude", "Shift+Drag", tool.ExtrudeEdges, count > 0),
					("Connect", "V", tool.ConnectEdges, count > 1),
					("Edge Cut", "C", () => EdgeCutTool.Open( tool ), true),
					("Split", "Alt+N", tool.SplitEdges, count > 0),
					("Snap To Edge", "I", tool.SnapEdgeToEdge, count == 2) );

				Sub( "Bevel" );
				Grid(
					("Bevel", "F", tool.QuickBevelEdges, count > 0),
					("Bevel Tool", "Alt+F", () => BevelTool.Open( tool ), count > 0) );
				using ( new GUILayout.HorizontalScope() )
				{
					GUILayout.Label( "Segments", EditorStyles.miniLabel, GUILayout.Width( 60 ) );
					HammerSettings.BevelSegments = EditorGUILayout.IntSlider( HammerSettings.BevelSegments, 1, 16 );
				}

				Sub( "Open edges" );
				Grid(
					("Extend", "N", tool.ExtendEdges, open),
					("Fill Hole", "P", tool.FillHole, open),
					("Bridge", "B", tool.BridgeEdges, count > 1 && open),
					("Bridge Tool", "Alt+B", () => BridgeTool.Open( tool ), count > 1 && open),
					("Arch", "Y", () => EdgeArchTool.Open( tool ), open),
					("Merge", "M", tool.MergeEdges, count == 2 && open),
					("Path Extrude", "Alt+X", () => PathExtrudeTool.Open( tool ), edges.Select( x => x.Component ).Distinct().Count() == 2) );

				Sub( "Remove" );
				Grid(
					("Dissolve", "Bksp", tool.DissolveEdges, count > 0),
					("Collapse", "Shift+O", tool.Collapse, count > 0),
					("Delete", "Del", tool.Delete, count > 0) );
			} );

			AlignSection( tool, count );

			Section( "Normals & UVs", () => Grid(
				("Hard", "H", () => tool.SetEdgeNormals( PolygonMesh.EdgeSmoothMode.Hard ), count > 0),
				("Soft", "J", () => tool.SetEdgeNormals( PolygonMesh.EdgeSmoothMode.Soft ), count > 0),
				("Default", "K", () => tool.SetEdgeNormals( PolygonMesh.EdgeSmoothMode.Default ), count > 0),
				("Weld UVs", "Ctrl+F", tool.WeldUVs, count > 0) ) );

			Section( "UV Peel", () =>
			{
				using ( new GUILayout.HorizontalScope() )
				{
					GUILayout.Label( "Primary", EditorStyles.miniLabel, GUILayout.Width( 44 ) );
					HammerSettings.PeelAlongV = GUILayout.Toolbar( HammerSettings.PeelAlongV ? 1 : 0, new[] { "U Axis", "V Axis" }, EditorStyles.miniButton ) == 1;
				}
				HammerSettings.PeelWorldSpace = EditorGUILayout.ToggleLeft( "World Space", HammerSettings.PeelWorldSpace );
				using ( new EditorGUI.DisabledScope( HammerSettings.PeelWorldSpace ) )
				{
					HammerSettings.PeelURepeats = EditorGUILayout.FloatField( "U Repeats", HammerSettings.PeelURepeats );
					HammerSettings.PeelVRepeats = EditorGUILayout.FloatField( "V Repeats", HammerSettings.PeelVRepeats );
				}
				HammerSettings.PeelUOffset = EditorGUILayout.FloatField( "U Offset", HammerSettings.PeelUOffset );
				HammerSettings.PeelVOffset = EditorGUILayout.FloatField( "V Offset", HammerSettings.PeelVOffset );
				Button( "UV Peel", "", tool.PeelUVs, count > 0 );
			} );

			Section( "Selection", () => Grid(
				("Loop", "L", tool.SelectLoop, count > 0),
				("Ring", "G", tool.SelectRing, count > 0),
				("Ribs", "Ctrl+G", tool.SelectRibs, count > 0),
				("Select All", "Ctrl+A", tool.SelectAll, true),
				("Grow", "Num +", tool.GrowSelection, count > 0),
				("Shrink", "Num -", tool.ShrinkSelection, count > 0) ) );

			DisplaySection( EditMode.Edge );
		}

		static void FaceGUI( HammerMeshTool tool )
		{
			var count = tool.SelectedFaces.Count();

			Section( "Edit", () =>
			{
				Sub( "Build" );
				Grid(
					("Extrude", "Shift+Drag", tool.ExtrudeFaces, count > 0),
					("Inset", "Shift+I", () => InsetTool.Open( tool ), count > 0),
					("Thicken", "G", tool.ThickenFaces, count > 0),
					("Bridge", "Alt+B", () => BridgeTool.Open( tool ), count > 1) );

				Sub( "Cut" );
				Grid(
					("Edge Cut", "C", () => EdgeCutTool.Open( tool ), true),
					("Clip", "Shift+X", () => ClipTool.Open( tool ), count > 0),
					("Mirror", "Shift+F", () => MirrorTool.Open( tool ), count > 0) );
				using ( new GUILayout.HorizontalScope() )
				{
					Button( "Quad Slice", "Ctrl+D", tool.QuadSlice, count > 0 );
					var cuts = HammerSettings.QuadSliceCuts;
					cuts.x = EditorGUILayout.IntField( cuts.x, GUILayout.Width( 28 ) );
					GUILayout.Label( "×", EditorStyles.miniLabel, GUILayout.Width( 10 ) );
					cuts.y = EditorGUILayout.IntField( cuts.y, GUILayout.Width( 28 ) );
					HammerSettings.QuadSliceCuts = cuts;
				}

				Sub( "Modify" );
				Grid(
					("Merge Meshes", "", tool.MergeSelectedFacesMeshes, tool.Selection.Components.Count() > 1),
					("Remove Bad Faces", "", tool.RemoveBrokenFaces, true),
					("Mesh Projection", "", () => MeshProjectionTool.Open( tool ), count > 0),
					("Detach", "N", tool.DetachFaces, count > 0),
					("Extract", "Alt+N", tool.ExtractFaces, count > 0),
					("Combine", "Bksp", tool.CombineFaces, count > 1),
					("Collapse", "Shift+O", tool.Collapse, count > 0),
					("Flip", "F", tool.FlipFaces, count > 0),
					("Snap To Grid", "Ctrl+B", tool.SnapToGrid, count > 0),
					("Delete", "Del", tool.Delete, count > 0) );

				Sub( "Visibility" );
				Grid(
					("Hide", "H", tool.HideFaces, count > 0),
					("Unhide All", "U", tool.UnhideFaces, true) );
			} );

			SubdivisionSection( tool, count > 0 );

			AlignSection( tool, count );
			SelectionSection( tool, count > 0, loop: true );

			Section( "Filtered Selection", () =>
			{
				Button( "Select Contiguous", "", tool.SelectContiguous, count > 0 );
				HammerSettings.FilterMaterial = EditorGUILayout.ToggleLeft( "Use Material", HammerSettings.FilterMaterial );
				using ( new GUILayout.HorizontalScope() )
				{
					HammerSettings.FilterNormal = EditorGUILayout.ToggleLeft( "Use Normal", HammerSettings.FilterNormal, GUILayout.Width( 90 ) );
					using ( new EditorGUI.DisabledScope( !HammerSettings.FilterNormal ) )
						HammerSettings.FilterNormalAngle = EditorGUILayout.FloatField( HammerSettings.FilterNormalAngle, GUILayout.Width( 50 ) );
					GUILayout.Label( "degrees", EditorStyles.miniLabel );
				}
			} );

			Section( "Texture", () =>
			{
				Button( "Fast Texture Tool", "Ctrl+G", () => FastTextureWindow.Open( tool ), count > 0 );
				Button( "Find / Replace Materials", "", () => FindReplaceMaterialsWindow.Open( tool ), true );
				Grid(
					("Apply Material", "Shift+T", tool.ApplyMaterial, count > 0),
					("Fit", "", () => tool.JustifyTexture( PolygonMesh.TextureJustification.Fit ), count > 0),
					("Align To Grid", "", tool.TextureAlignToGrid, count > 0),
					("Align To Face", "", tool.TextureAlignToFace, count > 0) );

				using ( new EditorGUI.DisabledScope( count == 0 ) )
				{
					Sub( "Adjust" );
					Strip( "Justify", ("L", () => tool.JustifyTexture( PolygonMesh.TextureJustification.Left )),
						("R", () => tool.JustifyTexture( PolygonMesh.TextureJustification.Right )),
						("T", () => tool.JustifyTexture( PolygonMesh.TextureJustification.Top )),
						("B", () => tool.JustifyTexture( PolygonMesh.TextureJustification.Bottom )),
						("C", () => tool.JustifyTexture( PolygonMesh.TextureJustification.Center )) );

					var step = HammerSettings.GridSize;
					Strip( "Shift", ("←", () => tool.ShiftTexture( new Vector2( -step, 0 ) )),
						("→", () => tool.ShiftTexture( new Vector2( step, 0 ) )),
						("↑", () => tool.ShiftTexture( new Vector2( 0, -step ) )),
						("↓", () => tool.ShiftTexture( new Vector2( 0, step ) )) );

					Strip( "Scale", ("½", () => tool.ScaleTexture( 0.5f )), ("×2", () => tool.ScaleTexture( 2.0f )) );

					Strip( "Rotate", ("-90°", () => tool.RotateTexture( -90 )), ("-15°", () => tool.RotateTexture( -15 )),
						("+15°", () => tool.RotateTexture( 15 )), ("+90°", () => tool.RotateTexture( 90 )) );

					var first = tool.SelectedFaces.FirstOrDefault();
					if ( first.IsValid )
					{
						Sub( "Values (first face)" );
						var mesh = first.Component.Mesh;
						var offset = mesh.GetTextureOffset( first.Handle );
						var scale = mesh.GetTextureScale( first.Handle );

						EditorGUI.BeginChangeCheck();
						var newOffset = EditorGUILayout.Vector2Field( "Offset", new Vector2( offset.x, offset.y ) );
						if ( EditorGUI.EndChangeCheck() ) tool.SetTextureOffsetScale( newOffset, null );

						EditorGUI.BeginChangeCheck();
						var newScale = EditorGUILayout.Vector2Field( "Scale", new Vector2( scale.x, scale.y ) );
						if ( EditorGUI.EndChangeCheck() ) tool.SetTextureOffsetScale( null, newScale );
					}
				}
			} );

			DisplaySection( EditMode.Face );
		}

		/// <summary>
		/// Lining the selection up: Move to Furthest along an axis, Radial Align, and dropping it
		/// onto what's below.
		/// </summary>
		static void AlignSection( HammerMeshTool tool, int count )
		{
			Section( "Align", () =>
			{
				using ( new EditorGUI.DisabledScope( count < 2 ) )
				{
					Sub( "Move to furthest" );
					Strip( "", ("X+", () => tool.MoveToFurthest( 0, 1 )), ("X-", () => tool.MoveToFurthest( 0, -1 )),
						("Y+", () => tool.MoveToFurthest( 1, 1 )), ("Y-", () => tool.MoveToFurthest( 1, -1 )),
						("Z+", () => tool.MoveToFurthest( 2, 1 )), ("Z-", () => tool.MoveToFurthest( 2, -1 )) );
				}

				Grid(
					("Radial Align", "", tool.RadialAlign, tool.SelectionVertexCount >= 3),
					("Move Path Trace Down", "", tool.MovePathTraceDown, count > 0) );
			} );
		}

		/// <summary>
		/// Smooth subdivision levels (shown without changing the editable mesh), baking, the
		/// plain four-way split and the displacement brush.
		/// </summary>
		static void SubdivisionSection( HammerMeshTool tool, bool any )
		{
			Section( "Subdivision", () =>
			{
				using ( new GUILayout.HorizontalScope() )
				{
					Button( "Increase", "", tool.IncreaseSubdivision, any );
					Button( "Decrease", "", tool.DecreaseSubdivision, any );
				}
				for ( int row = 0; row < 2; row++ )
				{
					using ( new GUILayout.HorizontalScope() )
					{
						for ( int col = 0; col < 3; col++ )
						{
							var level = row * 3 + col;
							Button( $"Level {level}", "", () => tool.SetSubdivision( level ), any );
						}
					}
				}
				Button( "Bake Subdivision", "", tool.BakeSubdivision, any );

				Sub( "Detail" );
				Grid(
					("Split Faces in Four", "", tool.Subdivide, any),
					("Displacement Tool", "", () => DisplacementTool.Open( tool ), true) );
			} );
		}

		/// <summary>
		/// Hammer's Display box.
		/// </summary>
		static void DisplaySection( EditMode mode )
		{
			Section( "Display", () =>
			{
				if ( mode == EditMode.Edge )
					HammerSettings.ShowHardSoftEdges = EditorGUILayout.ToggleLeft( "Show Hard / Soft Edges", HammerSettings.ShowHardSoftEdges );
				HammerSettings.ShowNormals = EditorGUILayout.ToggleLeft( "Show Normals", HammerSettings.ShowNormals );
				HammerSettings.DrawWireframe = EditorGUILayout.ToggleLeft( "Draw Wireframe", HammerSettings.DrawWireframe );
				if ( mode == EditMode.Edge )
					HammerSettings.EdgeLengthPreview = EditorGUILayout.ToggleLeft( "Edge Length Preview", HammerSettings.EdgeLengthPreview );
			} );
		}

		static void SelectionSection( HammerMeshTool tool, bool any, bool loop )
		{
			Section( "Selection", () =>
			{
				if ( loop )
					Button( "Select Loop", "L", tool.SelectLoop, any );

				if ( tool.HasCustomPivot )
					Button( "Clear Pivot", "", tool.ClearPivot );

				Grid(
					("Select All", "Ctrl+A", tool.SelectAll, true),
					("Invert", "Ctrl+I", tool.InvertSelection, true),
					("Grow", "Num +", tool.GrowSelection, any),
					("Shrink", "Num -", tool.ShrinkSelection, any),
					("Frame", "Shift+A", tool.FrameSelection, any) );
			} );
		}

		static void Strip( string label, params (string text, Action action)[] buttons )
		{
			using ( new GUILayout.HorizontalScope() )
			{
				if ( !string.IsNullOrEmpty( label ) )
					GUILayout.Label( label, EditorStyles.miniLabel, GUILayout.Width( 44 ) );
				for ( int i = 0; i < buttons.Length; i++ )
				{
					var style = buttons.Length == 1 ? EditorStyles.miniButton : i == 0 ? EditorStyles.miniButtonLeft : i == buttons.Length - 1 ? EditorStyles.miniButtonRight : EditorStyles.miniButtonMid;
					if ( GUILayout.Button( buttons[i].text, style ) ) buttons[i].action();
				}
			}
		}

		static readonly (string, string)[] ObjectKeys =
		{
			("Ctrl+D", "Set origin to pivot position"),
			("End", "Set origin to object center"),
			("Alt+O", "Set origin to target under cursor"),
			("Alt+T", "Align to target under cursor"),
			("Alt+R", "Rotate to target under cursor"),
			("Alt+X", "Pin to target under cursor"),
			("B", "Snap position to last selected object"),
			("Alt+B", "Align to last selected object"),
			("Alt+E", "Align selected objects to workplane"),
			("Alt+Q", "Align workplane to selected object"),
			("Ctrl+Num2", "Move object down by tracing"),
		};

		static void ObjectGUI( HammerMeshTool tool )
		{
			var meshes = UnityEditor.Selection.gameObjects.Count( x => x.GetComponent<HammerMesh>() != null );

			Section( "Object Building", () => KeyTable( ObjectKeys ) );

			Section( "Edit", () =>
			{
				Grid(
					("Set Origin To Pivot", "Ctrl+D", tool.SetOriginToPivot, meshes > 0),
					("Center Origin", "End", tool.CenterOrigin, meshes > 0),
					("Freeze Transform", "", tool.FreezeTransform, meshes > 0),
					("Clear Rotation & Scale", "Ctrl+Num0", tool.ClearRotationAndScale, meshes > 0),
					("Merge", "M", tool.MergeMeshes, meshes > 1),
					("Merge by Edge", "", tool.MergeMeshesByEdge, meshes > 1),
					("Separate", "Alt+N", tool.SeparateComponents, meshes > 0),
					("Flip Faces", "F", tool.FlipFaces, meshes > 0) );

				Sub( "Cut" );
				Grid(
					("Clip", "Shift+X", () => ClipTool.Open( tool ), meshes > 0),
					("Mirror", "Shift+F", () => MirrorTool.Open( tool ), meshes > 0) );

			} );

			Section( "Instances", () =>
			{
				var selected = UnityEditor.Selection.gameObjects.Select( g => g.GetComponent<HammerMesh>() ).Where( c => c != null ).ToList();
				var active = UnityEditor.Selection.activeGameObject != null ? UnityEditor.Selection.activeGameObject.GetComponent<HammerMesh>() : null;
				if ( active != null && !string.IsNullOrEmpty( active.InstanceGroup ) )
					GUILayout.Label( $"Linked instance: {HammerInstances.Members( active.InstanceGroup ).Count} copies", EditorStyles.miniLabel );
				Grid(
					("Make Instance", "", () => HammerInstances.MakeInstances( selected, active ), active != null),
					("Select All Instances", "", () => UnityEditor.Selection.objects = HammerInstances.Members( active.InstanceGroup ).Select( c => (UnityEngine.Object)c.gameObject ).ToArray(), active != null && !string.IsNullOrEmpty( active.InstanceGroup )),
					("Collapse Instance", "", () => HammerInstances.Collapse( selected ), selected.Any( c => !string.IsNullOrEmpty( c.InstanceGroup ) )) );
				GUILayout.Label( "Shift+drag an instance to place another linked copy.", EditorStyles.wordWrappedMiniLabel );
			} );

			Section( "Pivot", () =>
			{
				using ( new GUILayout.HorizontalScope() )
				{
					Button( "Previous", "", () => tool.StepPivot( -1 ), meshes > 0 );
					Button( "Next", "", () => tool.StepPivot( 1 ), meshes > 0 );
				}
				using ( new GUILayout.HorizontalScope() )
				{
					Button( "View Center", "", tool.PivotToViewCenter, meshes > 0 );
					Button( "World Origin", "", tool.PivotToWorldOrigin, meshes > 0 );
				}
				Button( "Clear", "", tool.ClearObjectPivot, meshes > 0 );
			} );

			Section( "Place", () =>
			{
				HammerSettings.AlignToSurface = EditorGUILayout.ToggleLeft( new GUIContent( "Align To Surface", "Align to Target also turns the object to the surface" ), HammerSettings.AlignToSurface );
				Grid(
					("Align to Target", "Alt+T", tool.AlignToTarget, meshes > 0),
					("Rotate to Target", "Alt+R", tool.RotateToTarget, meshes > 0),
					("Move Path Trace Down", "Ctrl+Num2", tool.MovePathTraceDown, meshes > 0),
					("Mesh Projection", "", () => MeshProjectionTool.Open( tool ), meshes > 0),
					("Path Tool", "", () => PathTool.Open( tool ), true) );
			} );

			SubdivisionSection( tool, meshes > 0 );

			Section( "Boolean", () =>
			{
				Sub( "Uses the active object as the target" );
				Grid(
					("Union", "", () => tool.Boolean( PolygonMesh.BooleanOperation.Union ), meshes > 1),
					("Subtract", "", () => tool.Boolean( PolygonMesh.BooleanOperation.Subtract ), meshes > 1),
					("Intersect", "", () => tool.Boolean( PolygonMesh.BooleanOperation.Intersect ), meshes > 1) );
			} );
		}

		static void PaintGUI( HammerMeshTool tool )
		{
			Section( "Brush", () =>
			{
				HammerMeshTool.PaintMode = (PaintMode)GUILayout.Toolbar( (int)HammerMeshTool.PaintMode, new[] { "Color", "Blend" }, EditorStyles.miniButton );

				if ( HammerMeshTool.PaintMode == PaintMode.Color )
				{
					HammerMeshTool.PaintColor = EditorGUILayout.ColorField( "Color", HammerMeshTool.PaintColor );
				}
				else
				{
					var b = HammerMeshTool.PaintBlend;
					var channel = b.x > 0.5f ? 0 : b.y > 0.5f ? 1 : b.z > 0.5f ? 2 : 3;
					var newChannel = GUILayout.Toolbar( channel, new[] { "R", "G", "B", "A" }, EditorStyles.miniButton );
					if ( newChannel != channel )
					{
						var v = Vector4.zero;
						v[newChannel] = 1;
						HammerMeshTool.PaintBlend = v;
					}
				}

				HammerMeshTool.PaintRadius = EditorGUILayout.Slider( "Radius", HammerMeshTool.PaintRadius, 1, 512 );
				HammerMeshTool.PaintStrength = EditorGUILayout.Slider( "Strength", HammerMeshTool.PaintStrength, 0, 1 );
				HammerMeshTool.PaintHardness = EditorGUILayout.Slider( "Hardness", HammerMeshTool.PaintHardness, 0, 1 );
			} );

			Section( "Fill Selected Meshes", () => Grid(
				("Fill", "Bksp", () => tool.FloodPaint( false ), true),
				("Clear", "Shift+Bksp", () => tool.FloodPaint( true ), true) ) );

			EditorGUILayout.HelpBox( "Shift erases, Ctrl+wheel sizes the brush, Shift+wheel sets strength. Colours show with the Hammer/Vertex Blend shader.", MessageType.None );
		}

		static void PrimitiveGUI()
		{
			Section( "Shape", () =>
			{
				var names = HammerMeshTool.PrimitiveTypes.Keys.ToArray();
				var index = Array.IndexOf( names, HammerSettings.PrimitiveType );
				var newIndex = GUILayout.SelectionGrid( Mathf.Max( 0, index ), names, 3, EditorStyles.miniButton );
				if ( newIndex != index ) HammerSettings.PrimitiveType = names[newIndex];

				GUILayout.Space( 4 );
				BuilderPropertiesGUI( HammerMeshTool.CurrentBuilder );
			} );

			EditorGUILayout.HelpBox( "2D views: drag out the shape. 3D view: drag the base, release, move for the height, click. Esc cancels.", MessageType.None );
		}

		/// <summary>
		/// A shape setting whose changes go into the undo history.
		/// </summary>
		readonly struct UndoableProperty
		{
			readonly object _target;
			readonly PropertyInfo _info;

			public UndoableProperty( object target, PropertyInfo info ) { _target = target; _info = info; }

			public string Name => _info.Name;
			public bool CanRead => _info.CanRead;
			public bool CanWrite => _info.CanWrite;
			public Type PropertyType => _info.PropertyType;
			public IEnumerable<Attribute> GetCustomAttributes() => _info.GetCustomAttributes();
			public object GetValue( object target ) => _info.GetValue( target );

			string Text( object v ) => Convert.ToString( v, System.Globalization.CultureInfo.InvariantCulture );

			object Parse( string text ) => _info.PropertyType.IsEnum
				? Enum.Parse( _info.PropertyType, text )
				: Convert.ChangeType( text, _info.PropertyType, System.Globalization.CultureInfo.InvariantCulture );

			public void SetValue( object target, object value )
			{
				var old = _info.GetValue( target );
				if ( Equals( old, value ) ) return;

				var info = _info;
				var self = this;
				SettingsUndo.RecordCustom( $"Shape.{target.GetType().Name}.{info.Name}", Text( old ), Text( value ),
					() => self.Text( info.GetValue( target ) ),
					t => { info.SetValue( target, self.Parse( t ) ); HammerViews.RepaintAll(); } );
				_info.SetValue( target, value );
			}
		}

		/// <summary>
		/// Which sides of a box get made, laid out like the box unfolded: Top over Back, Left, Front,
		/// Right, with Bottom underneath. Each button turns its side on or off.
		/// </summary>
		static Sandbox.Primitives.BlockPrimitive.Side BoxSidesGUI( string label, Sandbox.Primitives.BlockPrimitive.Side sides )
		{
			GUILayout.Label( label, EditorStyles.miniBoldLabel );

			const float w = 48, h = 26;
			var area = GUILayoutUtility.GetRect( w * 4, h * 3 + 4, GUILayout.ExpandWidth( true ) );
			var x0 = area.x + (area.width - w * 4) * 0.5f;

			(Sandbox.Primitives.BlockPrimitive.Side Side, int Col, int Row)[] cells =
			{
				(Sandbox.Primitives.BlockPrimitive.Side.Top, 2, 0),
				(Sandbox.Primitives.BlockPrimitive.Side.Back, 0, 1),
				(Sandbox.Primitives.BlockPrimitive.Side.Left, 1, 1),
				(Sandbox.Primitives.BlockPrimitive.Side.Front, 2, 1),
				(Sandbox.Primitives.BlockPrimitive.Side.Right, 3, 1),
				(Sandbox.Primitives.BlockPrimitive.Side.Bottom, 2, 2),
			};

			foreach ( var (side, col, row) in cells )
			{
				var r = new Rect( x0 + col * w + 1, area.y + row * (h + 1), w - 2, h );
				var on = (sides & side) != 0;
				if ( GUI.Toggle( r, on, side.ToString(), EditorStyles.miniButton ) != on )
					sides ^= side;
			}

			return sides;
		}

		/// <summary>
		/// Simple property sheet for the primitive builder's settings (sides, steps, etc).
		/// </summary>
		static void BuilderPropertiesGUI( object builder )
		{
			foreach ( var property in builder.GetType().GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
			{
				var p = new UndoableProperty( builder, property );
				if ( !p.CanRead || !p.CanWrite || p.GetCustomAttributes().Any( a => a.GetType().Name == "HideAttribute" ) )
					continue;

				var label = ObjectNames.NicifyVariableName( p.Name );
				var value = p.GetValue( builder );

				// The box's sides: s&box's unfolded-box picker
				if ( value is Sandbox.Primitives.BlockPrimitive.Side sides )
				{
					var newSides = BoxSidesGUI( label, sides );
					if ( newSides != sides ) p.SetValue( builder, newSides );
					continue;
				}

				// A row of buttons instead of a dropdown
				if ( value is Enum choice && p.GetCustomAttributes().Any( a => a.GetType().Name == "EnumButtonGroupAttribute" ) )
				{
					var values = Enum.GetValues( p.PropertyType );
					var names = values.Cast<Enum>().Select( v => ObjectNames.NicifyVariableName( v.ToString() ) ).ToArray();
					GUILayout.Label( label, EditorStyles.miniBoldLabel );
					var picked = GUILayout.Toolbar( Array.IndexOf( values, choice ), names, EditorStyles.miniButton );
					if ( picked >= 0 && !Equals( values.GetValue( picked ), choice ) ) p.SetValue( builder, values.GetValue( picked ) );
					continue;
				}

				switch ( value )
				{
					case int i:
						var ni = EditorGUILayout.IntField( label, i );
						if ( ni != i ) p.SetValue( builder, Math.Max( 1, ni ) );
						break;

					case float f:
						var nf = EditorGUILayout.FloatField( label, f );
						if ( !Mathf.Approximately( nf, f ) ) p.SetValue( builder, nf );
						break;

					case bool b:
						var nb = EditorGUILayout.Toggle( label, b );
						if ( nb != b ) p.SetValue( builder, nb );
						break;

					case Enum en when p.PropertyType.GetCustomAttributes( typeof( FlagsAttribute ), false ).Length > 0:
						var nflags = EditorGUILayout.EnumFlagsField( label, en );
						if ( !Equals( nflags, en ) ) p.SetValue( builder, nflags );
						break;

					case Enum en:
						var nenum = EditorGUILayout.EnumPopup( label, en );
						if ( !Equals( nenum, en ) ) p.SetValue( builder, nenum );
						break;
				}
			}
		}
	}
}
