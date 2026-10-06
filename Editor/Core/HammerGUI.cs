using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// GUI &lt;-&gt; world conversions for the view being drawn. Unity's HandleUtility versions only
	/// line up inside its own scene views (in a custom window they disagree with each other by
	/// the tab height), so the Hammer window's viewports work them out from their camera directly.
	/// </summary>
	public static class HammerGUI
	{
		static HammerView View => HammerViews.Current;

		static bool UseUnity => View == null || View.Window is SceneView;

		public static Camera Camera => UseUnity ? Camera.current : View.Camera;

		public static Ray GUIToRay( Vector2 guiPoint )
		{
			if ( UseUnity )
				return HandleUtility.GUIPointToWorldRay( guiPoint );

			var r = View.Rect;
			var vp = new Vector3( (guiPoint.x - r.x) / r.width, 1 - (guiPoint.y - r.y) / r.height, 0 );
			return View.Camera.ViewportPointToRay( vp );
		}

		/// <summary>
		/// World point to GUI position, with the distance in front of the camera in z (negative if behind).
		/// </summary>
		public static Vector3 WorldToGUI( Vector3 world )
		{
			if ( UseUnity )
				return HandleUtility.WorldToGUIPointWithDepth( world );

			var r = View.Rect;
			var vp = View.Camera.WorldToViewportPoint( world );
			return new Vector3( r.x + vp.x * r.width, r.y + (1 - vp.y) * r.height, vp.z );
		}

		public static Vector2 WorldToGUI2( Vector3 world ) => WorldToGUI( world );

		/// <summary>
		/// <see cref="WorldToGUI"/> for many points: the camera's matrices read once rather than
		/// once per point.
		/// </summary>
		public readonly struct Projector
		{
			readonly bool _unity;
			readonly Matrix4x4 _viewProjection, _worldToCamera;
			readonly Rect _rect;

			Projector( bool unity, Matrix4x4 viewProjection, Matrix4x4 worldToCamera, Rect rect )
			{
				_unity = unity;
				_viewProjection = viewProjection;
				_worldToCamera = worldToCamera;
				_rect = rect;
			}

			public static Projector Current()
			{
				if ( UseUnity ) return new Projector( true, default, default, default );
				var camera = View.Camera;
				return new Projector( false, camera.projectionMatrix * camera.worldToCameraMatrix, camera.worldToCameraMatrix, View.Rect );
			}

			public Vector3 Project( Vector3 world )
			{
				if ( _unity ) return WorldToGUI( world );
				var clip = _viewProjection * new Vector4( world.x, world.y, world.z, 1 );
				var w = Mathf.Abs( clip.w ) > 1e-12f ? clip.w : 1e-12f;
				var x = (clip.x / w + 1) * 0.5f;
				var y = (clip.y / w + 1) * 0.5f;
				var depth = -_worldToCamera.MultiplyPoint3x4( world ).z;
				return new Vector3( _rect.x + x * _rect.width, _rect.y + (1 - y) * _rect.height, depth );
			}
		}

		/// <summary>
		/// World size that appears about 80 pixels tall at this point, like HandleUtility.GetHandleSize.
		/// </summary>
		public static float HandleSize( Vector3 world )
		{
			if ( UseUnity )
				return HandleUtility.GetHandleSize( world );

			var camera = View.Camera;
			var pixels = 80.0f / Mathf.Max( View.Rect.height, 1 );

			if ( camera.orthographic )
				return camera.orthographicSize * 2 * pixels;

			var depth = Vector3.Dot( world - camera.transform.position, camera.transform.forward );
			return Mathf.Max( depth, 0.01f ) * Mathf.Tan( camera.fieldOfView * 0.5f * Mathf.Deg2Rad ) * 2 * pixels;
		}

		/// <summary>
		/// Text at a world position (Repaint only).
		/// </summary>
		public static void Label( Vector3 world, string text, GUIStyle style = null )
		{
			if ( Event.current.type != EventType.Repaint )
				return;

			var p = WorldToGUI( world );
			if ( p.z < 0 ) return;

			style ??= EditorStyles.whiteBoldLabel;
			var content = new GUIContent( text );
			var size = style.CalcSize( content );

			// Only inside the view being drawn: with several views, one view's labels would
			// otherwise land on top of its neighbours
			var view = HammerViews.Current;
			if ( view != null && view.Rect.width > 0 && !view.Rect.Contains( new Vector2( p.x, p.y ) ) )
				return;

			Handles.BeginGUI();
			GUI.Label( new Rect( p.x, p.y - size.y, size.x, size.y ), content, style );
			Handles.EndGUI();
		}

		static GUIStyle _outlined;

		/// <summary>
		/// Hammer's measurement text: small, in a colour, with a black outline so it reads on any
		/// background.
		/// </summary>
		/// <param name="besidePixels">When above zero, the label starts this far to the right of the
		/// point instead of being centred on it (beside a handle drawn there).</param>
		public static void OutlinedLabel( Vector3 world, string text, Color color, float besidePixels = 0 )
		{
			if ( Event.current.type != EventType.Repaint )
				return;

			var p = WorldToGUI( world );
			if ( p.z < 0 ) return;

			var view = HammerViews.Current;
			if ( view != null && view.Rect.width > 0 && !view.Rect.Contains( new Vector2( p.x, p.y ) ) )
				return;

			_outlined ??= new GUIStyle( EditorStyles.boldLabel ) { fontSize = 11, alignment = TextAnchor.MiddleCenter, padding = new RectOffset( 0, 0, 0, 0 ) };
			var content = new GUIContent( text );
			var size = _outlined.CalcSize( content );
			var rect = new Rect( besidePixels > 0 ? p.x + besidePixels : p.x - size.x * 0.5f, p.y - size.y * 0.5f, size.x, size.y );

			// Drawn straight from the style with no hover state: as a label under the mouse, Unity
			// would switch to the hover colour and the black outline would turn white
			Handles.BeginGUI();
			_outlined.normal.textColor = Color.black;
			for ( int dx = -1; dx <= 1; dx++ )
				for ( int dy = -1; dy <= 1; dy++ )
					if ( dx != 0 || dy != 0 )
						_outlined.Draw( new Rect( rect.x + dx, rect.y + dy, rect.width, rect.height ), content, false, false, false, false );
			_outlined.normal.textColor = color;
			_outlined.Draw( rect, content, false, false, false, false );
			Handles.EndGUI();
		}

		/// <summary>
		/// Distance in pixels from a GUI point to a world space segment.
		/// </summary>
		public static float DistanceToSegment( Vector2 guiPoint, Vector3 a, Vector3 b )
		{
			var ga = WorldToGUI( a );
			var gb = WorldToGUI( b );
			if ( ga.z < 0 && gb.z < 0 ) return float.MaxValue;
			return DistancePointSegment( guiPoint, ga, gb );
		}

		public static float DistancePointSegment( Vector2 p, Vector2 a, Vector2 b )
		{
			var ab = b - a;
			var len = ab.sqrMagnitude;
			var t = len > 1e-6f ? Mathf.Clamp01( Vector2.Dot( p - a, ab ) / len ) : 0;
			return Vector2.Distance( p, a + ab * t );
		}
	}
}
