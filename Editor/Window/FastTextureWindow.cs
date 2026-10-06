using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// s&amp;box's Fast Texture Tool (Ctrl+G in face mode): the selected faces' UVs drawn over their
	/// texture. Drag to move the texture, use the buttons to fit, flip, rotate and scale it.
	/// </summary>
	public sealed class FastTextureWindow : EditorWindow
	{
		const float ToolbarHeight = 30;

		HammerMeshTool _tool;
		float _zoom = 220; // pixels per texture tile
		Vector2 _pan = new( 0.5f, 0.5f ); // UV at the centre of the canvas
		bool _dragging;
		bool _panning;
		Vector2 _dragStart;
		Vector2 _applied;
		int _undoGroup;

		public static void Open( HammerMeshTool tool )
		{
			var window = GetWindow<FastTextureWindow>( true, "Fast Texture Tool" );
			window._tool = tool;
			window.minSize = new Vector2( 420, 380 );
			window.FrameFaces();
			window.Show();
		}

		void OnEnable()
		{
			HammerMesh.AnyRebuilt += OnRebuilt;
			Undo.undoRedoPerformed += Repaint;
			wantsMouseMove = true;
		}

		void OnDisable()
		{
			HammerMesh.AnyRebuilt -= OnRebuilt;
			Undo.undoRedoPerformed -= Repaint;
		}

		void OnSelectionChange() => Repaint();

		void OnRebuilt( HammerMesh mesh ) => Repaint();

		List<MeshFace> Faces => _tool == null ? new List<MeshFace>() : _tool.SelectedFaces.ToList();

		Texture FaceTexture( MeshFace face )
		{
			var m = face.Material?.Asset;
			if ( m == null ) m = HammerMaterials.Default;
			return m != null && m.HasProperty( "_MainTex" ) ? m.mainTexture : null;
		}

		void OnGUI()
		{
			if ( _tool == null ) _tool = HammerWindow.FocusedTool ?? HammerMeshTool.Active;

			var e = Event.current;
			var bar = new Rect( 0, 0, position.width, ToolbarHeight );
			var canvas = new Rect( 0, ToolbarHeight, position.width, position.height - ToolbarHeight - 22 );

			EditorGUI.DrawRect( new Rect( 0, 0, position.width, position.height ), HammerIcons.Background );
			DrawToolbar( bar );

			var faces = Faces;
			GUI.BeginClip( canvas );
			var local = new Rect( 0, 0, canvas.width, canvas.height );
			DrawCanvas( local, faces );
			CanvasInput( local, faces );
			GUI.EndClip();

			var status = faces.Count == 0 ? "Select faces in the Hammer window (face mode)." : $"{faces.Count} face{(faces.Count == 1 ? "" : "s")}  ·  drag to move the texture, middle mouse to pan, wheel to zoom";
			GUI.Label( new Rect( 8, position.height - 21, position.width - 16, 20 ), status, EditorStyles.miniLabel );
		}

		void DrawToolbar( Rect rect )
		{
			EditorGUI.DrawRect( rect, HammerIcons.Bar );
			GUILayout.BeginArea( new Rect( rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 6 ) );
			using ( new GUILayout.HorizontalScope() )
			{
				using ( new EditorGUI.DisabledScope( _tool == null || !Faces.Any() ) )
				{
					if ( GUILayout.Button( "Fit", EditorStyles.miniButtonLeft ) ) _tool.JustifyTexture( Sandbox.PolygonMesh.TextureJustification.Fit );
					if ( GUILayout.Button( "Grid", EditorStyles.miniButtonMid ) ) _tool.TextureAlignToGrid();
					if ( GUILayout.Button( "Face", EditorStyles.miniButtonRight ) ) _tool.TextureAlignToFace();
					GUILayout.Space( 6 );
					if ( GUILayout.Button( "Flip U", EditorStyles.miniButtonLeft ) ) _tool.FlipTexture( true );
					if ( GUILayout.Button( "Flip V", EditorStyles.miniButtonRight ) ) _tool.FlipTexture( false );
					GUILayout.Space( 6 );
					if ( GUILayout.Button( "⟲ 90", EditorStyles.miniButtonLeft ) ) _tool.RotateTexture( -90 );
					if ( GUILayout.Button( "⟳ 90", EditorStyles.miniButtonRight ) ) _tool.RotateTexture( 90 );
					GUILayout.Space( 6 );
					if ( GUILayout.Button( "½", EditorStyles.miniButtonLeft ) ) _tool.ScaleTexture( 0.5f );
					if ( GUILayout.Button( "×2", EditorStyles.miniButtonRight ) ) _tool.ScaleTexture( 2 );
				}

				GUILayout.FlexibleSpace();
				if ( GUILayout.Button( "Frame", EditorStyles.miniButton ) ) FrameFaces();
			}
			GUILayout.EndArea();
		}

		// UV <-> canvas: v points up in UV space, down on screen
		Vector2 ToCanvas( Rect r, Vector2 uv ) => r.center + new Vector2( uv.x - _pan.x, -(uv.y - _pan.y) ) * _zoom;
		Vector2 ToUV( Rect r, Vector2 p ) => _pan + new Vector2( p.x - r.center.x, -(p.y - r.center.y) ) / _zoom;

		void DrawCanvas( Rect r, List<MeshFace> faces )
		{
			if ( Event.current.type != EventType.Repaint ) return;

			EditorGUI.DrawRect( r, new Color( 0.05f, 0.05f, 0.05f ) );

			// The texture, tiled under the whole canvas, dimmed outside the 0-1 tile
			var tex = faces.Count > 0 ? FaceTexture( faces[0] ) : null;
			var min = ToUV( r, new Vector2( r.xMin, r.yMax ) );
			var max = ToUV( r, new Vector2( r.xMax, r.yMin ) );
			if ( tex != null )
			{
				var old = GUI.color;
				GUI.color = new Color( 1, 1, 1, 0.45f );
				GUI.DrawTextureWithTexCoords( r, tex, new Rect( min.x, min.y, max.x - min.x, max.y - min.y ) );
				GUI.color = old;

				var tile = Rect.MinMaxRect( ToCanvas( r, new Vector2( 0, 1 ) ).x, ToCanvas( r, new Vector2( 0, 1 ) ).y, ToCanvas( r, new Vector2( 1, 0 ) ).x, ToCanvas( r, new Vector2( 1, 0 ) ).y );
				GUI.DrawTexture( tile, tex, ScaleMode.StretchToFill );
			}

			// Tile lines
			Handles.color = new Color( 1, 1, 1, 0.25f );
			for ( var u = Mathf.Floor( min.x ); u <= max.x; u++ )
				Handles.DrawLine( ToCanvas( r, new Vector2( u, min.y ) ), ToCanvas( r, new Vector2( u, max.y ) ) );
			for ( var v = Mathf.Floor( min.y ); v <= max.y; v++ )
				Handles.DrawLine( ToCanvas( r, new Vector2( min.x, v ) ), ToCanvas( r, new Vector2( max.x, v ) ) );

			// Each selected face's UVs
			foreach ( var f in faces )
			{
				var uvs = f.Component.Mesh.GetFaceTextureCoords( f.Handle );
				if ( uvs == null || uvs.Length < 2 ) continue;

				var points = uvs.Select( uv => (Vector3)ToCanvas( r, new Vector2( uv.x, uv.y ) ) ).ToList();
				points.Add( points[0] );

				Handles.color = new Color( 1.0f, 0.92f, 0.15f, 0.12f );
				Handles.DrawAAConvexPolygon( points.Take( points.Count - 1 ).ToArray() );
				Handles.color = new Color( 1.0f, 0.92f, 0.15f, 1 );
				Handles.DrawAAPolyLine( 2, points.ToArray() );
			}
		}

		void CanvasInput( Rect r, List<MeshFace> faces )
		{
			var e = Event.current;
			if ( !r.Contains( e.mousePosition ) && !_dragging && !_panning ) return;

			switch ( e.type )
			{
				case EventType.ScrollWheel:
				{
					var before = ToUV( r, e.mousePosition );
					_zoom = Mathf.Clamp( _zoom * (e.delta.y > 0 ? 1 / 1.15f : 1.15f), 20, 5000 );
					_pan += before - ToUV( r, e.mousePosition );
					e.Use();
					Repaint();
					break;
				}

				case EventType.MouseDown when e.button == 2 || (e.button == 0 && e.alt):
					_panning = true;
					e.Use();
					break;

				case EventType.MouseDown when e.button == 0 && faces.Count > 0:
					_dragging = true;
					_dragStart = ToUV( r, e.mousePosition );
					_applied = Vector2.zero;
					Undo.IncrementCurrentGroup();
					_undoGroup = Undo.GetCurrentGroup();
					e.Use();
					break;

				case EventType.MouseDrag when _panning:
					_pan -= new Vector2( e.delta.x, -e.delta.y ) / _zoom;
					e.Use();
					Repaint();
					break;

				case EventType.MouseDrag when _dragging:
				{
					var delta = ToUV( r, e.mousePosition ) - _dragStart;

					// UV movement -> texture offset in texels; snap to grid steps
					var size = TextureSize( faces[0] );
					var texels = new Vector2( delta.x * size.x, delta.y * size.y );
					var snap = HammerSettings.GridSnap ^ (e.control || e.command);
					if ( snap )
					{
						var g = HammerSettings.GridSize;
						texels = new Vector2( Mathf.Round( texels.x / g ) * g, Mathf.Round( texels.y / g ) * g );
					}

					var step = texels - _applied;
					if ( step.sqrMagnitude > 1e-6f )
					{
						_tool.MoveTextureUV( step );
						_applied = texels;
					}

					e.Use();
					Repaint();
					break;
				}

				case EventType.MouseUp when _dragging || _panning:
					if ( _dragging )
					{
						Undo.SetCurrentGroupName( "Move Texture" );
						Undo.CollapseUndoOperations( _undoGroup );
					}
					_dragging = false;
					_panning = false;
					e.Use();
					break;
			}
		}

		static Vector2 TextureSize( MeshFace face )
		{
			var size = face.Material?.TextureSize ?? new Sandbox.Vector2( 512, 512 );
			return new Vector2( Mathf.Max( 1, size.x ), Mathf.Max( 1, size.y ) );
		}

		void FrameFaces()
		{
			var faces = Faces;
			var uvs = faces.SelectMany( f => f.Component.Mesh.GetFaceTextureCoords( f.Handle ) ?? new Sandbox.Vector2[0] ).ToList();
			if ( uvs.Count == 0 ) { _pan = new Vector2( 0.5f, 0.5f ); _zoom = 220; return; }

			var min = new Vector2( uvs.Min( x => x.x ), uvs.Min( x => x.y ) );
			var max = new Vector2( uvs.Max( x => x.x ), uvs.Max( x => x.y ) );
			_pan = (min + max) * 0.5f;
			var extent = Mathf.Max( max.x - min.x, max.y - min.y, 0.25f );
			_zoom = Mathf.Clamp( Mathf.Min( position.width, position.height - ToolbarHeight - 22 ) * 0.8f / extent, 20, 5000 );
			Repaint();
		}
	}
}
