using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Icons, colours and buttons for the Hammer window. Icons are rasterised here (at 2x, so they
	/// stay crisp on high-DPI screens) so the package doesn't need image assets.
	/// </summary>
	static class HammerIcons
	{
		// Icons are designed on a 32 unit grid and rendered at Size pixels
		const int Size = 64;
		const float Scale = Size / 32.0f;

		static Texture2D _select, _move, _rotate, _scale, _vertex, _edge, _face, _object, _shape, _paint, _clip, _knife, _mirror;
		static GUIStyle _modeLabel, _barLabel, _barValue;

		// ── Palette ──

		public static readonly Color Background = new( 0.085f, 0.085f, 0.085f );
		public static readonly Color Bar = new( 0.11f, 0.11f, 0.11f );
		public static readonly Color ButtonHover = new( 0.2f, 0.2f, 0.2f );
		public static readonly Color ButtonOn = new( 0.24f, 0.24f, 0.24f );
		public static readonly Color Divider = new( 0.03f, 0.03f, 0.03f );
		public static readonly Color Accent = new( 0.3f, 0.56f, 0.95f );
		public static readonly Color Panel = new( 0.105f, 0.105f, 0.105f );
		public static readonly Color Button = new( 0.165f, 0.165f, 0.165f );
		public static readonly Color ActiveView = new( 0.85f, 0.15f, 0.12f );

		// Grid colours, after Hammer: grey lines, orange every 8th, teal axes
		// (matched to Source 2 Hammer: light grey lines, dim orange every 8th, teal axes)
		public static readonly Color GridMinor = new( 0.75f, 0.75f, 0.75f, 0.42f );
		public static readonly Color GridMajor = new( 0.72f, 0.36f, 0.1f, 0.5f );
		public static readonly Color GridAxis = new( 0.0f, 0.6f, 0.62f, 0.9f );

		static readonly Color Light = new( 0.9f, 0.9f, 0.9f );
		static readonly Color Dim = new( 0.55f, 0.55f, 0.55f );
		static readonly Color CubeTop = new( 0.78f, 0.78f, 0.78f );
		static readonly Color CubeLeft = new( 0.58f, 0.58f, 0.58f );
		static readonly Color CubeRight = new( 0.44f, 0.44f, 0.44f );

		// ── Icons ──

		public static Texture Select => _select ??= Draw( c =>
		{
			Polygon( c, Light, new( 9, 4 ), new( 9, 25 ), new( 14, 20.5f ), new( 18, 28.5f ), new( 21.5f, 27 ), new( 17.5f, 19 ), new( 24, 18.5f ) );
		} );

		public static Texture Move => _move ??= Draw( c =>
		{
			Line( c, new( 16, 6 ), new( 16, 26 ), 2.2f, Light );
			Line( c, new( 6, 16 ), new( 26, 16 ), 2.2f, Light );
			Polygon( c, Light, new( 16, 2 ), new( 20.5f, 7.5f ), new( 11.5f, 7.5f ) );
			Polygon( c, Light, new( 16, 30 ), new( 11.5f, 24.5f ), new( 20.5f, 24.5f ) );
			Polygon( c, Light, new( 2, 16 ), new( 7.5f, 11.5f ), new( 7.5f, 20.5f ) );
			Polygon( c, Light, new( 30, 16 ), new( 24.5f, 20.5f ), new( 24.5f, 11.5f ) );
		} );

		public static Texture Rotate => _rotate ??= Draw( c =>
		{
			Arc( c, new( 16, 17 ), 10, 40, 330, 2.6f, Light );
			// Arrow head at the start of the arc
			var tip = new Vector2( 16 + 10 * Mathf.Cos( 40 * Mathf.Deg2Rad ), 17 - 10 * Mathf.Sin( 40 * Mathf.Deg2Rad ) );
			Polygon( c, Light, tip + new Vector2( -6, -3 ), tip + new Vector2( 3, -6 ), tip + new Vector2( 2.5f, 3.5f ) );
		} );

		public static Texture ScaleIcon => _scale ??= Draw( c =>
		{
			Rect( c, new( 4, 4 ), new( 28, 28 ), 2, Light );
			Polygon( c, Dim, new( 4, 18 ), new( 14, 18 ), new( 14, 28 ), new( 4, 28 ) );
			Line( c, new( 13, 19 ), new( 22, 10 ), 2.2f, Light );
			Polygon( c, Light, new( 24.5f, 7.5f ), new( 24.5f, 16 ), new( 16, 7.5f ) );
		} );

		public static Texture Vertex => _vertex ??= Draw( c =>
		{
			Cube( c, 0.35f );
			CubeEdges( c, Light, 1.2f );
			foreach ( var p in CubePoints ) Dot( c, p, 2.6f, Accent );
		} );

		public static Texture Edge => _edge ??= Draw( c =>
		{
			Cube( c, 0.8f );
			CubeEdges( c, new Color( 0.3f, 0.3f, 0.3f ), 1 );
			Line( c, CubeCenter, CubeBottom, 3, Accent );
			Line( c, CubeTopLeft, CubeTopPoint, 3, Accent );
		} );

		public static Texture Face => _face ??= Draw( c =>
		{
			Cube( c, 0.8f );
			Polygon( c, Accent, CubeCenter, CubeTopRight, CubeBottomRight, CubeBottom );
			CubeEdges( c, new Color( 0.3f, 0.3f, 0.3f ), 1 );
		} );

		public static Texture Object => _object ??= Draw( c =>
		{
			Cube( c, 1 );
			CubeOutline( c, Accent, 1.8f );
		} );

		public static Texture Shape => _shape ??= Draw( c =>
		{
			Cube( c, 0.9f, 0.82f, new Vector2( -2, 3 ) );
			Line( c, new( 25, 3 ), new( 25, 13 ), 2.6f, Accent );
			Line( c, new( 20, 8 ), new( 30, 8 ), 2.6f, Accent );
		} );

		public static Texture Paint => _paint ??= Draw( c =>
		{
			Line( c, new( 27, 5 ), new( 15, 17 ), 3.2f, Light );
			Polygon( c, Accent, new( 13, 15 ), new( 17, 19 ), new( 11, 28 ), new( 4, 28 ), new( 6, 21 ) );
		} );

		public static Texture Clip => _clip ??= Draw( c =>
		{
			Cube( c, 0.6f );
			Line( c, new( 3, 27 ), new( 29, 5 ), 2.4f, Accent );
		} );

		public static Texture Knife => _knife ??= Draw( c =>
		{
			Polygon( c, Light, new( 4, 24 ), new( 22, 6 ), new( 26, 10 ), new( 10, 26 ) );
			Line( c, new( 22, 6 ), new( 28, 2 ), 3, Dim );
			Line( c, new( 3, 30 ), new( 17, 30 ), 1.6f, Accent );
		} );

		public static Texture Mirror => _mirror ??= Draw( c =>
		{
			Line( c, new( 16, 3 ), new( 16, 29 ), 1.5f, Dim );
			Polygon( c, Light, new( 13, 8 ), new( 13, 24 ), new( 4, 24 ) );
			Polygon( c, Accent, new( 19, 8 ), new( 19, 24 ), new( 28, 24 ) );
		} );

		static Texture2D _global, _local, _texLock, _xray, _gridSnap;

		public static Texture Global => _global ??= Draw( c =>
		{
			Dot( c, new( 16, 16 ), 12, new Color( 0.3f, 0.55f, 0.85f ) );
			Arc( c, new( 16, 16 ), 12, 0, 360, 1.6f, Light );
			Line( c, new( 4, 16 ), new( 28, 16 ), 1.4f, Light );
			Line( c, new( 7, 10 ), new( 25, 10 ), 1.2f, Light );
			Line( c, new( 7, 22 ), new( 25, 22 ), 1.2f, Light );
			Line( c, new( 16, 4 ), new( 16, 28 ), 1.4f, Light );
		} );

		public static Texture Local => _local ??= Draw( c =>
		{
			Line( c, new( 8, 25 ), new( 8, 6 ), 2.2f, new Color( 0.45f, 0.85f, 0.35f ) );
			Polygon( c, new Color( 0.45f, 0.85f, 0.35f ), new( 8, 2 ), new( 12, 8 ), new( 4, 8 ) );
			Line( c, new( 7, 25 ), new( 26, 25 ), 2.2f, new Color( 0.95f, 0.35f, 0.3f ) );
			Polygon( c, new Color( 0.95f, 0.35f, 0.3f ), new( 30, 25 ), new( 24, 29 ), new( 24, 21 ) );
		} );

		public static Texture TexLock => _texLock ??= Draw( c =>
		{
			for ( int y = 0; y < 3; y++ )
				for ( int x = 0; x < 3; x++ )
					Polygon( c, (x + y) % 2 == 0 ? Accent : new Color( 0.35f, 0.35f, 0.35f ), new( 3 + x * 7, 3 + y * 7 ), new( 10 + x * 7, 3 + y * 7 ), new( 10 + x * 7, 10 + y * 7 ), new( 3 + x * 7, 10 + y * 7 ) );
			Arc( c, new( 23, 19 ), 3.5f, 0, 180, 1.8f, Light );
			Polygon( c, Light, new( 18, 19 ), new( 28, 19 ), new( 28, 29 ), new( 18, 29 ) );
		} );

		public static Texture XRay => _xray ??= Draw( c =>
		{
			Cube( c, 0.25f );
			CubeEdges( c, Light, 1.1f );
			Arc( c, new( 16, 16 ), 13.5f, 0, 360, 1.4f, new Color( 0.4f, 0.75f, 1.0f ) );
		} );

		/// <summary>
		/// Little picture of a viewport layout: rects in 0..1.
		/// </summary>
		public static void DrawLayoutIcon( Rect r, params Rect[] panes )
		{
			if ( Event.current.type != EventType.Repaint ) return;
			var c = new Color( 0.7f, 0.7f, 0.7f );
			foreach ( var p in panes )
			{
				var q = new Rect( r.x + p.x * r.width, r.y + p.y * r.height, p.width * r.width, p.height * r.height );
				EditorGUI.DrawRect( new Rect( q.x, q.y, q.width, 1 ), c );
				EditorGUI.DrawRect( new Rect( q.x, q.yMax - 1, q.width, 1 ), c );
				EditorGUI.DrawRect( new Rect( q.x, q.y, 1, q.height ), c );
				EditorGUI.DrawRect( new Rect( q.xMax - 1, q.y, 1, q.height ), c );
			}
		}

		static Texture2D _pivot, _block, _polygon, _texScale, _showGrid, _wires;

		public static Texture Pivot => _pivot ??= Draw( c =>
		{
			Arc( c, new( 16, 16 ), 11, 0, 360, 2.2f, Light );
			Line( c, new( 16, 9 ), new( 16, 23 ), 2.2f, Accent );
			Line( c, new( 9, 16 ), new( 23, 16 ), 2.2f, Accent );
		} );

		public static Texture Block => _block ??= Draw( c =>
		{
			Cube( c, 1 );
			CubeEdges( c, new Color( 0.25f, 0.25f, 0.25f ), 1 );
		} );

		public static Texture PolygonIcon => _polygon ??= Draw( c =>
		{
			Vector2[] p = { new( 6, 12 ), new( 16, 4 ), new( 27, 10 ), new( 25, 25 ), new( 9, 27 ) };
			for ( int k = 0; k < p.Length; k++ ) Line( c, p[k], p[(k + 1) % p.Length], 2.2f, Accent );
			foreach ( var q in p ) Dot( c, q, 2.4f, Light );
		} );

		public static Texture TexScaleLock => _texScale ??= Draw( c =>
		{
			Polygon( c, new Color( 0.35f, 0.35f, 0.35f ), new( 3, 3 ), new( 21, 3 ), new( 21, 21 ), new( 3, 21 ) );
			Polygon( c, Accent, new( 3, 3 ), new( 12, 3 ), new( 12, 12 ), new( 3, 12 ) );
			Polygon( c, Accent, new( 12, 12 ), new( 21, 12 ), new( 21, 21 ), new( 12, 21 ) );
			Line( c, new( 5, 19 ), new( 13, 11 ), 1.6f, Light );
			Arc( c, new( 23, 19 ), 3.5f, 0, 180, 1.8f, Light );
			Polygon( c, Light, new( 18, 19 ), new( 28, 19 ), new( 28, 29 ), new( 18, 29 ) );
		} );

		public static Texture ShowGridIcon => _showGrid ??= Draw( c =>
		{
			for ( int k = 0; k < 4; k++ )
			{
				Line( c, new( 4 + k * 8, 3 ), new( 4 + k * 8, 29 ), 1.4f, k == 2 ? Accent : Light );
				Line( c, new( 3, 4 + k * 8 ), new( 29, 4 + k * 8 ), 1.4f, k == 2 ? Accent : Light );
			}
		} );

		public static Texture Wires => _wires ??= Draw( c =>
		{
			Cube( c, 0.55f );
			CubeEdges( c, new Color( 0.45f, 0.78f, 1.0f ), 1.6f );
		} );

		static Texture2D _magnet, _vertexSnap, _surfaceSnap;

		// Snap icons: bold white shapes that read at status bar size; on/off is shown by the
		// button behind them (see SnapButton)

		public static Texture Magnet => _magnet ??= Draw( c =>
		{
			// A horseshoe magnet with bright tips
			Arc( c, new( 16, 15 ), 9.5f, 0, 180, 6.5f, Light );
			Line( c, new( 6.5f, 15 ), new( 6.5f, 22 ), 6.5f, Light );
			Line( c, new( 25.5f, 15 ), new( 25.5f, 22 ), 6.5f, Light );
			Line( c, new( 6.5f, 24 ), new( 6.5f, 29 ), 6.5f, new Color( 1.0f, 0.42f, 0.35f ) );
			Line( c, new( 25.5f, 24 ), new( 25.5f, 29 ), 6.5f, new Color( 1.0f, 0.42f, 0.35f ) );
		} );

		public static Texture GridSnap => _gridSnap ??= Draw( c =>
		{
			// A # grid with a dot locked onto a crossing
			for ( int i = 0; i < 2; i++ )
			{
				Line( c, new( 11 + i * 10, 3 ), new( 11 + i * 10, 29 ), 2.6f, Light );
				Line( c, new( 3, 11 + i * 10 ), new( 29, 11 + i * 10 ), 2.6f, Light );
			}
			Dot( c, new( 21, 11 ), 5, Light );
		} );

		public static Texture VertexSnap => _vertexSnap ??= Draw( c =>
		{
			// The corner of a box with a big dot on the vertex
			Line( c, new( 4, 10 ), new( 22, 10 ), 2.8f, Light );
			Line( c, new( 22, 10 ), new( 22, 28 ), 2.8f, Light );
			Line( c, new( 4, 10 ), new( 4, 28 ), 1.6f, Dim );
			Line( c, new( 4, 28 ), new( 22, 28 ), 1.6f, Dim );
			Dot( c, new( 22, 10 ), 6, Light );
		} );

		public static Texture SurfaceSnap => _surfaceSnap ??= Draw( c =>
		{
			// A box standing on a ground slab
			Polygon( c, Light, new( 2, 23 ), new( 30, 23 ), new( 30, 29 ), new( 2, 29 ) );
			Polygon( c, Light, new( 9, 6 ), new( 23, 6 ), new( 23, 20 ), new( 9, 20 ) );
		} );

		/// <summary>
		/// A status bar toggle: lit blue when on, the icon dimmed when off, faded right down when
		/// <paramref name="enabled"/> is false (snapping switched off as a whole).
		/// </summary>
		public static bool SnapButton( Rect rect, Texture icon, string tip, bool on, bool enabled = true )
		{
			var e = Event.current;
			var hover = rect.Contains( e.mousePosition );

			if ( e.type == EventType.Repaint )
			{
				var back = on ? new Color( Accent.r, Accent.g, Accent.b, enabled ? 0.55f : 0.2f ) : hover ? ButtonHover : Button;
				EditorGUI.DrawRect( rect, back );
				if ( hover ) EditorGUI.DrawRect( new Rect( rect.x, rect.yMax - 2, rect.width, 2 ), Accent );

				var old = GUI.color;
				GUI.color = new Color( 1, 1, 1, !enabled ? 0.3f : on ? 1.0f : 0.5f );
				var size = Mathf.Min( rect.width, rect.height ) - 4;
				GUI.DrawTexture( new Rect( rect.center.x - size / 2, rect.center.y - size / 2, size, size ), icon, ScaleMode.ScaleToFit );
				GUI.color = old;
			}

			GUI.Label( rect, new GUIContent( "", tip ), GUIStyle.none );
			return Click( rect );
		}

		// ── Buttons ──

		static GUIStyle ModeLabel => _modeLabel ??= new GUIStyle( EditorStyles.label ) { fontSize = 13, alignment = TextAnchor.MiddleLeft };

		public static GUIStyle BarLabel => _barLabel ??= new GUIStyle( EditorStyles.label ) { alignment = TextAnchor.MiddleRight, fontSize = 12, fixedHeight = 26 };
		public static GUIStyle BarValue => _barValue ??= new GUIStyle( EditorStyles.boldLabel ) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fixedHeight = 26 };

		/// <summary>
		/// Flat icon + label button for the mode bar, with an accent underline when on.
		/// </summary>
		public static bool ModeButton( Rect rect, Texture icon, string label, string tip, bool on )
		{
			var e = Event.current;
			var hover = rect.Contains( e.mousePosition );

			if ( e.type == EventType.Repaint )
			{
				if ( on || hover ) EditorGUI.DrawRect( rect, on ? ButtonOn : ButtonHover );
				if ( on ) EditorGUI.DrawRect( new Rect( rect.x, rect.yMax - 3, rect.width, 3 ), Accent );

				var iconSize = rect.height - 12;
				GUI.DrawTexture( new Rect( rect.x + 8, rect.y + (rect.height - iconSize) * 0.5f - 1, iconSize, iconSize ), icon, ScaleMode.ScaleToFit );

				var style = ModeLabel;
				style.normal.textColor = on ? Color.white : new Color( 0.78f, 0.78f, 0.78f );
				style.Draw( new Rect( rect.x + iconSize + 14, rect.y - 1, rect.width - iconSize - 14, rect.height ), label, false, false, false, false );
			}

			GUI.Label( rect, new GUIContent( "", tip ), GUIStyle.none );
			return Click( rect );
		}

		public static float ModeButtonWidth( string label, float height ) => height - 12 + 24 + ModeLabel.CalcSize( new GUIContent( label ) ).x;

		/// <summary>
		/// Flat square icon button for the tool strip, with an accent bar on the left when on.
		/// </summary>
		public static bool StripButton( Rect rect, Texture icon, string tip, bool on, bool enabled = true )
		{
			var e = Event.current;
			var hover = enabled && rect.Contains( e.mousePosition );

			if ( e.type == EventType.Repaint )
			{
				if ( on || hover ) EditorGUI.DrawRect( rect, on ? ButtonOn : ButtonHover );
				if ( on ) EditorGUI.DrawRect( new Rect( rect.x, rect.y, 3, rect.height ), Accent );

				var pad = rect.width * 0.2f;
				var old = GUI.color;
				if ( !enabled ) GUI.color = new Color( 1, 1, 1, 0.3f );
				GUI.DrawTexture( new Rect( rect.x + pad, rect.y + pad, rect.width - pad * 2, rect.height - pad * 2 ), icon, ScaleMode.ScaleToFit );
				GUI.color = old;
			}

			GUI.Label( rect, new GUIContent( "", tip ), GUIStyle.none );
			return enabled && Click( rect );
		}

		static bool Click( Rect rect )
		{
			var e = Event.current;
			if ( e.type == EventType.MouseDown && e.button == 0 && rect.Contains( e.mousePosition ) )
			{
				e.Use();
				return true;
			}

			return false;
		}

		// ── Cube used by the mode icons (isometric, on the 32 grid) ──

		static readonly Vector2 CubeTopPoint = new( 16, 3 );
		static readonly Vector2 CubeTopRight = new( 28, 9.5f );
		static readonly Vector2 CubeCenter = new( 16, 16 );
		static readonly Vector2 CubeTopLeft = new( 4, 9.5f );
		static readonly Vector2 CubeBottomLeft = new( 4, 22.5f );
		static readonly Vector2 CubeBottom = new( 16, 29 );
		static readonly Vector2 CubeBottomRight = new( 28, 22.5f );
		static readonly Vector2[] CubePoints = { CubeTopPoint, CubeTopRight, CubeCenter, CubeTopLeft, CubeBottomLeft, CubeBottom, CubeBottomRight };

		static void Cube( Color[] c, float alpha, float size = 1, Vector2 offset = default )
		{
			Vector2 P( Vector2 p ) => (p - new Vector2( 16, 16 )) * size + new Vector2( 16, 16 ) + offset;
			Color A( Color col ) => new( col.r, col.g, col.b, alpha );

			Polygon( c, A( CubeTop ), P( CubeTopPoint ), P( CubeTopRight ), P( CubeCenter ), P( CubeTopLeft ) );
			Polygon( c, A( CubeLeft ), P( CubeTopLeft ), P( CubeCenter ), P( CubeBottom ), P( CubeBottomLeft ) );
			Polygon( c, A( CubeRight ), P( CubeCenter ), P( CubeTopRight ), P( CubeBottomRight ), P( CubeBottom ) );
		}

		static void CubeOutline( Color[] c, Color color, float width )
		{
			Vector2[] o = { CubeTopPoint, CubeTopRight, CubeBottomRight, CubeBottom, CubeBottomLeft, CubeTopLeft };
			for ( int i = 0; i < o.Length; i++ ) Line( c, o[i], o[(i + 1) % o.Length], width, color );
		}

		static void CubeEdges( Color[] c, Color color, float width )
		{
			CubeOutline( c, color, width );
			Line( c, CubeTopLeft, CubeCenter, width, color );
			Line( c, CubeTopRight, CubeCenter, width, color );
			Line( c, CubeCenter, CubeBottom, width, color );
		}

		// ── Tiny rasteriser (coordinates on the 32 grid, top-down) ──

		static Texture2D Draw( System.Action<Color[]> paint )
		{
			var pixels = new Color[Size * Size];
			paint( pixels );

			var tex = new Texture2D( Size, Size, TextureFormat.RGBA32, true ) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Trilinear };
			tex.SetPixels( pixels );
			tex.Apply( true );
			return tex;
		}

		static void Blend( Color[] c, int x, int y, Color color )
		{
			if ( x < 0 || y < 0 || x >= Size || y >= Size || color.a <= 0 ) return;
			// Texture rows go bottom-up; our coordinates are top-down
			var i = (Size - 1 - y) * Size + x;
			var dst = c[i];
			var a = color.a + dst.a * (1 - color.a);
			c[i] = a > 0 ? new Color( (color.r * color.a + dst.r * dst.a * (1 - color.a)) / a, (color.g * color.a + dst.g * dst.a * (1 - color.a)) / a, (color.b * color.a + dst.b * dst.a * (1 - color.a)) / a, a ) : dst;
		}

		/// <summary>
		/// Pixel centre in grid units.
		/// </summary>
		static Vector2 P( int x, int y ) => new( (x + 0.5f) / Scale, (y + 0.5f) / Scale );

		static void Line( Color[] c, Vector2 a, Vector2 b, float width, Color color )
		{
			var ab = b - a;
			for ( int y = 0; y < Size; y++ )
			{
				for ( int x = 0; x < Size; x++ )
				{
					var p = P( x, y );
					var t = Mathf.Clamp01( Vector2.Dot( p - a, ab ) / Mathf.Max( ab.sqrMagnitude, 1e-4f ) );
					var d = Vector2.Distance( p, a + ab * t ) * Scale;
					var cover = Mathf.Clamp01( width * Scale * 0.5f + 0.5f - d );
					if ( cover > 0 ) Blend( c, x, y, new Color( color.r, color.g, color.b, color.a * cover ) );
				}
			}
		}

		static void Rect( Color[] c, Vector2 min, Vector2 max, float width, Color color )
		{
			Line( c, new( min.x, min.y ), new( max.x, min.y ), width, color );
			Line( c, new( max.x, min.y ), new( max.x, max.y ), width, color );
			Line( c, new( max.x, max.y ), new( min.x, max.y ), width, color );
			Line( c, new( min.x, max.y ), new( min.x, min.y ), width, color );
		}

		static void Arc( Color[] c, Vector2 center, float radius, float fromDegrees, float toDegrees, float width, Color color )
		{
			const int steps = 48;
			Vector2 At( float deg ) => center + new Vector2( Mathf.Cos( deg * Mathf.Deg2Rad ), -Mathf.Sin( deg * Mathf.Deg2Rad ) ) * radius;
			for ( int i = 0; i < steps; i++ )
			{
				var a = Mathf.Lerp( fromDegrees, toDegrees, i / (float)steps );
				var b = Mathf.Lerp( fromDegrees, toDegrees, (i + 1) / (float)steps );
				Line( c, At( a ), At( b ), width, color );
			}
		}

		static void Dot( Color[] c, Vector2 center, float radius, Color color )
		{
			for ( int y = 0; y < Size; y++ )
			{
				for ( int x = 0; x < Size; x++ )
				{
					var d = Vector2.Distance( P( x, y ), center ) * Scale;
					var cover = Mathf.Clamp01( radius * Scale + 0.5f - d );
					if ( cover > 0 ) Blend( c, x, y, new Color( color.r, color.g, color.b, color.a * cover ) );
				}
			}
		}

		/// <summary>
		/// Filled polygon (even-odd), 4x4 supersampled for smooth edges.
		/// </summary>
		static void Polygon( Color[] c, Color color, params Vector2[] points )
		{
			for ( int y = 0; y < Size; y++ )
			{
				for ( int x = 0; x < Size; x++ )
				{
					int inside = 0;
					for ( int sy = 0; sy < 4; sy++ )
					{
						for ( int sx = 0; sx < 4; sx++ )
						{
							var p = new Vector2( (x + (sx + 0.5f) / 4) / Scale, (y + (sy + 0.5f) / 4) / Scale );
							if ( Inside( p, points ) ) inside++;
						}
					}

					if ( inside > 0 ) Blend( c, x, y, new Color( color.r, color.g, color.b, color.a * inside / 16.0f ) );
				}
			}
		}

		static bool Inside( Vector2 p, Vector2[] poly )
		{
			bool inside = false;
			for ( int i = 0, j = poly.Length - 1; i < poly.Length; j = i++ )
			{
				if ( (poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x )
					inside = !inside;
			}
			return inside;
		}
	}
}
