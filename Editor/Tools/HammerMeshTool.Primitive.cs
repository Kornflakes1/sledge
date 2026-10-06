using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Sandbox.Primitives;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	partial class HammerMeshTool
	{
		enum PrimitiveStage
		{
			Idle,
			DraggingBase,

			/// <summary>
			/// Drawn and waiting: resize it with the handles, Enter/Space keeps it, Esc drops it.
			/// </summary>
			Editing,
		}

		public static readonly Dictionary<string, Type> PrimitiveTypes = new()
		{
			{ "Box", typeof( BlockPrimitive ) },
			{ "Cylinder", typeof( CylinderPrimitive ) },
			{ "Sphere", typeof( SpherePrimitive ) },
			{ "Stairs", typeof( StairsPrimitive ) },
			{ "Spike", typeof( SpikePrimitive ) },
			{ "Doorway", typeof( DoorwayPrimitive ) },
			{ "Quad", typeof( QuadPrimitive ) },
		};

		static readonly Dictionary<string, PrimitiveBuilder> _builders = new();

		PrimitiveStage _primitiveStage;
		// The shape being drawn is kept in the workplane's frame (just the world without one), so it
		// can be drawn square to a tilted surface
		Plane _primitivePlane;
		Vector3 _primitiveNormal;
		Vector3 _primitiveStart;
		Vector3 _primitiveEnd;
		float _primitiveHeight;
		Bounds _editBounds;
		HammerMesh _justCreated;

		/// <summary>
		/// The builder for the chosen primitive type, kept so its settings persist.
		/// </summary>
		public static PrimitiveBuilder CurrentBuilder
		{
			get
			{
				var name = HammerSettings.PrimitiveType;
				if ( !PrimitiveTypes.TryGetValue( name, out var type ) )
				{
					name = "Box";
					type = typeof( BlockPrimitive );
				}

				if ( !_builders.TryGetValue( name, out var builder ) )
				{
					builder = (PrimitiveBuilder)Activator.CreateInstance( type );
					_builders[name] = builder;
				}

				return builder;
			}
		}

		/// <summary>
		/// Escape: cancel the primitive being drawn, otherwise clear the selection.
		/// </summary>
		public void Cancel()
		{
			if ( _subTool != null )
			{
				_subTool.Cancel();
				return;
			}

			if ( _mode == EditMode.Primitive && _primitiveStage != PrimitiveStage.Idle )
				CancelPrimitive();
			else if ( Selection.Count > 0 )
				Selection.Clear();
			else if ( this == Active )
				Deactivate(); // Esc with nothing to cancel leaves the scene view tool
		}

		/// <summary>
		/// Enter: finish the primitive being drawn.
		/// </summary>
		public void Confirm()
		{
			if ( _subTool != null )
			{
				_subTool.Apply();
				return;
			}

			ConfirmPrimitive();
		}

		/// <summary>
		/// Enter or Space: keep the shape being drawn.
		/// </summary>
		void ConfirmPrimitive()
		{
			if ( _mode == EditMode.Primitive && _primitiveStage == PrimitiveStage.Editing )
				CreatePrimitive();
		}

		/// <summary>
		/// Ctrl+Z with something half done: cancel it (an open tool, a shape being drawn, a drag)
		/// rather than undoing what came before. Returns true if there was something.
		/// </summary>
		public bool CancelPending()
		{
			if ( _subTool != null )
			{
				_subTool.Cancel();
				return true;
			}

			if ( _mode == EditMode.Primitive && _primitiveStage != PrimitiveStage.Idle )
			{
				CancelPrimitive();
				return true;
			}

			if ( _typed != null )
			{
				_typed = null;
				return true;
			}

			return false;
		}

		void CancelPrimitive()
		{
			_primitiveStage = PrimitiveStage.Idle;
			HammerViews.RepaintAll();
		}

		void PrimitiveGUI( HammerView view )
		{
			var e = Event.current;
			var id = GUIUtility.GetControlID( FocusType.Passive );

			if ( e.type == EventType.Layout )
				HandleUtility.AddDefaultControl( id );

			// The drawn box's resize handles get the mouse first
			if ( _primitiveStage == PrimitiveStage.Editing )
				EditHandlesGUI();

			switch ( e.GetTypeForControl( id ) )
			{
				case EventType.MouseDown when e.button == 0 && !e.alt && HandleUtility.nearestControl == id:
				{
					// Clicking away from a drawn box keeps it and starts the next
					if ( _primitiveStage == PrimitiveStage.Editing )
						CreatePrimitive();

					if ( !TryGetPlacementPoint( e.mousePosition, e.shift, out var point, out var normal ) )
						break;
					HammerTrace.Log( $"Primitive down point={point} normal={normal}" );

					point = Workplane.ToLocal( point );
					normal = NearestAxis( Workplane.DirectionToLocal( normal ) );
					_primitiveNormal = normal;
					_primitivePlane = new Plane( normal, point );
					_primitiveStart = Snap( point );
					_primitiveEnd = _primitiveStart;
					_primitiveStage = PrimitiveStage.DraggingBase;
					GUIUtility.hotControl = id;
					e.Use();
					break;
				}

				case EventType.MouseDrag when _primitiveStage == PrimitiveStage.DraggingBase && GUIUtility.hotControl == id:
				{
					var world = HammerGUI.GUIToRay( e.mousePosition );
					var ray = new Ray( Workplane.ToLocal( world.origin ), Workplane.DirectionToLocal( world.direction ) );
					if ( _primitivePlane.Raycast( ray, out var enter ) )
						_primitiveEnd = Snap( ray.GetPoint( enter ) );
					e.Use();
					break;
				}

				case EventType.MouseUp when _primitiveStage == PrimitiveStage.DraggingBase && GUIUtility.hotControl == id:
				{
					GUIUtility.hotControl = 0;
					e.Use();

					var size = Vector3.ProjectOnPlane( _primitiveEnd - _primitiveStart, _primitiveNormal );
					HammerTrace.Log( $"Primitive up start={_primitiveStart} end={_primitiveEnd} normal={_primitiveNormal} size={size}" );
					var minSize = HammerSettings.GridSize * SourceSpace.UnitScale * 0.5f;

					if ( Mathf.Abs( size.x ) < minSize && Mathf.Abs( size.y ) < minSize && Mathf.Abs( size.z ) < minSize )
					{
						CancelPrimitive();
						break;
					}

					// Hammer: the base grows straight into a box (as tall as the last one), which
					// stays up with resize handles until it's confirmed
					_primitiveHeight = CurrentBuilder.Is2D ? 0 : _primitiveDepth;
					_editBounds = new Bounds( _primitiveStart, Vector3.zero );
					_editBounds.Encapsulate( _primitiveEnd );
					_editBounds.Encapsulate( _primitiveEnd + _primitiveNormal * _primitiveHeight );
					_primitiveStage = PrimitiveStage.Editing;
					break;
				}

				case EventType.KeyDown when e.keyCode == KeyCode.Escape && _primitiveStage != PrimitiveStage.Idle:
					CancelPrimitive();
					e.Use();
					break;

				case EventType.KeyDown when (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Space) && _primitiveStage == PrimitiveStage.Editing:
					CreatePrimitive();
					e.Use();
					break;
			}

			if ( e.type == EventType.Repaint )
				DrawPrimitivePreview();

			if ( _primitiveStage != PrimitiveStage.Idle )
				view.Repaint();
		}

		/// <summary>
		/// A ball on each side of the drawn box: drag to move that side (snapped to the grid).
		/// </summary>
		void EditHandlesGUI()
		{
			Color[] colors = { new( 0.25f, 0.85f, 0.12f ), new( 0.15f, 0.45f, 1.0f ), new( 0.92f, 0.12f, 0.1f ) };

			for ( int axis = 0; axis < 3; axis++ )
			{
				// A flat shape has no thickness to change
				if ( CurrentBuilder.Is2D && Mathf.Abs( _primitiveNormal[axis] ) > 0.5f ) continue;

				for ( int sign = -1; sign <= 1; sign += 2 )
				{
					var dir = Vector3.zero;
					dir[axis] = sign;
					var position = Workplane.ToWorld( _editBounds.center + dir * _editBounds.extents[axis] );
					var size = HammerGUI.HandleSize( position );

					EditorGUI.BeginChangeCheck();
					Handles.color = colors[axis];
					var moved = HammerGizmos.Slider( position, Workplane.DirectionToWorld( dir ), size * 0.16f, BallCap );
					if ( !EditorGUI.EndChangeCheck() ) continue;

					var side = Workplane.ToLocal( moved )[axis];
					if ( HammerSettings.GridSnap ^ (Event.current.control || Event.current.command) )
						side = Mathf.Round( side / (HammerSettings.GridSize * SourceSpace.UnitScale) ) * HammerSettings.GridSize * SourceSpace.UnitScale;

					var min = _editBounds.min;
					var max = _editBounds.max;
					if ( sign > 0 ) max[axis] = Mathf.Max( side, min[axis] + 1e-3f );
					else min[axis] = Mathf.Min( side, max[axis] - 1e-3f );
					_editBounds.SetMinMax( min, max );

					if ( Mathf.Abs( _primitiveNormal[axis] ) > 0.5f )
						_primitiveHeight = _editBounds.size[axis];
				}
			}
		}

		/// <summary>
		/// Snap a point in the workplane's frame to its grid.
		/// </summary>
		Vector3 Snap( Vector3 local ) => HammerSettings.GridSnap ? Workplane.ToLocal( HammerSettings.SnapWorld( Workplane.ToWorld( local ) ) ) : local;

		/// <summary>
		/// Depth of new shapes drawn in a 2D view; follows the height of the last 3D-drawn shape.
		/// </summary>
		static float _primitiveDepth = 64 * SourceSpace.UnitScale;

		internal bool TryGetPlacementPoint( Vector2 mouse, out Vector3 point, out Vector3 normal ) => TryGetPlacementPoint( mouse, true, out point, out normal );

		/// <param name="onSurface">Build on the surface under the cursor (Hammer: hold Shift);
		/// otherwise on the grid.</param>
		internal bool TryGetPlacementPoint( Vector2 mouse, bool onSurface, out Vector3 point, out Vector3 normal )
		{
			var ray = HammerGUI.GUIToRay( mouse );

			if ( _view != null && _view.Orthographic )
			{
				// 2D view: draw on the plane through the origin facing the camera
				normal = NearestAxis( -_view.Camera.transform.forward );
				if ( new Plane( normal, Vector3.zero ).Raycast( ray, out var d ) || new Plane( -normal, Vector3.zero ).Raycast( ray, out d ) )
				{
					point = ray.GetPoint( d );
					return true;
				}

				point = ray.origin;
				return true;
			}

			if ( onSurface && MeshPicking.RaycastFace( ray, MeshPicking.VisibleMeshes(), out var hit ) )
			{
				// Build on the surface, squared up to the nearest world axis so boxes stay axis aligned
				normal = NearestAxis( hit.Normal );
				point = hit.Point;
				return true;
			}

			if ( onSurface && Physics.Raycast( ray, out var physicsHit ) )
			{
				normal = NearestAxis( physicsHit.normal );
				point = physicsHit.point;
				return true;
			}

			// The grid: the workplane, or the ground
			normal = Workplane.Up;
			var ground = new Plane( Workplane.Up, Workplane.Origin );
			if ( ground.Raycast( ray, out var enter ) )
			{
				point = ray.GetPoint( enter );
				return true;
			}

			point = default;
			return false;
		}

		static Vector3 NearestAxis( Vector3 n )
		{
			var a = new Vector3( Mathf.Abs( n.x ), Mathf.Abs( n.y ), Mathf.Abs( n.z ) );
			if ( a.x >= a.y && a.x >= a.z ) return new Vector3( Mathf.Sign( n.x ), 0, 0 );
			if ( a.y >= a.z ) return new Vector3( 0, Mathf.Sign( n.y ), 0 );
			return new Vector3( 0, 0, Mathf.Sign( n.z ) );
		}

		/// <summary>
		/// The box being drawn, as world space min/max.
		/// </summary>
		Bounds PrimitiveBounds()
		{
			if ( _primitiveStage == PrimitiveStage.Editing )
				return _editBounds;

			var b = new Bounds( _primitiveStart, Vector3.zero );
			b.Encapsulate( _primitiveEnd );
			return b;
		}

		void DrawPrimitivePreview()
		{
			if ( _primitiveStage == PrimitiveStage.Idle )
				return;

			var bounds = PrimitiveBounds();
			var min = bounds.min;
			var max = bounds.max;
			Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

			// Hammer's look: a dashed yellow box with each side's length on it
			var corners = new Vector3[8];
			for ( int i = 0; i < 8; i++ )
				corners[i] = new Vector3( (i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z );
			int[] lines = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
			for ( int i = 0; i < 8; i++ ) corners[i] = Workplane.ToWorld( corners[i] );
			Handles.color = new Color( 1.0f, 0.92f, 0.15f, 1.0f );
			Handles.DrawDottedLines( corners, lines, 4.0f );

			// w / l / h are Hammer's X / Y / Z: Unity's Z / X / Y
			var size = bounds.size * SourceSpace.UnitsPerMetre;
			if ( size.z > 0.01f ) HammerGUI.OutlinedLabel( Workplane.ToWorld( new Vector3( min.x, min.y, bounds.center.z ) ), $"w:{size.z:0.##}", Color.white );
			if ( size.x > 0.01f ) HammerGUI.OutlinedLabel( Workplane.ToWorld( new Vector3( bounds.center.x, min.y, max.z ) ), $"l:{size.x:0.##}", Color.white );
			if ( size.y > 0.01f ) HammerGUI.OutlinedLabel( Workplane.ToWorld( new Vector3( min.x, bounds.center.y, max.z ) ), $"h:{size.y:0.##}", Color.white );
		}


		void CreatePrimitive()
		{
			var bounds = PrimitiveBounds();
			_primitiveStage = PrimitiveStage.Idle;

			if ( Mathf.Abs( _primitiveHeight ) > 1e-4f )
				_primitiveDepth = Mathf.Abs( _primitiveHeight );

			var builder = CurrentBuilder;
			var minSource = SourceSpace.ToSourcePosition( bounds.min );
			var maxSource = SourceSpace.ToSourcePosition( bounds.max );
			var box = S.BBox.FromPoints( new[] { minSource, maxSource } );

			builder.Material = HammerMaterials.Get( HammerSettings.ActiveMaterial ) ?? S.Material.Load( HammerMaterials.DefaultKey );

			var mesh = builder.CreateMesh( box );
			var center = mesh.CalculateBounds().Center;
			mesh.ApplyTransform( new S.Transform( -center ) );

			var name = HammerSettings.PrimitiveType;
			var go = new GameObject( name );
			Undo.RegisterCreatedObjectUndo( go, $"Create {name}" );
			// Built square to the workplane
			go.transform.SetPositionAndRotation( Workplane.ToWorld( SourceSpace.ToUnityPosition( center ) ), Workplane.Rotation );
			go.isStatic = true;

			var component = go.AddComponent<HammerMesh>();
			component.SmoothingAngle = 40.0f;
			component.Mesh = mesh;

			// Like Hammer: straight into the Select tool with the new mesh selected (and switching
			// to Vertices, Edges or Faces next selects all of its)
			_justCreated = component;
			UnityEditor.Selection.activeGameObject = go;
			Selection.Clear();
			Mode = EditMode.Object;
			MoveMode = MoveMode.Select;
			HammerViews.RepaintAll();
		}
	}
}
