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
		public static float Strength = 0.5f;
		public static float Hardness = 0.3f;

		/// <summary>Only the selected objects (otherwise everything under the brush).</summary>
		public static bool SelectedOnly = true;

		List<HammerMesh> _targets;
		int _undoGroup;
		bool _stroking;
		bool _hasBrush;
		Vector3 _brushPoint;
		Vector3 _brushNormal;
		Vector3 _strokeNormal;
		Vector3 _strokePlanePoint;

		public override string Title => "Displacement";
		public override string Help => "Drag on a surface to sculpt. Ctrl reverses, Shift smooths. Ctrl+scroll: size, Shift+scroll: strength. Subdivide first for detail. Enter keeps, Esc undoes.";

		public static void Open( HammerMeshTool tool )
		{
			var d = new DisplacementTool();
			Undo.IncrementCurrentGroup();
			d._undoGroup = Undo.GetCurrentGroup();
			tool.BeginSubTool( d );
		}

		List<HammerMesh> Targets()
		{
			var selected = Tool.Mode == EditMode.Object
				? UnityEditor.Selection.gameObjects.SelectMany( g => g.GetComponentsInChildren<HammerMesh>() ).ToList()
				: Tool.Selection.Components.ToList();

			return SelectedOnly && selected.Count > 0 ? selected.Distinct().ToList() : MeshPicking.VisibleMeshes();
		}

		public override void OnOverlayGUI()
		{
			Mode = (DisplaceMode)GUILayout.Toolbar( (int)Mode, new[] { "Push", "Inflate", "Flatten", "Smooth", "Pinch" }, EditorStyles.miniButton );
			Radius = EditorGUILayout.Slider( "Radius", Radius, 4, 1024 );
			Strength = EditorGUILayout.Slider( "Strength", Strength, 0.01f, 1 );
			Hardness = EditorGUILayout.Slider( "Hardness", Hardness, 0, 1 );
			SelectedOnly = EditorGUILayout.ToggleLeft( "Selected objects only", SelectedOnly );
		}

		public override void OnViewGUI( HammerView view )
		{
			var e = Event.current;
			var id = GUIUtility.GetControlID( FocusType.Passive );
			if ( e.type == EventType.Layout )
				HandleUtility.AddDefaultControl( id );

			if ( e.type == EventType.MouseMove || e.type == EventType.MouseDrag )
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
					_strokeNormal = _brushNormal;
					_strokePlanePoint = _brushPoint;
					// Each stroke is its own undo step
					Undo.FlushUndoRecordObjects();
					Undo.IncrementCurrentGroup();
					Undo.RecordObjects( _targets.ToArray(), "Displace" );
					Dab( e.control || e.command, e.shift );
					e.Use();
					break;

				case EventType.MouseDrag when _stroking && GUIUtility.hotControl == id:
					// Recorded on every step, as with drags (Unity only restores what changed since
					// the last record)
					Undo.RecordObjects( _targets.Where( x => x != null ).ToArray(), "Displace" );
					Dab( e.control || e.command, e.shift );
					e.Use();
					break;

				case EventType.MouseUp when _stroking && GUIUtility.hotControl == id:
					GUIUtility.hotControl = 0;
					_stroking = false;
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
					e.Use();
					break;

				case EventType.ScrollWheel when e.shift:
					Strength = Mathf.Clamp( Strength + (e.delta.y > 0 ? -0.05f : 0.05f), 0.01f, 1 );
					e.Use();
					break;
			}

			if ( e.type == EventType.Repaint && _hasBrush )
			{
				var radius = Radius * SourceSpace.UnitScale;
				var color = new Color( 0.3f, 0.75f, 1.0f );
				Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
				Handles.color = color;
				Handles.DrawWireDisc( _brushPoint, _brushNormal, radius, 2.0f );
				Handles.color = new Color( color.r, color.g, color.b, 0.4f );
				Handles.DrawWireDisc( _brushPoint, _brushNormal, radius * Hardness, 1.0f );
				Handles.DrawLine( _brushPoint, _brushPoint + _brushNormal * radius * (0.3f + Strength * 0.7f) );
			}
		}

		void Dab( bool reverse, bool smooth )
		{
			if ( !_hasBrush || _targets == null ) return;

			var mode = smooth ? DisplaceMode.Smooth : Mode;
			var changed = new List<HammerMesh>();

			foreach ( var c in _targets )
			{
				if ( c == null ) continue;
				if ( Displace( c, _brushPoint, mode == DisplaceMode.PushPull ? _strokeNormal : _brushNormal, _strokePlanePoint, mode, reverse, Radius, Strength, Hardness ) )
					changed.Add( c );
			}

			HammerMeshTool.RebuildNow( changed );
		}

		/// <summary>
		/// One dab of the brush on a mesh. Returns whether anything moved.
		/// </summary>
		/// <param name="center">Brush centre, world space.</param>
		/// <param name="normal">Brush direction, world space.</param>
		/// <param name="planePoint">Where the stroke started (Flatten levels to here).</param>
		/// <param name="radius">In inches.</param>
		internal static bool Displace( HammerMesh component, Vector3 center, Vector3 normal, Vector3 planePoint, DisplaceMode mode, bool reverse, float radius, float strength, float hardness )
		{
			var worldRadius = radius * SourceSpace.UnitScale;
			var renderer = component.GetComponent<MeshRenderer>();
			if ( renderer != null && renderer.bounds.SqrDistance( center ) > worldRadius * worldRadius )
				return false;

			var mesh = component.Mesh;
			var localCenter = component.WorldToSource( center );
			var localNormal = SourceSpace.ToSourceDirection( component.transform.InverseTransformDirection( normal ) ).Normal;
			var localPlane = component.WorldToSource( planePoint );

			// Work out every move first, then apply, so smoothing reads the old positions
			var moves = new List<(VertexHandle, S.Vector3)>();
			var step = radius * 0.05f * strength * (reverse ? -1 : 1);

			foreach ( var v in mesh.VertexHandles )
			{
				var p = mesh.GetVertexPosition( v );
				var world = component.SourceToWorld( p );
				var distance = Vector3.Distance( world, center );
				if ( distance > worldRadius ) continue;

				var weight = Falloff( distance / worldRadius, hardness );
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
					{
						var height = S.Vector3.Dot( p - localPlane, localNormal );
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

		static float Falloff( float t, float hardness )
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
