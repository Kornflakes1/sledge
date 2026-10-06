using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	public enum PaintMode
	{
		/// <summary>Vertex colour tint.</summary>
		Color,

		/// <summary>Material blend weights (written to UV3 for blend shaders).</summary>
		Blend,
	}

	/// <summary>
	/// Vertex paint mode (5): brush colour or blend weights onto face corners, like the s&amp;box
	/// vertex paint tool. Shift erases, Ctrl+drag resizes the brush, Backspace floods the selection.
	/// </summary>
	partial class HammerMeshTool
	{
		public static PaintMode PaintMode = PaintMode.Color;
		public static Color PaintColor = Color.red;
		public static Vector4 PaintBlend = new( 1, 0, 0, 0 );
		public static float PaintRadius = 32;
		public static float PaintStrength = 1;
		public static float PaintHardness = 0.5f;

		bool _painting;
		bool _hasBrush;
		Vector3 _brushPoint;
		Vector3 _brushNormal;
		HammerMesh[] _paintComponents;

		void PaintGUI( HammerView view )
		{
			var e = Event.current;
			var id = GUIUtility.GetControlID( FocusType.Passive );

			if ( e.type == EventType.Layout )
				HandleUtility.AddDefaultControl( id );

			if ( e.type == EventType.MouseMove || e.type == EventType.MouseDrag )
			{
				_hasBrush = MeshPicking.PickFace( e.mousePosition, MeshPicking.VisibleMeshes(), out var hit );
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
					GUIUtility.hotControl = id;
					_painting = true;
					_paintComponents = EditMeshes().Concat( MeshPicking.VisibleMeshes() ).Distinct().ToArray();
					Undo.FlushUndoRecordObjects();
					Undo.IncrementCurrentGroup();
					Undo.RecordObjects( _paintComponents, "Vertex Paint" );
					PaintAt( e.shift );
					e.Use();
					break;

				case EventType.MouseDrag when _painting && GUIUtility.hotControl == id:
					// Record again on every step: Unity keeps the state from the first record of
					// the stroke, but only restores the values that changed since the last one
					Undo.RecordObjects( _paintComponents.Where( x => x != null ).ToArray(), "Vertex Paint" );
					PaintAt( e.shift );
					e.Use();
					break;

				case EventType.MouseUp when _painting && GUIUtility.hotControl == id:
					GUIUtility.hotControl = 0;
					_painting = false;
					foreach ( var c in _paintComponents.Where( x => x != null ) )
					{
						c.Commit();
						EditorUtility.SetDirty( c );
					}
					_paintComponents = null;
					e.Use();
					break;

				case EventType.ScrollWheel when e.control || e.command:
					PaintRadius = Mathf.Clamp( PaintRadius * (e.delta.y > 0 ? 0.9f : 1.1f), 1, 1024 );
					e.Use();
					break;

				case EventType.ScrollWheel when e.shift:
					PaintStrength = Mathf.Clamp01( PaintStrength + (e.delta.y > 0 ? -0.05f : 0.05f) );
					e.Use();
					break;
			}

			if ( e.type == EventType.Repaint && _hasBrush )
			{
				var radius = PaintRadius * SourceSpace.UnitScale;
				var color = PaintMode == PaintMode.Color ? PaintColor : new Color( PaintBlend.x, PaintBlend.y, PaintBlend.z, 1 );
				Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
				Handles.color = color;
				Handles.DrawWireDisc( _brushPoint, _brushNormal, radius, 2.0f );
				Handles.color = new Color( color.r, color.g, color.b, 0.4f );
				Handles.DrawWireDisc( _brushPoint, _brushNormal, radius * PaintHardness, 1.0f );
				Handles.DrawLine( _brushPoint, _brushPoint + _brushNormal * radius * (0.5f + PaintStrength) );
			}
		}

		void PaintAt( bool erase )
		{
			if ( !_hasBrush || _paintComponents == null )
				return;

			var radius = PaintRadius * SourceSpace.UnitScale;

			foreach ( var c in _paintComponents )
			{
				if ( c == null ) continue;

				var renderer = c.GetComponent<MeshRenderer>();
				if ( renderer != null && renderer.bounds.SqrDistance( _brushPoint ) > radius * radius )
					continue;

				var changed = false;
				var mesh = c.Mesh;

				foreach ( var face in mesh.FaceHandles )
				{
					if ( !mesh.GetFaceVerticesConnectedToFace( face, out var corners ) )
						continue;

					foreach ( var corner in corners )
					{
						var vertex = mesh.GetVertexConnectedToFaceVertex( corner );
						var world = c.SourceToWorld( mesh.GetVertexPosition( vertex ) );
						var distance = Vector3.Distance( world, _brushPoint );
						if ( distance > radius ) continue;

						var falloff = Falloff( distance / radius );
						var amount = PaintStrength * falloff;

						if ( PaintMode == PaintMode.Color )
						{
							var current = ToUnity( mesh.GetVertexColor( corner ) );
							var target = erase ? Color.white : PaintColor;
							var next = Color.Lerp( current, target, amount );
							mesh.SetVertexColor( corner, ToSource( next ) );
						}
						else
						{
							var current = mesh.GetVertexBlend( corner );
							var cv = new Vector4( current.r, current.g, current.b, current.a ) / 255.0f;
							var target = erase ? Vector4.zero : PaintBlend;
							var next = Vector4.Lerp( cv, target, amount ) * 255.0f;
							mesh.SetVertexBlend( corner, new S.Color32( (byte)next.x, (byte)next.y, (byte)next.z, (byte)next.w ) );
						}

						changed = true;
					}
				}

				if ( changed )
				{
					mesh.UpdateVertexData();
					c.RebuildRenderMesh();
					c.MarkModified();
				}
			}
		}

		float Falloff( float t )
		{
			// Full strength inside the hardness radius, smooth falloff outside it
			if ( t <= PaintHardness ) return 1;
			var x = Mathf.InverseLerp( 1, PaintHardness, t );
			return x * x * (3 - 2 * x);
		}

		static Color ToUnity( S.Color32 c ) => new Color32( c.r, c.g, c.b, c.a );
		static S.Color32 ToSource( Color c )
		{
			Color32 c32 = c;
			return new S.Color32( c32.r, c32.g, c32.b, c32.a );
		}

		/// <summary>
		/// Backspace in paint mode: fill every corner of the selected meshes (Shift: clear them).
		/// </summary>
		public void FloodPaint( bool erase )
		{
			var components = EditMeshes();
			if ( components.Count == 0 ) return;

			using ( Scope( "Vertex Paint Fill", components ) )
			{
				foreach ( var c in components )
				{
					var mesh = c.Mesh;
					foreach ( var corner in mesh.HalfEdgeHandles )
					{
						if ( !mesh.GetHalfEdgeFace( corner ).IsValid ) continue;

						if ( PaintMode == PaintMode.Color )
							mesh.SetVertexColor( corner, ToSource( erase ? Color.white : PaintColor ) );
						else
						{
							var b = erase ? Vector4.zero : PaintBlend * 255.0f;
							mesh.SetVertexBlend( corner, new S.Color32( (byte)b.x, (byte)b.y, (byte)b.z, (byte)b.w ) );
						}
					}

					mesh.UpdateVertexData();
				}
			}

			HammerViews.RepaintAll();
		}
	}
}
