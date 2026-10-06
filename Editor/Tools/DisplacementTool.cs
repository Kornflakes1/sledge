using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	public enum DisplaceMode
	{
		PushPull,
		Inflate,
		Flatten,
		Smooth,
		Pinch,
		Grab,
		Noise,
		RaiseTo,
	}

	/// <summary>Which way the brush pushes.</summary>
	public enum DisplaceNormalMode
	{
		Average,
		BrushCenter,
		View,
		X,
		Y,
		Z,
	}

	/// <summary>The level Flatten works to.</summary>
	public enum DisplaceFlattenMode
	{
		Center,
		Average,
		Highest,
		Lowest,
	}

	public enum DisplaceBrushPreset
	{
		Smooth,
		Linear,
		Sphere,
		Sharp,
		Constant,
		Custom,
	}

	/// <summary>
	/// Hammer's displacement tool: sculpt meshes with a brush. Works best on subdivided faces.
	/// Ctrl reverses the brush, Shift smooths, Ctrl+scroll resizes, Shift+scroll sets strength.
	/// Enter keeps the strokes, Esc throws them all away.
	/// </summary>
	public sealed class DisplacementTool : SubTool
	{
		public static DisplaceMode Mode = DisplaceMode.PushPull;
		public static float Radius = 64;

		/// <summary>0 to 100, as in Hammer.</summary>
		public static float Strength = 50;

		/// <summary>Raise To: the height above where the stroke started. Flatten: the level's offset.</summary>
		public static float Offset = 0;

		/// <summary>0 to 100: how hard Shift smooths.</summary>
		public static float SmoothAmount = 50;

		public static DisplaceNormalMode NormalMode = DisplaceNormalMode.Average;
		public static DisplaceFlattenMode FlattenMode = DisplaceFlattenMode.Center;

		/// <summary>Work the level and direction out again for every dab, rather than once per stroke.</summary>
		public static bool UpdatePlane = true;

		/// <summary>Each brush keeps its own radius.</summary>
		public static bool SaveRadius;

		/// <summary>Leave alone surfaces facing away from the camera.</summary>
		public static bool ExcludeBackfaces;

		public static DisplaceBrushPreset Preset = DisplaceBrushPreset.Smooth;
		public static float InWeight = 0.5f, OutWeight = 0.5f, InSlope = 0, OutSlope = 0;

		/// <summary>Only the selected objects (otherwise everything under the brush).</summary>
		public static bool SelectedOnly;
		public static bool HighlightSelected = true;

		static readonly Dictionary<DisplaceMode, float> _savedRadius = new();

		// Older callers' name for the falloff's hard middle: kept for scripting
		public static float Hardness = 0.3f;

		List<HammerMesh> _targets;
		int _undoGroup;
		bool _stroking;
		bool _hasBrush;
		Vector3 _brushPoint;
		Vector3 _brushNormal;
		Vector3 _strokeNormal;
		Vector3 _strokePlanePoint;

		// Grab: where each vertex under the brush started, and how much it follows the mouse
		readonly Dictionary<HammerMesh, List<(VertexHandle Vertex, S.Vector3 Start, float Weight)>> _grab = new();
		Vector3 _grabStart;
		Plane _grabPlane;

		public override string Title => "Displacement";
		public override string Help => "Drag on a surface to sculpt. Ctrl reverses, Shift smooths. Ctrl+scroll: size, Shift+scroll: strength. Subdivide first for detail. Enter keeps, Esc undoes.";

		public override (string Key, string Operation)[] Keys => new[]
		{
			("LMouse", "Sculpt"),
			("Ctrl", "Reverse"),
			("Shift", "Smooth"),
			("Ctrl+Wheel", "Brush size"),
			("Shift+Wheel", "Strength"),
			("Enter", "Keep and close"),
			("Esc", "Undo strokes and close"),
		};

		static readonly string[] BrushNames = { "Push / Pull", "Inflate", "Flatten", "Smooth", "Pinch", "Grab", "Noise", "Raise To" };

		// Hammer's layout: two rows of four
		static readonly DisplaceMode[] BrushOrder =
		{
			DisplaceMode.PushPull, DisplaceMode.Inflate, DisplaceMode.Grab, DisplaceMode.Noise,
			DisplaceMode.Flatten, DisplaceMode.Pinch, DisplaceMode.RaiseTo, DisplaceMode.Smooth,
		};

		static int IconIndex( DisplaceMode mode ) => mode switch
		{
			DisplaceMode.PushPull => 0,
			DisplaceMode.Inflate => 1,
			DisplaceMode.Grab => 2,
			DisplaceMode.Noise => 3,
			DisplaceMode.Flatten => 4,
			DisplaceMode.Pinch => 5,
			DisplaceMode.RaiseTo => 6,
			_ => 7,
		};

		public static void Open( HammerMeshTool tool )
		{
			var d = new DisplacementTool();
			Undo.IncrementCurrentGroup();
			d._undoGroup = Undo.GetCurrentGroup();
			tool.BeginSubTool( d );
		}

		List<HammerMesh> SelectedMeshes() => (Tool.Mode == EditMode.Object
			? UnityEditor.Selection.gameObjects.SelectMany( g => g.GetComponentsInChildren<HammerMesh>() )
			: Tool.Selection.Components.Concat( UnityEditor.Selection.gameObjects.SelectMany( g => g.GetComponentsInChildren<HammerMesh>() ) ))
			.Where( x => x != null ).Distinct().ToList();

		List<HammerMesh> Targets()
		{
			var selected = SelectedMeshes();
			return SelectedOnly && selected.Count > 0 ? selected : MeshPicking.VisibleMeshes();
		}

		// ── Panel ──

		public override void OnOverlayGUI()
		{
			Group( "Displacement tool" );
			for ( int row = 0; row < 2; row++ )
			{
				using ( new GUILayout.HorizontalScope() )
				{
					GUILayout.FlexibleSpace();
					for ( int k = 0; k < 4; k++ )
					{
						var mode = BrushOrder[row * 4 + k];
						if ( BrushButton( mode ) ) SetMode( mode );
						GUILayout.FlexibleSpace();
					}
				}
				GUILayout.Space( 4 );
			}

			Group( "Sculpt" );
			var radius = EditorGUILayout.Slider( "Radius", Radius, 4, 1024 );
			if ( radius != Radius ) { Radius = radius; if ( SaveRadius ) _savedRadius[Mode] = radius; }
			Strength = EditorGUILayout.Slider( "Strength", Strength, 0, 100 );
			using ( new EditorGUI.DisabledScope( Mode != DisplaceMode.RaiseTo && Mode != DisplaceMode.Flatten ) )
				Offset = EditorGUILayout.Slider( "Offset", Offset, -512, 512 );
			SmoothAmount = EditorGUILayout.Slider( "Smooth amount", SmoothAmount, 0, 100 );
			NormalMode = (DisplaceNormalMode)EditorGUILayout.EnumPopup( "Normal mode", NormalMode );
			using ( new EditorGUI.DisabledScope( Mode != DisplaceMode.Flatten ) )
				FlattenMode = (DisplaceFlattenMode)EditorGUILayout.EnumPopup( "Flatten mode", FlattenMode );
			UpdatePlane = EditorGUILayout.Toggle( "Update Plane", UpdatePlane );
			var save = EditorGUILayout.Toggle( "Save Radius", SaveRadius );
			if ( save != SaveRadius ) { SaveRadius = save; if ( save ) _savedRadius[Mode] = Radius; }
			ExcludeBackfaces = EditorGUILayout.Toggle( "Exclude Backfaces", ExcludeBackfaces );

			Group( "Brush" );
			using ( new GUILayout.HorizontalScope() )
			{
				using ( new GUILayout.VerticalScope() )
				{
					var preset = (DisplaceBrushPreset)EditorGUILayout.EnumPopup( "Preset", Preset );
					if ( preset != Preset ) ApplyPreset( preset );
					EditorGUI.BeginChangeCheck();
					InWeight = EditorGUILayout.Slider( "In Weight", InWeight, 0, 1 );
					OutWeight = EditorGUILayout.Slider( "Out Weight", OutWeight, 0, 1 );
					InSlope = EditorGUILayout.Slider( "In Slope", InSlope, -4, 4 );
					OutSlope = EditorGUILayout.Slider( "Out Slope", OutSlope, -4, 4 );
					if ( EditorGUI.EndChangeCheck() ) { Preset = DisplaceBrushPreset.Custom; _curve = null; }
				}
				CurvePreview( GUILayoutUtility.GetRect( 90, 90, GUILayout.Width( 90 ), GUILayout.Height( 90 ) ) );
			}

			Group( "Apply Displacement To" );
			if ( GUILayout.Toggle( !SelectedOnly, " Everything", EditorStyles.radioButton ) ) SelectedOnly = false;
			if ( GUILayout.Toggle( SelectedOnly, " Selected Objects", EditorStyles.radioButton ) ) SelectedOnly = true;
			GUILayout.Space( 4 );
			HighlightSelected = EditorGUILayout.ToggleLeft( "Highlight Selected Objects", HighlightSelected );
		}

		static void Group( string title )
		{
			GUILayout.Space( 6 );
			GUILayout.Label( title, EditorStyles.miniBoldLabel );
		}

		void SetMode( DisplaceMode mode )
		{
			if ( SaveRadius ) _savedRadius[Mode] = Radius;
			Mode = mode;
			if ( SaveRadius && _savedRadius.TryGetValue( mode, out var r ) ) Radius = r;
			HammerViews.RepaintAll();
		}

		static bool BrushButton( DisplaceMode mode )
		{
			var rect = GUILayoutUtility.GetRect( 46, 40, GUILayout.Width( 46 ), GUILayout.Height( 40 ) );
			var clicked = GUI.Button( rect, new GUIContent( "", BrushNames[(int)mode] ), EditorStyles.miniButton );
			if ( Event.current.type == EventType.Repaint )
			{
				if ( mode == Mode )
				{
					var border = new Color( 0.95f, 0.55f, 0.15f );
					EditorGUI.DrawRect( new Rect( rect.x, rect.y, rect.width, 1 ), border );
					EditorGUI.DrawRect( new Rect( rect.x, rect.yMax - 1, rect.width, 1 ), border );
					EditorGUI.DrawRect( new Rect( rect.x, rect.y, 1, rect.height ), border );
					EditorGUI.DrawRect( new Rect( rect.xMax - 1, rect.y, 1, rect.height ), border );
				}
				GUI.DrawTexture( new Rect( rect.x + 5, rect.y + 3, rect.width - 10, rect.height - 6 ), HammerIcons.Brush( IconIndex( mode ) ), ScaleMode.ScaleToFit );
			}
			return clicked;
		}

		static void ApplyPreset( DisplaceBrushPreset preset )
		{
			Preset = preset;
			(InWeight, OutWeight, InSlope, OutSlope) = preset switch
			{
				DisplaceBrushPreset.Linear => (1 / 3f, 1 / 3f, 1f, 1f),
				DisplaceBrushPreset.Sphere => (0.5f, 0.5f, 2.5f, 0f),
				DisplaceBrushPreset.Sharp => (0.5f, 0.5f, 0f, 3f),
				DisplaceBrushPreset.Constant => (0f, 0f, 0f, 0f),
				DisplaceBrushPreset.Custom => (InWeight, OutWeight, InSlope, OutSlope),
				_ => (0.5f, 0.5f, 0f, 0f),
			};
			_curve = null;
		}

		static AnimationCurve _curve;
		static (float, float, float, float, DisplaceBrushPreset) _curveFor;

		/// <summary>
		/// The brush's falloff: 0 at the rim to 1 in the middle, along the distance in from the rim.
		/// </summary>
		public static float Falloff01( float fromRim )
		{
			if ( Preset == DisplaceBrushPreset.Constant ) return fromRim > 0 ? 1 : 0;
			var key = (InWeight, OutWeight, InSlope, OutSlope, Preset);
			if ( _curve == null || _curveFor != key )
			{
				var a = new Keyframe( 0, 0, 0, InSlope, 0, InWeight ) { weightedMode = WeightedMode.Both };
				var b = new Keyframe( 1, 1, OutSlope, 0, OutWeight, 0 ) { weightedMode = WeightedMode.Both };
				_curve = new AnimationCurve( a, b );
				_curveFor = key;
			}
			return Mathf.Clamp01( _curve.Evaluate( Mathf.Clamp01( fromRim ) ) );
		}

		static void CurvePreview( Rect rect )
		{
			if ( Event.current.type != EventType.Repaint ) return;
			EditorGUI.DrawRect( rect, Color.black );
			var fill = new Color( 0.55f, 0.55f, 0.55f );
			var columns = Mathf.Max( 1, (int)rect.width );
			for ( int x = 0; x < columns; x++ )
			{
				var h = Falloff01( x / (float)(columns - 1) ) * rect.height;
				EditorGUI.DrawRect( new Rect( rect.x + x, rect.yMax - h, 1, h ), fill );
				EditorGUI.DrawRect( new Rect( rect.x + x, rect.yMax - h, 1, 1 ), Color.white );
			}
		}

		// ── Sculpting ──

		public override void OnViewGUI( HammerView view )
		{
			var e = Event.current;
			var id = GUIUtility.GetControlID( FocusType.Passive );
			if ( e.type == EventType.Layout )
				HandleUtility.AddDefaultControl( id );

			if ( e.type == EventType.MouseMove || (e.type == EventType.MouseDrag && Mode != DisplaceMode.Grab) )
			{
				_targets ??= Targets();
				_hasBrush = MeshPicking.RaycastFace( HammerGUI.GUIToRay( e.mousePosition ), _targets, out var hit );
				if ( _hasBrush )
				{
					_brushPoint = hit.Point;
					_brushNormal = hit.Normal;
				}
				view.Repaint();
			}

			switch ( e.GetTypeForControl( id ) )
			{
				case EventType.MouseDown when e.button == 0 && !e.alt && HandleUtility.nearestControl == id:
					_targets = Targets();
					GUIUtility.hotControl = id;
					_stroking = true;
					_strokeNormal = BrushDirection( _brushPoint, _brushNormal );
					_strokePlanePoint = _brushPoint;
					// Each stroke is its own undo step
					Undo.FlushUndoRecordObjects();
					Undo.IncrementCurrentGroup();
					Undo.RecordObjects( _targets.ToArray(), "Displace" );
					if ( Mode == DisplaceMode.Grab && !e.shift ) BeginGrab();
					else Dab( e.control || e.command, e.shift );
					e.Use();
					break;

				case EventType.MouseDrag when _stroking && GUIUtility.hotControl == id:
					// Recorded on every step, as with drags (Unity only restores what changed since
					// the last record)
					Undo.RecordObjects( _targets.Where( x => x != null ).ToArray(), "Displace" );
					if ( Mode == DisplaceMode.Grab && _grab.Count > 0 ) DragGrab( e.mousePosition );
					else Dab( e.control || e.command, e.shift );
					view.Repaint();
					e.Use();
					break;

				case EventType.MouseUp when _stroking && GUIUtility.hotControl == id:
					GUIUtility.hotControl = 0;
					_stroking = false;
					_grab.Clear();
					foreach ( var c in _targets.Where( x => x != null ) )
					{
						c.Commit();
						EditorUtility.SetDirty( c );
					}
					MeshHealth.AfterEdit( _targets );
					e.Use();
					break;

				case EventType.ScrollWheel when e.control || e.command:
					Radius = Mathf.Clamp( Radius * (e.delta.y > 0 ? 0.9f : 1.1f), 4, 1024 );
					if ( SaveRadius ) _savedRadius[Mode] = Radius;
					e.Use();
					break;

				case EventType.ScrollWheel when e.shift:
					Strength = Mathf.Clamp( Strength + (e.delta.y > 0 ? -5 : 5), 0, 100 );
					e.Use();
					break;
			}

			if ( e.type == EventType.Repaint )
			{
				Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

				// The meshes the brush is limited to
				if ( SelectedOnly && HighlightSelected )
				{
					Handles.color = new Color( 1.0f, 0.6f, 0.15f, 0.35f );
					foreach ( var c in SelectedMeshes() )
						Handles.DrawLines( MeshEdgeCache.WorldEdges( c ) );
				}

				if ( _hasBrush )
				{
					var radius = Radius * SourceSpace.UnitScale;
					var color = new Color( 0.3f, 0.75f, 1.0f );
					var direction = _stroking ? _strokeNormal : BrushDirection( _brushPoint, _brushNormal );
					Handles.color = color;
					Handles.DrawWireDisc( _brushPoint, _brushNormal, radius, 2.0f );

					// Where the falloff is at half strength
					var half = 1.0f;
					for ( int k = 0; k <= 32; k++ )
						if ( Falloff01( k / 32f ) >= 0.5f ) { half = 1 - k / 32f; break; }
					Handles.color = new Color( color.r, color.g, color.b, 0.4f );
					Handles.DrawWireDisc( _brushPoint, _brushNormal, radius * half, 1.0f );
					Handles.DrawLine( _brushPoint, _brushPoint + direction * radius * (0.3f + Strength / 100 * 0.7f) );
				}
			}
		}

		/// <summary>Which way this dab pushes, from the Normal mode.</summary>
		Vector3 BrushDirection( Vector3 point, Vector3 hitNormal )
		{
			switch ( NormalMode )
			{
				case DisplaceNormalMode.BrushCenter: return hitNormal;
				case DisplaceNormalMode.View:
				{
					var camera = HammerGUI.Camera;
					return camera != null ? -camera.transform.forward : hitNormal;
				}
				case DisplaceNormalMode.X: return Vector3.right;
				case DisplaceNormalMode.Y: return Vector3.up;
				case DisplaceNormalMode.Z: return Vector3.forward;
			}

			// Average: the surface under the whole brush
			var sum = Vector3.zero;
			var worldRadius = Radius * SourceSpace.UnitScale;
			foreach ( var c in _targets ?? new List<HammerMesh>() )
			{
				if ( c == null ) continue;
				var mesh = c.Mesh;
				foreach ( var f in mesh.FaceHandles )
				{
					var center = c.SourceToWorld( mesh.GetFaceCenter( f ) );
					if ( (center - point).sqrMagnitude > worldRadius * worldRadius ) continue;
					mesh.ComputeFaceNormal( f, out var n );
					sum += c.SourceDirectionToWorld( n );
				}
			}
			return sum.sqrMagnitude > 1e-10f ? sum.normalized : hitNormal;
		}

		void Dab( bool reverse, bool smooth )
		{
			if ( !_hasBrush || _targets == null ) return;

			var mode = smooth ? DisplaceMode.Smooth : Mode;
			var strength = (smooth ? SmoothAmount : Strength) / 100;
			var direction = UpdatePlane ? BrushDirection( _brushPoint, _brushNormal ) : _strokeNormal;
			var planePoint = UpdatePlane && mode != DisplaceMode.RaiseTo ? _brushPoint : _strokePlanePoint;
			if ( mode == DisplaceMode.Flatten ) planePoint = FlattenLevel( _brushPoint, direction, planePoint );
			if ( mode == DisplaceMode.Flatten || mode == DisplaceMode.RaiseTo ) planePoint += direction * Offset * SourceSpace.UnitScale;

			var camera = HammerGUI.Camera;
			var changed = new List<HammerMesh>();

			foreach ( var c in _targets )
			{
				if ( c == null ) continue;
				if ( Displace( c, _brushPoint, direction, planePoint, mode, reverse, Radius, strength, Falloff01, ExcludeBackfaces && camera != null ? camera.transform.position : null ) )
					changed.Add( c );
			}

			HammerMeshTool.RebuildNow( changed );
		}

		/// <summary>The level Flatten works to, from the Flatten mode.</summary>
		Vector3 FlattenLevel( Vector3 center, Vector3 direction, Vector3 fallback )
		{
			if ( FlattenMode == DisplaceFlattenMode.Center ) return fallback;

			var worldRadius = Radius * SourceSpace.UnitScale;
			float sum = 0, count = 0, high = float.MinValue, low = float.MaxValue;
			foreach ( var c in _targets )
			{
				if ( c == null ) continue;
				var mesh = c.Mesh;
				foreach ( var v in mesh.VertexHandles )
				{
					var p = c.SourceToWorld( mesh.GetVertexPosition( v ) );
					if ( (p - center).sqrMagnitude > worldRadius * worldRadius ) continue;
					var h = Vector3.Dot( p - center, direction );
					sum += h; count++;
					high = Mathf.Max( high, h );
					low = Mathf.Min( low, h );
				}
			}
			if ( count == 0 ) return fallback;

			var height = FlattenMode switch
			{
				DisplaceFlattenMode.Average => sum / count,
				DisplaceFlattenMode.Highest => high,
				_ => low,
			};
			return center + direction * height;
		}

		void BeginGrab()
		{
			_grab.Clear();
			if ( !_hasBrush ) return;
			var camera = HammerGUI.Camera;
			_grabStart = _brushPoint;
			_grabPlane = new Plane( camera != null ? -camera.transform.forward : _brushNormal, _brushPoint );

			var worldRadius = Radius * SourceSpace.UnitScale;
			foreach ( var c in _targets )
			{
				if ( c == null ) continue;
				var mesh = c.Mesh;
				var list = new List<(VertexHandle, S.Vector3, float)>();
				foreach ( var v in mesh.VertexHandles )
				{
					var p = mesh.GetVertexPosition( v );
					var d = Vector3.Distance( c.SourceToWorld( p ), _grabStart );
					if ( d > worldRadius ) continue;
					list.Add( (v, p, Falloff01( 1 - d / worldRadius ) * Mathf.Max( Strength / 100, 0.01f ) * 2) );
				}
				if ( list.Count > 0 ) _grab[c] = list;
			}
		}

		void DragGrab( Vector2 mouse )
		{
			var ray = HammerGUI.GUIToRay( mouse );
			if ( !_grabPlane.Raycast( ray, out var enter ) ) return;
			var delta = ray.GetPoint( enter ) - _grabStart;

			foreach ( var (c, list) in _grab )
			{
				if ( c == null ) continue;
				var mesh = c.Mesh;
				var localDelta = SourceSpace.ToSourceDirection( c.transform.InverseTransformVector( delta ) );
				foreach ( var (v, start, weight) in list )
					mesh.SetVertexPosition( v, start + localDelta * Mathf.Min( weight, 1 ) );
				mesh.ComputeFaceTextureCoordinatesFromParameters();
			}
			_brushPoint = _grabStart + delta;
			HammerMeshTool.RebuildNow( _grab.Keys.ToList() );
		}

		/// <summary>
		/// One dab of the brush on a mesh, with the old hard-middle falloff. Returns whether anything moved.
		/// </summary>
		/// <param name="radius">In inches.</param>
		/// <param name="strength">0 to 1.</param>
		internal static bool Displace( HammerMesh component, Vector3 center, Vector3 normal, Vector3 planePoint, DisplaceMode mode, bool reverse, float radius, float strength, float hardness ) =>
			Displace( component, center, normal, planePoint, mode, reverse, radius, strength, t => HardFalloff( 1 - t, hardness ), null );

		/// <summary>
		/// One dab of the brush on a mesh. Returns whether anything moved.
		/// </summary>
		/// <param name="center">Brush centre, world space.</param>
		/// <param name="normal">Brush direction, world space.</param>
		/// <param name="planePoint">The level Flatten and Raise To work to.</param>
		/// <param name="radius">In inches.</param>
		/// <param name="strength">0 to 1.</param>
		/// <param name="falloff">Weight from the distance in from the rim (0 at the rim, 1 in the middle).</param>
		/// <param name="excludeFacingAwayFrom">When set, vertices facing away from this point are left alone.</param>
		internal static bool Displace( HammerMesh component, Vector3 center, Vector3 normal, Vector3 planePoint, DisplaceMode mode, bool reverse, float radius, float strength, System.Func<float, float> falloff, Vector3? excludeFacingAwayFrom )
		{
			var worldRadius = radius * SourceSpace.UnitScale;
			var renderer = component.GetComponent<MeshRenderer>();
			if ( renderer != null && renderer.bounds.SqrDistance( center ) > worldRadius * worldRadius )
				return false;

			var mesh = component.Mesh;
			var localCenter = component.WorldToSource( center );
			var localNormal = SourceSpace.ToSourceDirection( component.transform.InverseTransformDirection( normal ) ).Normal;
			var localPlane = component.WorldToSource( planePoint );
			var localEye = excludeFacingAwayFrom.HasValue ? component.WorldToSource( excludeFacingAwayFrom.Value ) : default;

			// Work out every move first, then apply, so smoothing reads the old positions
			var moves = new List<(VertexHandle, S.Vector3)>();
			var step = radius * 0.05f * strength * (reverse ? -1 : 1);

			foreach ( var v in mesh.VertexHandles )
			{
				var p = mesh.GetVertexPosition( v );
				var world = component.SourceToWorld( p );
				var distance = Vector3.Distance( world, center );
				if ( distance > worldRadius ) continue;
				if ( excludeFacingAwayFrom.HasValue && S.Vector3.Dot( VertexNormal( mesh, v ), localEye - p ) < 0 ) continue;

				var weight = falloff( 1 - distance / worldRadius );
				if ( weight <= 0 ) continue;
				S.Vector3 target;

				switch ( mode )
				{
					case DisplaceMode.PushPull:
						target = p + localNormal * step * weight;
						break;

					case DisplaceMode.Inflate:
						target = p + VertexNormal( mesh, v ) * step * weight;
						break;

					case DisplaceMode.Flatten:
					case DisplaceMode.RaiseTo:
					{
						var height = S.Vector3.Dot( p - localPlane, localNormal );
						// Raise To only lifts up to the level (Ctrl: only lowers down to it)
						if ( mode == DisplaceMode.RaiseTo && (reverse ? height < 0 : height > 0) ) continue;
						target = p - localNormal * height * Mathf.Clamp01( strength * weight * 0.5f );
						break;
					}

					case DisplaceMode.Smooth:
					{
						if ( !mesh.GetEdgesConnectedToVertex( v, out var edges ) || edges.Count == 0 ) continue;
						var sum = S.Vector3.Zero;
						foreach ( var e in edges )
						{
							mesh.GetEdgeVertices( e, out var a, out var b );
							sum += mesh.GetVertexPosition( a == v ? b : a );
						}
						var average = sum / edges.Count;
						target = p + (average - p) * Mathf.Clamp01( strength * weight * 0.6f );
						break;
					}

					case DisplaceMode.Noise:
					{
						// The same bumps every dab at a spot, so strokes build them up rather than fizz
						var h = Mathf.Sin( p.x * 12.9898f + p.y * 78.233f + p.z * 37.719f ) * 43758.5453f;
						var random = (h - Mathf.Floor( h )) * 2 - 1;
						target = p + localNormal * step * weight * random;
						break;
					}

					default: // Pinch: draw in towards the brush centre, across the surface
					{
						var toward = localCenter - p;
						toward -= localNormal * S.Vector3.Dot( toward, localNormal );
						target = p + toward * Mathf.Clamp01( strength * weight * 0.15f ) * (reverse ? -1 : 1);
						break;
					}
				}

				moves.Add( (v, target) );
			}

			foreach ( var (v, target) in moves )
				mesh.SetVertexPosition( v, target );

			if ( moves.Count > 0 )
				mesh.ComputeFaceTextureCoordinatesFromParameters();

			return moves.Count > 0;
		}

		static S.Vector3 VertexNormal( S.PolygonMesh mesh, VertexHandle v )
		{
			var sum = S.Vector3.Zero;
			if ( mesh.GetFacesConnectedToVertex( v, out var faces ) )
			{
				foreach ( var f in faces )
				{
					mesh.ComputeFaceNormal( f, out var n );
					sum += n;
				}
			}
			return sum.Length > 1e-6f ? sum.Normal : new S.Vector3( 0, 0, 1 );
		}

		static float HardFalloff( float t, float hardness )
		{
			// Full strength inside the hardness radius, smooth falloff outside it
			if ( t <= hardness ) return 1;
			var x = Mathf.InverseLerp( 1, hardness, t );
			return x * x * (3 - 2 * x);
		}

		// Strokes are already applied, each undoable on its own
		public override void Apply() => Close();

		public override void Cancel()
		{
			// Throw away every stroke made since the tool opened
			Undo.RevertAllDownToGroup( _undoGroup );
			Close();
		}
	}
}
